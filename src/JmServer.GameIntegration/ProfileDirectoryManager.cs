using JmServer.Domain;

namespace JmServer.GameIntegration;

public sealed record InstalledProfile(
    IReadOnlyList<string> RegisteredCharacterNames,
    string? QuarantineDirectory);

public sealed record CollectedProfile(
    byte[] BundleData,
    string? QuarantineDirectory);

public static class ProfileDirectoryManager
{
    public static async Task<InstalledProfile> InstallCheckoutAsync(
        ReadOnlyMemory<byte> bundleData,
        string? saveDirectory = null,
        string? quarantineRoot = null,
        CancellationToken cancellationToken = default)
    {
        var files = ProfileBundleCodec.Decode(bundleData.Span);
        var registeredNames = files
            .Where(file => ProfileSavePolicy.IsCharacterSave(file.RelativePath))
            .Select(file => Path.GetFileNameWithoutExtension(file.RelativePath))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (registeredNames.Length == 0)
        {
            throw new InvalidDataException("Server profile does not contain a character save.");
        }

        saveDirectory = ResolveSaveDirectory(saveDirectory);
        Directory.CreateDirectory(saveDirectory);
        var existing = Directory.EnumerateFiles(saveDirectory, "*", SearchOption.TopDirectoryOnly)
            .Where(path => ProfileSavePolicy.IsManagedFileName(Path.GetFileName(path)))
            .ToArray();
        var registeredNameSet = registeredNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var localControlFiles = existing
            .Where(path =>
            {
                var fileName = Path.GetFileName(path);
                return ProfileSavePolicy.IsCharacterControl(fileName) &&
                       IsRegisteredCharacterFile(fileName, registeredNameSet);
            })
            .Select(path => new ProfileFile(Path.GetFileName(path), File.ReadAllBytes(path)))
            .ToArray();
        var quarantineDirectory = Quarantine(existing, quarantineRoot);
        var installedPaths = new List<string>();
        try
        {
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = ResolveChildPath(saveDirectory, file.RelativePath);
                var temporary = destination + ".jmdownload-" + Guid.NewGuid().ToString("N");
                try
                {
                    await File.WriteAllBytesAsync(temporary, file.Data, cancellationToken);
                    File.Move(temporary, destination, overwrite: false);
                    installedPaths.Add(destination);
                }
                finally
                {
                    File.Delete(temporary);
                }
            }

            foreach (var file in localControlFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = ResolveChildPath(saveDirectory, file.RelativePath);
                var temporary = destination + ".jmlocal-" + Guid.NewGuid().ToString("N");
                try
                {
                    await File.WriteAllBytesAsync(temporary, file.Data, cancellationToken);
                    File.Move(temporary, destination, overwrite: true);
                    if (!installedPaths.Contains(destination, StringComparer.OrdinalIgnoreCase))
                    {
                        installedPaths.Add(destination);
                    }
                }
                finally
                {
                    File.Delete(temporary);
                }
            }
        }
        catch
        {
            foreach (var installedPath in installedPaths)
            {
                File.Delete(installedPath);
            }

            RestoreQuarantine(quarantineDirectory, saveDirectory);
            throw;
        }

        return new InstalledProfile(registeredNames, quarantineDirectory);
    }

    public static CollectedProfile CollectForCheckin(
        IEnumerable<string> registeredCharacterNames,
        string? saveDirectory = null,
        string? quarantineRoot = null)
    {
        ArgumentNullException.ThrowIfNull(registeredCharacterNames);
        saveDirectory = ResolveSaveDirectory(saveDirectory);
        var registered = registeredCharacterNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (registered.Count == 0)
        {
            throw new InvalidDataException("Profile lease does not contain registered characters.");
        }

        var managedPaths = Directory.Exists(saveDirectory)
            ? Directory.EnumerateFiles(saveDirectory, "*", SearchOption.TopDirectoryOnly)
                .Where(path => ProfileSavePolicy.IsManagedFileName(Path.GetFileName(path)))
                .ToArray()
            : [];
        var unexpected = managedPaths
            .Where(path =>
            {
                var name = Path.GetFileName(path);
                return !ProfileSavePolicy.IsSharedStash(name) &&
                       !IsRegisteredCharacterFile(name, registered);
            })
            .ToArray();
        var quarantineDirectory = Quarantine(unexpected, quarantineRoot);
        var remaining = managedPaths.Except(unexpected, StringComparer.OrdinalIgnoreCase).ToArray();
        var presentCharacters = remaining
            .Select(path => Path.GetFileName(path))
            .Where(name => name is not null && ProfileSavePolicy.IsCharacterSave(name))
            .Select(name => Path.GetFileNameWithoutExtension(name!))
            .Where(name => name is not null)
            .Select(name => name!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!registered.SetEquals(presentCharacters))
        {
            throw new InvalidDataException(
                "A server-registered character save is missing; the profile was kept for recovery.");
        }

        var files = remaining
            .Select(path => new ProfileFile(Path.GetFileName(path), File.ReadAllBytes(path)))
            .ToArray();
        return new CollectedProfile(ProfileBundleCodec.Encode(files), quarantineDirectory);
    }

    public static void RemoveCheckedInProfile(string? saveDirectory = null)
    {
        saveDirectory = ResolveSaveDirectory(saveDirectory);
        if (!Directory.Exists(saveDirectory))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(saveDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            if (ProfileSavePolicy.IsManagedFileName(Path.GetFileName(path)))
            {
                File.Delete(ResolveChildPath(saveDirectory, Path.GetFileName(path)));
            }
        }
    }

    public static string? QuarantineManagedProfile(
        string? saveDirectory = null,
        string? quarantineRoot = null,
        IEnumerable<string>? additionalPaths = null)
    {
        saveDirectory = ResolveSaveDirectory(saveDirectory);
        var paths = Directory.Exists(saveDirectory)
            ? Directory.EnumerateFiles(saveDirectory, "*", SearchOption.TopDirectoryOnly)
                .Where(path => ProfileSavePolicy.IsManagedFileName(Path.GetFileName(path)))
                .ToList()
            : [];

        if (additionalPaths is not null)
        {
            foreach (var additionalPath in additionalPaths)
            {
                var path = Path.GetFullPath(additionalPath);
                if (!string.Equals(
                        Path.GetDirectoryName(path),
                        saveDirectory,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "An additional quarantine file must be a direct child of the save directory.");
                }

                if (File.Exists(path))
                {
                    paths.Add(path);
                }
            }
        }

        return Quarantine(
            paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            quarantineRoot);
    }

    private static string ResolveSaveDirectory(string? saveDirectory) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(
            saveDirectory ?? D2RClientLayout.GetModSaveDirectory()));

    private static bool IsRegisteredCharacterFile(
        string fileName,
        IReadOnlySet<string> registeredCharacterNames)
    {
        if (ProfileSavePolicy.IsSharedStash(fileName))
        {
            return false;
        }

        return registeredCharacterNames.Any(
            characterName => ProfileSavePolicy.BelongsToCharacter(fileName, characterName));
    }

    private static string ResolveChildPath(string directory, string fileName)
    {
        if (!string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal))
        {
            throw new InvalidDataException("Profile file path must be a direct child of the save directory.");
        }

        var path = Path.GetFullPath(Path.Combine(directory, fileName));
        var prefix = directory + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Profile file escaped the save directory.");
        }

        return path;
    }

    private static string? Quarantine(IReadOnlyList<string> paths, string? quarantineRoot)
    {
        if (paths.Count == 0)
        {
            return null;
        }

        quarantineRoot = Path.GetFullPath(quarantineRoot ?? GetDefaultQuarantineRoot());
        var destination = Path.Combine(
            quarantineRoot,
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(destination);
        var moved = new List<(string Source, string Target)>();
        try
        {
            foreach (var path in paths)
            {
                var source = Path.GetFullPath(path);
                var target = Path.Combine(destination, Path.GetFileName(source));
                File.Move(source, target, overwrite: false);
                moved.Add((source, target));
            }
        }
        catch
        {
            foreach (var move in moved.AsEnumerable().Reverse())
            {
                if (File.Exists(move.Target) && !File.Exists(move.Source))
                {
                    File.Move(move.Target, move.Source, overwrite: false);
                }
            }

            if (!Directory.EnumerateFileSystemEntries(destination).Any())
            {
                Directory.Delete(destination);
            }

            throw;
        }

        return destination;
    }

    private static void RestoreQuarantine(string? quarantineDirectory, string saveDirectory)
    {
        if (quarantineDirectory is null || !Directory.Exists(quarantineDirectory))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(quarantineDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            File.Move(path, ResolveChildPath(saveDirectory, Path.GetFileName(path)), overwrite: false);
        }

        Directory.Delete(quarantineDirectory);
    }

    private static string GetDefaultQuarantineRoot()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(local))
        {
            throw new InvalidOperationException("The current Windows local application data folder was not found.");
        }

        return Path.Combine(local, "JM Server", "quarantine");
    }
}
