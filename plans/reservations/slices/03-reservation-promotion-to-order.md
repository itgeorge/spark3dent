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

- reservation delivery overrides / override logs;
- historical reservation management page;
- invoice/accounting behavior;
- changing direct order creation behavior.

## Current State / Context

After Slices 1 and 2:

- active reservations exist, consume capacity, and can be opened from list/calendar;
- reservation calendar display already uses `/api/scheduling/orders/calendar` fields `reservations` and `impressionReservations`;
- reservation list/calendar endpoints hide cancelled/promoted/expired reservations through active reservation queries;
- reservations do not have order codes;
- reservation review/detail uses `Web/wwwroot/js/order-review-view.js` with `reviewKind === 'reservation'`;
- reservation edit/save uses `Web/wwwroot/js/order-flow-view.js` and remains separate from promotion;
- frontend API wrapper is `Web/wwwroot/js/orders-api.js`;
- `SchedulingReservationService` already has transaction-based create/update validation and disables reservation deadline overrides;
- `SchedulingOrderService` already has the order code generation retry, order audit, deadline recommendation log, and override-log patterns, but many helpers are private.

Current relevant backend files:

- `Orders/OrderRecord.cs`
- `Orders/ReservationRecord.cs`
- `Orders/SchedulingReservationService.cs`
- `Orders/SchedulingOrderService.cs`
- `Orders/SchedulingWriteTransaction.cs`
- `Database/SqliteSchedulingWriteTransaction.cs`
- `Database/SqliteOrderRepo.cs`
- `Database/SqliteReservationRepo.cs`
- `Database/Entities/SchedulingOrderEntity.cs`
- `Database/Entities/SchedulingReservationEntity.cs`
- `Web/SchedulingApi.cs`

## Desired End State

### Domain / persistence traceability

Add order traceability fields:

```text
SchedulingOrders.PromotedFromReservationId nullable
SchedulingOrderEntity.PromotedFromReservationId nullable
OrderRecord.PromotedFromReservationId nullable
Order DTO promotedFromReservationId
```

Implementation hint: add `PromotedFromReservationId = null` as a trailing optional positional parameter on `OrderRecord` to minimize constructor churn, then wire it through `SqliteOrderRepo.ToDomain`, `ApplyToEntity`, and `SchedulingApi.ToDto(OrderRecord)`.

Reservation already has:

```text
PromotedOrderId nullable
PromotedOrderCode nullable
PromotedAt nullable
Status = Promoted
```

No human-readable reservation reference/order-code-like field should be added.

### Promotion behavior

Promotion must run inside the serialized scheduling write transaction (`ISchedulingWriteTransaction.ExecuteAsync((txOrders, txReservations) => ...)`).

Required operation:

1. Re-fetch reservation inside the transaction.
2. Check actor visibility and promotion authorization:
   - reserving clinic can promote own active reservation;
   - lab can promote any active reservation.
3. Check reservation is active and non-expired using `ReservationActiveRules.IsActiveForScheduling(reservation, _clock.UtcNow)`.
   - `EnsureActive` alone is not enough because it only checks status.
4. Re-run delivery validation using reservation impression date after-cutoff semantics:
   - `ReservationActiveRules.ToAfterCutoffImpressionTimestampUtc(reservation.ImpressionDate)`.
5. Exclude the current reservation id from reservation capacity usage.
   - Use `new OrderSchedulingInput(material, workItems, impressionTimestampUtc, excludedOrderId: null, excludedReservationId: reservation.Id)` or the existing helper pattern in `SchedulingReservationService`.
6. Do **not** allow promotion deadline overrides in this slice.
   - If the requested delivery date is invalid, reject with `overrideAllowed: false` / clear error.
7. Generate a unique order code and create an order.
8. Set order `PromotedFromReservationId = reservation.Id`.
9. Mark reservation `Promoted`, set `PromotedOrderId`, `PromotedOrderCode`, `PromotedAt`, and `UpdatedAt`.
10. Commit.
11. After commit, write audit/recommendation logs for the created order and reservation promotion.

If any transactional step fails, no order is created and the reservation remains active.

### Created order data

The created order should copy reservation case/scheduling data:

- clinic code/display name from reservation;
- case name;
- impression date;
- product category;
- material;
- work items;
- requested delivery date;
- shade;
- notes/color note;
- calculated capacity units from promotion-time validation;
- `PromotedFromReservationId`.

Member attribution policy for the created order:

- use the promoting actor as `MemberId`, `MemberLabel`, and `MemberPinHashFingerprint`;
- audit metadata should include reservation id and reservation creator/member info for traceability.

Order `CreatedAt` and `UpdatedAt` should be promotion time. Deadline recommendation logs should use the reservation impression timestamp after cutoff, not promotion time.

### Service/API placement

Preferred backend shape:

- Add `PromoteReservationAsync(actor, id, ip, userAgent, ct)` on `SchedulingReservationService` returning both records, e.g. `ReservationPromotionResult(ReservationRecord Reservation, OrderRecord Order)`.
- Add dependencies to `SchedulingReservationService` as needed:
  - `IOrderCodeGenerator` for generated order code retry;
  - `IDeadlineRecommendationLogRepository` for order recommendation logs created by promotion;
  - keep reservation overrides disabled; no override log dependency is required for promotion in this slice.
- Either copy the small private order-code/recommendation-log helpers from `SchedulingOrderService` or refactor shared helpers carefully. Prefer the smallest safe change.

Add API endpoint:

```text
POST /api/scheduling/reservations/{id}/promote
```

Response:

```json
{
  "reservation": { ...promoted reservation... },
  "order": { ...created order with promotedFromReservationId... }
}
```

API errors should be consistent with existing reservation endpoints:

- `401` not authenticated;
- `404` reservation not found/not visible;
- `400` inactive/expired/invalid delivery date;
- no successful promotion of cancelled/promoted/expired reservations.

Add frontend API wrapper:

```js
promoteReservation: function(id) {
  return jsonRequest('/api/scheduling/reservations/' + encodeURIComponent(id) + '/promote', { method: 'POST', body: '{}' }, { error: 'Could not promote reservation.' });
}
```

### UI

Reservation detail/review should include a clear promotion action, e.g.:

```text
Promote to order
```

Current place to add it:

- `Web/wwwroot/orders.html`: add a button near `reviewCancelBtn` / `reviewEditBtn` in the review action row, e.g. `reviewPromoteBtn`.
- `Web/wwwroot/js/order-review-view.js`: show/enable it only for active reservations; hide it for normal orders and cancelled/promoted reservations.

Behavior:

- available for active non-expired reservations only as far as the UI can know;
- backend remains authoritative for expiry/authorization/date validity;
- clicking calls `ordersApi.promoteReservation(reviewOrder.id)`;
- disable the button while saving and show errors in `reviewMsg`;
- success should route to the created order confirmation/review and show generated code.

Recommended success route:

```text
created/{orderCode}
```

because existing `createdConfirmationView` already loads the order and displays the generated code. Passing through `order/{orderCode}` is also acceptable if simpler, but document the chosen route in review notes.

The normal `Save reservation` action must remain separate and must not promote.

## Implementation Plan

1. Add migration and model mapping for `SchedulingOrders.PromotedFromReservationId`.
2. Add `PromotedFromReservationId` to `OrderRecord`, `SchedulingOrderEntity`, `SqliteOrderRepo`, and order DTOs.
3. Add promotion result type and `SchedulingReservationService.PromoteReservationAsync`.
4. Add any required service dependencies in composition/DI.
5. In promotion transaction:
   - load authorized reservation from `txReservations`;
   - require active status and non-expired active scheduling state;
   - validate delivery date with reservation id excluded from capacity;
   - generate/create order with retry using `txOrders.CreateOrderAsync`;
   - update reservation to `Promoted` with order id/code/time.
6. After commit, append reservation/order audit metadata and persist the created order's deadline recommendation log.
7. Add `POST /api/scheduling/reservations/{id}/promote` to `Web/SchedulingApi.cs`.
8. Add `ordersApi.promoteReservation`.
9. Add review UI promotion button and success navigation.
10. Refresh list/calendar state after promotion naturally by navigating to created order; when returning to root, active reservation queries should omit promoted rows.

## TDD Plan

### Domain/unit tests

- Clinic can promote own active reservation.
- Clinic cannot promote another clinic's reservation.
- Lab can promote active reservation.
- Expired reservation cannot be promoted.
- Cancelled reservation cannot be promoted.
- Already promoted reservation cannot be promoted again.
- Promotion excludes reservation capacity and counts capacity once after commit.
- Promotion failure does not create order or alter reservation.
- Promotion rejects invalid delivery date for both clinic and lab; no reservation override path.

### Database tests

- Order `PromotedFromReservationId` persists.
- Reservation promoted fields persist.
- Active reservation list/calendar queries exclude promoted reservations.
- Active order list/calendar queries include the promoted order.

### API tests

- `POST /api/scheduling/reservations/{id}/promote` returns order with code and promoted reservation.
- Created order DTO contains `promotedFromReservationId`.
- Generated order appears in order list/calendar.
- Reservation disappears from active reservation list/calendar.
- Capacity after promotion is counted once.
- Clinic cannot promote another clinic's reservation.
- Cancelled/promoted/expired reservation promotion returns an error.

### UI/manual tests

- Create reservation as clinic.
- Open reservation review, click `Promote to order`.
- Confirm generated code appears on success route.
- Return to root: reservation disappears from active list/calendar and order appears.
- Confirm capacity indicator/availability is unchanged except entity type changed.
- Confirm edit/save reservation still works without promotion.
- Confirm promotion button is hidden/disabled for normal orders and inactive reservations.

## Validation Plan

Run:

```bash
dotnet test Spark3Dent.sln --no-restore --verbosity quiet
node --check Web/wwwroot/js/orders-api.js
node --check Web/wwwroot/js/order-review-view.js
node --check Web/wwwroot/js/orders-page.js
```

Manual validation should prove promotion replacement capacity behavior.

## Review Notes for Implementing Agent

Include:

- migration name;
- promotion API shape;
- order/reservation fields added;
- code generation retry behavior;
- capacity exclusion/replacement explanation;
- whether promotion writes a deadline recommendation log and what impression timestamp was used;
- UI route after promotion;
- test and manual validation results.
