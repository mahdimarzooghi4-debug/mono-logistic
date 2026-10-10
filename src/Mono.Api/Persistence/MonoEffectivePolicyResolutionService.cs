using System.Text.Json;
using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

/// <summary>Resolves active, effective policy versions for an explicit decision context.</summary>
public sealed class MonoEffectivePolicyResolutionService(
    NpgsqlDataSource dataSource,
    IMonoOperationsContext operationsContext)
{
    public async Task<EffectivePolicyResolutionResponse> ResolveAsync(
        EffectivePolicyContext context,
        CancellationToken cancellationToken = default)
    {
        RequireAdministrator();
        var serviceId = NormalizeId(context.ServiceId, "Service ID");
        var customerId = NormalizeId(context.CustomerId, "Customer ID");
        var contractId = NormalizeId(context.ContractId, "Contract ID");
        var branchId = NormalizeId(context.BranchId, "Branch ID");
        var missionId = NormalizeId(context.MissionId, "Mission ID");
        var runtimeId = NormalizeId(context.RuntimeId, "Runtime ID");
        var zoneIds = NormalizeZoneIds(context.ZoneIds);

        const string sql = """
            SELECT p.policy_id, p.policy_type, p.scope, p.scope_id,
                   v.version_number, v.enforcement_type, v.priority,
                   v.effective_from, v.effective_to, v.definition::text,
                   CASE p.scope
                       WHEN 'PLATFORM' THEN 0
                       WHEN 'SERVICE' THEN 1
                       WHEN 'CUSTOMER' THEN 2
                       WHEN 'CONTRACT' THEN 2
                       WHEN 'BRANCH' THEN 3
                       WHEN 'ZONE' THEN 3
                       WHEN 'MISSION' THEN 4
                       WHEN 'RUNTIME' THEN 4
                       ELSE 99
                   END AS precedence_tier
            FROM policies p
            JOIN policy_versions v ON v.policy_id = p.policy_id
            WHERE v.status = 'ACTIVE'
              AND (v.effective_from IS NULL OR v.effective_from <= @requested_at)
              AND (v.effective_to IS NULL OR v.effective_to > @requested_at)
              AND (
                    p.scope = 'PLATFORM'
                 OR (p.scope = 'SERVICE' AND p.scope_id = @service_id)
                 OR (p.scope = 'CUSTOMER' AND p.scope_id = @customer_id)
                 OR (p.scope = 'CONTRACT' AND p.scope_id = @contract_id)
                 OR (p.scope = 'BRANCH' AND p.scope_id = @branch_id)
                 OR (p.scope = 'ZONE' AND p.scope_id = ANY(@zone_ids))
                 OR (p.scope = 'MISSION' AND p.scope_id = @mission_id)
                 OR (p.scope = 'RUNTIME' AND p.scope_id = @runtime_id)
              )
            ORDER BY precedence_tier, v.priority DESC, p.policy_id;
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("requested_at", NpgsqlDbType.TimestampTz, context.RequestedAt);
        AddOptionalText(command, "service_id", serviceId);
        AddOptionalText(command, "customer_id", customerId);
        AddOptionalText(command, "contract_id", contractId);
        AddOptionalText(command, "branch_id", branchId);
        AddOptionalText(command, "mission_id", missionId);
        AddOptionalText(command, "runtime_id", runtimeId);
        command.Parameters.AddWithValue("zone_ids", NpgsqlDbType.Array | NpgsqlDbType.Text, zoneIds);

        var policies = new List<ResolvedPolicyVersion>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            using var definition = JsonDocument.Parse(reader.GetString(9));
            policies.Add(new ResolvedPolicyVersion(
                reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetInt32(4),
                reader.GetString(5), reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
                reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
                definition.RootElement.Clone(), reader.GetInt32(10)));
        }
        return new EffectivePolicyResolutionResponse(context.RequestedAt, policies);
    }

    private string RequireAdministrator()
    {
        if (string.IsNullOrWhiteSpace(operationsContext.ActorId) ||
            !operationsContext.Roles.Contains(MonoOperationsRoles.Administrator, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("An authenticated Mono administrator is required.");
        return operationsContext.ActorId;
    }

    private static string? NormalizeId(string? value, string label)
    {
        if (value is null) return null;
        ArgumentException.ThrowIfNullOrWhiteSpace(value, label);
        var normalized = value.Trim();
        if (normalized.Length > 128)
            throw new ArgumentException(label + " must be at most 128 characters.");
        return normalized;
    }

    private static string[] NormalizeZoneIds(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0) return [];
        if (values.Count > 20) throw new ArgumentException("At most 20 zone IDs may be supplied.");
        return values.Select(value => NormalizeId(value, "Zone ID")!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static void AddOptionalText(NpgsqlCommand command, string name, string? value) =>
        command.Parameters.AddWithValue(name, NpgsqlDbType.Text, (object?)value ?? DBNull.Value);
}

public sealed record EffectivePolicyContext(
    string? ServiceId,
    string? CustomerId,
    string? ContractId,
    string? BranchId,
    IReadOnlyList<string>? ZoneIds,
    string? MissionId,
    string? RuntimeId,
    DateTimeOffset RequestedAt);

public sealed record EffectivePolicyResolutionResponse(
    DateTimeOffset RequestedAt,
    IReadOnlyList<ResolvedPolicyVersion> Policies);

public sealed record ResolvedPolicyVersion(
    string PolicyId,
    string PolicyType,
    string Scope,
    string? ScopeId,
    int VersionNumber,
    string EnforcementType,
    int Priority,
    DateTimeOffset? EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    JsonElement Definition,
    int PrecedenceTier);
