# Sprint 131 — Dispatch queue attempt outcomes

## Goal

Make recent courier dispatch outcomes visible in the operations queue so staff can see retry context without leaving the queue.

## Delivered

- Add an attempt count and latest-attempt summary to each dispatch queue item.
- Include the latest offer's courier, status, last event action, reason, and timestamp.
- Preserve the active-offer projection and existing queue filters and pagination.
- Cover the no-attempt, active-offer, and withdrawn-offer cases in the lifecycle integration test.

Full attempt history remains available through the mission dispatch-attempt history endpoint.
