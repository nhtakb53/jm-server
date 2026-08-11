using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace JmServer.Launcher;

public static class ClientProfileStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("JM Server client profile v1");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static string ProfilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "JM Server",
        "client-profile.json");

    public static async Task SaveAsync(
        ClientProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "The encrypted client profile is supported on Windows only.");
        }

        Validate(profile);

        var plainToken = Encoding.UTF8.GetBytes(profile.Token);
        byte[]? protectedToken = null;
        try
        {
            protectedToken = ProtectedData.Protect(
                plainToken,
                Entropy,
                DataProtectionScope.CurrentUser);
            var stored = new StoredClientProfile(
                profile.Server,
                profile.Port,
                profile.UseTls,
                profile.CertificateSha256,
                profile.DeviceId,
                Convert.ToBase64String(protectedToken));
            var json = JsonSerializer.Serialize(stored, JsonOptions);

            Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath)!);
            var temporaryPath = ProfilePath + ".jmnew-" + Guid.NewGuid().ToString("N");
            try
            {
                await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
                File.Move(temporaryPath, ProfilePath, overwrite: true);
            }
            finally
            {
                File.Delete(temporaryPath);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainToken);
            if (protectedToken is not null)
            {
                CryptographicOperations.ZeroMemory(protectedToken);
            }
        }
    }

    public static async Task<ClientProfile?> TryLoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(ProfilePath))
        {
            return null;
        }

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "The encrypted client profile is supported on Windows only.");
        }

        var json = await File.ReadAllTextAsync(ProfilePath, cancellationToken);
        var stored = JsonSerializer.Deserialize<StoredClientProfile>(json, JsonOptions)
                     ?? throw new InvalidDataException("The client profile is empty.");

        byte[]? protectedToken = null;
        byte[]? plainToken = null;
        try
        {
            protectedToken = Convert.FromBase64String(stored.ProtectedToken);
            plainToken = ProtectedData.Unprotect(
                protectedToken,
                Entropy,
                DataProtectionScope.CurrentUser);
            var profile = new ClientProfile(
                stored.Server,
                stored.Port,
                stored.UseTls,
                stored.CertificateSha256,
                stored.DeviceId,
                Encoding.UTF8.GetString(plainToken));
            Validate(profile);
            return profile;
        }
        finally
        {
            if (protectedToken is not null)
            {
                CryptographicOperations.ZeroMemory(protectedToken);
            }

            if (plainToken is not null)
            {
                CryptographicOperations.ZeroMemory(plainToken);
            }
        }
    }

    private static void Validate(ClientProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Server) ||
            profile.Port is < 1 or > 65535 ||
            profile.DeviceId == Guid.Empty ||
            string.IsNullOrWhiteSpace(profile.Token))
        {
            throw new InvalidDataException("The client profile contains invalid connection credentials.");
        }

        if (!profile.UseTls)
        {
            throw new InvalidDataException("JMServer client profiles require TLS.");
        }

        try
        {
            if (string.IsNullOrWhiteSpace(profile.CertificateSha256) ||
                Convert.FromHexString(profile.CertificateSha256).Length != 32)
            {
                throw new InvalidDataException("The client profile requires a SHA-256 certificate pin.");
            }
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException(
                "The client profile certificate pin is not valid hexadecimal.",
                exception);
        }
    }

    private sealed record StoredClientProfile(
        string Server,
        int Port,
        bool UseTls,
        string? CertificateSha256,
        Guid DeviceId,
        string ProtectedToken);
}

public sealed record ClientProfile(
    string Server,
    int Port,
    bool UseTls,
    string? CertificateSha256,
    Guid DeviceId,
    string Token);
