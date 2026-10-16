using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;

namespace Mono.Api.Api;

public sealed record CourierPayoutDestinationVerificationInput(
    [property: JsonPropertyName("iban")] string? Iban,
    [property: JsonPropertyName("evidence_reference")] string? EvidenceReference);
public sealed record CourierPayoutDestinationStatusResponse(
    [property: JsonPropertyName("destination_status")] string DestinationStatus,
    [property: JsonPropertyName("masked_iban")] string? MaskedIban);
public sealed record CourierDailyWithdrawalQuotaResponse(
    [property: JsonPropertyName("daily_limit")] long DailyLimit,
    [property: JsonPropertyName("used_today")] long UsedToday,
    [property: JsonPropertyName("remaining_today")] long RemainingToday,
    [property: JsonPropertyName("resets_at")] DateTimeOffset ResetsAt);
public sealed record CourierWithdrawalCreateInput(
    [property: JsonPropertyName("amount_toman")] long AmountToman);
public sealed record CourierWithdrawalDecisionInput(
    [property: JsonPropertyName("reason_code")] string? ReasonCode,
    [property: JsonPropertyName("bank_reference")] string? BankReference);
public sealed record CourierWithdrawalItemResponse(
    [property: JsonPropertyName("withdrawal_id")] string WithdrawalId,
    [property: JsonPropertyName("amount_toman")] long AmountToman,
    [property: JsonPropertyName("destination_masked_iban")] string DestinationMaskedIban,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("reason_code")] string? ReasonCode,
    [property: JsonPropertyName("bank_reference")] string? BankReference);
public sealed record OperationsCourierWithdrawalItemResponse(
    [property: JsonPropertyName("withdrawal_id")] string WithdrawalId,
    [property: JsonPropertyName("courier_id")] string CourierId,
    [property: JsonPropertyName("amount_toman")] long AmountToman,
    [property: JsonPropertyName("destination_masked_iban")] string DestinationMaskedIban,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("reason_code")] string? ReasonCode,
    [property: JsonPropertyName("bank_reference")] string? BankReference);
public sealed record CourierWithdrawalPageResponse<T>(
    [property: JsonPropertyName("items")] IReadOnlyList<T> Items,
    [property: JsonPropertyName("next_cursor")] string? NextCursor);
public sealed record CourierWithdrawalCreatedResponse(
    [property: JsonPropertyName("withdrawal")] CourierWithdrawalItemResponse Withdrawal,
    [property: JsonPropertyName("already_requested")] bool AlreadyRequested);

public static class MonoCourierWithdrawalEndpoints
{
    public static IEndpointRouteBuilder MapMonoCourierWithdrawalEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/operations/v1/couriers/{courierId}/payout-destination/verify",
                async Task<IResult> (
                    string courierId,
                    CourierPayoutDestinationVerificationInput input,
                    HttpContext httpContext,
                    PostgresCourierWithdrawalService service,
                    TimeProvider timeProvider,
                    CancellationToken cancellationToken) =>
                {
                    var key = httpContext.Request.Headers["Idempotency-Key"].ToString();
                    if (string.IsNullOrWhiteSpace(key))
                        return Results.BadRequest(new { error_code = "IDEMPOTENCY_KEY_REQUIRED" });
                    var result = await service.VerifyDestinationAsync(courierId, input.Iban ?? "",
                        input.EvidenceReference ?? "", key, timeProvider.GetUtcNow(), cancellationToken);
                    return result.OutcomeCode switch
                    {
                        "VERIFIED" => Results.Ok(new { destination_status = "VERIFIED", already_verified = result.AlreadyRequested }),
                        "INVALID_IBAN" => Results.BadRequest(new { error_code = result.OutcomeCode }),
                        "INVALID_INPUT" => Results.BadRequest(new { error_code = result.OutcomeCode }),
                        "COURIER_NOT_FOUND" => Results.NotFound(new { error_code = result.OutcomeCode }),
                        "ENCRYPTION_UNAVAILABLE" => Results.Problem(statusCode: 503, title: "Payout destination verification is unavailable."),
                        _ => Results.Conflict(new { error_code = result.OutcomeCode })
                    };
                })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapGet("/operations/v1/couriers/{courierId}/payout-destination",
                async Task<IResult> (
                    string courierId,
                    HttpContext httpContext,
                    PostgresCourierWithdrawalService service,
                    TimeProvider timeProvider,
                    CancellationToken cancellationToken) =>
                {
                    httpContext.Response.Headers.CacheControl = "no-store";
                    var result = await service.ReadDestinationForOperationsAsync(
                        courierId, timeProvider.GetUtcNow(), cancellationToken);
                    return result.OutcomeCode switch
                    {
                        "ACCESS_GRANTED" => Results.Ok(new
                        {
                            destination_status = "VERIFIED",
                            iban = result.Iban,
                            masked_iban = result.MaskedIban
                        }),
                        "NOT_FOUND" => Results.NotFound(new { error_code = result.OutcomeCode }),
                        _ => Results.Problem(statusCode: 503,
                            title: "Payout destination read is unavailable.",
                            detail: result.OutcomeCode)
                    };
                })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapGet("/courier/v1/finance/payout-destination",
                async Task<IResult> (
                    IMonoCourierContext courier,
                    HttpContext httpContext,
                    PostgresCourierWithdrawalService service,
                    CancellationToken cancellationToken) =>
                {
                    httpContext.Response.Headers.CacheControl = "no-store";
                    var result = await service.GetDestinationStatusAsync(
                        courier.CourierId, cancellationToken);
                    return Results.Ok(new CourierPayoutDestinationStatusResponse(
                        result.OutcomeCode, result.MaskedIban));
                })
            .RequireAuthorization(MonoCourierAuthorizationPolicies.Courier);

        endpoints.MapGet("/courier/v1/finance/withdrawal-quota",
                async Task<IResult> (
                    IMonoCourierContext courier,
                    HttpContext httpContext,
                    PostgresCourierWithdrawalService service,
                    TimeProvider timeProvider,
                    CancellationToken cancellationToken) =>
                {
                    httpContext.Response.Headers.CacheControl = "no-store";
                    var quota = await service.GetDailyWithdrawalQuotaAsync(
                        courier.CourierId, timeProvider.GetUtcNow(), cancellationToken);
                    return Results.Ok(new CourierDailyWithdrawalQuotaResponse(
                        quota.DailyLimit, quota.UsedToday, quota.RemainingToday, quota.ResetsAt));
                })
            .RequireAuthorization(MonoCourierAuthorizationPolicies.Courier);

        endpoints.MapPost("/courier/v1/finance/withdrawals",
                async Task<IResult> (
                    CourierWithdrawalCreateInput input,
                    IMonoCourierContext courier,
                    HttpContext httpContext,
                    PostgresCourierWithdrawalService service,
                    TimeProvider timeProvider,
                    CancellationToken cancellationToken) =>
                {
                    var key = httpContext.Request.Headers["Idempotency-Key"].ToString();
                    if (string.IsNullOrWhiteSpace(key))
                        return Results.BadRequest(new { error_code = "IDEMPOTENCY_KEY_REQUIRED" });
                    var result = await service.CreateWithdrawalAsync(courier.CourierId, input.AmountToman,
                        key, timeProvider.GetUtcNow(), cancellationToken);
                    if (result.Withdrawal is not null)
                    {
                        var item = ToCourierResponse(result.Withdrawal);
                        return result.AlreadyRequested
                            ? Results.Ok(new CourierWithdrawalCreatedResponse(item, true))
                            : Results.Created($"/courier/v1/finance/withdrawals/{result.Withdrawal.WithdrawalId}",
                                new CourierWithdrawalCreatedResponse(item, false));
                    }
                    return result.OutcomeCode switch
                    {
                        "INVALID_INPUT" => Results.BadRequest(new { error_code = result.OutcomeCode }),
                        "COURIER_NOT_FOUND" => Results.NotFound(new { error_code = result.OutcomeCode }),
                        _ => Results.Conflict(new { error_code = result.OutcomeCode })
                    };
                })
            .RequireAuthorization(MonoCourierAuthorizationPolicies.Courier);

        endpoints.MapGet("/courier/v1/finance/withdrawals/{withdrawalId}", async Task<IResult> (
                string withdrawalId,
                IMonoCourierContext courier,
                PostgresCourierWithdrawalService service,
                CancellationToken cancellationToken) =>
            {
                var item = await service.FindForCourierAsync(courier.CourierId, withdrawalId, cancellationToken);
                return item is null ? Results.NotFound() : Results.Ok(ToCourierResponse(item));
            })
            .RequireAuthorization(MonoCourierAuthorizationPolicies.Courier);

        endpoints.MapGet("/courier/v1/finance/withdrawals", async Task<IResult> (
                IMonoCourierContext courier,
                PostgresCourierWithdrawalService service,
                int? limit,
                string? cursor,
                CancellationToken cancellationToken) =>
            {
                var pageSize = limit ?? 50;
                if (pageSize is < 1 or > 100)
                    return Results.BadRequest(new { error_code = "INVALID_LIMIT" });
                if (!TryDecodeCursor(cursor, out var before))
                    return Results.BadRequest(new { error_code = "INVALID_CURSOR" });
                var page = await service.ListForCourierAsync(courier.CourierId, pageSize, before, cancellationToken);
                return Results.Ok(new CourierWithdrawalPageResponse<CourierWithdrawalItemResponse>(
                    page.Items.Select(ToCourierResponse).ToArray(),
                    page.NextBeforeSequence is long next ? EncodeCursor(next) : null));
            })
            .RequireAuthorization(MonoCourierAuthorizationPolicies.Courier);

        endpoints.MapGet("/operations/v1/courier-withdrawals", async Task<IResult> (
                PostgresCourierWithdrawalService service,
                int? limit,
                string? cursor,
                CancellationToken cancellationToken) =>
            {
                var pageSize = limit ?? 50;
                if (pageSize is < 1 or > 100)
                    return Results.BadRequest(new { error_code = "INVALID_LIMIT" });
                if (!TryDecodeCursor(cursor, out var before))
                    return Results.BadRequest(new { error_code = "INVALID_CURSOR" });
                var page = await service.ListForOperationsAsync(pageSize, before, cancellationToken);
                return Results.Ok(new CourierWithdrawalPageResponse<OperationsCourierWithdrawalItemResponse>(
                    page.Items.Select(ToOperationsResponse).ToArray(),
                    page.NextBeforeSequence is long next ? EncodeCursor(next) : null));
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        MapDecision(endpoints, "processing", "PROCESSING");
        MapDecision(endpoints, "paid", "PAID");
        MapDecision(endpoints, "reject", "REJECTED");
        return endpoints;
    }

    private static void MapDecision(IEndpointRouteBuilder endpoints, string route, string action)
    {
        endpoints.MapPost($"/operations/v1/courier-withdrawals/{{withdrawalId}}/{route}",
                async Task<IResult> (
                    string withdrawalId,
                    CourierWithdrawalDecisionInput? input,
                    HttpContext httpContext,
                    PostgresCourierWithdrawalService service,
                    TimeProvider timeProvider,
                    CancellationToken cancellationToken) =>
                {
                    var key = httpContext.Request.Headers["Idempotency-Key"].ToString();
                    if (string.IsNullOrWhiteSpace(key))
                        return Results.BadRequest(new { error_code = "IDEMPOTENCY_KEY_REQUIRED" });
                    var result = await service.TransitionAsync(withdrawalId, action,
                        input?.ReasonCode, input?.BankReference, key, timeProvider.GetUtcNow(), cancellationToken);
                    if (result.Withdrawal is not null)
                        return Results.Ok(ToOperationsResponse(result.Withdrawal));
                    return result.OutcomeCode switch
                    {
                        "NOT_FOUND" => Results.NotFound(new { error_code = result.OutcomeCode }),
                        "INVALID_INPUT" => Results.BadRequest(new { error_code = result.OutcomeCode }),
                        _ => Results.Conflict(new { error_code = result.OutcomeCode })
                    };
                })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);
    }

    private static CourierWithdrawalItemResponse ToCourierResponse(CourierWithdrawalItem item) =>
        new(item.WithdrawalId, item.AmountToman, item.MaskedIban, item.Status, item.CreatedAt,
            item.ReasonCode, item.BankReference);

    private static OperationsCourierWithdrawalItemResponse ToOperationsResponse(CourierWithdrawalItem item) =>
        new(item.WithdrawalId, item.CourierId, item.AmountToman, item.MaskedIban, item.Status,
            item.CreatedAt, item.ReasonCode, item.BankReference);

    private static string EncodeCursor(long sequence) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(sequence.ToString(CultureInfo.InvariantCulture)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool TryDecodeCursor(string? cursor, out long? sequence)
    {
        sequence = null;
        if (cursor is null)
            return true;
        if (cursor.Length is 0 or > 128)
            return false;
        try
        {
            var value = cursor.Replace('-', '+').Replace('_', '/');
            value += new string('=', (4 - value.Length % 4) % 4);
            if (!long.TryParse(Encoding.UTF8.GetString(Convert.FromBase64String(value)),
                    NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
                return false;
            sequence = parsed;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
