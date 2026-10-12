# Location Resolution v1

## Purpose

Normalize a delivery location into a validated WGS84 coordinate while keeping address lookup behind Mono's provider-neutral Maps Gateway.

## Resolution rules

1. Latitude and longitude must either both be supplied or both be absent.
2. A supplied pair must be finite and within WGS84 latitude/longitude ranges.
3. When a valid coordinate pair is present, it is authoritative. Address text is retained in normalized form and geocoding is not called.
4. When coordinates are absent, a non-empty address is required and sent to the Maps Gateway after trimming.
5. A geocoding no-match remains unresolved (null); the resolver does not invent coordinates or treat an address as a successful resolution.
6. A provider-formatted address is retained when present; otherwise the normalized input address is retained.
7. Cancellation is passed through to the Maps Gateway.

## Boundary

Sprint 042 adds the reusable DeliveryLocationResolver capability and its contract tests. It is not wired into delivery-request intake, which remains receipt-only and stores the submitted canonical request. No provider is configured or contacted by this change.

Zone resolution is not implemented here. It depends on the zone registry and geometry model under E6.1; until those exist, a resolved coordinate does not imply coverage or service eligibility.
