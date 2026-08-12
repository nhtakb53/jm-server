using System.Buffers.Binary;
using System.Text;

namespace JmServer.Domain;

public sealed record ProfileFile(string RelativePath, byte[] Data);

public static class ProfileBundleCodec
{
    private static ReadOnlySpan<byte> Magic => "JMP1"u8;

    public const int MaxFileCount = 64;
    public const int MaxFileLength = 4 * 1024 * 1024;
    public const int MaxBundleLength = 8 * 1024 * 1024;
    public const int MaxPathUtf8Length = 255;

    public static byte[] Encode(IEnumerable<ProfileFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var materialized = files
            .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        ValidateFileSet(materialized);

        var encodedPaths = materialized
            .Select(file => Encoding.UTF8.GetBytes(file.RelativePath))
            .ToArray();
        var totalLength = 6L;
        for (var index = 0; index < materialized.Length; index++)
        {
            totalLength += 6L + encodedPaths[index].Length + materialized[index].Data.Length;
        }

        if (totalLength > MaxBundleLength)
        {
            throw new InvalidDataException(
                $"Profile bundle is {totalLength} bytes; maximum is {MaxBundleLength}.");
        }

        var output = GC.AllocateUninitializedArray<byte>((int)totalLength);
        Magic.CopyTo(output);
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(4, 2), (ushort)materialized.Length);
        var offset = 6;
        for (var index = 0; index < materialized.Length; index++)
        {
            var file = materialized[index];
            var path = encodedPaths[index];
            BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(offset, 2), (ushort)path.Length);
            BinaryPrimitives.WriteInt32BigEndian(output.AsSpan(offset + 2, 4), file.Data.Length);
            offset += 6;
            path.CopyTo(output.AsSpan(offset));
            offset += path.Length;
            file.Data.CopyTo(output.AsSpan(offset));
            offset += file.Data.Length;
        }

        return output;
    }

    public static IReadOnlyList<ProfileFile> Decode(ReadOnlySpan<byte> bundle)
    {
        if (bundle.Length is < 6 or > MaxBundleLength || !bundle[..4].SequenceEqual(Magic))
        {
            throw new InvalidDataException("Profile bundle header is invalid.");
        }

        var fileCount = BinaryPrimitives.ReadUInt16BigEndian(bundle.Slice(4, 2));
        if (fileCount > MaxFileCount)
        {
            throw new InvalidDataException(
                $"Profile bundle has {fileCount} files; maximum is {MaxFileCount}.");
        }

        var files = new List<ProfileFile>(fileCount);
        var offset = 6;
        for (var index = 0; index < fileCount; index++)
        {
            if (bundle.Length - offset < 6)
            {
                throw new InvalidDataException("Profile bundle file header is truncated.");
            }

            var pathLength = BinaryPrimitives.ReadUInt16BigEndian(bundle.Slice(offset, 2));
            var dataLength = BinaryPrimitives.ReadInt32BigEndian(bundle.Slice(offset + 2, 4));
            offset += 6;
            if (pathLength is 0 or > MaxPathUtf8Length ||
                dataLength is < 0 or > MaxFileLength ||
                bundle.Length - offset < pathLength + (long)dataLength)
            {
                throw new InvalidDataException("Profile bundle file length is invalid.");
            }

            string relativePath;
            try
            {
                relativePath = new UTF8Encoding(false, true)
                    .GetString(bundle.Slice(offset, pathLength));
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidDataException("Profile bundle path is not valid UTF-8.", exception);
            }

            offset += pathLength;
            var data = bundle.Slice(offset, dataLength).ToArray();
            offset += dataLength;
            files.Add(new ProfileFile(relativePath, data));
        }

        if (offset != bundle.Length)
        {
            throw new InvalidDataException("Profile bundle contains trailing bytes.");
        }

        ValidateFileSet(files);
        return files;
    }

    private static void ValidateFileSet(IReadOnlyList<ProfileFile> files)
    {
        if (files.Count > MaxFileCount)
        {
            throw new InvalidDataException(
                $"Profile bundle has {files.Count} files; maximum is {MaxFileCount}.");
        }

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            ArgumentNullException.ThrowIfNull(file);
            ArgumentNullException.ThrowIfNull(file.Data);
            if (!ProfileSavePolicy.IsManagedFileName(file.RelativePath))
            {
                throw new InvalidDataException(
                    $"'{file.RelativePath}' is not an allowed 정만서버 profile file.");
            }

            var encodedLength = Encoding.UTF8.GetByteCount(file.RelativePath);
            if (encodedLength is 0 or > MaxPathUtf8Length)
            {
                throw new InvalidDataException("Profile file name is too long.");
            }

            if (file.Data.Length > MaxFileLength)
            {
                throw new InvalidDataException(
                    $"Profile file '{file.RelativePath}' exceeds {MaxFileLength} bytes.");
            }

            if (!paths.Add(file.RelativePath))
            {
                throw new InvalidDataException(
                    $"Profile bundle contains duplicate path '{file.RelativePath}'.");
            }
        }
    }
}

public static class ProfileSavePolicy
{
    public const string PreferredSoftcoreSharedStashName = "ModernSharedStashSoftCoreV2.d2i";

    private static readonly HashSet<string> CharacterExtensions = new(
        [
            ".d2s",
            ".ctl", ".ctlo",
            ".key", ".keyo",
            ".ma0", ".ma1", ".ma2", ".ma3", ".map"
        ],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> SharedStashNames = new(
        [
            PreferredSoftcoreSharedStashName,
            "ModernSharedStashHardCoreV2.d2i",
            "SharedStashSoftCoreV2.d2i",
            "SharedStashHardCoreV2.d2i"
        ],
        StringComparer.OrdinalIgnoreCase);

    public static bool IsManagedFileName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !string.Equals(value, Path.GetFileName(value), StringComparison.Ordinal) ||
            value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return false;
        }

        return SharedStashNames.Contains(value) ||
               CharacterExtensions.Contains(Path.GetExtension(value));
    }

    public static bool IsCharacterSave(string value) =>
        string.Equals(Path.GetExtension(value), ".d2s", StringComparison.OrdinalIgnoreCase);

    public static bool IsCharacterCompanion(string value) =>
        CharacterExtensions.Contains(Path.GetExtension(value)) && !IsCharacterSave(value);

    public static bool IsCharacterControl(string value)
    {
        var extension = Path.GetExtension(value);
        return extension.Equals(".key", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".keyo", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".ctl", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".ctlo", StringComparison.OrdinalIgnoreCase);
    }

    public static bool BelongsToCharacter(string value, string characterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(characterName);
        if (!IsCharacterSave(value) && !IsCharacterCompanion(value))
        {
            return false;
        }

        var stem = Path.GetFileNameWithoutExtension(value);
        var extension = Path.GetExtension(value);
        var hasOfflineControlSlot =
            extension.Equals(".keyo", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".ctlo", StringComparison.OrdinalIgnoreCase);
        if (!hasOfflineControlSlot)
        {
            return stem.Equals(characterName, StringComparison.OrdinalIgnoreCase);
        }

        // D2R appends a numeric input slot to offline control files, for example
        // Hero0.keyo and Hero0.ctlo. The suffix is not part of the character name.
        if (stem.Length <= characterName.Length ||
            !stem.StartsWith(characterName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return stem.AsSpan(characterName.Length).IndexOfAnyExceptInRange('0', '9') < 0;
    }

    public static bool IsSharedStash(string value) => SharedStashNames.Contains(value);

    public static bool IsSoftcoreSharedStash(string value) =>
        IsSharedStash(value) &&
        value.Contains("SoftCore", StringComparison.OrdinalIgnoreCase);

    public static ProfileFile? SelectPreferredSoftcoreSharedStash(
        IEnumerable<ProfileFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        ProfileFile? fallback = null;
        foreach (var file in files)
        {
            if (!IsSoftcoreSharedStash(file.RelativePath))
            {
                continue;
            }

            if (string.Equals(
                    file.RelativePath,
                    PreferredSoftcoreSharedStashName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return file;
            }

            fallback ??= file;
        }

        return fallback;
    }
}
