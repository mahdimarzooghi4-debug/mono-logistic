using Mono.Api.Domain;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

public sealed class CustomerBranchRepository(NpgsqlDataSource dataSource)
{
    public async Task CreateCustomerAsync(Customer customer, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO customers (
                customer_id, legal_name, display_name, status, customer_type,
                default_channel, default_currency, created_at)
            VALUES (
                @customer_id, @legal_name, @display_name, @status, @customer_type,
                @default_channel, @default_currency, @created_at);
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customer.CustomerId);
        command.Parameters.AddWithValue("legal_name", NpgsqlDbType.Text, customer.LegalName);
        AddNullable(command, "display_name", NpgsqlDbType.Text, customer.DisplayName);
        command.Parameters.AddWithValue("status", NpgsqlDbType.Text, customer.Status);
        AddNullable(command, "customer_type", NpgsqlDbType.Text, customer.CustomerType);
        AddNullable(command, "default_channel", NpgsqlDbType.Text, customer.DefaultChannel);
        AddNullable(command, "default_currency", NpgsqlDbType.Text, customer.DefaultCurrency);
        command.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, customer.CreatedAt);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task CreateBranchAsync(Branch branch, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO branches (
                branch_id, customer_id, name, address, latitude, longitude,
                zone_id, operating_hours, status)
            VALUES (
                @branch_id, @customer_id, @name, @address, @latitude, @longitude,
                @zone_id, @operating_hours, @status);
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("branch_id", NpgsqlDbType.Text, branch.BranchId);
        command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, branch.CustomerId);
        command.Parameters.AddWithValue("name", NpgsqlDbType.Text, branch.Name);
        AddNullable(command, "address", NpgsqlDbType.Text, branch.Address);
        AddNullable(command, "latitude", NpgsqlDbType.Double, branch.Latitude);
        AddNullable(command, "longitude", NpgsqlDbType.Double, branch.Longitude);
        AddNullable(command, "zone_id", NpgsqlDbType.Text, branch.ZoneId);
        AddNullable(command, "operating_hours", NpgsqlDbType.Jsonb, branch.OperatingHoursJson);
        command.Parameters.AddWithValue("status", NpgsqlDbType.Text, branch.Status);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<Branch?> FindBranchAsync(
        string customerId,
        string branchId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT branch_id, customer_id, name, address, latitude, longitude,
                   zone_id, operating_hours::text, status
            FROM branches
            WHERE customer_id = @customer_id AND branch_id = @branch_id;
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
        command.Parameters.AddWithValue("branch_id", NpgsqlDbType.Text, branchId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new Branch
        {
            BranchId = reader.GetString(0),
            CustomerId = reader.GetString(1),
            Name = reader.GetString(2),
            Address = GetNullableString(reader, 3),
            Latitude = reader.IsDBNull(4) ? null : reader.GetDouble(4),
            Longitude = reader.IsDBNull(5) ? null : reader.GetDouble(5),
            ZoneId = GetNullableString(reader, 6),
            OperatingHoursJson = GetNullableString(reader, 7),
            Status = reader.GetString(8)
        };
    }

    private static void AddNullable<T>(NpgsqlCommand command, string name, NpgsqlDbType type, T? value)
    {
        command.Parameters.AddWithValue(name, type, value is null ? DBNull.Value : value);
    }

    private static string? GetNullableString(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
}
