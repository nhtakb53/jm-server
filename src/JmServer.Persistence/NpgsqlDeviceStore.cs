using System.Security.Cryptography;
using JmServer.Domain;
using Npgsql;

namespace JmServer.Persistence;

public sealed class NpgsqlDeviceStore(NpgsqlDataSource dataSource) : IDeviceStore
{
    public async Task<DeviceIdentity?> AuthenticateAsync(
        Guid deviceId,
        ReadOnlyMemory<byte> tokenHash,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT d.token_hash, d.account_id, a.username
            FROM jm.devices d
            JOIN jm.accounts a ON a.account_id = d.account_id
            WHERE d.device_id = $1 AND d.revoked_at IS NULL;
            """;
        command.Parameters.AddWithValue(deviceId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var storedHash = reader.GetFieldValue<byte[]>(0);
        if (storedHash.Length != tokenHash.Length ||
            !CryptographicOperations.FixedTimeEquals(storedHash, tokenHash.Span))
        {
            return null;
        }

        var identity = new DeviceIdentity(
            reader.GetGuid(1),
            deviceId,
            reader.GetString(2));

        await reader.DisposeAsync();
        await using var update = connection.CreateCommand();
        update.CommandText = "UPDATE jm.devices SET last_authenticated_at = $2 WHERE device_id = $1;";
        update.Parameters.AddWithValue(deviceId);
        update.Parameters.AddWithValue(DateTime.UtcNow);
        await update.ExecuteNonQueryAsync(cancellationToken);
        return identity;
    }
}
