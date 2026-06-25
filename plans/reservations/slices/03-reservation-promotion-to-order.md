# Slice 3 - Reservation Promotion to Order

## Goal

Allow the reserving clinic and lab to promote an active reservation into a real order with an order code, while atomically replacing the reservation's capacity hold with the created order.

After this slice, clinics can use reservations as intended: reserve expected future work, then promote the reservation when the impression is physically taken.

## Requirements Covered

From `plans/reservations/v1-requirements.md`:

- Clinics can and are expected to promote their own reservations.
- Lab can promote any active reservation.
- Promotion generates an order code.
- Promotion creates an order from reservation data.
- Created order exposes source reservation traceability, preferably `PromotedFromReservationId`.
- Promoted reservation no longer consumes capacity.
- Promotion excludes the reservation capacity during validation so capacity is counted once.
- Promotion is serialized/atomic with commit-time revalidation.
- Expired/cancelled/promoted reservations cannot be promoted through normal flow.

Explicitly out of scope:

- lab override for invalid promotion date if not already available from Slice 1;
- historical reservation management page;
- invoice/accounting behavior.

## Current State / Context

After Slice 1 and 2:

- active reservations exist, consume capacity, and can be opened from list/calendar;
- reservations do not have order codes;
- order create/update already has code generation, audit, recommendation log, and commit-time revalidation patterns.

## Desired End State

### Domain model

Add order traceability fields:

```text
SchedulingOrders.PromotedFromReservationId nullable
OrderRecord.PromotedFromReservationId nullable
Order DTO promotedFromReservationId
```

Reservation already has:

```text
PromotedOrderId nullable
PromotedOrderCode nullable
PromotedAt nullable
Status = Promoted
```

### Promotion behavior

Promotion must run inside the serialized scheduling write transaction.

Required operation:

1. Re-fetch reservation.
2. Check actor visibility and promotion authorization:
   - reserving clinic can promote own active reservation;
   - lab can promote any active reservation.
3. Check reservation active and non-expired.
4. Re-run delivery validation using reservation impression date after-cutoff semantics.
5. Exclude the current reservation id from capacity usage.
6. If valid, generate unique order code and create order.
7. Set order `PromotedFromReservationId`.
8. Mark reservation `Promoted`, set promoted order id/code/time.
9. Commit.

If any step fails, no order is created and reservation remains active.

### Created order data

The created order should copy:

- clinic code/display name;
- member attribution policy needs to be explicit:
  - preferred: use promoting actor as order creator/member, while audit metadata records reservation creator;
- case name;
- impression date;
- product category;
- material;
- work items;
- requested delivery date;
- shade;
- notes/color note;
- calculated capacity units.

Order `CreatedAt` should be promotion time. Scheduling/recommendation logs should use the reservation impression date/timestamp for deadline calculations.

### UI

Reservation detail/edit flow should include a clear promotion action, e.g.:

```text
Promote to order
```

Behavior:

- available for active non-expired reservations only;
- clinic can promote own reservation;
- lab can promote all visible active reservations;
- success routes to created order confirmation/review and shows generated code;
- failure shows clear scheduling/expiry/authorization error.

The normal `Save reservation` action must remain separate and must not promote.

### API

Add:

```text
POST /api/scheduling/reservations/{id}/promote
```

Response:

```json
{
  "reservation": { ...promoted reservation... },
  "order": { ...created order... }
}
```

## Implementation Plan

1. Add `PromotedFromReservationId` to order domain/entity/DTO and migration.
2. Add reservation repository method or transaction operation to mark promoted.
3. Add promotion method on reservation service using `ISchedulingWriteTransaction`.
4. Reuse order code generation with duplicate retry inside transaction.
5. Reuse deadline validation/audit from order create, passing reservation impression timestamp and reservation exclusion.
6. Persist order recommendation log after successful promotion.
7. Add audit event `ReservationPromoted` and optionally `OrderCreatedFromReservation` metadata.
8. Add API endpoint.
9. Add UI promotion button and success navigation.

## TDD Plan

### Domain/unit tests

- Clinic can promote own active reservation.
- Clinic cannot promote another clinic's reservation.
- Lab can promote active reservation.
- Expired reservation cannot be promoted.
- Cancelled reservation cannot be promoted.
- Promotion excludes reservation capacity and counts capacity once after commit.
- Promotion failure does not create order or alter reservation.

### Database tests

- Order `PromotedFromReservationId` persists.
- Reservation promoted fields persist.
- Active reservation capacity query excludes promoted reservations.

### API tests

- `POST /reservations/{id}/promote` returns order with code and promoted reservation.
- Created order DTO contains `promotedFromReservationId`.
- Generated order appears in order list/calendar.
- Reservation disappears from active reservation list/calendar.
- Capacity after promotion is counted once.

### UI/manual tests

- Create reservation as clinic.
- Open reservation, click `Promote to order`.
- Confirm generated code appears.
- Confirm reservation disappears from active views and order appears.
- Confirm capacity indicator/availability is unchanged except entity type changed.

## Validation Plan

Run:

```bash
dotnet test Spark3Dent.sln --no-restore --verbosity quiet
```

Manual validation should prove promotion replacement capacity behavior.

## Review Notes for Implementing Agent

Include:

- migration name;
- promotion API shape;
- order/reservation fields added;
- code generation retry behavior;
- capacity exclusion/replacement explanation;
- UI route after promotion;
- test and manual validation results.
