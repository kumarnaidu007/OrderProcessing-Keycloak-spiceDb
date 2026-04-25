using System.ComponentModel.DataAnnotations;

namespace OrderProcessing.Dtos.Auth;

public class RefreshTokenRequest
{
    [Required]
    public Guid SessionId { get; set; }

    [Required]
    public string RefreshToken { get; set; } = "";
}
