using System.Buffers.Binary;
using D2SSharp.Model;

namespace JmServer.GameIntegration;

public static class D2SaveMetadataReader
{
    private const int HeaderLength = 8;

    public static D2SaveMetadata Read(ReadOnlySpan<byte> saveData)
    {
        if (saveData.Length < HeaderLength)
        {
            throw new InvalidDataException("The D2 save file is too short.");
        }

        var version = BinaryPrimitives.ReadUInt32LittleEndian(saveData[4..]);
        if (version >= 104)
        {
            ref readonly var layout = ref D2SaveLayoutV104.FromReadOnly(saveData);
            return new D2SaveMetadata(
                version,
                layout.Name,
                layout.Character.Class.ToString(),
                layout.Character.Level);
        }

        ref readonly var legacyLayout = ref D2SaveLayout.FromReadOnly(saveData);
        return new D2SaveMetadata(
            version,
            legacyLayout.Name,
            legacyLayout.Character.Class.ToString(),
            legacyLayout.Character.Level);
    }
}

public sealed record D2SaveMetadata(
    uint Version,
    string Name,
    string CharacterClass,
    byte Level);
