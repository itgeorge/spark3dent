# Slice 5 - Reservation Overrides, Recommendation Logs, and Debug Visibility

## Goal

Bring reservations to parity with orders for scheduling auditability and explicit lab override behavior.

After this slice, successful reservation create/update/promotion decisions are inspectable, lab users can explicitly override invalid reservation delivery dates with a reason, and override/recommendation logs can be retrieved for debugging.

## Requirements Covered

From `plans/reservations/v1-requirements.md`:

- Lab override rules apply to reservation delivery dates.
- Clinic users cannot override invalid delivery dates.
- Reservation create/update/promotion writes recommendation logs or reservation-specific equivalent.
- Lab overrides on reservation create/update/promotion write override logs with rules bypassed and reason.
- Override-scheduled reservations consume capacity while active.
- Expired/cancelled/promoted reservations disappear from ordinary views.
- Optional follow-up: historical/debug page for inactive reservations.

Explicitly out of scope:

- detailed admin UI for all inactive reservations unless chosen as a small debug page;
- changing direct order override behavior except to share code safely.

## Current State / Context

Orders already have:

- `DeadlineRecommendationLog`;
- `DeadlineOverrideLog`;
- explicit lab override request fields;
- debug endpoints for order logs;
- UI modal requiring override reason.

Reservations from earlier slices have create/update/promotion but may only support normal valid saves.

## Desired End State

### Reservation recommendation logs

Choose one approach:

1. Extend existing recommendation log schema with nullable reservation fields:

```text
OrderId nullable
OrderCode nullable
ReservationId nullable
EntityType
```

2. Or add separate reservation recommendation log table.

Either is acceptable if API/debug access is clear.

Logged fields should match order recommendation logs where possible:

- reservation id;
- actor;
- impression timestamp/date using after-cutoff semantics;
- effective intake business date;
- material config snapshot;
- tooth count;
- capacity units;
- minimum/recommended/selected delivery date;
- candidate checks;
- result status/failure reason.

Successful reservation create/update should write a reservation recommendation log.

Successful reservation promotion should write:

- normal order recommendation log for the created order; and/or
- reservation promotion log metadata linking the reservation and order.

### Reservation override logs

Choose one approach:

1. Extend existing override log schema with nullable reservation fields; or
2. Add separate reservation override log table.

Override logs should record:

- reservation id;
- selected delivery date;
- system recommended date;
- minimum date;
- order/reservation capacity units;
- rules bypassed;
- reason;
- daily/weekly usage and limits when available;
- recommendation log id if practical.

### Backend behavior

Reservation create/update/promotion delivery validation should behave like order saves:

- valid selected delivery date: save normally;
- invalid selected delivery date and actor is clinic: reject;
- invalid selected delivery date and actor is lab without confirmation/reason: reject with override-required response;
- invalid selected delivery date and actor is lab with confirmation + non-empty reason: save and log override.

Important: overrides apply only to delivery-date scheduling rules. Impression date restrictions are not overrideable in V1 unless a future requirement says so.

### API

Add debug endpoints, for example:

```text
GET /api/scheduling/reservations/{id}/deadline-recommendation-logs
GET /api/scheduling/reservations/{id}/deadline-override-logs
```

Access:

- lab only for logs, matching current order debug endpoints.

Reservation create/update/promote requests should accept existing override fields:

```json
{
  "confirmDeadlineOverride": true,
  "deadlineOverrideReason": "..."
}
```

Error responses should expose:

```json
{
  "error": "...",
  "overrideAllowed": true,
  "failedRules": ["WeeklyCapacityExceeded"],
  "recommendedDate": "2026-06-08"
}
```

### UI

Reservation flow should reuse the existing lab override modal for delivery-date override.

Behavior:

- clinic users cannot select/save invalid delivery dates;
- lab users can choose invalid delivery date, see warning, enter reason, and save reservation;
- if commit-time revalidation fails after stale preview, lab can resubmit with reason;
- impression-date invalidity should show a normal validation error, not override modal.

### Optional debug/historical page

If time permits, add a small lab-only debug view or endpoint to list cancelled/promoted/expired reservations. This is optional; ordinary views must continue hiding them.

## Implementation Plan

1. Decide log schema approach: extend existing tables vs reservation-specific tables.
2. Add migration/repositories/domain records.
3. Refactor order log builders if useful so reservation logs reuse recommendation audit data.
4. Add override decision path to reservation create/update/promotion services.
5. Ensure override saves still happen inside serialized write transaction after validation.
6. Persist recommendation log after successful reservation mutation.
7. Persist override log after successful override mutation.
8. Add API debug endpoints and access control.
9. Wire reservation frontend to existing override modal and request fields.
10. Add tests and manual validation.

## TDD Plan

### Domain/unit tests

- Clinic invalid reservation delivery save rejects and no override log is created.
- Lab invalid reservation delivery save without reason rejects.
- Lab invalid reservation delivery save with reason succeeds and logs rules.
- Override reservation consumes future capacity.
- Invalid impression date cannot be overridden.
- Reservation create/update produces recommendation log.
- Promotion produces order log and preserves reservation/order linkage.

### Database tests

- Recommendation log round-trip for reservation.
- Override log round-trip for reservation.
- New schema remains compatible with existing order logs.
- Pending model changes test passes.

### API tests

- Lab can retrieve reservation recommendation logs.
- Lab can retrieve reservation override logs.
- Clinic cannot retrieve debug logs.
- Lab override response/request works for reservation create/update.
- Clinic override fields are ignored/rejected for invalid reservation save.

### UI/manual tests

- As lab, pick capacity-full delivery date for reservation, enter override reason, save.
- Confirm reservation is saved, visible, and consumes capacity.
- Retrieve override log through API and confirm reason/rules.
- Confirm invalid impression date cannot be overridden.

## Validation Plan

Run:

```bash
dotnet test Spark3Dent.sln --no-restore --verbosity quiet
```

Manual validation should include one lab override and one clinic rejection.

## Review Notes for Implementing Agent

Include:

- chosen log schema;
- migration name;
- API endpoint shapes;
- how override decision shares or differs from order override;
- whether recommendation log id is referenced by override log;
- manual validation results;
- automated test result;
- deviations from plan.
