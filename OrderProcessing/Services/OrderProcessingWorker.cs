using Microsoft.EntityFrameworkCore;
using OrderProcessing.Common;
using OrderProcessing.Models;

namespace OrderProcessing.Services;

/// <summary>Background loop: claims jobs, runs inventory + simulated payment, updates order status.</summary>
public class OrderProcessingWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OrderProcessingWorker> _logger;
    private readonly IConfiguration _configuration;

    public OrderProcessingWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<OrderProcessingWorker> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Order processing worker started.");
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<OrderProcessingContext>();
                    await TryProcessNextJobAsync(db, stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Worker iteration failed.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
    }

    private async Task TryProcessNextJobAsync(OrderProcessingContext db, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var job = await db.OrderProcessingJobs
            .Where(j =>
                (j.JobStatus == JobStatuses.Pending || j.JobStatus == JobStatuses.InProgress)
                && (j.NextRetryAtUtc == null || j.NextRetryAtUtc <= now)
                && (j.LockExpiresAtUtc == null || j.LockExpiresAtUtc < now))
            .OrderBy(j => j.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (job is null)
            return;

        job.LockToken = Guid.NewGuid();
        job.LockedAtUtc = now;
        job.LockExpiresAtUtc = now.AddSeconds(30);
        job.JobStatus = JobStatuses.InProgress;
        job.Attempt += 1;
        job.UpdatedAtUtc = now;
        await db.SaveChangesAsync(ct);

        try
        {
            await ProcessJobCoreAsync(db, job, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Processing failed for job {JobId} order {OrderId}", job.JobId, job.OrderId);
            db.ChangeTracker.Clear();
            var jobRow = await db.OrderProcessingJobs.FirstOrDefaultAsync(j => j.JobId == job.JobId, ct);
            if (jobRow is null)
            {
                _logger.LogError("Failed to reload job {JobId} after processing error.", job.JobId);
                return;
            }
            await HandleJobErrorAsync(db, jobRow, ex.Message, ct);
        }
    }

    private async Task ProcessJobCoreAsync(OrderProcessingContext db, OrderProcessingJob job, CancellationToken ct)
    {
        var order = await db.Orders
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Product)
            .Include(o => o.PaymentAttempt)
            .FirstAsync(o => o.OrderId == job.OrderId, ct);

        if (order.Status is OrderStatuses.Completed or OrderStatuses.Cancelled)
        {
            await MarkJobTerminalAsync(db, job, JobStatuses.Succeeded, null, ct);
            await db.SaveChangesAsync(ct);
            return;
        }

        if (order.Status == OrderStatuses.Failed)
        {
            await MarkJobTerminalAsync(db, job, JobStatuses.Failed, order.FailureReason, ct);
            await db.SaveChangesAsync(ct);
            return;
        }

        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);

        if (order.Status == OrderStatuses.Pending)
        {
            TransitionOrderStatus(db, order, OrderStatuses.Processing, "Background worker started processing");
            AddDomainEvent(db, order.OrderId, job.JobId, DomainEventTypes.OrderProcessingStarted);
        }

        var linesToDeduct = new List<OrderItem>();
        foreach (var line in order.OrderItems)
        {
            var idem = $"order-{order.OrderId}-deduct-{line.ProductId}";
            if (await db.InventoryLedgers.AnyAsync(l => l.IdempotencyKey == idem, ct))
                continue;
            linesToDeduct.Add(line);
        }

        var requiredByProduct = linesToDeduct
            .GroupBy(x => x.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

        if (requiredByProduct.Count > 0)
        {
            var productIds = requiredByProduct.Keys.ToList();
            var productsForCheck = await db.Products
                .Where(p => productIds.Contains(p.ProductId))
                .ToDictionaryAsync(p => p.ProductId, ct);

            foreach (var needed in requiredByProduct)
            {
                var product = productsForCheck[needed.Key];
                if (product.AvailableQuantity < needed.Value)
                {
                    var msg = $"Insufficient stock for product {product.Name} (id {product.ProductId}).";
                    FailOrder(db, order, msg);
                    AddDomainEvent(db, order.OrderId, job.JobId, DomainEventTypes.OrderFailed);
                    await MarkJobTerminalAsync(db, job, JobStatuses.Failed, msg, ct);
                    await db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                    _logger.LogWarning("Order {OrderId} failed: {Reason}", order.OrderId, msg);
                    return;
                }
            }
        }

        foreach (var line in linesToDeduct)
        {
            var idem = $"order-{order.OrderId}-deduct-{line.ProductId}";
            var product = await db.Products.FirstAsync(p => p.ProductId == line.ProductId, ct);
            product.AvailableQuantity -= line.Quantity;
            product.UpdatedAtUtc = DateTime.UtcNow;
            db.InventoryLedgers.Add(new InventoryLedger
            {
                OrderId = order.OrderId,
                ProductId = line.ProductId,
                MovementType = InventoryMovementTypes.SaleDeduct,
                Quantity = line.Quantity,
                IdempotencyKey = idem,
                Succeeded = true,
                CreatedAtUtc = DateTime.UtcNow
            });
            AddDomainEvent(db, order.OrderId, job.JobId, DomainEventTypes.InventoryDeducted,
                $"{{\"productId\":{line.ProductId},\"quantity\":{line.Quantity}}}");
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            var jobRow = await db.OrderProcessingJobs.FirstAsync(j => j.JobId == job.JobId, ct);
            await HandleJobErrorAsync(db, jobRow, "Concurrency conflict updating inventory.", ct);
            return;
        }

        var paymentKey = $"order-{order.OrderId}-payment";
        var payment = order.PaymentAttempt;
        if (payment is null)
        {
            payment = new PaymentAttempt
            {
                OrderId = order.OrderId,
                AttemptNo = 1,
                Amount = order.TotalAmount,
                Status = PaymentStatuses.Pending,
                ProviderReference = "SIM",
                IdempotencyKey = paymentKey,
                FailureReason = null,
                CreatedAtUtc = DateTime.UtcNow
            };
            db.PaymentAttempts.Add(payment);
            await db.SaveChangesAsync(ct);
        }
        else
            payment.AttemptNo += 1;

        if (payment.Status == PaymentStatuses.Succeeded)
        {
            if (order.Status != OrderStatuses.Completed)
            {
                TransitionOrderStatus(db, order, OrderStatuses.Completed, "Payment already succeeded");
                order.CompletedAtUtc = DateTime.UtcNow;
                order.UpdatedAtUtc = DateTime.UtcNow;
            }

            await MarkJobTerminalAsync(db, job, JobStatuses.Succeeded, null, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return;
        }

        var failRate = _configuration.GetValue("OrderProcessing:PaymentFailureRate", 0.0);
        var fail = Random.Shared.NextDouble() < failRate;
        if (fail)
        {
            payment.Status = PaymentStatuses.Failed;
            payment.FailureReason = "Simulated payment decline.";
            AddDomainEvent(db, order.OrderId, job.JobId, DomainEventTypes.PaymentFailed);

            var maxPaymentAttempts = _configuration.GetValue("OrderProcessing:MaxPaymentAttempts", 2);
            if (payment.AttemptNo < Math.Max(1, maxPaymentAttempts))
            {
                payment.Status = PaymentStatuses.Pending;
                payment.FailureReason = null;
                job.JobStatus = JobStatuses.Pending;
                job.NextRetryAtUtc = DateTime.UtcNow.Add(GetBackoff(payment.AttemptNo));
                job.LockedAtUtc = null;
                job.LockExpiresAtUtc = null;
                job.LockToken = null;
                job.LastError = "Simulated payment decline; retry scheduled.";
                job.UpdatedAtUtc = DateTime.UtcNow;
                AddDomainEvent(db, order.OrderId, job.JobId, DomainEventTypes.PaymentRetryScheduled);
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                _logger.LogWarning(
                    "Order {OrderId} payment transient failure. Scheduled retry attempt {Attempt}.",
                    order.OrderId,
                    payment.AttemptNo + 1);
                return;
            }

            var finalFailure = "Simulated payment decline after retries.";
            await RestoreInventoryAsync(db, order, job.JobId, ct);
            TransitionOrderStatus(db, order, OrderStatuses.Failed, finalFailure);
            order.FailureReason = finalFailure;
            order.UpdatedAtUtc = DateTime.UtcNow;
            AddDomainEvent(db, order.OrderId, job.JobId, DomainEventTypes.OrderFailed);
            await MarkJobTerminalAsync(db, job, JobStatuses.Failed, finalFailure, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            _logger.LogWarning("Order {OrderId} payment failed after retries.", order.OrderId);
            return;
        }

        payment.Status = PaymentStatuses.Succeeded;
        payment.FailureReason = null;
        payment.ProviderReference = $"SIM-{Guid.NewGuid():N}";
        TransitionOrderStatus(db, order, OrderStatuses.Completed, "Payment succeeded (simulated).");
        order.CompletedAtUtc = DateTime.UtcNow;
        order.FailureReason = null;
        order.UpdatedAtUtc = DateTime.UtcNow;
        AddDomainEvent(db, order.OrderId, job.JobId, DomainEventTypes.PaymentSucceeded);
        AddDomainEvent(db, order.OrderId, job.JobId, DomainEventTypes.OrderCompleted);
        await MarkJobTerminalAsync(db, job, JobStatuses.Succeeded, null, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        _logger.LogInformation("Order {OrderId} completed successfully.", order.OrderId);
    }

    private static void FailOrder(OrderProcessingContext db, Order order, string reason)
    {
        TransitionOrderStatus(db, order, OrderStatuses.Failed, reason);
        order.FailureReason = reason;
        order.UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>Sets new status and appends history using previous <see cref="Order.Status"/>.</summary>
    private static void TransitionOrderStatus(
        OrderProcessingContext db,
        Order order,
        string toStatus,
        string reason)
    {
        if (order.Status == toStatus)
            return;
        if (!OrderStatusTransitions.IsValid(order.Status, toStatus))
            throw new InvalidOperationException($"Invalid order status transition {order.Status} -> {toStatus}.");
        var from = order.Status;
        order.Status = toStatus;
        order.UpdatedAtUtc = DateTime.UtcNow;
        db.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = order.OrderId,
            FromStatus = from,
            ToStatus = toStatus,
            Reason = reason,
            ActorType = "Worker",
            ActorUserId = null,
            CorrelationId = order.CorrelationId,
            OccurredAtUtc = DateTime.UtcNow
        });
    }

    private static void AddDomainEvent(OrderProcessingContext db, long orderId, long? jobId, string eventType, string? payloadJson = null)
    {
        db.OrderDomainEvents.Add(new OrderDomainEvent
        {
            OrderId = orderId,
            JobId = jobId,
            EventType = eventType,
            PayloadJson = string.IsNullOrEmpty(payloadJson) ? "{}" : payloadJson,
            Severity = "Info",
            ActorType = "Worker",
            ActorUserId = null,
            CorrelationId = null,
            OccurredAtUtc = DateTime.UtcNow
        });
    }

    private static Task MarkJobTerminalAsync(
        OrderProcessingContext db,
        OrderProcessingJob job,
        string terminalStatus,
        string? lastError,
        CancellationToken ct)
    {
        job.JobStatus = terminalStatus;
        job.LastError = lastError;
        job.LockedAtUtc = null;
        job.LockExpiresAtUtc = null;
        job.LockToken = null;
        job.UpdatedAtUtc = DateTime.UtcNow;
        return Task.CompletedTask;
    }

    private static async Task RestoreInventoryAsync(OrderProcessingContext db, Order order, long? jobId, CancellationToken ct)
    {
        foreach (var line in order.OrderItems)
        {
            var deductKey = $"order-{order.OrderId}-deduct-{line.ProductId}";
            var restoreKey = $"order-{order.OrderId}-restore-{line.ProductId}";
            var wasDeducted = await db.InventoryLedgers.AnyAsync(l => l.IdempotencyKey == deductKey, ct);
            if (!wasDeducted)
                continue;
            var alreadyRestored = await db.InventoryLedgers.AnyAsync(l => l.IdempotencyKey == restoreKey, ct);
            if (alreadyRestored)
                continue;

            var product = await db.Products.FirstAsync(p => p.ProductId == line.ProductId, ct);
            product.AvailableQuantity += line.Quantity;
            product.UpdatedAtUtc = DateTime.UtcNow;
            db.InventoryLedgers.Add(new InventoryLedger
            {
                OrderId = order.OrderId,
                ProductId = line.ProductId,
                MovementType = InventoryMovementTypes.SaleRestore,
                Quantity = line.Quantity,
                IdempotencyKey = restoreKey,
                Succeeded = true,
                CreatedAtUtc = DateTime.UtcNow
            });
            AddDomainEvent(
                db,
                order.OrderId,
                jobId,
                DomainEventTypes.InventoryRestored,
                $"{{\"productId\":{line.ProductId},\"quantity\":{line.Quantity}}}");
        }
    }

    private async Task HandleJobErrorAsync(OrderProcessingContext db, OrderProcessingJob job, string message, CancellationToken ct)
    {
        job.RetryCount += 1;
        job.LastError = message.Length > 2000 ? message[..2000] : message;
        job.LockedAtUtc = null;
        job.LockExpiresAtUtc = null;
        job.LockToken = null;
        job.UpdatedAtUtc = DateTime.UtcNow;

        if (job.RetryCount >= job.MaxRetries)
        {
            job.JobStatus = JobStatuses.Failed;
            var order = await db.Orders.FirstAsync(o => o.OrderId == job.OrderId, ct);
            if (order.Status is not (OrderStatuses.Completed or OrderStatuses.Failed or OrderStatuses.Cancelled))
            {
                var reason = message.Length > 500 ? message[..500] : message;
                TransitionOrderStatus(db, order, OrderStatuses.Failed, reason);
                order.FailureReason = reason;
                order.UpdatedAtUtc = DateTime.UtcNow;
            }

            _logger.LogError("Job {JobId} exceeded retries; marked failed.", job.JobId);
        }
        else
        {
            job.JobStatus = JobStatuses.Pending;
            job.NextRetryAtUtc = DateTime.UtcNow.Add(GetBackoff(job.RetryCount));
            _logger.LogWarning("Job {JobId} scheduled retry {Retry} at {Next}.", job.JobId, job.RetryCount, job.NextRetryAtUtc);
        }

        await db.SaveChangesAsync(ct);
    }

    private static TimeSpan GetBackoff(int retryCount) =>
        retryCount switch
        {
            1 => TimeSpan.FromSeconds(30),
            2 => TimeSpan.FromMinutes(2),
            _ => TimeSpan.FromMinutes(5)
        };
}
