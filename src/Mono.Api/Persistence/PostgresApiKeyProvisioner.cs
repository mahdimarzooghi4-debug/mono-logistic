using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

public sealed class PostgresApiKeyProvisioner(NpgsqlDataSource dataSource)
{
    public async Task CreateAsync(ApiKeyCredential credential, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO customer_api_keys (key_id, customer_id, secret_hash, scopes, expires_at)
            SELECT @key_id, @customer_id, @secret_hash, @scopes, @expires_at
            FROM customers
            WHERE customer_id = @customer_id AND status = 'ACTIVE';
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("key_id", NpgsqlDbType.Text, credential.KeyId);
        command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, credential.CustomerId);
        command.Parameters.AddWithValue("secret_hash", NpgsqlDbType.Text, credential.SecretHash);
        command.Parameters.AddWithValue("scopes", NpgsqlDbType.Array | NpgsqlDbType.Text, credential.Scopes.ToArray());
        command.Parameters.AddWithValue("expires_at", NpgsqlDbType.TimestampTz, credential.ExpiresAt);

        var inserted = await command.ExecuteNonQueryAsync(cancellationToken);
        if (inserted != 1)
            throw new InvalidOperationException("The customer does not exist or is not active; no API key was stored.");
    }
}
