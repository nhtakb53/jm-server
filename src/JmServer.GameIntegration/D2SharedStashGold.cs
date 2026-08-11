using System.Buffers.Binary;
using D2SSharp.Enums;
using D2SSharp.Model;
using JmServer.Domain;

namespace JmServer.GameIntegration;

public static class D2SharedStashGold
{
    public const string SoftcoreFileName = ProfileSavePolicy.PreferredSoftcoreSharedStashName;
    public const uint MaximumGoldPerTab = 2_500_000;
    public const byte MaximumAdvancedStackSize = 99;

    private const uint CurrentItemFormat = 105;
    private const uint CurrentStashFormat = 2;
    private const uint StashMagic = 2_857_740_885;
    private const int HeaderLength = 64;
    private const int GoldOffset = 12;
    private const int TabLengthOffset = 16;
    private const int TabTypeOffset = 20;

    private static readonly string[] AdvancedStashItemCodes =
    [
        "rvs", "rvl",
        "gcv", "gfv", "gsv", "gzv", "gpv",
        "gcy", "gfy", "gsy", "gly", "gpy",
        "gcb", "gfb", "gsb", "glb", "gpb",
        "gcg", "gfg", "gsg", "glg", "gpg",
        "gcr", "gfr", "gsr", "glr", "gpr",
        "gcw", "gfw", "gsw", "glw", "gpw",
        "skc", "skf", "sku", "skl", "skz",
        "r01", "r02", "r03", "r04", "r05", "r06", "r07", "r08", "r09",
        "r10", "r11", "r12", "r13", "r14", "r15", "r16", "r17", "r18", "r19",
        "r20", "r21", "r22", "r23", "r24", "r25", "r26", "r27", "r28", "r29",
        "r30", "r31", "r32", "r33",
        "pk1", "pk2", "pk3",
        "dhn", "bey", "mbr",
        "toa", "tes", "ceh", "bet", "fed",
        "xa1", "xa2", "xa3", "xa4", "xa5",
        "ua1", "ua2", "ua3", "ua4", "ua5"
    ];

    public static IReadOnlyList<string> InitialAdvancedStashItemCodes => AdvancedStashItemCodes;

    public static ProfileFile CreateInitialSoftcoreProfileFile() =>
        new(SoftcoreFileName, CreateModernSoftcore(MaximumGoldPerTab));

    public static byte[] CreateModernSoftcore(uint goldPerNormalTab)
    {
        ValidateGold(goldPerNormalTab);

        var stash = new D2StashSave();
        for (var index = 0; index < 5; index++)
        {
            stash.Add(CreateTab(StashTabType.Normal, goldPerNormalTab));
        }

        var advancedStash = CreateTab(StashTabType.AdvancedStash, gold: 0);
        RefillMaterialStacks(advancedStash);
        stash.Add(advancedStash);
        stash.Add(new D2StashTab
        {
            StashFormat = CurrentStashFormat,
            ItemFormat = CurrentItemFormat,
            Gold = 0,
            TabType = StashTabType.Chronicle,
            Chronicle = new ChronicleSection
            {
                // Current D2R reserves 64 bytes after the empty chronicle lists.
                TrailingData = new byte[64]
            }
        });

        return stash.ToBytes(
            externalData: JmD2ExternalData.Instance,
            targetVersion: CurrentItemFormat);
    }

    public static byte[] RefillMaterialStacks(ReadOnlySpan<byte> stashData)
    {
        var stash = D2StashSave.Read(stashData, JmD2ExternalData.Instance);
        var advancedStash = stash.SingleOrDefault(tab =>
            tab.TabType == StashTabType.AdvancedStash);
        if (advancedStash is null)
        {
            advancedStash = CreateTab(StashTabType.AdvancedStash, gold: 0);
            var chronicleIndex = stash.FindIndex(tab => tab.TabType == StashTabType.Chronicle);
            if (chronicleIndex >= 0)
            {
                stash.Insert(chronicleIndex, advancedStash);
            }
            else
            {
                stash.Add(advancedStash);
            }
        }

        RefillMaterialStacks(advancedStash);
        return stash.ToBytes(
            externalData: JmD2ExternalData.Instance,
            targetVersion: CurrentItemFormat);
    }

    public static byte[] SetNormalTabGold(ReadOnlySpan<byte> stashData, uint goldPerTab)
    {
        ValidateGold(goldPerTab);
        if (stashData.Length < HeaderLength)
        {
            throw new InvalidDataException("D2R shared stash is too short.");
        }

        var output = stashData.ToArray();
        var offset = 0;
        var normalTabCount = 0;
        while (offset < output.Length)
        {
            var remaining = output.Length - offset;
            if (remaining < HeaderLength ||
                BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(offset, 4)) != StashMagic)
            {
                throw new InvalidDataException("D2R shared stash tab header is invalid.");
            }

            var tabLength = BinaryPrimitives.ReadUInt16LittleEndian(
                output.AsSpan(offset + TabLengthOffset, 2));
            if (tabLength < HeaderLength || tabLength > remaining)
            {
                throw new InvalidDataException("D2R shared stash tab length is invalid.");
            }

            var stashFormat = BinaryPrimitives.ReadUInt32LittleEndian(
                output.AsSpan(offset + 4, 4));
            var tabType = stashFormat < 2
                ? StashTabType.Normal
                : (StashTabType)output[offset + TabTypeOffset];
            if (tabType == StashTabType.Normal)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(
                    output.AsSpan(offset + GoldOffset, 4),
                    goldPerTab);
                normalTabCount++;
            }

            offset += tabLength;
        }

        if (normalTabCount == 0)
        {
            throw new InvalidDataException("D2R shared stash has no normal tabs.");
        }

        return output;
    }

    private static D2StashTab CreateTab(StashTabType tabType, uint gold) => new()
    {
        StashFormat = CurrentStashFormat,
        ItemFormat = CurrentItemFormat,
        Gold = gold,
        TabType = tabType,
        Items = new ItemsSection()
    };

    private static void RefillMaterialStacks(D2StashTab advancedStash)
    {
        var existingByCode = advancedStash.Items
            .GroupBy(item => item.ItemCodeString, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (var itemCode in AdvancedStashItemCodes)
        {
            if (!existingByCode.TryGetValue(itemCode, out var item))
            {
                item = CreateAdvancedStackItem(itemCode);
                advancedStash.Items.Add(item);
            }

            item.AdvancedStashStackSize = MaximumAdvancedStackSize;
        }
    }

    private static Item CreateAdvancedStackItem(string itemCode) => new()
    {
        Flags = ItemFlags.Identified | ItemFlags.CompactSave,
        Version = 101,
        Position = new ItemPosition
        {
            Mode = ItemMode.Stored,
            StorePage = StorePage.Stash,
            InvX = 0,
            InvY = 0
        },
        ItemCodeString = itemCode,
        ItemLevel = 1,
        Quality = ItemQuality.Normal,
        AdvancedStashStackSize = MaximumAdvancedStackSize
    };

    private static void ValidateGold(uint goldPerTab)
    {
        if (goldPerTab > MaximumGoldPerTab)
        {
            throw new ArgumentOutOfRangeException(
                nameof(goldPerTab),
                $"Gold per shared stash tab cannot exceed {MaximumGoldPerTab:N0}.");
        }
    }
}
