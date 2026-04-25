using System.ComponentModel.DataAnnotations;

namespace OrderProcessing.Dtos.Addresses;

public class CreateCustomerAddressRequest
{
    [MaxLength(64)]
    public string? Label { get; set; }

    [Required, MaxLength(200)]
    public string RecipientName { get; set; } = "";

    [Required, MaxLength(50)]
    public string Phone { get; set; } = "";

    [Required, MaxLength(256)]
    public string AddressLine1 { get; set; } = "";

    [MaxLength(256)]
    public string? AddressLine2 { get; set; }

    [Required, MaxLength(128)]
    public string City { get; set; } = "";

    [MaxLength(128)]
    public string? StateOrRegion { get; set; }

    [Required, MaxLength(32)]
    public string PostalCode { get; set; } = "";

    [Required, MinLength(2), MaxLength(2)]
    public string CountryCode { get; set; } = "";

    public bool IsDefault { get; set; }
}
