using System.ComponentModel.DataAnnotations;

namespace OrderProcessing.Dtos.Auth;

/// <summary>Start login: an OTP is created and you must call verify-otp.</summary>
public class RequestOtpRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; set; } = "";
}
