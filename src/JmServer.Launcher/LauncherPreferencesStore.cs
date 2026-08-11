using System.Text.Json;
using JmServer.GameIntegration;

namespace JmServer.Launcher;

public sealed record LauncherPreferences(
    string GameDirectory,
    string LoaderArchivePath,
    string? PvpHostAddress = null,
    D2RWindowMode? WindowMode = null,
    int? WindowWidth = null,
    int? WindowHeight = null);

public static class LauncherPreferencesStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static string Path => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "JM Server",
        "launcher-preferences.json");

    public static async Task<LauncherPreferences?> TryLoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(Path))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(Path, cancellationToken);
        return JsonSerializer.Deserialize<LauncherPreferences>(json, Options)
               ?? throw new InvalidDataException("Launcher preferences are empty.");
    }

    public static async Task SaveAsync(
        LauncherPreferences preferences,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var json = JsonSerializer.Serialize(preferences, Options);
        var temporary = Path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(temporary, json, cancellationToken);
            File.Move(temporary, Path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
