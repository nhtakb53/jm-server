using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using JmServer.GameIntegration;

namespace JmServer.GameIntegration.Tests;

public sealed class CertificatePinningTests
{
    [Fact]
    public void Validate_AcceptsExactPinnedCertificateDespiteSelfSignedChainError()
    {
        var now = DateTime.UtcNow;
        using var certificate = CreateCertificate(now.AddDays(-1), now.AddDays(1));
        var expectedHash = SHA256.HashData(certificate.RawData);

        var accepted = CertificatePinning.Validate(
            certificate,
            SslPolicyErrors.RemoteCertificateChainErrors |
            SslPolicyErrors.RemoteCertificateNameMismatch,
            expectedHash,
            now);

        Assert.True(accepted);
    }

    [Fact]
    public void Validate_RejectsDifferentCertificateHash()
    {
        var now = DateTime.UtcNow;
        using var certificate = CreateCertificate(now.AddDays(-1), now.AddDays(1));

        var accepted = CertificatePinning.Validate(
            certificate,
            SslPolicyErrors.RemoteCertificateChainErrors,
            new byte[SHA256.HashSizeInBytes],
            now);

        Assert.False(accepted);
    }

    [Fact]
    public void Validate_RejectsExpiredCertificate()
    {
        var now = DateTime.UtcNow;
        using var certificate = CreateCertificate(now.AddDays(-2), now.AddDays(-1));

        var accepted = CertificatePinning.Validate(
            certificate,
            SslPolicyErrors.RemoteCertificateChainErrors,
            SHA256.HashData(certificate.RawData),
            now);

        Assert.False(accepted);
    }

    [Fact]
    public void CreateSha256Validator_RejectsMalformedPin()
    {
        Assert.Throws<ArgumentException>(() =>
            CertificatePinning.CreateSha256Validator("not-a-sha256-pin"));
    }

    private static X509Certificate2 CreateCertificate(DateTime notBefore, DateTime notAfter)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=JM Server Test",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(notBefore, notAfter);
    }
}
