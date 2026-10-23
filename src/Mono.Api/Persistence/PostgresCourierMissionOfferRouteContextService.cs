using System.Text.RegularExpressions;
using Mono.Api.Domain;
using Mono.Api.Services;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

/// <summary>Reads only the current courier's live offer and its versioned map coordinates.</summary>
public sealed class PostgresCourierMissionOfferRouteContextService(
    NpgsqlDataSource dataSource,
    IRouteLocationCoordinateResolver coordinates,
    TimeProvider timeProvider)
{
    private static readonly Regex OfferIdPattern = new(
        @"\Amco_[0-9a-f]{32}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex CourierIdPattern = new(
        @"\Acrr_[0-9a-f]{32}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public async Task<CourierMissionOfferRouteContext?> FindAsync(
        string courierId,
        string offerId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(courierId) || !CourierIdPattern.IsMatch(courierId) ||
            string.IsNullOrWhiteSpace(offerId) || !OfferIdPattern.IsMatch(offerId))
            throw new ArgumentException("Courier or offer ID is invalid.");

        const string sql = """
            SELECT o.offer_id, o.mission_id, m.delivery_request_id, o.expires_at
            FROM mission_courier_offers o
            JOIN missions m ON m.mission_id = o.mission_id
            WHERE o.offer_id = @offer_id
              AND o.courier_id = @courier_id
              AND o.status = 'OFFERED'
              AND m.status = 'SEARCHING_COURIER'
              AND o.expires_at > @now;
            """;

        string? readOfferId = null;
        string? missionId = null;
        string? deliveryRequestId = null;
        DateTimeOffset? expiresAt = null;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var command = new NpgsqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("offer_id", NpgsqlDbType.Text, offerId);
            command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
            command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, timeProvider.GetUtcNow());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                readOfferId = reader.GetString(0);
                missionId = reader.GetString(1);
                deliveryRequestId = reader.GetString(2);
                expiresAt = reader.GetFieldValue<DateTimeOffset>(3).ToUniversalTime();
            }
        }

        if (readOfferId is null || missionId is null || deliveryRequestId is null || expiresAt is null)
            return null;

        var suffix = deliveryRequestId.StartsWith("drq_", StringComparison.Ordinal)
            ? deliveryRequestId[4..]
            : string.Empty;
        var locationIds = new[] { $"loc_{suffix}_pickup", $"loc_{suffix}_dropoff" };
        var resolved = suffix.Length == 32
            ? await coordinates.ResolveAsync(locationIds, cancellationToken)
            : new Dictionary<string, GeoCoordinate>(StringComparer.Ordinal);

        return CourierMissionOfferRouteContext.Create(
            readOfferId, missionId, deliveryRequestId, expiresAt.Value, resolved);
    }
}
