using System.ComponentModel.DataAnnotations;

namespace OrderProcessing.Dtos.Orders;

public class CreateOrderRequest
{
    /// <summary>Must belong to the authenticated customer; used to build <c>OrderShippingSnapshot</c>.</summary>
    [Required]
    public long CustomerAddressId { get; set; }

    [MaxLength(500)]
    public string? DeliveryInstructions { get; set; }

    [Required, MinLength(1)]
    public List<OrderLineRequest> Items { get; set; } = [];

    public Guid? CorrelationId { get; set; }
}

public class OrderLineRequest
{
    [Range(1, long.MaxValue)]
    public long ProductId { get; set; }

    [Range(1, int.MaxValue)]
    public int Quantity { get; set; }
}
