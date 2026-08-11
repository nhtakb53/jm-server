using System.Text.Json;
using JmServer.GameIntegration;

namespace JmServer.Launcher;

public sealed record ProfileLeaseFile(
    string Server,
    int Port,
    bool UseTls,
    string? CertificateSha256,
    Guid SelectedCharacterId,
    Guid LeaseId,
    long Revision,
    DateTimeOffset LeaseExpiresAt,
    IReadOnlyList<string> RegisteredCharacterNames)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static string Path => System.IO.Path.Combine(
        D2RClientLayout.GetModSaveDirectory(),
        ".jmprofile-lease.json");

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var json = JsonSerializer.Serialize(this, Options);
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

    public static async Task<ProfileLeaseFile> LoadAsync(CancellationToken cancellationToken)
    {
        var json = await File.ReadAllTextAsync(Path, cancellationToken);
        return JsonSerializer.Deserialize<ProfileLeaseFile>(json, Options)
               ?? throw new InvalidDataException("Profile lease file is empty.");
    }
}
