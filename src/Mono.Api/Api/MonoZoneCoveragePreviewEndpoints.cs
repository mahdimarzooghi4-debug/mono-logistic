using Mono.Api.Security;
using Mono.Api.Services;

namespace Mono.Api.Api;

public static class MonoZoneCoveragePreviewEndpoints
{
    public static IEndpointRouteBuilder MapMonoZoneCoveragePreviewEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/operations/v1/zones/coverage-preview", async (
            ZoneCoveragePreviewRequest request,
            MonoZoneCoveragePreviewService previews,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await previews.PreviewAsync(
                    request.Latitude, request.Longitude, cancellationToken));
            }
            catch (ArgumentException)
            {
                return Results.BadRequest(new ApiErrorResponse(new ApiError(
                    "INVALID_COORDINATE", "Latitude and longitude must be valid WGS84 coordinates.")));
            }
        }).RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        return endpoints;
    }
}

public sealed record ZoneCoveragePreviewRequest(double Latitude, double Longitude);
