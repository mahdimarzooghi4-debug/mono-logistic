namespace Mono.Api.Security;

/// <summary>
/// Identity supplied by a trusted Mono staff authentication boundary.
/// Implementations must derive actor and roles from authenticated server-side claims,
/// never from request data or configuration authoring arguments.
/// </summary>
public interface IMonoOperationsContext
{
    string ActorId { get; }
    IReadOnlyCollection<string> Roles { get; }
}

public static class MonoOperationsRoles
{
    public const string Administrator = "MONO_ADMIN";
}
