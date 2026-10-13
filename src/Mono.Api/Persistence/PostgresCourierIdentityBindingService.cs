using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

public enum CourierIdentityBindingOutcome
{
    Applied, Replayed, Conflict, CourierNotFound, InvalidState, AuthorityNotConfigured
}

public sealed record CourierIdentityBindingSummary(
    [property: JsonPropertyName("courier_id")] string CourierId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("reason_code")] string ReasonCode,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt);

public sealed record CourierIdentityBindingWriteResult(
    CourierIdentityBindingOutcome Outcome,
    CourierIdentityBindingSummary? Summary);

/// <summary>Stores the Mono-owned mapping from a configured OIDC subject to a courier.</summary>
public sealed class PostgresCourierIdentityBindingService(
    NpgsqlDataSource dataSource,
    IMonoOperationsContext operationsContext,
    string? configuredAuthority,
    TimeProvider? timeProvider = null)
{
    private static readonly Regex CourierIdPattern = new(
        @"\Acrr_[0-9a-f]{32}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex ReasonPattern = new(
        @"\A[A-Z][A-Z0-9_]{1,63}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex EvidencePattern = new(
        @"\A[A-Za-z0-9][A-Za-z0-9._:/~-]{0,255}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex KeyPattern = new(
        @"\A[A-Za-z0-9._~-]{1,128}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public bool IsConfiguredAuthority(string? issuer) =>
        TryConfiguredAuthority(out var expected) &&
        string.Equals(expected, issuer, StringComparison.Ordinal);

    public async Task<CourierIdentityBindingWriteResult> BindAsync(
        string courierId, string subject, string reasonCode, string evidenceReference,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var evidence = evidenceReference ?? string.Empty;
        var key = idempotencyKey ?? string.Empty;
        if (!IsValidCourierId(courierId) || string.IsNullOrWhiteSpace(subject) || subject.Length > 512 ||
            subject.Any(char.IsControl) || !IsValidReason(reasonCode) ||
            !EvidencePattern.IsMatch(evidence) || !KeyPattern.IsMatch(key))
            throw new ArgumentException("Courier identity binding fields are invalid.");
        if (!TryConfiguredAuthority(out var issuer))
            return new(CourierIdentityBindingOutcome.AuthorityNotConfigured, null);

        return await WriteAsync("BIND", courierId, issuer, subject, reasonCode, evidence,
            key, cancellationToken);
    }

    public async Task<CourierIdentityBindingWriteResult> RevokeAsync(
        string courierId, string reasonCode, string evidenceReference,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var evidence = evidenceReference ?? string.Empty;
        var key = idempotencyKey ?? string.Empty;
        if (!IsValidCourierId(courierId) || !IsValidReason(reasonCode) ||
            !EvidencePattern.IsMatch(evidence) || !KeyPattern.IsMatch(key))
            throw new ArgumentException("Courier identity revocation fields are invalid.");
        if (!TryConfiguredAuthority(out var issuer))
            return new(CourierIdentityBindingOutcome.AuthorityNotConfigured, null);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        const string query = """
            SELECT subject FROM courier_identity_binding_events
            WHERE courier_id = @courier_id AND issuer = @issuer AND action = 'BOUND'
            ORDER BY event_sequence DESC LIMIT 1;
            """;
        await using var command = new NpgsqlCommand(query, connection);
        command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
        command.Parameters.AddWithValue("issuer", NpgsqlDbType.Text, issuer);
        var subject = (string?)await command.ExecuteScalarAsync(cancellationToken);
        if (subject is null)
            return new(CourierIdentityBindingOutcome.InvalidState, null);
        return await WriteAsync("REVOKE", courierId, issuer, subject, reasonCode, evidence,
            key, cancellationToken);
    }

    public async Task<CourierIdentityBindingSummary?> FindAsync(
        string courierId, CancellationToken cancellationToken = default)
    {
        if (!IsValidCourierId(courierId))
            throw new ArgumentException("Courier ID is invalid.", nameof(courierId));
        const string sql = """
            SELECT binding.courier_id, binding.status, event.reason_code, binding.updated_at
            FROM courier_identity_bindings binding
            LEFT JOIN LATERAL (
                SELECT reason_code FROM courier_identity_binding_events
                WHERE courier_id = binding.courier_id
                ORDER BY event_sequence DESC LIMIT 1
            ) event ON TRUE
            WHERE binding.courier_id = @courier_id;
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new CourierIdentityBindingSummary(reader.GetString(0), reader.GetString(1),
                reader.IsDBNull(2) ? string.Empty : reader.GetString(2), reader.GetFieldValue<DateTimeOffset>(3))
            : null;
    }

    private async Task<CourierIdentityBindingWriteResult> WriteAsync(
        string operation, string courierId, string issuer, string subject,
        string reasonCode, string evidenceReference, string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var actorId = RequireAdministrator();
        var requestHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join("\n", operation, courierId, issuer, subject, reasonCode, evidenceReference))));

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var lockIdempotency = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended(@lock_key, 0));", connection, transaction))
        {
            lockIdempotency.Parameters.AddWithValue("lock_key", NpgsqlDbType.Text,
                string.Join("\\n", actorId, operation, idempotencyKey));
            await lockIdempotency.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var lockCourier = new NpgsqlCommand(
            "SELECT courier_id FROM couriers WHERE courier_id = @courier_id FOR UPDATE;", connection, transaction))
        {
            lockCourier.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
            if (await lockCourier.ExecuteScalarAsync(cancellationToken) is null)
                return new(CourierIdentityBindingOutcome.CourierNotFound, null);
        }

        const string replaySql = """
            SELECT request_hash, event_id FROM courier_identity_binding_idempotency
            WHERE actor_id = @actor_id AND operation = @operation AND idempotency_key = @key;
            """;
        await using (var replay = new NpgsqlCommand(replaySql, connection, transaction))
        {
            replay.Parameters.AddWithValue("actor_id", NpgsqlDbType.Text, actorId);
            replay.Parameters.AddWithValue("operation", NpgsqlDbType.Text, operation);
            replay.Parameters.AddWithValue("key", NpgsqlDbType.Text, idempotencyKey);
            await using var reader = await replay.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                if (!string.Equals(reader.GetString(0), requestHash, StringComparison.Ordinal))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return new(CourierIdentityBindingOutcome.Conflict, null);
                }
                await reader.CloseAsync();
                var summary = await ReadSummaryAsync(connection, transaction, courierId, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new(CourierIdentityBindingOutcome.Replayed, summary);
            }
        }

        if (operation == "BIND")
        {
            const string canBind = """
                SELECT courier.status = 'ACTIVE'
                    AND NOT COALESCE(latest.action = 'SUSPENDED', FALSE)
                    AND NOT EXISTS (
                        SELECT 1 FROM courier_identity_bindings b
                        WHERE b.courier_id = @courier_id AND b.status = 'BOUND')
                    AND NOT EXISTS (
                        SELECT 1 FROM courier_identity_bindings b
                        WHERE b.issuer = @issuer AND b.subject = @subject AND b.status = 'BOUND')
                FROM couriers courier
                LEFT JOIN LATERAL (
                    SELECT action FROM courier_operational_status_events
                    WHERE courier_id = courier.courier_id ORDER BY event_sequence DESC LIMIT 1
                ) latest ON TRUE WHERE courier.courier_id = @courier_id;
                """;
            await using var check = new NpgsqlCommand(canBind, connection, transaction);
            check.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
            check.Parameters.AddWithValue("issuer", NpgsqlDbType.Text, issuer);
            check.Parameters.AddWithValue("subject", NpgsqlDbType.Text, subject);
            if (!Equals(await check.ExecuteScalarAsync(cancellationToken), true))
            {
                await transaction.RollbackAsync(cancellationToken);
                return new(CourierIdentityBindingOutcome.InvalidState, null);
            }
        }
        else
        {
            const string canRevoke = """
                SELECT EXISTS(SELECT 1 FROM courier_identity_bindings
                    WHERE courier_id = @courier_id AND issuer = @issuer AND subject = @subject AND status = 'BOUND');
                """;
            await using var check = new NpgsqlCommand(canRevoke, connection, transaction);
            check.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
            check.Parameters.AddWithValue("issuer", NpgsqlDbType.Text, issuer);
            check.Parameters.AddWithValue("subject", NpgsqlDbType.Text, subject);
            if (!Equals(await check.ExecuteScalarAsync(cancellationToken), true))
            {
                await transaction.RollbackAsync(cancellationToken);
                return new(CourierIdentityBindingOutcome.InvalidState, null);
            }
        }

        var now = _timeProvider.GetUtcNow();
        var eventId = "cibe_" + Guid.NewGuid().ToString("N");
        const string insertEvent = """
            INSERT INTO courier_identity_binding_events
                (event_id, courier_id, action, issuer, subject, actor_id, reason_code, evidence_reference, occurred_at)
            VALUES (@event_id, @courier_id, @action, @issuer, @subject, @actor_id, @reason_code, @evidence_reference, @occurred_at);
            """;
        await using (var insert = new NpgsqlCommand(insertEvent, connection, transaction))
        {
            insert.Parameters.AddWithValue("event_id", NpgsqlDbType.Text, eventId);
            insert.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
            insert.Parameters.AddWithValue("action", NpgsqlDbType.Text, operation == "BIND" ? "BOUND" : "REVOKED");
            insert.Parameters.AddWithValue("issuer", NpgsqlDbType.Text, issuer);
            insert.Parameters.AddWithValue("subject", NpgsqlDbType.Text, subject);
            insert.Parameters.AddWithValue("actor_id", NpgsqlDbType.Text, actorId);
            insert.Parameters.AddWithValue("reason_code", NpgsqlDbType.Text, reasonCode);
            insert.Parameters.AddWithValue("evidence_reference", NpgsqlDbType.Text, evidenceReference);
            insert.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, now);
            try { await insert.ExecuteNonQueryAsync(cancellationToken); }
            catch (PostgresException exception) when (exception.SqlState is "23505" or "P0001")
            {
                await transaction.RollbackAsync(cancellationToken);
                return new(CourierIdentityBindingOutcome.InvalidState, null);
            }
        }

        const string insertIdempotency = """
            INSERT INTO courier_identity_binding_idempotency
                (actor_id, operation, idempotency_key, request_hash, event_id, created_at)
            VALUES (@actor_id, @operation, @key, @request_hash, @event_id, @created_at);
            """;
        await using (var insert = new NpgsqlCommand(insertIdempotency, connection, transaction))
        {
            insert.Parameters.AddWithValue("actor_id", NpgsqlDbType.Text, actorId);
            insert.Parameters.AddWithValue("operation", NpgsqlDbType.Text, operation);
            insert.Parameters.AddWithValue("key", NpgsqlDbType.Text, idempotencyKey);
            insert.Parameters.AddWithValue("request_hash", NpgsqlDbType.Text, requestHash);
            insert.Parameters.AddWithValue("event_id", NpgsqlDbType.Text, eventId);
            insert.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, now);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        var result = await ReadSummaryAsync(connection, transaction, courierId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(CourierIdentityBindingOutcome.Applied, result);
    }

    private static async Task<CourierIdentityBindingSummary?> ReadSummaryAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string courierId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT b.courier_id, b.status, e.reason_code, b.updated_at
            FROM courier_identity_bindings b
            LEFT JOIN LATERAL (SELECT reason_code FROM courier_identity_binding_events
                WHERE courier_id = b.courier_id ORDER BY event_sequence DESC LIMIT 1) e ON TRUE
            WHERE b.courier_id = @courier_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                reader.GetFieldValue<DateTimeOffset>(3))
            : null;
    }

    private string RequireAdministrator()
    {
        var actors = operationsContext.ActorId;
        if (string.IsNullOrWhiteSpace(actors) ||
            !operationsContext.Roles.Contains(MonoOperationsRoles.Administrator, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("MFA-authenticated Mono administrator context is required.");
        return actors;
    }

    private bool TryConfiguredAuthority(out string issuer)
    {
        issuer = configuredAuthority?.Trim() ?? string.Empty;
        return Uri.TryCreate(issuer, UriKind.Absolute, out var uri) &&
            string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsValidCourierId(string? value) => value is not null && CourierIdPattern.IsMatch(value);
    private static bool IsValidReason(string? value) => value is not null && ReasonPattern.IsMatch(value);
}
