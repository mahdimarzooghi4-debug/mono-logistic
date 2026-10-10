using Mono.Api.Persistence;
using Mono.Api.Security;

namespace Mono.Api.Api;

public static class BranchEndpoints
{
    public static IEndpointRouteBuilder MapBranchEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/v1/branches", async (
                CustomerBranchRepository repository,
                CancellationToken cancellationToken) =>
            {
                var branches = await repository.ListBranchesAsync(cancellationToken);
                return Results.Ok(new BranchListResponse(branches.Select(BranchSummary.From).ToArray()));
            })
            .RequireAuthorization(MonoAuthorizationPolicies.BranchesRead);

        return endpoints;
    }
}

public sealed record BranchListResponse(IReadOnlyList<BranchSummary> Branches);

public sealed record BranchSummary(
    string BranchId,
    string Name,
    string? Address,
    double? Latitude,
    double? Longitude,
    string? ZoneId,
    string Status)
{
    public static BranchSummary From(Mono.Api.Domain.Branch branch) =>
        new(branch.BranchId, branch.Name, branch.Address, branch.Latitude, branch.Longitude, branch.ZoneId, branch.Status);
}
