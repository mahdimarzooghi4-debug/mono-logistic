# Operations Delivery Request and Mission context API

The MFA-gated Mono administrator endpoint searches Delivery Requests across customers and includes safe Mission context when the request has been confirmed.

```
GET /operations/v1/delivery-requests?page_size=50&status=RECEIVED&query=HANA-ORDER&created_after=2026-10-01T00:00:00Z&created_before=2026-10-31T23:59:59Z&customer_id=cus_...&branch_id=br_...&has_mission=true&cursor=drq_...
GET /operations/v1/delivery-requests/{delivery_request_id}
```

Search uses keyset pagination ordered by `created_at DESC, delivery_request_id DESC`; page size is 1–100 (default 50). The optional status filter currently accepts `RECEIVED`; the case-insensitive prefix query matches the Delivery Request ID or external reference. Date bounds are inclusive. Optional filters select a customer, branch, or whether a Mission exists. A supplied cursor must match all active filters.

List and detail responses contain the request ID, customer ID, branch ID, external reference, request status, and creation time. When confirmed, they also contain Mission ID, Mission status, and committed delivery time; those three fields are null before confirmation. The response never includes stored request payloads, addresses, coordinates, or contact details.

Search and detail reads are recorded in the append-only Delivery Request operations audit table. Search terms and filter values are not stored in that audit record.
