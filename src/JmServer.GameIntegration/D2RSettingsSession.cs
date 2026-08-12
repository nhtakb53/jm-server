using System.Text.Json;

namespace JmServer.GameIntegration;

public sealed class D2RSettingsSession
{
    private const string MetadataFileName = "session.json";
    private const string OriginalFileName = "original-settings.json";
    private const string CapturedFileName = "captured-settings.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _baseSaveDirectory;
    private readonly string _modSaveDirectory;
    private readonly string _sessionDirectory;
    private bool _completed;

    private D2RSettingsSession(
        string baseSaveDirectory,
        string modSaveDirectory,
        string sessionDirectory)
    {
        _baseSaveDirectory = baseSaveDirectory;
        _modSaveDirectory = modSaveDirectory;
        _sessionDirectory = sessionDirectory;
    }

    public static async Task<D2RSettingsSession> BeginAsync(
        string? baseSaveDirectory = null,
        string? modSaveDirectory = null,
        string? sessionDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var session = Create(baseSaveDirectory, modSaveDirectory, sessionDirectory);
        await RecoverInterruptedAsync(
            session._baseSaveDirectory,
            session._modSaveDirectory,
            session._sessionDirectory,
            cancellationToken);

        var privateSettings = await D2RModSettings.ReadForSyncAsync(
                                  session._modSaveDirectory,
                                  cancellationToken)
                              ?? throw new FileNotFoundException(
                                  "The JM Server settings file is missing.",
                                  D2RModSettings.GetSettingsPath(session._modSaveDirectory));
        var baseSettingsPath = GetBaseSettingsPath(session._baseSaveDirectory);
        var originalExists = File.Exists(baseSettingsPath);

        session.DeleteSessionDirectory();
        Directory.CreateDirectory(session._sessionDirectory);
        if (originalExists)
        {
            File.Copy(baseSettingsPath, session.OriginalSettingsPath, overwrite: true);
        }

        await session.WriteMetadataAsync(
            new SettingsSessionMetadata(originalExists, GameSettingsActivated: false),
            cancellationToken);
        await WriteSettingsAtomicallyAsync(
            baseSettingsPath,
            privateSettings,
            cancellationToken);
        await session.WriteMetadataAsync(
            new SettingsSessionMetadata(originalExists, GameSettingsActivated: true),
            cancellationToken);
        return session;
    }

    public static async Task RecoverInterruptedAsync(
        string? baseSaveDirectory = null,
        string? modSaveDirectory = null,
        string? sessionDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var session = Create(baseSaveDirectory, modSaveDirectory, sessionDirectory);
        if (!File.Exists(session.MetadataPath))
        {
            return;
        }

        var metadata = await session.ReadMetadataAsync(cancellationToken);
        await session.CaptureAndRestoreAsync(metadata, cancellationToken);
        session.DeleteSessionDirectory();
        session._completed = true;
    }

    public async Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        if (_completed)
        {
            return;
        }

        var metadata = await ReadMetadataAsync(cancellationToken);
        await CaptureAndRestoreAsync(metadata, cancellationToken);
        DeleteSessionDirectory();
        _completed = true;
    }

    private string MetadataPath => Path.Combine(_sessionDirectory, MetadataFileName);

    private string OriginalSettingsPath => Path.Combine(_sessionDirectory, OriginalFileName);

    private string CapturedSettingsPath => Path.Combine(_sessionDirectory, CapturedFileName);

    private static D2RSettingsSession Create(
        string? baseSaveDirectory,
        string? modSaveDirectory,
        string? sessionDirectory)
    {
        baseSaveDirectory = NormalizeDirectory(
            baseSaveDirectory ?? D2RClientLayout.GetBaseSaveDirectory());
        modSaveDirectory = NormalizeDirectory(
            modSaveDirectory ?? D2RClientLayout.GetModSaveDirectory());
        sessionDirectory = NormalizeDirectory(
            sessionDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "JM Server",
                "settings-session"));
        return new D2RSettingsSession(
            baseSaveDirectory,
            modSaveDirectory,
            sessionDirectory);
    }

    private async Task CaptureAndRestoreAsync(
        SettingsSessionMetadata metadata,
        CancellationToken cancellationToken)
    {
        var baseSettingsPath = GetBaseSettingsPath(_baseSaveDirectory);
        if (metadata.GameSettingsActivated)
        {
            if (File.Exists(baseSettingsPath))
            {
                File.Copy(baseSettingsPath, CapturedSettingsPath, overwrite: true);
            }

            metadata = metadata with { GameSettingsActivated = false };
            await WriteMetadataAsync(metadata, cancellationToken);
        }

        await RestoreOriginalAsync(metadata, baseSettingsPath, cancellationToken);
        if (File.Exists(CapturedSettingsPath))
        {
            var capturedSettings = await File.ReadAllBytesAsync(
                CapturedSettingsPath,
                cancellationToken);
            await D2RModSettings.InstallSyncedAsync(
                capturedSettings,
                _modSaveDirectory,
                cancellationToken);
        }
    }

    private async Task RestoreOriginalAsync(
        SettingsSessionMetadata metadata,
        string baseSettingsPath,
        CancellationToken cancellationToken)
    {
        if (metadata.OriginalSettingsExisted)
        {
            if (!File.Exists(OriginalSettingsPath))
            {
                throw new FileNotFoundException(
                    "The original D2R settings backup is missing.",
                    OriginalSettingsPath);
            }

            var originalSettings = await File.ReadAllBytesAsync(
                OriginalSettingsPath,
                cancellationToken);
            await WriteSettingsAtomicallyAsync(
                baseSettingsPath,
                originalSettings,
                cancellationToken);
            return;
        }

        File.Delete(baseSettingsPath);
    }

    private async Task<SettingsSessionMetadata> ReadMetadataAsync(
        CancellationToken cancellationToken)
    {
        var json = await File.ReadAllTextAsync(MetadataPath, cancellationToken);
        return JsonSerializer.Deserialize<SettingsSessionMetadata>(json, JsonOptions)
               ?? throw new InvalidDataException("The D2R settings session metadata is empty.");
    }

    private async Task WriteMetadataAsync(
        SettingsSessionMetadata metadata,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_sessionDirectory);
        var temporaryPath = MetadataPath + ".jmnew-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                JsonSerializer.Serialize(metadata, JsonOptions),
                cancellationToken);
            File.Move(temporaryPath, MetadataPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static async Task WriteSettingsAtomicallyAsync(
        string path,
        ReadOnlyMemory<byte> settings,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".jmsettings-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllBytesAsync(
                temporaryPath,
                settings.ToArray(),
                cancellationToken);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private void DeleteSessionDirectory()
    {
        if (Directory.Exists(_sessionDirectory))
        {
            Directory.Delete(_sessionDirectory, recursive: true);
        }
    }

    private static string GetBaseSettingsPath(string baseSaveDirectory) =>
        Path.Combine(baseSaveDirectory, "Settings.json");

    private static string NormalizeDirectory(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private sealed record SettingsSessionMetadata(
        bool OriginalSettingsExisted,
        bool GameSettingsActivated);
}
