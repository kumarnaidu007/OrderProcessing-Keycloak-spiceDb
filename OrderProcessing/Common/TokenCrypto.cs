using System.Security.Cryptography;
using System.Text;

namespace OrderProcessing.Common;

public static class TokenCrypto
{
    public static string GenerateRefreshTokenPlainText()
    {
        var bytes = RandomNumberGenerator.GetBytes(48);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static string HashRefreshToken(string plainText)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(plainText));
        return Convert.ToBase64String(hash);
    }
}
