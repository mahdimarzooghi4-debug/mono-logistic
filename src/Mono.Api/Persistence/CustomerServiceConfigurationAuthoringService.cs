using System.Text.Json;
using System.Text.RegularExpressions;
using Mono.Api.Domain;
using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

/// <summary>
/// Internal application service for versioned customer service configuration.
/// It is deliberately not exposed by the public API.
/// </summary>
public sealed class CustomerServiceConfigurationAuthoringService(
    NpgsqlDataSource dataSource,
    IMonoOperationsContext operationsContext)
{
    private static readonly Regex CustomerIdPattern = new(
        @"\Acus_[0-9a-f]{32}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex ServiceCodePattern = new(
        @"\A[A-Z][A-Z0-9_]{0,63}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public async Task<CustomerServiceConfiguration> CreateDraftAsync(
        string customerId,
        string serviceCode,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveTo,
        JsonElement configuration,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var actorId = RequireAdministrator();
        customerId = NormalizeCustomerId(customerId);
        serviceCode = NormalizeServiceCode(serviceCode);
        ValidateEffectiveRange(effectiveFrom, effectiveTo);
        ValidateReason(reason);
        if (configuration.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Configuration must be a JSON object.", nameof(configuration));

        var now = DateTimeOffset.UtcNow;
        var config = configuration.Clone();
        var configurationId = "scf_" + Guid.NewGuid().ToString("N");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await LockServiceAsync(connection, transaction, customerId, serviceCode, cancellationToken);
        const string nextVersionSql = """
            SELECT COALESCE(MAX(version), 0) + 1
            FROM customer_service_configurations
            WHERE customer_id = @customer_id AND service_code = @service_code;
            """;
        await using var versionCommand = new NpgsqlCommand(nextVersionSql, connection, transaction);
        versionCommand.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
        versionCommand.Parameters.AddWithValue("service_code", NpgsqlDbType.Text, serviceCode);
        var version = Convert.ToInt32(await versionCommand.ExecuteScalarAsync(cancellationToken));

        const string insertSql = """
            INSERT INTO customer_service_configurations (
                service_configuration_id, customer_id, service_code, version, status,
                effective_from, effective_to, configuration, created_at)
            VALUES (
                @configuration_id, @customer_id, @service_code, @version, 'DRAFT',
                @effective_from, @effective_to, @configuration, @created_at);
            """;
        await using (var insert = new NpgsqlCommand(insertSql, connection, transaction))
        {
            insert.Parameters.AddWithValue("configuration_id", NpgsqlDbType.Text, configurationId);
            insert.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            insert.Parameters.AddWithValue("service_code", NpgsqlDbType.Text, serviceCode);
            insert.Parameters.AddWithValue("version", NpgsqlDbType.Integer, version);
            insert.Parameters.AddWithValue("effective_from", NpgsqlDbType.TimestampTz, effectiveFrom);
            insert.Parameters.AddWithValue("effective_to", NpgsqlDbType.TimestampTz, (object?)effectiveTo ?? DBNull.Value);
            insert.Parameters.AddWithValue("configuration", NpgsqlDbType.Jsonb, config.GetRawText());
            insert.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, now);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await InsertAuditAsync(connection, transaction, configurationId, customerId, serviceCode,
            version, "DRAFT_CREATED", actorId, reason, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new CustomerServiceConfiguration(configurationId, customerId, serviceCode, version,
            "DRAFT", effectiveFrom, effectiveTo, config, now);
    }

    public async Task ActivateDraftAsync(
        string customerId,
        string serviceConfigurationId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var actorId = RequireAdministrator();
        customerId = NormalizeCustomerId(customerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceConfigurationId);
        ValidateReason(reason);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        const string selectSql = """
            SELECT service_code, version, status, effective_from, effective_to
            FROM customer_service_configurations
            WHERE customer_id = @customer_id AND service_configuration_id = @configuration_id
            FOR UPDATE;
            """;
        string serviceCode;
        int version;
        string status;
        DateTimeOffset effectiveFrom;
        DateTimeOffset? effectiveTo;
        await using (var select = new NpgsqlCommand(selectSql, connection, transaction))
        {
            select.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            select.Parameters.AddWithValue("configuration_id", NpgsqlDbType.Text, serviceConfigurationId);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new KeyNotFoundException("Draft service configuration was not found for this customer.");
            serviceCode = reader.GetString(0);
            version = reader.GetInt32(1);
            status = reader.GetString(2);
            effectiveFrom = reader.GetFieldValue<DateTimeOffset>(3);
            effectiveTo = reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4);
        }

        if (!string.Equals(status, "DRAFT", StringComparison.Ordinal))
            throw new InvalidOperationException("Only a Draft service configuration can be activated.");

        await LockServiceAsync(connection, transaction, customerId, serviceCode, cancellationToken);
        const string overlapSql = """
            SELECT EXISTS (
                SELECT 1
                FROM customer_service_configurations
                WHERE customer_id = @customer_id
                  AND service_code = @service_code
                  AND status = 'ACTIVE'
                  AND service_configuration_id <> @configuration_id
                  AND tstzrange(effective_from, COALESCE(effective_to, 'infinity'::timestamptz), '[)')
                      && tstzrange(@effective_from, COALESCE(@effective_to, 'infinity'::timestamptz), '[)')
            );
            """;
        await using (var overlap = new NpgsqlCommand(overlapSql, connection, transaction))
        {
            overlap.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            overlap.Parameters.AddWithValue("service_code", NpgsqlDbType.Text, serviceCode);
            overlap.Parameters.AddWithValue("configuration_id", NpgsqlDbType.Text, serviceConfigurationId);
            overlap.Parameters.AddWithValue("effective_from", NpgsqlDbType.TimestampTz, effectiveFrom);
            overlap.Parameters.AddWithValue("effective_to", NpgsqlDbType.TimestampTz, (object?)effectiveTo ?? DBNull.Value);
            if (Convert.ToBoolean(await overlap.ExecuteScalarAsync(cancellationToken)))
                throw new ConfigurationActivationConflictException(serviceCode, customerId);
        }

        const string updateSql = """
            UPDATE customer_service_configurations SET status = 'ACTIVE'
            WHERE customer_id = @customer_id
              AND service_configuration_id = @configuration_id AND status = 'DRAFT';
            """;
        await using (var update = new NpgsqlCommand(updateSql, connection, transaction))
        {
            update.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            update.Parameters.AddWithValue("configuration_id", NpgsqlDbType.Text, serviceConfigurationId);
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("Draft service configuration changed before activation.");
        }

        await InsertAuditAsync(connection, transaction, serviceConfigurationId, customerId, serviceCode,
            version, "ACTIVATED", actorId, reason, DateTimeOffset.UtcNow, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private string RequireAdministrator()
    {
        if (string.IsNullOrWhiteSpace(operationsContext.ActorId) ||
            !operationsContext.Roles.Contains(MonoOperationsRoles.Administrator, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("An authenticated Mono administrator is required.");
        return operationsContext.ActorId;
    }

    private static string NormalizeCustomerId(string customerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        var normalized = customerId.Trim();
        if (!CustomerIdPattern.IsMatch(normalized))
            throw new ArgumentException("Customer ID is invalid.", nameof(customerId));
        return normalized;
    }

    private static string NormalizeServiceCode(string serviceCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceCode);
        var normalized = serviceCode.Trim().ToUpperInvariant();
        if (!ServiceCodePattern.IsMatch(normalized))
            throw new ArgumentException("Service code is invalid.", nameof(serviceCode));
        return normalized;
    }

    private static void ValidateEffectiveRange(DateTimeOffset start, DateTimeOffset? end)
    {
        if (end.HasValue && end.Value <= start)
            throw new ArgumentException("Effective end must be later than effective start.", nameof(end));
    }

    private static void ValidateReason(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (reason.Trim().Length > 1000)
            throw new ArgumentException("Reason must be at most 1000 characters.", nameof(reason));
    }

    private static async Task LockServiceAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction,
        string customerId, string serviceCode, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended(@lock_key, 0));", connection, transaction);
        command.Parameters.AddWithValue("lock_key", NpgsqlDbType.Text, customerId + "/" + serviceCode);
        await command.ExecuteScalarAsync(cancellationToken);
    }

    private static async Task InsertAuditAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction,
        string configurationId, string customerId, string serviceCode, int version,
        string action, string actorId, string reason, DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO customer_service_configuration_audit (
                audit_id, service_configuration_id, customer_id, service_code, version,
                action, actor_id, reason, occurred_at)
            VALUES (
                @audit_id, @configuration_id, @customer_id, @service_code, @version,
                @action, @actor_id, @reason, @occurred_at);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("audit_id", NpgsqlDbType.Text, "sca_" + Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("configuration_id", NpgsqlDbType.Text, configurationId);
        command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
        command.Parameters.AddWithValue("service_code", NpgsqlDbType.Text, serviceCode);
        command.Parameters.AddWithValue("version", NpgsqlDbType.Integer, version);
        command.Parameters.AddWithValue("action", NpgsqlDbType.Text, action);
        command.Parameters.AddWithValue("actor_id", NpgsqlDbType.Text, actorId);
        command.Parameters.AddWithValue("reason", NpgsqlDbType.Text, reason.Trim());
        command.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, at);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

public sealed class ConfigurationActivationConflictException(string serviceCode, string customerId)
    : InvalidOperationException(
        "An active configuration overlaps the effective period for customer " + customerId +
        " and service " + serviceCode + ".");
