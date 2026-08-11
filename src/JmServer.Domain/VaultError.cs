namespace JmServer.Domain;

public enum VaultError
{
    CharacterNotFound,
    CharacterNameInvalid,
    CharacterAlreadyExists,
    CharacterLimitReached,
    CharacterCreationInvalid,
    CharacterDeleted,
    CharacterManagementInvalid,
    PvpRoomNotFound,
    PvpRoomFull,
    PvpRoomConflict,
    PvpEndpointInvalid,
    LeaseConflict,
    LeaseInvalid,
    RevisionConflict,
    SaveTooLarge,
    ChecksumMismatch
}

public sealed class VaultException : Exception
{
    public VaultException(VaultError error, string message)
        : base(message)
    {
        Error = error;
    }

    public VaultError Error { get; }
}
