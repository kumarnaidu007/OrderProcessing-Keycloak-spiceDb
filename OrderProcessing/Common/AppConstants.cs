namespace OrderProcessing.Common;

public static class Roles
{
    public const string Customer = "Customer";
    public const string Admin = "Admin";
}

public static class OrderStatuses
{
    public const string Pending = "Pending";
    public const string Processing = "Processing";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
}

public static class JobStatuses
{
    public const string Pending = "Pending";
    public const string InProgress = "InProgress";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
}

public static class InventoryMovementTypes
{
    public const string SaleDeduct = "SaleDeduct";
    public const string SaleRestore = "SaleRestore";
}

public static class PaymentStatuses
{
    public const string Pending = "Pending";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
}

public static class DomainEventTypes
{
    public const string OrderCreated = "OrderCreated";
    public const string OrderCancelled = "OrderCancelled";
    public const string OrderProcessingStarted = "OrderProcessingStarted";
    public const string InventoryDeducted = "InventoryDeducted";
    public const string InventoryRestored = "InventoryRestored";
    public const string PaymentSucceeded = "PaymentSucceeded";
    public const string PaymentFailed = "PaymentFailed";
    public const string PaymentRetryScheduled = "PaymentRetryScheduled";
    public const string OrderCompleted = "OrderCompleted";
    public const string OrderFailed = "OrderFailed";
}
