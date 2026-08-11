using System.Security.Cryptography;
using System.Text.Json;

namespace JmServer.Domain;

public sealed class DeviceSettingsService
{
    public const int MaximumSettingsLength = 256 * 1024;

    private readonly IDeviceSettingsStore _store;
    private readonly TimeProvider _timeProvider;

    public DeviceSettingsService(
        IDeviceSettingsStore store,
        TimeProvider? timeProvider = null)
    {
        _store = store;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<DeviceSettingsSnapshot?> GetAsync(
        Guid accountId,
        Guid deviceId,
        CancellationToken cancellationToken) =>
        _store.GetAsync(accountId, deviceId, cancellationToken);

    public Task<DeviceSettingsSnapshot> PutAsync(
        Guid accountId,
        Guid deviceId,
        ReadOnlyMemory<byte> settingsData,
        string expectedSha256Hex,
        CancellationToken cancellationToken)
    {
        ValidateSettings(settingsData.Span);

        byte[] expectedHash;
        try
        {
            expectedHash = Convert.FromHexString(expectedSha256Hex);
        }
        catch (FormatException)
        {
            throw new InvalidDataException("Device settings checksum is not valid hexadecimal.");
        }

        var actualHash = SHA256.HashData(settingsData.Span);
        if (expectedHash.Length != actualHash.Length ||
            !CryptographicOperations.FixedTimeEquals(expectedHash, actualHash))
        {
            throw new InvalidDataException("Device settings checksum does not match its content.");
        }

        return _store.PutAsync(
            accountId,
            deviceId,
            settingsData,
            actualHash,
            _timeProvider.GetUtcNow(),
            cancellationToken);
    }

    private static void ValidateSettings(ReadOnlySpan<byte> settingsData)
    {
        if (settingsData.IsEmpty)
        {
            throw new InvalidDataException("Device settings cannot be empty.");
        }

        if (settingsData.Length > MaximumSettingsLength)
        {
            throw new InvalidDataException(
                $"Device settings are {settingsData.Length} bytes; maximum is {MaximumSettingsLength}.");
        }

        try
        {
            using var document = JsonDocument.Parse(settingsData.ToArray());
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("Device settings JSON root must be an object.");
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Device settings are not valid JSON.", exception);
        }
    }
}
