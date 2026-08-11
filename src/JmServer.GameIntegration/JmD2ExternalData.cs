using D2SSharp.Data;
using D2SSharp.Enums;

namespace JmServer.GameIntegration;

internal sealed class JmD2ExternalData : IExternalData
{
    private const int CustomCompactItemIndex = int.MinValue + 1;
    private const string MiscTablePath = "data/global/excel/misc.txt";

    private static readonly IExternalData BuiltIn = TxtFileExternalData.Default;
    private static readonly HashSet<uint> CustomCompactItemCodes = LoadCustomCompactItemCodes();

    public static JmD2ExternalData Instance { get; } = new();

    private JmD2ExternalData()
    {
    }

    public StatInfo GetStatInfo(StatId statId, uint saveVersion) =>
        BuiltIn.GetStatInfo(statId, saveVersion);

    public int GetItemIndex(uint itemCode, uint saveVersion)
    {
        var builtInIndex = BuiltIn.GetItemIndex(itemCode, saveVersion);
        return builtInIndex >= 0
            ? builtInIndex
            : CustomCompactItemCodes.Contains(itemCode)
                ? CustomCompactItemIndex
                : -1;
    }

    public ItemTypeInfo GetItemInfo(int itemIndex, uint saveVersion)
    {
        if (itemIndex != CustomCompactItemIndex)
        {
            return BuiltIn.GetItemInfo(itemIndex, saveVersion);
        }

        // Every JM selector/workstone uses the built-in compact Torch (tch/torc) layout.
        // Reusing that type information lets D2SSharp preserve custom control items
        // without teaching it about their generated gameplay recipes or names.
        var torchIndex = BuiltIn.GetItemIndex(MakeItemCode("tch"), saveVersion);
        return BuiltIn.GetItemInfo(torchIndex, saveVersion);
    }

    public int GetQuantityBits(uint saveVersion) => BuiltIn.GetQuantityBits(saveVersion);

    public int GetMagicPrefixOffset(uint saveVersion) =>
        BuiltIn.GetMagicPrefixOffset(saveVersion);

    public int GetAutoMagicOffset(uint saveVersion) =>
        BuiltIn.GetAutoMagicOffset(saveVersion);

    private static HashSet<uint> LoadCustomCompactItemCodes()
    {
        using var stream = JmSupplyModPackage.OpenFile(MiscTablePath);
        using var reader = new StreamReader(stream);
        var header = reader.ReadLine()?.TrimEnd('\r').Split('\t')
                     ?? throw new InvalidDataException("The bundled misc table is empty.");
        var nameIndex = Array.FindIndex(header, name => name == "name");
        var codeIndex = Array.FindIndex(header, name => name == "code");
        var typeIndex = Array.FindIndex(header, name => name == "type");
        var compactSaveIndex = Array.FindIndex(header, name => name == "compactsave");
        if (nameIndex < 0 || codeIndex < 0 || typeIndex < 0 || compactSaveIndex < 0)
        {
            throw new InvalidDataException(
                "The bundled misc table is missing selector format columns.");
        }

        var result = new HashSet<uint>();
        while (reader.ReadLine() is { } line)
        {
            var cells = line.TrimEnd('\r').Split('\t');
            if (cells.Length <= Math.Max(Math.Max(nameIndex, codeIndex), Math.Max(typeIndex, compactSaveIndex)) ||
                !cells[nameIndex].StartsWith("JM ", StringComparison.Ordinal) ||
                cells[typeIndex] != "torc" ||
                cells[compactSaveIndex] != "1")
            {
                continue;
            }

            var code = cells[codeIndex];
            if (code.Length is < 1 or > 4 || !result.Add(MakeItemCode(code)))
            {
                throw new InvalidDataException(
                    $"The bundled custom item code '{code}' is invalid or duplicated.");
            }
        }

        var expectedCount = JmSupplyModPackage.Manifest.CustomItemCount;
        if (result.Count != expectedCount)
        {
            throw new InvalidDataException(
                $"The bundled custom item code count is {result.Count}; expected {expectedCount}.");
        }

        return result;
    }

    private static uint MakeItemCode(string value)
    {
        Span<char> code = stackalloc char[4];
        code.Fill(' ');
        value.AsSpan().CopyTo(code);
        return code[0] |
               (uint)code[1] << 8 |
               (uint)code[2] << 16 |
               (uint)code[3] << 24;
    }
}
