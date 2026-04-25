namespace OrderProcessing.Dtos.Orders;

public class OrderStatusResponse
{
    public long OrderId { get; set; }
    public string Status { get; set; } = "";
    public string? FailureReason { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}
