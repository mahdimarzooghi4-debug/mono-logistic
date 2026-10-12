using System.Text.RegularExpressions;
using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

/// <summary>
/// Read-only inspection for Mono operations staff. Never registered on public HTTP routes.
/// </summary>
public sealed class CustomerServiceConfigurationInspectionService(
    NpgsqlDataSource dataSource,
    IMonoOperationsContext operationsContext)
{
    private static readonly Regex CustomerIdPattern = new(
        @"\Acus_[0-9a-f]{32}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex ServiceCodePattern = new(
        @"\A[A-Z][A-Z0-9_]{0,63}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public async Task<IReadOnlyList<CustomerServiceConfigurationVersionSummary>> ListVersionsAsync(
        string customerId,
        string serviceCode,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        EnsureAdministrator();
        customerId = NormalizeCustomerId(customerId);
        serviceCode = NormalizeServiceCode(serviceCode);
        ValidateLimit(limit);

        const string sql = """
            SELECT service_configuration_id, customer_id, service_code, version, status,
                   effective_from, effective_to, created_at
            FROM customer_service_configurations
            WHERE customer_id = @customer_id AND service_code = @service_code
            ORDER BY version DESC
            LIMIT @limit;
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
        command.Parameters.AddWithValue("service_code", NpgsqlDbType.Text, serviceCode);
        command.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<CustomerServiceConfigurationVersionSummary>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new CustomerServiceConfigurationVersionSummary(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3),
                reader.GetString(4), reader.GetFieldValue<DateTimeOffset>(5),
                reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
                reader.GetFieldValue<DateTimeOffset>(7)));
        }
        return rows;
    }

    public async Task<IReadOnlyList<CustomerServiceConfigurationAuditEntry>> ListAuditHistoryAsync(
        string customerId,
        string serviceCode,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        EnsureAdministrator();
        customerId = NormalizeCustomerId(customerId);
        serviceCode = NormalizeServiceCode(serviceCode);
        ValidateLimit(limit);

        const string sql = """
            SELECT audit_id, service_configuration_id, customer_id, service_code, version,
                   action, actor_id, reason, occurred_at
            FROM customer_service_configuration_audit
            WHERE customer_id = @customer_id AND service_code = @service_code
            ORDER BY occurred_at DESC, audit_id DESC
            LIMIT @limit;
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
        command.Parameters.AddWithValue("service_code", NpgsqlDbType.Text, serviceCode);
        command.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<CustomerServiceConfigurationAuditEntry>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new CustomerServiceConfigurationAuditEntry(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetInt32(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
                reader.GetFieldValue<DateTimeOffset>(8)));
        }
        return rows;
    }

    private void EnsureAdministrator()
    {
        if (string.IsNullOrWhiteSpace(operationsContext.ActorId) ||
            !operationsContext.Roles.Contains(MonoOperationsRoles.Administrator, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("An authenticated Mono administrator is required.");
    }

    private static string NormalizeCustomerId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        value = value.Trim();
        if (!CustomerIdPattern.IsMatch(value))
            throw new ArgumentException("Customer ID is invalid.", nameof(value));
        return value;
    }

    private static string NormalizeServiceCode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        value = value.Trim().ToUpperInvariant();
        if (!ServiceCodePattern.IsMatch(value))
            throw new ArgumentException("Service code is invalid.", nameof(value));
        return value;
    }

    private static void ValidateLimit(int limit)
    {
        if (limit is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be from 1 to 100.");
    }
}

public sealed record CustomerServiceConfigurationVersionSummary(
    string ServiceConfigurationId,
    string CustomerId,
    string ServiceCode,
    int Version,
    string Status,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    DateTimeOffset CreatedAt);

public sealed record CustomerServiceConfigurationAuditEntry(
    string AuditId,
    string ServiceConfigurationId,
    string CustomerId,
    string ServiceCode,
    int Version,
    string Action,
    string ActorId,
    string Reason,
    DateTimeOffset OccurredAt);
