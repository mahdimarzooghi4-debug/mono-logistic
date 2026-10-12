using System.Text;
using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Services;

/// <summary>
/// Read-only operator catalog for immutable zone geometry snapshots.
/// Stored snapshots are reloaded through the integrity-checking repository before being returned.
/// </summary>
public sealed class MonoZoneGeometryCatalogService(
    NpgsqlDataSource dataSource,
    PostgresZoneGeometryRepository geometryRepository,
    IMonoOperationsContext operationsContext)
{
    private const int DefaultPageSize = 50;
    private const int MaximumPageSize = 100;

    public async Task<ZoneGeometryCatalogPage> ListAsync(
        string? kind,
        int? pageSize,
        string? cursor,
        CancellationToken cancellationToken = default)
    {
        RequireAdministrator();

        ZoneKind? zoneKind = null;
        if (kind is not null)
        {
            if (!Enum.TryParse<ZoneKind>(kind, ignoreCase: false, out var parsed) ||
                !Enum.IsDefined(typeof(ZoneKind), parsed))
                throw new ArgumentException("Unsupported zone kind.", nameof(kind));
            zoneKind = parsed;
        }

        var size = pageSize ?? DefaultPageSize;
        if (size is < 1 or > MaximumPageSize)
            throw new ArgumentOutOfRangeException(nameof(pageSize), $"Page size must be between 1 and {MaximumPageSize}.");

        var after = DecodeCursor(cursor);
        var rows = new List<CatalogRow>(size + 1);
        const string sql = """
            SELECT g.zone_id, g.zone_kind, g.geometry_version, g.source_id,
                   g.source_revision, g.attribution, g.geometry_srid, g.geometry_type,
                   g.geometry_sha256, g.snapshot_sha256, g.recorded_at,
                   (a.zone_id IS NOT NULL), a.snapshot_sha256,
                   review.review_id, review.decision, review.actor_id,
                   review.reason, review.occurred_at
            FROM zone_geometry_versions g
            LEFT JOIN zone_geometry_active a
              ON a.zone_id = g.zone_id AND a.geometry_version = g.geometry_version
            LEFT JOIN LATERAL (
                SELECT r.review_id, r.decision, r.actor_id, r.reason, r.occurred_at
                FROM zone_geometry_reviews r
                WHERE r.zone_id = g.zone_id AND r.geometry_version = g.geometry_version
                ORDER BY r.review_id DESC
                LIMIT 1
            ) review ON TRUE
            WHERE (@zone_kind IS NULL OR g.zone_kind = @zone_kind)
              AND (@after_zone_id IS NULL OR (g.zone_id, g.geometry_version) > (@after_zone_id, @after_version))
            ORDER BY g.zone_id, g.geometry_version
            LIMIT @take;
            """;

        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var command = new NpgsqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("zone_kind", NpgsqlDbType.Text,
                zoneKind is null ? DBNull.Value : zoneKind.Value.ToString());
            command.Parameters.AddWithValue("after_zone_id", NpgsqlDbType.Text,
                after is null ? DBNull.Value : after.Value.ZoneId);
            command.Parameters.AddWithValue("after_version", NpgsqlDbType.Text,
                after is null ? DBNull.Value : after.Value.GeometryVersion);
            command.Parameters.AddWithValue("take", NpgsqlDbType.Integer, size + 1);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new CatalogRow(
                    reader.GetString(0), reader.GetString(1), reader.GetString(2),
                    reader.GetString(3), reader.GetString(4), reader.GetString(5),
                    reader.GetInt32(6), reader.GetString(7), reader.GetString(8),
                    reader.GetString(9), reader.GetFieldValue<DateTimeOffset>(10),
                    reader.GetBoolean(11), reader.IsDBNull(12) ? null : reader.GetString(12),
                    reader.IsDBNull(13) ? null : reader.GetInt64(13),
                    reader.IsDBNull(14) ? null : reader.GetString(14),
                    reader.IsDBNull(15) ? null : reader.GetString(15),
                    reader.IsDBNull(16) ? null : reader.GetString(16),
                    reader.IsDBNull(17) ? null : reader.GetFieldValue<DateTimeOffset>(17)));
            }
        }

        var hasMore = rows.Count > size;
        if (hasMore)
            rows.RemoveAt(rows.Count - 1);

        var items = new List<ZoneGeometryCatalogItem>(rows.Count);
        foreach (var row in rows)
        {
            if (row.IsActive && !string.Equals(
                    row.ActiveSnapshotHash, row.SnapshotHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Active zone geometry hash does not match its immutable snapshot.");

            var boundary = await geometryRepository.FindAsync(
                row.ZoneId, row.GeometryVersion, cancellationToken);
            if (boundary is null ||
                boundary.Kind.ToString() != row.Kind ||
                boundary.SourceId != row.SourceId ||
                boundary.SourceRevision != row.SourceRevision ||
                boundary.Attribution != row.Attribution ||
                boundary.GeometryType != row.GeometryType)
                throw new InvalidOperationException(
                    $"Zone geometry snapshot {row.ZoneId}/{row.GeometryVersion} failed integrity verification.");

            var latestReview = row.ReviewId is null
                ? null
                : new ZoneGeometryCatalogReview(
                    row.ReviewId.Value, row.ReviewDecision!, row.ReviewActorId!,
                    row.ReviewReason!, row.ReviewOccurredAt!.Value);

            items.Add(new ZoneGeometryCatalogItem(
                row.ZoneId, row.Kind, row.GeometryVersion, row.SourceId,
                row.SourceRevision, row.Attribution, row.GeometrySrid, row.GeometryType,
                row.GeometryHash, row.SnapshotHash, row.RecordedAt, row.IsActive,
                latestReview, IntegrityVerified: true));
        }

        var nextCursor = hasMore && rows.Count > 0 ? EncodeCursor(rows[^1].ZoneId, rows[^1].GeometryVersion) : null;
        return new ZoneGeometryCatalogPage(items, hasMore, nextCursor);
    }

    private void RequireAdministrator()
    {
        if (string.IsNullOrWhiteSpace(operationsContext.ActorId) ||
            !operationsContext.Roles.Contains(MonoOperationsRoles.Administrator, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("An authenticated Mono administrator is required.");
    }

    private static (string ZoneId, string GeometryVersion)? DecodeCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return null;
        if (cursor.Length > 1024)
            throw new ArgumentException("Catalog cursor is invalid.", nameof(cursor));

        try
        {
            var padded = cursor.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - padded.Length % 4) % 4);
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            var separator = decoded.IndexOf('\n');
            if (separator <= 0 || separator == decoded.Length - 1 ||
                decoded.IndexOf('\n', separator + 1) >= 0)
                throw new FormatException();
            return (decoded[..separator], decoded[(separator + 1)..]);
        }
        catch (FormatException)
        {
            throw new ArgumentException("Catalog cursor is invalid.", nameof(cursor));
        }
    }

    private static string EncodeCursor(string zoneId, string geometryVersion)
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{zoneId}\n{geometryVersion}"));
        return encoded.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private sealed record CatalogRow(
        string ZoneId,
        string Kind,
        string GeometryVersion,
        string SourceId,
        string SourceRevision,
        string Attribution,
        int GeometrySrid,
        string GeometryType,
        string GeometryHash,
        string SnapshotHash,
        DateTimeOffset RecordedAt,
        bool IsActive,
        string? ActiveSnapshotHash,
        long? ReviewId,
        string? ReviewDecision,
        string? ReviewActorId,
        string? ReviewReason,
        DateTimeOffset? ReviewOccurredAt);
}

public sealed record ZoneGeometryCatalogPage(
    IReadOnlyList<ZoneGeometryCatalogItem> Items,
    bool HasMore,
    string? NextCursor);

public sealed record ZoneGeometryCatalogItem(
    string ZoneId,
    string Kind,
    string GeometryVersion,
    string SourceId,
    string SourceRevision,
    string Attribution,
    int GeometrySrid,
    string GeometryType,
    string GeometryHash,
    string SnapshotHash,
    DateTimeOffset RecordedAt,
    bool IsActive,
    ZoneGeometryCatalogReview? LatestReview,
    bool IntegrityVerified);

public sealed record ZoneGeometryCatalogReview(
    long ReviewId,
    string Decision,
    string ActorId,
    string Reason,
    DateTimeOffset OccurredAt);
