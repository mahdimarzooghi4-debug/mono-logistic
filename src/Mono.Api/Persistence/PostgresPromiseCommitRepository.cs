using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mono.Api.Domain;
using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

public enum PromiseCommitWriteOutcome
{
    Committed,
    Replayed,
    Conflict,
    DeliveryRequestNotFound
}

public sealed record PromiseCommitWriteResult(
    PromiseCommitWriteOutcome Outcome,
    string PromiseId,
    int Version,
    string? EventId);

public sealed class PostgresPromiseCommitRepository : IPromiseCommitStore
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ICustomerContext _customerContext;
    private readonly TimeProvider _timeProvider;

    public PostgresPromiseCommitRepository(
        NpgsqlDataSource dataSource,
        ICustomerContext customerContext,
        TimeProvider? timeProvider = null)
    {
        _dataSource = dataSource;
        _customerContext = customerContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<PromiseCommitWriteResult> CommitAsync(
        PromiseCommitProposal proposal,
        CancellationToken cancellationToken = default)
    {
        PromiseCommitProposalValidator.Validate(proposal);
        var customerId = _customerContext.CustomerId;
        var canonicalProposal = CanonicalProposal(proposal);
        var proposalHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonicalProposal))).ToLowerInvariant();
        var policyVersionsJson = JsonSerializer.Serialize(
            proposal.PolicyVersions.OrderBy(x => x.Key, StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal));
        var reasonCodesJson = JsonSerializer.Serialize(
            proposal.ReasonCodes.OrderBy(x => x, StringComparer.Ordinal).ToArray());
        var now = _timeProvider.GetUtcNow();
        var eventId = $"evt_{Guid.NewGuid():N}";
        var outcome = proposal.Outcome.ToString().ToUpperInvariant();
        var eventType = proposal.Version == 1 ? "promise.committed" : "promise.revised";
        var eventData = JsonSerializer.Serialize(new
        {
            promise_id = proposal.PromiseId,
            delivery_request_id = proposal.DeliveryRequestId,
            version = proposal.Version,
            decision_id = proposal.DecisionId,
            committed_delivery_at = proposal.CommittedDeliveryAt.ToUniversalTime(),
            outcome,
            safety_buffer_ticks = proposal.SafetyBuffer.Ticks,
            safety_buffer_policy_version = proposal.SafetyBufferPolicyVersion,
            policy_versions = JsonSerializer.Deserialize<JsonElement>(policyVersionsJson),
            reason_codes = JsonSerializer.Deserialize<JsonElement>(reasonCodesJson)
        });

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
            lockRequest.Parameters.AddWithValue("delivery_request_id", NpgsqlDbType.Text, proposal.DeliveryRequestId);
            var requestExists = await lockRequest.ExecuteScalarAsync(cancellationToken);
            if (requestExists is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return new PromiseCommitWriteResult(
                    PromiseCommitWriteOutcome.DeliveryRequestNotFound,
                    proposal.PromiseId,
                    proposal.Version,
                    null);
            }
        }

        const string decisionLockSql = "SELECT pg_advisory_xact_lock(hashtextextended(@lock_key, 0));";
        await using (var decisionLock = new NpgsqlCommand(decisionLockSql, connection, transaction))
        {
            decisionLock.Parameters.AddWithValue("lock_key", NpgsqlDbType.Text, $"{customerId}:{proposal.DecisionId}");
            await decisionLock.ExecuteNonQueryAsync(cancellationToken);
        }

        const string replaySql = """
            SELECT promise_id, version, proposal_hash, event_id
            FROM promise_versions
            WHERE customer_id = @customer_id AND decision_id = @decision_id;
            """;
        await using (var replay = new NpgsqlCommand(replaySql, connection, transaction))
        {
            replay.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            replay.Parameters.AddWithValue("decision_id", NpgsqlDbType.Text, proposal.DecisionId);
            await using var reader = await replay.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var existingPromiseId = reader.GetString(0);
                var existingVersion = reader.GetInt32(1);
                var existingHash = reader.GetString(2);
                var existingEventId = reader.GetString(3);
                await reader.DisposeAsync();
                await transaction.CommitAsync(cancellationToken);
                var result = string.Equals(existingHash, proposalHash, StringComparison.Ordinal)
                    ? PromiseCommitWriteOutcome.Replayed
                    : PromiseCommitWriteOutcome.Conflict;
                return new PromiseCommitWriteResult(result, existingPromiseId, existingVersion, existingEventId);
            }
        }

        const string currentSql = """
            SELECT promise_id, current_version
            FROM promises
            WHERE customer_id = @customer_id AND delivery_request_id = @delivery_request_id
            FOR UPDATE;
            """;
        string? currentPromiseId = null;
        var currentVersion = 0;
        await using (var current = new NpgsqlCommand(currentSql, connection, transaction))
        {
            current.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            current.Parameters.AddWithValue("delivery_request_id", NpgsqlDbType.Text, proposal.DeliveryRequestId);
            await using var reader = await current.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                currentPromiseId = reader.GetString(0);
                currentVersion = reader.GetInt32(1);
            }
        }

        if ((currentPromiseId is null && proposal.Version != 1) ||
            (currentPromiseId is not null &&
             (currentPromiseId != proposal.PromiseId || proposal.Version != currentVersion + 1)))
        {
            await transaction.CommitAsync(cancellationToken);
            return new PromiseCommitWriteResult(
                PromiseCommitWriteOutcome.Conflict,
                proposal.PromiseId,
                proposal.Version,
                null);
        }

        if (currentPromiseId is null)
        {
            const string insertPromiseSql = """
                INSERT INTO promises (
                    promise_id, customer_id, delivery_request_id, current_version, latest_decision_id,
                    committed_delivery_at, outcome, safety_buffer_ticks, safety_buffer_policy_version,
                    policy_versions, reason_codes, updated_at)
                VALUES (
                    @promise_id, @customer_id, @delivery_request_id, @version, @decision_id,
                    @committed_delivery_at, @outcome, @safety_buffer_ticks, @buffer_policy_version,
                    @policy_versions, @reason_codes, @updated_at);
                """;
            await using var insertPromise = new NpgsqlCommand(insertPromiseSql, connection, transaction);
            AddPromiseParameters(insertPromise, proposal, customerId, outcome, policyVersionsJson, reasonCodesJson, now);
            await insertPromise.ExecuteNonQueryAsync(cancellationToken);
        }
        else
        {
            const string updatePromiseSql = """
                UPDATE promises SET
                    current_version = @version,
                    latest_decision_id = @decision_id,
                    committed_delivery_at = @committed_delivery_at,
                    outcome = @outcome,
                    safety_buffer_ticks = @safety_buffer_ticks,
                    safety_buffer_policy_version = @buffer_policy_version,
                    policy_versions = @policy_versions,
                    reason_codes = @reason_codes,
                    updated_at = @updated_at
                WHERE customer_id = @customer_id AND delivery_request_id = @delivery_request_id;
                """;
            await using var updatePromise = new NpgsqlCommand(updatePromiseSql, connection, transaction);
            AddPromiseParameters(updatePromise, proposal, customerId, outcome, policyVersionsJson, reasonCodesJson, now);
            await updatePromise.ExecuteNonQueryAsync(cancellationToken);
        }

        const string insertVersionSql = """
            INSERT INTO promise_versions (
                promise_id, customer_id, version, decision_id, proposal_hash, proposal, event_id, created_at)
            VALUES (@promise_id, @customer_id, @version, @decision_id, @proposal_hash, @proposal, @event_id, @created_at);
            """;
        await using (var insertVersion = new NpgsqlCommand(insertVersionSql, connection, transaction))
        {
            insertVersion.Parameters.AddWithValue("promise_id", NpgsqlDbType.Text, proposal.PromiseId);
            insertVersion.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            insertVersion.Parameters.AddWithValue("version", NpgsqlDbType.Integer, proposal.Version);
            insertVersion.Parameters.AddWithValue("decision_id", NpgsqlDbType.Text, proposal.DecisionId);
            insertVersion.Parameters.AddWithValue("proposal_hash", NpgsqlDbType.Text, proposalHash);
            insertVersion.Parameters.AddWithValue("proposal", NpgsqlDbType.Jsonb, canonicalProposal);
            insertVersion.Parameters.AddWithValue("event_id", NpgsqlDbType.Text, eventId);
            insertVersion.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, now);
            await insertVersion.ExecuteNonQueryAsync(cancellationToken);
        }

        const string insertOutboxSql = """
            INSERT INTO outbox_events (
                event_id, event_type, event_version, occurred_at, producer, correlation_id, causation_id,
                customer_id, aggregate_type, aggregate_id, sequence, data, created_at)
            VALUES (
                @event_id, @event_type, 1, @occurred_at, 'mono-api', @correlation_id, NULL,
                @customer_id, 'promise', @aggregate_id, @sequence, @data, @created_at);
            """;
        await using (var insertEvent = new NpgsqlCommand(insertOutboxSql, connection, transaction))
        {
            insertEvent.Parameters.AddWithValue("event_id", NpgsqlDbType.Text, eventId);
            insertEvent.Parameters.AddWithValue("event_type", NpgsqlDbType.Text, eventType);
            insertEvent.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, now);
            insertEvent.Parameters.AddWithValue("correlation_id", NpgsqlDbType.Text, proposal.DecisionId);
            insertEvent.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            insertEvent.Parameters.AddWithValue("aggregate_id", NpgsqlDbType.Text, proposal.PromiseId);
            insertEvent.Parameters.AddWithValue("sequence", NpgsqlDbType.Bigint, proposal.Version);
            insertEvent.Parameters.AddWithValue("data", NpgsqlDbType.Jsonb, eventData);
            insertEvent.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, now);
            await insertEvent.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new PromiseCommitWriteResult(
            PromiseCommitWriteOutcome.Committed,
            proposal.PromiseId,
            proposal.Version,
            eventId);
    }

    private static string CanonicalProposal(PromiseCommitProposal proposal)
    {
        var payload = new
        {
            promise_id = proposal.PromiseId,
            delivery_request_id = proposal.DeliveryRequestId,
            decision_id = proposal.DecisionId,
            version = proposal.Version,
            evaluated_at = proposal.EvaluatedAt.ToUniversalTime(),
            committed_delivery_at = proposal.CommittedDeliveryAt.ToUniversalTime(),
            outcome = proposal.Outcome.ToString().ToUpperInvariant(),
            safety_buffer_ticks = proposal.SafetyBuffer.Ticks,
            safety_buffer_policy_version = proposal.SafetyBufferPolicyVersion,
            policy_versions = proposal.PolicyVersions.OrderBy(x => x.Key, StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal),
            reason_codes = proposal.ReasonCodes.OrderBy(x => x, StringComparer.Ordinal).ToArray()
        };
        return JsonSerializer.Serialize(payload);
    }

    private static void AddPromiseParameters(
        NpgsqlCommand command,
        PromiseCommitProposal proposal,
        string customerId,
        string outcome,
        string policyVersionsJson,
        string reasonCodesJson,
        DateTimeOffset now)
    {
        command.Parameters.AddWithValue("promise_id", NpgsqlDbType.Text, proposal.PromiseId);
        command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
        command.Parameters.AddWithValue("delivery_request_id", NpgsqlDbType.Text, proposal.DeliveryRequestId);
        command.Parameters.AddWithValue("version", NpgsqlDbType.Integer, proposal.Version);
        command.Parameters.AddWithValue("decision_id", NpgsqlDbType.Text, proposal.DecisionId);
        command.Parameters.AddWithValue("committed_delivery_at", NpgsqlDbType.TimestampTz, proposal.CommittedDeliveryAt.ToUniversalTime());
        command.Parameters.AddWithValue("outcome", NpgsqlDbType.Text, outcome);
        command.Parameters.AddWithValue("safety_buffer_ticks", NpgsqlDbType.Bigint, proposal.SafetyBuffer.Ticks);
        command.Parameters.AddWithValue("buffer_policy_version", NpgsqlDbType.Text, proposal.SafetyBufferPolicyVersion);
        command.Parameters.AddWithValue("policy_versions", NpgsqlDbType.Jsonb, policyVersionsJson);
        command.Parameters.AddWithValue("reason_codes", NpgsqlDbType.Jsonb, reasonCodesJson);
        command.Parameters.AddWithValue("updated_at", NpgsqlDbType.TimestampTz, now);
    }
}
