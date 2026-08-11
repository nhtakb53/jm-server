using System.Security.Cryptography;
using System.Text.Json;
using JmServer.Domain;
using Npgsql;
using NpgsqlTypes;

namespace JmServer.Persistence;

public sealed class AdministrationStore(NpgsqlDataSource dataSource)
{
    public async Task<Guid> RenameAccountAsync(
        string currentUsername,
        string newUsername,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentUsername);
        ArgumentException.ThrowIfNullOrWhiteSpace(newUsername);

        currentUsername = currentUsername.Trim();
        newUsername = newUsername.Trim();
        await using var command = dataSource.CreateCommand(
            """
            UPDATE jm.accounts
            SET username = $2, username_normalized = $3
            WHERE username_normalized = $1
            RETURNING account_id;
            """);
        command.Parameters.AddWithValue(currentUsername.ToUpperInvariant());
        command.Parameters.AddWithValue(newUsername);
        command.Parameters.AddWithValue(newUsername.ToUpperInvariant());
        return (Guid)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException($"Account '{currentUsername}' does not exist."));
    }

    public async Task<ProvisionedDevice> ProvisionDeviceAsync(
        string username,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        username = username.Trim();
        displayName = displayName.Trim();
        var normalized = username.ToUpperInvariant();
        var accountId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var token = TokenHasher.GenerateToken();
        var tokenHash = TokenHasher.Hash(token);
        var now = DateTime.UtcNow;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var account = connection.CreateCommand())
        {
            account.CommandText =
                """
                INSERT INTO jm.accounts (account_id, username, username_normalized, created_at)
                VALUES ($1, $2, $3, $4)
                ON CONFLICT (username_normalized) DO UPDATE SET username = EXCLUDED.username
                RETURNING account_id;
                """;
            account.Parameters.AddWithValue(accountId);
            account.Parameters.AddWithValue(username);
            account.Parameters.AddWithValue(normalized);
            account.Parameters.AddWithValue(now);
            accountId = (Guid)(await account.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException("Account provisioning did not return an account id."));
        }

        await using (var device = connection.CreateCommand())
        {
            device.CommandText =
                """
                INSERT INTO jm.devices (
                    device_id, account_id, display_name, token_hash, created_at)
                VALUES ($1, $2, $3, $4, $5);
                """;
            device.Parameters.AddWithValue(deviceId);
            device.Parameters.AddWithValue(accountId);
            device.Parameters.AddWithValue(displayName);
            device.Parameters.AddWithValue(tokenHash);
            device.Parameters.AddWithValue(now);
            await device.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var vault = connection.CreateCommand())
        {
            vault.CommandText =
                """
                INSERT INTO jm.account_vaults (account_id, created_at, updated_at)
                VALUES ($1, $2, $2)
                ON CONFLICT (account_id) DO NOTHING;
                """;
            vault.Parameters.AddWithValue(accountId);
            vault.Parameters.AddWithValue(now);
            await vault.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new ProvisionedDevice(accountId, deviceId, username, displayName, token);
    }

    public async Task<Guid> ImportCharacterAsync(
        string username,
        string characterName,
        string characterClass,
        ReadOnlyMemory<byte> saveData,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(characterName);
        ArgumentException.ThrowIfNullOrWhiteSpace(characterClass);
        if (saveData.IsEmpty)
        {
            throw new ArgumentException("Character save cannot be empty.", nameof(saveData));
        }

        if (saveData.Length > ProfileBundleCodec.MaxFileLength)
        {
            throw new ArgumentException(
                $"Character save cannot exceed {ProfileBundleCodec.MaxFileLength} bytes.",
                nameof(saveData));
        }

        var accountId = await FindAccountIdAsync(username, cancellationToken);
        var characterId = Guid.NewGuid();
        var hash = SHA256.HashData(saveData.Span);
        var now = DateTime.UtcNow;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                INSERT INTO jm.characters (
                    character_id, account_id, name, character_class, save_data,
                    save_sha256, revision, created_at, updated_at)
                VALUES ($1, $2, $3, $4, $5, $6, 0, $7, $7);
                """;
            command.Parameters.AddWithValue(characterId);
            command.Parameters.AddWithValue(accountId);
            command.Parameters.AddWithValue(characterName.Trim());
            command.Parameters.AddWithValue(characterClass.Trim());
            command.Parameters.AddWithValue(saveData.ToArray());
            command.Parameters.AddWithValue(hash);
            command.Parameters.AddWithValue(now);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var vault = connection.CreateCommand())
        {
            vault.CommandText =
                """
                INSERT INTO jm.account_vaults (account_id, created_at, updated_at)
                VALUES ($1, $2, $2)
                ON CONFLICT (account_id) DO NOTHING;
                """;
            vault.Parameters.AddWithValue(accountId);
            vault.Parameters.AddWithValue(now);
            await vault.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var vaultFile = connection.CreateCommand())
        {
            vaultFile.CommandText =
                """
                INSERT INTO jm.account_vault_files (
                    account_id, relative_path, file_data, file_sha256, updated_at)
                VALUES ($1, $2, $3, $4, $5)
                ON CONFLICT (account_id, relative_path) DO UPDATE
                SET file_data = EXCLUDED.file_data,
                    file_sha256 = EXCLUDED.file_sha256,
                    updated_at = EXCLUDED.updated_at;
                """;
            vaultFile.Parameters.AddWithValue(accountId);
            vaultFile.Parameters.AddWithValue(characterName.Trim() + ".d2s");
            vaultFile.Parameters.AddWithValue(saveData.ToArray());
            vaultFile.Parameters.AddWithValue(hash);
            vaultFile.Parameters.AddWithValue(now);
            await vaultFile.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return characterId;
    }

    public async Task<StoredSharedStash?> ReadSoftcoreSharedStashAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        var accountId = await FindAccountIdAsync(username, cancellationToken);
        await using var command = dataSource.CreateCommand(
            """
            SELECT relative_path, file_data
            FROM jm.account_vault_files
            WHERE account_id = $1
              AND lower(relative_path) IN (
                  'modernsharedstashsoftcorev2.d2i',
                  'sharedstashsoftcorev2.d2i')
            ORDER BY CASE
                WHEN lower(relative_path) = 'modernsharedstashsoftcorev2.d2i' THEN 0
                ELSE 1
            END
            LIMIT 1;
            """);
        command.Parameters.AddWithValue(accountId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new StoredSharedStash(reader.GetString(0), reader.GetFieldValue<byte[]>(1))
            : null;
    }

    public async Task<IReadOnlyList<StoredVaultFile>> ReadVaultFilesAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        var accountId = await FindAccountIdAsync(username, cancellationToken);
        await using var command = dataSource.CreateCommand(
            """
            SELECT relative_path, file_data
            FROM jm.account_vault_files
            WHERE account_id = $1
            ORDER BY lower(relative_path);
            """);
        command.Parameters.AddWithValue(accountId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var files = new List<StoredVaultFile>();
        while (await reader.ReadAsync(cancellationToken))
        {
            files.Add(new StoredVaultFile(
                reader.GetString(0),
                reader.GetFieldValue<byte[]>(1)));
        }

        return files;
    }

    public async Task ImportSharedStashAsync(
        string username,
        string fileName,
        ReadOnlyMemory<byte> stashData,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        if (!ProfileSavePolicy.IsSharedStash(fileName))
        {
            throw new ArgumentException("The file is not a supported D2R shared stash.", nameof(fileName));
        }

        if (stashData.IsEmpty || stashData.Length > ProfileBundleCodec.MaxFileLength)
        {
            throw new ArgumentException("Shared stash size is invalid.", nameof(stashData));
        }

        var accountId = await FindAccountIdAsync(username, cancellationToken);
        var hash = SHA256.HashData(stashData.Span);
        var now = DateTime.UtcNow;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        long revision;
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
                throw new InvalidOperationException($"Account '{username}' has no profile vault.");
            }

            revision = reader.GetInt64(0);
            var hasActiveLease = !reader.IsDBNull(1) &&
                                 !reader.IsDBNull(2) &&
                                 reader.GetFieldValue<DateTime>(2) > now;
            if (hasActiveLease)
            {
                throw new InvalidOperationException(
                    $"Account '{username}' is currently checked out. Check it in before changing the shared stash.");
            }
        }

        var previousFiles = new List<ProfileFile>();
        await using (var files = connection.CreateCommand())
        {
            files.CommandText =
                """
                SELECT relative_path, file_data
                FROM jm.account_vault_files
                WHERE account_id = $1
                ORDER BY lower(relative_path);
                """;
            files.Parameters.AddWithValue(accountId);
            await using var reader = await files.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                previousFiles.Add(new ProfileFile(
                    reader.GetString(0),
                    reader.GetFieldValue<byte[]>(1)));
            }
        }

        var previousBundle = ProfileBundleCodec.Encode(previousFiles);
        await using (var version = connection.CreateCommand())
        {
            version.CommandText =
                """
                INSERT INTO jm.account_vault_versions (
                    vault_version_id, account_id, revision, bundle_data,
                    bundle_sha256, created_by_device_id, created_at)
                VALUES ($1, $2, $3, $4, $5, NULL, $6)
                ON CONFLICT (account_id, revision) DO NOTHING;
                """;
            version.Parameters.AddWithValue(Guid.NewGuid());
            version.Parameters.AddWithValue(accountId);
            version.Parameters.AddWithValue(revision);
            version.Parameters.AddWithValue(previousBundle);
            version.Parameters.AddWithValue(SHA256.HashData(previousBundle));
            version.Parameters.AddWithValue(now);
            await version.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var removeAlternateStash = connection.CreateCommand())
        {
            removeAlternateStash.CommandText =
                """
                DELETE FROM jm.account_vault_files
                WHERE account_id = $1
                  AND lower(relative_path) <> lower($2)
                  AND (
                      ($3 AND lower(relative_path) IN (
                          'modernsharedstashsoftcorev2.d2i',
                          'sharedstashsoftcorev2.d2i'))
                      OR
                      (NOT $3 AND lower(relative_path) IN (
                          'modernsharedstashhardcorev2.d2i',
                          'sharedstashhardcorev2.d2i'))
                  );
                """;
            removeAlternateStash.Parameters.AddWithValue(accountId);
            removeAlternateStash.Parameters.AddWithValue(fileName);
            removeAlternateStash.Parameters.AddWithValue(
                ProfileSavePolicy.IsSoftcoreSharedStash(fileName));
            await removeAlternateStash.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                INSERT INTO jm.account_vault_files (
                    account_id, relative_path, file_data, file_sha256, updated_at)
                VALUES ($1, $2, $3, $4, $5)
                ON CONFLICT (account_id, relative_path) DO UPDATE
                SET file_data = EXCLUDED.file_data,
                    file_sha256 = EXCLUDED.file_sha256,
                    updated_at = EXCLUDED.updated_at;
                """;
            command.Parameters.AddWithValue(accountId);
            command.Parameters.AddWithValue(fileName);
            command.Parameters.AddWithValue(stashData.ToArray());
            command.Parameters.AddWithValue(hash);
            command.Parameters.AddWithValue(now);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

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
            update.Parameters.AddWithValue(checked(revision + 1));
            update.Parameters.AddWithValue(now);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<UpdatedCharacterSave> ReplaceActiveCharacterSaveAsync(
        string username,
        string characterName,
        ReadOnlyMemory<byte> saveData,
        string auditEventType,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(characterName);
        ArgumentException.ThrowIfNullOrWhiteSpace(auditEventType);
        if (saveData.IsEmpty || saveData.Length > ProfileBundleCodec.MaxFileLength)
        {
            throw new ArgumentException("Character save size is invalid.", nameof(saveData));
        }

        var accountId = await FindAccountIdAsync(username, cancellationToken);
        var replacement = saveData.ToArray();
        var replacementHash = SHA256.HashData(replacement);
        var now = DateTime.UtcNow;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        long vaultRevision;
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
                throw new InvalidOperationException($"Account '{username}' has no profile vault.");
            }

            vaultRevision = reader.GetInt64(0);
            var hasActiveLease = !reader.IsDBNull(1) &&
                                 !reader.IsDBNull(2) &&
                                 reader.GetFieldValue<DateTime>(2) > now;
            if (hasActiveLease)
            {
                throw new InvalidOperationException(
                    $"Account '{username}' is currently checked out. Check it in before changing a character save.");
            }
        }

        Guid characterId;
        long characterRevision;
        byte[] previousSave;
        byte[] previousHash;
        string storedName;
        await using (var character = connection.CreateCommand())
        {
            character.CommandText =
                """
                SELECT character_id, name, revision, save_data, save_sha256
                FROM jm.characters
                WHERE account_id = $1
                  AND upper(name) = upper($2)
                  AND deleted_at IS NULL
                FOR UPDATE;
                """;
            character.Parameters.AddWithValue(accountId);
            character.Parameters.AddWithValue(characterName.Trim());
            await using var reader = await character.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException(
                    $"Active character '{characterName}' does not exist for '{username}'.");
            }

            characterId = reader.GetGuid(0);
            storedName = reader.GetString(1);
            characterRevision = reader.GetInt64(2);
            previousSave = reader.GetFieldValue<byte[]>(3);
            previousHash = reader.GetFieldValue<byte[]>(4);
        }

        var previousFiles = new List<ProfileFile>();
        await using (var files = connection.CreateCommand())
        {
            files.CommandText =
                """
                SELECT relative_path, file_data
                FROM jm.account_vault_files
                WHERE account_id = $1
                ORDER BY lower(relative_path);
                """;
            files.Parameters.AddWithValue(accountId);
            await using var reader = await files.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                previousFiles.Add(new ProfileFile(
                    reader.GetString(0),
                    reader.GetFieldValue<byte[]>(1)));
            }
        }

        var previousBundle = ProfileBundleCodec.Encode(previousFiles);
        await using (var preserveVault = connection.CreateCommand())
        {
            preserveVault.CommandText =
                """
                INSERT INTO jm.account_vault_versions (
                    vault_version_id, account_id, revision, bundle_data,
                    bundle_sha256, created_by_device_id, created_at)
                VALUES ($1, $2, $3, $4, $5, NULL, $6)
                ON CONFLICT (account_id, revision) DO NOTHING;
                """;
            preserveVault.Parameters.AddWithValue(Guid.NewGuid());
            preserveVault.Parameters.AddWithValue(accountId);
            preserveVault.Parameters.AddWithValue(vaultRevision);
            preserveVault.Parameters.AddWithValue(previousBundle);
            preserveVault.Parameters.AddWithValue(SHA256.HashData(previousBundle));
            preserveVault.Parameters.AddWithValue(now);
            await preserveVault.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var preserveCharacter = connection.CreateCommand())
        {
            preserveCharacter.CommandText =
                """
                INSERT INTO jm.character_versions (
                    character_version_id, character_id, revision, save_data,
                    save_sha256, created_by_device_id, created_at)
                VALUES ($1, $2, $3, $4, $5, NULL, $6)
                ON CONFLICT (character_id, revision) DO NOTHING;
                """;
            preserveCharacter.Parameters.AddWithValue(Guid.NewGuid());
            preserveCharacter.Parameters.AddWithValue(characterId);
            preserveCharacter.Parameters.AddWithValue(characterRevision);
            preserveCharacter.Parameters.AddWithValue(previousSave);
            preserveCharacter.Parameters.AddWithValue(previousHash);
            preserveCharacter.Parameters.AddWithValue(now);
            await preserveCharacter.ExecuteNonQueryAsync(cancellationToken);
        }

        var newCharacterRevision = checked(characterRevision + 1);
        await using (var updateCharacter = connection.CreateCommand())
        {
            updateCharacter.CommandText =
                """
                UPDATE jm.characters
                SET save_data = $2,
                    save_sha256 = $3,
                    revision = $4,
                    updated_at = $5
                WHERE character_id = $1;
                """;
            updateCharacter.Parameters.AddWithValue(characterId);
            updateCharacter.Parameters.AddWithValue(replacement);
            updateCharacter.Parameters.AddWithValue(replacementHash);
            updateCharacter.Parameters.AddWithValue(newCharacterRevision);
            updateCharacter.Parameters.AddWithValue(now);
            await updateCharacter.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var updateFile = connection.CreateCommand())
        {
            updateFile.CommandText =
                """
                UPDATE jm.account_vault_files
                SET file_data = $3,
                    file_sha256 = $4,
                    updated_at = $5
                WHERE account_id = $1
                  AND lower(relative_path) = lower($2);
                """;
            updateFile.Parameters.AddWithValue(accountId);
            updateFile.Parameters.AddWithValue(storedName + ".d2s");
            updateFile.Parameters.AddWithValue(replacement);
            updateFile.Parameters.AddWithValue(replacementHash);
            updateFile.Parameters.AddWithValue(now);
            if (await updateFile.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException(
                    $"Vault file '{storedName}.d2s' was not found exactly once.");
            }
        }

        var newVaultRevision = checked(vaultRevision + 1);
        await using (var updateVault = connection.CreateCommand())
        {
            updateVault.CommandText =
                """
                UPDATE jm.account_vaults
                SET revision = $2,
                    lease_id = NULL,
                    leased_device_id = NULL,
                    lease_expires_at = NULL,
                    updated_at = $3
                WHERE account_id = $1;
                """;
            updateVault.Parameters.AddWithValue(accountId);
            updateVault.Parameters.AddWithValue(newVaultRevision);
            updateVault.Parameters.AddWithValue(now);
            await updateVault.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var audit = connection.CreateCommand())
        {
            audit.CommandText =
                """
                INSERT INTO jm.audit_events (
                    account_id, device_id, character_id, event_type, details, created_at)
                VALUES ($1, NULL, $2, $3, $4, $5);
                """;
            audit.Parameters.AddWithValue(accountId);
            audit.Parameters.AddWithValue(characterId);
            audit.Parameters.AddWithValue(auditEventType.Trim());
            audit.Parameters.Add(new NpgsqlParameter
            {
                NpgsqlDbType = NpgsqlDbType.Jsonb,
                Value = JsonSerializer.Serialize(new
                {
                    name = storedName,
                    previousSha256 = Convert.ToHexString(previousHash),
                    sha256 = Convert.ToHexString(replacementHash)
                })
            });
            audit.Parameters.AddWithValue(now);
            await audit.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new UpdatedCharacterSave(
            characterId,
            storedName,
            newCharacterRevision,
            newVaultRevision);
    }

    public async Task<RotatedDeviceToken> RotateDeviceTokenAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        if (deviceId == Guid.Empty)
        {
            throw new ArgumentException("Device id is required.", nameof(deviceId));
        }

        var token = TokenHasher.GenerateToken();
        var tokenHash = TokenHasher.Hash(token);
        await using var command = dataSource.CreateCommand(
            """
            UPDATE jm.devices d
            SET token_hash = $2, revoked_at = NULL
            FROM jm.accounts a
            WHERE d.device_id = $1 AND a.account_id = d.account_id
            RETURNING a.username, d.display_name;
            """);
        command.Parameters.AddWithValue(deviceId);
        command.Parameters.AddWithValue(tokenHash);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException($"Device '{deviceId}' does not exist.");
        }

        return new RotatedDeviceToken(deviceId, reader.GetString(0), reader.GetString(1), token);
    }

    private async Task<Guid> FindAccountIdAsync(string username, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "SELECT account_id FROM jm.accounts WHERE username_normalized = $1;");
        command.Parameters.AddWithValue(username.Trim().ToUpperInvariant());
        return (Guid)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException($"Account '{username}' does not exist."));
    }
}

public sealed record ProvisionedDevice(
    Guid AccountId,
    Guid DeviceId,
    string Username,
    string DisplayName,
    string Token);

public sealed record RotatedDeviceToken(
    Guid DeviceId,
    string Username,
    string DisplayName,
    string Token);

public sealed record StoredSharedStash(string FileName, byte[] Data);

public sealed record StoredVaultFile(string FileName, byte[] Data);

public sealed record UpdatedCharacterSave(
    Guid CharacterId,
    string Name,
    long CharacterRevision,
    long VaultRevision);
