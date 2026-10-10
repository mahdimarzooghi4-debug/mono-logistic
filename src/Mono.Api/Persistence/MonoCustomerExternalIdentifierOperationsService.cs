using System.Text.RegularExpressions;
using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

/// <summary>Mono administrator operations for customer-owned external identifiers.</summary>
public sealed class MonoCustomerExternalIdentifierOperationsService(
    NpgsqlDataSource dataSource,
    IMonoOperationsContext operationsContext)
{
    private static readonly Regex CustomerIdPattern = new(
        @"\Acus_[0-9a-f]{32}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex NamespacePattern = new(
        @"\A[a-z0-9][a-z0-9._-]{0,63}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public async Task<IReadOnlyList<CustomerExternalIdentifierSummary>?> ListAsync(
        string customerId,
        CancellationToken cancellationToken = default)
    {
        RequireAdministrator();
        customerId = NormalizeCustomerId(customerId);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        if (!await CustomerExistsAsync(connection, customerId, cancellationToken))
            return null;

        await using var command = new NpgsqlCommand("""
            SELECT external_identifier_id, customer_id, namespace, external_id, created_at
            FROM customer_external_identifiers
            WHERE customer_id = @customer_id AND status = 'ACTIVE'
            ORDER BY namespace, external_id, external_identifier_id;
            """, connection);
        command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var identifiers = new List<CustomerExternalIdentifierSummary>();
        while (await reader.ReadAsync(cancellationToken))
        {
            identifiers.Add(new CustomerExternalIdentifierSummary(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetFieldValue<DateTimeOffset>(4)));
        }

        return identifiers;
    }

    public async Task<CustomerExternalIdentifierSummary> AddAsync(
        string customerId,
        string @namespace,
        string externalId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var actorId = RequireAdministrator();
        customerId = NormalizeCustomerId(customerId);
        @namespace = NormalizeNamespace(@namespace);
        externalId = NormalizeExternalId(externalId);
        reason = NormalizeReason(reason);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (!await CustomerExistsAsync(connection, customerId, cancellationToken, transaction))
            throw new KeyNotFoundException("Customer was not found.");

        var identifier = new CustomerExternalIdentifierSummary(
            "cei_" + Guid.NewGuid().ToString("N"),
            customerId,
            @namespace,
            externalId,
            DateTimeOffset.UtcNow);

        try
        {
            await using var insert = new NpgsqlCommand("""
                INSERT INTO customer_external_identifiers (
                    external_identifier_id, customer_id, namespace, external_id, created_at)
                VALUES (@identifier_id, @customer_id, @namespace, @external_id, @created_at);
                """, connection, transaction);
            insert.Parameters.AddWithValue("identifier_id", NpgsqlDbType.Text, identifier.IdentifierId);
            insert.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            insert.Parameters.AddWithValue("namespace", NpgsqlDbType.Text, @namespace);
            insert.Parameters.AddWithValue("external_id", NpgsqlDbType.Text, externalId);
            insert.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, identifier.CreatedAt);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new CustomerExternalIdentifierConflictException();
        }

        await using (var audit = new NpgsqlCommand("""
            INSERT INTO customer_external_identifier_audit (
                audit_id, external_identifier_id, customer_id, namespace, external_id,
                action, actor_id, reason, occurred_at)
            VALUES (@audit_id, @identifier_id, @customer_id, @namespace, @external_id,
                    'ADDED', @actor_id, @reason, @occurred_at);
            """, connection, transaction))
        {
            audit.Parameters.AddWithValue("audit_id", NpgsqlDbType.Text, "ceia_" + Guid.NewGuid().ToString("N"));
            audit.Parameters.AddWithValue("identifier_id", NpgsqlDbType.Text, identifier.IdentifierId);
            audit.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            audit.Parameters.AddWithValue("namespace", NpgsqlDbType.Text, @namespace);
            audit.Parameters.AddWithValue("external_id", NpgsqlDbType.Text, externalId);
            audit.Parameters.AddWithValue("actor_id", NpgsqlDbType.Text, actorId);
            audit.Parameters.AddWithValue("reason", NpgsqlDbType.Text, reason);
            audit.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, identifier.CreatedAt);
            await audit.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return identifier;
    }

    public async Task<bool> RetireAsync(
        string customerId,
        string identifierId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var actorId = RequireAdministrator();
        customerId = NormalizeCustomerId(customerId);
        identifierId = NormalizeIdentifierId(identifierId);
        reason = NormalizeReason(reason);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        string @namespace;
        string externalId;
        string status;
        await using (var select = new NpgsqlCommand("""
            SELECT namespace, external_id, status
            FROM customer_external_identifiers
            WHERE customer_id = @customer_id AND external_identifier_id = @identifier_id
            FOR UPDATE;
            """, connection, transaction))
        {
            select.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            select.Parameters.AddWithValue("identifier_id", NpgsqlDbType.Text, identifierId);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return false;
            @namespace = reader.GetString(0);
            externalId = reader.GetString(1);
            status = reader.GetString(2);
        }

        if (!string.Equals(status, "ACTIVE", StringComparison.Ordinal))
            throw new CustomerExternalIdentifierConflictException();

        var retiredAt = DateTimeOffset.UtcNow;
        await using (var update = new NpgsqlCommand("""
            UPDATE customer_external_identifiers
            SET status = 'RETIRED', retired_at = @retired_at
            WHERE customer_id = @customer_id AND external_identifier_id = @identifier_id
              AND status = 'ACTIVE';
            """, connection, transaction))
        {
            update.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            update.Parameters.AddWithValue("identifier_id", NpgsqlDbType.Text, identifierId);
            update.Parameters.AddWithValue("retired_at", NpgsqlDbType.TimestampTz, retiredAt);
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new CustomerExternalIdentifierConflictException();
        }

        await using (var audit = new NpgsqlCommand("""
            INSERT INTO customer_external_identifier_audit (
                audit_id, external_identifier_id, customer_id, namespace, external_id,
                action, actor_id, reason, occurred_at)
            VALUES (@audit_id, @identifier_id, @customer_id, @namespace, @external_id,
                    'RETIRED', @actor_id, @reason, @occurred_at);
            """, connection, transaction))
        {
            audit.Parameters.AddWithValue("audit_id", NpgsqlDbType.Text, "ceia_" + Guid.NewGuid().ToString("N"));
            audit.Parameters.AddWithValue("identifier_id", NpgsqlDbType.Text, identifierId);
            audit.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            audit.Parameters.AddWithValue("namespace", NpgsqlDbType.Text, @namespace);
            audit.Parameters.AddWithValue("external_id", NpgsqlDbType.Text, externalId);
            audit.Parameters.AddWithValue("actor_id", NpgsqlDbType.Text, actorId);
            audit.Parameters.AddWithValue("reason", NpgsqlDbType.Text, reason);
            audit.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, retiredAt);
            await audit.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<CustomerExternalIdentifierAuditSummary>?> ListAuditAsync(
        string customerId,
        CancellationToken cancellationToken = default)
    {
        RequireAdministrator();
        customerId = NormalizeCustomerId(customerId);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        if (!await CustomerExistsAsync(connection, customerId, cancellationToken))
            return null;

        await using var command = new NpgsqlCommand("""
            SELECT external_identifier_id, namespace, external_id, action, actor_id, reason, occurred_at
            FROM customer_external_identifier_audit
            WHERE customer_id = @customer_id
            ORDER BY occurred_at, audit_id;
            """, connection);
        command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var entries = new List<CustomerExternalIdentifierAuditSummary>();
        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new CustomerExternalIdentifierAuditSummary(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetFieldValue<DateTimeOffset>(6)));
        }

        return entries;
    }

    private string RequireAdministrator()
    {
        if (string.IsNullOrWhiteSpace(operationsContext.ActorId) ||
            !operationsContext.Roles.Contains(MonoOperationsRoles.Administrator, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("An authenticated Mono administrator is required.");
        return operationsContext.ActorId;
    }

    private static async Task<bool> CustomerExistsAsync(
        NpgsqlConnection connection,
        string customerId,
        CancellationToken cancellationToken,
        NpgsqlTransaction? transaction = null)
    {
        await using var command = new NpgsqlCommand(
            "SELECT 1 FROM customers WHERE customer_id = @customer_id;",
            connection,
            transaction);
        command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static string NormalizeIdentifierId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim();
        if (!Regex.IsMatch(normalized, @"\\Acei_[0-9a-f]{32}\\z", RegexOptions.CultureInvariant))
            throw new ArgumentException("External identifier ID is invalid.", nameof(value));
        return normalized;
    }

    private static string NormalizeCustomerId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim();
        if (!CustomerIdPattern.IsMatch(normalized))
            throw new ArgumentException("Customer ID is invalid.", nameof(value));
        return normalized;
    }

    private static string NormalizeNamespace(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToLowerInvariant();
        if (!NamespacePattern.IsMatch(normalized))
            throw new ArgumentException("Namespace must be a lowercase system key.", nameof(value));
        return normalized;
    }

    private static string NormalizeExternalId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim();
        if (normalized.Length > 256)
            throw new ArgumentException("External ID must be at most 256 characters.", nameof(value));
        return normalized;
    }

    private static string NormalizeReason(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim();
        if (normalized.Length > 1000)
            throw new ArgumentException("Reason must be at most 1000 characters.", nameof(value));
        return normalized;
    }
}

public sealed record CustomerExternalIdentifierSummary(
    string IdentifierId,
    string CustomerId,
    string Namespace,
    string ExternalId,
    DateTimeOffset CreatedAt);

public sealed record CustomerExternalIdentifierAuditSummary(
    string IdentifierId,
    string Namespace,
    string ExternalId,
    string Action,
    string ActorId,
    string Reason,
    DateTimeOffset OccurredAt);

public sealed class CustomerExternalIdentifierConflictException : Exception { }
