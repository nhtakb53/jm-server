using System.Security.Cryptography;
using System.Text;

namespace JmServer.Domain;

public static class TokenHasher
{
    public static byte[] Hash(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return SHA256.HashData(Encoding.UTF8.GetBytes(token));
    }

    public static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
