# Quote Read API v1

## Endpoints

- GET /v1/delivery-requests/{deliveryRequestId}/quote
- GET /v1/delivery-requests/{deliveryRequestId}/quote/versions

Both endpoints require an authenticated customer API key with delivery-requests.read. Every lookup uses the authenticated tenant. A quote belonging to another customer is returned as not found.

## Current Quote

The current endpoint returns the newest persisted version, including quote ID, request ID, version, issue and expiry instants, whether it is expired at read time, distance, total fare, buyer/seller shares, and record timestamp. The response excludes policy-version evidence and audit-evidence identifiers.

An expired Quote remains readable as historical/current persisted state; the API reports is_expired=true. It does not renew or extend the quote.

## Version history

The history endpoint returns newest-first Quote summaries. page_size defaults to 20 and accepts 1–100. before_version is an optional positive cursor; next_before_version is returned when another page exists.

## Errors

- 401 when customer authentication is missing or invalid.
- 403 when the credential lacks delivery-requests.read.
- 400 for an invalid page size or non-positive version cursor.
- 404 with QUOTE_NOT_FOUND when no Quote exists for the request in the authenticated tenant.

The API does not create, calculate, renew, confirm, or charge a Quote. Expiry remains the timestamp persisted with the Quote.
