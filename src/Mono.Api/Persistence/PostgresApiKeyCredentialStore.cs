using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

public sealed class PostgresApiKeyCredentialStore(NpgsqlDataSource dataSource) : IApiKeyCredentialStore
{
    public async Task<ApiKeyCredential?> FindAsync(string keyId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT k.key_id, k.customer_id, k.secret_hash, k.expires_at, k.revoked_at,
                   c.status = 'ACTIVE', k.scopes
            FROM customer_api_keys AS k
            INNER JOIN customers AS c ON c.customer_id = k.customer_id
            WHERE k.key_id = @key_id;
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("key_id", NpgsqlDbType.Text, keyId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new ApiKeyCredential(reader.GetString(0), reader.GetString(1), reader.GetString(2),
            reader.GetFieldValue<DateTimeOffset>(3),
            reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4),
            reader.GetBoolean(5), reader.GetFieldValue<string[]>(6));
    }
}
