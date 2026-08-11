namespace JmServer.Contracts;

public enum MessageType : ushort
{
    HandshakeRequest = 1,
    HandshakeResponse = 2,
    AuthenticateRequest = 3,
    AuthenticateResponse = 4,

    ListCharactersRequest = 10,
    ListCharactersResponse = 11,
    GetCharacterCreationPolicyRequest = 12,
    GetCharacterCreationPolicyResponse = 13,
    CreateCharacterRequest = 14,
    CreateCharacterResponse = 15,
    CheckoutProfileRequest = 20,
    CheckoutProfileResponse = 21,
    RenewProfileLeaseRequest = 22,
    RenewProfileLeaseResponse = 23,
    CheckinProfileRequest = 24,
    CheckinProfileResponse = 25,
    GetDeviceSettingsRequest = 26,
    GetDeviceSettingsResponse = 27,
    PutDeviceSettingsRequest = 28,
    PutDeviceSettingsResponse = 29,

    ListPvpRoomsRequest = 30,
    ListPvpRoomsResponse = 31,
    CreatePvpRoomRequest = 32,
    CreatePvpRoomResponse = 33,
    JoinPvpRoomRequest = 34,
    JoinPvpRoomResponse = 35,
    RenewPvpRoomRequest = 36,
    RenewPvpRoomResponse = 37,
    LeavePvpRoomRequest = 38,
    LeavePvpRoomResponse = 39,

    GetCharacterManagementRequest = 40,
    GetCharacterManagementResponse = 41,
    ListDeletedCharactersRequest = 42,
    ListDeletedCharactersResponse = 43,
    RenameCharacterRequest = 44,
    RenameCharacterResponse = 45,
    ResetCharacterStatsRequest = 46,
    ResetCharacterStatsResponse = 47,
    DeleteCharacterRequest = 48,
    DeleteCharacterResponse = 49,
    RestoreCharacterRequest = 50,
    RestoreCharacterResponse = 51,
    PurgeDeletedCharacterRequest = 52,
    PurgeDeletedCharacterResponse = 53,

    ErrorResponse = ushort.MaxValue
}
