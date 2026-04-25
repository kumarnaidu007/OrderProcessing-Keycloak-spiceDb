namespace OrderProcessing.Dtos.Products;

public class ProductResponse
{
    public long ProductId { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public int AvailableQuantity { get; set; }
    public bool IsActive { get; set; }

    /// <summary>Send on admin update for optimistic concurrency (optional).</summary>
    public string? RowVersion { get; set; }
}
