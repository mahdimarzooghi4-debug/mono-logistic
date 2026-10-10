using System.Text.Json;
using System.Text.RegularExpressions;
using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

/// <summary>Mono administrator authoring and inspection for immutable policy draft versions.</summary>
public sealed class MonoPolicyDraftOperationsService(
    NpgsqlDataSource dataSource,
    IMonoOperationsContext operationsContext)
{
    private static readonly Regex PolicyIdPattern = new(
        @"\Apol_[0-9a-f]{32}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly HashSet<string> Scopes = new(StringComparer.Ordinal)
    {
        "PLATFORM", "SERVICE", "CUSTOMER", "BRANCH", "ZONE", "CONTRACT", "MISSION", "RUNTIME"
    };
    private static readonly HashSet<string> EnforcementTypes = new(StringComparer.Ordinal)
    {
        "HARD", "OPTIMIZATION", "COMMERCIAL", "CONFIGURATION",
        "CONTRACTUAL", "WORKFLOW", "FINANCIAL", "RISK"
    };

    public async Task<PolicyVersionSummary> CreateAsync(
        CreatePolicyDraftInput input,
        CancellationToken cancellationToken = default)
    {
        var actorId = RequireAdministrator();
        var policyType = NormalizePolicyType(input.PolicyType);
        var scope = NormalizeChoice(input.Scope, Scopes, "Policy scope is invalid.");
        var scopeId = NormalizeScopeId(scope, input.ScopeId);
        var version = NormalizeVersion(input.EnforcementType, input.Priority,
            input.EffectiveFrom, input.EffectiveTo, input.Definition, input.Reason);
        var now = DateTimeOffset.UtcNow;
        var policyId = "pol_" + Guid.NewGuid().ToString("N");

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var policy = new NpgsqlCommand("""
            INSERT INTO policies (policy_id, policy_type, scope, scope_id, created_at, created_by)
            VALUES (@policy_id, @policy_type, @scope, @scope_id, @created_at, @created_by);
            """, connection, transaction))
        {
            policy.Parameters.AddWithValue("policy_id", NpgsqlDbType.Text, policyId);
            policy.Parameters.AddWithValue("policy_type", NpgsqlDbType.Text, policyType);
            policy.Parameters.AddWithValue("scope", NpgsqlDbType.Text, scope);
            policy.Parameters.AddWithValue("scope_id", NpgsqlDbType.Text, (object?)scopeId ?? DBNull.Value);
            policy.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, now);
            policy.Parameters.AddWithValue("created_by", NpgsqlDbType.Text, actorId);
            await policy.ExecuteNonQueryAsync(cancellationToken);
        }

        await InsertVersionAsync(connection, transaction, policyId, 1, version,
            actorId, now, cancellationToken);
        await InsertAuditAsync(connection, transaction, policyId, 1, "POLICY_CREATED",
            actorId, version.Reason, policyType, scope, scopeId, version, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToSummary(policyId, 1, version, actorId, now);
    }

    public async Task<PolicyVersionSummary> CreateVersionAsync(
        string policyId,
        CreatePolicyVersionInput input,
        CancellationToken cancellationToken = default)
    {
        var actorId = RequireAdministrator();
        policyId = NormalizePolicyId(policyId);
        var version = NormalizeVersion(input.EnforcementType, input.Priority,
            input.EffectiveFrom, input.EffectiveTo, input.Definition, input.Reason);
        var now = DateTimeOffset.UtcNow;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        string policyType;
        string scope;
        string? scopeId;
        await using (var policy = new NpgsqlCommand("""
            SELECT policy_type, scope, scope_id
            FROM policies WHERE policy_id = @policy_id
            FOR UPDATE;
            """, connection, transaction))
        {
            policy.Parameters.AddWithValue("policy_id", NpgsqlDbType.Text, policyId);
            await using var reader = await policy.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new KeyNotFoundException("Policy was not found.");
            policyType = reader.GetString(0);
            scope = reader.GetString(1);
            scopeId = reader.IsDBNull(2) ? null : reader.GetString(2);
        }

        int versionNumber;
        await using (var latest = new NpgsqlCommand("""
            SELECT COALESCE(MAX(version_number), 0) + 1
            FROM policy_versions WHERE policy_id = @policy_id;
            """, connection, transaction))
        {
            latest.Parameters.AddWithValue("policy_id", NpgsqlDbType.Text, policyId);
            versionNumber = (int)(await latest.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException("Could not allocate a policy version."));
        }

        await InsertVersionAsync(connection, transaction, policyId, versionNumber, version,
            actorId, now, cancellationToken);
        await InsertAuditAsync(connection, transaction, policyId, versionNumber, "VERSION_DRAFTED",
            actorId, version.Reason, policyType, scope, scopeId, version, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToSummary(policyId, versionNumber, version, actorId, now);
    }


    public async Task<PolicyVersionSummary> ActivateAsync(
        string policyId, int versionNumber, string reason,
        CancellationToken cancellationToken = default)
    {
        var actorId = RequireAdministrator();
        policyId = NormalizePolicyId(policyId);
        if (versionNumber < 1) throw new ArgumentOutOfRangeException(nameof(versionNumber));
        reason = NormalizeLifecycleReason(reason);
        var now = DateTimeOffset.UtcNow;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var identity = await LockPolicyAsync(connection, transaction, policyId, cancellationToken);
        var requested = await ReadVersionForUpdateAsync(connection, transaction, policyId, versionNumber, cancellationToken)
            ?? throw new PolicyVersionNotFoundException();
        if (requested.Status != "DRAFT")
            throw new PolicyVersionStateConflictException("Only a Draft policy version can be activated.");
        if (requested.EffectiveTo is not null && requested.EffectiveTo <= now)
            throw new PolicyVersionStateConflictException("An expired policy version cannot be activated.");

        var active = await ReadActiveVersionForUpdateAsync(connection, transaction, policyId, cancellationToken);
        if (active is not null)
        {
            await UpdateVersionStatusAsync(connection, transaction, policyId, active.VersionNumber, "INACTIVE", cancellationToken);
            await InsertLifecycleAuditAsync(connection, transaction, identity, active with { Status = "INACTIVE" },
                "VERSION_DEACTIVATED", actorId, reason, now, cancellationToken);
        }
        await UpdateVersionStatusAsync(connection, transaction, policyId, versionNumber, "ACTIVE", cancellationToken);
        var activated = requested with { Status = "ACTIVE" };
        await InsertLifecycleAuditAsync(connection, transaction, identity, activated,
            "VERSION_ACTIVATED", actorId, reason, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return activated;
    }

    public async Task<PolicyVersionSummary> RetireAsync(
        string policyId, int versionNumber, string reason,
        CancellationToken cancellationToken = default)
    {
        var actorId = RequireAdministrator();
        policyId = NormalizePolicyId(policyId);
        if (versionNumber < 1) throw new ArgumentOutOfRangeException(nameof(versionNumber));
        reason = NormalizeLifecycleReason(reason);
        var now = DateTimeOffset.UtcNow;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var identity = await LockPolicyAsync(connection, transaction, policyId, cancellationToken);
        var version = await ReadVersionForUpdateAsync(connection, transaction, policyId, versionNumber, cancellationToken)
            ?? throw new PolicyVersionNotFoundException();
        if (version.Status is not ("ACTIVE" or "INACTIVE"))
            throw new PolicyVersionStateConflictException("Only an Active or Inactive policy version can be retired.");

        await UpdateVersionStatusAsync(connection, transaction, policyId, versionNumber, "RETIRED", cancellationToken);
        var retired = version with { Status = "RETIRED" };
        await InsertLifecycleAuditAsync(connection, transaction, identity, retired,
            "VERSION_RETIRED", actorId, reason, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return retired;
    }

    private static async Task<PolicyIdentity> LockPolicyAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string policyId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT policy_type, scope, scope_id FROM policies WHERE policy_id = @policy_id FOR UPDATE;
            """, connection, transaction);
        command.Parameters.AddWithValue("policy_id", NpgsqlDbType.Text, policyId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new KeyNotFoundException("Policy was not found.");
        return new PolicyIdentity(policyId, reader.GetString(0), reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2));
    }

    private static async Task<PolicyVersionSummary?> ReadVersionForUpdateAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string policyId, int versionNumber,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT version_number, enforcement_type, priority, status, effective_from, effective_to,
                   definition::text, created_at, created_by
            FROM policy_versions WHERE policy_id = @policy_id AND version_number = @version_number FOR UPDATE;
            """, connection, transaction);
        command.Parameters.AddWithValue("policy_id", NpgsqlDbType.Text, policyId);
        command.Parameters.AddWithValue("version_number", NpgsqlDbType.Integer, versionNumber);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadVersion(policyId, reader) : null;
    }

    private static async Task<PolicyVersionSummary?> ReadActiveVersionForUpdateAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string policyId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT version_number, enforcement_type, priority, status, effective_from, effective_to,
                   definition::text, created_at, created_by
            FROM policy_versions WHERE policy_id = @policy_id AND status = 'ACTIVE' FOR UPDATE;
            """, connection, transaction);
        command.Parameters.AddWithValue("policy_id", NpgsqlDbType.Text, policyId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadVersion(policyId, reader) : null;
    }

    private static async Task UpdateVersionStatusAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string policyId, int versionNumber,
        string status, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            UPDATE policy_versions SET status = @status WHERE policy_id = @policy_id AND version_number = @version_number;
            """, connection, transaction);
        command.Parameters.AddWithValue("policy_id", NpgsqlDbType.Text, policyId);
        command.Parameters.AddWithValue("version_number", NpgsqlDbType.Integer, versionNumber);
        command.Parameters.AddWithValue("status", NpgsqlDbType.Text, status);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertLifecycleAuditAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, PolicyIdentity identity,
        PolicyVersionSummary version, string action, string actorId, string reason, DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var snapshot = JsonSerializer.Serialize(new
        {
            policyId = identity.PolicyId, policyType = identity.PolicyType, scope = identity.Scope,
            scopeId = identity.ScopeId, versionNumber = version.VersionNumber,
            enforcementType = version.EnforcementType, version.Priority, status = version.Status,
            version.EffectiveFrom, version.EffectiveTo, version.Definition
        });
        await using var command = new NpgsqlCommand("""
            INSERT INTO policy_audit (
                audit_id, policy_id, version_number, action, actor_id, reason, policy_snapshot, occurred_at)
            VALUES (@audit_id, @policy_id, @version_number, @action, @actor_id, @reason, @snapshot, @occurred_at);
            """, connection, transaction);
        command.Parameters.AddWithValue("audit_id", NpgsqlDbType.Text, "pa_" + Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("policy_id", NpgsqlDbType.Text, identity.PolicyId);
        command.Parameters.AddWithValue("version_number", NpgsqlDbType.Integer, version.VersionNumber);
        command.Parameters.AddWithValue("action", NpgsqlDbType.Text, action);
        command.Parameters.AddWithValue("actor_id", NpgsqlDbType.Text, actorId);
        command.Parameters.AddWithValue("reason", NpgsqlDbType.Text, reason);
        command.Parameters.AddWithValue("snapshot", NpgsqlDbType.Jsonb, snapshot);
        command.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, occurredAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<PolicyListPageResponse> ListAsync(
        int pageSize,
        string? cursor,
        CancellationToken cancellationToken = default)
    {
        RequireAdministrator();
        if (pageSize is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(pageSize), "Page size must be between 1 and 100.");
        if (cursor is not null)
            cursor = NormalizePolicyId(cursor);

        const string sql = """
            SELECT p.policy_id, p.policy_type, p.scope, p.scope_id, p.created_at, p.created_by,
                   latest.version_number, latest.status
            FROM policies p
            JOIN LATERAL (
                SELECT version_number, status
                FROM policy_versions v
                WHERE v.policy_id = p.policy_id
                ORDER BY version_number DESC
                LIMIT 1
            ) latest ON TRUE
            WHERE @cursor IS NULL OR p.policy_id > @cursor
            ORDER BY p.policy_id
            LIMIT @take;
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("cursor", NpgsqlDbType.Text, (object?)cursor ?? DBNull.Value);
        command.Parameters.AddWithValue("take", NpgsqlDbType.Integer, pageSize + 1);
        var items = new List<PolicySummary>(pageSize + 1);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                items.Add(new PolicySummary(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetFieldValue<DateTimeOffset>(4), reader.GetString(5),
                    reader.GetInt32(6), reader.GetString(7)));
        }

        string? nextCursor = null;
        if (items.Count > pageSize)
        {
            items.RemoveAt(items.Count - 1);
            nextCursor = items[^1].PolicyId;
        }
        return new PolicyListPageResponse(items, nextCursor);
    }

    public async Task<PolicyDetailResponse?> GetAsync(
        string policyId,
        CancellationToken cancellationToken = default)
    {
        RequireAdministrator();
        policyId = NormalizePolicyId(policyId);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        string policyType;
        string scope;
        string? scopeId;
        DateTimeOffset createdAt;
        string createdBy;
        await using (var command = new NpgsqlCommand("""
            SELECT policy_type, scope, scope_id, created_at, created_by
            FROM policies WHERE policy_id = @policy_id;
            """, connection))
        {
            command.Parameters.AddWithValue("policy_id", NpgsqlDbType.Text, policyId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return null;
            policyType = reader.GetString(0);
            scope = reader.GetString(1);
            scopeId = reader.IsDBNull(2) ? null : reader.GetString(2);
            createdAt = reader.GetFieldValue<DateTimeOffset>(3);
            createdBy = reader.GetString(4);
        }

        var versions = new List<PolicyVersionSummary>();
        await using (var command = new NpgsqlCommand("""
            SELECT version_number, enforcement_type, priority, status, effective_from, effective_to,
                   definition::text, created_at, created_by
            FROM policy_versions WHERE policy_id = @policy_id
            ORDER BY version_number;
            """, connection))
        {
            command.Parameters.AddWithValue("policy_id", NpgsqlDbType.Text, policyId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                versions.Add(ReadVersion(policyId, reader));
        }
        return new PolicyDetailResponse(
            policyId, policyType, scope, scopeId, createdAt, createdBy, versions);
    }

    public async Task<IReadOnlyList<PolicyAuditSummary>?> ListAuditAsync(
        string policyId,
        CancellationToken cancellationToken = default)
    {
        RequireAdministrator();
        policyId = NormalizePolicyId(policyId);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using (var exists = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM policies WHERE policy_id = @policy_id);", connection))
        {
            exists.Parameters.AddWithValue("policy_id", NpgsqlDbType.Text, policyId);
            if (!Equals(await exists.ExecuteScalarAsync(cancellationToken), true))
                return null;
        }
        await using var command = new NpgsqlCommand("""
            SELECT audit_id, version_number, action, actor_id, reason,
                   policy_snapshot::text, occurred_at
            FROM policy_audit
            WHERE policy_id = @policy_id
            ORDER BY occurred_at, audit_id;
            """, connection);
        command.Parameters.AddWithValue("policy_id", NpgsqlDbType.Text, policyId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var entries = new List<PolicyAuditSummary>();
        while (await reader.ReadAsync(cancellationToken))
            entries.Add(new PolicyAuditSummary(
                reader.GetString(0), reader.GetInt32(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4),
                ParseJsonElement(reader.GetString(5)),
                reader.GetFieldValue<DateTimeOffset>(6)));
        return entries;
    }

    private async Task InsertVersionAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string policyId,
        int versionNumber, NormalizedPolicyDraft version, string actorId,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO policy_versions (
                policy_id, version_number, enforcement_type, priority, status,
                effective_from, effective_to, definition, created_at, created_by)
            VALUES (
                @policy_id, @version_number, @enforcement_type, @priority, 'DRAFT',
                @effective_from, @effective_to, @definition, @created_at, @created_by);
            """, connection, transaction);
        command.Parameters.AddWithValue("policy_id", NpgsqlDbType.Text, policyId);
        command.Parameters.AddWithValue("version_number", NpgsqlDbType.Integer, versionNumber);
        command.Parameters.AddWithValue("enforcement_type", NpgsqlDbType.Text, version.EnforcementType);
        command.Parameters.AddWithValue("priority", NpgsqlDbType.Integer, version.Priority);
        command.Parameters.AddWithValue("effective_from", NpgsqlDbType.TimestampTz,
            (object?)version.EffectiveFrom ?? DBNull.Value);
        command.Parameters.AddWithValue("effective_to", NpgsqlDbType.TimestampTz,
            (object?)version.EffectiveTo ?? DBNull.Value);
        command.Parameters.AddWithValue("definition", NpgsqlDbType.Jsonb, version.Definition.GetRawText());
        command.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, now);
        command.Parameters.AddWithValue("created_by", NpgsqlDbType.Text, actorId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task InsertAuditAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string policyId,
        int versionNumber, string action, string actorId, string reason, string policyType,
        string scope, string? scopeId, NormalizedPolicyDraft version,
        DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        var snapshot = JsonSerializer.Serialize(new
        {
            policyId, policyType, scope, scopeId, versionNumber,
            version.EnforcementType, version.Priority, status = "DRAFT",
            version.EffectiveFrom, version.EffectiveTo, version.Definition
        });
        await using var command = new NpgsqlCommand("""
            INSERT INTO policy_audit (
                audit_id, policy_id, version_number, action, actor_id, reason,
                policy_snapshot, occurred_at)
            VALUES (@audit_id, @policy_id, @version_number, @action, @actor_id,
                    @reason, @snapshot, @occurred_at);
            """, connection, transaction);
        command.Parameters.AddWithValue("audit_id", NpgsqlDbType.Text, "pa_" + Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("policy_id", NpgsqlDbType.Text, policyId);
        command.Parameters.AddWithValue("version_number", NpgsqlDbType.Integer, versionNumber);
        command.Parameters.AddWithValue("action", NpgsqlDbType.Text, action);
        command.Parameters.AddWithValue("actor_id", NpgsqlDbType.Text, actorId);
        command.Parameters.AddWithValue("reason", NpgsqlDbType.Text, reason);
        command.Parameters.AddWithValue("snapshot", NpgsqlDbType.Jsonb, snapshot);
        command.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, occurredAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static JsonElement ParseJsonElement(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static PolicyVersionSummary ReadVersion(string policyId, NpgsqlDataReader reader) =>
        new(policyId, reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4),
            reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
            ParseJsonElement(reader.GetString(6)),
            reader.GetFieldValue<DateTimeOffset>(7), reader.GetString(8));

    private static PolicyVersionSummary ToSummary(
        string policyId, int number, NormalizedPolicyDraft version, string actorId, DateTimeOffset now) =>
        new(policyId, number, version.EnforcementType, version.Priority, "DRAFT",
            version.EffectiveFrom, version.EffectiveTo, version.Definition, now, actorId);

    private string RequireAdministrator()
    {
        if (string.IsNullOrWhiteSpace(operationsContext.ActorId) ||
            !operationsContext.Roles.Contains(MonoOperationsRoles.Administrator, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("An authenticated Mono administrator is required.");
        return operationsContext.ActorId;
    }

    private static NormalizedPolicyDraft NormalizeVersion(
        string enforcementType, int priority, DateTimeOffset? effectiveFrom,
        DateTimeOffset? effectiveTo, JsonElement definition, string reason)
    {
        var normalizedEnforcement = NormalizeChoice(enforcementType, EnforcementTypes,
            "Policy enforcement type is invalid.");
        if (effectiveFrom is not null && effectiveTo is not null && effectiveTo <= effectiveFrom)
            throw new ArgumentException("Effective end must be later than effective start.");
        if (definition.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Policy definition must be a JSON object.");
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var normalizedReason = reason.Trim();
        if (normalizedReason.Length > 1000)
            throw new ArgumentException("Reason must be at most 1000 characters.");
        return new NormalizedPolicyDraft(
            normalizedEnforcement, priority, effectiveFrom, effectiveTo, definition.Clone(), normalizedReason);
    }

    private static string NormalizePolicyType(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToUpperInvariant();
        if (!Regex.IsMatch(normalized, @"\A[A-Z][A-Z0-9_]{0,63}\z", RegexOptions.CultureInvariant))
            throw new ArgumentException("Policy type must be a 1–64 character uppercase identifier.");
        return normalized;
    }

    private static string NormalizeScopeId(string scope, string? value)
    {
        if (scope == "PLATFORM")
        {
            if (!string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Platform scope must not include a scope ID.");
            return null!;
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim();
        if (normalized.Length > 128)
            throw new ArgumentException("Scope ID must be at most 128 characters.");
        return normalized;
    }

    private static string NormalizeLifecycleReason(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim();
        if (normalized.Length > 1000) throw new ArgumentException("Reason must be at most 1000 characters.");
        return normalized;
    }

    private static string NormalizePolicyId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim();
        if (!PolicyIdPattern.IsMatch(normalized))
            throw new ArgumentException("Policy ID is invalid.");
        return normalized;
    }

    private static string NormalizeChoice(string value, HashSet<string> allowed, string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToUpperInvariant();
        if (!allowed.Contains(normalized))
            throw new ArgumentException(error);
        return normalized;
    }
}

public sealed record PolicyIdentity(string PolicyId, string PolicyType, string Scope, string? ScopeId);
public sealed class PolicyVersionNotFoundException : Exception { }
public sealed class PolicyVersionStateConflictException(string message) : Exception(message);

public sealed record CreatePolicyDraftInput(
    string PolicyType, string Scope, string? ScopeId, string EnforcementType, int Priority,
    DateTimeOffset? EffectiveFrom, DateTimeOffset? EffectiveTo, JsonElement Definition, string Reason);

public sealed record CreatePolicyVersionInput(
    string EnforcementType, int Priority, DateTimeOffset? EffectiveFrom,
    DateTimeOffset? EffectiveTo, JsonElement Definition, string Reason);

public sealed record NormalizedPolicyDraft(
    string EnforcementType, int Priority, DateTimeOffset? EffectiveFrom,
    DateTimeOffset? EffectiveTo, JsonElement Definition, string Reason);

public sealed record PolicyVersionSummary(
    string PolicyId, int VersionNumber, string EnforcementType, int Priority, string Status,
    DateTimeOffset? EffectiveFrom, DateTimeOffset? EffectiveTo, JsonElement Definition,
    DateTimeOffset CreatedAt, string CreatedBy);

public sealed record PolicySummary(
    string PolicyId, string PolicyType, string Scope, string? ScopeId,
    DateTimeOffset CreatedAt, string CreatedBy, int LatestVersion, string LatestStatus);

public sealed record PolicyListPageResponse(
    IReadOnlyList<PolicySummary> Policies, string? NextCursor);

public sealed record PolicyDetailResponse(
    string PolicyId, string PolicyType, string Scope, string? ScopeId,
    DateTimeOffset CreatedAt, string CreatedBy, IReadOnlyList<PolicyVersionSummary> Versions);

public sealed record PolicyAuditSummary(
    string AuditId, int VersionNumber, string Action, string ActorId, string Reason,
    JsonElement PolicySnapshot, DateTimeOffset OccurredAt);
