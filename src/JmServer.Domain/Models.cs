namespace JmServer.Domain;

public sealed record DeviceIdentity(Guid AccountId, Guid DeviceId, string Username);

public sealed record VaultCharacterSummary(
    Guid CharacterId,
    string Name,
    string CharacterClass,
    long Revision,
    bool IsLeased,
    DateTimeOffset? LeaseExpiresAt);

public sealed record ProfileCheckout(
    Guid SelectedCharacterId,
    Guid LeaseId,
    long Revision,
    DateTimeOffset LeaseExpiresAt,
    byte[] BundleData,
    byte[] Sha256);

public sealed record ProfileCheckinResult(
    long Revision,
    byte[] Sha256);

public sealed record DeviceSettingsSnapshot(
    long Revision,
    byte[] SettingsData,
    byte[] Sha256);

public enum VaultCharacterClass
{
    Amazon,
    Sorceress,
    Necromancer,
    Paladin,
    Barbarian,
    Druid,
    Assassin,
    Warlock
}

public enum CharacterCreationPreset
{
    FreshStart,
    PvpReady
}

public sealed record CharacterClassDefinition(
    VaultCharacterClass CharacterClass,
    string DisplayName,
    bool RequiresWarlockExpansion);

public sealed record CharacterPresetDefinition(
    CharacterCreationPreset Preset,
    string DisplayName,
    string Description,
    byte Level,
    bool CompletesQuests,
    bool UnlocksAllWaypoints,
    int UnspentStatPoints,
    int UnspentSkillPoints);

public sealed record CharacterCreationPolicy(
    int MaxCharactersPerAccount,
    int MinimumNameLength,
    int MaximumNameLength,
    IReadOnlyList<CharacterClassDefinition> Classes,
    IReadOnlyList<CharacterPresetDefinition> Presets);

public sealed record CharacterSaveRequest(
    string Name,
    VaultCharacterClass CharacterClass,
    CharacterPresetDefinition Preset,
    DateTimeOffset CreatedAt);

public sealed record NewVaultCharacter(
    string Name,
    VaultCharacterClass CharacterClass,
    CharacterCreationPreset Preset,
    byte Level,
    byte[] SaveData,
    ProfileFile? InitialSharedStash = null);

public sealed record VaultCharacterData(
    Guid CharacterId,
    string Name,
    VaultCharacterClass CharacterClass,
    long VaultRevision,
    bool IsLeased,
    DateTimeOffset? LeaseExpiresAt,
    byte[] SaveData);

public sealed record DeletedVaultCharacterSummary(
    Guid CharacterId,
    string Name,
    string CharacterClass,
    DateTimeOffset DeletedAt);

public sealed record CharacterPrimaryStats(
    byte Level,
    int Strength,
    int Dexterity,
    int Vitality,
    int Energy,
    int UnspentStatPoints);

public sealed record CharacterManagementSnapshot(
    VaultCharacterSummary Character,
    CharacterPrimaryStats Stats);

public enum PvpRoomState
{
    Waiting,
    Ready
}

public sealed record PvpRoomParticipant(
    Guid AccountId,
    Guid DeviceId,
    string Username,
    Guid CharacterId,
    string CharacterName);

public sealed record PvpRoomSnapshot(
    Guid RoomId,
    string RoomCode,
    PvpRoomParticipant Host,
    PvpRoomParticipant? Guest,
    string HostAddress,
    int HostPort,
    PvpRoomState State,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);
