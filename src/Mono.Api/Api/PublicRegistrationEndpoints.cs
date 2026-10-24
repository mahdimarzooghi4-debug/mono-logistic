using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Mono.Api.Persistence;
using Mono.Api.Security;

namespace Mono.Api.Api;

public sealed record PublicRegistrationInput(
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("full_name")] string? FullName,
    [property: JsonPropertyName("phone")] string? Phone,
    [property: JsonPropertyName("business_name")] string? BusinessName,
    [property: JsonPropertyName("privacy_notice_version")] string? PrivacyNoticeVersion,
    [property: JsonPropertyName("consent")] bool Consent);

public sealed record PublicRegistrationTransitionInput(
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("reason_code")] string? ReasonCode);

public static class PublicRegistrationEndpoints
{
    public static IEndpointRouteBuilder MapPublicRegistrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/v1/public-registration-requests", async (
            PublicRegistrationInput input, HttpRequest request, PostgresPublicRegistrationService service, CancellationToken ct) =>
        {
            if (!service.IsEnabled)
                return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "Registration is temporarily unavailable.", type: "https://mono.invalid/problems/registration-unavailable");
            if (request.Headers["Idempotency-Key"].Count != 1 || string.IsNullOrWhiteSpace(request.Headers["Idempotency-Key"].ToString()))
                return Results.BadRequest(new { error = "IDEMPOTENCY_KEY_REQUIRED" });
            try
            {
                var result = await service.SubmitAsync(input.Type ?? "", new(input.FullName ?? "", input.Phone ?? "", input.BusinessName),
                    input.Consent, input.PrivacyNoticeVersion ?? "", request.Headers["Idempotency-Key"].ToString(), ct);
                if (result.Outcome == PublicRegistrationWriteOutcome.Conflict)
                    return Results.Conflict(new { error = "IDEMPOTENCY_CONFLICT" });
                return result.Outcome == PublicRegistrationWriteOutcome.Created
                    ? Results.Accepted(value: result.Receipt)
                    : Results.Ok(result.Receipt);
            }
            catch (ArgumentException)
            {
                return Results.BadRequest(new { error = "INVALID_REGISTRATION" });
            }
        }).AllowAnonymous();

        endpoints.MapGet("/operations/v1/public-registration-requests", async (
            [FromQuery(Name = "page_size")] int? pageSize,
            PostgresPublicRegistrationService service, CancellationToken ct) =>
        {
            var size = pageSize ?? 50;
            if (size is < 1 or > 100) return Results.BadRequest(new { error = "INVALID_PAGE_SIZE" });
            try { return Results.Ok(await service.ListAsync(size, ct)); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
        }).RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapPost("/operations/v1/public-registration-requests/{requestId}/transition", async (
            string requestId, PublicRegistrationTransitionInput input,
            PostgresPublicRegistrationService service, CancellationToken ct) =>
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(requestId, @"\Areg_[0-9a-f]{32}\z"))
                return Results.BadRequest(new { error = "INVALID_REQUEST_ID" });
            try
            {
                var updated = await service.TransitionAsync(requestId, input.Status ?? "", input.ReasonCode, ct);
                return updated ? Results.NoContent() : Results.NotFound(new { error = "REQUEST_NOT_FOUND" });
            }
            catch (ArgumentException)
            {
                return Results.BadRequest(new { error = "INVALID_TRANSITION" });
            }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
        }).RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);
        return endpoints;
    }
}