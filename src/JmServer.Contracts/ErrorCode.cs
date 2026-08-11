namespace JmServer.Contracts;

public enum ErrorCode
{
    Unknown = 0,
    InvalidPacket = 1,
    UnsupportedProtocol = 2,
    AuthenticationRequired = 3,
    AuthenticationFailed = 4,
    CharacterNotFound = 10,
    CharacterLeaseConflict = 11,
    CharacterLeaseInvalid = 12,
    CharacterRevisionConflict = 13,
    SaveTooLarge = 14,
    SaveChecksumMismatch = 15,
    CharacterNameInvalid = 16,
    CharacterAlreadyExists = 17,
    CharacterLimitReached = 18,
    CharacterCreationInvalid = 19,
    DatabaseUnavailable = 20,
    CharacterDeleted = 21,
    CharacterManagementInvalid = 22,
    PvpRoomNotFound = 30,
    PvpRoomFull = 31,
    PvpRoomConflict = 32,
    PvpEndpointInvalid = 33,
    InternalServerError = 99
}
