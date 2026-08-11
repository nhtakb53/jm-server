using System.Security.Cryptography;
using JmServer.Domain;

namespace JmServer.Domain.Tests;

public sealed class DeviceSettingsServiceTests
{
    [Fact]
    public async Task ValidJsonObjectIsSavedWithVerifiedChecksum()
    {
        var store = new RecordingDeviceSettingsStore();
        var service = new DeviceSettingsService(store);
        var accountId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var settings = """{"Gamma":220,"VSync":0}"""u8.ToArray();
        var hash = SHA256.HashData(settings);

        var result = await service.PutAsync(
            accountId,
            deviceId,
            settings,
            Convert.ToHexString(hash),
            CancellationToken.None);

        Assert.Equal(1, result.Revision);
        Assert.Equal(settings, store.SavedData);
        Assert.Equal(hash, store.SavedHash);
        Assert.Equal(accountId, store.AccountId);
        Assert.Equal(deviceId, store.DeviceId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("not-json")]
    public async Task InvalidSettingsAreRejected(string json)
    {
        var service = new DeviceSettingsService(new RecordingDeviceSettingsStore());
        var data = System.Text.Encoding.UTF8.GetBytes(json);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.PutAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            data,
            Convert.ToHexString(SHA256.HashData(data)),
            CancellationToken.None));
    }

    [Fact]
    public async Task MismatchedChecksumIsRejected()
    {
        var service = new DeviceSettingsService(new RecordingDeviceSettingsStore());
        var data = "{}"u8.ToArray();

        await Assert.ThrowsAsync<InvalidDataException>(() => service.PutAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            data,
            Convert.ToHexString(new byte[32]),
            CancellationToken.None));
    }

    [Fact]
    public async Task OversizedSettingsAreRejected()
    {
        var service = new DeviceSettingsService(new RecordingDeviceSettingsStore());
        var data = Enumerable.Repeat(
                (byte)' ',
                DeviceSettingsService.MaximumSettingsLength + 1)
            .ToArray();
        data[0] = (byte)'{';
        data[^1] = (byte)'}';

        await Assert.ThrowsAsync<InvalidDataException>(() => service.PutAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            data,
            Convert.ToHexString(SHA256.HashData(data)),
            CancellationToken.None));
    }

    private sealed class RecordingDeviceSettingsStore : IDeviceSettingsStore
    {
        public Guid AccountId { get; private set; }
        public Guid DeviceId { get; private set; }
        public byte[]? SavedData { get; private set; }
        public byte[]? SavedHash { get; private set; }

        public Task<DeviceSettingsSnapshot?> GetAsync(
            Guid accountId,
            Guid deviceId,
            CancellationToken cancellationToken) =>
            Task.FromResult<DeviceSettingsSnapshot?>(null);

        public Task<DeviceSettingsSnapshot> PutAsync(
            Guid accountId,
            Guid deviceId,
            ReadOnlyMemory<byte> settingsData,
            ReadOnlyMemory<byte> sha256,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            AccountId = accountId;
            DeviceId = deviceId;
            SavedData = settingsData.ToArray();
            SavedHash = sha256.ToArray();
            return Task.FromResult(new DeviceSettingsSnapshot(
                1,
                SavedData,
                SavedHash));
        }
    }
}
