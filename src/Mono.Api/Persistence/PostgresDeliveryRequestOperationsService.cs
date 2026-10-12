using Mono.Api.Domain;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

/// <summary>Privacy-limited cross-customer Delivery Request inspection for Mono operations.</summary>
public sealed class PostgresDeliveryRequestOperationsService(NpgsqlDataSource dataSource)
{
    public async Task<DeliveryRequestOperationsPage?> ListAsync(
        int pageSize,
        string? status,
        string? query,
        DateTimeOffset? createdAfter,
        DateTimeOffset? createdBefore,
        string? cursor,
        CancellationToken cancellationToken = default)
    {
        if (pageSize is < 1 or > 100 ||
            !DeliveryRequestOperationsInputValidator.IsValidStatus(status) ||
            !DeliveryRequestOperationsInputValidator.IsValidQuery(query) ||
            !DeliveryRequestOperationsInputValidator.IsValidDateRange(createdAfter, createdBefore) ||
            !DeliveryRequestOperationsInputValidator.IsValidCursor(cursor))
            throw new ArgumentException("Delivery Request operations filters are invalid.");

        var normalizedQuery = query?.ToLowerInvariant();
        DateTimeOffset? cursorCreatedAt = null;
        if (cursor is not null)
        {
            await using var cursorConnection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var cursorCommand = new NpgsqlCommand("""
                SELECT created_at, status, external_reference
                FROM delivery_requests
                WHERE delivery_request_id = @delivery_request_id;
                """, cursorConnection);
            cursorCommand.Parameters.AddWithValue("delivery_request_id", NpgsqlDbType.Text, cursor);
            await using var reader = await cursorCommand.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return null;

            cursorCreatedAt = reader.GetFieldValue<DateTimeOffset>(0);
            var cursorStatus = reader.GetString(1);
            var cursorExternalReference = reader.GetString(2);
            if ((status is not null && !string.Equals(cursorStatus, status, StringComparison.Ordinal)) ||
                (createdAfter is not null && cursorCreatedAt < createdAfter) ||
                (createdBefore is not null && cursorCreatedAt > createdBefore) ||
                (normalizedQuery is not null && !MatchesQuery(cursor, cursorExternalReference, normalizedQuery)))
                return null;
        }

        const string firstPageSql = """
            SELECT delivery_request_id, customer_id, branch_id, external_reference, status, created_at
            FROM delivery_requests
            WHERE (@status IS NULL OR status = @status)
              AND (@query_pattern IS NULL
                   OR lower(delivery_request_id) LIKE @query_pattern ESCAPE '!'
                   OR lower(external_reference) LIKE @query_pattern ESCAPE '!')
              AND (@created_after IS NULL OR created_at >= @created_after)
              AND (@created_before IS NULL OR created_at <= @created_before)
            ORDER BY created_at DESC, delivery_request_id DESC
            LIMIT @take;
            """;
        const string nextPageSql = """
            SELECT delivery_request_id, customer_id, branch_id, external_reference, status, created_at
            FROM delivery_requests
            WHERE (@status IS NULL OR status = @status)
              AND (@query_pattern IS NULL
                   OR lower(delivery_request_id) LIKE @query_pattern ESCAPE '!'
                   OR lower(external_reference) LIKE @query_pattern ESCAPE '!')
              AND (@created_after IS NULL OR created_at >= @created_after)
              AND (@created_before IS NULL OR created_at <= @created_before)
              AND (created_at < @cursor_created_at
                   OR (created_at = @cursor_created_at AND delivery_request_id < @cursor_id))
            ORDER BY created_at DESC, delivery_request_id DESC
            LIMIT @take;
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(cursor is null ? firstPageSql : nextPageSql, connection);
        command.Parameters.AddWithValue("status", NpgsqlDbType.Text, (object?)status ?? DBNull.Value);
        command.Parameters.AddWithValue("query_pattern", NpgsqlDbType.Text,
            normalizedQuery is null ? DBNull.Value : BuildPrefixPattern(normalizedQuery));
        command.Parameters.AddWithValue("created_after", NpgsqlDbType.TimestampTz,
            (object?)createdAfter ?? DBNull.Value);
        command.Parameters.AddWithValue("created_before", NpgsqlDbType.TimestampTz,
            (object?)createdBefore ?? DBNull.Value);
        command.Parameters.AddWithValue("take", NpgsqlDbType.Integer, pageSize + 1);
        if (cursor is not null)
        {
            command.Parameters.AddWithValue("cursor_created_at", NpgsqlDbType.TimestampTz, cursorCreatedAt!.Value);
            command.Parameters.AddWithValue("cursor_id", NpgsqlDbType.Text, cursor);
        }

        var items = new List<DeliveryRequestOperationsItem>(pageSize + 1);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                items.Add(new DeliveryRequestOperationsItem(
                    reader.GetString(0), reader.GetString(1), reader.GetString(2),
                    reader.GetString(3), reader.GetString(4), reader.GetFieldValue<DateTimeOffset>(5)));
        }

        string? nextCursor = null;
        if (items.Count > pageSize)
        {
            items.RemoveAt(items.Count - 1);
            nextCursor = items[^1].DeliveryRequestId;
        }
        return new DeliveryRequestOperationsPage(items, nextCursor);
    }

    public async Task<DeliveryRequestOperationsItem?> FindByIdAsync(
        string deliveryRequestId,
        CancellationToken cancellationToken = default)
    {
        if (!DeliveryRequestOperationsInputValidator.IsValidCursor(deliveryRequestId))
            throw new ArgumentException("Delivery Request ID is invalid.", nameof(deliveryRequestId));

        const string sql = """
            SELECT delivery_request_id, customer_id, branch_id, external_reference, status, created_at
            FROM delivery_requests WHERE delivery_request_id = @delivery_request_id;
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("delivery_request_id", NpgsqlDbType.Text, deliveryRequestId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new DeliveryRequestOperationsItem(
                reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), reader.GetFieldValue<DateTimeOffset>(5))
            : null;
    }

    private static string BuildPrefixPattern(string query) =>
        query.Replace("!", "!!", StringComparison.Ordinal)
            .Replace("%", "!%", StringComparison.Ordinal)
            .Replace("_", "!_", StringComparison.Ordinal) + "%";

    private static bool MatchesQuery(string requestId, string externalReference, string normalizedQuery) =>
        requestId.StartsWith(normalizedQuery, StringComparison.OrdinalIgnoreCase) ||
        externalReference.StartsWith(normalizedQuery, StringComparison.OrdinalIgnoreCase);
}
