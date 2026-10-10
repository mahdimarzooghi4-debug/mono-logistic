using System.Text.Json;
using System.Text.RegularExpressions;
using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

/// <summary>Read-only, customer-scoped branch details and operations audit history.</summary>
public sealed class MonoBranchInspectionService(
    NpgsqlDataSource dataSource,
    IMonoOperationsContext operationsContext)
{
    private static readonly Regex CustomerIdPattern = new(
        @"\Acus_[0-9a-f]{32}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex BranchIdPattern = new(
        @"\Abr_[0-9a-f]{32}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex AuditIdPattern = new(
        @"\Aboa_[0-9a-f]{32}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public async Task<ManagedBranchDetail?> FindAsync(
        string customerId,
        string branchId,
        CancellationToken cancellationToken = default)
    {
        RequireAdministrator();
        customerId = NormalizeCustomerId(customerId);
        branchId = NormalizeBranchId(branchId);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT branch_id, customer_id, name, address, latitude, longitude,
                   zone_id, operating_hours::text, status
            FROM branches
            WHERE customer_id = @customer_id AND branch_id = @branch_id;
            """, connection);
        command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
        command.Parameters.AddWithValue("branch_id", NpgsqlDbType.Text, branchId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        JsonElement? hours = null;
        if (!reader.IsDBNull(7))
        {
            using var document = JsonDocument.Parse(reader.GetString(7));
            hours = document.RootElement.Clone();
        }

        return new ManagedBranchDetail(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetDouble(4),
            reader.IsDBNull(5) ? null : reader.GetDouble(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            hours,
            reader.GetString(8));
    }

    public async Task<ManagedBranchAuditPage?> ListAuditAsync(
        string customerId,
        string branchId,
        int pageSize,
        string? cursor,
        CancellationToken cancellationToken = default)
    {
        RequireAdministrator();
        customerId = NormalizeCustomerId(customerId);
        branchId = NormalizeBranchId(branchId);
        if (pageSize is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        if (cursor is not null && !AuditIdPattern.IsMatch(cursor))
            throw new BranchAuditCursorException();

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using (var exists = new NpgsqlCommand("""
            SELECT 1 FROM branches
            WHERE customer_id = @customer_id AND branch_id = @branch_id;
            """, connection))
        {
            exists.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            exists.Parameters.AddWithValue("branch_id", NpgsqlDbType.Text, branchId);
            if (await exists.ExecuteScalarAsync(cancellationToken) is null)
                return null;
        }

        DateTimeOffset? cursorAt = null;
        if (cursor is not null)
        {
            await using var findCursor = new NpgsqlCommand("""
                SELECT occurred_at FROM branch_operations_audit
                WHERE customer_id = @customer_id AND branch_id = @branch_id AND audit_id = @audit_id;
                """, connection);
            findCursor.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            findCursor.Parameters.AddWithValue("branch_id", NpgsqlDbType.Text, branchId);
            findCursor.Parameters.AddWithValue("audit_id", NpgsqlDbType.Text, cursor);
            await using var cursorReader = await findCursor.ExecuteReaderAsync(cancellationToken);
            if (!await cursorReader.ReadAsync(cancellationToken))
                throw new BranchAuditCursorException();
            cursorAt = cursorReader.GetFieldValue<DateTimeOffset>(0);
        }

        await using var command = new NpgsqlCommand("""
            SELECT audit_id, branch_id, customer_id, action, actor_id, reason, occurred_at
            FROM branch_operations_audit
            WHERE customer_id = @customer_id AND branch_id = @branch_id
              AND (@cursor_at IS NULL OR (occurred_at, audit_id) > (@cursor_at, @cursor_id))
            ORDER BY occurred_at, audit_id
            LIMIT @take;
            """, connection);
        command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
        command.Parameters.AddWithValue("branch_id", NpgsqlDbType.Text, branchId);
        command.Parameters.AddWithValue("cursor_at", NpgsqlDbType.TimestampTz,
            cursorAt.HasValue ? cursorAt.Value : DBNull.Value);
        command.Parameters.AddWithValue("cursor_id", NpgsqlDbType.Text,
            cursor ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("take", NpgsqlDbType.Integer, pageSize + 1);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var entries = new List<MonoBranchAuditSummary>();
        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new MonoBranchAuditSummary(
                reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), reader.GetString(5),
                reader.GetFieldValue<DateTimeOffset>(6)));
        }

        var hasMore = entries.Count > pageSize;
        if (hasMore)
            entries.RemoveAt(entries.Count - 1);
        return new ManagedBranchAuditPage(
            entries,
            hasMore ? entries[^1].AuditId : null);
    }

    private void RequireAdministrator()
    {
        if (string.IsNullOrWhiteSpace(operationsContext.ActorId) ||
            !operationsContext.Roles.Contains(MonoOperationsRoles.Administrator, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("An authenticated Mono administrator is required.");
    }

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
}

public sealed record ManagedBranchDetail(
    string BranchId,
    string CustomerId,
    string Name,
    string? Address,
    double? Latitude,
    double? Longitude,
    string? ZoneId,
    JsonElement? OperatingHours,
    string Status);

public sealed record MonoBranchAuditSummary(
    string AuditId,
    string BranchId,
    string CustomerId,
    string Action,
    string ActorId,
    string Reason,
    DateTimeOffset OccurredAt);

public sealed record ManagedBranchAuditPage(
    IReadOnlyList<MonoBranchAuditSummary> Entries,
    string? NextCursor);

public sealed class BranchAuditCursorException : Exception { }
