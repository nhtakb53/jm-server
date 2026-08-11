using System.Security.Cryptography;

namespace JmServer.Domain;

public sealed class ProfileVaultService
{
    public static readonly TimeSpan DefaultLeaseDuration = TimeSpan.FromMinutes(2);

    private readonly IProfileStore _store;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _leaseDuration;

    public ProfileVaultService(
        IProfileStore store,
        TimeProvider? timeProvider = null,
        TimeSpan? leaseDuration = null)
    {
        _store = store;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _leaseDuration = leaseDuration ?? DefaultLeaseDuration;
        if (_leaseDuration < TimeSpan.FromSeconds(30))
        {
            throw new ArgumentOutOfRangeException(
                nameof(leaseDuration),
                "Lease duration must be at least 30 seconds.");
        }
    }

    public Task<ProfileCheckout> CheckoutAsync(
        Guid accountId,
        Guid deviceId,
        Guid selectedCharacterId,
        CancellationToken cancellationToken) =>
        _store.CheckoutAsync(
            accountId,
            deviceId,
            selectedCharacterId,
            _timeProvider.GetUtcNow(),
            _leaseDuration,
            cancellationToken);

    public Task<DateTimeOffset> RenewLeaseAsync(
        Guid accountId,
        Guid deviceId,
        Guid leaseId,
        CancellationToken cancellationToken) =>
        _store.RenewLeaseAsync(
            accountId,
            deviceId,
            leaseId,
            _timeProvider.GetUtcNow(),
            _leaseDuration,
            cancellationToken);

    public Task<ProfileCheckinResult> CheckinAsync(
        Guid accountId,
        Guid deviceId,
        Guid leaseId,
        long expectedRevision,
        ReadOnlyMemory<byte> bundleData,
        string expectedSha256Hex,
        CancellationToken cancellationToken)
    {
        if (bundleData.Length > ProfileBundleCodec.MaxBundleLength)
        {
            throw new VaultException(
                VaultError.SaveTooLarge,
                $"Profile bundle is {bundleData.Length} bytes; maximum is {ProfileBundleCodec.MaxBundleLength}.");
        }

        _ = ProfileBundleCodec.Decode(bundleData.Span);
        byte[] expectedHash;
        try
        {
            expectedHash = Convert.FromHexString(expectedSha256Hex);
        }
        catch (FormatException)
        {
            throw new VaultException(VaultError.ChecksumMismatch, "Profile checksum is not valid hexadecimal.");
        }

        var actualHash = SHA256.HashData(bundleData.Span);
        if (expectedHash.Length != actualHash.Length ||
            !CryptographicOperations.FixedTimeEquals(expectedHash, actualHash))
        {
            throw new VaultException(VaultError.ChecksumMismatch, "Profile checksum does not match its content.");
        }

        return _store.CheckinAsync(
            accountId,
            deviceId,
            leaseId,
            expectedRevision,
            bundleData,
            actualHash,
            _timeProvider.GetUtcNow(),
            cancellationToken);
    }
}
