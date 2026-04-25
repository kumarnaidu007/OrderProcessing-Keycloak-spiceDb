namespace OrderProcessing.Dtos.Addresses;

public class CustomerAddressResponse
{
    public long CustomerAddressId { get; set; }
    public string Label { get; set; } = "";
    public string RecipientName { get; set; } = "";
    public string Phone { get; set; } = "";
    public string AddressLine1 { get; set; } = "";
    public string? AddressLine2 { get; set; }
    public string City { get; set; } = "";
    public string? StateOrRegion { get; set; }
    public string PostalCode { get; set; } = "";
    public string CountryCode { get; set; } = "";
    public bool IsDefault { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
