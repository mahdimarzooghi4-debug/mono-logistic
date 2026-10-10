using System.Text.Json;
using System.Text.RegularExpressions;
using Mono.Api.Domain;
using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

public sealed class CustomerServiceConfigurationRepository(
    NpgsqlDataSource dataSource,
    ICustomerContext customerContext)
{
    private static readonly Regex ServiceCodePattern = new(
        @"\A[A-Z][A-Z0-9_]{0,63}\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public async Task<CustomerServiceConfiguration?> FindEffectiveAsync(
        string serviceCode,
        DateTimeOffset evaluatedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceCode);
        var normalizedServiceCode = serviceCode.Trim().ToUpperInvariant();
        if (!ServiceCodePattern.IsMatch(normalizedServiceCode))
        {
            throw new ArgumentException("Service code must use uppercase ASCII letters, digits, or underscores.",
                nameof(serviceCode));
        }

        const string sql = """
            SELECT service_configuration_id, customer_id, service_code, version, status,
                   effective_from, effective_to, configuration::text, created_at
            FROM customer_service_configurations
            WHERE customer_id = @customer_id
              AND service_code = @service_code
              AND status = 'ACTIVE'
              AND effective_from <= @evaluated_at
              AND (effective_to IS NULL OR @evaluated_at < effective_to)
            ORDER BY version DESC
            LIMIT 2;
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerContext.CustomerId);
        command.Parameters.AddWithValue("service_code", NpgsqlDbType.Text, normalizedServiceCode);
        command.Parameters.AddWithValue("evaluated_at", NpgsqlDbType.TimestampTz, evaluatedAt);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var configuration = Map(reader);
        if (await reader.ReadAsync(cancellationToken))
        {
            throw new AmbiguousServiceConfigurationException(normalizedServiceCode, customerContext.CustomerId);
        }

        return configuration;
    }

    private static CustomerServiceConfiguration Map(NpgsqlDataReader reader)
    {
        using var document = JsonDocument.Parse(reader.GetString(7));
        return new CustomerServiceConfiguration(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt32(3),
            reader.GetString(4),
            reader.GetFieldValue<DateTimeOffset>(5),
            reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
            document.RootElement.Clone(),
            reader.GetFieldValue<DateTimeOffset>(8));
    }
}
