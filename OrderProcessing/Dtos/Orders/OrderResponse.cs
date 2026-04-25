namespace OrderProcessing.Dtos.Orders;

public class OrderResponse
{
    public long OrderId { get; set; }
    public long CustomerId { get; set; }
    public string Status { get; set; } = "";
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = "";
    public string? FailureReason { get; set; }
    public Guid? CorrelationId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public List<OrderItemResponse> Items { get; set; } = [];

    /// <summary>Frozen ship-to at order time (from <c>CustomerAddresses</c>).</summary>
    public ShippingSnapshotResponse? Shipping { get; set; }
}

public class ShippingSnapshotResponse
{
    public string RecipientName { get; set; } = "";
    public string Phone { get; set; } = "";
    public string? Email { get; set; }
    public string AddressLine1 { get; set; } = "";
    public string? AddressLine2 { get; set; }
    public string City { get; set; } = "";
    public string? StateOrRegion { get; set; }
    public string PostalCode { get; set; } = "";
    public string CountryCode { get; set; } = "";
    public string? DeliveryInstructions { get; set; }
    public long? SourceCustomerAddressId { get; set; }
}

public class OrderItemResponse
{
    public long OrderItemId { get; set; }
    public long ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
}
