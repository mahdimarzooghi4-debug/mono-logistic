using System.Text.Json.Serialization;
using Mono.Api.Persistence;
using Mono.Api.Security;

namespace Mono.Api.Api;

public sealed record CourierIdentityBindInput(
    [property: JsonPropertyName("subject")] string? Subject,
    [property: JsonPropertyName("reason_code")] string? ReasonCode,
    [property: JsonPropertyName("evidence_reference")] string? EvidenceReference);

public sealed record CourierIdentityRevokeInput(
    [property: JsonPropertyName("reason_code")] string? ReasonCode,
    [property: JsonPropertyName("evidence_reference")] string? EvidenceReference);

public static class MonoCourierIdentityBindingEndpoints
{
    public static IEndpointRouteBuilder MapMonoCourierIdentityBindingEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/operations/v1/couriers/{courierId}/identity-binding", async (
            string courierId, CourierIdentityBindInput input, HttpRequest request,
            PostgresCourierIdentityBindingService bindings, CancellationToken cancellationToken) =>
        {
            if (!TryReadIdempotencyKey(request, out var key))
                return Results.BadRequest(Error("INVALID_REQUEST", "Exactly one valid Idempotency-Key header is required."));
            try
            {
                var result = await bindings.BindAsync(courierId, input.Subject ?? string.Empty,
                    input.ReasonCode ?? string.Empty, input.EvidenceReference ?? string.Empty, key, cancellationToken);
                return Map(result);
            }
            catch (ArgumentException)
            {
                return Results.BadRequest(Error("INVALID_REQUEST", "Courier identity binding fields are invalid."));
            }
        }).RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapPost("/operations/v1/couriers/{courierId}/identity-binding/revoke", async (
            string courierId, CourierIdentityRevokeInput input, HttpRequest request,
            PostgresCourierIdentityBindingService bindings, CancellationToken cancellationToken) =>
        {
            if (!TryReadIdempotencyKey(request, out var key))
                return Results.BadRequest(Error("INVALID_REQUEST", "Exactly one valid Idempotency-Key header is required."));
            try
            {
                var result = await bindings.RevokeAsync(courierId, input.ReasonCode ?? string.Empty,
                    input.EvidenceReference ?? string.Empty, key, cancellationToken);
                return Map(result);
            }
            catch (ArgumentException)
            {
                return Results.BadRequest(Error("INVALID_REQUEST", "Courier identity revocation fields are invalid."));
            }
        }).RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapGet("/operations/v1/couriers/{courierId}/identity-binding", async (
            string courierId, PostgresCourierIdentityBindingService bindings,
            PostgresCourierOperationsReadAuditService readAudit, CancellationToken cancellationToken) =>
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(courierId, @"\Acrr_[0-9a-f]{32}\z"))
                return Results.BadRequest(Error("INVALID_REQUEST", "Courier ID is invalid."));
            var summary = await bindings.FindAsync(courierId, cancellationToken);
            await readAudit.RecordAsync(PostgresCourierOperationsReadAuditService.CourierDetail,
                courierId, summary is null ? 0 : 1, cancellationToken);
            return summary is null
                ? Results.NotFound(Error("COURIER_NOT_FOUND", "The courier identity binding was not found."))
                : Results.Ok(summary);
        }).RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        return endpoints;
    }

    private static IResult Map(CourierIdentityBindingWriteResult result) => result.Outcome switch
    {
        CourierIdentityBindingOutcome.Applied or CourierIdentityBindingOutcome.Replayed
            when result.Summary is not null => Results.Ok(result.Summary),
        CourierIdentityBindingOutcome.Conflict =>
            Results.Conflict(Error("IDEMPOTENCY_CONFLICT", "The Idempotency-Key was used with a different request.")),
        CourierIdentityBindingOutcome.CourierNotFound =>
            Results.NotFound(Error("COURIER_NOT_FOUND", "The courier was not found.")),
        CourierIdentityBindingOutcome.InvalidState =>
            Results.Conflict(Error("COURIER_IDENTITY_BINDING_NOT_ALLOWED",
                "The courier or identity binding is not in a state that allows this operation.")),
        CourierIdentityBindingOutcome.AuthorityNotConfigured =>
            Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "COURIER_IDENTITY_AUTHORITY_NOT_CONFIGURED"),
        _ => throw new InvalidOperationException("Unexpected courier identity binding outcome.")
    };

    private static bool TryReadIdempotencyKey(HttpRequest request, out string key)
    {
        key = string.Empty;
        if (!request.Headers.TryGetValue("Idempotency-Key", out var values) ||
            values.Count != 1 || string.IsNullOrWhiteSpace(values[0]))
            return false;
        key = values[0]!;
        return System.Text.RegularExpressions.Regex.IsMatch(key, @"\A[A-Za-z0-9._~-]{1,128}\z");
    }

    private static object Error(string code, string message) =>
        new { error = new { code, message } };
}
