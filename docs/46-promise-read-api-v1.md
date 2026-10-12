# Promise Read API v1

## Endpoint

GET /v1/delivery-requests/{deliveryRequestId}/promise

The endpoint requires the delivery-requests.read scope and an authenticated customer API key. The customer context from authentication is applied to the lookup. A Promise belonging to another customer is indistinguishable from a missing Promise.

## Response

A successful response contains only the customer-visible current Promise summary:

- promise_id
- delivery_request_id
- version
- committed_delivery_at
- outcome

The endpoint does not expose internal safety-buffer values, policy-version evidence, decision reason codes, or outbox metadata.

## Errors

- 401 when customer authentication is missing or invalid.
- 403 when the credential lacks delivery-requests.read.
- 404 with PROMISE_NOT_FOUND when there is no committed Promise for the request in the authenticated customer's tenant.

No write endpoint or Mission creation behavior is introduced.
