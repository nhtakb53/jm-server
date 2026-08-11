using JmServer.Domain;
using Npgsql;

namespace JmServer.Persistence;

public sealed class DatabaseProvisioner(NpgsqlDataSource administrationDataSource)
{
    public async Task<ProvisionedDatabase> ProvisionAsync(
        string databaseName,
        string roleName,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(databaseName, nameof(databaseName));
        ValidateIdentifier(roleName, nameof(roleName));

        await using var connection = await administrationDataSource.OpenConnectionAsync(cancellationToken);
        if (await ExistsAsync(
                connection,
                "SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = $1);",
                databaseName,
                cancellationToken))
        {
            throw new InvalidOperationException($"Database '{databaseName}' already exists.");
        }

        if (await ExistsAsync(
                connection,
                "SELECT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = $1);",
                roleName,
                cancellationToken))
        {
            throw new InvalidOperationException($"PostgreSQL role '{roleName}' already exists.");
        }

        var password = TokenHasher.GenerateToken();
        await ExecuteAsync(
            connection,
            $"CREATE ROLE {roleName} WITH LOGIN PASSWORD '{password}' " +
            "NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;",
            cancellationToken);

        try
        {
            await ExecuteAsync(
                connection,
                $"CREATE DATABASE {databaseName} WITH OWNER = {roleName} " +
                "ENCODING = 'UTF8' TEMPLATE = template0;",
                cancellationToken);
        }
        catch
        {
            await ExecuteAsync(connection, $"DROP ROLE {roleName};", CancellationToken.None);
            throw;
        }

        var targetConnectionString = new NpgsqlConnectionStringBuilder(connection.ConnectionString)
        {
            Database = databaseName,
            Username = roleName,
            Password = password,
            ApplicationName = "JM Server",
            Pooling = true
        }.ConnectionString;

        return new ProvisionedDatabase(databaseName, roleName, password, targetConnectionString);
    }

    private static async Task<bool> ExistsAsync(
        NpgsqlConnection connection,
        string sql,
        string value,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue(value);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken)
                      ?? throw new InvalidOperationException("PostgreSQL did not return an existence result."));
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

    private static void ValidateIdentifier(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length is < 3 or > 63 ||
            value[0] is < 'a' or > 'z' ||
            value.Any(character =>
                character is not (>= 'a' and <= 'z') and
                not (>= '0' and <= '9') and
                not '_'))
        {
            throw new ArgumentException(
                "PostgreSQL names must be 3-63 lowercase ASCII letters, digits, or underscores, " +
                "and start with a letter.",
                parameterName);
        }
    }
}

public sealed record ProvisionedDatabase(
    string DatabaseName,
    string RoleName,
    string Password,
    string ConnectionString);
