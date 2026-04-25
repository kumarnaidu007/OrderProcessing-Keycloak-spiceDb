using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OrderProcessing.Common;
using OrderProcessing.Controllers;
using OrderProcessing.Dtos.Orders;
using OrderProcessing.Models;
using OrderProcessing.Services;
using Xunit;

namespace OrderProcessing.Tests;

public class OrderFlowTests
{
    [Fact]
    public async Task Create_order_then_worker_processes_to_completed()
    {
        await using var db = await CreateDbAsync();
        var (user, customer, address, product) = await SeedCustomerAndProductAsync(db);

        var controller = BuildOrdersController(db, user.UserId);
        var request = new CreateOrderRequest
        {
            CustomerAddressId = address.CustomerAddressId,
            Items = [new OrderLineRequest { ProductId = product.ProductId, Quantity = 2 }]
        };

        var created = await controller.Create(request, "flow-key-1", CancellationToken.None);
        var createdResult = Assert.IsType<CreatedAtActionResult>(created.Result);
        _ = Assert.IsType<OrderResponse>(createdResult.Value);

        var worker = BuildWorker(paymentFailureRate: 0);
        await InvokePrivateAsync(worker, "TryProcessNextJobAsync", db, CancellationToken.None);

        var order = await db.Orders.AsNoTracking().SingleAsync();
        var savedProduct = await db.Products.AsNoTracking().SingleAsync();
        var payment = await db.PaymentAttempts.AsNoTracking().SingleAsync();
        var inventory = await db.InventoryLedgers.AsNoTracking().SingleAsync();
        var job = await db.OrderProcessingJobs.AsNoTracking().SingleAsync();

        Assert.Equal(OrderStatuses.Completed, order.Status);
        Assert.Equal(8, savedProduct.AvailableQuantity);
        Assert.Equal(PaymentStatuses.Succeeded, payment.Status);
        Assert.Equal(JobStatuses.Succeeded, job.JobStatus);
        Assert.Equal($"order-{order.OrderId}-deduct-{product.ProductId}", inventory.IdempotencyKey);
    }

    [Fact]
    public async Task Duplicate_idempotency_key_returns_existing_order()
    {
        await using var db = await CreateDbAsync();
        var (user, _, address, product) = await SeedCustomerAndProductAsync(db);

        var controller = BuildOrdersController(db, user.UserId);
        var request = new CreateOrderRequest
        {
            CustomerAddressId = address.CustomerAddressId,
            Items = [new OrderLineRequest { ProductId = product.ProductId, Quantity = 1 }]
        };

        var first = await controller.Create(request, "same-key", CancellationToken.None);
        _ = Assert.IsType<CreatedAtActionResult>(first.Result);

        var second = await controller.Create(request, "same-key", CancellationToken.None);
        var secondResult = Assert.IsType<OkObjectResult>(second.Result);
        _ = Assert.IsType<OrderResponse>(secondResult.Value);

        Assert.Equal(1, await db.Orders.CountAsync());
        Assert.Equal(1, await db.OrderProcessingJobs.CountAsync());
    }

    [Fact]
    public async Task Payment_failure_retries_once_then_fails_and_restores_inventory()
    {
        await using var db = await CreateDbAsync();
        var (user, _, address, product) = await SeedCustomerAndProductAsync(db);

        var controller = BuildOrdersController(db, user.UserId);
        var request = new CreateOrderRequest
        {
            CustomerAddressId = address.CustomerAddressId,
            Items = [new OrderLineRequest { ProductId = product.ProductId, Quantity = 2 }]
        };

        var created = await controller.Create(request, "flow-key-payment-fail", CancellationToken.None);
        _ = Assert.IsType<CreatedAtActionResult>(created.Result);

        var worker = BuildWorker(paymentFailureRate: 1.0, maxPaymentAttempts: 2);
        await InvokePrivateAsync(worker, "TryProcessNextJobAsync", db, CancellationToken.None);

        var firstAttemptOrder = await db.Orders.AsNoTracking().SingleAsync();
        var firstAttemptProduct = await db.Products.AsNoTracking().SingleAsync();
        var firstAttemptJob = await db.OrderProcessingJobs.SingleAsync();
        Assert.Equal(OrderStatuses.Processing, firstAttemptOrder.Status);
        Assert.Equal(8, firstAttemptProduct.AvailableQuantity);
        Assert.Equal(JobStatuses.Pending, firstAttemptJob.JobStatus);
        Assert.NotNull(firstAttemptJob.NextRetryAtUtc);

        firstAttemptJob.NextRetryAtUtc = DateTime.UtcNow.AddSeconds(-1);
        await db.SaveChangesAsync();

        await InvokePrivateAsync(worker, "TryProcessNextJobAsync", db, CancellationToken.None);

        var finalOrder = await db.Orders.AsNoTracking().SingleAsync();
        var finalProduct = await db.Products.AsNoTracking().SingleAsync();
        var ledgers = await db.InventoryLedgers.AsNoTracking().OrderBy(x => x.InventoryLedgerId).ToListAsync();
        var payment = await db.PaymentAttempts.AsNoTracking().SingleAsync();
        var finalJob = await db.OrderProcessingJobs.AsNoTracking().SingleAsync();

        Assert.Equal(OrderStatuses.Failed, finalOrder.Status);
        Assert.Equal(10, finalProduct.AvailableQuantity);
        Assert.Equal(2, ledgers.Count);
        Assert.Contains(ledgers, l => l.MovementType == InventoryMovementTypes.SaleDeduct);
        Assert.Contains(ledgers, l => l.MovementType == InventoryMovementTypes.SaleRestore);
        Assert.Equal(PaymentStatuses.Failed, payment.Status);
        Assert.Equal(2, payment.AttemptNo);
        Assert.Equal(JobStatuses.Failed, finalJob.JobStatus);
    }

    [Fact]
    public async Task Pending_order_can_be_cancelled()
    {
        await using var db = await CreateDbAsync();
        var (user, _, address, product) = await SeedCustomerAndProductAsync(db);

        var controller = BuildOrdersController(db, user.UserId);
        var request = new CreateOrderRequest
        {
            CustomerAddressId = address.CustomerAddressId,
            Items = [new OrderLineRequest { ProductId = product.ProductId, Quantity = 1 }]
        };

        var created = await controller.Create(request, "cancel-key-1", CancellationToken.None);
        var createdResult = Assert.IsType<CreatedAtActionResult>(created.Result);
        var createdOrder = Assert.IsType<OrderResponse>(createdResult.Value);

        var cancel = await controller.Cancel(createdOrder.OrderId, CancellationToken.None);
        var cancelResult = Assert.IsType<OkObjectResult>(cancel.Result);
        var status = Assert.IsType<OrderStatusResponse>(cancelResult.Value);
        Assert.Equal(OrderStatuses.Cancelled, status.Status);

        var savedOrder = await db.Orders.AsNoTracking().SingleAsync();
        var job = await db.OrderProcessingJobs.AsNoTracking().SingleAsync();
        Assert.Equal(OrderStatuses.Cancelled, savedOrder.Status);
        Assert.Equal(JobStatuses.Failed, job.JobStatus);
    }

    [Fact]
    public async Task Non_pending_order_cannot_be_cancelled()
    {
        await using var db = await CreateDbAsync();
        var (user, _, address, product) = await SeedCustomerAndProductAsync(db);

        var controller = BuildOrdersController(db, user.UserId);
        var request = new CreateOrderRequest
        {
            CustomerAddressId = address.CustomerAddressId,
            Items = [new OrderLineRequest { ProductId = product.ProductId, Quantity = 1 }]
        };

        var created = await controller.Create(request, "cancel-key-2", CancellationToken.None);
        var createdResult = Assert.IsType<CreatedAtActionResult>(created.Result);
        var createdOrder = Assert.IsType<OrderResponse>(createdResult.Value);

        var worker = BuildWorker(paymentFailureRate: 0.0);
        await InvokePrivateAsync(worker, "TryProcessNextJobAsync", db, CancellationToken.None);

        var cancel = await controller.Cancel(createdOrder.OrderId, CancellationToken.None);
        _ = Assert.IsType<ConflictObjectResult>(cancel.Result);
    }

    [Fact]
    public async Task Multi_item_order_with_one_insufficient_product_does_not_partially_deduct_inventory()
    {
        await using var db = await CreateDbAsync();
        var (user, _, address, firstProduct) = await SeedCustomerAndProductAsync(db);
        var secondProduct = new Product
        {
            Name = "Low Stock Product",
            Price = 50m,
            AvailableQuantity = 1,
            ReservedQuantity = 0,
            RowVersion = [1],
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = user.UserId,
            UpdatedByUserId = user.UserId
        };
        db.Products.Add(secondProduct);
        await db.SaveChangesAsync();

        var controller = BuildOrdersController(db, user.UserId);
        var request = new CreateOrderRequest
        {
            CustomerAddressId = address.CustomerAddressId,
            Items =
            [
                new OrderLineRequest { ProductId = firstProduct.ProductId, Quantity = 2 },
                new OrderLineRequest { ProductId = secondProduct.ProductId, Quantity = 2 }
            ]
        };

        var created = await controller.Create(request, "mixed-stock-key-1", CancellationToken.None);
        _ = Assert.IsType<CreatedAtActionResult>(created.Result);

        var worker = BuildWorker(paymentFailureRate: 0.0);
        await InvokePrivateAsync(worker, "TryProcessNextJobAsync", db, CancellationToken.None);

        var order = await db.Orders.AsNoTracking().SingleAsync();
        var products = await db.Products.AsNoTracking().OrderBy(p => p.ProductId).ToListAsync();
        var ledgers = await db.InventoryLedgers.AsNoTracking().ToListAsync();
        var job = await db.OrderProcessingJobs.AsNoTracking().SingleAsync();

        Assert.Equal(OrderStatuses.Failed, order.Status);
        Assert.Equal(10, products.Single(p => p.ProductId == firstProduct.ProductId).AvailableQuantity);
        Assert.Equal(1, products.Single(p => p.ProductId == secondProduct.ProductId).AvailableQuantity);
        Assert.Empty(ledgers);
        Assert.Equal(JobStatuses.Failed, job.JobStatus);
    }

    private static async Task<OrderProcessingContext> CreateDbAsync()
    {
        var dbName = $"OrderProcessingTests_{Guid.NewGuid():N}";
        var connectionString =
            $"Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog={dbName};Integrated Security=True;Trust Server Certificate=True";

        var options = new DbContextOptionsBuilder<OrderProcessingContext>()
            .UseSqlServer(connectionString)
            .EnableSensitiveDataLogging()
            .Options;

        var db = new OrderProcessingContext(options);
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private static async Task<(User user, Customer customer, CustomerAddress address, Product product)> SeedCustomerAndProductAsync(OrderProcessingContext db)
    {
        var now = DateTime.UtcNow;
        var user = new User
        {
            Email = "customer@test.local",
            DisplayName = "Customer One",
            Phone = "9999999999",
            IsActive = true,
            IsEmailVerified = true,
            IsPhoneVerified = false,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var customer = new Customer
        {
            UserId = user.UserId,
            DisplayName = "Customer One",
            Email = user.Email,
            Phone = user.Phone,
            ExternalReference = null!,
            IsActive = true,
            CreatedAtUtc = now
        };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var address = new CustomerAddress
        {
            CustomerId = customer.CustomerId,
            Label = "Home",
            RecipientName = "Customer One",
            Phone = "9999999999",
            AddressLine1 = "123 Main Street",
            AddressLine2 = "",
            City = "Chennai",
            StateOrRegion = "TN",
            PostalCode = "600001",
            CountryCode = "IN",
            IsDefault = true,
            CreatedAtUtc = now
        };
        db.CustomerAddresses.Add(address);

        var product = new Product
        {
            Name = "Test Product",
            Price = 100m,
            AvailableQuantity = 10,
            ReservedQuantity = 0,
            RowVersion = [1],
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CreatedByUserId = user.UserId,
            UpdatedByUserId = user.UserId
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();

        return (user, customer, address, product);
    }

    private static OrdersController BuildOrdersController(OrderProcessingContext db, long userId)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(AppClaims.Permission, PermissionCodes.OrdersCreate),
            new Claim(AppClaims.Permission, PermissionCodes.OrdersReadOwn)
        };

        var controller = new OrdersController(db, NullLogger<OrdersController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(
                        new ClaimsIdentity(
                            claims,
                            "TestAuth"))
                }
            }
        };
        return controller;
    }

    private static OrderProcessingWorker BuildWorker(double paymentFailureRate, int maxPaymentAttempts = 2)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OrderProcessing:PaymentFailureRate"] = paymentFailureRate.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["OrderProcessing:MaxPaymentAttempts"] = maxPaymentAttempts.ToString(System.Globalization.CultureInfo.InvariantCulture)
            })
            .Build();

        var provider = new ServiceCollection().BuildServiceProvider();
        return new OrderProcessingWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<OrderProcessingWorker>.Instance,
            config);
    }

    private static async Task InvokePrivateAsync(object instance, string methodName, params object[] args)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var task = method!.Invoke(instance, args) as Task;
        Assert.NotNull(task);
        await task!;
    }
}
