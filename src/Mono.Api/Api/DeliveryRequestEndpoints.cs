using Microsoft.AspNetCore.Mvc;
using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;
using Mono.Api.Services;

namespace Mono.Api.Api;

public static class DeliveryRequestEndpoints
{
    public static IEndpointRouteBuilder MapDeliveryRequestEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/v1/delivery-requests", async (
                DeliveryRequestInput input,
                HttpRequest request,
                CustomerBranchRepository branches,
                PostgresDeliveryRequestRepository deliveryRequests,
                CancellationToken cancellationToken) =>
            {
                var idempotencyValues = request.Headers["Idempotency-Key"];
                if (idempotencyValues.Count != 1 ||
                    !DeliveryRequestInputValidator.IsValidIdempotencyKey(idempotencyValues[0]))
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST", "A valid Idempotency-Key header is required.")));
                }

                var errors = DeliveryRequestInputValidator.Validate(input);
                if (errors.Count > 0)
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST", string.Join(" ", errors))));
                }

                var normalized = input.Normalize();
                var branch = await branches.FindBranchAsync(normalized.BranchId, cancellationToken);
                if (branch is null || !string.Equals(branch.Status, "ACTIVE", StringComparison.Ordinal))
                {
                    return Results.NotFound(new ApiErrorResponse(
                        new ApiError("BRANCH_NOT_FOUND", "The active branch was not found for this customer.")));
                }

                var result = await deliveryRequests.CreateOrReplayAsync(
                    normalized.BranchId,
                    normalized.ExternalReference,
                    idempotencyValues[0]!,
                    normalized.ToCanonicalJson(),
                    cancellationToken);

                if (result.Outcome == DeliveryRequestWriteOutcome.Conflict)
                {
                    return Results.Conflict(new ApiErrorResponse(
                        new ApiError("IDEMPOTENCY_CONFLICT",
                            "The Idempotency-Key was already used with a different request.")));
                }

                return Results.Ok(new DeliveryRequestResponse(result.DeliveryRequestId, result.Status));
            })
            .RequireAuthorization(MonoAuthorizationPolicies.DeliveryRequestsWrite);

        endpoints.MapGet("/v1/delivery-requests/{deliveryRequestId}", async (
                string deliveryRequestId,
                PostgresDeliveryRequestRepository deliveryRequests,
                PostgresDeliveryQuoteRepository quotes,
                PostgresPromiseCommitRepository promises,
                CancellationToken cancellationToken) =>
            {
                var result = await deliveryRequests.FindStatusAsync(deliveryRequestId, cancellationToken);
                if (result is null)
                {
                    return Results.NotFound(new ApiErrorResponse(
                        new ApiError("DELIVERY_REQUEST_NOT_FOUND", "The delivery request was not found for this customer.")));
                }

                var quote = await quotes.FindLatestByDeliveryRequestAsync(deliveryRequestId, cancellationToken);
                var promise = await promises.FindByDeliveryRequestAsync(deliveryRequestId, cancellationToken);
                return Results.Ok(new DeliveryRequestDetailResponse(
                    result.DeliveryRequestId,
                    result.ExternalReference,
                    result.Status,
                    result.CreatedAt,
                    result.MissionId,
                    result.MissionStatus,
                    result.CommittedDeliveryAt,
                    quote,
                    promise));
            })
            .RequireAuthorization(MonoAuthorizationPolicies.DeliveryRequestsRead);


        endpoints.MapPost("/v1/delivery-requests/{deliveryRequestId}/promise", async (
                string deliveryRequestId,
                PromiseCommitInput input,
                HttpRequest request,
                PromiseCommitApplicationService promiseCommit,
                PostgresPromiseCommitRepository promises,
                CancellationToken cancellationToken) =>
            {
                var idempotencyValues = request.Headers["Idempotency-Key"];
                if (idempotencyValues.Count != 1 ||
                    !DeliveryRequestInputValidator.IsValidIdempotencyKey(idempotencyValues[0]))
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST", "A valid Idempotency-Key header is required.")));
                }

                if (!DeliveryRequestInputValidator.IsValidDeliveryRequestId(deliveryRequestId))
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST", "The delivery request ID is invalid.")));
                }

                PromiseCommitApplicationResult result;
                try
                {
                    var criteria = input.ToRequestedCriteria();
                    var alternativeSearch = input.ToAlternativeSearch();
                    var proposal = input.ToProposal(deliveryRequestId, idempotencyValues[0]!);
                    PromiseCommitProposalValidator.Validate(proposal);
                    var requestHash = input.ComputeRequestHash(
                        deliveryRequestId, idempotencyValues[0]!);
                    result = await promiseCommit.EvaluateAndCommitIdempotentlyAsync(
                        criteria,
                        input.RequestedDeliveryAt,
                        alternativeSearch,
                        proposal,
                        requestHash,
                        cancellationToken);
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST", "The Promise proposal or feasibility evidence is invalid.")));
                }

                switch (result.Status)
                {
                    case PromiseCommitApplicationStatus.Committed:
                    case PromiseCommitApplicationStatus.Replayed:
                        var response = await promises.FindByDeliveryRequestAsync(
                            deliveryRequestId, cancellationToken);
                        return response is null
                            ? Results.NotFound(new ApiErrorResponse(
                                new ApiError("PROMISE_NOT_FOUND", "The Promise was not found for this customer.")))
                            : Results.Ok(response);
                    case PromiseCommitApplicationStatus.Conflict:
                        return Results.Conflict(new ApiErrorResponse(
                            new ApiError("PROMISE_CONFLICT",
                                "The Promise version or Idempotency-Key conflicts with an existing commitment.")));
                    case PromiseCommitApplicationStatus.DeliveryRequestNotFound:
                        return Results.NotFound(new ApiErrorResponse(
                            new ApiError("DELIVERY_REQUEST_NOT_FOUND",
                                "The delivery request was not found for this customer.")));
                    case PromiseCommitApplicationStatus.Rejected:
                        return Results.UnprocessableEntity(new ApiErrorResponse(
                            new ApiError("PROMISE_REJECTED",
                                "The feasibility evaluation rejected the requested and alternative promises.")));
                    case PromiseCommitApplicationStatus.Unresolved:
                        return Results.UnprocessableEntity(new ApiErrorResponse(
                            new ApiError("PROMISE_UNRESOLVED",
                                "Complete feasibility evidence is required before a Promise can be committed.")));
                    case PromiseCommitApplicationStatus.ProposalMismatch:
                        return Results.UnprocessableEntity(new ApiErrorResponse(
                            new ApiError("PROMISE_PROPOSAL_MISMATCH",
                                "The proposal must match the resolved feasibility outcome and delivery time.")));
                    case PromiseCommitApplicationStatus.EvidenceIntegrityInvalid:
                        return Results.UnprocessableEntity(new ApiErrorResponse(
                            new ApiError("PROMISE_EVIDENCE_INVALID",
                                "The feasibility evidence integrity envelope is invalid.")));
                    case PromiseCommitApplicationStatus.EvidenceSourceUnverified:
                        return Results.UnprocessableEntity(new ApiErrorResponse(
                            new ApiError("PROMISE_EVIDENCE_UNVERIFIED",
                                "The feasibility evidence source could not be verified.")));
                    default:
                        throw new ArgumentOutOfRangeException(nameof(result.Status));
                }
            })
            .RequireAuthorization(MonoAuthorizationPolicies.DeliveryRequestsWrite);

        endpoints.MapGet("/v1/delivery-requests/{deliveryRequestId}/promise", async (
                string deliveryRequestId,
                PostgresPromiseCommitRepository promises,
                CancellationToken cancellationToken) =>
            {
                var result = await promises.FindByDeliveryRequestAsync(deliveryRequestId, cancellationToken);
                if (result is null)
                {
                    return Results.NotFound(new ApiErrorResponse(
                        new ApiError("PROMISE_NOT_FOUND", "The committed promise was not found for this customer.")));
                }

                return Results.Ok(result);
            })
            .RequireAuthorization(MonoAuthorizationPolicies.DeliveryRequestsRead);

        endpoints.MapGet("/v1/delivery-requests/{deliveryRequestId}/promise/versions", async (
                string deliveryRequestId,
                [FromQuery(Name = "page_size")] int? pageSize,
                [FromQuery(Name = "before_version")] int? beforeVersion,
                PostgresPromiseCommitRepository promises,
                CancellationToken cancellationToken) =>
            {
                var size = pageSize ?? 20;
                if (size is < 1 or > 100 || beforeVersion is < 1)
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST",
                            "page_size must be between 1 and 100 and before_version must be positive.")));
                }

                var page = await promises.ListVersionsByDeliveryRequestAsync(
                    deliveryRequestId, size, beforeVersion, cancellationToken);
                if (page is null)
                {
                    return Results.NotFound(new ApiErrorResponse(
                        new ApiError("PROMISE_NOT_FOUND", "The committed promise was not found for this customer.")));
                }

                return Results.Ok(page);
            })
            .RequireAuthorization(MonoAuthorizationPolicies.DeliveryRequestsRead);


        endpoints.MapPost("/v1/delivery-requests/{deliveryRequestId}/quote", async (
                string deliveryRequestId,
                DeliveryQuoteIssuanceInput input,
                HttpRequest request,
                DeliveryQuoteIssuanceApplicationService issuance,
                PostgresDeliveryQuoteRepository quotes,
                CancellationToken cancellationToken) =>
            {
                var idempotencyValues = request.Headers["Idempotency-Key"];
                if (idempotencyValues.Count != 1 ||
                    !DeliveryRequestInputValidator.IsValidIdempotencyKey(idempotencyValues[0]))
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST", "A valid Idempotency-Key header is required.")));
                }

                if (!DeliveryRequestInputValidator.IsValidDeliveryRequestId(deliveryRequestId) ||
                    input.DistanceEvidence is null ||
                    input.ExpiresAt == default)
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST",
                            "A valid delivery request ID, expires_at, and distance_evidence are required.")));
                }

                HanaDistancePricingEvidence evidence;
                try
                {
                    evidence = input.DistanceEvidence.Attach();
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST", "The distance evidence fields are invalid.")));
                }

                var requestHash = input.ComputeRequestHash(deliveryRequestId, evidence);
                var result = await issuance.IssueInitialIdempotentlyAsync(
                    deliveryRequestId, input.ExpiresAt, evidence, idempotencyValues[0]!, requestHash,
                    cancellationToken);

                switch (result.Status)
                {
                    case DeliveryQuoteIssuanceStatus.Issued:
                    case DeliveryQuoteIssuanceStatus.Replayed:
                        var response = await quotes.FindLatestByDeliveryRequestAsync(
                            deliveryRequestId, cancellationToken);
                        return response is null
                            ? Results.NotFound(new ApiErrorResponse(
                                new ApiError("QUOTE_NOT_FOUND", "The quote was not found for this customer.")))
                            : Results.Ok(response);
                    case DeliveryQuoteIssuanceStatus.IdempotencyConflict:
                        return Results.Conflict(new ApiErrorResponse(
                            new ApiError("IDEMPOTENCY_CONFLICT",
                                "The Idempotency-Key was already used with a different quote request.")));
                    case DeliveryQuoteIssuanceStatus.Conflict:
                        return Results.Conflict(new ApiErrorResponse(
                            new ApiError("QUOTE_ALREADY_ISSUED",
                                "An initial Quote has already been issued for this delivery request.")));
                    case DeliveryQuoteIssuanceStatus.DeliveryRequestNotFound:
                        return Results.NotFound(new ApiErrorResponse(
                            new ApiError("DELIVERY_REQUEST_NOT_FOUND",
                                "The delivery request was not found for this customer.")));
                    case DeliveryQuoteIssuanceStatus.ExpiryInvalid:
                        return Results.UnprocessableEntity(new ApiErrorResponse(
                            new ApiError("QUOTE_EXPIRY_INVALID",
                                "expires_at must be later than the server issue time.")));
                    case DeliveryQuoteIssuanceStatus.EvidenceIntegrityInvalid:
                        return Results.UnprocessableEntity(new ApiErrorResponse(
                            new ApiError("QUOTE_EVIDENCE_INVALID",
                                "The distance evidence integrity envelope is invalid.")));
                    case DeliveryQuoteIssuanceStatus.EvidenceSourceUnverified:
                        return Results.UnprocessableEntity(new ApiErrorResponse(
                            new ApiError("QUOTE_EVIDENCE_UNVERIFIED",
                                "The distance evidence source could not be verified.")));
                    default:
                        throw new ArgumentOutOfRangeException(nameof(result.Status));
                }
            })
            .RequireAuthorization(MonoAuthorizationPolicies.DeliveryRequestsWrite);

        endpoints.MapGet("/v1/delivery-requests/{deliveryRequestId}/quote", async (
                string deliveryRequestId,
                PostgresDeliveryQuoteRepository quotes,
                CancellationToken cancellationToken) =>
            {
                var result = await quotes.FindLatestByDeliveryRequestAsync(deliveryRequestId, cancellationToken);
                if (result is null)
                {
                    return Results.NotFound(new ApiErrorResponse(
                        new ApiError("QUOTE_NOT_FOUND", "The quote was not found for this customer.")));
                }

                return Results.Ok(result);
            })
            .RequireAuthorization(MonoAuthorizationPolicies.DeliveryRequestsRead);

        endpoints.MapGet("/v1/delivery-requests/{deliveryRequestId}/quote/versions", async (
                string deliveryRequestId,
                [FromQuery(Name = "page_size")] int? pageSize,
                [FromQuery(Name = "before_version")] int? beforeVersion,
                PostgresDeliveryQuoteRepository quotes,
                CancellationToken cancellationToken) =>
            {
                var size = pageSize ?? 20;
                if (size is < 1 or > 100 || beforeVersion is < 1)
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST",
                            "page_size must be between 1 and 100 and before_version must be positive.")));
                }

                var page = await quotes.ListVersionsByDeliveryRequestAsync(
                    deliveryRequestId, size, beforeVersion, cancellationToken);
                if (page is null)
                {
                    return Results.NotFound(new ApiErrorResponse(
                        new ApiError("QUOTE_NOT_FOUND", "The quote was not found for this customer.")));
                }

                return Results.Ok(page);
            })
            .RequireAuthorization(MonoAuthorizationPolicies.DeliveryRequestsRead);

        endpoints.MapGet("/v1/delivery-requests", async (
                [FromQuery(Name = "page_size")] int? pageSize,
                string? cursor,
                PostgresDeliveryRequestRepository deliveryRequests,
                CancellationToken cancellationToken) =>
            {
                var size = pageSize ?? 50;
                if (size is < 1 or > 100)
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST", "page_size must be between 1 and 100.")));
                }

                if (cursor is not null && !DeliveryRequestInputValidator.IsValidDeliveryRequestId(cursor))
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_CURSOR", "The cursor is invalid for this customer.")));
                }

                var page = await deliveryRequests.ListStatusAsync(size, cursor, cancellationToken);
                if (page is null)
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_CURSOR", "The cursor is invalid for this customer.")));
                }

                return Results.Ok(page);
            })
            .RequireAuthorization(MonoAuthorizationPolicies.DeliveryRequestsRead);

        return endpoints;
    }
}

public sealed record ApiErrorResponse(ApiError Error);

public sealed record ApiError(string Code, string Message);
