using Npgsql;
using NpgsqlTypes;
using System.Data;

namespace Mono.Api.Persistence;

public sealed record StoredWebhookEndpoint(
    string EndpointId,
    string CustomerId,
    string Url,
    IReadOnlyList<string> Events,
    string Status,
    DateTimeOffset CreatedAt);

public sealed record WebhookEndpointPage(
    IReadOnlyList<StoredWebhookEndpoint> Items,
    string? NextCursor);

public sealed record WebhookEndpointLifecycleEvent(
    long EventId,
    string Action,
    DateTimeOffset OccurredAt);

public sealed record WebhookEndpointLifecycleEventPage(
    IReadOnlyList<WebhookEndpointLifecycleEvent> Items,
    string? NextCursor);

public sealed record WebhookEndpointMutationResult(
    StoredWebhookEndpoint? Endpoint,
    bool Changed);

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
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Transaction = transaction;
        command.Parameters.AddWithValue("endpoint_id", NpgsqlDbType.Text, endpointId);
        command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
        command.Parameters.AddWithValue("url", NpgsqlDbType.Text, url);
        command.Parameters.AddWithValue("events", NpgsqlDbType.Array | NpgsqlDbType.Text, events.ToArray());
        command.Parameters.AddWithValue("secret_ciphertext", NpgsqlDbType.Text, protectedSecret);
        command.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, createdAt);
        await command.ExecuteNonQueryAsync(cancellationToken);

        await AppendEventAsync(connection, transaction, endpointId, customerId, "CREATED", createdAt, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new StoredWebhookEndpoint(endpointId, customerId, url,
            events.Order(StringComparer.Ordinal).ToArray(), "ACTIVE", createdAt);
    }

    public async Task<WebhookEndpointPage> ListAsync(
        string customerId,
        int limit,
        DateTimeOffset? beforeCreatedAt,
        string? beforeEndpointId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT endpoint_id, customer_id, url, events, status, created_at
            FROM webhook_endpoints
            WHERE customer_id = @customer_id
              AND (@before_created_at IS NULL OR (created_at, endpoint_id) < (@before_created_at, @before_endpoint_id))
            ORDER BY created_at DESC, endpoint_id DESC
            LIMIT @limit;
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
        command.Parameters.AddWithValue("before_created_at", NpgsqlDbType.TimestampTz, (object?)beforeCreatedAt ?? DBNull.Value);
        command.Parameters.AddWithValue("before_endpoint_id", NpgsqlDbType.Text, (object?)beforeEndpointId ?? DBNull.Value);
        command.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit + 1);

        var items = new List<StoredWebhookEndpoint>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            items.Add(ReadEndpoint(reader));

        string? nextCursor = null;
        if (items.Count > limit)
        {
            items.RemoveAt(items.Count - 1);
            var last = items[^1];
            nextCursor = WebhookEndpointCursor.Encode(last.CreatedAt, last.EndpointId);
        }
        return new WebhookEndpointPage(items, nextCursor);
    }

    public async Task<StoredWebhookEndpoint?> FindAsync(
        string customerId,
        string endpointId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT endpoint_id, customer_id, url, events, status, created_at
            FROM webhook_endpoints
            WHERE customer_id = @customer_id AND endpoint_id = @endpoint_id;
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
        command.Parameters.AddWithValue("endpoint_id", NpgsqlDbType.Text, endpointId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadEndpoint(reader) : null;
    }

    public async Task<WebhookEndpointLifecycleEventPage> ListLifecycleEventsAsync(
        string customerId,
        string endpointId,
        int limit,
        long? beforeEventId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT event_id, action, occurred_at
            FROM webhook_endpoint_events
            WHERE customer_id = @customer_id AND endpoint_id = @endpoint_id
              AND (@before_event_id IS NULL OR event_id < @before_event_id)
            ORDER BY event_id DESC
            LIMIT @limit;
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
        command.Parameters.AddWithValue("endpoint_id", NpgsqlDbType.Text, endpointId);
        command.Parameters.AddWithValue("before_event_id", NpgsqlDbType.Bigint, (object?)beforeEventId ?? DBNull.Value);
        command.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit + 1);

        var items = new List<WebhookEndpointLifecycleEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            items.Add(new WebhookEndpointLifecycleEvent(
                reader.GetInt64(0), reader.GetString(1), reader.GetFieldValue<DateTimeOffset>(2)));

        string? nextCursor = null;
        if (items.Count > limit)
        {
            items.RemoveAt(items.Count - 1);
            nextCursor = WebhookEndpointLifecycleCursor.Encode(items[^1].EventId);
        }
        return new WebhookEndpointLifecycleEventPage(items, nextCursor);
    }

    public async Task<WebhookEndpointMutationResult> DisableAsync(
        string customerId,
        string endpointId,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var current = await FindForUpdateAsync(connection, transaction, customerId, endpointId, cancellationToken);
        if (current is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new WebhookEndpointMutationResult(null, false);
        }
        if (current.Status == "DISABLED")
        {
            await transaction.CommitAsync(cancellationToken);
            return new WebhookEndpointMutationResult(current, false);
        }

        const string sql = """
            UPDATE webhook_endpoints SET status = 'DISABLED', disabled_at = @disabled_at
            WHERE customer_id = @customer_id AND endpoint_id = @endpoint_id;
            """;
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue("disabled_at", NpgsqlDbType.TimestampTz, occurredAt);
            command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            command.Parameters.AddWithValue("endpoint_id", NpgsqlDbType.Text, endpointId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await CancelQueuedDeliveriesAsync(connection, transaction, endpointId, occurredAt, cancellationToken);
        await AppendEventAsync(connection, transaction, endpointId, customerId, "DISABLED", occurredAt, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new WebhookEndpointMutationResult(current with { Status = "DISABLED" }, true);
    }

    public async Task<WebhookEndpointMutationResult> RotateSecretAsync(
        string customerId,
        string endpointId,
        string protectedSecret,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var current = await FindForUpdateAsync(connection, transaction, customerId, endpointId, cancellationToken);
        if (current is null || current.Status != "ACTIVE")
        {
            await transaction.RollbackAsync(cancellationToken);
            return new WebhookEndpointMutationResult(current, false);
        }

        const string sql = """
            UPDATE webhook_endpoints SET secret_ciphertext = @secret_ciphertext
            WHERE customer_id = @customer_id AND endpoint_id = @endpoint_id AND status = 'ACTIVE';
            """;
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue("secret_ciphertext", NpgsqlDbType.Text, protectedSecret);
            command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            command.Parameters.AddWithValue("endpoint_id", NpgsqlDbType.Text, endpointId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await AppendEventAsync(connection, transaction, endpointId, customerId, "SECRET_ROTATED", occurredAt, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new WebhookEndpointMutationResult(current, true);
    }

    private static async Task<StoredWebhookEndpoint?> FindForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string customerId,
        string endpointId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT endpoint_id, customer_id, url, events, status, created_at
            FROM webhook_endpoints
            WHERE customer_id = @customer_id AND endpoint_id = @endpoint_id
            FOR UPDATE;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
        command.Parameters.AddWithValue("endpoint_id", NpgsqlDbType.Text, endpointId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadEndpoint(reader) : null;
    }

    private static StoredWebhookEndpoint ReadEndpoint(NpgsqlDataReader reader) => new(
        reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetFieldValue<string[]>(3),
        reader.GetString(4), reader.GetFieldValue<DateTimeOffset>(5));

    private static async Task CancelQueuedDeliveriesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string endpointId,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE webhook_deliveries
            SET status = 'CANCELLED', completed_at = @occurred_at, lease_expires_at = NULL
            WHERE endpoint_id = @endpoint_id AND status IN ('PENDING', 'RETRYING');
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, occurredAt);
        command.Parameters.AddWithValue("endpoint_id", NpgsqlDbType.Text, endpointId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task AppendEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string endpointId,
        string customerId,
        string action,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO webhook_endpoint_events (endpoint_id, customer_id, action, occurred_at)
            VALUES (@endpoint_id, @customer_id, @action, @occurred_at);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("endpoint_id", NpgsqlDbType.Text, endpointId);
        command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
        command.Parameters.AddWithValue("action", NpgsqlDbType.Text, action);
        command.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, occurredAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

public static class WebhookEndpointCursor
{
    public static string Encode(DateTimeOffset createdAt, string endpointId)
    {
        var value = $"{createdAt:O}|{endpointId}";
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static bool TryDecode(string? cursor, out DateTimeOffset createdAt, out string endpointId)
    {
        createdAt = default;
        endpointId = string.Empty;
        if (string.IsNullOrWhiteSpace(cursor) || cursor.Length > 256)
            return false;
        try
        {
            var value = cursor.Replace('-', '+').Replace('_', '/');
            value += new string('=', (4 - value.Length % 4) % 4);
            var parts = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(value)).Split('|');
            if (parts.Length != 2 || !DateTimeOffset.TryParseExact(parts[0], "O",
                    System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsed) ||
                !System.Text.RegularExpressions.Regex.IsMatch(parts[1], @"\Awhk_[0-9a-f]{32}\z"))
                return false;
            createdAt = parsed;
            endpointId = parts[1];
            return true;
        }
        catch (Exception exception) when (exception is FormatException or ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}

public static class WebhookEndpointLifecycleCursor
{
    public static string Encode(long eventId) => Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes(eventId.ToString(System.Globalization.CultureInfo.InvariantCulture)))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static bool TryDecode(string? cursor, out long eventId)
    {
        eventId = 0;
        if (string.IsNullOrWhiteSpace(cursor) || cursor.Length > 64)
            return false;
        try
        {
            var value = cursor.Replace('-', '+').Replace('_', '/');
            value += new string('=', (4 - value.Length % 4) % 4);
            return long.TryParse(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(value)),
                System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out eventId)
                && eventId > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
