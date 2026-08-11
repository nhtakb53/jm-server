using D2SSharp.Enums;
using D2SSharp.Model;
using JmServer.Domain;

namespace JmServer.GameIntegration.Tests;

public sealed class D2CharacterSaveFactoryTests
{
    [Theory]
    [InlineData(VaultCharacterClass.Amazon, 20, 25, 20, 15)]
    [InlineData(VaultCharacterClass.Sorceress, 10, 25, 10, 35)]
    [InlineData(VaultCharacterClass.Necromancer, 15, 25, 15, 25)]
    [InlineData(VaultCharacterClass.Paladin, 25, 20, 25, 15)]
    [InlineData(VaultCharacterClass.Barbarian, 30, 20, 25, 10)]
    [InlineData(VaultCharacterClass.Druid, 15, 20, 25, 20)]
    [InlineData(VaultCharacterClass.Assassin, 20, 20, 20, 25)]
    [InlineData(VaultCharacterClass.Warlock, 15, 20, 25, 20)]
    public void Create_Level99PresetProducesCompletedCampaignSave(
        VaultCharacterClass characterClass,
        int strength,
        int dexterity,
        int vitality,
        int energy)
    {
        var preset = CharacterVaultService.CreationPolicy.Presets.Single();
        var bytes = new D2CharacterSaveFactory().Create(
            new CharacterSaveRequest(
                "새캐릭터",
                characterClass,
                preset,
                new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero)));

        var save = D2Save.Read(bytes);
        Assert.True(D2Save.VerifyChecksum(bytes));
        Assert.Equal(105u, save.Version);
        Assert.Equal("새캐릭터", save.Character.Preview.Name);
        Assert.Equal(characterClass.ToString(), save.Character.Class.ToString());
        Assert.Equal(99, save.Character.Level);
        Assert.Equal(strength, save.Stats.GetStat(StatId.Strength));
        Assert.Equal(dexterity, save.Stats.GetStat(StatId.Dexterity));
        Assert.Equal(vitality, save.Stats.GetStat(StatId.Vitality));
        Assert.Equal(energy, save.Stats.GetStat(StatId.Energy));
        Assert.Equal(505, save.Stats.GetStat(StatId.StatPoints));
        Assert.Equal(110, save.Stats.GetStat(StatId.SkillPoints));
        Assert.Equal(3_520_485_254L, save.Stats.GetStat(StatId.Experience));
        AssertStarterCube(save);
        Assert.All(save.Skills.SkillLevels, level => Assert.Equal(0, level));
        Assert.Equal(
            GameVersion.ReignOfTheWarlock,
            save.Character.Preview.GameVersion);
        AssertCampaignComplete(save);
    }

    private static void AssertCampaignComplete(D2Save save)
    {
        Assert.Equal(15u, save.Character.Flags.Progression);
        Assert.Equal(Difficulty.Hell, save.Character.Flags.MaxAllowedDifficulty);

        foreach (var difficulty in new[]
                 {
                     save.Quests.Normal,
                     save.Quests.Nightmare,
                     save.Quests.Hell
                 })
        {
            foreach (var actProperty in difficulty.GetType().GetProperties())
            {
                var act = actProperty.GetValue(difficulty);
                if (act is null)
                {
                    continue;
                }

                foreach (var questProperty in act.GetType().GetProperties()
                             .Where(property =>
                                 property.PropertyType == typeof(QuestFlags) &&
                                 !property.Name.StartsWith("Reserved", StringComparison.Ordinal) &&
                                 property.Name != "Introduction"))
                {
                    var flags = (QuestFlags)questProperty.GetValue(act)!;
                    Assert.Equal(
                        QuestFlags.RewardGranted,
                        flags & QuestFlags.RewardGranted);
                    if (questProperty.Name == "Completion")
                    {
                        Assert.Equal(
                            QuestFlags.CompletedBefore,
                            flags & QuestFlags.CompletedBefore);
                    }
                }
            }

            Assert.Equal(
                QuestFlags.Custom2,
                difficulty.ActIII.TheGoldenBird & QuestFlags.Custom2);
            Assert.Equal(
                QuestFlags.Custom3,
                difficulty.ActV.PrisonOfIce & QuestFlags.Custom3);
        }

        foreach (var difficulty in new[]
                 {
                     save.Waypoints.Normal,
                     save.Waypoints.Nightmare,
                     save.Waypoints.Hell
                 })
        {
            for (var index = 0; index < 39; index++)
            {
                Assert.True(difficulty.IsWaypointActive(index));
            }
        }

        foreach (var difficulty in new[]
                 {
                     save.PlayerIntro.Normal,
                     save.PlayerIntro.Nightmare,
                     save.PlayerIntro.Hell
                 })
        {
            for (var npcIndex = 1; npcIndex <= 34; npcIndex++)
            {
                Assert.True(difficulty.IsQuestIntroShown(npcIndex));
                Assert.True(difficulty.IsNpcIntroShown(npcIndex));
            }
        }
    }

    private static void AssertStarterCube(D2Save save)
    {
        var cube = Assert.Single(save.Items);
        Assert.Equal("box", cube.ItemCodeString);
        Assert.Equal(ItemMode.Stored, cube.Position.Mode);
        Assert.Equal(StorePage.Inventory, cube.Position.StorePage);
        Assert.Equal((byte)0, cube.Position.InvX);
        Assert.Equal((byte)0, cube.Position.InvY);
    }
}
