using System.Text.RegularExpressions;
using Mono.Api.Domain;
using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

/// <summary>Customer registry operations reserved for authenticated Mono administrators.</summary>
public sealed class MonoCustomerOperationsService(
    NpgsqlDataSource dataSource,
    IMonoOperationsContext operationsContext)
{
    private static readonly Regex CustomerIdPattern = new(
        @"\Acus_[0-9a-f]{32}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public async Task<ManagedCustomerSummary> CreatePendingAsync(
        string legalName,
        string? displayName,
        string? customerType,
        string? defaultChannel,
        string? defaultCurrency,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var actorId = RequireAdministrator();
        ArgumentException.ThrowIfNullOrWhiteSpace(legalName);
        if (legalName.Trim().Length > 256)
            throw new ArgumentException("Legal name must be at most 256 characters.", nameof(legalName));
        ValidateOptional(displayName, 128, nameof(displayName));
        ValidateOptional(customerType, 64, nameof(customerType));
        ValidateOptional(defaultChannel, 64, nameof(defaultChannel));
        ValidateOptional(defaultCurrency, 3, nameof(defaultCurrency));
        ValidateReason(reason);

        var customer = Customer.Create(
            legalName, "PENDING", displayName, customerType, defaultChannel,
            string.IsNullOrWhiteSpace(defaultCurrency) ? null : defaultCurrency.Trim().ToUpperInvariant());
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        const string insertSql = """
            INSERT INTO customers (
                customer_id, legal_name, display_name, status, customer_type,
                default_channel, default_currency, created_at)
            VALUES (
                @customer_id, @legal_name, @display_name, @status, @customer_type,
                @default_channel, @default_currency, @created_at);
            """;
        await using (var insert = new NpgsqlCommand(insertSql, connection, transaction))
        {
            insert.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customer.CustomerId);
            insert.Parameters.AddWithValue("legal_name", NpgsqlDbType.Text, customer.LegalName);
            AddNullable(insert, "display_name", NpgsqlDbType.Text, customer.DisplayName);
            insert.Parameters.AddWithValue("status", NpgsqlDbType.Text, customer.Status);
            AddNullable(insert, "customer_type", NpgsqlDbType.Text, customer.CustomerType);
            AddNullable(insert, "default_channel", NpgsqlDbType.Text, customer.DefaultChannel);
            AddNullable(insert, "default_currency", NpgsqlDbType.Text, customer.DefaultCurrency);
            insert.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, customer.CreatedAt);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await InsertAuditAsync(connection, transaction, customer.CustomerId,
            "CREATED", actorId, reason, customer.CreatedAt, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ManagedCustomerSummary.From(customer);
    }

    public async Task<MonoCustomerPageResponse?> ListAsync(
        int pageSize,
        string? cursor,
        CancellationToken cancellationToken = default)
    {
        RequireAdministrator();
        if (pageSize is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(pageSize));

        DateTimeOffset? cursorCreatedAt = null;
        if (cursor is not null)
        {
            cursor = NormalizeCustomerId(cursor);
            await using var cursorConnection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var cursorCommand = new NpgsqlCommand(
                "SELECT created_at FROM customers WHERE customer_id = @customer_id;",
                cursorConnection);
            cursorCommand.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, cursor);
            var cursorValue = await cursorCommand.ExecuteScalarAsync(cancellationToken);
            if (cursorValue is null)
                return null;
            cursorCreatedAt = (DateTimeOffset)cursorValue;
        }

        const string firstPageSql = """
            SELECT customer_id, legal_name, display_name, status, customer_type,
                   default_channel, default_currency, created_at
            FROM customers
            ORDER BY created_at DESC, customer_id DESC
            LIMIT @take;
            """;
        const string nextPageSql = """
            SELECT customer_id, legal_name, display_name, status, customer_type,
                   default_channel, default_currency, created_at
            FROM customers
            WHERE created_at < @cursor_created_at
               OR (created_at = @cursor_created_at AND customer_id < @cursor_id)
            ORDER BY created_at DESC, customer_id DESC
            LIMIT @take;
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(cursor is null ? firstPageSql : nextPageSql, connection);
        command.Parameters.AddWithValue("take", NpgsqlDbType.Integer, pageSize + 1);
        if (cursor is not null)
        {
            command.Parameters.AddWithValue("cursor_created_at", NpgsqlDbType.TimestampTz, cursorCreatedAt!.Value);
            command.Parameters.AddWithValue("cursor_id", NpgsqlDbType.Text, cursor);
        }

        var customers = new List<ManagedCustomerSummary>(pageSize + 1);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                customers.Add(new ManagedCustomerSummary(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.GetFieldValue<DateTimeOffset>(7)));
            }
        }

        string? nextCursor = null;
        if (customers.Count > pageSize)
        {
            customers.RemoveAt(customers.Count - 1);
            nextCursor = customers[^1].CustomerId;
        }

        return new MonoCustomerPageResponse(customers, nextCursor);
    }

    public async Task<ManagedCustomerSummary?> FindAsync(
        string customerId,
        CancellationToken cancellationToken = default)
    {
        RequireAdministrator();
        customerId = NormalizeCustomerId(customerId);

        const string sql = """
            SELECT customer_id, legal_name, display_name, status, customer_type,
                   default_channel, default_currency, created_at
            FROM customers
            WHERE customer_id = @customer_id;
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new ManagedCustomerSummary(
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.GetFieldValue<DateTimeOffset>(7));
    }

    public async Task<IReadOnlyList<MonoCustomerAuditSummary>?> ListAuditAsync(
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
                return null;
        }

        const string sql = """
            SELECT action, actor_id, reason, occurred_at
            FROM customer_operations_audit
            WHERE customer_id = @customer_id
            ORDER BY occurred_at, audit_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var entries = new List<MonoCustomerAuditSummary>();
        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new MonoCustomerAuditSummary(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetFieldValue<DateTimeOffset>(3)));
        }

        return entries;
    }

    public async Task ActivateAsync(
        string customerId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var actorId = RequireAdministrator();
        customerId = NormalizeCustomerId(customerId);
        ValidateReason(reason);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        string status;
        await using (var select = new NpgsqlCommand("""
            SELECT status FROM customers WHERE customer_id = @customer_id FOR UPDATE;
            """, connection, transaction))
        {
            select.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            var value = await select.ExecuteScalarAsync(cancellationToken);
            if (value is null)
                throw new KeyNotFoundException("Customer was not found.");
            status = (string)value;
        }

        if (!string.Equals(status, "PENDING", StringComparison.Ordinal))
            throw new InvalidOperationException("Only a pending customer can be activated.");

        var activatedAt = DateTimeOffset.UtcNow;
        await using (var update = new NpgsqlCommand("""
            UPDATE customers SET status = 'ACTIVE'
            WHERE customer_id = @customer_id AND status = 'PENDING';
            """, connection, transaction))
        {
            update.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("Customer status changed before activation.");
        }

        await InsertAuditAsync(connection, transaction, customerId,
            "ACTIVATED", actorId, reason, activatedAt, cancellationToken);
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

    private static void ValidateOptional(string? value, int maxLength, string parameter)
    {
        if (value?.Trim().Length > maxLength)
            throw new ArgumentException($"Value must be at most {maxLength} characters.", parameter);
    }

    private static void ValidateReason(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (reason.Trim().Length > 1000)
            throw new ArgumentException("Reason must be at most 1000 characters.", nameof(reason));
    }

    private static void AddNullable(NpgsqlCommand command, string name, NpgsqlDbType type, string? value) =>
        command.Parameters.AddWithValue(name, type,
            string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim());

    private static async Task InsertAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string customerId,
        string action,
        string actorId,
        string reason,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO customer_operations_audit (
                audit_id, customer_id, action, actor_id, reason, occurred_at)
            VALUES (@audit_id, @customer_id, @action, @actor_id, @reason, @occurred_at);
            """, connection, transaction);
        command.Parameters.AddWithValue("audit_id", NpgsqlDbType.Text, "coa_" + Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
        command.Parameters.AddWithValue("action", NpgsqlDbType.Text, action);
        command.Parameters.AddWithValue("actor_id", NpgsqlDbType.Text, actorId);
        command.Parameters.AddWithValue("reason", NpgsqlDbType.Text, reason.Trim());
        command.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, occurredAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

public sealed record MonoCustomerAuditSummary(
    string Action,
    string ActorId,
    string Reason,
    DateTimeOffset OccurredAt);

public sealed record ManagedCustomerSummary(
    string CustomerId,
    string LegalName,
    string? DisplayName,
    string Status,
    string? CustomerType,
    string? DefaultChannel,
    string? DefaultCurrency,
    DateTimeOffset CreatedAt)
{
    public static ManagedCustomerSummary From(Customer customer) =>
        new(customer.CustomerId, customer.LegalName, customer.DisplayName, customer.Status,
            customer.CustomerType, customer.DefaultChannel, customer.DefaultCurrency, customer.CreatedAt);
}

public sealed record MonoCustomerPageResponse(
    IReadOnlyList<ManagedCustomerSummary> Customers,
    string? NextCursor);
