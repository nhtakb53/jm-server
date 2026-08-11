using System.Security.Cryptography;
using JmServer.Contracts;
using JmServer.GameIntegration;
using JmServer.Protocol;

namespace JmServer.Launcher;

public static class DeviceSettingsSynchronizer
{
    public static async Task<DeviceSettingsDownloadResult> DownloadAsync(
        LauncherConnection connection,
        CancellationToken cancellationToken)
    {
        var response = await connection.GetDeviceSettingsAsync(cancellationToken);
        ValidateDownload(response);
        if (!response.Metadata.Exists)
        {
            return new DeviceSettingsDownloadResult(false, 0);
        }

        await D2RModSettings.InstallSyncedAsync(
            response.Binary,
            cancellationToken: cancellationToken);
        return new DeviceSettingsDownloadResult(true, response.Metadata.Revision);
    }

    public static async Task<DeviceSettingsUploadResult> UploadAsync(
        LauncherConnection connection,
        CancellationToken cancellationToken)
    {
        var settingsData = await D2RModSettings.ReadForSyncAsync(
            cancellationToken: cancellationToken);
        if (settingsData is null)
        {
            return new DeviceSettingsUploadResult(false, 0);
        }

        var sha256Hex = Convert.ToHexString(SHA256.HashData(settingsData));
        var response = await connection.PutDeviceSettingsAsync(
            new PutDeviceSettingsRequest(settingsData.Length, sha256Hex),
            settingsData,
            cancellationToken);
        if (!string.Equals(response.Sha256Hex, sha256Hex, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Server returned a different checksum after saving the device settings.");
        }

        return new DeviceSettingsUploadResult(true, response.Revision);
    }

    internal static void ValidateDownload(
        BinaryPayload<GetDeviceSettingsResponse> response)
    {
        if (!response.Metadata.Exists)
        {
            if (response.Metadata.Revision != 0 ||
                response.Metadata.SettingsLength != 0 ||
                response.Binary.Length != 0 ||
                !string.IsNullOrEmpty(response.Metadata.Sha256Hex))
            {
                throw new InvalidDataException(
                    "Server returned inconsistent metadata for missing device settings.");
            }

            return;
        }

        if (response.Metadata.Revision < 1)
        {
            throw new InvalidDataException("Server returned an invalid device settings revision.");
        }

        if (response.Metadata.SettingsLength != response.Binary.Length ||
            response.Binary.Length is < 1 or > ProtocolConstants.MaxDeviceSettingsLength)
        {
            throw new InvalidDataException(
                "Server returned device settings with an invalid length.");
        }

        var actualHash = Convert.ToHexString(SHA256.HashData(response.Binary));
        if (!string.Equals(
                actualHash,
                response.Metadata.Sha256Hex,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Server returned device settings with a mismatched checksum.");
        }
    }
}

public sealed record DeviceSettingsDownloadResult(bool Downloaded, long Revision);

public sealed record DeviceSettingsUploadResult(bool Uploaded, long Revision);
