using JmServer.Domain;
using Npgsql;

namespace JmServer.Persistence;

public sealed class NpgsqlDeviceSettingsStore(NpgsqlDataSource dataSource)
    : IDeviceSettingsStore
{
    public async Task<DeviceSettingsSnapshot?> GetAsync(
        Guid accountId,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT revision, settings_data, settings_sha256
            FROM jm.device_settings
            WHERE account_id = $1 AND device_id = $2;
            """);
        command.Parameters.AddWithValue(accountId);
        command.Parameters.AddWithValue(deviceId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new DeviceSettingsSnapshot(
            reader.GetInt64(0),
            reader.GetFieldValue<byte[]>(1),
            reader.GetFieldValue<byte[]>(2));
    }

    public async Task<DeviceSettingsSnapshot> PutAsync(
        Guid accountId,
        Guid deviceId,
        ReadOnlyMemory<byte> settingsData,
        ReadOnlyMemory<byte> sha256,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        long revision;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                INSERT INTO jm.device_settings (
                    device_id, account_id, revision, settings_data, settings_sha256, updated_at)
                VALUES ($1, $2, 1, $3, $4, $5)
                ON CONFLICT (device_id) DO UPDATE
                SET revision = CASE
                        WHEN jm.device_settings.settings_sha256 = EXCLUDED.settings_sha256
                            THEN jm.device_settings.revision
                        ELSE jm.device_settings.revision + 1
                    END,
                    settings_data = EXCLUDED.settings_data,
                    settings_sha256 = EXCLUDED.settings_sha256,
                    updated_at = CASE
                        WHEN jm.device_settings.settings_sha256 = EXCLUDED.settings_sha256
                            THEN jm.device_settings.updated_at
                        ELSE EXCLUDED.updated_at
                    END
                WHERE jm.device_settings.account_id = EXCLUDED.account_id
                RETURNING revision;
                """;
            command.Parameters.AddWithValue(deviceId);
            command.Parameters.AddWithValue(accountId);
            command.Parameters.AddWithValue(settingsData.ToArray());
            command.Parameters.AddWithValue(sha256.ToArray());
            command.Parameters.AddWithValue(now.UtcDateTime);
            revision = (long)(await command.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException("The authenticated device does not belong to the account."));
        }

        await using (var audit = connection.CreateCommand())
        {
            audit.CommandText =
                """
                INSERT INTO jm.audit_events (
                    account_id, device_id, event_type, details, created_at)
                VALUES ($1, $2, 'device-settings.saved', jsonb_build_object('revision', $3), $4);
                """;
            audit.Parameters.AddWithValue(accountId);
            audit.Parameters.AddWithValue(deviceId);
            audit.Parameters.AddWithValue(revision);
            audit.Parameters.AddWithValue(now.UtcDateTime);
            await audit.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new DeviceSettingsSnapshot(
            revision,
            settingsData.ToArray(),
            sha256.ToArray());
    }
}
