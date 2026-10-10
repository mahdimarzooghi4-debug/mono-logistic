using System.Security.Cryptography;
using System.Text;
using Mono.Api.Domain;
using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

public enum DeliveryRequestWriteOutcome
{
    Created,
    Replayed,
    Conflict
}

public sealed record DeliveryRequestWriteResult(
    string DeliveryRequestId,
    string Status,
    DeliveryRequestWriteOutcome Outcome);

public sealed class PostgresDeliveryRequestRepository(
    NpgsqlDataSource dataSource,
    ICustomerContext customerContext)
{
    public async Task<DeliveryRequestWriteResult> CreateOrReplayAsync(
        string branchId,
        string externalReference,
        string idempotencyKey,
        string canonicalPayload,
        CancellationToken cancellationToken = default)
    {
        var customerId = customerContext.CustomerId;
        var requestHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPayload))).ToLowerInvariant();
        var deliveryRequestId = $"drq_{Guid.NewGuid():N}";

        const string insertSql = """
            INSERT INTO delivery_requests (
                delivery_request_id, customer_id, branch_id, external_reference,
                idempotency_key, request_hash, request_payload, status)
            VALUES (
                @delivery_request_id, @customer_id, @branch_id, @external_reference,
                @idempotency_key, @request_hash, @request_payload, 'RECEIVED')
            ON CONFLICT (customer_id, idempotency_key) DO NOTHING
            RETURNING delivery_request_id, status;
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        string? createdId = null;
        string? createdStatus = null;
        await using (var insert = new NpgsqlCommand(insertSql, connection))
        {
            AddParameters(insert, deliveryRequestId, customerId, branchId, externalReference,
                idempotencyKey, requestHash, canonicalPayload);
            await using var reader = await insert.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                createdId = reader.GetString(0);
                createdStatus = reader.GetString(1);
            }
        }

        if (createdId is not null && createdStatus is not null)
            return new DeliveryRequestWriteResult(createdId, createdStatus, DeliveryRequestWriteOutcome.Created);

        const string existingSql = """
            SELECT delivery_request_id, status, request_hash
            FROM delivery_requests
            WHERE customer_id = @customer_id AND idempotency_key = @idempotency_key;
            """;
        await using var lookup = new NpgsqlCommand(existingSql, connection);
        lookup.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
        lookup.Parameters.AddWithValue("idempotency_key", NpgsqlDbType.Text, idempotencyKey);
        await using var existing = await lookup.ExecuteReaderAsync(cancellationToken);
        if (!await existing.ReadAsync(cancellationToken))
            throw new InvalidOperationException("The idempotency record could not be read after a conflict.");

        var existingId = existing.GetString(0);
        var existingStatus = existing.GetString(1);
        var existingHash = existing.GetString(2);
        var outcome = string.Equals(existingHash, requestHash, StringComparison.Ordinal)
            ? DeliveryRequestWriteOutcome.Replayed
            : DeliveryRequestWriteOutcome.Conflict;

        return new DeliveryRequestWriteResult(existingId, existingStatus, outcome);
    }

    public async Task<DeliveryRequestStatusResponse?> FindStatusAsync(
        string deliveryRequestId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT delivery_request_id, external_reference, status, created_at
            FROM delivery_requests
            WHERE customer_id = @customer_id AND delivery_request_id = @delivery_request_id;
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerContext.CustomerId);
        command.Parameters.AddWithValue("delivery_request_id", NpgsqlDbType.Text, deliveryRequestId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new DeliveryRequestStatusResponse(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetFieldValue<DateTimeOffset>(3));
    }

    public async Task<DeliveryRequestPageResponse?> ListStatusAsync(
        int pageSize,
        string? cursor,
        CancellationToken cancellationToken = default)
    {
        if (pageSize is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(pageSize), "Page size must be between 1 and 100.");

        var customerId = customerContext.CustomerId;
        DateTimeOffset? cursorCreatedAt = null;
        if (cursor is not null)
        {
            const string cursorSql = """
                SELECT created_at
                FROM delivery_requests
                WHERE customer_id = @customer_id AND delivery_request_id = @delivery_request_id;
                """;
            await using var cursorConnection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var cursorCommand = new NpgsqlCommand(cursorSql, cursorConnection);
            cursorCommand.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            cursorCommand.Parameters.AddWithValue("delivery_request_id", NpgsqlDbType.Text, cursor);
            await using var cursorReader = await cursorCommand.ExecuteReaderAsync(cancellationToken);
            if (!await cursorReader.ReadAsync(cancellationToken))
                return null;

            cursorCreatedAt = cursorReader.GetFieldValue<DateTimeOffset>(0);
        }

        const string firstPageSql = """
            SELECT delivery_request_id, external_reference, status, created_at
            FROM delivery_requests
            WHERE customer_id = @customer_id
            ORDER BY created_at DESC, delivery_request_id DESC
            LIMIT @take;
            """;
        const string nextPageSql = """
            SELECT delivery_request_id, external_reference, status, created_at
            FROM delivery_requests
            WHERE customer_id = @customer_id
              AND (created_at < @cursor_created_at
                   OR (created_at = @cursor_created_at AND delivery_request_id < @cursor_id))
            ORDER BY created_at DESC, delivery_request_id DESC
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

        var items = new List<DeliveryRequestStatusResponse>(pageSize + 1);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new DeliveryRequestStatusResponse(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetFieldValue<DateTimeOffset>(3)));
            }
        }

        string? nextCursor = null;
        if (items.Count > pageSize)
        {
            items.RemoveAt(items.Count - 1);
            nextCursor = items[^1].DeliveryRequestId;
        }

        return new DeliveryRequestPageResponse(items, nextCursor);
    }

    private static void AddParameters(
        NpgsqlCommand command,
        string deliveryRequestId,
        string customerId,
        string branchId,
        string externalReference,
        string idempotencyKey,
        string requestHash,
        string canonicalPayload)
    {
        command.Parameters.AddWithValue("delivery_request_id", NpgsqlDbType.Text, deliveryRequestId);
        command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
        command.Parameters.AddWithValue("branch_id", NpgsqlDbType.Text, branchId);
        command.Parameters.AddWithValue("external_reference", NpgsqlDbType.Text, externalReference);
        command.Parameters.AddWithValue("idempotency_key", NpgsqlDbType.Text, idempotencyKey);
        command.Parameters.AddWithValue("request_hash", NpgsqlDbType.Text, requestHash);
        command.Parameters.AddWithValue("request_payload", NpgsqlDbType.Jsonb, canonicalPayload);
    }
}
