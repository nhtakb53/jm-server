using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace JmServer.GameIntegration;

internal static class JmSupplyModPackage
{
    private const string ResourcePrefix = "JmServer.GameIntegration.ModData.";
    private const string ManifestResourceName = ResourcePrefix + "package-manifest.json";

    private static readonly Lazy<SupplyPackageManifest> PackageManifest = new(LoadManifest);

    public static SupplyPackageManifest Manifest => PackageManifest.Value;

    public static async Task WriteToDirectoryAsync(
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        destinationDirectory = Path.GetFullPath(destinationDirectory);
        foreach (var file in Manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = ResolveChildPath(destinationDirectory, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var source = OpenFile(file.RelativePath);
            await using var output = new FileStream(
                destination,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                useAsync: true);
            await source.CopyToAsync(output, cancellationToken);
        }
    }

    public static async Task<LoaderVerificationResult> VerifyAsync(
        string modDirectory,
        CancellationToken cancellationToken = default)
    {
        modDirectory = Path.GetFullPath(modDirectory);
        if (Manifest.GameVersion != D2RClientLayout.SupportedGameVersion)
        {
            return LoaderVerificationResult.Failure(
                $"Bundled supply data targets D2R {Manifest.GameVersion}, not " +
                $"{D2RClientLayout.SupportedGameVersion}.");
        }

        foreach (var expected in Manifest.Files)
        {
            var path = ResolveChildPath(modDirectory, expected.RelativePath);
            if (!File.Exists(path))
            {
                return LoaderVerificationResult.Failure(
                    $"The in-game supply mod is missing '{expected.RelativePath}'.");
            }

            await using var stream = File.OpenRead(path);
            var actualHash = await SHA256.HashDataAsync(stream, cancellationToken);
            if (!CryptographicOperations.FixedTimeEquals(
                    actualHash,
                    Convert.FromHexString(expected.Sha256)))
            {
                return LoaderVerificationResult.Failure(
                    $"The in-game supply mod file '{expected.RelativePath}' was changed.");
            }
        }

        return LoaderVerificationResult.Success(
            $"In-game supply catalog is valid: {Manifest.UniqueItemCount} uniques, " +
            $"{Manifest.SetItemCount} sets, {Manifest.BaseSelectorCount} bases, " +
            $"{Manifest.MaterialSelectorCount} materials, {Manifest.CharmSelectorCount} charm selectors, " +
            $"{Manifest.QuickCraftRecipeCount} quick crafts and " +
            $"{Manifest.WorkbenchRecipeCount} workbench recipes.");
    }

    private static SupplyPackageManifest LoadManifest()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
                               ManifestResourceName)
                           ?? throw new InvalidDataException(
                               "The embedded in-game supply manifest is missing.");
        var manifest = JsonSerializer.Deserialize<SupplyPackageManifest>(stream)
                       ?? throw new InvalidDataException(
                           "The embedded in-game supply manifest is invalid.");
        if (manifest.SchemaVersion != InGameSupplyModBuilder.PackageSchemaVersion ||
            manifest.Files.Count == 0 ||
            manifest.BaseSelectorCount == 0 || manifest.MaterialSelectorCount == 0 ||
            manifest.CharmSelectorCount == 0 || manifest.ControlTokenCount == 0 ||
            manifest.CustomItemCount == 0 || manifest.WorkbenchRecipeCount == 0 ||
            manifest.QuickCraftRecipeCount != InGameSupplyModBuilder.ExpectedVanillaCraftRecipeCount ||
            manifest.Files.Any(file =>
                string.IsNullOrWhiteSpace(file.RelativePath) ||
                file.Sha256.Length != 64))
        {
            throw new InvalidDataException("The embedded in-game supply manifest failed validation.");
        }

        _ = manifest.Files
            .Select(file => ResolveChildPath("C:\\jm-package-root", file.RelativePath))
            .ToArray();
        return manifest;
    }

    internal static Stream OpenFile(string relativePath)
    {
        var resourceName = ResourcePrefix + relativePath.Replace('/', '.').Replace('\\', '.');
        return Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
               ?? throw new InvalidDataException(
                   $"Embedded supply resource '{relativePath}' is missing.");
    }

    private static string ResolveChildPath(string rootDirectory, string relativePath)
    {
        if (Path.IsPathRooted(relativePath) ||
            relativePath.Split('/', '\\').Any(part => part is "" or "." or ".."))
        {
            throw new InvalidDataException(
                $"Supply package path '{relativePath}' is not a safe relative path.");
        }

        rootDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
        var result = Path.GetFullPath(Path.Combine(
            rootDirectory,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!result.StartsWith(
                rootDirectory + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Supply package path '{relativePath}' escaped its destination.");
        }

        return result;
    }
}
