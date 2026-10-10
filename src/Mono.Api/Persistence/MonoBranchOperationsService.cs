using System.Text.Json;
using System.Text.RegularExpressions;
using Mono.Api.Domain;
using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

/// <summary>Branch registry writes for authenticated Mono administrators.</summary>
public sealed class MonoBranchOperationsService(
    NpgsqlDataSource dataSource,
    IMonoOperationsContext operationsContext)
{
    private static readonly Regex CustomerIdPattern = new(
        @"\Acus_[0-9a-f]{32}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public async Task<ManagedBranchSummary> CreateForActiveCustomerAsync(
        string customerId,
        string name,
        string? address,
        double? latitude,
        double? longitude,
        string? zoneId,
        JsonElement? operatingHours,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var actorId = RequireAdministrator();
        customerId = NormalizeCustomerId(customerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Trim().Length > 256)
            throw new ArgumentException("Branch name must be at most 256 characters.", nameof(name));
        if (address?.Trim().Length > 1000)
            throw new ArgumentException("Address must be at most 1000 characters.", nameof(address));
        if (zoneId?.Trim().Length > 128)
            throw new ArgumentException("Zone ID must be at most 128 characters.", nameof(zoneId));
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (reason.Trim().Length > 1000)
            throw new ArgumentException("Reason must be at most 1000 characters.", nameof(reason));

        string? operatingHoursJson = null;
        if (operatingHours.HasValue)
        {
            if (operatingHours.Value.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("Operating hours must be a JSON object.", nameof(operatingHours));
            operatingHoursJson = operatingHours.Value.GetRawText();
        }

        var branch = Branch.Create(
            customerId, name, "ACTIVE", address, latitude, longitude, zoneId, operatingHoursJson);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var customer = new NpgsqlCommand("""
            SELECT status FROM customers WHERE customer_id = @customer_id FOR SHARE;
            """, connection, transaction))
        {
            customer.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            var status = await customer.ExecuteScalarAsync(cancellationToken);
            if (status is null)
                throw new KeyNotFoundException("Customer was not found.");
            if (!string.Equals((string)status, "ACTIVE", StringComparison.Ordinal))
                throw new InvalidOperationException("A branch can only be added to an active customer.");
        }

        const string insertSql = """
            INSERT INTO branches (
                branch_id, customer_id, name, address, latitude, longitude,
                zone_id, operating_hours, status)
            VALUES (
                @branch_id, @customer_id, @name, @address, @latitude, @longitude,
                @zone_id, @operating_hours, @status);
            """;
        await using (var insert = new NpgsqlCommand(insertSql, connection, transaction))
        {
            insert.Parameters.AddWithValue("branch_id", NpgsqlDbType.Text, branch.BranchId);
            insert.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, branch.CustomerId);
            insert.Parameters.AddWithValue("name", NpgsqlDbType.Text, branch.Name);
            AddNullable(insert, "address", NpgsqlDbType.Text, branch.Address);
            AddNullable(insert, "latitude", NpgsqlDbType.Double, branch.Latitude);
            AddNullable(insert, "longitude", NpgsqlDbType.Double, branch.Longitude);
            AddNullable(insert, "zone_id", NpgsqlDbType.Text, branch.ZoneId);
            AddNullable(insert, "operating_hours", NpgsqlDbType.Jsonb, branch.OperatingHoursJson);
            insert.Parameters.AddWithValue("status", NpgsqlDbType.Text, branch.Status);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var audit = new NpgsqlCommand("""
            INSERT INTO branch_operations_audit (
                audit_id, branch_id, customer_id, action, actor_id, reason, occurred_at)
            VALUES (@audit_id, @branch_id, @customer_id, 'CREATED', @actor_id, @reason, @occurred_at);
            """, connection, transaction))
        {
            audit.Parameters.AddWithValue("audit_id", NpgsqlDbType.Text, "boa_" + Guid.NewGuid().ToString("N"));
            audit.Parameters.AddWithValue("branch_id", NpgsqlDbType.Text, branch.BranchId);
            audit.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            audit.Parameters.AddWithValue("actor_id", NpgsqlDbType.Text, actorId);
            audit.Parameters.AddWithValue("reason", NpgsqlDbType.Text, reason.Trim());
            audit.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, DateTimeOffset.UtcNow);
            await audit.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return ManagedBranchSummary.From(branch);
    }

    public async Task<ManagedBranchSummary> UpdateForActiveCustomerAsync(
        string customerId,
        string branchId,
        string name,
        string? address,
        double? latitude,
        double? longitude,
        string? zoneId,
        JsonElement? operatingHours,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var actorId = RequireAdministrator();
        customerId = NormalizeCustomerId(customerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Trim().Length > 256)
            throw new ArgumentException("Branch name must be at most 256 characters.", nameof(name));
        if (address?.Trim().Length > 1000)
            throw new ArgumentException("Address must be at most 1000 characters.", nameof(address));
        if (zoneId?.Trim().Length > 128)
            throw new ArgumentException("Zone ID must be at most 128 characters.", nameof(zoneId));
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (reason.Trim().Length > 1000)
            throw new ArgumentException("Reason must be at most 1000 characters.", nameof(reason));

        string? operatingHoursJson = null;
        if (operatingHours.HasValue)
        {
            if (operatingHours.Value.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("Operating hours must be a JSON object.", nameof(operatingHours));
            operatingHoursJson = operatingHours.Value.GetRawText();
        }

        var updated = Branch.Create(
            customerId, name, "ACTIVE", address, latitude, longitude, zoneId, operatingHoursJson);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var select = new NpgsqlCommand("""
            SELECT b.status, c.status
            FROM branches b
            JOIN customers c ON c.customer_id = b.customer_id
            WHERE b.branch_id = @branch_id AND b.customer_id = @customer_id
            FOR UPDATE OF b, c;
            """, connection, transaction))
        {
            select.Parameters.AddWithValue("branch_id", NpgsqlDbType.Text, branchId.Trim());
            select.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new KeyNotFoundException("Branch was not found for this customer.");
            if (!string.Equals(reader.GetString(0), "ACTIVE", StringComparison.Ordinal) ||
                !string.Equals(reader.GetString(1), "ACTIVE", StringComparison.Ordinal))
                throw new InvalidOperationException("Only an active branch of an active customer can be updated.");
        }

        await using (var update = new NpgsqlCommand("""
            UPDATE branches
            SET name = @name, address = @address, latitude = @latitude,
                longitude = @longitude, zone_id = @zone_id, operating_hours = @operating_hours
            WHERE branch_id = @branch_id AND customer_id = @customer_id AND status = 'ACTIVE';
            """, connection, transaction))
        {
            update.Parameters.AddWithValue("name", NpgsqlDbType.Text, updated.Name);
            AddNullable(update, "address", NpgsqlDbType.Text, updated.Address);
            AddNullable(update, "latitude", NpgsqlDbType.Double, updated.Latitude);
            AddNullable(update, "longitude", NpgsqlDbType.Double, updated.Longitude);
            AddNullable(update, "zone_id", NpgsqlDbType.Text, updated.ZoneId);
            AddNullable(update, "operating_hours", NpgsqlDbType.Jsonb, updated.OperatingHoursJson);
            update.Parameters.AddWithValue("branch_id", NpgsqlDbType.Text, branchId.Trim());
            update.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("Branch changed before update.");
        }

        await using (var audit = new NpgsqlCommand("""
            INSERT INTO branch_operations_audit (
                audit_id, branch_id, customer_id, action, actor_id, reason, occurred_at)
            VALUES (@audit_id, @branch_id, @customer_id, 'UPDATED', @actor_id, @reason, @occurred_at);
            """, connection, transaction))
        {
            audit.Parameters.AddWithValue("audit_id", NpgsqlDbType.Text, "boa_" + Guid.NewGuid().ToString("N"));
            audit.Parameters.AddWithValue("branch_id", NpgsqlDbType.Text, branchId.Trim());
            audit.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            audit.Parameters.AddWithValue("actor_id", NpgsqlDbType.Text, actorId);
            audit.Parameters.AddWithValue("reason", NpgsqlDbType.Text, reason.Trim());
            audit.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, DateTimeOffset.UtcNow);
            await audit.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return updated with { BranchId = branchId.Trim() };
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

    private static void AddNullable(NpgsqlCommand command, string name, NpgsqlDbType type, object? value) =>
        command.Parameters.AddWithValue(name, type, value ?? DBNull.Value);
}

public sealed record ManagedBranchSummary(
    string BranchId,
    string CustomerId,
    string Name,
    string? Address,
    double? Latitude,
    double? Longitude,
    string? ZoneId,
    string Status)
{
    public static ManagedBranchSummary From(Branch branch) =>
        new(branch.BranchId, branch.CustomerId, branch.Name, branch.Address,
            branch.Latitude, branch.Longitude, branch.ZoneId, branch.Status);
}
