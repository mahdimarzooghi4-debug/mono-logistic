using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mono.Api.Domain;
using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

public enum DeliveryQuoteWriteOutcome
{
    Persisted,
    Replayed,
    Conflict,
    DeliveryRequestNotFound
}

public sealed record DeliveryQuoteWriteResult(
    DeliveryQuoteWriteOutcome Outcome,
    string QuoteId,
    int Version);

public sealed class PostgresDeliveryQuoteRepository
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
            audit_evidence_ids = quote.AuditEvidenceIds.ToArray()
        };
        return JsonSerializer.Serialize(payload);
    }
}
