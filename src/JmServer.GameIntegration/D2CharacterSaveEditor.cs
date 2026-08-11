using D2SSharp.Enums;
using D2SSharp.Model;
using JmServer.Domain;

namespace JmServer.GameIntegration;

public sealed class D2CharacterSaveEditor : ICharacterSaveEditor
{
    private static readonly IReadOnlyDictionary<VaultCharacterClass, CharacterRules> Rules =
        new Dictionary<VaultCharacterClass, CharacterRules>
        {
            [VaultCharacterClass.Amazon] = new(20, 25, 20, 15, 12, 4, 6),
            [VaultCharacterClass.Sorceress] = new(10, 25, 10, 35, 8, 4, 8),
            [VaultCharacterClass.Necromancer] = new(15, 25, 15, 25, 8, 4, 8),
            [VaultCharacterClass.Paladin] = new(25, 20, 25, 15, 12, 4, 6),
            [VaultCharacterClass.Barbarian] = new(30, 20, 25, 10, 16, 4, 4),
            [VaultCharacterClass.Druid] = new(15, 20, 25, 20, 8, 4, 8),
            [VaultCharacterClass.Assassin] = new(20, 20, 20, 25, 12, 5, 7),
            [VaultCharacterClass.Warlock] = new(15, 20, 25, 20, 12, 4, 8)
        };

    public CharacterPrimaryStats ReadPrimaryStats(ReadOnlyMemory<byte> saveData)
    {
        var save = Read(saveData);
        return new CharacterPrimaryStats(
            save.Character.Level,
            ReadInt(save, StatId.Strength),
            ReadInt(save, StatId.Dexterity),
            ReadInt(save, StatId.Vitality),
            ReadInt(save, StatId.Energy),
            ReadInt(save, StatId.StatPoints));
    }

    public byte[] Rename(ReadOnlyMemory<byte> saveData, string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        var save = Read(saveData);
        save.Character.Name = newName;
        save.Character.Preview.Name = newName;
        return WriteAndValidate(save, newName);
    }

    public GameVersion ReadGameVersion(ReadOnlyMemory<byte> saveData) =>
        Read(saveData).Character.Preview.GameVersion;

    public byte[] UpgradeToReignOfTheWarlock(ReadOnlyMemory<byte> saveData)
    {
        var save = Read(saveData);
        save.Character.Preview.GameVersion = GameVersion.ReignOfTheWarlock;
        return WriteAndValidate(save, save.Character.Preview.Name);
    }

    public byte[] ResetPrimaryStats(
        ReadOnlyMemory<byte> saveData,
        VaultCharacterClass characterClass)
    {
        var save = Read(saveData);
        var parsedClass = ParseClass(save.Character.Class);
        if (parsedClass != characterClass)
        {
            throw new InvalidDataException(
                $"Character class mismatch: server={characterClass}, save={parsedClass}.");
        }

        var rules = Rules[characterClass];
        var strength = ReadInt(save, StatId.Strength);
        var dexterity = ReadInt(save, StatId.Dexterity);
        var vitality = ReadInt(save, StatId.Vitality);
        var energy = ReadInt(save, StatId.Energy);
        var unspent = ReadInt(save, StatId.StatPoints);
        var allocatedStrength = ValidateAllocation(strength, rules.Strength, "strength");
        var allocatedDexterity = ValidateAllocation(dexterity, rules.Dexterity, "dexterity");
        var allocatedVitality = ValidateAllocation(vitality, rules.Vitality, "vitality");
        var allocatedEnergy = ValidateAllocation(energy, rules.Energy, "energy");
        var refunded = checked(
            unspent + allocatedStrength + allocatedDexterity + allocatedVitality + allocatedEnergy);
        if (refunded is < 0 or > 10_000)
        {
            throw new InvalidDataException("Character stat point total is outside the server policy.");
        }

        save.Stats.SetStat(StatId.Strength, rules.Strength);
        save.Stats.SetStat(StatId.Dexterity, rules.Dexterity);
        save.Stats.SetStat(StatId.Vitality, rules.Vitality);
        save.Stats.SetStat(StatId.Energy, rules.Energy);
        save.Stats.SetStat(StatId.StatPoints, refunded);

        ResetCurrentAndMaximum(
            save,
            StatId.Life,
            StatId.MaxLife,
            allocatedVitality,
            rules.LifePerVitalityQuarter);
        ResetCurrentAndMaximum(
            save,
            StatId.Stamina,
            StatId.MaxStamina,
            allocatedVitality,
            rules.StaminaPerVitalityQuarter);
        ResetCurrentAndMaximum(
            save,
            StatId.Mana,
            StatId.MaxMana,
            allocatedEnergy,
            rules.ManaPerEnergyQuarter);

        return WriteAndValidate(save, save.Character.Preview.Name);
    }

    private static D2Save Read(ReadOnlyMemory<byte> saveData)
    {
        if (saveData.IsEmpty || saveData.Length > ProfileBundleCodec.MaxFileLength)
        {
            throw new InvalidDataException("Character save size is invalid.");
        }

        var bytes = saveData.ToArray();
        if (!D2Save.VerifyChecksum(bytes))
        {
            throw new InvalidDataException("Character save checksum is invalid.");
        }

        return D2Save.Read(bytes, JmD2ExternalData.Instance);
    }

    private static int ReadInt(D2Save save, StatId stat) =>
        checked((int)save.Stats.GetStat(stat));

    private static int ValidateAllocation(int current, int minimum, string statName)
    {
        if (current < minimum)
        {
            throw new InvalidDataException(
                $"Character {statName} is lower than the class minimum.");
        }

        return current - minimum;
    }

    private static void ResetCurrentAndMaximum(
        D2Save save,
        StatId currentStat,
        StatId maximumStat,
        int allocatedPrimaryStat,
        int gainPerPointQuarter)
    {
        var current = save.Stats.GetStat(currentStat);
        var maximum = save.Stats.GetStat(maximumStat);
        var reduction = checked((long)allocatedPrimaryStat * gainPerPointQuarter * 64L);
        var resetMaximum = Math.Max(256L, maximum - reduction);
        save.Stats.SetStat(maximumStat, resetMaximum);
        save.Stats.SetStat(currentStat, Math.Clamp(current, 256L, resetMaximum));
    }

    private static byte[] WriteAndValidate(D2Save save, string expectedName)
    {
        var bytes = save.ToBytes(
            externalData: JmD2ExternalData.Instance,
            targetVersion: save.Version);
        if (!D2Save.VerifyChecksum(bytes))
        {
            throw new InvalidDataException("Managed character save checksum is invalid.");
        }

        var metadata = D2SaveMetadataReader.Read(bytes);
        if (!string.Equals(metadata.Name, expectedName, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Managed character save name was not persisted.");
        }

        return bytes;
    }

    private static VaultCharacterClass ParseClass(CharacterClass characterClass) =>
        characterClass switch
        {
            CharacterClass.Amazon => VaultCharacterClass.Amazon,
            CharacterClass.Sorceress => VaultCharacterClass.Sorceress,
            CharacterClass.Necromancer => VaultCharacterClass.Necromancer,
            CharacterClass.Paladin => VaultCharacterClass.Paladin,
            CharacterClass.Barbarian => VaultCharacterClass.Barbarian,
            CharacterClass.Druid => VaultCharacterClass.Druid,
            CharacterClass.Assassin => VaultCharacterClass.Assassin,
            CharacterClass.Warlock => VaultCharacterClass.Warlock,
            _ => throw new InvalidDataException($"Unsupported character class '{characterClass}'.")
        };

    private sealed record CharacterRules(
        int Strength,
        int Dexterity,
        int Vitality,
        int Energy,
        int LifePerVitalityQuarter,
        int StaminaPerVitalityQuarter,
        int ManaPerEnergyQuarter);
}
