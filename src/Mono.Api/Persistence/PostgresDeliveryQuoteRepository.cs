using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mono.Api.Domain;
using Mono.Api.Security;
using Mono.Api.Services;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

public enum DeliveryQuoteWriteOutcome
{
    Persisted,
    Replayed,
    Conflict,
    IdempotencyConflict,
    DeliveryRequestNotFound
}

public sealed record DeliveryQuoteWriteResult(
    DeliveryQuoteWriteOutcome Outcome,
    string QuoteId,
    int Version);

public sealed class PostgresDeliveryQuoteRepository : IDeliveryQuoteStore
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ICustomerContext _customerContext;
    private readonly TimeProvider _timeProvider;

    public PostgresDeliveryQuoteRepository(
        NpgsqlDataSource dataSource,
        ICustomerContext customerContext,
        TimeProvider? timeProvider = null)
    {
        _dataSource = dataSource;
        _customerContext = customerContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<DeliveryQuoteWriteResult> PersistAsync(
        DeliveryQuote quote,
        CancellationToken cancellationToken = default)
    {
        DeliveryQuoteContract.Validate(quote);
        var customerId = _customerContext.CustomerId;
        if (!string.Equals(quote.CustomerId, customerId, StringComparison.Ordinal))
            throw new ArgumentException("Quote customer must match the authenticated tenant.", nameof(quote));

        var canonicalSnapshot = CanonicalSnapshot(quote);
        var snapshotHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonicalSnapshot))).ToLowerInvariant();
        var createdAt = _timeProvider.GetUtcNow();

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        const string lockRequestSql = """
            SELECT 1 FROM delivery_requests
            WHERE customer_id = @customer_id AND delivery_request_id = @delivery_request_id
            FOR UPDATE;
            """;
        await using (var lockRequest = new NpgsqlCommand(lockRequestSql, connection, transaction))
        {
            lockRequest.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            lockRequest.Parameters.AddWithValue("delivery_request_id", NpgsqlDbType.Text, quote.DeliveryRequestId);
            if (await lockRequest.ExecuteScalarAsync(cancellationToken) is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return new DeliveryQuoteWriteResult(
                    DeliveryQuoteWriteOutcome.DeliveryRequestNotFound, quote.QuoteId, quote.Version);
            }
        }

        const string existingSql = """
            SELECT quote_id, snapshot_hash
            FROM delivery_quote_versions
            WHERE customer_id = @customer_id
              AND delivery_request_id = @delivery_request_id
              AND version = @version;
            """;
        await using (var existing = new NpgsqlCommand(existingSql, connection, transaction))
        {
            existing.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            existing.Parameters.AddWithValue("delivery_request_id", NpgsqlDbType.Text, quote.DeliveryRequestId);
            existing.Parameters.AddWithValue("version", NpgsqlDbType.Integer, quote.Version);
            await using var reader = await existing.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var existingQuoteId = reader.GetString(0);
                var existingHash = reader.GetString(1);
                await reader.DisposeAsync();
                await transaction.CommitAsync(cancellationToken);
                var outcome = existingQuoteId == quote.QuoteId &&
                              string.Equals(existingHash, snapshotHash, StringComparison.Ordinal)
                    ? DeliveryQuoteWriteOutcome.Replayed
                    : DeliveryQuoteWriteOutcome.Conflict;
                return new DeliveryQuoteWriteResult(outcome, existingQuoteId, quote.Version);
            }
        }

        const string currentVersionSql = """
            SELECT COALESCE(MAX(version), 0)
            FROM delivery_quote_versions
            WHERE customer_id = @customer_id AND delivery_request_id = @delivery_request_id;
            """;
        await using var currentVersionCommand = new NpgsqlCommand(currentVersionSql, connection, transaction);
        currentVersionCommand.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
        currentVersionCommand.Parameters.AddWithValue("delivery_request_id", NpgsqlDbType.Text, quote.DeliveryRequestId);
        var currentVersion = Convert.ToInt32(await currentVersionCommand.ExecuteScalarAsync(cancellationToken));
        if (quote.Version != currentVersion + 1)
        {
            await transaction.CommitAsync(cancellationToken);
            return new DeliveryQuoteWriteResult(
                DeliveryQuoteWriteOutcome.Conflict, quote.QuoteId, quote.Version);
        }

        const string insertSql = """
            INSERT INTO delivery_quote_versions (
                customer_id, delivery_request_id, quote_id, version, issued_at, expires_at,
                snapshot_hash, snapshot, created_at)
            VALUES (
                @customer_id, @delivery_request_id, @quote_id, @version, @issued_at, @expires_at,
                @snapshot_hash, @snapshot, @created_at);
            """;
        await using (var insert = new NpgsqlCommand(insertSql, connection, transaction))
        {
            insert.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            insert.Parameters.AddWithValue("delivery_request_id", NpgsqlDbType.Text, quote.DeliveryRequestId);
            insert.Parameters.AddWithValue("quote_id", NpgsqlDbType.Text, quote.QuoteId);
            insert.Parameters.AddWithValue("version", NpgsqlDbType.Integer, quote.Version);
            insert.Parameters.AddWithValue("issued_at", NpgsqlDbType.TimestampTz, quote.IssuedAt.ToUniversalTime());
            insert.Parameters.AddWithValue("expires_at", NpgsqlDbType.TimestampTz, quote.ExpiresAt.ToUniversalTime());
            insert.Parameters.AddWithValue("snapshot_hash", NpgsqlDbType.Text, snapshotHash);
            insert.Parameters.AddWithValue("snapshot", NpgsqlDbType.Jsonb, canonicalSnapshot);
            insert.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, createdAt);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new DeliveryQuoteWriteResult(DeliveryQuoteWriteOutcome.Persisted, quote.QuoteId, quote.Version);
    }


    public async Task<DeliveryQuoteWriteResult> IssueInitialIdempotentlyAsync(
        DeliveryQuote quote,
        string idempotencyKey,
        string requestHash,
        CancellationToken cancellationToken = default)
    {
        DeliveryQuoteContract.Validate(quote);
        if (string.IsNullOrWhiteSpace(idempotencyKey) ||
            idempotencyKey.Length > 200 ||
            idempotencyKey.Any(char.IsControl) ||
            requestHash.Length != 64 ||
            requestHash.Any(character => !((character >= '0' && character <= '9') ||
                                            (character >= 'a' && character <= 'f'))))
        {
            throw new ArgumentException("A valid idempotency key and lowercase SHA-256 request hash are required.");
        }

        var customerId = _customerContext.CustomerId;
        if (!string.Equals(quote.CustomerId, customerId, StringComparison.Ordinal))
            throw new ArgumentException("Quote customer must match the authenticated tenant.", nameof(quote));

        var canonicalSnapshot = CanonicalSnapshot(quote);
        var snapshotHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonicalSnapshot))).ToLowerInvariant();
        var createdAt = _timeProvider.GetUtcNow();
        var lockMaterial = Encoding.UTF8.GetBytes(customerId + ":" + idempotencyKey);
        var lockId = BitConverter.ToInt64(SHA256.HashData(lockMaterial).AsSpan(0, sizeof(long)));

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var idempotencyLock = new NpgsqlCommand(
                         "SELECT pg_advisory_xact_lock(@lock_id);", connection, transaction))
        {
            idempotencyLock.Parameters.AddWithValue("lock_id", NpgsqlDbType.Bigint, lockId);
            await idempotencyLock.ExecuteNonQueryAsync(cancellationToken);
        }

        const string existingKeySql = """
            SELECT delivery_request_id, request_hash, quote_id, version
            FROM delivery_quote_issuance_idempotency
            WHERE customer_id = @customer_id AND idempotency_key = @idempotency_key;
            """;
        await using (var existingKey = new NpgsqlCommand(existingKeySql, connection, transaction))
        {
            existingKey.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            existingKey.Parameters.AddWithValue("idempotency_key", NpgsqlDbType.Text, idempotencyKey);
            await using var reader = await existingKey.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var existingRequestId = reader.GetString(0);
                var existingRequestHash = reader.GetString(1);
                var existingQuoteId = reader.GetString(2);
                var existingVersion = reader.GetInt32(3);
                await reader.DisposeAsync();
                await transaction.CommitAsync(cancellationToken);
                var outcome = string.Equals(existingRequestId, quote.DeliveryRequestId, StringComparison.Ordinal) &&
                              string.Equals(existingRequestHash, requestHash, StringComparison.Ordinal)
                    ? DeliveryQuoteWriteOutcome.Replayed
                    : DeliveryQuoteWriteOutcome.IdempotencyConflict;
                return new DeliveryQuoteWriteResult(outcome, existingQuoteId, existingVersion);
            }
        }

        const string lockRequestSql = """
            SELECT 1 FROM delivery_requests
            WHERE customer_id = @customer_id AND delivery_request_id = @delivery_request_id
            FOR UPDATE;
            """;
        await using (var lockRequest = new NpgsqlCommand(lockRequestSql, connection, transaction))
        {
            lockRequest.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            lockRequest.Parameters.AddWithValue("delivery_request_id", NpgsqlDbType.Text, quote.DeliveryRequestId);
            if (await lockRequest.ExecuteScalarAsync(cancellationToken) is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return new DeliveryQuoteWriteResult(
                    DeliveryQuoteWriteOutcome.DeliveryRequestNotFound, quote.QuoteId, quote.Version);
            }
        }

        const string currentVersionSql = """
            SELECT COALESCE(MAX(version), 0)
            FROM delivery_quote_versions
            WHERE customer_id = @customer_id AND delivery_request_id = @delivery_request_id;
            """;
        await using var currentVersionCommand = new NpgsqlCommand(currentVersionSql, connection, transaction);
        currentVersionCommand.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
        currentVersionCommand.Parameters.AddWithValue("delivery_request_id", NpgsqlDbType.Text, quote.DeliveryRequestId);
        var currentVersion = Convert.ToInt32(await currentVersionCommand.ExecuteScalarAsync(cancellationToken));
        if (currentVersion != 0 || quote.Version != 1)
        {
            await transaction.CommitAsync(cancellationToken);
            return new DeliveryQuoteWriteResult(
                DeliveryQuoteWriteOutcome.Conflict, quote.QuoteId, quote.Version);
        }

        const string insertQuoteSql = """
            INSERT INTO delivery_quote_versions (
                customer_id, delivery_request_id, quote_id, version, issued_at, expires_at,
                snapshot_hash, snapshot, created_at)
            VALUES (
                @customer_id, @delivery_request_id, @quote_id, @version, @issued_at, @expires_at,
                @snapshot_hash, @snapshot, @created_at);
            """;
        await using (var insertQuote = new NpgsqlCommand(insertQuoteSql, connection, transaction))
        {
            insertQuote.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            insertQuote.Parameters.AddWithValue("delivery_request_id", NpgsqlDbType.Text, quote.DeliveryRequestId);
            insertQuote.Parameters.AddWithValue("quote_id", NpgsqlDbType.Text, quote.QuoteId);
            insertQuote.Parameters.AddWithValue("version", NpgsqlDbType.Integer, quote.Version);
            insertQuote.Parameters.AddWithValue("issued_at", NpgsqlDbType.TimestampTz, quote.IssuedAt.ToUniversalTime());
            insertQuote.Parameters.AddWithValue("expires_at", NpgsqlDbType.TimestampTz, quote.ExpiresAt.ToUniversalTime());
            insertQuote.Parameters.AddWithValue("snapshot_hash", NpgsqlDbType.Text, snapshotHash);
            insertQuote.Parameters.AddWithValue("snapshot", NpgsqlDbType.Jsonb, canonicalSnapshot);
            insertQuote.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, createdAt);
            await insertQuote.ExecuteNonQueryAsync(cancellationToken);
        }

        const string insertIdempotencySql = """
            INSERT INTO delivery_quote_issuance_idempotency (
                customer_id, idempotency_key, delivery_request_id, request_hash,
                quote_id, version, created_at)
            VALUES (
                @customer_id, @idempotency_key, @delivery_request_id, @request_hash,
                @quote_id, @version, @created_at);
            """;
        await using (var insertIdempotency = new NpgsqlCommand(insertIdempotencySql, connection, transaction))
        {
            insertIdempotency.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            insertIdempotency.Parameters.AddWithValue("idempotency_key", NpgsqlDbType.Text, idempotencyKey);
            insertIdempotency.Parameters.AddWithValue("delivery_request_id", NpgsqlDbType.Text, quote.DeliveryRequestId);
            insertIdempotency.Parameters.AddWithValue("request_hash", NpgsqlDbType.Text, requestHash);
            insertIdempotency.Parameters.AddWithValue("quote_id", NpgsqlDbType.Text, quote.QuoteId);
            insertIdempotency.Parameters.AddWithValue("version", NpgsqlDbType.Integer, quote.Version);
            insertIdempotency.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, createdAt);
            await insertIdempotency.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new DeliveryQuoteWriteResult(DeliveryQuoteWriteOutcome.Persisted, quote.QuoteId, quote.Version);
    }


    public async Task<DeliveryQuoteReadResponse?> FindLatestByDeliveryRequestAsync(
        string deliveryRequestId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT quote_id, delivery_request_id, version, issued_at, expires_at,
                   expires_at <= @evaluated_at,
                   (snapshot->'fare'->>'distance_meters')::numeric,
                   (snapshot->'fare'->>'fare_toman')::integer,
                   (snapshot->'fare'->>'buyer_share_toman')::integer,
                   (snapshot->'fare'->>'seller_share_toman')::integer,
                   created_at
            FROM delivery_quote_versions
            WHERE customer_id = @customer_id AND delivery_request_id = @delivery_request_id
            ORDER BY version DESC
            LIMIT 1;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, _customerContext.CustomerId);
        command.Parameters.AddWithValue("delivery_request_id", NpgsqlDbType.Text, deliveryRequestId);
        command.Parameters.AddWithValue("evaluated_at", NpgsqlDbType.TimestampTz, _timeProvider.GetUtcNow());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadQuote(reader) : null;
    }

    public async Task<DeliveryQuoteVersionPageResponse?> ListVersionsByDeliveryRequestAsync(
        string deliveryRequestId,
        int pageSize,
        int? beforeVersion = null,
        CancellationToken cancellationToken = default)
    {
        if (pageSize is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(pageSize), "Page size must be between 1 and 100.");
        if (beforeVersion is < 1)
            throw new ArgumentOutOfRangeException(nameof(beforeVersion), "Version cursor must be positive.");

        const string existsSql = """
            SELECT 1
            FROM delivery_quote_versions
            WHERE customer_id = @customer_id AND delivery_request_id = @delivery_request_id
            LIMIT 1;
            """;
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using (var exists = new NpgsqlCommand(existsSql, connection))
        {
            exists.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, _customerContext.CustomerId);
            exists.Parameters.AddWithValue("delivery_request_id", NpgsqlDbType.Text, deliveryRequestId);
            if (await exists.ExecuteScalarAsync(cancellationToken) is null)
                return null;
        }

        const string sql = """
            SELECT quote_id, delivery_request_id, version, issued_at, expires_at,
                   expires_at <= @evaluated_at,
                   (snapshot->'fare'->>'distance_meters')::numeric,
                   (snapshot->'fare'->>'fare_toman')::integer,
                   (snapshot->'fare'->>'buyer_share_toman')::integer,
                   (snapshot->'fare'->>'seller_share_toman')::integer,
                   created_at
            FROM delivery_quote_versions
            WHERE customer_id = @customer_id
              AND delivery_request_id = @delivery_request_id
              AND (@before_version IS NULL OR version < @before_version)
            ORDER BY version DESC
            LIMIT @take;
            """;
        var items = new List<DeliveryQuoteReadResponse>(pageSize + 1);
        await using (var command = new NpgsqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, _customerContext.CustomerId);
            command.Parameters.AddWithValue("delivery_request_id", NpgsqlDbType.Text, deliveryRequestId);
            command.Parameters.AddWithValue("before_version", NpgsqlDbType.Integer, (object?)beforeVersion ?? DBNull.Value);
            command.Parameters.AddWithValue("evaluated_at", NpgsqlDbType.TimestampTz, _timeProvider.GetUtcNow());
            command.Parameters.AddWithValue("take", NpgsqlDbType.Integer, pageSize + 1);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                items.Add(ReadQuote(reader));
        }

        var hasMore = items.Count > pageSize;
        if (hasMore)
            items.RemoveAt(items.Count - 1);
        return new DeliveryQuoteVersionPageResponse(items, hasMore ? items[^1].Version : null);
    }

    private static DeliveryQuoteReadResponse ReadQuote(NpgsqlDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt32(2),
            reader.GetFieldValue<DateTimeOffset>(3),
            reader.GetFieldValue<DateTimeOffset>(4),
            reader.GetBoolean(5),
            reader.GetDecimal(6),
            reader.GetInt32(7),
            reader.GetInt32(8),
            reader.GetInt32(9),
            reader.GetFieldValue<DateTimeOffset>(10));

    private static string CanonicalSnapshot(DeliveryQuote quote)
    {
        var payload = new
        {
            quote_id = quote.QuoteId,
            customer_id = quote.CustomerId,
            delivery_request_id = quote.DeliveryRequestId,
            version = quote.Version,
            issued_at = quote.IssuedAt.ToUniversalTime(),
            expires_at = quote.ExpiresAt.ToUniversalTime(),
            fare = new
            {
                policy_version = quote.Fare.PolicyVersion,
                distance_meters = quote.Fare.DistanceMeters,
                distance_band_start_meters = quote.Fare.DistanceBandStartMeters,
                fare_toman = quote.Fare.FareToman,
                buyer_share_toman = quote.Fare.BuyerShareToman,
                seller_share_toman = quote.Fare.SellerShareToman
            },
            policy_versions = quote.PolicyVersions.OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal),
            audit_evidence_ids = quote.AuditEvidenceIds.ToArray(),
            pricing_evidence = quote.PricingEvidence is null
                ? null
                : new
                {
                    evidence_id = quote.PricingEvidence.EvidenceId,
                    observed_at = quote.PricingEvidence.ObservedAt.ToUniversalTime(),
                    distance_meters = quote.PricingEvidence.DistanceMeters,
                    source_id = quote.PricingEvidence.SourceId,
                    payload_sha256 = quote.PricingEvidence.PayloadSha256
                }
        };
        return JsonSerializer.Serialize(payload);
    }
}
