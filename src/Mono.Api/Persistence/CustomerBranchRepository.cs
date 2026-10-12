using Mono.Api.Domain;
using Npgsql;
using NpgsqlTypes;
using Mono.Api.Security;

namespace Mono.Api.Persistence;

public sealed class CustomerBranchRepository(NpgsqlDataSource dataSource, ICustomerContext customerContext)
{
    public async Task CreateBranchAsync(Branch branch, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(branch.CustomerId, customerContext.CustomerId, StringComparison.Ordinal))
        {
            throw new CustomerContextUnavailableException("A branch can only be written inside the authenticated customer context.");
        }

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

    public async Task<IReadOnlyList<Branch>> ListBranchesAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT branch_id, customer_id, name, address, latitude, longitude,
                   zone_id, operating_hours::text, status
            FROM branches
            WHERE customer_id = @customer_id
            ORDER BY name, branch_id;
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerContext.CustomerId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var branches = new List<Branch>();
        while (await reader.ReadAsync(cancellationToken))
        {
            branches.Add(new Branch
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
            });
        }

        return branches;
    }

    public async Task<Branch?> FindBranchAsync(
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
        command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerContext.CustomerId);
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

    private static void AddNullable(NpgsqlCommand command, string name, NpgsqlDbType type, object? value)
    {
        command.Parameters.AddWithValue(name, type, value ?? DBNull.Value);
    }

    private static string? GetNullableString(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
}
