namespace OrderProcessing.Common;

public static class OrderStatusTransitions
{
    public static bool IsValid(string? fromStatus, string toStatus) =>
        (fromStatus, toStatus) switch
        {
            (null, OrderStatuses.Pending) => true,
            (OrderStatuses.Pending, OrderStatuses.Processing) => true,
            (OrderStatuses.Pending, OrderStatuses.Failed) => true,
            (OrderStatuses.Pending, OrderStatuses.Cancelled) => true,
            (OrderStatuses.Processing, OrderStatuses.Completed) => true,
            (OrderStatuses.Processing, OrderStatuses.Failed) => true,
            (OrderStatuses.Processing, OrderStatuses.Cancelled) => true,
            (var same, var target) when same == target => true,
            _ => false
        };
}
