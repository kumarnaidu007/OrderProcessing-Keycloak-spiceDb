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
            .Where(j => j.JobStatus == JobStatuses.Pending
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
            await HandleJobErrorAsync(db, job, ex.Message, ct);
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

        foreach (var line in order.OrderItems)
        {
            var idem = $"order-{order.OrderId}-deduct-{line.ProductId}";
            if (await db.InventoryLedgers.AnyAsync(l => l.IdempotencyKey == idem, ct))
                continue;

            var product = await db.Products.FirstAsync(p => p.ProductId == line.ProductId, ct);
            if (product.AvailableQuantity < line.Quantity)
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
                AttemptNo = 0,
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

        payment.AttemptNo += 1;
        var failRate = _configuration.GetValue("OrderProcessing:PaymentFailureRate", 0.0);
        var fail = Random.Shared.NextDouble() < failRate;
        if (fail)
        {
            payment.Status = PaymentStatuses.Failed;
            payment.FailureReason = "Simulated payment decline.";
            TransitionOrderStatus(db, order, OrderStatuses.Failed, payment.FailureReason);
            order.FailureReason = payment.FailureReason;
            order.UpdatedAtUtc = DateTime.UtcNow;
            AddDomainEvent(db, order.OrderId, job.JobId, DomainEventTypes.PaymentFailed);
            AddDomainEvent(db, order.OrderId, job.JobId, DomainEventTypes.OrderFailed);
            await MarkJobTerminalAsync(db, job, JobStatuses.Failed, payment.FailureReason, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            _logger.LogWarning("Order {OrderId} payment failed (simulated).", order.OrderId);
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
                var from = order.Status;
                order.Status = OrderStatuses.Failed;
                order.FailureReason = message.Length > 500 ? message[..500] : message;
                order.UpdatedAtUtc = DateTime.UtcNow;
                db.OrderStatusHistories.Add(new OrderStatusHistory
                {
                    OrderId = order.OrderId,
                    FromStatus = from,
                    ToStatus = OrderStatuses.Failed,
                    Reason = message,
                    ActorType = "Worker",
                    ActorUserId = null,
                    CorrelationId = order.CorrelationId,
                    OccurredAtUtc = DateTime.UtcNow
                });
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
