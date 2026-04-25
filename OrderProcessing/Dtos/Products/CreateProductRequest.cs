using System.ComponentModel.DataAnnotations;

namespace OrderProcessing.Dtos.Products;

public class CreateProductRequest
{
    [Required, MaxLength(200)]
    public string Name { get; set; } = "";

    [Range(typeof(decimal), "0.01", "79228162514264337593543950335")]
    public decimal Price { get; set; }

    [Range(0, int.MaxValue)]
    public int AvailableQuantity { get; set; }
}
