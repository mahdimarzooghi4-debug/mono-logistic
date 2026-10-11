# Versioned Quote Contract v1

## Quote snapshot

A `DeliveryQuote` records:

- quote ID and tenant customer ID;
- delivery request ID and positive quote version;
- issue and explicit expiry timestamps;
- the calculated Hana distance fare;
- policy versions, including the version matching the fare; and
- one or more unique audit-evidence identifiers.

The contract validates the tenant and delivery-request ID formats already used by Mono. It verifies that the fare snapshot still matches the distance and the Hana distance tariff version recorded on the quote.

## Expiration

`ExpiresAt` must be later than `IssuedAt`. `DeliveryQuoteContract.IsExpired` treats the quote as expired when the evaluation time is equal to or later than `ExpiresAt`.

The contract requires an explicit expiry timestamp. It does not choose a quote lifetime, default TTL, renewal behavior, or persistence policy; those decisions remain open.

## Scope limits

This is a validation-only domain contract. It does not issue or persist quotes, assign versions, enforce idempotency, or expose an API. Audit evidence IDs are references, not proof that evidence is accurate or trusted. Source trust continues to follow the separate fail-closed source-verification boundary.
