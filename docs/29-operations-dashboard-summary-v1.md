# Operations Dashboard Summary v1

`GET /operations/v1/dashboard/summary?sla_horizon_minutes=15` returns a point-in-time aggregate for the Mono operations console. The endpoint is administrator-only and sends `Cache-Control: no-store`.

## Contract

| Field | Definition | Source |
| --- | --- | --- |
| `as_of` | UTC instant used for all current-state calculations | API clock |
| `business_date` / `time_zone` | Current business day in `Asia/Tehran` | API clock and IANA time zone |
| `active_mission_count` | Missions in a nonterminal lifecycle state | `missions` |
| `unassigned_mission_count` | Active missions without an accepted courier offer | `missions`, `mission_courier_offers` |
| `online_courier_count` | Active couriers whose latest presence projection is online | `couriers`, `courier_live_presence` |
| `sla_at_risk_count` | Active missions due after `as_of` and no later than `as_of + sla_horizon_minutes` | `missions.committed_delivery_at` |
| `sla_overdue_count` | Active missions whose committed delivery time has passed | `missions.committed_delivery_at` |
| `delivered_today_count` | Delivered mission events inside the Tehran business-day interval | `outbox_events` |
| `on_time_delivered_today_count` | Those delivered events at or before their committed delivery time | `outbox_events`, `missions` |
| `on_time_delivery_rate_percent` | On-time delivered today / delivered today × 100, rounded to one decimal; `null` when no missions were delivered | Derived |

The SLA horizon accepts 0 through 10,080 minutes and defaults to 15. A mission can appear as overdue or at risk, never both. Dashboard reads are recorded as `DASHBOARD_SUMMARY` in the operations read audit. No sample values are substituted when authoritative data is unavailable; the console shows an error and leaves the aggregate unavailable.

Zone capacity is intentionally absent: the existing API exposes planned capacity, which is not equivalent to live available capacity. Incoming-call metrics also remain outside this contract until Mono selects a telephony provider and phone-number matching policy.
