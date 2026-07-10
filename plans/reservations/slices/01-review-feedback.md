# Slice 1 Review Feedback - Active Reservation Capacity Hold MVP

Review of the Slice 1 reservation implementation against:

- `plans/reservations/slices/01-active-reservation-capacity-hold.md`
- `plans/reservations/v1-requirements.md`

Validation performed by reviewer:

```bash
dotnet test Spark3Dent.sln --no-restore --verbosity quiet
```

The full solution test command timed out at the harness limit while `Web.Tests` was still running, but all earlier projects had passed. `Web.Tests` was then run separately:

```bash
dotnet test Web.Tests/Web.Tests.csproj --no-restore --verbosity quiet
```

Result: passed, 156 tests.

Overall: the backend/domain foundation is solid, tests pass, and the slice is close. Please address the items below before we consider Slice 1 complete.

## 1. Impression date changes do not reload delivery availability

`Web/wwwroot/js/order-flow-view.js` now shows the reservation `impression` date input, but there does not appear to be a `change` listener for it.

Because reservation delivery scheduling is based on the selected impression date, changing the impression date must refresh the delivery calendar/minimum/recommendation.

Please add an impression-date change handler in reservation mode that:

- resets delivery selection if it may no longer be valid;
- resets any deadline override state;
- updates summary/dirty state;
- reloads date availability if currently on step 3;
- causes the delivery calendar to recompute from the new impression date.

This is important even in Slice 1 because the basic reservation flow already includes impression-date-based delivery availability.

## 2. Disable reservation delivery overrides for Slice 1

`SchedulingReservationService.DecideDeadlineCommitAsync` currently mirrors order behavior and allows lab actors to save invalid reservation delivery dates with `DeadlineOverrideRequest` confirmation/reason.

However, Slice 1 explicitly defers reservation override/recommendation logs to later slices. Allowing reservation overrides now creates unlogged scheduling bypasses.

For Slice 1, please disable reservation delivery overrides entirely:

- invalid reservation delivery date should be rejected for clinics;
- invalid reservation delivery date should also be rejected for lab users;
- do not accept `ConfirmDeadlineOverride` / `DeadlineOverrideReason` for reservations yet;
- leave full reservation override behavior to the later override/logging slice.

Do **not** implement reservation override logs as part of this fix unless the slice scope is deliberately expanded. Preferred resolution is to forbid overrides for reservations in Slice 1.

## 3. Active reservation list can hide valid reservations behind expired rows

`SqliteReservationRepo.ListActiveReservationsForActorAsync` loads `limit * 2` active-status rows, then filters expired reservations in memory:

```csharp
.Take(Math.Clamp(limit, 1, 500) * 2)
...
.Where(r => ReservationActiveRules.IsActiveForScheduling(r, nowUtc))
.Take(limit)
```

If many expired-but-still-`Active` reservations sort before valid active reservations, valid active reservations beyond this pre-filtered window will not appear.

Please fix this by either:

- pushing expiry filtering into the database query using the lab-local ignore cutoff semantics; or
- fetching/filtering in a loop until enough active non-expired rows are collected or there are no more rows.

The key requirement is that ordinary active views must not omit valid active reservations just because stale expired rows sort first.

## 4. Reservation route fallback paths can redirect to order routes

In `order-flow-view.js`, reservation route handlers appear to call `validOrderFlowStepOrReplace('new', ...)` or `validOrderFlowStepOrReplace('edit', ...)`.

That helper builds fallback routes like:

```text
new/1
edit/{id}/1
```

For invalid reservation route steps, this can redirect users from reservation routes into order routes.

Please make route fallback mode-aware so reservation routes fall back to:

```text
reservation-new/1
reservation-edit/{id}/1
```

## 5. Dirty-guard safe transition checks use `code`, not reservation `id`

`isSafeTransition` compares:

```js
from.params?.code === to.params?.code
```

Reservation edit routes use `id`, not `code`. This can make reservation edit transitions behave incorrectly in the dirty navigation guard.

Please update safe-transition logic to compare the correct route parameter based on route type:

- order edit route: compare `code`;
- reservation edit route: compare `id`;
- new order/new reservation routes should remain safe only within the same flow type as intended.

## 6. Switching between order and reservation flows can preserve stale form state

`showNew()` sets `reservationMode = false` before computing whether the form needs reset, and `showNewReservation()` sets `reservationMode = true` before computing whether the form needs reset.

This makes the previous-mode comparison ineffective and can preserve stale state when switching between `+ New order` and `+ Reservation`.

Please capture previous mode before changing `reservationMode`, and force a reset when moving between order and reservation flows.

Expected behavior:

- switching from order flow to reservation flow starts a clean reservation form;
- switching from reservation flow to order flow starts a clean order form;
- direct order creation remains unchanged.

## Scope reminders

The following are **not** required for Slice 1 and should not be added just to address this feedback:

- reservation promotion to order;
- polished same-calendar dual-date picker;
- reservation recommendation logs;
- reservation override logs;
- full reservation calendar delivery chips / impression dots beyond minimal active display;
- historical/cancelled/expired reservation page.

## Suggested validation after fixes

Run:

```bash
dotnet test Spark3Dent.sln --no-restore --verbosity quiet
```

If the full command exceeds local/harness timeout, run project-level tests and report all results, especially:

```bash
dotnet test Orders.Tests/Orders.Tests.csproj --no-restore --verbosity quiet
dotnet test Database.Tests/Database.Tests.csproj --no-restore --verbosity quiet
dotnet test Web.Tests/Web.Tests.csproj --no-restore --verbosity quiet
```

Manual browser checks to repeat:

1. Create a reservation, change impression date on step 3, and verify delivery availability/recommendation refreshes.
2. Try to save an invalid reservation delivery date as clinic and as lab; both should reject in Slice 1.
3. Switch between `+ New order` and `+ Reservation`; confirm forms do not leak stale state.
4. Visit invalid reservation step routes and confirm fallback stays in reservation routes.
5. Confirm active non-expired reservations still appear when expired active-status reservations exist in the database.
