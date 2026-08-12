using System.Security.Cryptography;
using System.Text.Json;
using JmServer.Domain;
using Npgsql;
using NpgsqlTypes;

namespace JmServer.Persistence;

public sealed class NpgsqlCharacterStore(
    NpgsqlDataSource dataSource,
    ISharedStashProvisioner? sharedStashProvisioner = null) : ICharacterStore
{
    public async Task<IReadOnlyList<VaultCharacterSummary>> ListAsync(
        Guid accountId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var characters = new List<VaultCharacterSummary>();
        await using var command = dataSource.CreateCommand(
            """
            SELECT c.character_id, c.name, c.character_class, v.revision,
                   v.lease_id, v.lease_expires_at
            FROM jm.characters c
            JOIN jm.account_vaults v ON v.account_id = c.account_id
            WHERE c.account_id = $1
              AND c.deleted_at IS NULL
            ORDER BY c.name;
            """);
        command.Parameters.AddWithValue(accountId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var leaseExpiresAt = reader.IsDBNull(5)
                ? (DateTimeOffset?)null
                : new DateTimeOffset(reader.GetDateTime(5), TimeSpan.Zero);
            var activeLease = !reader.IsDBNull(4) && leaseExpiresAt > now;
            characters.Add(new VaultCharacterSummary(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt64(3),
                activeLease,
                activeLease ? leaseExpiresAt : null));
        }

        return characters;
    }

    public async Task<VaultCharacterSummary> CreateAsync(
        Guid accountId,
        Guid deviceId,
        NewVaultCharacter character,
        int maximumCharacters,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(character);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        long currentRevision;
        await using (var vault = connection.CreateCommand())
        {
            vault.CommandText =
                """
                SELECT revision, lease_id, lease_expires_at
                FROM jm.account_vaults
                WHERE account_id = $1
                FOR UPDATE;
                """;
            vault.Parameters.AddWithValue(accountId);
            await using var reader = await vault.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new VaultException(
                    VaultError.CharacterCreationInvalid,
                    "계정의 캐릭터 보관소를 찾을 수 없습니다.");
            }

            currentRevision = reader.GetInt64(0);
            var hasActiveLease = !reader.IsDBNull(1) &&
                                 !reader.IsDBNull(2) &&
                                 new DateTimeOffset(reader.GetDateTime(2), TimeSpan.Zero) > now;
            if (hasActiveLease)
            {
                throw new VaultException(
                    VaultError.LeaseConflict,
                    "플레이 중이거나 복구 대기 중인 프로필에는 캐릭터를 생성할 수 없습니다.");
            }
        }

        await using (var count = connection.CreateCommand())
        {
            count.CommandText =
                """
                SELECT count(*) FILTER (WHERE deleted_at IS NULL),
                       count(*) FILTER (WHERE upper(name) = upper($2))
                FROM jm.characters
                WHERE account_id = $1;
                """;
            count.Parameters.AddWithValue(accountId);
            count.Parameters.AddWithValue(character.Name);
            await using var reader = await count.ExecuteReaderAsync(cancellationToken);
            _ = await reader.ReadAsync(cancellationToken);
            if (reader.GetInt64(1) > 0)
            {
                throw new VaultException(
                    VaultError.CharacterAlreadyExists,
                    $"'{character.Name}' 캐릭터가 이미 존재합니다.");
            }

            if (reader.GetInt64(0) >= maximumCharacters)
            {
                throw new VaultException(
                    VaultError.CharacterLimitReached,
                    $"계정당 캐릭터는 최대 {maximumCharacters}명까지 생성할 수 있습니다.");
            }
        }

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
            version.Parameters.AddWithValue(currentRevision);
            version.Parameters.AddWithValue(previousBundle);
            version.Parameters.AddWithValue(SHA256.HashData(previousBundle));
            version.Parameters.AddWithValue(deviceId);
            version.Parameters.AddWithValue(now.UtcDateTime);
            await version.ExecuteNonQueryAsync(cancellationToken);
        }

        var characterId = Guid.NewGuid();
        var saveHash = SHA256.HashData(character.SaveData);
        try
        {
            await using var insert = connection.CreateCommand();
            insert.CommandText =
                """
                INSERT INTO jm.characters (
                    character_id, account_id, name, character_class, save_data,
                    save_sha256, revision, created_at, updated_at)
                VALUES ($1, $2, $3, $4, $5, $6, 0, $7, $7);
                """;
            insert.Parameters.AddWithValue(characterId);
            insert.Parameters.AddWithValue(accountId);
            insert.Parameters.AddWithValue(character.Name);
            insert.Parameters.AddWithValue(character.CharacterClass.ToString());
            insert.Parameters.AddWithValue(character.SaveData);
            insert.Parameters.AddWithValue(saveHash);
            insert.Parameters.AddWithValue(now.UtcDateTime);
            await insert.ExecuteNonQueryAsync(cancellationToken);

            await using var insertFile = connection.CreateCommand();
            insertFile.CommandText =
                """
                INSERT INTO jm.account_vault_files (
                    account_id, relative_path, file_data, file_sha256, updated_at)
                VALUES ($1, $2, $3, $4, $5);
                """;
            insertFile.Parameters.AddWithValue(accountId);
            insertFile.Parameters.AddWithValue(character.Name + ".d2s");
            insertFile.Parameters.AddWithValue(character.SaveData);
            insertFile.Parameters.AddWithValue(saveHash);
            insertFile.Parameters.AddWithValue(now.UtcDateTime);
            await insertFile.ExecuteNonQueryAsync(cancellationToken);

            var existingSharedStash =
                ProfileSavePolicy.SelectPreferredSoftcoreSharedStash(previousFiles);
            var provisionedSharedStash = existingSharedStash is null
                ? character.InitialSharedStash
                : sharedStashProvisioner?.ProvisionSharedStash(existingSharedStash);
            if (provisionedSharedStash is not null)
            {
                var stashHash = SHA256.HashData(provisionedSharedStash.Data);
                await using var insertStash = connection.CreateCommand();
                insertStash.CommandText =
                    """
                    INSERT INTO jm.account_vault_files (
                        account_id, relative_path, file_data, file_sha256, updated_at)
                    VALUES ($1, $2, $3, $4, $5)
                    ON CONFLICT (account_id, relative_path) DO UPDATE
                    SET file_data = EXCLUDED.file_data,
                        file_sha256 = EXCLUDED.file_sha256,
                        updated_at = EXCLUDED.updated_at;
                    """;
                insertStash.Parameters.AddWithValue(accountId);
                insertStash.Parameters.AddWithValue(provisionedSharedStash.RelativePath);
                insertStash.Parameters.AddWithValue(provisionedSharedStash.Data);
                insertStash.Parameters.AddWithValue(stashHash);
                insertStash.Parameters.AddWithValue(now.UtcDateTime);
                await insertStash.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        catch (PostgresException exception) when (
            exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new VaultException(
                VaultError.CharacterAlreadyExists,
                $"'{character.Name}' 캐릭터가 이미 존재합니다.");
        }

        var newRevision = currentRevision + 1;
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

        await using (var audit = connection.CreateCommand())
        {
            audit.CommandText =
                """
                INSERT INTO jm.audit_events (
                    account_id, device_id, character_id, event_type, details, created_at)
                VALUES ($1, $2, $3, 'character.create', $4, $5);
                """;
            audit.Parameters.AddWithValue(accountId);
            audit.Parameters.AddWithValue(deviceId);
            audit.Parameters.AddWithValue(characterId);
            audit.Parameters.Add(new NpgsqlParameter
            {
                NpgsqlDbType = NpgsqlDbType.Jsonb,
                Value = JsonSerializer.Serialize(new
                {
                    name = character.Name,
                    characterClass = character.CharacterClass.ToString(),
                    preset = character.Preset.ToString(),
                    level = character.Level,
                    sha256 = Convert.ToHexString(saveHash)
                })
            });
            audit.Parameters.AddWithValue(now.UtcDateTime);
            await audit.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new VaultCharacterSummary(
            characterId,
            character.Name,
            character.CharacterClass.ToString(),
            newRevision,
            IsLeased: false,
            LeaseExpiresAt: null);
    }

    public async Task<VaultCharacterData> GetAsync(
        Guid accountId,
        Guid characterId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT c.character_id, c.name, c.character_class, v.revision,
                   v.lease_id, v.lease_expires_at, c.save_data
            FROM jm.characters c
            JOIN jm.account_vaults v ON v.account_id = c.account_id
            WHERE c.account_id = $1
              AND c.character_id = $2
              AND c.deleted_at IS NULL;
            """);
        command.Parameters.AddWithValue(accountId);
        command.Parameters.AddWithValue(characterId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new VaultException(VaultError.CharacterNotFound, "캐릭터를 찾을 수 없습니다.");
        }

        var leaseExpiresAt = reader.IsDBNull(5)
            ? (DateTimeOffset?)null
            : new DateTimeOffset(reader.GetDateTime(5), TimeSpan.Zero);
        var activeLease = !reader.IsDBNull(4) && leaseExpiresAt > now;
        return new VaultCharacterData(
            reader.GetGuid(0),
            reader.GetString(1),
            Enum.Parse<VaultCharacterClass>(reader.GetString(2), ignoreCase: false),
            reader.GetInt64(3),
            activeLease,
            activeLease ? leaseExpiresAt : null,
            reader.GetFieldValue<byte[]>(6));
    }

    public async Task<IReadOnlyList<DeletedVaultCharacterSummary>> ListDeletedAsync(
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var characters = new List<DeletedVaultCharacterSummary>();
        await using var command = dataSource.CreateCommand(
            """
            SELECT character_id, name, character_class, deleted_at
            FROM jm.characters
            WHERE account_id = $1
              AND deleted_at IS NOT NULL
            ORDER BY deleted_at DESC;
            """);
        command.Parameters.AddWithValue(accountId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            characters.Add(new DeletedVaultCharacterSummary(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                new DateTimeOffset(reader.GetDateTime(3), TimeSpan.Zero)));
        }

        return characters;
    }

    public async Task<VaultCharacterSummary> RenameAsync(
        Guid accountId,
        Guid deviceId,
        Guid characterId,
        long expectedVaultRevision,
        string newName,
        ReadOnlyMemory<byte> saveData,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var vaultRevision = await LockVaultAsync(
            connection, accountId, expectedVaultRevision, now, cancellationToken);
        var character = await ReadCharacterForUpdateAsync(
            connection, accountId, characterId, deleted: false, cancellationToken);
        await EnsureNameAvailableAsync(
            connection, accountId, characterId, newName, cancellationToken);
        await PreserveVaultAsync(connection, accountId, deviceId, vaultRevision, now, cancellationToken);
        await PreserveCharacterAsync(connection, character, deviceId, now, cancellationToken);

        var files = await ReadCharacterFilesAsync(
            connection, accountId, character.Name, cancellationToken);
        if (!files.Any(file => ProfileSavePolicy.IsCharacterSave(file.RelativePath)))
        {
            throw new InvalidDataException($"'{character.Name}.d2s'가 서버 프로필에 없습니다.");
        }

        foreach (var file in files)
        {
            var newPath = newName + Path.GetExtension(file.RelativePath);
            var newData = ProfileSavePolicy.IsCharacterSave(file.RelativePath)
                ? saveData.ToArray()
                : file.Data;
            await using (var deleteOld = connection.CreateCommand())
            {
                deleteOld.CommandText =
                    "DELETE FROM jm.account_vault_files WHERE account_id = $1 AND relative_path = $2;";
                deleteOld.Parameters.AddWithValue(accountId);
                deleteOld.Parameters.AddWithValue(file.RelativePath);
                await deleteOld.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var insertNew = connection.CreateCommand();
            insertNew.CommandText =
                """
                INSERT INTO jm.account_vault_files (
                    account_id, relative_path, file_data, file_sha256, updated_at)
                VALUES ($1, $2, $3, $4, $5);
                """;
            insertNew.Parameters.AddWithValue(accountId);
            insertNew.Parameters.AddWithValue(newPath);
            insertNew.Parameters.AddWithValue(newData);
            insertNew.Parameters.AddWithValue(SHA256.HashData(newData));
            insertNew.Parameters.AddWithValue(now.UtcDateTime);
            await insertNew.ExecuteNonQueryAsync(cancellationToken);
        }

        var saveBytes = saveData.ToArray();
        await using (var update = connection.CreateCommand())
        {
            update.CommandText =
                """
                UPDATE jm.characters
                SET name = $2,
                    save_data = $3,
                    save_sha256 = $4,
                    revision = revision + 1,
                    updated_at = $5
                WHERE character_id = $1;
                """;
            update.Parameters.AddWithValue(characterId);
            update.Parameters.AddWithValue(newName);
            update.Parameters.AddWithValue(saveBytes);
            update.Parameters.AddWithValue(SHA256.HashData(saveBytes));
            update.Parameters.AddWithValue(now.UtcDateTime);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        var newRevision = await AdvanceVaultAsync(
            connection, accountId, vaultRevision, now, cancellationToken);
        await InsertAuditAsync(
            connection,
            accountId,
            deviceId,
            characterId,
            "character.rename",
            new { oldName = character.Name, newName },
            now,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new VaultCharacterSummary(
            characterId, newName, character.CharacterClass, newRevision, false, null);
    }

    public async Task<VaultCharacterSummary> ResetStatsAsync(
        Guid accountId,
        Guid deviceId,
        Guid characterId,
        long expectedVaultRevision,
        CharacterPrimaryStats stats,
        ReadOnlyMemory<byte> saveData,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var vaultRevision = await LockVaultAsync(
            connection, accountId, expectedVaultRevision, now, cancellationToken);
        var character = await ReadCharacterForUpdateAsync(
            connection, accountId, characterId, deleted: false, cancellationToken);
        await PreserveVaultAsync(connection, accountId, deviceId, vaultRevision, now, cancellationToken);
        await PreserveCharacterAsync(connection, character, deviceId, now, cancellationToken);

        var saveBytes = saveData.ToArray();
        await using (var updateFile = connection.CreateCommand())
        {
            updateFile.CommandText =
                """
                UPDATE jm.account_vault_files
                SET file_data = $3, file_sha256 = $4, updated_at = $5
                WHERE account_id = $1 AND lower(relative_path) = lower($2);
                """;
            updateFile.Parameters.AddWithValue(accountId);
            updateFile.Parameters.AddWithValue(character.Name + ".d2s");
            updateFile.Parameters.AddWithValue(saveBytes);
            updateFile.Parameters.AddWithValue(SHA256.HashData(saveBytes));
            updateFile.Parameters.AddWithValue(now.UtcDateTime);
            if (await updateFile.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidDataException($"'{character.Name}.d2s'가 서버 프로필에 없습니다.");
            }
        }

        await using (var updateCharacter = connection.CreateCommand())
        {
            updateCharacter.CommandText =
                """
                UPDATE jm.characters
                SET save_data = $2,
                    save_sha256 = $3,
                    revision = revision + 1,
                    updated_at = $4
                WHERE character_id = $1;
                """;
            updateCharacter.Parameters.AddWithValue(characterId);
            updateCharacter.Parameters.AddWithValue(saveBytes);
            updateCharacter.Parameters.AddWithValue(SHA256.HashData(saveBytes));
            updateCharacter.Parameters.AddWithValue(now.UtcDateTime);
            await updateCharacter.ExecuteNonQueryAsync(cancellationToken);
        }

        var newRevision = await AdvanceVaultAsync(
            connection, accountId, vaultRevision, now, cancellationToken);
        await InsertAuditAsync(
            connection,
            accountId,
            deviceId,
            characterId,
            "character.stats.reset",
            stats,
            now,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new VaultCharacterSummary(
            characterId, character.Name, character.CharacterClass, newRevision, false, null);
    }

    public async Task DeleteAsync(
        Guid accountId,
        Guid deviceId,
        Guid characterId,
        long expectedVaultRevision,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var vaultRevision = await LockVaultAsync(
            connection, accountId, expectedVaultRevision, now, cancellationToken);
        var character = await ReadCharacterForUpdateAsync(
            connection, accountId, characterId, deleted: false, cancellationToken);
        await PreserveVaultAsync(connection, accountId, deviceId, vaultRevision, now, cancellationToken);
        await PreserveCharacterAsync(connection, character, deviceId, now, cancellationToken);
        var files = await ReadCharacterFilesAsync(
            connection, accountId, character.Name, cancellationToken);
        if (!files.Any(file => ProfileSavePolicy.IsCharacterSave(file.RelativePath)))
        {
            throw new InvalidDataException($"'{character.Name}.d2s'가 서버 프로필에 없습니다.");
        }

        await using (var clearTrash = connection.CreateCommand())
        {
            clearTrash.CommandText = "DELETE FROM jm.character_trash_files WHERE character_id = $1;";
            clearTrash.Parameters.AddWithValue(characterId);
            await clearTrash.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var file in files)
        {
            await using (var insertTrash = connection.CreateCommand())
            {
                insertTrash.CommandText =
                """
                INSERT INTO jm.character_trash_files (
                    character_id, relative_path, file_data, file_sha256, deleted_at)
                VALUES ($1, $2, $3, $4, $5);
                """;
                insertTrash.Parameters.AddWithValue(characterId);
                insertTrash.Parameters.AddWithValue(file.RelativePath);
                insertTrash.Parameters.AddWithValue(file.Data);
                insertTrash.Parameters.AddWithValue(SHA256.HashData(file.Data));
                insertTrash.Parameters.AddWithValue(now.UtcDateTime);
                await insertTrash.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var deleteActive = connection.CreateCommand();
            deleteActive.CommandText =
                "DELETE FROM jm.account_vault_files WHERE account_id = $1 AND relative_path = $2;";
            deleteActive.Parameters.AddWithValue(accountId);
            deleteActive.Parameters.AddWithValue(file.RelativePath);
            await deleteActive.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var delete = connection.CreateCommand())
        {
            delete.CommandText =
                """
                UPDATE jm.characters
                SET deleted_at = $2, updated_at = $2
                WHERE character_id = $1;
                """;
            delete.Parameters.AddWithValue(characterId);
            delete.Parameters.AddWithValue(now.UtcDateTime);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        _ = await AdvanceVaultAsync(connection, accountId, vaultRevision, now, cancellationToken);
        await InsertAuditAsync(
            connection,
            accountId,
            deviceId,
            characterId,
            "character.delete",
            new { character.Name },
            now,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<VaultCharacterSummary> RestoreAsync(
        Guid accountId,
        Guid deviceId,
        Guid characterId,
        int maximumCharacters,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var vaultRevision = await LockVaultAsync(
            connection, accountId, expectedRevision: null, now, cancellationToken);
        var character = await ReadCharacterForUpdateAsync(
            connection, accountId, characterId, deleted: true, cancellationToken);

        await using (var count = connection.CreateCommand())
        {
            count.CommandText =
                "SELECT count(*) FROM jm.characters WHERE account_id = $1 AND deleted_at IS NULL;";
            count.Parameters.AddWithValue(accountId);
            if ((long)(await count.ExecuteScalarAsync(cancellationToken) ?? 0L) >= maximumCharacters)
            {
                throw new VaultException(
                    VaultError.CharacterLimitReached,
                    $"계정 캐릭터는 최대 {maximumCharacters}명까지 복구할 수 있습니다.");
            }
        }

        var files = await ReadTrashFilesAsync(connection, characterId, cancellationToken);
        if (!files.Any(file => ProfileSavePolicy.IsCharacterSave(file.RelativePath)))
        {
            throw new VaultException(VaultError.CharacterDeleted, "삭제 캐릭터의 보존 파일이 없습니다.");
        }

        await PreserveVaultAsync(connection, accountId, deviceId, vaultRevision, now, cancellationToken);
        foreach (var file in files)
        {
            await using var restore = connection.CreateCommand();
            restore.CommandText =
                """
                INSERT INTO jm.account_vault_files (
                    account_id, relative_path, file_data, file_sha256, updated_at)
                VALUES ($1, $2, $3, $4, $5);
                """;
            restore.Parameters.AddWithValue(accountId);
            restore.Parameters.AddWithValue(file.RelativePath);
            restore.Parameters.AddWithValue(file.Data);
            restore.Parameters.AddWithValue(SHA256.HashData(file.Data));
            restore.Parameters.AddWithValue(now.UtcDateTime);
            await restore.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var restoreCharacter = connection.CreateCommand())
        {
            restoreCharacter.CommandText =
                "UPDATE jm.characters SET deleted_at = NULL, updated_at = $2 WHERE character_id = $1;";
            restoreCharacter.Parameters.AddWithValue(characterId);
            restoreCharacter.Parameters.AddWithValue(now.UtcDateTime);
            await restoreCharacter.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var clearTrash = connection.CreateCommand())
        {
            clearTrash.CommandText = "DELETE FROM jm.character_trash_files WHERE character_id = $1;";
            clearTrash.Parameters.AddWithValue(characterId);
            await clearTrash.ExecuteNonQueryAsync(cancellationToken);
        }

        var newRevision = await AdvanceVaultAsync(
            connection, accountId, vaultRevision, now, cancellationToken);
        await InsertAuditAsync(
            connection,
            accountId,
            deviceId,
            characterId,
            "character.restore",
            new { character.Name },
            now,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new VaultCharacterSummary(
            characterId, character.Name, character.CharacterClass, newRevision, false, null);
    }

    public async Task PurgeDeletedAsync(
        Guid accountId,
        Guid deviceId,
        Guid characterId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        _ = await LockVaultAsync(
            connection, accountId, expectedRevision: null, now, cancellationToken);
        var character = await ReadCharacterForUpdateAsync(
            connection, accountId, characterId, deleted: true, cancellationToken);

        await using (var delete = connection.CreateCommand())
        {
            delete.CommandText =
                "DELETE FROM jm.characters WHERE account_id = $1 AND character_id = $2;";
            delete.Parameters.AddWithValue(accountId);
            delete.Parameters.AddWithValue(characterId);
            if (await delete.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new VaultException(VaultError.CharacterDeleted, "영구 삭제할 캐릭터가 없습니다.");
            }
        }

        await InsertAuditAsync(
            connection,
            accountId,
            deviceId,
            characterId,
            "character.purge",
            new { character.Name },
            now,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<long> LockVaultAsync(
        NpgsqlConnection connection,
        Guid accountId,
        long? expectedRevision,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT revision, lease_id, lease_expires_at
            FROM jm.account_vaults
            WHERE account_id = $1
            FOR UPDATE;
            """;
        command.Parameters.AddWithValue(accountId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new VaultException(VaultError.CharacterNotFound, "계정 프로필을 찾을 수 없습니다.");
        }

        var revision = reader.GetInt64(0);
        if (expectedRevision.HasValue && revision != expectedRevision.Value)
        {
            throw new VaultException(
                VaultError.RevisionConflict,
                $"예상 프로필 리비전은 {expectedRevision.Value}이지만 서버는 {revision}입니다.");
        }

        var activeLease = !reader.IsDBNull(1) &&
                          !reader.IsDBNull(2) &&
                          new DateTimeOffset(reader.GetDateTime(2), TimeSpan.Zero) > now;
        if (activeLease)
        {
            throw new VaultException(
                VaultError.LeaseConflict,
                "게임 실행 또는 복구 대기 중에는 캐릭터를 관리할 수 없습니다.");
        }

        return revision;
    }

    private static async Task<ManagedCharacterRow> ReadCharacterForUpdateAsync(
        NpgsqlConnection connection,
        Guid accountId,
        Guid characterId,
        bool deleted,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
             SELECT character_id, name, character_class, revision,
                    save_data, save_sha256, deleted_at
             FROM jm.characters
             WHERE account_id = $1
               AND character_id = $2
               AND deleted_at IS {(deleted ? "NOT NULL" : "NULL")}
             FOR UPDATE;
             """;
        command.Parameters.AddWithValue(accountId);
        command.Parameters.AddWithValue(characterId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new VaultException(
                deleted ? VaultError.CharacterDeleted : VaultError.CharacterNotFound,
                deleted ? "복구할 삭제 캐릭터를 찾을 수 없습니다." : "캐릭터를 찾을 수 없습니다.");
        }

        return new ManagedCharacterRow(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt64(3),
            reader.GetFieldValue<byte[]>(4),
            reader.GetFieldValue<byte[]>(5));
    }

    private static async Task EnsureNameAvailableAsync(
        NpgsqlConnection connection,
        Guid accountId,
        Guid characterId,
        string name,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT EXISTS (
                SELECT 1
                FROM jm.characters
                WHERE account_id = $1
                  AND character_id <> $2
                  AND upper(name) = upper($3));
            """;
        command.Parameters.AddWithValue(accountId);
        command.Parameters.AddWithValue(characterId);
        command.Parameters.AddWithValue(name);
        if ((bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false))
        {
            throw new VaultException(
                VaultError.CharacterAlreadyExists,
                $"'{name}' 캐릭터가 이미 존재하거나 휴지통에 있습니다.");
        }
    }

    private static async Task PreserveVaultAsync(
        NpgsqlConnection connection,
        Guid accountId,
        Guid deviceId,
        long revision,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var files = await ReadFilesAsync(connection, accountId, cancellationToken);
        var bundle = ProfileBundleCodec.Encode(files);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO jm.account_vault_versions (
                vault_version_id, account_id, revision, bundle_data,
                bundle_sha256, created_by_device_id, created_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7)
            ON CONFLICT (account_id, revision) DO NOTHING;
            """;
        command.Parameters.AddWithValue(Guid.NewGuid());
        command.Parameters.AddWithValue(accountId);
        command.Parameters.AddWithValue(revision);
        command.Parameters.AddWithValue(bundle);
        command.Parameters.AddWithValue(SHA256.HashData(bundle));
        command.Parameters.AddWithValue(deviceId);
        command.Parameters.AddWithValue(now.UtcDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task PreserveCharacterAsync(
        NpgsqlConnection connection,
        ManagedCharacterRow character,
        Guid deviceId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO jm.character_versions (
                character_version_id, character_id, revision, save_data,
                save_sha256, created_by_device_id, created_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7)
            ON CONFLICT (character_id, revision) DO NOTHING;
            """;
        command.Parameters.AddWithValue(Guid.NewGuid());
        command.Parameters.AddWithValue(character.CharacterId);
        command.Parameters.AddWithValue(character.Revision);
        command.Parameters.AddWithValue(character.SaveData);
        command.Parameters.AddWithValue(character.Sha256);
        command.Parameters.AddWithValue(deviceId);
        command.Parameters.AddWithValue(now.UtcDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long> AdvanceVaultAsync(
        NpgsqlConnection connection,
        Guid accountId,
        long currentRevision,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var newRevision = checked(currentRevision + 1);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE jm.account_vaults
            SET revision = $2,
                lease_id = NULL,
                leased_device_id = NULL,
                lease_expires_at = NULL,
                updated_at = $3
            WHERE account_id = $1;
            """;
        command.Parameters.AddWithValue(accountId);
        command.Parameters.AddWithValue(newRevision);
        command.Parameters.AddWithValue(now.UtcDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return newRevision;
    }

    private static async Task<IReadOnlyList<ProfileFile>> ReadCharacterFilesAsync(
        NpgsqlConnection connection,
        Guid accountId,
        string characterName,
        CancellationToken cancellationToken)
    {
        var files = await ReadFilesAsync(connection, accountId, cancellationToken);
        return files.Where(file =>
                !ProfileSavePolicy.IsSharedStash(file.RelativePath) &&
                ProfileSavePolicy.BelongsToCharacter(file.RelativePath, characterName))
            .ToArray();
    }

    private static async Task<IReadOnlyList<ProfileFile>> ReadTrashFilesAsync(
        NpgsqlConnection connection,
        Guid characterId,
        CancellationToken cancellationToken)
    {
        var files = new List<ProfileFile>();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT relative_path, file_data
            FROM jm.character_trash_files
            WHERE character_id = $1
            ORDER BY relative_path;
            """;
        command.Parameters.AddWithValue(characterId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            files.Add(new ProfileFile(reader.GetString(0), reader.GetFieldValue<byte[]>(1)));
        }

        return files;
    }

    private static async Task InsertAuditAsync(
        NpgsqlConnection connection,
        Guid accountId,
        Guid deviceId,
        Guid characterId,
        string eventType,
        object details,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO jm.audit_events (
                account_id, device_id, character_id, event_type, details, created_at)
            VALUES ($1, $2, $3, $4, $5, $6);
            """;
        command.Parameters.AddWithValue(accountId);
        command.Parameters.AddWithValue(deviceId);
        command.Parameters.AddWithValue(characterId);
        command.Parameters.AddWithValue(eventType);
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Jsonb,
            Value = JsonSerializer.Serialize(details)
        });
        command.Parameters.AddWithValue(now.UtcDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

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

    private sealed record ManagedCharacterRow(
        Guid CharacterId,
        string Name,
        string CharacterClass,
        long Revision,
        byte[] SaveData,
        byte[] Sha256);
}
