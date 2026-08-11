namespace JmServer.Domain;

public interface IDeviceStore
{
    Task<DeviceIdentity?> AuthenticateAsync(
        Guid deviceId,
        ReadOnlyMemory<byte> tokenHash,
        CancellationToken cancellationToken);
}

public interface ICharacterStore
{
    Task<IReadOnlyList<VaultCharacterSummary>> ListAsync(
        Guid accountId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<VaultCharacterSummary> CreateAsync(
        Guid accountId,
        Guid deviceId,
        NewVaultCharacter character,
        int maximumCharacters,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<VaultCharacterData> GetAsync(
        Guid accountId,
        Guid characterId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<DeletedVaultCharacterSummary>> ListDeletedAsync(
        Guid accountId,
        CancellationToken cancellationToken);

    Task<VaultCharacterSummary> RenameAsync(
        Guid accountId,
        Guid deviceId,
        Guid characterId,
        long expectedVaultRevision,
        string newName,
        ReadOnlyMemory<byte> saveData,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<VaultCharacterSummary> ResetStatsAsync(
        Guid accountId,
        Guid deviceId,
        Guid characterId,
        long expectedVaultRevision,
        CharacterPrimaryStats stats,
        ReadOnlyMemory<byte> saveData,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        Guid accountId,
        Guid deviceId,
        Guid characterId,
        long expectedVaultRevision,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<VaultCharacterSummary> RestoreAsync(
        Guid accountId,
        Guid deviceId,
        Guid characterId,
        int maximumCharacters,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task PurgeDeletedAsync(
        Guid accountId,
        Guid deviceId,
        Guid characterId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public interface ICharacterSaveFactory
{
    byte[] Create(CharacterSaveRequest request);

    ProfileFile? CreateInitialSharedStash() => null;
}

public interface ISharedStashProvisioner
{
    ProfileFile ProvisionSharedStash(ProfileFile? existingStash);
}

public interface ICharacterSaveEditor
{
    CharacterPrimaryStats ReadPrimaryStats(ReadOnlyMemory<byte> saveData);

    byte[] Rename(ReadOnlyMemory<byte> saveData, string newName);

    byte[] ResetPrimaryStats(ReadOnlyMemory<byte> saveData, VaultCharacterClass characterClass);
}

public interface IProfileStore
{
    Task<ProfileCheckout> CheckoutAsync(
        Guid accountId,
        Guid deviceId,
        Guid selectedCharacterId,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task<DateTimeOffset> RenewLeaseAsync(
        Guid accountId,
        Guid deviceId,
        Guid leaseId,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task<ProfileCheckinResult> CheckinAsync(
        Guid accountId,
        Guid deviceId,
        Guid leaseId,
        long expectedRevision,
        ReadOnlyMemory<byte> bundleData,
        ReadOnlyMemory<byte> sha256,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
