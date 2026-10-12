using Mono.Api.Domain;
using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

public sealed class PostgresMissionHistoryRepository(
    NpgsqlDataSource dataSource,
    ICustomerContext customerContext)
{
    public async Task<MissionHistoryPageResponse?> ListAsync(
        int pageSize,
        string? cursor,
        CancellationToken cancellationToken = default)
    {
        if (pageSize is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(pageSize));

        var customerId = customerContext.CustomerId;
        DateTimeOffset? cursorCreatedAt = null;
        if (cursor is not null)
        {
            const string cursorSql = """
                SELECT created_at
                FROM missions
                WHERE customer_id = @customer_id AND mission_id = @mission_id;
                """;
            await using var cursorConnection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var cursorCommand = new NpgsqlCommand(cursorSql, cursorConnection);
            cursorCommand.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            cursorCommand.Parameters.AddWithValue("mission_id", NpgsqlDbType.Text, cursor);
            await using var cursorReader = await cursorCommand.ExecuteReaderAsync(cancellationToken);
            if (!await cursorReader.ReadAsync(cancellationToken))
                return null;
            cursorCreatedAt = cursorReader.GetFieldValue<DateTimeOffset>(0);
        }

        const string firstPageSql = """
            SELECT mission_id, delivery_request_id, quote_id, quote_version,
                   promise_id, promise_version, status, committed_delivery_at, created_at
            FROM missions
            WHERE customer_id = @customer_id
            ORDER BY created_at DESC, mission_id DESC
            LIMIT @take;
            """;
        const string nextPageSql = """
            SELECT mission_id, delivery_request_id, quote_id, quote_version,
                   promise_id, promise_version, status, committed_delivery_at, created_at
            FROM missions
            WHERE customer_id = @customer_id
              AND (created_at < @cursor_created_at
                   OR (created_at = @cursor_created_at AND mission_id < @cursor_id))
            ORDER BY created_at DESC, mission_id DESC
            LIMIT @take;
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(cursor is null ? firstPageSql : nextPageSql, connection);
        command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
        command.Parameters.AddWithValue("take", NpgsqlDbType.Integer, pageSize + 1);
        if (cursor is not null)
        {
            command.Parameters.AddWithValue("cursor_created_at", NpgsqlDbType.TimestampTz, cursorCreatedAt!.Value);
            command.Parameters.AddWithValue("cursor_id", NpgsqlDbType.Text, cursor);
        }

        var items = new List<MissionConfirmationResponse>(pageSize + 1);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new MissionConfirmationResponse(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt32(3),
                    reader.GetString(4),
                    reader.GetInt32(5),
                    reader.GetString(6),
                    reader.GetFieldValue<DateTimeOffset>(7),
                    reader.GetFieldValue<DateTimeOffset>(8)));
            }
        }

        string? nextCursor = null;
        if (items.Count > pageSize)
        {
            items.RemoveAt(items.Count - 1);
            nextCursor = items[^1].MissionId;
        }

        return new MissionHistoryPageResponse(items, nextCursor);
    }
}
