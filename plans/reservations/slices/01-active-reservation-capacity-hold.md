# Slice 1 - Active Reservation Capacity Hold MVP

## Goal

Deliver a first end-to-end reservation path that lets users create, view, edit, and cancel active reservations, and makes active non-expired reservations consume scheduling capacity alongside orders.

This slice should be evaluable from the browser and API: a clinic can create a reservation with a future impression date and delivery date, see it in normal views, and observe that its delivery-date/week capacity affects subsequent order/reservation scheduling.

## Requirements Covered

From `plans/reservations/v1-requirements.md`:

- Reservation has no order code.
- Lab and reserving clinic visibility.
- Future impression date with all non-working days, including lab offdays, blocked.
- Reservation impressions use after-cutoff scheduling semantics.
- Active reservations consume capacity like orders.
- Cancelled/promoted/expired reservations do not consume capacity.
- Automatic ignore/expiry at `impressionDate + 2 days 00:00` lab-local time.
- Reservation edits can be saved without promotion.
- Direct order creation remains unchanged.

Explicitly out of scope for this slice:

- promotion to order;
- polished same-calendar dual-date picker;
- reservation delivery chips / impression dots in month calendar;
- reservation override logs/recommendation logs beyond minimal audit if needed;
- historical view for expired/cancelled reservations.

## Current State / Context

Current scheduling is order-only:

- `OrderRecord`, `SchedulingOrderEntity`, and `IOrderRepository` represent orders.
- `DeadlineRecommendationService` capacity usage reads active orders through `IOrderRepository.ListActiveOrdersByDeadlineRangeAsync`.
- `SchedulingOrderService` validates and persists orders inside `ISchedulingWriteTransaction`.
- Frontend order flow can be reused/adapted, but currently assumes order mode and hidden/today impression behavior.

## Desired End State

### Data model

Add a reservation entity/table and domain record, e.g.:

```text
SchedulingReservations
ReservationRecord
ReservationStatus: Active, Cancelled, Promoted
```

Suggested columns/fields:

```text
Id
ClinicCode
ClinicDisplayName
MemberId
MemberLabel
MemberPinHashFingerprint
CaseName
ImpressionDate
ProductCategory
Material
WorkItemsJson
RequestedDeliveryDate
Status
Shade
Notes
ColorNote
CalculatedCapacityUnits
CreatedAt
UpdatedAt
CreatedIp
CreatedUserAgent
PromotedOrderId nullable
PromotedOrderCode nullable
PromotedAt nullable
```

Do not add an order code or reservation code.

### Expiry / active semantics

Add a single canonical helper for active reservation filtering:

```text
IsActiveForScheduling(reservation, nowUtc)
```

Rules:

- status must be `Active`;
- lab-local now must be before `impressionDate + 2 days at 00:00`.

All ordinary display and capacity queries must use this helper/semantics.

### Reservation scheduling

Add reservation date validation using existing scheduling services where possible:

- validate impression date is future in lab-local date terms;
- validate impression date is not any configured non-working day, including lab offday;
- do not apply first-business-day-after-closure to impression dates;
- do not capacity-check impression dates;
- convert impression date to an after-cutoff timestamp for delivery recommendation/validation.

A deterministic implementation may use a lab-local timestamp like `12:00` or `11:01` on the impression date, then convert to UTC before calling `DeadlineRecommendationService`.

### Capacity including reservations

Refactor capacity usage so it includes:

- non-cancelled orders;
- active non-expired reservations.

Recommended approach:

- add a capacity-consumer abstraction, e.g. `ISchedulingCapacityUsageSource`;
- or extend `DeadlineRecommendationService` dependencies so it can read both order and reservation active deadline ranges.

Capacity queries must support excluding a reservation id during reservation update.

### API

Add reservation endpoints:

```text
POST   /api/scheduling/reservations
GET    /api/scheduling/reservations/{id}
PUT    /api/scheduling/reservations/{id}
DELETE /api/scheduling/reservations/{id}
```

Also add reservation date availability support. Either:

```text
POST /api/scheduling/reservations/dates
```

or extend `/api/scheduling/dates` with an explicit reservation mode.

Create/update request shape should mirror order shape plus explicit `impressionDate` and `requestedDeliveryDate`.

Access control:

- clinic may access only its own reservations;
- lab may access all reservations and choose target clinic on create;
- clinic cannot create/update for another clinic.

### UI

Add `+ Reservation` next to `+ New order` with distinct color.

Implement a basic reservation flow by reusing the order flow where practical:

- mode label says reservation;
- lab target clinic picker works;
- date step includes an explicit impression date control and delivery date picker;
- saving creates/updates a reservation without generating an order code;
- edit existing reservation and save changes without promotion;
- cancel reservation action from reservation detail/edit UI.

The same-calendar dual-date UX can be deferred to Slice 4, but this slice must still allow selecting both dates from the browser.

### List/root view

Ordinary root list should include active non-expired reservations alongside orders, with:

- entity type badge `Reservation`;
- no order code;
- compact material + tooth-count summary;
- impression date and delivery date.

Cancelled/expired reservations should not appear.

## Implementation Plan

1. Add reservation domain records, status enum, repository interface, and validation helpers.
2. Add EF entity/table/migration and SQLite repository.
3. Add lab-local reservation expiry helper and tests.
4. Add impression-date validator that uses the same non-working-day provider as scheduling, including lab offdays.
5. Refactor capacity usage to include active reservations.
6. Add reservation service create/get/update/cancel methods with commit-time validation inside serialized write transaction.
7. Add API endpoints and DTOs.
8. Add frontend reservation entry point, basic flow mode, save/edit/cancel UI, and active list rendering.
9. Preserve direct order behavior and existing order tests.

## TDD Plan

### Domain/unit tests

- Future impression date required.
- Weekend/holiday/lab-offday impression date rejected.
- First business day after closure is allowed for impression date.
- Reservation impression date uses after-cutoff semantics.
- Reservation expiry helper returns inactive from `impressionDate + 2 days 00:00` lab-local.
- Reservation update excludes itself from capacity checks.
- Cancelled/expired reservations do not consume capacity.

### Database tests

- Migration creates `SchedulingReservations`.
- Create/get/update/cancel reservation round-trips work items, shade, notes, capacity units.
- Active reservation deadline-range query excludes cancelled and expired reservations.
- Clinic/lab scoping repository queries return expected rows.

### API tests

- Clinic creates reservation successfully and response has no order code.
- Lab creates reservation for target clinic.
- Clinic cannot access another clinic reservation.
- Active reservation blocks later order capacity.
- Active order blocks later reservation capacity.
- Reservation cancel releases capacity.
- Expired reservation is omitted from active list and capacity.

### UI/manual tests

- Log in as clinic, click `+ Reservation`, create reservation, return to list and see reservation badge.
- Confirm no order code is shown.
- Try creating an order that exceeds capacity because of the reservation and confirm it is rejected/recommended later.
- Cancel reservation and confirm the capacity becomes available.

## Validation Plan

Run:

```bash
dotnet test Spark3Dent.sln --no-restore --verbosity quiet
```

Manual browser validation should include reservation create/edit/cancel and capacity-blocking behavior.

## Review Notes for Implementing Agent

Include in handoff:

- migration name/table summary;
- reservation domain/repository/service names;
- how after-cutoff impression timestamp is calculated;
- how active/non-expired filtering is centralized;
- how capacity usage combines orders and reservations;
- API endpoint/request/response summary;
- frontend files changed and manual validation results;
- automated test command/result;
- deviations from this plan.
