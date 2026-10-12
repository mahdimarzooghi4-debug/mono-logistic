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
        string? customerId,
        string? branchId,
        bool? hasMission,
        string? cursor,
        CancellationToken cancellationToken = default)
    {
        if (pageSize is < 1 or > 100 ||
            !DeliveryRequestOperationsInputValidator.IsValidStatus(status) ||
            !DeliveryRequestOperationsInputValidator.IsValidQuery(query) ||
            !DeliveryRequestOperationsInputValidator.IsValidDateRange(createdAfter, createdBefore) ||
            !DeliveryRequestOperationsInputValidator.IsValidCustomerId(customerId) ||
            !DeliveryRequestOperationsInputValidator.IsValidBranchId(branchId) ||
            !DeliveryRequestOperationsInputValidator.IsValidCursor(cursor))
            throw new ArgumentException("Delivery Request operations filters are invalid.");

        var normalizedQuery = query?.ToLowerInvariant();
        DateTimeOffset? cursorCreatedAt = null;
        if (cursor is not null)
        {
            await using var cursorConnection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var cursorCommand = new NpgsqlCommand("""
                SELECT d.created_at, d.status, d.external_reference, d.customer_id, d.branch_id,
                       (m.mission_id IS NOT NULL)
                FROM delivery_requests d
                LEFT JOIN missions m
                  ON m.customer_id = d.customer_id
                 AND m.delivery_request_id = d.delivery_request_id
                WHERE d.delivery_request_id = @delivery_request_id;
                """, cursorConnection);
            cursorCommand.Parameters.AddWithValue("delivery_request_id", NpgsqlDbType.Text, cursor);
            await using var reader = await cursorCommand.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return null;

            cursorCreatedAt = reader.GetFieldValue<DateTimeOffset>(0);
            var cursorStatus = reader.GetString(1);
            var cursorExternalReference = reader.GetString(2);
            var cursorCustomerId = reader.GetString(3);
            var cursorBranchId = reader.GetString(4);
            var cursorHasMission = reader.GetBoolean(5);
            if ((status is not null && !string.Equals(cursorStatus, status, StringComparison.Ordinal)) ||
                (createdAfter is not null && cursorCreatedAt < createdAfter) ||
                (createdBefore is not null && cursorCreatedAt > createdBefore) ||
                (customerId is not null && !string.Equals(cursorCustomerId, customerId, StringComparison.Ordinal)) ||
                (branchId is not null && !string.Equals(cursorBranchId, branchId, StringComparison.Ordinal)) ||
                (hasMission is not null && cursorHasMission != hasMission) ||
                (normalizedQuery is not null && !MatchesQuery(cursor, cursorExternalReference, normalizedQuery)))
                return null;
        }

        const string firstPageSql = """
            SELECT d.delivery_request_id, d.customer_id, d.branch_id, d.external_reference,
                   d.status, d.created_at, m.mission_id, m.status, m.committed_delivery_at
            FROM delivery_requests d
            LEFT JOIN missions m
              ON m.customer_id = d.customer_id
             AND m.delivery_request_id = d.delivery_request_id
            WHERE (@status IS NULL OR d.status = @status)
              AND (@query_pattern IS NULL
                   OR lower(d.delivery_request_id) LIKE @query_pattern ESCAPE '!'
                   OR lower(d.external_reference) LIKE @query_pattern ESCAPE '!')
              AND (@created_after IS NULL OR d.created_at >= @created_after)
              AND (@created_before IS NULL OR d.created_at <= @created_before)
              AND (@customer_id IS NULL OR d.customer_id = @customer_id)
              AND (@branch_id IS NULL OR d.branch_id = @branch_id)
              AND (@has_mission IS NULL OR (m.mission_id IS NOT NULL) = @has_mission)
            ORDER BY d.created_at DESC, d.delivery_request_id DESC
            LIMIT @take;
            """;
        const string nextPageSql = """
            SELECT d.delivery_request_id, d.customer_id, d.branch_id, d.external_reference,
                   d.status, d.created_at, m.mission_id, m.status, m.committed_delivery_at
            FROM delivery_requests d
            LEFT JOIN missions m
              ON m.customer_id = d.customer_id
             AND m.delivery_request_id = d.delivery_request_id
            WHERE (@status IS NULL OR d.status = @status)
              AND (@query_pattern IS NULL
                   OR lower(d.delivery_request_id) LIKE @query_pattern ESCAPE '!'
                   OR lower(d.external_reference) LIKE @query_pattern ESCAPE '!')
              AND (@created_after IS NULL OR d.created_at >= @created_after)
              AND (@created_before IS NULL OR d.created_at <= @created_before)
              AND (@customer_id IS NULL OR d.customer_id = @customer_id)
              AND (@branch_id IS NULL OR d.branch_id = @branch_id)
              AND (@has_mission IS NULL OR (m.mission_id IS NOT NULL) = @has_mission)
              AND (d.created_at < @cursor_created_at
                   OR (d.created_at = @cursor_created_at AND d.delivery_request_id < @cursor_id))
            ORDER BY d.created_at DESC, d.delivery_request_id DESC
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
        command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, (object?)customerId ?? DBNull.Value);
        command.Parameters.AddWithValue("branch_id", NpgsqlDbType.Text, (object?)branchId ?? DBNull.Value);
        command.Parameters.AddWithValue("has_mission", NpgsqlDbType.Boolean, (object?)hasMission ?? DBNull.Value);
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
                items.Add(ReadItem(reader));
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
            SELECT d.delivery_request_id, d.customer_id, d.branch_id, d.external_reference,
                   d.status, d.created_at, m.mission_id, m.status, m.committed_delivery_at
            FROM delivery_requests d
            LEFT JOIN missions m
              ON m.customer_id = d.customer_id
             AND m.delivery_request_id = d.delivery_request_id
            WHERE d.delivery_request_id = @delivery_request_id;
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("delivery_request_id", NpgsqlDbType.Text, deliveryRequestId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadItem(reader) : null;
    }

    private static DeliveryRequestOperationsItem ReadItem(NpgsqlDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetFieldValue<DateTimeOffset>(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8));

    private static string BuildPrefixPattern(string query) =>
        query.Replace("!", "!!", StringComparison.Ordinal)
            .Replace("%", "!%", StringComparison.Ordinal)
            .Replace("_", "!_", StringComparison.Ordinal) + "%";

    private static bool MatchesQuery(string requestId, string externalReference, string normalizedQuery) =>
        requestId.StartsWith(normalizedQuery, StringComparison.OrdinalIgnoreCase) ||
        externalReference.StartsWith(normalizedQuery, StringComparison.OrdinalIgnoreCase);
}
