using D2SSharp.Enums;
using D2SSharp.Model;

namespace JmServer.GameIntegration.Tests;

public sealed class D2SharedStashGoldTests
{
    [Fact]
    public void CreateModernSoftcore_CreatesCurrentDlcLayoutWithFullNormalTabs()
    {
        var bytes = D2SharedStashGold.CreateModernSoftcore(
            D2SharedStashGold.MaximumGoldPerTab);

        var stash = D2StashSave.Read(bytes);
        Assert.Equal(7, stash.Count);
        Assert.All(stash.Take(5), tab =>
        {
            Assert.Equal(2u, tab.StashFormat);
            Assert.Equal(105u, tab.ItemFormat);
            Assert.Equal(StashTabType.Normal, tab.TabType);
            Assert.Equal(D2SharedStashGold.MaximumGoldPerTab, tab.Gold);
            Assert.Empty(tab.Items);
        });
        var advancedStash = stash[5];
        Assert.Equal(StashTabType.AdvancedStash, advancedStash.TabType);
        Assert.Equal(0u, advancedStash.Gold);
        Assert.Equal(91, advancedStash.Items.Count);
        Assert.Equal(
            D2SharedStashGold.InitialAdvancedStashItemCodes.Order(),
            advancedStash.Items.Select(item => item.ItemCodeString).Order());
        Assert.All(advancedStash.Items, item =>
        {
            Assert.Equal(
                D2SharedStashGold.MaximumAdvancedStackSize,
                item.AdvancedStashStackSize);
            Assert.Equal(StorePage.Stash, item.Position.StorePage);
        });
        Assert.Equal(StashTabType.Chronicle, stash[6].TabType);
        Assert.Equal(0u, stash[6].Gold);
        Assert.NotNull(stash[6].Chronicle);
        Assert.Equal(64, stash[6].Chronicle!.TrailingData.Length);
    }

    [Fact]
    public void RefillMaterialStacks_PreservesOtherItemsAndRestoresAllMaterialStacks()
    {
        var stash = D2StashSave.Read(D2SharedStashGold.CreateModernSoftcore(123));
        var advancedStash = stash.Single(tab => tab.TabType == StashTabType.AdvancedStash);
        advancedStash.Items.Single(item => item.ItemCodeString == "r22")
            .AdvancedStashStackSize = 1;
        advancedStash.Items.Add(new Item
        {
            Flags = ItemFlags.Identified | ItemFlags.CompactSave,
            Version = 101,
            Position = new ItemPosition
            {
                Mode = ItemMode.Stored,
                StorePage = StorePage.Stash
            },
            ItemCodeString = "tsc",
            ItemLevel = 1,
            Quality = ItemQuality.Normal,
            AdvancedStashStackSize = 7
        });

        var updated = D2SharedStashGold.RefillMaterialStacks(
            stash.ToBytes(targetVersion: 105));
        var parsed = D2StashSave.Read(updated);
        var parsedAdvanced = parsed.Single(tab => tab.TabType == StashTabType.AdvancedStash);

        Assert.Equal(92, parsedAdvanced.Items.Count);
        Assert.Equal(
            (byte)7,
            parsedAdvanced.Items.Single(item => item.ItemCodeString == "tsc")
                .AdvancedStashStackSize);
        Assert.All(
            parsedAdvanced.Items.Where(item =>
                D2SharedStashGold.InitialAdvancedStashItemCodes.Contains(item.ItemCodeString)),
            item => Assert.Equal(
                D2SharedStashGold.MaximumAdvancedStackSize,
                item.AdvancedStashStackSize));
    }

    [Fact]
    public void SetNormalTabGold_ChangesOnlyNormalTabGoldFields()
    {
        var original = D2SharedStashGold.CreateModernSoftcore(123);

        var updated = D2SharedStashGold.SetNormalTabGold(original, 456_789);
        var stash = D2StashSave.Read(updated);

        Assert.All(stash.Take(5), tab => Assert.Equal(456_789u, tab.Gold));
        Assert.Equal(0u, stash[5].Gold);
        Assert.Equal(0u, stash[6].Gold);

        var reverted = D2SharedStashGold.SetNormalTabGold(updated, 123);
        Assert.Equal(original, reverted);
    }

    [Fact]
    public void SetNormalTabGold_RejectsInvalidDataWithoutReturningPartialOutput()
    {
        Assert.Throws<InvalidDataException>(
            () => D2SharedStashGold.SetNormalTabGold(new byte[64], 1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => D2SharedStashGold.CreateModernSoftcore(
                D2SharedStashGold.MaximumGoldPerTab + 1));
    }

    [Fact]
    public void CreateInitialSoftcoreProfileFile_UsesManagedModernFileName()
    {
        var file = D2SharedStashGold.CreateInitialSoftcoreProfileFile();

        Assert.Equal(D2SharedStashGold.SoftcoreFileName, file.RelativePath);
        Assert.True(JmServer.Domain.ProfileSavePolicy.IsSoftcoreSharedStash(file.RelativePath));
    }

    [Fact]
    public void InitialMaterialCodes_MatchEveryAdvancedStashStackableGameDataRow()
    {
        var assembly = typeof(D2SharedStashGold).Assembly;
        var resourceName = assembly.GetManifestResourceNames().Single(name =>
            name.EndsWith("ModData.data.global.excel.misc.txt", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        var columns = reader.ReadLine()!.Split('\t');
        var codeIndex = Array.IndexOf(columns, "code");
        var stackableIndex = Array.IndexOf(columns, "AdvancedStashStackable");
        Assert.True(codeIndex >= 0);
        Assert.True(stackableIndex >= 0);

        var expectedCodes = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            var values = line.Split('\t');
            if (values.Length > stackableIndex && values[stackableIndex] == "1")
            {
                expectedCodes.Add(values[codeIndex]);
            }
        }

        Assert.Equal(91, expectedCodes.Count);
        Assert.Equal(
            expectedCodes.Order(StringComparer.Ordinal),
            D2SharedStashGold.InitialAdvancedStashItemCodes.Order(StringComparer.Ordinal));
    }
}
