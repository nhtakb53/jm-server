using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace JmServer.GameIntegration;

public static class CertificatePinning
{
    public static RemoteCertificateValidationCallback CreateSha256Validator(string sha256Hex)
    {
        byte[] expectedHash;
        try
        {
            expectedHash = Convert.FromHexString(sha256Hex.Replace(":", string.Empty));
        }
        catch (FormatException exception)
        {
            throw new ArgumentException(
                "Certificate SHA-256 pin is not valid hexadecimal.",
                nameof(sha256Hex),
                exception);
        }

        if (expectedHash.Length != SHA256.HashSizeInBytes)
        {
            throw new ArgumentException(
                "Certificate SHA-256 pin must contain exactly 32 bytes.",
                nameof(sha256Hex));
        }

        return (_, certificate, _, sslPolicyErrors) =>
            Validate(certificate, sslPolicyErrors, expectedHash, DateTime.UtcNow);
    }

    internal static bool Validate(
        X509Certificate? certificate,
        SslPolicyErrors sslPolicyErrors,
        ReadOnlySpan<byte> expectedHash,
        DateTime utcNow)
    {
        if (certificate is null ||
            sslPolicyErrors is SslPolicyErrors.RemoteCertificateNotAvailable)
        {
            return false;
        }

        using var certificate2 = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
        if (utcNow < certificate2.NotBefore.ToUniversalTime() ||
            utcNow > certificate2.NotAfter.ToUniversalTime())
        {
            return false;
        }

        var actualHash = SHA256.HashData(certificate2.RawData);
        return expectedHash.Length == actualHash.Length &&
               CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
    }
}
