using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JmServer.GameIntegration;

public static partial class D2RLoaderInstaller
{
    private const string ConfigurationRelativePath = "d2rloader/config/d2rloader.toml";
    private const string ModInfoRelativePath = "mods/JMServer/JMServer.mpq/modinfo.json";
    private const string DisableDebugRelativePath =
        "mods/JMServer/JMServer.mpq/data/global/.DISABLE_DEBUG";
    private const string ModDirectoryRelativePath = "mods/JMServer/JMServer.mpq";
    private static readonly string[] ObsoleteModRelativePaths =
    [
        "mods/JMServer/JMServer.mpq/data/local/lng/strings/jmserver.json",
        "mods/JMServer/JMServer.mpq/data/global/ui/layouts/vendorpanellayouthd.json",
        "mods/JMServer/JMServer.mpq/data/global/ui/layouts/controller/vendorpanellayouthd.json"
    ];

    public static async Task<LoaderInstallationResult> InstallAsync(
        string archivePath,
        string gameDirectory,
        CancellationToken cancellationToken = default)
    {
        archivePath = Path.GetFullPath(archivePath);
        gameDirectory = NormalizeGameDirectory(gameDirectory);
        EnsureGameIsNotRunning();

        var gameVersion = ReadAndValidateGameVersion(gameDirectory);
        var verification = await D2RLoaderVerifier.VerifyReleaseArchiveAsync(
            archivePath,
            cancellationToken);
        if (!verification.IsValid)
        {
            throw new InvalidDataException(verification.Message);
        }

        var stagingDirectory = Path.Combine(
            Path.GetTempPath(),
            "jm-server-loader-" + Guid.NewGuid().ToString("N"));
        var backupDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JM Server",
            "loader-backups",
            DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(stagingDirectory);
        var appliedChanges = new List<AppliedFileChange>();
        try
        {
            ZipFile.ExtractToDirectory(archivePath, stagingDirectory);
            var stagedConfiguration = Path.Combine(
                stagingDirectory,
                ConfigurationRelativePath.Replace('/', Path.DirectorySeparatorChar));
            var configuration = await File.ReadAllTextAsync(stagedConfiguration, cancellationToken);
            configuration = CreateRestrictedConfiguration(configuration);
            await File.WriteAllTextAsync(stagedConfiguration, configuration, cancellationToken);

            var stagedModInfo = Path.Combine(
                stagingDirectory,
                ModInfoRelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(stagedModInfo)!);
            await File.WriteAllTextAsync(
                stagedModInfo,
                "{\n  \"name\": \"JMServer\",\n  \"savepath\": \"JMServer/\"\n}\n",
                cancellationToken);

            var stagedDisableDebug = Path.Combine(
                stagingDirectory,
                DisableDebugRelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(stagedDisableDebug)!);
            await File.WriteAllBytesAsync(stagedDisableDebug, [], cancellationToken);

            var stagedModDirectory = Path.Combine(
                stagingDirectory,
                ModDirectoryRelativePath.Replace('/', Path.DirectorySeparatorChar));
            await JmSupplyModPackage.WriteToDirectoryAsync(stagedModDirectory, cancellationToken);

            var relativeFiles = new List<string>
            {
                "D2RLoader.exe",
                "D2RCore.dll",
                ConfigurationRelativePath,
                ModInfoRelativePath,
                DisableDebugRelativePath
            };
            relativeFiles.AddRange(JmSupplyModPackage.Manifest.Files.Select(
                file => ModDirectoryRelativePath + "/" + file.RelativePath));

            foreach (var relativeFile in relativeFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var platformRelativePath = relativeFile.Replace('/', Path.DirectorySeparatorChar);
                var sourcePath = Path.Combine(stagingDirectory, platformRelativePath);
                var destinationPath = Path.Combine(gameDirectory, platformRelativePath);
                var existed = File.Exists(destinationPath);
                string? backupPath = null;

                if (existed && await FilesHaveSameContentAsync(
                        sourcePath,
                        destinationPath,
                        cancellationToken))
                {
                    continue;
                }

                if (existed)
                {
                    backupPath = Path.Combine(backupDirectory, platformRelativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                    File.Copy(destinationPath, backupPath, overwrite: false);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                var temporaryDestination = destinationPath + ".jmnew-" + Guid.NewGuid().ToString("N");
                try
                {
                    File.Copy(sourcePath, temporaryDestination, overwrite: false);
                    try
                    {
                        File.Move(temporaryDestination, destinationPath, overwrite: true);
                    }
                    catch (UnauthorizedAccessException exception)
                    {
                        throw new UnauthorizedAccessException(
                            $"Could not replace '{destinationPath}'. Close D2R/D2RLoader and " +
                            "run the launcher with permission to modify the game directory.",
                            exception);
                    }
                }
                finally
                {
                    File.Delete(temporaryDestination);
                }

                appliedChanges.Add(new AppliedFileChange(destinationPath, existed, backupPath));
            }

            foreach (var obsoleteRelativePath in ObsoleteModRelativePaths)
            {
                var obsoletePath = Path.Combine(
                    gameDirectory,
                    obsoleteRelativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(obsoletePath))
                {
                    continue;
                }

                var obsoleteBackupPath = Path.Combine(
                    backupDirectory,
                    obsoleteRelativePath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(obsoleteBackupPath)!);
                File.Copy(obsoletePath, obsoleteBackupPath, overwrite: false);
                File.Delete(obsoletePath);
                appliedChanges.Add(new AppliedFileChange(
                    obsoletePath,
                    Existed: true,
                    BackupPath: obsoleteBackupPath));
            }

            var installedVerification = await VerifyInstallationAsync(gameDirectory, cancellationToken);
            if (!installedVerification.IsValid)
            {
                throw new InvalidDataException(installedVerification.Message);
            }

            await D2RModSettings.EnsureInitializedAsync(cancellationToken: cancellationToken);

            return new LoaderInstallationResult(
                gameDirectory,
                gameVersion,
                D2RLoaderVerifier.Version,
                appliedChanges.Any(change => change.Existed) ? backupDirectory : null,
                JmSupplyModPackage.Manifest.UniqueItemCount,
                JmSupplyModPackage.Manifest.SetItemCount,
                JmSupplyModPackage.Manifest.BaseSelectorCount,
                JmSupplyModPackage.Manifest.MaterialSelectorCount,
                JmSupplyModPackage.Manifest.CharmSelectorCount,
                JmSupplyModPackage.Manifest.ControlTokenCount,
                JmSupplyModPackage.Manifest.WorkbenchRecipeCount,
                JmSupplyModPackage.Manifest.QuickCraftRecipeCount);
        }
        catch
        {
            RollBack(appliedChanges);
            throw;
        }
        finally
        {
            Directory.Delete(stagingDirectory, recursive: true);
        }
    }

    private static async Task<bool> FilesHaveSameContentAsync(
        string firstPath,
        string secondPath,
        CancellationToken cancellationToken)
    {
        if (new FileInfo(firstPath).Length != new FileInfo(secondPath).Length)
        {
            return false;
        }

        await using var first = new FileStream(
            firstPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        await using var second = new FileStream(
            secondPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        var firstHash = await SHA256.HashDataAsync(first, cancellationToken);
        var secondHash = await SHA256.HashDataAsync(second, cancellationToken);
        return CryptographicOperations.FixedTimeEquals(firstHash, secondHash);
    }

    public static async Task<LoaderVerificationResult> VerifyInstallationAsync(
        string gameDirectory,
        CancellationToken cancellationToken = default)
    {
        try
        {
            gameDirectory = NormalizeGameDirectory(gameDirectory);
            ReadAndValidateGameVersion(gameDirectory);

            var requiredHashes = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["D2RLoader.exe"] = D2RLoaderVerifier.LoaderExecutableSha256,
                ["D2RCore.dll"] = D2RLoaderVerifier.CoreLibrarySha256
            };

            foreach (var expected in requiredHashes)
            {
                var path = Path.Combine(gameDirectory, expected.Key);
                if (!File.Exists(path))
                {
                    return LoaderVerificationResult.Failure($"Installed loader is missing {expected.Key}.");
                }

                await using var stream = File.OpenRead(path);
                var actualHash = await SHA256.HashDataAsync(stream, cancellationToken);
                if (!CryptographicOperations.FixedTimeEquals(
                        actualHash,
                        Convert.FromHexString(expected.Value)))
                {
                    return LoaderVerificationResult.Failure(
                        $"Installed {expected.Key} does not match the pinned release.");
                }
            }

            var configurationPath = Path.Combine(
                gameDirectory,
                ConfigurationRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(configurationPath))
            {
                return LoaderVerificationResult.Failure("The restricted D2RLoader configuration is missing.");
            }

            var configuration = await File.ReadAllTextAsync(configurationPath, cancellationToken);
            if (!HasRestrictedConfiguration(configuration))
            {
                return LoaderVerificationResult.Failure(
                    "D2RLoader settings are not locked to the restricted JMServer profile.");
            }

            var modInfoPath = Path.Combine(
                gameDirectory,
                ModInfoRelativePath.Replace('/', Path.DirectorySeparatorChar));
            var disableDebugPath = Path.Combine(
                gameDirectory,
                DisableDebugRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(modInfoPath) || !File.Exists(disableDebugPath))
            {
                return LoaderVerificationResult.Failure(
                    "The isolated JMServer save profile or debug-disable marker is missing.");
            }

            using var modInfo = JsonDocument.Parse(
                await File.ReadAllTextAsync(modInfoPath, cancellationToken));
            if (!modInfo.RootElement.TryGetProperty("name", out var name) ||
                name.GetString() != D2RClientLayout.ModName ||
                !modInfo.RootElement.TryGetProperty("savepath", out var savePath) ||
                savePath.GetString() != D2RClientLayout.ModName + "/")
            {
                return LoaderVerificationResult.Failure("JMServer modinfo.json is not restricted as expected.");
            }

            var supplyVerification = await JmSupplyModPackage.VerifyAsync(
                Path.Combine(
                    gameDirectory,
                    ModDirectoryRelativePath.Replace('/', Path.DirectorySeparatorChar)),
                cancellationToken);
            if (!supplyVerification.IsValid)
            {
                return supplyVerification;
            }

            foreach (var obsoleteRelativePath in ObsoleteModRelativePaths)
            {
                var obsoletePath = Path.Combine(
                    gameDirectory,
                    obsoleteRelativePath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(obsoletePath))
                {
                    return LoaderVerificationResult.Failure(
                        $"The obsolete JMServer mod file '{obsoleteRelativePath}' is still installed.");
                }
            }

            return LoaderVerificationResult.Success(
                $"D2R {D2RClientLayout.SupportedGameVersion} and restricted D2RLoader " +
                $"{D2RLoaderVerifier.Version} installation are valid; " +
                $"the in-game catalog contains {JmSupplyModPackage.Manifest.UniqueItemCount} uniques and " +
                $"{JmSupplyModPackage.Manifest.SetItemCount} sets, " +
                $"{JmSupplyModPackage.Manifest.BaseSelectorCount} bases, " +
                $"{JmSupplyModPackage.Manifest.MaterialSelectorCount} materials, " +
                $"{JmSupplyModPackage.Manifest.CharmSelectorCount} charm selectors, " +
                $"{JmSupplyModPackage.Manifest.QuickCraftRecipeCount} quick crafts and " +
                $"{JmSupplyModPackage.Manifest.WorkbenchRecipeCount} workbench recipes.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          InvalidDataException or JsonException)
        {
            return LoaderVerificationResult.Failure(exception.Message);
        }
    }

    internal static string CreateRestrictedConfiguration(string configuration)
    {
        configuration = ReplaceSetting(configuration, "default_mod", "\"JMServer\"");
        configuration = ReplaceSetting(configuration, "show_tcpip_button", "true");
        configuration = ReplaceSetting(configuration, "allow_global_extensions", "false");
        configuration = ReplaceSetting(configuration, "allow_mod_extensions", "false");
        configuration = ReplaceSetting(configuration, "monitor_startup_crashes", "false");
        configuration = ReplaceSetting(configuration, "write_crash_dumps", "false");
        configuration = ReplaceSetting(configuration, "enable_console", "false");
        configuration = ReplaceSetting(
            configuration,
            "set_materials_limit",
            D2SharedStashGold.MaximumAdvancedStackSize.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
        return configuration;
    }

    internal static bool HasRestrictedConfiguration(string configuration) =>
        HasSetting(configuration, "default_mod", "\"JMServer\"") &&
        HasSetting(configuration, "show_tcpip_button", "true") &&
        HasSetting(configuration, "allow_global_extensions", "false") &&
        HasSetting(configuration, "allow_mod_extensions", "false") &&
        HasSetting(configuration, "monitor_startup_crashes", "false") &&
        HasSetting(configuration, "write_crash_dumps", "false") &&
        HasSetting(configuration, "enable_console", "false") &&
        HasSettingInSection(
            configuration,
            "d2rcore.stash",
            "set_materials_limit",
            D2SharedStashGold.MaximumAdvancedStackSize.ToString(
                System.Globalization.CultureInfo.InvariantCulture));

    public static void EnsureGameIsNotRunning()
    {
        EnsureProcessesAreNotRunning(["D2R", "D2RLoader"]);
    }

    public static void EnsurePrivateSessionProcessesAreStopped()
    {
        EnsureProcessesAreNotRunning(
            ["D2R", "D2RLoader", "Battle.net", "Agent", "Diablo II Resurrected Launcher"]);
    }

    private static void EnsureProcessesAreNotRunning(IEnumerable<string> processNames)
    {
        var runningNames = new List<string>();
        foreach (var processName in processNames)
        {
            var processes = Process.GetProcessesByName(processName);
            try
            {
                if (processes.Length != 0)
                {
                    runningNames.Add(processName);
                }
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
        }

        if (runningNames.Count != 0)
        {
            throw new InvalidOperationException(
                "Close these processes before continuing: " +
                string.Join(", ", runningNames));
        }
    }

    private static string NormalizeGameDirectory(string gameDirectory)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory))
        {
            throw new ArgumentException("A D2R game directory is required.", nameof(gameDirectory));
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameDirectory));
    }

    private static string ReadAndValidateGameVersion(string gameDirectory)
    {
        var executablePath = Path.Combine(gameDirectory, "D2R.exe");
        if (!File.Exists(executablePath))
        {
            throw new InvalidDataException($"D2R.exe was not found in '{gameDirectory}'.");
        }

        var gameVersion = FileVersionInfo.GetVersionInfo(executablePath).FileVersion ?? "unknown";
        if (!string.Equals(
                gameVersion,
                D2RClientLayout.SupportedGameVersion,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"D2R version '{gameVersion}' is not the pinned " +
                $"{D2RClientLayout.SupportedGameVersion} build.");
        }

        return gameVersion;
    }

    private static string ReplaceSetting(string configuration, string key, string value)
    {
        var regex = new Regex(
            $"(?m)^(?<indent>\\s*){Regex.Escape(key)}\\s*=\\s*[^#\\r\\n]*(?<comment>\\s*#.*)?$",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
        var matches = regex.Matches(configuration);
        if (matches.Count != 1)
        {
            throw new InvalidDataException(
                $"Expected exactly one '{key}' setting in D2RLoader configuration.");
        }

        return regex.Replace(
            configuration,
            match => $"{match.Groups["indent"].Value}{key} = {value}{match.Groups["comment"].Value}",
            count: 1);
    }

    private static bool HasSetting(string configuration, string key, string value) =>
        Regex.IsMatch(
            configuration,
            $"(?m)^\\s*{Regex.Escape(key)}\\s*=\\s*{Regex.Escape(value)}\\s*(?:#.*)?$",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

    private static bool HasSettingInSection(
        string configuration,
        string section,
        string key,
        string value)
    {
        var settingRegex = new Regex(
            $"(?m)^[ \\t]*{Regex.Escape(key)}[ \\t]*=[ \\t]*{Regex.Escape(value)}[ \\t]*(?:#[^\\r\\n]*)?$",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
        var settingMatches = settingRegex.Matches(configuration);
        if (settingMatches.Count != 1)
        {
            return false;
        }

        var sectionRegex = new Regex(
            "(?m)^[ \\t]*\\[(?<name>[^]\\r\\n]+)\\][ \\t]*(?:#[^\\r\\n]*)?$",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
        var sections = sectionRegex.Matches(configuration);
        var settingIndex = settingMatches[0].Index;
        string? containingSection = null;
        foreach (Match sectionMatch in sections)
        {
            if (sectionMatch.Index > settingIndex)
            {
                break;
            }

            containingSection = sectionMatch.Groups["name"].Value;
        }

        return string.Equals(containingSection, section, StringComparison.Ordinal);
    }

    private static void RollBack(IEnumerable<AppliedFileChange> appliedChanges)
    {
        foreach (var change in appliedChanges.Reverse())
        {
            if (change.Existed && change.BackupPath is not null)
            {
                File.Copy(change.BackupPath, change.DestinationPath, overwrite: true);
            }
            else
            {
                File.Delete(change.DestinationPath);
            }
        }
    }

    private sealed record AppliedFileChange(
        string DestinationPath,
        bool Existed,
        string? BackupPath);
}

public sealed record LoaderInstallationResult(
    string GameDirectory,
    string GameVersion,
    string LoaderVersion,
    string? BackupDirectory,
    int SupplyUniqueItemCount,
    int SupplySetItemCount,
    int BaseSelectorCount,
    int MaterialSelectorCount,
    int CharmSelectorCount,
    int ControlTokenCount,
    int WorkbenchRecipeCount,
    int QuickCraftRecipeCount);
