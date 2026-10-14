using Mono.Api.Domain;

namespace Mono.Api.Persistence;

public interface IRoutePlanVersionStore
{
    Task<RoutePlanVersionWriteResult> PersistAsync(
        RoutePlanVersionWriteCommand command,
        CancellationToken cancellationToken = default);

    Task<RoutePlanVersion?> FindCurrentAsync(
        string routeId,
        CancellationToken cancellationToken = default);

    Task<RoutePlanVersion?> FindVersionAsync(
        string routeId,
        int version,
        CancellationToken cancellationToken = default);
}
