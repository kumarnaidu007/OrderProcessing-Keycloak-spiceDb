namespace OrderProcessing.Dtos.Auth;

/// <summary>Returned from login/register-otp: client must POST verify-otp with LoginOtpId and Code.</summary>
public class LoginOtpChallengeResponse
{
    public string Message { get; set; } = "";
    public string NextStep { get; set; } = "verify_otp";
    public string VerifyEndpoint { get; set; } = "/api/auth/verify-otp";
    public long LoginOtpId { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public string LoginIdentifier { get; set; } = "";

    /// <summary>Only populated in Development so you can test without email delivery.</summary>
    public string? DevOtpCode { get; set; }
}
