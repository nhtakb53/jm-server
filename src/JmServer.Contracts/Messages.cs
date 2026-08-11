namespace JmServer.Contracts;

public sealed record HandshakeRequest(ushort ProtocolVersion, string ClientVersion);

public sealed record HandshakeResponse(
    bool Accepted,
    ushort ProtocolVersion,
    string ServerVersion,
    string? Message);

public sealed record AuthenticateRequest(Guid DeviceId, string Token);

public sealed record AuthenticateResponse(Guid AccountId, Guid DeviceId, string Username);

public sealed record ListCharactersRequest;

public sealed record CharacterSummary(
    Guid CharacterId,
    string Name,
    string CharacterClass,
    long Revision,
    bool IsLeased,
    DateTimeOffset? LeaseExpiresAt);

public sealed record ListCharactersResponse(IReadOnlyList<CharacterSummary> Characters);

public enum PlayableCharacterClass
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

public sealed record GetCharacterCreationPolicyRequest;

public sealed record CharacterClassOption(
    PlayableCharacterClass CharacterClass,
    string DisplayName,
    bool RequiresWarlockExpansion);

public sealed record CharacterPresetOption(
    CharacterCreationPreset Preset,
    string DisplayName,
    string Description,
    byte Level,
    bool CompletesQuests,
    bool UnlocksAllWaypoints,
    int UnspentStatPoints,
    int UnspentSkillPoints);

public sealed record GetCharacterCreationPolicyResponse(
    int MaxCharactersPerAccount,
    int MinimumNameLength,
    int MaximumNameLength,
    IReadOnlyList<CharacterClassOption> Classes,
    IReadOnlyList<CharacterPresetOption> Presets);

public sealed record CreateCharacterRequest(
    string Name,
    PlayableCharacterClass CharacterClass,
    CharacterCreationPreset Preset);

public sealed record CreateCharacterResponse(CharacterSummary Character);

public sealed record CharacterPrimaryStatsInfo(
    byte Level,
    int Strength,
    int Dexterity,
    int Vitality,
    int Energy,
    int UnspentStatPoints);

public sealed record GetCharacterManagementRequest(Guid CharacterId);

public sealed record GetCharacterManagementResponse(
    CharacterSummary Character,
    CharacterPrimaryStatsInfo Stats);

public sealed record DeletedCharacterSummary(
    Guid CharacterId,
    string Name,
    string CharacterClass,
    DateTimeOffset DeletedAt);

public sealed record ListDeletedCharactersRequest;

public sealed record ListDeletedCharactersResponse(
    IReadOnlyList<DeletedCharacterSummary> Characters);

public sealed record RenameCharacterRequest(Guid CharacterId, string NewName);

public sealed record RenameCharacterResponse(CharacterSummary Character);

public sealed record ResetCharacterStatsRequest(Guid CharacterId);

public sealed record ResetCharacterStatsResponse(
    CharacterSummary Character,
    CharacterPrimaryStatsInfo Stats);

public sealed record DeleteCharacterRequest(Guid CharacterId);

public sealed record DeleteCharacterResponse(Guid CharacterId);

public sealed record RestoreCharacterRequest(Guid CharacterId);

public sealed record RestoreCharacterResponse(CharacterSummary Character);

public sealed record PurgeDeletedCharacterRequest(Guid CharacterId);

public sealed record PurgeDeletedCharacterResponse(Guid CharacterId);

public sealed record CheckoutProfileRequest(Guid SelectedCharacterId);

public sealed record CheckoutProfileResponse(
    Guid SelectedCharacterId,
    Guid LeaseId,
    long Revision,
    DateTimeOffset LeaseExpiresAt,
    int BundleLength,
    string Sha256Hex);

public sealed record RenewProfileLeaseRequest(Guid LeaseId);

public sealed record RenewProfileLeaseResponse(
    Guid LeaseId,
    DateTimeOffset LeaseExpiresAt);

public sealed record CheckinProfileRequest(
    Guid LeaseId,
    long ExpectedRevision,
    int BundleLength,
    string Sha256Hex);

public sealed record CheckinProfileResponse(
    long Revision,
    string Sha256Hex);

public sealed record GetDeviceSettingsRequest;

public sealed record GetDeviceSettingsResponse(
    bool Exists,
    long Revision,
    int SettingsLength,
    string Sha256Hex);

public sealed record PutDeviceSettingsRequest(
    int SettingsLength,
    string Sha256Hex);

public sealed record PutDeviceSettingsResponse(
    long Revision,
    string Sha256Hex);

public enum PvpRoomStatus
{
    Waiting,
    Ready
}

public sealed record PvpRoomInfo(
    Guid RoomId,
    string RoomCode,
    string HostUsername,
    string HostCharacterName,
    string? GuestUsername,
    string? GuestCharacterName,
    string HostAddress,
    int HostPort,
    PvpRoomStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    bool IsHost,
    bool IsParticipant,
    bool CanJoin);

public sealed record ListPvpRoomsRequest;

public sealed record ListPvpRoomsResponse(IReadOnlyList<PvpRoomInfo> Rooms);

public sealed record CreatePvpRoomRequest(Guid CharacterId, string HostAddress);

public sealed record CreatePvpRoomResponse(PvpRoomInfo Room);

public sealed record JoinPvpRoomRequest(Guid RoomId, Guid CharacterId);

public sealed record JoinPvpRoomResponse(PvpRoomInfo Room);

public sealed record RenewPvpRoomRequest(Guid RoomId);

public sealed record RenewPvpRoomResponse(PvpRoomInfo Room);

public sealed record LeavePvpRoomRequest(Guid RoomId);

public sealed record LeavePvpRoomResponse(Guid RoomId);

public sealed record ErrorResponse(ErrorCode Code, string Message, bool Retryable = false);
