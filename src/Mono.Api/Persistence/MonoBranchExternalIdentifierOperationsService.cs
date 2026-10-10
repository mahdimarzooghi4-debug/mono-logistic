using System.Text.RegularExpressions;
using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

/// <summary>Mono administrator operations for customer-scoped branch identifiers.</summary>
public sealed class MonoBranchExternalIdentifierOperationsService(
    NpgsqlDataSource dataSource,
    IMonoOperationsContext operationsContext)
{
    private static readonly Regex CustomerIdPattern = new(
        @"\Acus_[0-9a-f]{32}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex BranchIdPattern = new(
        @"\Abr_[0-9a-f]{32}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex NamespacePattern = new(
        @"\A[a-z0-9][a-z0-9._-]{0,63}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public async Task<IReadOnlyList<BranchExternalIdentifierSummary>?> ListAsync(
        string customerId,
        string branchId,
        CancellationToken cancellationToken = default)
    {
        RequireAdministrator();
        customerId = NormalizeCustomerId(customerId);
        branchId = NormalizeBranchId(branchId);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        if (!await BranchExistsAsync(connection, customerId, branchId, cancellationToken))
            return null;

        await using var command = new NpgsqlCommand("""
            SELECT external_identifier_id, branch_id, customer_id, namespace, external_id, created_at
            FROM branch_external_identifiers
            WHERE customer_id = @customer_id AND branch_id = @branch_id AND status = 'ACTIVE'
            ORDER BY namespace, external_id, external_identifier_id;
            """, connection);
        AddScope(command, customerId, branchId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var entries = new List<BranchExternalIdentifierSummary>();
        while (await reader.ReadAsync(cancellationToken))
            entries.Add(ReadSummary(reader));
        return entries;
    }

    public async Task<BranchExternalIdentifierSummary> AddAsync(
        string customerId,
        string branchId,
        string @namespace,
        string externalId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var actorId = RequireAdministrator();
        customerId = NormalizeCustomerId(customerId);
        branchId = NormalizeBranchId(branchId);
        @namespace = NormalizeNamespace(@namespace);
        externalId = NormalizeExternalId(externalId);
        reason = NormalizeReason(reason);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var branch = new NpgsqlCommand("""
            SELECT b.status, c.status
            FROM branches b
            JOIN customers c ON c.customer_id = b.customer_id
            WHERE b.customer_id = @customer_id AND b.branch_id = @branch_id
            FOR SHARE OF b, c;
            """, connection, transaction))
        {
            AddScope(branch, customerId, branchId);
            await using var reader = await branch.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new KeyNotFoundException("Branch was not found for this customer.");
            if (!string.Equals(reader.GetString(0), "ACTIVE", StringComparison.Ordinal) ||
                !string.Equals(reader.GetString(1), "ACTIVE", StringComparison.Ordinal))
                throw new InvalidOperationException("An identifier can only be added to an active branch of an active customer.");
        }

        var entry = new BranchExternalIdentifierSummary(
            "bxi_" + Guid.NewGuid().ToString("N"),
            branchId,
            customerId,
            @namespace,
            externalId,
            DateTimeOffset.UtcNow);
        try
        {
            await using var insert = new NpgsqlCommand("""
                INSERT INTO branch_external_identifiers (
                    external_identifier_id, branch_id, customer_id, namespace, external_id, created_at)
                VALUES (@identifier_id, @branch_id, @customer_id, @namespace, @external_id, @created_at);
                """, connection, transaction);
            insert.Parameters.AddWithValue("identifier_id", NpgsqlDbType.Text, entry.IdentifierId);
            AddScope(insert, customerId, branchId);
            insert.Parameters.AddWithValue("namespace", NpgsqlDbType.Text, @namespace);
            insert.Parameters.AddWithValue("external_id", NpgsqlDbType.Text, externalId);
            insert.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, entry.CreatedAt);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new BranchExternalIdentifierConflictException();
        }

        await InsertAuditAsync(
            connection, transaction, entry, "ADDED", actorId, reason, entry.CreatedAt, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return entry;
    }

    public async Task<bool> RetireAsync(
        string customerId,
        string branchId,
        string identifierId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var actorId = RequireAdministrator();
        customerId = NormalizeCustomerId(customerId);
        branchId = NormalizeBranchId(branchId);
        identifierId = NormalizeIdentifierId(identifierId);
        reason = NormalizeReason(reason);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        string @namespace;
        string externalId;
        string status;
        await using (var select = new NpgsqlCommand("""
            SELECT namespace, external_id, status
            FROM branch_external_identifiers
            WHERE customer_id = @customer_id AND branch_id = @branch_id
              AND external_identifier_id = @identifier_id
            FOR UPDATE;
            """, connection, transaction))
        {
            AddScope(select, customerId, branchId);
            select.Parameters.AddWithValue("identifier_id", NpgsqlDbType.Text, identifierId);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return false;
            @namespace = reader.GetString(0);
            externalId = reader.GetString(1);
            status = reader.GetString(2);
        }
        if (!string.Equals(status, "ACTIVE", StringComparison.Ordinal))
            throw new BranchExternalIdentifierConflictException();

        var retiredAt = DateTimeOffset.UtcNow;
        await using (var update = new NpgsqlCommand("""
            UPDATE branch_external_identifiers
            SET status = 'RETIRED', retired_at = @retired_at
            WHERE customer_id = @customer_id AND branch_id = @branch_id
              AND external_identifier_id = @identifier_id AND status = 'ACTIVE';
            """, connection, transaction))
        {
            AddScope(update, customerId, branchId);
            update.Parameters.AddWithValue("identifier_id", NpgsqlDbType.Text, identifierId);
            update.Parameters.AddWithValue("retired_at", NpgsqlDbType.TimestampTz, retiredAt);
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new BranchExternalIdentifierConflictException();
        }

        var entry = new BranchExternalIdentifierSummary(
            identifierId, branchId, customerId, @namespace, externalId, retiredAt);
        await InsertAuditAsync(
            connection, transaction, entry, "RETIRED", actorId, reason, retiredAt, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<BranchExternalIdentifierAuditSummary>?> ListAuditAsync(
        string customerId,
        string branchId,
        CancellationToken cancellationToken = default)
    {
        RequireAdministrator();
        customerId = NormalizeCustomerId(customerId);
        branchId = NormalizeBranchId(branchId);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        if (!await BranchExistsAsync(connection, customerId, branchId, cancellationToken))
            return null;

        await using var command = new NpgsqlCommand("""
            SELECT external_identifier_id, namespace, external_id, action, actor_id, reason, occurred_at
            FROM branch_external_identifier_audit
            WHERE customer_id = @customer_id AND branch_id = @branch_id
            ORDER BY occurred_at, audit_id;
            """, connection);
        AddScope(command, customerId, branchId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var entries = new List<BranchExternalIdentifierAuditSummary>();
        while (await reader.ReadAsync(cancellationToken))
            entries.Add(new BranchExternalIdentifierAuditSummary(
                reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), reader.GetString(5),
                reader.GetFieldValue<DateTimeOffset>(6)));
        return entries;
    }

    private async Task InsertAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        BranchExternalIdentifierSummary entry,
        string action,
        string actorId,
        string reason,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        await using var audit = new NpgsqlCommand("""
            INSERT INTO branch_external_identifier_audit (
                audit_id, external_identifier_id, branch_id, customer_id, namespace, external_id,
                action, actor_id, reason, occurred_at)
            VALUES (@audit_id, @identifier_id, @branch_id, @customer_id, @namespace, @external_id,
                    @action, @actor_id, @reason, @occurred_at);
            """, connection, transaction);
        audit.Parameters.AddWithValue("audit_id", NpgsqlDbType.Text, "bxia_" + Guid.NewGuid().ToString("N"));
        audit.Parameters.AddWithValue("identifier_id", NpgsqlDbType.Text, entry.IdentifierId);
        AddScope(audit, entry.CustomerId, entry.BranchId);
        audit.Parameters.AddWithValue("namespace", NpgsqlDbType.Text, entry.Namespace);
        audit.Parameters.AddWithValue("external_id", NpgsqlDbType.Text, entry.ExternalId);
        audit.Parameters.AddWithValue("action", NpgsqlDbType.Text, action);
        audit.Parameters.AddWithValue("actor_id", NpgsqlDbType.Text, actorId);
        audit.Parameters.AddWithValue("reason", NpgsqlDbType.Text, reason);
        audit.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, occurredAt);
        await audit.ExecuteNonQueryAsync(cancellationToken);
    }

    private string RequireAdministrator()
    {
        if (string.IsNullOrWhiteSpace(operationsContext.ActorId) ||
            !operationsContext.Roles.Contains(MonoOperationsRoles.Administrator, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("An authenticated Mono administrator is required.");
        return operationsContext.ActorId;
    }

    private static async Task<bool> BranchExistsAsync(
        NpgsqlConnection connection, string customerId, string branchId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT 1 FROM branches WHERE customer_id = @customer_id AND branch_id = @branch_id;
            """, connection);
        AddScope(command, customerId, branchId);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static void AddScope(NpgsqlCommand command, string customerId, string branchId)
    {
        command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
        command.Parameters.AddWithValue("branch_id", NpgsqlDbType.Text, branchId);
    }

    private static BranchExternalIdentifierSummary ReadSummary(NpgsqlDataReader reader) =>
        new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.GetFieldValue<DateTimeOffset>(5));

    private static string NormalizeCustomerId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim();
        if (!CustomerIdPattern.IsMatch(normalized))
            throw new ArgumentException("Customer ID is invalid.", nameof(value));
        return normalized;
    }

    private static string NormalizeBranchId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim();
        if (!BranchIdPattern.IsMatch(normalized))
            throw new ArgumentException("Branch ID is invalid.", nameof(value));
        return normalized;
    }

    private static string NormalizeIdentifierId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim();
        if (!Regex.IsMatch(normalized, @"\Abxi_[0-9a-f]{32}\z", RegexOptions.CultureInvariant))
            throw new ArgumentException("External identifier ID is invalid.", nameof(value));
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

public sealed record BranchExternalIdentifierSummary(
    string IdentifierId,
    string BranchId,
    string CustomerId,
    string Namespace,
    string ExternalId,
    DateTimeOffset CreatedAt);

public sealed record BranchExternalIdentifierAuditSummary(
    string IdentifierId,
    string Namespace,
    string ExternalId,
    string Action,
    string ActorId,
    string Reason,
    DateTimeOffset OccurredAt);

public sealed class BranchExternalIdentifierConflictException : Exception { }
