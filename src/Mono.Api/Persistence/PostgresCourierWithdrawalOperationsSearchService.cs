using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

public sealed class PostgresCourierWithdrawalOperationsSearchService(
    NpgsqlDataSource dataSource,
    IMonoOperationsContext operationsContext)
{
    public async Task<CourierWithdrawalPage> SearchAsync(
        string? status, string? courierId, int pageSize, long? beforeSequence,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (pageSize is < 1 or > 100 || beforeSequence is <= 0)
            throw new ArgumentOutOfRangeException(nameof(pageSize));

        const string sql = """
            WITH latest AS (
                SELECT DISTINCT ON (event.withdrawal_id)
                    event.withdrawal_id, event.action, event.reason_code, event.bank_reference,
                    event.withdrawal_event_sequence
                FROM courier_withdrawal_events event
                ORDER BY event.withdrawal_id, event.withdrawal_event_sequence DESC
            )
            SELECT request.withdrawal_id, request.courier_id, request.amount_toman,
                   destination.masked_iban, latest.action, request.created_at,
                   latest.reason_code, latest.bank_reference, latest.withdrawal_event_sequence
            FROM courier_withdrawal_requests request
            JOIN latest ON latest.withdrawal_id = request.withdrawal_id
            JOIN courier_payout_destinations destination ON destination.destination_id = request.destination_id
            WHERE (@status IS NULL OR latest.action = @status)
              AND (@courier_id IS NULL OR request.courier_id = @courier_id)
              AND (@before_sequence IS NULL OR latest.withdrawal_event_sequence < @before_sequence)
            ORDER BY latest.withdrawal_event_sequence DESC
            LIMIT @take;
            """;
        var items = new List<CourierWithdrawalItem>();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.Add("status", NpgsqlDbType.Text).Value = status is null ? DBNull.Value : status;
            command.Parameters.Add("courier_id", NpgsqlDbType.Text).Value =
                courierId is null ? DBNull.Value : courierId;
            command.Parameters.Add("before_sequence", NpgsqlDbType.Bigint).Value =
                beforeSequence.HasValue ? beforeSequence.Value : DBNull.Value;
            command.Parameters.AddWithValue("take", NpgsqlDbType.Integer, checked(pageSize + 1));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                items.Add(new CourierWithdrawalItem(reader.GetString(0), reader.GetString(1),
                    reader.GetInt64(2), reader.GetString(3), reader.GetString(4),
                    reader.GetFieldValue<DateTimeOffset>(5), reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7), reader.GetInt64(8)));
        }

        var hasMore = items.Count > pageSize;
        if (hasMore)
            items.RemoveAt(pageSize);
        var nextSequence = hasMore ? items[^1].EventSequence : (long?)null;
        await RecordReadAsync(connection, transaction, status, courierId, beforeSequence,
            pageSize, items.Count, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new CourierWithdrawalPage(items, nextSequence);
    }

    private async Task RecordReadAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction,
        string? status, string? courierId, long? beforeSequence, int pageSize,
        int resultCount, DateTimeOffset now, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO courier_withdrawal_operations_search_audit
                (search_audit_id, actor_id, status_filter, courier_id_filter,
                 cursor_sequence, page_size, result_count, occurred_at)
            VALUES (@id, @actor_id, @status, @courier_id, @cursor, @page_size, @result_count, @occurred_at);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Text, "cws_" + Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("actor_id", NpgsqlDbType.Text, operationsContext.ActorId);
        command.Parameters.Add("status", NpgsqlDbType.Text).Value = status is null ? DBNull.Value : status;
        command.Parameters.Add("courier_id", NpgsqlDbType.Text).Value =
            courierId is null ? DBNull.Value : courierId;
        command.Parameters.Add("cursor", NpgsqlDbType.Bigint).Value =
            beforeSequence.HasValue ? beforeSequence.Value : DBNull.Value;
        command.Parameters.AddWithValue("page_size", NpgsqlDbType.Integer, pageSize);
        command.Parameters.AddWithValue("result_count", NpgsqlDbType.Integer, resultCount);
        command.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
