namespace Mono.Api.Domain;

public sealed record RouteLocationCoordinateWriteCommand(
    string CommandId,
    string LocationId,
    int ExpectedVersion,
    GeoCoordinate Coordinate);

public enum RouteLocationCoordinateWriteOutcome
{
    Persisted,
    Replayed,
    Conflict
}

public sealed record RouteLocationCoordinateWriteResult(
    RouteLocationCoordinateWriteOutcome Outcome,
    string LocationId,
    int Version);

public sealed record RouteLocationCoordinateVersion(
    string LocationId,
    int Version,
    GeoCoordinate Coordinate,
    DateTimeOffset RecordedAt);

public static class RouteLocationCoordinateValidator
{
    public static void Validate(RouteLocationCoordinateWriteCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!IsIdentifier(command.CommandId) || !IsIdentifier(command.LocationId) ||
            command.ExpectedVersion < 0 || command.Coordinate is null || !command.Coordinate.IsValid)
            throw new ArgumentException("Route location coordinate write command is malformed.", nameof(command));
    }

    private static bool IsIdentifier(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value == value.Trim() &&
        value.Length <= 128 && !value.Any(char.IsControl);
}
