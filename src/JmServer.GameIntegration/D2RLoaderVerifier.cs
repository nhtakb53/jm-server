using System.IO.Compression;
using System.Security.Cryptography;

namespace JmServer.GameIntegration;

public static class D2RLoaderVerifier
{
    public const string Version = "1.0.1-beta";
    public const string ReleaseArchiveSha256 =
        "989D1D30F54324DAF3C68C8F28E25B329CBCA7C77E7C87C7E0EBE4B0F75F3B9F";
    public const string LoaderExecutableSha256 =
        "0FE9C136673BD6F5EC53A062A1A9782DA2B18E6C65B3BFCA8460EE2D93A96A6B";
    public const string CoreLibrarySha256 =
        "151242286E64A0A98502726571646809B562D4A0B39A45EEE243E0EA200AEA76";

    private static readonly IReadOnlyDictionary<string, string> ExpectedFileHashes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["D2RLoader.exe"] = LoaderExecutableSha256,
            ["D2RCore.dll"] = CoreLibrarySha256
        };

    public static async Task<LoaderVerificationResult> VerifyReleaseArchiveAsync(
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(archivePath))
        {
            return LoaderVerificationResult.Failure($"Loader archive does not exist: {archivePath}");
        }

        await using (var stream = File.OpenRead(archivePath))
        {
            var archiveHash = await SHA256.HashDataAsync(stream, cancellationToken);
            if (!CryptographicOperations.FixedTimeEquals(
                    archiveHash,
                    Convert.FromHexString(ReleaseArchiveSha256)))
            {
                return LoaderVerificationResult.Failure(
                    $"Archive SHA-256 does not match the pinned D2RLoader {Version} release.");
            }
        }

        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            if (!IsSafeRelativePath(entry.FullName))
            {
                return LoaderVerificationResult.Failure(
                    $"Archive contains an unsafe path: {entry.FullName}");
            }
        }

        foreach (var expected in ExpectedFileHashes)
        {
            var entry = archive.GetEntry(expected.Key);
            if (entry is null)
            {
                return LoaderVerificationResult.Failure(
                    $"Archive is missing required file {expected.Key}.");
            }

            await using var stream = entry.Open();
            var actualHash = await SHA256.HashDataAsync(stream, cancellationToken);
            if (!CryptographicOperations.FixedTimeEquals(
                    actualHash,
                    Convert.FromHexString(expected.Value)))
            {
                return LoaderVerificationResult.Failure(
                    $"{expected.Key} SHA-256 does not match the pinned release.");
            }
        }

        if (archive.GetEntry("d2rloader/config/d2rloader.toml") is null)
        {
            return LoaderVerificationResult.Failure(
                "Archive is missing d2rloader/config/d2rloader.toml.");
        }

        return LoaderVerificationResult.Success(
            $"D2RLoader {Version} archive and required file hashes are valid.");
    }

    internal static bool IsSafeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
        {
            return false;
        }

        var segments = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.All(segment => segment is not "." and not "..");
    }
}

public sealed record LoaderVerificationResult(bool IsValid, string Message)
{
    public static LoaderVerificationResult Success(string message) => new(true, message);

    public static LoaderVerificationResult Failure(string message) => new(false, message);
}
