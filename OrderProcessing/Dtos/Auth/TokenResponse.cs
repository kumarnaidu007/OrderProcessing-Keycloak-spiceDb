namespace OrderProcessing.Dtos.Auth;

public class TokenResponse
{
    public string AccessToken { get; set; } = "";
    public string TokenType { get; set; } = "Bearer";
    public int ExpiresInSeconds { get; set; }

    /// <summary>Store securely; only shown once. Used with <see cref="SessionId"/> on POST /api/auth/refresh.</summary>
    public string RefreshToken { get; set; } = "";

    public Guid SessionId { get; set; }
}
