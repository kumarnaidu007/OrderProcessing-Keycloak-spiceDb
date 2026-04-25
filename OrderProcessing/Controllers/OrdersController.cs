using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderProcessing.Common;
using OrderProcessing.Dtos.Orders;
using OrderProcessing.Models;

namespace OrderProcessing.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly OrderProcessingContext _db;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(OrderProcessingContext db, ILogger<OrdersController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpPost]
    [Authorize(Policy = PolicyNames.OrdersCreate)]
    public async Task<ActionResult<OrderResponse>> Create(
        [FromBody] CreateOrderRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKeyHeader,
        CancellationToken ct)
    {
        var userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (customer is null)
            return Problem(statusCode: 400, detail: "No customer profile linked to this user. Register as a customer.");

        var addr = await _db.CustomerAddresses.FirstOrDefaultAsync(
            a => a.CustomerAddressId == request.CustomerAddressId && a.CustomerId == customer.CustomerId, ct);
        if (addr is null)
            return BadRequest(new { message = "CustomerAddressId is invalid or does not belong to your account." });

        var idempotencyKey = string.IsNullOrWhiteSpace(idempotencyKeyHeader)
            ? Guid.NewGuid().ToString("N")
            : idempotencyKeyHeader.Trim();
        if (idempotencyKey.Length > 128)
            return BadRequest(new { message = "Idempotency-Key max length is 128." });

        var existing = await _db.Orders
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Product)
            .Include(o => o.OrderShippingSnapshot)
            .FirstOrDefaultAsync(o => o.CustomerId == customer.CustomerId && o.IdempotencyKey == idempotencyKey, ct);
        if (existing is not null)
        {
            _logger.LogInformation("Idempotent replay for order {OrderId} key {Key}", existing.OrderId, idempotencyKey);
            return Ok(MapOrder(existing));
        }

        if (request.Items is null || request.Items.Count == 0)
            return BadRequest(new { message = "At least one line item is required." });

        var productIds = request.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await _db.Products.Where(p => productIds.Contains(p.ProductId)).ToListAsync(ct);
        if (products.Count != productIds.Count)
            return BadRequest(new { message = "One or more products were not found." });
        if (products.Any(p => !p.IsActive))
            return BadRequest(new { message = "One or more products are inactive." });

        var now = DateTime.UtcNow;
        decimal total = 0;
        var lines = new List<OrderItem>();
        foreach (var line in request.Items)
        {
            var product = products.First(p => p.ProductId == line.ProductId);
            var unit = product.Price;
            var lineTotal = unit * line.Quantity;
            total += lineTotal;
            lines.Add(new OrderItem
            {
                ProductId = line.ProductId,
                Quantity = line.Quantity,
                UnitPrice = unit,
                LineTotal = lineTotal
            });
        }

        var order = new Order
        {
            CustomerId = customer.CustomerId,
            Status = OrderStatuses.Pending,
            TotalAmount = total,
            Currency = "USD",
            IdempotencyKey = idempotencyKey,
            FailureReason = null,
            CorrelationId = request.CorrelationId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        foreach (var line in lines)
            order.OrderItems.Add(line);

        _db.Orders.Add(order);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueIdempotencyViolation(ex))
        {
            var raced = await _db.Orders
                .Include(o => o.OrderItems)
                .ThenInclude(i => i.Product)
                .Include(o => o.OrderShippingSnapshot)
                .FirstOrDefaultAsync(o => o.IdempotencyKey == idempotencyKey, ct);
            if (raced is null)
                throw;
            _logger.LogInformation("Idempotent race replay for order {OrderId} key {Key}", raced.OrderId, idempotencyKey);
            return Ok(MapOrder(raced));
        }

        var ship = new OrderShippingSnapshot
        {
            OrderId = order.OrderId,
            RecipientName = addr.RecipientName,
            Phone = addr.Phone,
            Email = customer.Email ?? "",
            AddressLine1 = addr.AddressLine1,
            AddressLine2 = addr.AddressLine2 ?? "",
            City = addr.City,
            StateOrRegion = addr.StateOrRegion ?? "",
            PostalCode = addr.PostalCode,
            CountryCode = addr.CountryCode,
            DeliveryInstructions = request.DeliveryInstructions?.Trim() ?? "",
            SourceCustomerAddressId = addr.CustomerAddressId
        };
        _db.OrderShippingSnapshots.Add(ship);

        var job = new OrderProcessingJob
        {
            OrderId = order.OrderId,
            JobStatus = JobStatuses.Pending,
            Attempt = 0,
            RetryCount = 0,
            MaxRetries = 5,
            NextRetryAtUtc = null,
            LockedAtUtc = null,
            LockToken = null,
            LockExpiresAtUtc = null,
            LastError = null,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        _db.OrderProcessingJobs.Add(job);

        _db.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = order.OrderId,
            FromStatus = null,
            ToStatus = OrderStatuses.Pending,
            Reason = "Order created",
            ActorType = "Customer",
            ActorUserId = userId,
            CorrelationId = request.CorrelationId,
            OccurredAtUtc = now
        });

        _db.OrderDomainEvents.Add(new OrderDomainEvent
        {
            OrderId = order.OrderId,
            JobId = null,
            EventType = DomainEventTypes.OrderCreated,
            PayloadJson = "{}",
            Severity = "Info",
            ActorType = "Customer",
            ActorUserId = userId,
            CorrelationId = request.CorrelationId,
            OccurredAtUtc = now
        });

        AuditLogWriter.Add(_db, nameof(Order), order.OrderId.ToString(), "order.create", userId,
            new { customer.CustomerId, request.CustomerAddressId, total });
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Order {OrderId} created for customer {CustomerId}", order.OrderId, customer.CustomerId);

        await _db.Entry(order).Collection(o => o.OrderItems).Query().Include(i => i.Product).LoadAsync(ct);
        await _db.Entry(order).Reference(o => o.OrderShippingSnapshot).LoadAsync(ct);
        return CreatedAtAction(nameof(Get), new { orderId = order.OrderId }, MapOrder(order));
    }

    [HttpGet("{orderId:long}")]
    [Authorize(Policy = PolicyNames.OrdersRead)]
    public async Task<ActionResult<OrderResponse>> Get(long orderId, CancellationToken ct)
    {
        var order = await _db.Orders
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Product)
            .Include(o => o.OrderShippingSnapshot)
            .FirstOrDefaultAsync(o => o.OrderId == orderId, ct);
        if (order is null)
            return NotFound();
        if (!await CanAccessOrderAsync(order, ct))
            return Forbid();
        return Ok(MapOrder(order));
    }

    [HttpGet("{orderId:long}/status")]
    [Authorize(Policy = PolicyNames.OrdersRead)]
    public async Task<ActionResult<OrderStatusResponse>> GetStatus(long orderId, CancellationToken ct)
    {
        var order = await _db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.OrderId == orderId, ct);
        if (order is null)
            return NotFound();
        if (!await CanAccessOrderAsync(order, ct))
            return Forbid();
        return Ok(new OrderStatusResponse
        {
            OrderId = order.OrderId,
            Status = order.Status,
            FailureReason = order.FailureReason,
            CreatedAtUtc = order.CreatedAtUtc,
            UpdatedAtUtc = order.UpdatedAtUtc,
            CompletedAtUtc = order.CompletedAtUtc
        });
    }

    [HttpPost("{orderId:long}/cancel")]
    [Authorize(Policy = PolicyNames.OrdersRead)]
    public async Task<ActionResult<OrderStatusResponse>> Cancel(long orderId, CancellationToken ct)
    {
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.OrderId == orderId, ct);
        if (order is null)
            return NotFound();
        if (!await CanAccessOrderAsync(order, ct))
            return Forbid();

        if (order.Status == OrderStatuses.Cancelled)
            return Ok(ToStatusResponse(order));

        if (order.Status != OrderStatuses.Pending)
            return Conflict(new { message = $"Only {OrderStatuses.Pending} orders can be cancelled. Current status: {order.Status}." });

        var userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (!OrderStatusTransitions.IsValid(order.Status, OrderStatuses.Cancelled))
            return Conflict(new { message = $"Invalid transition from {order.Status} to {OrderStatuses.Cancelled}." });

        var from = order.Status;
        var now = DateTime.UtcNow;
        order.Status = OrderStatuses.Cancelled;
        order.CancelledAtUtc = now;
        order.UpdatedAtUtc = now;
        order.FailureReason = null;

        _db.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = order.OrderId,
            FromStatus = from,
            ToStatus = OrderStatuses.Cancelled,
            Reason = "Order cancelled by user request",
            ActorType = "Customer",
            ActorUserId = userId,
            CorrelationId = order.CorrelationId,
            OccurredAtUtc = now
        });
        _db.OrderDomainEvents.Add(new OrderDomainEvent
        {
            OrderId = order.OrderId,
            JobId = null,
            EventType = DomainEventTypes.OrderCancelled,
            PayloadJson = "{}",
            Severity = "Info",
            ActorType = "Customer",
            ActorUserId = userId,
            CorrelationId = order.CorrelationId,
            OccurredAtUtc = now
        });
        AuditLogWriter.Add(_db, nameof(Order), order.OrderId.ToString(), "order.cancel", userId, null);

        var job = await _db.OrderProcessingJobs.FirstOrDefaultAsync(j => j.OrderId == order.OrderId, ct);
        if (job is not null && job.JobStatus is JobStatuses.Pending or JobStatuses.InProgress)
        {
            job.JobStatus = JobStatuses.Failed;
            job.LastError = "Order was cancelled by user.";
            job.LockedAtUtc = null;
            job.LockExpiresAtUtc = null;
            job.LockToken = null;
            job.UpdatedAtUtc = now;
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Order {OrderId} cancelled by user {UserId}.", order.OrderId, userId);
        return Ok(ToStatusResponse(order));
    }

    [HttpGet]
    [Authorize(Policy = PolicyNames.OrdersList)]
    public async Task<ActionResult<IReadOnlyList<OrderResponse>>> List(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var q = _db.Orders
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Product)
            .Include(o => o.OrderShippingSnapshot)
            .AsQueryable();

        if (User.HasClaim(AppClaims.Permission, PermissionCodes.OrdersReadAll))
        {
            // all orders
        }
        else if (User.HasClaim(AppClaims.Permission, PermissionCodes.OrdersReadOwn))
        {
            var userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var customerId = await _db.Customers.Where(c => c.UserId == userId).Select(c => c.CustomerId).FirstOrDefaultAsync(ct);
            if (customerId == 0)
                return Ok(Array.Empty<OrderResponse>());
            q = q.Where(o => o.CustomerId == customerId);
        }
        else
            return Forbid();

        if (!string.IsNullOrWhiteSpace(status))
            q = q.Where(o => o.Status == status);

        var list = await q
            .OrderByDescending(o => o.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return Ok(list.Select(MapOrder).ToList());
    }

    private async Task<bool> CanAccessOrderAsync(Order order, CancellationToken ct)
    {
        if (User.HasClaim(AppClaims.Permission, PermissionCodes.OrdersReadAll))
            return true;
        if (!User.HasClaim(AppClaims.Permission, PermissionCodes.OrdersReadOwn))
            return false;
        var userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var customerId = await _db.Customers.Where(c => c.UserId == userId).Select(c => c.CustomerId).FirstOrDefaultAsync(ct);
        return customerId != 0 && order.CustomerId == customerId;
    }

    private static OrderResponse MapOrder(Order o)
    {
        ShippingSnapshotResponse? ship = null;
        if (o.OrderShippingSnapshot is { } s)
        {
            ship = new ShippingSnapshotResponse
            {
                RecipientName = s.RecipientName,
                Phone = s.Phone,
                Email = string.IsNullOrEmpty(s.Email) ? null : s.Email,
                AddressLine1 = s.AddressLine1,
                AddressLine2 = string.IsNullOrEmpty(s.AddressLine2) ? null : s.AddressLine2,
                City = s.City,
                StateOrRegion = string.IsNullOrEmpty(s.StateOrRegion) ? null : s.StateOrRegion,
                PostalCode = s.PostalCode,
                CountryCode = s.CountryCode,
                DeliveryInstructions = string.IsNullOrEmpty(s.DeliveryInstructions) ? null : s.DeliveryInstructions,
                SourceCustomerAddressId = s.SourceCustomerAddressId
            };
        }

        return new OrderResponse
        {
            OrderId = o.OrderId,
            CustomerId = o.CustomerId,
            Status = o.Status,
            TotalAmount = o.TotalAmount,
            Currency = o.Currency,
            FailureReason = o.FailureReason,
            CorrelationId = o.CorrelationId,
            CreatedAtUtc = o.CreatedAtUtc,
            UpdatedAtUtc = o.UpdatedAtUtc,
            Shipping = ship,
            Items = o.OrderItems.Select(i => new OrderItemResponse
            {
                OrderItemId = i.OrderItemId,
                ProductId = i.ProductId,
                ProductName = i.Product?.Name ?? "",
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice,
                LineTotal = i.LineTotal
            }).ToList()
        };
    }

    private static OrderStatusResponse ToStatusResponse(Order order) => new()
    {
        OrderId = order.OrderId,
        Status = order.Status,
        FailureReason = order.FailureReason,
        CreatedAtUtc = order.CreatedAtUtc,
        UpdatedAtUtc = order.UpdatedAtUtc,
        CompletedAtUtc = order.CompletedAtUtc
    };

    private static bool IsUniqueIdempotencyViolation(DbUpdateException ex) =>
        ex.InnerException?.Message.Contains("UQ_Orders_IdempotencyKey", StringComparison.OrdinalIgnoreCase) == true
        || ex.Message.Contains("UQ_Orders_IdempotencyKey", StringComparison.OrdinalIgnoreCase);
}
