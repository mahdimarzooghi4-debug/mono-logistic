# Promise Version History API v1

## Endpoint

GET /v1/delivery-requests/{deliveryRequestId}/promise/versions

Requires an authenticated customer API key with the delivery-requests.read scope. All version lookups are restricted to the authenticated customer's tenant. A Promise in another tenant is reported exactly like a missing Promise.

## Pagination

- page_size is optional; default is 20, allowed range is 1–100.
- before_version is an optional positive integer cursor.
- Items are ordered from newest version to oldest.
- When more records exist, next_before_version is set to the last returned version. Pass it as before_version to retrieve the next page.

## Response fields

Each item contains promise_id, delivery_request_id, version, committed_delivery_at, outcome, and recorded_at. The response does not contain decision IDs, safety-buffer values, policy versions, reason codes, proposal hashes, outbox IDs, or raw proposal JSON.

## Errors

- 401 when customer authentication is missing or invalid.
- 403 when the credential lacks delivery-requests.read.
- 400 for an out-of-range page_size or non-positive before_version.
- 404 with PROMISE_NOT_FOUND when no Promise exists for the delivery request in the authenticated tenant.
