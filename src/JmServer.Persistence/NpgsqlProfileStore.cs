using System.Security.Cryptography;
using JmServer.Domain;
using Npgsql;
using NpgsqlTypes;

namespace JmServer.Persistence;

public sealed class NpgsqlProfileStore(NpgsqlDataSource dataSource) : IProfileStore
{
    public async Task<ProfileCheckout> CheckoutAsync(
        Guid accountId,
        Guid deviceId,
        Guid selectedCharacterId,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var vault = await ReadForUpdateAsync(
            connection,
            accountId,
            selectedCharacterId,
            cancellationToken);
        if (vault is null)
        {
            throw new VaultException(
                VaultError.CharacterNotFound,
                $"Character '{selectedCharacterId}' was not found.");
        }

        if (vault.LeaseId is not null &&
            vault.LeaseExpiresAt > now &&
            vault.LeasedDeviceId != deviceId)
        {
            throw new VaultException(
                VaultError.LeaseConflict,
                "This account profile is checked out by another device.");
        }

        var files = await ReadFilesAsync(connection, accountId, cancellationToken);
        if (!files.Any(file => string.Equals(
                file.RelativePath,
                vault.SelectedCharacterName + ".d2s",
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(
                $"The server profile does not contain '{vault.SelectedCharacterName}.d2s'.");
        }

        var bundle = ProfileBundleCodec.Encode(files);
        var leaseId = vault.LeaseId is not null && vault.LeasedDeviceId == deviceId
            ? vault.LeaseId.Value
            : Guid.NewGuid();
        var expiresAt = now.Add(leaseDuration);
        await using (var update = connection.CreateCommand())
        {
            update.CommandText =
                """
                UPDATE jm.account_vaults
                SET lease_id = $2, leased_device_id = $3, lease_expires_at = $4
                WHERE account_id = $1;
                """;
            update.Parameters.AddWithValue(accountId);
            update.Parameters.AddWithValue(leaseId);
            update.Parameters.AddWithValue(deviceId);
            update.Parameters.AddWithValue(expiresAt.UtcDateTime);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        await InsertAuditAsync(
            connection,
            accountId,
            deviceId,
            selectedCharacterId,
            "profile.checkout",
            now,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ProfileCheckout(
            selectedCharacterId,
            leaseId,
            vault.Revision,
            expiresAt,
            bundle,
            SHA256.HashData(bundle));
    }

    public async Task<DateTimeOffset> RenewLeaseAsync(
        Guid accountId,
        Guid deviceId,
        Guid leaseId,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        var expiresAt = now.Add(leaseDuration);
        await using var command = dataSource.CreateCommand(
            """
            UPDATE jm.account_vaults
            SET lease_expires_at = $4
            WHERE account_id = $1
              AND leased_device_id = $2
              AND lease_id = $3
            RETURNING account_id;
            """);
        command.Parameters.AddWithValue(accountId);
        command.Parameters.AddWithValue(deviceId);
        command.Parameters.AddWithValue(leaseId);
        command.Parameters.AddWithValue(expiresAt.UtcDateTime);
        if (await command.ExecuteScalarAsync(cancellationToken) is null)
        {
            throw new VaultException(VaultError.LeaseInvalid, "Profile lease is no longer valid.");
        }

        return expiresAt;
    }

    public async Task<ProfileCheckinResult> CheckinAsync(
        Guid accountId,
        Guid deviceId,
        Guid leaseId,
        long expectedRevision,
        ReadOnlyMemory<byte> bundleData,
        ReadOnlyMemory<byte> sha256,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var incomingFiles = ProfileBundleCodec.Decode(bundleData.Span);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var vault = await ReadForUpdateAsync(connection, accountId, cancellationToken);
        if (vault is null || vault.LeaseId != leaseId || vault.LeasedDeviceId != deviceId)
        {
            throw new VaultException(VaultError.LeaseInvalid, "Profile lease is no longer valid.");
        }

        if (vault.Revision != expectedRevision)
        {
            throw new VaultException(
                VaultError.RevisionConflict,
                $"Expected profile revision {expectedRevision}, but server has revision {vault.Revision}.");
        }

        var characters = await ReadCharactersAsync(connection, accountId, cancellationToken);
        ValidateIncomingFiles(incomingFiles, characters);
        var previousFiles = await ReadFilesAsync(connection, accountId, cancellationToken);
        var previousBundle = ProfileBundleCodec.Encode(previousFiles);
        await using (var version = connection.CreateCommand())
        {
            version.CommandText =
                """
                INSERT INTO jm.account_vault_versions (
                    vault_version_id, account_id, revision, bundle_data,
                    bundle_sha256, created_by_device_id, created_at)
                VALUES ($1, $2, $3, $4, $5, $6, $7)
                ON CONFLICT (account_id, revision) DO NOTHING;
                """;
            version.Parameters.AddWithValue(Guid.NewGuid());
            version.Parameters.AddWithValue(accountId);
            version.Parameters.AddWithValue(vault.Revision);
            version.Parameters.AddWithValue(previousBundle);
            version.Parameters.AddWithValue(SHA256.HashData(previousBundle));
            version.Parameters.AddWithValue(deviceId);
            version.Parameters.AddWithValue(now.UtcDateTime);
            await version.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var delete = connection.CreateCommand())
        {
            delete.CommandText = "DELETE FROM jm.account_vault_files WHERE account_id = $1;";
            delete.Parameters.AddWithValue(accountId);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var file in incomingFiles)
        {
            await using var insert = connection.CreateCommand();
            insert.CommandText =
                """
                INSERT INTO jm.account_vault_files (
                    account_id, relative_path, file_data, file_sha256, updated_at)
                VALUES ($1, $2, $3, $4, $5);
                """;
            insert.Parameters.AddWithValue(accountId);
            insert.Parameters.AddWithValue(file.RelativePath);
            insert.Parameters.AddWithValue(file.Data);
            insert.Parameters.AddWithValue(SHA256.HashData(file.Data));
            insert.Parameters.AddWithValue(now.UtcDateTime);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var character in characters)
        {
            var save = incomingFiles.Single(file => string.Equals(
                file.RelativePath,
                character.Name + ".d2s",
                StringComparison.OrdinalIgnoreCase));
            await PreserveAndUpdateCharacterAsync(
                connection,
                character,
                deviceId,
                save.Data,
                now,
                cancellationToken);
        }

        var newRevision = vault.Revision + 1;
        await using (var update = connection.CreateCommand())
        {
            update.CommandText =
                """
                UPDATE jm.account_vaults
                SET revision = $2,
                    lease_id = NULL,
                    leased_device_id = NULL,
                    lease_expires_at = NULL,
                    updated_at = $3
                WHERE account_id = $1;
                """;
            update.Parameters.AddWithValue(accountId);
            update.Parameters.AddWithValue(newRevision);
            update.Parameters.AddWithValue(now.UtcDateTime);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        await InsertAuditAsync(
            connection,
            accountId,
            deviceId,
            null,
            "profile.checkin",
            now,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ProfileCheckinResult(newRevision, sha256.ToArray());
    }

    private static async Task<AccountVaultRow?> ReadForUpdateAsync(
        NpgsqlConnection connection,
        Guid accountId,
        Guid selectedCharacterId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT v.revision, v.lease_id, v.leased_device_id, v.lease_expires_at, c.name
            FROM jm.account_vaults v
            JOIN jm.characters c ON c.account_id = v.account_id
            WHERE v.account_id = $1
              AND c.character_id = $2
              AND c.deleted_at IS NULL
            FOR UPDATE OF v;
            """;
        command.Parameters.AddWithValue(accountId);
        command.Parameters.AddWithValue(selectedCharacterId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadVaultRow(reader, reader.GetString(4));
    }

    private static async Task<AccountVaultRow?> ReadForUpdateAsync(
        NpgsqlConnection connection,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT revision, lease_id, leased_device_id, lease_expires_at
            FROM jm.account_vaults
            WHERE account_id = $1
            FOR UPDATE;
            """;
        command.Parameters.AddWithValue(accountId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadVaultRow(reader, null) : null;
    }

    private static AccountVaultRow ReadVaultRow(NpgsqlDataReader reader, string? selectedCharacterName) =>
        new(
            reader.GetInt64(0),
            reader.IsDBNull(1) ? null : reader.GetGuid(1),
            reader.IsDBNull(2) ? null : reader.GetGuid(2),
            reader.IsDBNull(3)
                ? null
                : new DateTimeOffset(reader.GetDateTime(3), TimeSpan.Zero),
            selectedCharacterName);

    private static async Task<IReadOnlyList<ProfileFile>> ReadFilesAsync(
        NpgsqlConnection connection,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var files = new List<ProfileFile>();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT relative_path, file_data
            FROM jm.account_vault_files
            WHERE account_id = $1
            ORDER BY relative_path;
            """;
        command.Parameters.AddWithValue(accountId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            files.Add(new ProfileFile(reader.GetString(0), reader.GetFieldValue<byte[]>(1)));
        }

        return files;
    }

    private static async Task<IReadOnlyList<CharacterRow>> ReadCharactersAsync(
        NpgsqlConnection connection,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var characters = new List<CharacterRow>();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT character_id, name, revision, save_data, save_sha256
            FROM jm.characters
            WHERE account_id = $1
              AND deleted_at IS NULL
            ORDER BY name;
            """;
        command.Parameters.AddWithValue(accountId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            characters.Add(new CharacterRow(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetInt64(2),
                reader.GetFieldValue<byte[]>(3),
                reader.GetFieldValue<byte[]>(4)));
        }

        return characters;
    }

    private static void ValidateIncomingFiles(
        IReadOnlyList<ProfileFile> files,
        IReadOnlyList<CharacterRow> characters)
    {
        var registeredNames = characters
            .Select(character => character.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var incomingNames = files
            .Where(file => ProfileSavePolicy.IsCharacterSave(file.RelativePath))
            .Select(file => Path.GetFileNameWithoutExtension(file.RelativePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!registeredNames.SetEquals(incomingNames))
        {
            throw new InvalidDataException(
                "Profile check-in must contain exactly the server-registered character saves.");
        }

        foreach (var file in files.Where(file => ProfileSavePolicy.IsCharacterCompanion(file.RelativePath)))
        {
            if (!registeredNames.Any(
                    characterName => ProfileSavePolicy.BelongsToCharacter(
                        file.RelativePath,
                        characterName)))
            {
                throw new InvalidDataException(
                    $"Companion file '{file.RelativePath}' does not belong to a registered character.");
            }
        }
    }

    private static async Task PreserveAndUpdateCharacterAsync(
        NpgsqlConnection connection,
        CharacterRow character,
        Guid deviceId,
        byte[] saveData,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using (var version = connection.CreateCommand())
        {
            version.CommandText =
                """
                INSERT INTO jm.character_versions (
                    character_version_id, character_id, revision, save_data,
                    save_sha256, created_by_device_id, created_at)
                VALUES ($1, $2, $3, $4, $5, $6, $7)
                ON CONFLICT (character_id, revision) DO NOTHING;
                """;
            version.Parameters.AddWithValue(Guid.NewGuid());
            version.Parameters.AddWithValue(character.CharacterId);
            version.Parameters.AddWithValue(character.Revision);
            version.Parameters.AddWithValue(character.SaveData);
            version.Parameters.AddWithValue(character.Sha256);
            version.Parameters.AddWithValue(deviceId);
            version.Parameters.AddWithValue(now.UtcDateTime);
            await version.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var update = connection.CreateCommand();
        update.CommandText =
            """
            UPDATE jm.characters
            SET save_data = $2,
                save_sha256 = $3,
                revision = revision + 1,
                lease_id = NULL,
                leased_device_id = NULL,
                lease_expires_at = NULL,
                updated_at = $4
            WHERE character_id = $1;
            """;
        update.Parameters.AddWithValue(character.CharacterId);
        update.Parameters.AddWithValue(saveData);
        update.Parameters.AddWithValue(SHA256.HashData(saveData));
        update.Parameters.AddWithValue(now.UtcDateTime);
        await update.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertAuditAsync(
        NpgsqlConnection connection,
        Guid accountId,
        Guid deviceId,
        Guid? characterId,
        string eventType,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO jm.audit_events (
                account_id, device_id, character_id, event_type, created_at)
            VALUES ($1, $2, $3, $4, $5);
            """;
        command.Parameters.AddWithValue(accountId);
        command.Parameters.AddWithValue(deviceId);
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Uuid,
            Value = characterId.HasValue ? characterId.Value : DBNull.Value
        });
        command.Parameters.AddWithValue(eventType);
        command.Parameters.AddWithValue(now.UtcDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed record AccountVaultRow(
        long Revision,
        Guid? LeaseId,
        Guid? LeasedDeviceId,
        DateTimeOffset? LeaseExpiresAt,
        string? SelectedCharacterName);

    private sealed record CharacterRow(
        Guid CharacterId,
        string Name,
        long Revision,
        byte[] SaveData,
        byte[] Sha256);
}
