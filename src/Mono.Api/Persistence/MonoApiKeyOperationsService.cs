using System.Text.RegularExpressions;
using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

/// <summary>Managed API key lifecycle operations for Mono administrators.</summary>
public sealed class MonoApiKeyOperationsService(
    NpgsqlDataSource dataSource,
    IMonoOperationsContext operationsContext)
{
    private static readonly Regex CustomerIdPattern = new(
        @"\Acus_[0-9a-f]{32}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex KeyIdPattern = new(
        @"\Akey_[0-9a-f]{16}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public async Task<IReadOnlyList<ApiKeyInspectionSummary>> ListForCustomerAsync(
        string customerId,
        CancellationToken cancellationToken = default)
    {
        RequireAdministrator();
        customerId = NormalizeCustomerId(customerId);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using (var customer = new NpgsqlCommand(
            "SELECT 1 FROM customers WHERE customer_id = @customer_id;",
            connection))
        {
            customer.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            if (await customer.ExecuteScalarAsync(cancellationToken) is null)
                throw new KeyNotFoundException("Customer was not found.");
        }

        await using var command = new NpgsqlCommand("""
            SELECT key_id, customer_id, scopes, created_at, expires_at, revoked_at
            FROM customer_api_keys
            WHERE customer_id = @customer_id
            ORDER BY created_at DESC, key_id;
            """, connection);
        command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var keys = new List<ApiKeyInspectionSummary>();
        while (await reader.ReadAsync(cancellationToken))
        {
            keys.Add(new ApiKeyInspectionSummary(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetFieldValue<string[]>(2),
                reader.GetFieldValue<DateTimeOffset>(3),
                reader.GetFieldValue<DateTimeOffset>(4),
                reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5)));
        }

        return keys;
    }

    public async Task<IReadOnlyList<ApiKeyAuditSummary>?> ListAuditAsync(
        string customerId,
        string keyId,
        CancellationToken cancellationToken = default)
    {
        RequireAdministrator();
        customerId = NormalizeCustomerId(customerId);
        keyId = NormalizeKeyId(keyId);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using (var key = new NpgsqlCommand("""
            SELECT 1 FROM customer_api_keys
            WHERE customer_id = @customer_id AND key_id = @key_id;
            """, connection))
        {
            key.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            key.Parameters.AddWithValue("key_id", NpgsqlDbType.Text, keyId);
            if (await key.ExecuteScalarAsync(cancellationToken) is null)
                return null;
        }

        await using var command = new NpgsqlCommand("""
            SELECT key_id, customer_id, action, actor_id, reason, occurred_at
            FROM customer_api_key_operations_audit
            WHERE customer_id = @customer_id AND key_id = @key_id
            ORDER BY occurred_at, audit_id;
            """, connection);
        command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
        command.Parameters.AddWithValue("key_id", NpgsqlDbType.Text, keyId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var entries = new List<ApiKeyAuditSummary>();
        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new ApiKeyAuditSummary(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetFieldValue<DateTimeOffset>(5)));
        }

        return entries;
    }

    public async Task<ApiKeyRevocationSummary> RevokeAsync(
        string customerId,
        string keyId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var actorId = RequireAdministrator();
        customerId = NormalizeCustomerId(customerId);
        keyId = NormalizeKeyId(keyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (reason.Trim().Length > 1000)
            throw new ArgumentException("Reason must be at most 1000 characters.", nameof(reason));

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        bool alreadyRevoked;
        await using (var select = new NpgsqlCommand("""
            SELECT revoked_at
            FROM customer_api_keys
            WHERE customer_id = @customer_id AND key_id = @key_id
            FOR UPDATE;
            """, connection, transaction))
        {
            select.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            select.Parameters.AddWithValue("key_id", NpgsqlDbType.Text, keyId);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new KeyNotFoundException("API key was not found for this customer.");
            alreadyRevoked = !reader.IsDBNull(0);
        }

        if (alreadyRevoked)
            throw new InvalidOperationException("API key has already been revoked.");

        var revokedAt = DateTimeOffset.UtcNow;
        await using (var update = new NpgsqlCommand("""
            UPDATE customer_api_keys
            SET revoked_at = @revoked_at
            WHERE customer_id = @customer_id AND key_id = @key_id AND revoked_at IS NULL;
            """, connection, transaction))
        {
            update.Parameters.AddWithValue("revoked_at", NpgsqlDbType.TimestampTz, revokedAt);
            update.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            update.Parameters.AddWithValue("key_id", NpgsqlDbType.Text, keyId);
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("API key changed before revocation.");
        }

        await using (var audit = new NpgsqlCommand("""
            INSERT INTO customer_api_key_operations_audit (
                audit_id, key_id, customer_id, action, actor_id, reason, occurred_at)
            VALUES (@audit_id, @key_id, @customer_id, 'REVOKED', @actor_id, @reason, @occurred_at);
            """, connection, transaction))
        {
            audit.Parameters.AddWithValue("audit_id", NpgsqlDbType.Text, "kao_" + Guid.NewGuid().ToString("N"));
            audit.Parameters.AddWithValue("key_id", NpgsqlDbType.Text, keyId);
            audit.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            audit.Parameters.AddWithValue("actor_id", NpgsqlDbType.Text, actorId);
            audit.Parameters.AddWithValue("reason", NpgsqlDbType.Text, reason.Trim());
            audit.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, revokedAt);
            await audit.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new ApiKeyRevocationSummary(keyId, customerId, revokedAt);
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

    private static string NormalizeKeyId(string keyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        var normalized = keyId.Trim();
        if (!KeyIdPattern.IsMatch(normalized))
            throw new ArgumentException("API key ID is invalid.", nameof(keyId));
        return normalized;
    }
}

public sealed record ApiKeyInspectionSummary(
    string KeyId,
    string CustomerId,
    IReadOnlyList<string> Scopes,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt);

public sealed record ApiKeyAuditSummary(
    string KeyId,
    string CustomerId,
    string Action,
    string ActorId,
    string Reason,
    DateTimeOffset OccurredAt);

public sealed record ApiKeyRevocationSummary(
    string KeyId,
    string CustomerId,
    DateTimeOffset RevokedAt);
