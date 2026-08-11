using D2SSharp.Enums;
using D2SSharp.Model;
using JmServer.Domain;

namespace JmServer.GameIntegration.Tests;

public sealed class D2CharacterSaveEditorTests
{
    [Fact]
    public void ManagementOperations_PreserveJmSelectorItemsUnknownToTheBuiltInParser()
    {
        var original = CreatePvpWarlock("SelectorUser");
        var save = D2Save.Read(original);
        save.Items.Add(new Item
        {
            Flags = ItemFlags.Identified | ItemFlags.CompactSave,
            Version = 101,
            Position = new ItemPosition
            {
                Mode = ItemMode.Stored,
                StorePage = StorePage.Inventory,
                InvX = 3,
                InvY = 2
            },
            ItemCodeString = "309",
            ItemLevel = 99,
            Quality = ItemQuality.Normal
        });
        var selectorSave = save.ToBytes(
            externalData: JmD2ExternalData.Instance,
            targetVersion: save.Version);

        var builtInError = Assert.Throws<InvalidDataException>(() => D2Save.Read(selectorSave));
        Assert.Contains("item code=309", builtInError.Message, StringComparison.Ordinal);

        var editor = new D2CharacterSaveEditor();
        var stats = editor.ReadPrimaryStats(selectorSave);
        var renamedBytes = editor.Rename(selectorSave, "SelectorSafe");
        var resetBytes = editor.ResetPrimaryStats(
            renamedBytes,
            VaultCharacterClass.Warlock);
        var reset = D2Save.Read(resetBytes, JmD2ExternalData.Instance);

        Assert.Equal(99, stats.Level);
        Assert.Equal("SelectorSafe", reset.Character.Preview.Name);
        Assert.Contains(reset.Items, item => item.ItemCodeString == "309");
        Assert.True(D2Save.VerifyChecksum(resetBytes));
    }

    [Fact]
    public void Rename_UpdatesBothNamesAndPreservesAValidSave()
    {
        var original = CreatePvpWarlock("OldName");

        var renamed = new D2CharacterSaveEditor().Rename(original, "새이름");
        var save = D2Save.Read(renamed);

        Assert.True(D2Save.VerifyChecksum(renamed));
        Assert.Equal("새이름", save.Character.Preview.Name);
        Assert.Equal(99, save.Character.Level);
        Assert.Equal("box", Assert.Single(save.Items).ItemCodeString);
    }

    [Fact]
    public void UpgradeToReignOfTheWarlock_ConvertsExpansionCharacterAndPreservesSave()
    {
        var original = CreatePvpWarlock("EraUpgrade");
        var expansion = D2Save.Read(original);
        expansion.Character.Class = CharacterClass.Sorceress;
        expansion.Character.Preview.GameVersion = GameVersion.Expansion;
        expansion.Demon = null;
        var expansionBytes = expansion.ToBytes(
            externalData: JmD2ExternalData.Instance,
            targetVersion: expansion.Version);

        var editor = new D2CharacterSaveEditor();
        var upgradedBytes = editor.UpgradeToReignOfTheWarlock(expansionBytes);
        var upgraded = D2Save.Read(upgradedBytes, JmD2ExternalData.Instance);

        Assert.True(D2Save.VerifyChecksum(upgradedBytes));
        Assert.Equal(GameVersion.ReignOfTheWarlock, editor.ReadGameVersion(upgradedBytes));
        Assert.Equal(CharacterClass.Sorceress, upgraded.Character.Class);
        Assert.Equal("EraUpgrade", upgraded.Character.Preview.Name);
        Assert.Equal(99, upgraded.Character.Level);
        Assert.Equal("box", Assert.Single(upgraded.Items).ItemCodeString);
    }

    [Fact]
    public void ResetPrimaryStats_RefundsOnlyAllocatedPointsAndRepairsDerivedStats()
    {
        var original = CreatePvpWarlock("StatWarlock");
        var allocated = D2Save.Read(original);
        var baselineLife = allocated.Stats.GetStat(StatId.MaxLife);
        var baselineStamina = allocated.Stats.GetStat(StatId.MaxStamina);
        var baselineMana = allocated.Stats.GetStat(StatId.MaxMana);
        allocated.Stats.SetStat(StatId.Strength, 115);
        allocated.Stats.SetStat(StatId.Vitality, 75);
        allocated.Stats.SetStat(StatId.Energy, 40);
        allocated.Stats.SetStat(StatId.StatPoints, 335);
        allocated.Stats.SetStat(StatId.MaxLife, baselineLife + 50 * 12 * 64L);
        allocated.Stats.SetStat(StatId.Life, baselineLife + 50 * 12 * 64L);
        allocated.Stats.SetStat(StatId.MaxStamina, baselineStamina + 50 * 4 * 64L);
        allocated.Stats.SetStat(StatId.Stamina, baselineStamina + 50 * 4 * 64L);
        allocated.Stats.SetStat(StatId.MaxMana, baselineMana + 20 * 8 * 64L);
        allocated.Stats.SetStat(StatId.Mana, baselineMana + 20 * 8 * 64L);
        var allocatedBytes = allocated.ToBytes(targetVersion: allocated.Version);

        var resetBytes = new D2CharacterSaveEditor().ResetPrimaryStats(
            allocatedBytes,
            VaultCharacterClass.Warlock);
        var reset = D2Save.Read(resetBytes);

        Assert.True(D2Save.VerifyChecksum(resetBytes));
        Assert.Equal(15, reset.Stats.GetStat(StatId.Strength));
        Assert.Equal(20, reset.Stats.GetStat(StatId.Dexterity));
        Assert.Equal(25, reset.Stats.GetStat(StatId.Vitality));
        Assert.Equal(20, reset.Stats.GetStat(StatId.Energy));
        Assert.Equal(505, reset.Stats.GetStat(StatId.StatPoints));
        Assert.Equal(baselineLife, reset.Stats.GetStat(StatId.MaxLife));
        Assert.Equal(baselineStamina, reset.Stats.GetStat(StatId.MaxStamina));
        Assert.Equal(baselineMana, reset.Stats.GetStat(StatId.MaxMana));
        Assert.Equal("box", Assert.Single(reset.Items).ItemCodeString);
    }

    private static byte[] CreatePvpWarlock(string name)
    {
        var preset = CharacterVaultService.CreationPolicy.Presets.Single(
            item => item.Preset == CharacterCreationPreset.PvpReady);
        return new D2CharacterSaveFactory().Create(
            new CharacterSaveRequest(
                name,
                VaultCharacterClass.Warlock,
                preset,
                new DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.Zero)));
    }
}
