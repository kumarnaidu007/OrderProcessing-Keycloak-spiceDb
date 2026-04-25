using System.ComponentModel.DataAnnotations;

namespace OrderProcessing.Dtos.Auth;

public class VerifyOtpRequest
{
    [Required]
    public long LoginOtpId { get; set; }

    [Required, StringLength(12, MinimumLength = 4)]
    public string Code { get; set; } = "";
}
