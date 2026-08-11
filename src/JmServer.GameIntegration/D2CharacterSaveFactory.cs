using System.Security.Cryptography;
using D2SSharp.Enums;
using D2SSharp.Model;
using JmServer.Domain;

namespace JmServer.GameIntegration;

public sealed class D2CharacterSaveFactory : ICharacterSaveFactory, ISharedStashProvisioner
{
    private const uint CurrentSaveVersion = 105;
    private const uint Level99Experience = 3_520_485_254;
    private const uint CompletedDifficultyFlags = 0x0000_0F00;
    private const int QuestLifeReward = 60;

    private static readonly IReadOnlyDictionary<VaultCharacterClass, BaseStats> StatsByClass =
        new Dictionary<VaultCharacterClass, BaseStats>
        {
            [VaultCharacterClass.Amazon] = new(20, 25, 20, 15, 50, 15, 84, 8, 6, 4),
            [VaultCharacterClass.Sorceress] = new(10, 25, 10, 35, 40, 35, 74, 4, 8, 4),
            [VaultCharacterClass.Necromancer] = new(15, 25, 15, 25, 45, 25, 79, 6, 8, 4),
            [VaultCharacterClass.Paladin] = new(25, 20, 25, 15, 55, 15, 89, 8, 6, 4),
            [VaultCharacterClass.Barbarian] = new(30, 20, 25, 10, 55, 10, 92, 8, 4, 4),
            [VaultCharacterClass.Druid] = new(15, 20, 25, 20, 55, 20, 84, 6, 8, 4),
            [VaultCharacterClass.Assassin] = new(20, 20, 20, 25, 50, 25, 95, 8, 6, 5),
            [VaultCharacterClass.Warlock] = new(15, 20, 25, 20, 55, 20, 86, 8, 6, 4)
        };

    public ProfileFile CreateInitialSharedStash() =>
        D2SharedStashGold.CreateInitialSoftcoreProfileFile();

    public ProfileFile ProvisionSharedStash(ProfileFile? existingStash)
    {
        if (existingStash is null)
        {
            return CreateInitialSharedStash();
        }

        if (!ProfileSavePolicy.IsSoftcoreSharedStash(existingStash.RelativePath))
        {
            throw new InvalidDataException("공유 보관함 파일 이름이 올바르지 않습니다.");
        }

        return new ProfileFile(
            existingStash.RelativePath,
            D2SharedStashGold.RefillMaterialStacks(existingStash.Data));
    }

    public byte[] Create(CharacterSaveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var baseStats = StatsByClass[request.CharacterClass];
        var d2Class = MapClass(request.CharacterClass);
        var level = request.Preset.Level;
        var unixTime = checked((uint)request.CreatedAt.ToUnixTimeSeconds());
        var isWarlock = request.CharacterClass == VaultCharacterClass.Warlock;
        var stats = CreateStats(baseStats, request.Preset);
        var save = new D2Save
        {
            Version = CurrentSaveVersion,
            Character = new Character
            {
                Name = request.Name,
                Preview = new PreviewData
                {
                    Name = request.Name,
                    GameVersion = GameVersion.ReignOfTheWarlock,
                    ExpansionSaveTime = unixTime,
                    ExpansionExperience = level == 1 ? 0 : Level99Experience
                },
                Class = d2Class,
                Level = level,
                Flags = (CharacterFlags)(
                    (uint)CharacterFlags.Expansion |
                    (request.Preset.CompletesQuests ? CompletedDifficultyFlags : 0)),
                NumStats = 16,
                NumSkills = 30,
                CreateTime = unixTime,
                LastSaveTime = unixTime,
                MapSeed = unchecked((uint)RandomNumberGenerator.GetInt32(int.MaxValue)),
                SkillHotkeys = Enumerable.Range(0, 16).Select(_ => new Skill()).ToArray()
            },
            Quests = new QuestSection(),
            Waypoints = new WaypointSection(),
            PlayerIntro = new PlayerIntroSection(),
            Stats = stats,
            Skills = new SkillsSection
            {
                CharacterClass = d2Class,
                SkillLevels = new byte[30]
            },
            Items = new ItemsSection { CreateHoradricCube() },
            Corpses = new CorpseSection(),
            MercItems = new MercItemsSection(),
            IronGolem = new IronGolemSection(),
            Demon = isWarlock ? new DemonSection() : null
        };

        if (request.Preset.CompletesQuests)
        {
            CompleteAllQuests(save.Quests);
            CompleteAllNpcIntroductions(save.PlayerIntro);
        }

        if (request.Preset.UnlocksAllWaypoints)
        {
            UnlockAllWaypoints(save.Waypoints);
        }

        var data = save.ToBytes(
            externalData: JmD2ExternalData.Instance,
            targetVersion: CurrentSaveVersion);
        ValidateGeneratedSave(data, request);
        return data;
    }

    private static PlayerStats CreateStats(
        BaseStats baseStats,
        CharacterPresetDefinition preset)
    {
        var completedLevels = preset.Level - 1;
        var questLife = preset.CompletesQuests ? QuestLifeReward : 0;
        var life = ToFixed(baseStats.Life, baseStats.LifePerLevelQuarter, completedLevels, questLife);
        var mana = ToFixed(baseStats.Mana, baseStats.ManaPerLevelQuarter, completedLevels);
        var stamina = ToFixed(
            baseStats.Stamina,
            baseStats.StaminaPerLevelQuarter,
            completedLevels);
        var stats = new PlayerStats();
        stats.SetStat(StatId.Strength, baseStats.Strength);
        stats.SetStat(StatId.Dexterity, baseStats.Dexterity);
        stats.SetStat(StatId.Vitality, baseStats.Vitality);
        stats.SetStat(StatId.Energy, baseStats.Energy);
        stats.SetStat(StatId.Level, preset.Level);
        stats.SetStat(StatId.Life, life);
        stats.SetStat(StatId.MaxLife, life);
        stats.SetStat(StatId.Mana, mana);
        stats.SetStat(StatId.MaxMana, mana);
        stats.SetStat(StatId.Stamina, stamina);
        stats.SetStat(StatId.MaxStamina, stamina);
        if (preset.UnspentStatPoints > 0)
        {
            stats.SetStat(StatId.StatPoints, preset.UnspentStatPoints);
        }

        if (preset.UnspentSkillPoints > 0)
        {
            stats.SetStat(StatId.SkillPoints, preset.UnspentSkillPoints);
        }

        if (preset.Level > 1)
        {
            stats.SetStat(StatId.Experience, Level99Experience);
        }

        return stats;
    }

    private static long ToFixed(
        int baseValue,
        int perLevelQuarter,
        int completedLevels,
        int flatBonus = 0) =>
        (baseValue + flatBonus) * 256L + perLevelQuarter * completedLevels * 64L;

    private static void CompleteAllQuests(QuestSection section)
    {
        foreach (var difficulty in new[] { section.Normal, section.Nightmare, section.Hell })
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
                                 property.CanWrite &&
                                 property.PropertyType == typeof(QuestFlags) &&
                                 !property.Name.StartsWith("Reserved", StringComparison.Ordinal) &&
                                 property.Name != "Introduction"))
                {
                    var flags = QuestFlags.RewardGranted;
                    if (questProperty.Name == "Completion")
                    {
                        flags |= QuestFlags.CompletedBefore;
                    }

                    questProperty.SetValue(act, flags);
                }
            }

            // D2S quest bits record whether the permanent reward item was actually consumed.
            // Golden Bird bit 6: Potion of Life (+20 life), Prison of Ice bit 7:
            // Scroll of Resistance (+10 all resistances). Apply both in all three difficulties.
            difficulty.ActIII.TheGoldenBird |= QuestFlags.Custom2;
            difficulty.ActV.PrisonOfIce |= QuestFlags.Custom3;
        }
    }

    private static void UnlockAllWaypoints(WaypointSection section)
    {
        foreach (var difficulty in new[] { section.Normal, section.Nightmare, section.Hell })
        {
            for (var index = 0; index < 39; index++)
            {
                difficulty.SetWaypoint(index, active: true);
            }
        }
    }

    private static void CompleteAllNpcIntroductions(PlayerIntroSection section)
    {
        foreach (var difficulty in new[] { section.Normal, section.Nightmare, section.Hell })
        {
            for (var npcIndex = 1; npcIndex <= 34; npcIndex++)
            {
                difficulty.SetQuestIntroShown(npcIndex, value: true);
                difficulty.SetNpcIntroShown(npcIndex, value: true);
            }
        }
    }

    private static Item CreateHoradricCube() => new()
    {
        Flags = ItemFlags.Identified | ItemFlags.CompactSave,
        Version = 101,
        Position = new ItemPosition
        {
            Mode = ItemMode.Stored,
            StorePage = StorePage.Inventory,
            InvX = 0,
            InvY = 0
        },
        ItemCodeString = "box",
        ItemLevel = 1,
        Quality = ItemQuality.Normal
    };

    private static CharacterClass MapClass(VaultCharacterClass characterClass) =>
        characterClass switch
        {
            VaultCharacterClass.Amazon => CharacterClass.Amazon,
            VaultCharacterClass.Sorceress => CharacterClass.Sorceress,
            VaultCharacterClass.Necromancer => CharacterClass.Necromancer,
            VaultCharacterClass.Paladin => CharacterClass.Paladin,
            VaultCharacterClass.Barbarian => CharacterClass.Barbarian,
            VaultCharacterClass.Druid => CharacterClass.Druid,
            VaultCharacterClass.Assassin => CharacterClass.Assassin,
            VaultCharacterClass.Warlock => CharacterClass.Warlock,
            _ => throw new ArgumentOutOfRangeException(nameof(characterClass))
        };

    private static void ValidateGeneratedSave(byte[] data, CharacterSaveRequest request)
    {
        if (!D2Save.VerifyChecksum(data))
        {
            throw new InvalidDataException("생성된 D2R 캐릭터 세이브의 체크섬이 올바르지 않습니다.");
        }

        var parsed = D2Save.Read(data, JmD2ExternalData.Instance);
        var metadata = D2SaveMetadataReader.Read(data);
        if (!string.Equals(metadata.Name, request.Name, StringComparison.Ordinal) ||
            !string.Equals(
                metadata.CharacterClass,
                request.CharacterClass.ToString(),
                StringComparison.Ordinal) ||
            metadata.Level != request.Preset.Level ||
            parsed.Items.Count != 1 ||
            parsed.Items[0].ItemCodeString != "box" ||
            parsed.Items[0].Position.StorePage != StorePage.Inventory ||
            parsed.Skills.SkillLevels.Any(level => level != 0))
        {
            throw new InvalidDataException("생성된 D2R 캐릭터 세이브가 서버 생성 정책과 일치하지 않습니다.");
        }
    }

    private sealed record BaseStats(
        int Strength,
        int Dexterity,
        int Vitality,
        int Energy,
        int Life,
        int Mana,
        int Stamina,
        int LifePerLevelQuarter,
        int ManaPerLevelQuarter,
        int StaminaPerLevelQuarter);
}
