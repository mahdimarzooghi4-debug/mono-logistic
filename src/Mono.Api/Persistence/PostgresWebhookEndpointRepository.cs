using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

public sealed record StoredWebhookEndpoint(
    string EndpointId,
    string CustomerId,
    string Url,
    IReadOnlyList<string> Events,
    string Status,
    DateTimeOffset CreatedAt);

public sealed class PostgresWebhookEndpointRepository(NpgsqlDataSource dataSource)
{
    public async Task<StoredWebhookEndpoint> CreateAsync(
        string customerId,
        string url,
        IReadOnlyCollection<string> events,
        string protectedSecret,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        var endpointId = "whk_" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        const string sql = """
            INSERT INTO webhook_endpoints (
                endpoint_id, customer_id, url, events, secret_ciphertext, status, created_at)
            VALUES (@endpoint_id, @customer_id, @url, @events, @secret_ciphertext, 'ACTIVE', @created_at);
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("endpoint_id", NpgsqlDbType.Text, endpointId);
        command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
        command.Parameters.AddWithValue("url", NpgsqlDbType.Text, url);
        command.Parameters.AddWithValue("events", NpgsqlDbType.Array | NpgsqlDbType.Text, events.ToArray());
        command.Parameters.AddWithValue("secret_ciphertext", NpgsqlDbType.Text, protectedSecret);
        command.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, createdAt);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return new StoredWebhookEndpoint(endpointId, customerId, url,
            events.Order(StringComparer.Ordinal).ToArray(), "ACTIVE", createdAt);
    }
}
