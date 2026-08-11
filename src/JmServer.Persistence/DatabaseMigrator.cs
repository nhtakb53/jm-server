using Npgsql;

namespace JmServer.Persistence;

public sealed class DatabaseMigrator(NpgsqlDataSource dataSource)
{
    private const string ResourcePrefix = "JmServer.Persistence.Migrations.";

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await ExecuteAsync(
            connection,
            """
            CREATE SCHEMA IF NOT EXISTS jm;
            CREATE TABLE IF NOT EXISTS jm.schema_migrations (
                migration_id text PRIMARY KEY,
                applied_at timestamptz NOT NULL
            );
            SELECT pg_advisory_xact_lock(hashtext('jm-server-schema-migrations'));
            """,
            cancellationToken);

        var assembly = typeof(DatabaseMigrator).Assembly;
        var resources = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal) &&
                           name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToArray();

        foreach (var resourceName in resources)
        {
            var migrationId = resourceName[ResourcePrefix.Length..^4];
            if (await HasMigrationAsync(connection, migrationId, cancellationToken))
            {
                continue;
            }

            await using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Migration resource '{resourceName}' was not found.");
            using var reader = new StreamReader(stream);
            var sql = await reader.ReadToEndAsync(cancellationToken);

            await ExecuteAsync(connection, sql, cancellationToken);

            await using var insert = connection.CreateCommand();
            insert.CommandText =
                "INSERT INTO jm.schema_migrations (migration_id, applied_at) VALUES ($1, $2);";
            insert.Parameters.AddWithValue(migrationId);
            insert.Parameters.AddWithValue(DateTime.UtcNow);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<bool> HasMigrationAsync(
        NpgsqlConnection connection,
        string migrationId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS (SELECT 1 FROM jm.schema_migrations WHERE migration_id = $1);";
        command.Parameters.AddWithValue(migrationId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
