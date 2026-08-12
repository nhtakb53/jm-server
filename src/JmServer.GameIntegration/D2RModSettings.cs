using System.Text.Json;
using System.Text.Json.Nodes;

namespace JmServer.GameIntegration;

public static class D2RModSettings
{
    private const string SettingsFileName = "Settings.json";
    public const int MaximumSettingsLength = 256 * 1024;

    public static string GetSettingsPath(string? modSaveDirectory = null)
    {
        modSaveDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(
            modSaveDirectory ?? D2RClientLayout.GetModSaveDirectory()));
        return Path.Combine(modSaveDirectory, SettingsFileName);
    }

    public static bool HasLocalSettings(string? modSaveDirectory = null) =>
        File.Exists(GetSettingsPath(modSaveDirectory));

    public static async Task<byte[]?> ReadForSyncAsync(
        string? modSaveDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var path = GetSettingsPath(modSaveDirectory);
        if (!File.Exists(path))
        {
            return null;
        }

        var settingsData = await File.ReadAllBytesAsync(path, cancellationToken);
        ValidateSyncData(settingsData, path);
        return settingsData;
    }

    public static async Task InstallSyncedAsync(
        ReadOnlyMemory<byte> settingsData,
        string? modSaveDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var targetPath = GetSettingsPath(modSaveDirectory);
        ValidateSyncData(settingsData.Span, targetPath);
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

        var temporaryPath = targetPath + ".sync-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, settingsData.ToArray(), cancellationToken);
            File.Move(temporaryPath, targetPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    public static async Task EnsureInitializedAsync(
        string? baseSaveDirectory = null,
        string? modSaveDirectory = null,
        CancellationToken cancellationToken = default,
        D2RDisplaySettings? displaySettings = null)
    {
        baseSaveDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(
            baseSaveDirectory ?? D2RClientLayout.GetBaseSaveDirectory()));
        modSaveDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(
            modSaveDirectory ?? D2RClientLayout.GetModSaveDirectory()));
        Directory.CreateDirectory(modSaveDirectory);

        var sourcePath = Path.Combine(baseSaveDirectory, SettingsFileName);
        var targetPath = GetSettingsPath(modSaveDirectory);
        var source = await ReadObjectAsync(sourcePath, cancellationToken);
        var target = await ReadObjectAsync(targetPath, cancellationToken);

        JsonObject result;
        if (target is not null)
        {
            result = target;
            if (source is not null)
            {
                foreach (var setting in source)
                {
                    if (!result.ContainsKey(setting.Key))
                    {
                        result[setting.Key] = setting.Value?.DeepClone();
                    }
                }
            }
        }
        else
        {
            result = source?.DeepClone().AsObject() ?? new JsonObject();
        }

        result["First Time"] = source?["First Time"]?.DeepClone() ?? 1;
        result["Tutorial"] ??= source?["Tutorial"]?.DeepClone() ?? 3;
        result["Help Menu"] ??= source?["Help Menu"]?.DeepClone() ?? 1;
        if (displaySettings is not null)
        {
            displaySettings.Validate();
            result["Window Mode"] = (int)displaySettings.WindowMode;
            result["Screen Resolution (Windowed)"] = displaySettings.WindowResolution;
        }

        var temporaryPath = Path.Combine(
            modSaveDirectory,
            $".{SettingsFileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                result.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine,
                cancellationToken);
            File.Move(temporaryPath, targetPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task<JsonObject?> ReadObjectAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        try
        {
            return JsonNode.Parse(json)?.AsObject()
                   ?? throw new InvalidDataException($"D2R settings file '{path}' is empty.");
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            throw new InvalidDataException($"D2R settings file '{path}' is not valid JSON.", exception);
        }
    }

    private static void ValidateSyncData(ReadOnlySpan<byte> settingsData, string path)
    {
        if (settingsData.IsEmpty)
        {
            throw new InvalidDataException($"D2R settings file '{path}' is empty.");
        }

        if (settingsData.Length > MaximumSettingsLength)
        {
            throw new InvalidDataException(
                $"D2R settings file '{path}' is {settingsData.Length} bytes; " +
                $"maximum is {MaximumSettingsLength}.");
        }

        try
        {
            var node = JsonNode.Parse(settingsData);
            if (node is not JsonObject)
            {
                throw new InvalidDataException(
                    $"D2R settings file '{path}' must contain a JSON object.");
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"D2R settings file '{path}' is not valid JSON.",
                exception);
        }
    }
}
