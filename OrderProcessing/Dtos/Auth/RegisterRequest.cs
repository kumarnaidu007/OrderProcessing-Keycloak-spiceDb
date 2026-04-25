using System.ComponentModel.DataAnnotations;

namespace OrderProcessing.Dtos.Auth;

public class RegisterRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; set; } = "";

    [Required, MaxLength(200)]
    public string DisplayName { get; set; } = "";
}
