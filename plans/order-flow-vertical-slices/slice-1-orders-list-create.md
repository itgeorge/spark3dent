# Slice 1 Plan — `/orders` Auth + Order List + Add Order Create Flow

*Created: 2026-06-03*

## Goal

Deliver a first end-to-end scheduler page at `/orders` with:

1. scheduling login,
2. submitted orders list,
3. simple status text,
4. `+ New order` button,
5. order creation using the current stepper prototype UX.

This slice should avoid technician-role and invoicing-security changes. Those are Slice 2.

## User-Visible Flow

1. User navigates directly to `/orders`.
2. App calls `GET /api/scheduling/auth/me`.
3. If unauthenticated, show login.
4. After login, show submitted orders list.
5. List rows/cards show at least:
   - order code / shortened code,
   - case name,
   - teeth range,
   - shade,
   - delivery date,
   - status text.
6. User clicks `+ New order`.
7. Stepper create flow starts.
8. User creates order.
9. Confirmation/review is shown, with a way back to list.
10. Created order appears in list.

## Backend Scope

### Add clinic-scoped list endpoint

Add:

```http
GET /api/scheduling/orders?limit=100
```

Behavior for Slice 1:

- requires existing scheduling auth cookie,
- returns orders only for `actor.ClinicCode`,
- sorted newest first,
- includes all statuses currently available (`Created` only at this point),
- keeps existing `/api/scheduling/technician/orders` unchanged until Slice 2.

### Suggested domain/repository changes

Current:

- `Orders/Repositories.cs`
  - `ListOrdersAsync(int limit = 100, ...)` returns all orders.
- `Orders/SchedulingOrderService.cs`
  - `ListOrdersAsync(...)` forwards to repository.
- `Database/SqliteOrderRepo.cs`
  - `ListOrdersAsync(...)` returns all orders.

Add:

```csharp
Task<IReadOnlyList<OrderRecord>> ListOrdersForClinicAsync(string clinicCode, int limit = 100, CancellationToken ct = default);
```

Then add service wrapper:

```csharp
Task<IReadOnlyList<OrderRecord>> ListOrdersForClinicAsync(string clinicCode, int limit = 100, CancellationToken ct = default);
```

Route should use actor clinic code, never accept clinic code from query in this slice.

## Frontend Scope

Replace or substantially rewrite `Web/wwwroot/orders.html` as a scheduler page composed from:

- auth/login behavior from existing `orders.html`,
- stepper UX from `Web/wwwroot/order-prototypes/stepper.html`,
- list/card visual patterns from `Web/wwwroot/index.html`.

Do not add invoicer navigation yet.

### Recommended view model

Use simple state:

```js
let view = 'login'; // login | list | create | confirmation
let actor = null;
let orders = [];
```

Recommended functions:

- `loadMe()`
- `login()`
- `logout()` optional but useful
- `loadOrders()`
- `renderList()`
- `startCreateOrder()`
- `createOrder()` adapted from stepper
- `showConfirmation(order)`
- `backToList()`

### Stepper integration notes

`stepper.html` is a prototype and has create-only assumptions:

- it hides list/app state,
- it calls `POST /api/scheduling/orders`,
- it shows step 5 confirmation after creation.

For this slice, acceptable implementation options:

1. copy/adapt the prototype into `orders.html`, or
2. refactor within `orders.html` only, without extracting shared JS yet.

Do not over-invest in reusable modules in Slice 1 unless it makes the implementation smaller.

## Files Expected to Change

- `Web/SchedulingApi.cs`
- `Orders/Repositories.cs`
- `Orders/SchedulingOrderService.cs`
- `Database/SqliteOrderRepo.cs`
- `Web/wwwroot/orders.html`
- `Web/Web.csproj` only if embedded resource declarations change, likely no change because `orders.html` is already embedded.
- Tests:
  - `Database.Tests/SqliteOrderRepoTest.cs`
  - `Orders.Tests/SchedulingOrderServiceTest.cs` if service method is tested
  - add Web route tests if there is an existing pattern; otherwise document manual API verification.

## Tests to Add/Update

Minimum backend tests:

- repository clinic list returns only matching clinic orders,
- repository clinic list sorts newest first,
- endpoint/service does not accept arbitrary clinic code for clinic-scoped list.

If route-level tests are practical:

- unauthenticated `GET /api/scheduling/orders` returns 401,
- authenticated clinic gets only own orders.

## Manual Verification

1. Run app.
2. Open `/orders`.
3. Confirm login appears if not authenticated.
4. Login using demo clinic credentials.
5. Confirm list renders.
6. Click `+ New order`.
7. Complete stepper.
8. Confirm order code appears.
9. Return to list.
10. Confirm new order appears with status text.
11. Refresh browser and confirm session/list still works.

## Out of Scope

- Technician role.
- Invoicing route protection.
- App switcher/topbar navigation.
- Edit mode.
- Cancellation.
- Audit log.
- Technician clinic selection.

## Implementation Checklist

- [ ] Add repository method for clinic-scoped order listing.
- [ ] Add service method for clinic-scoped order listing.
- [ ] Add `GET /api/scheduling/orders` route.
- [ ] Add/update tests for clinic-scoped listing.
- [ ] Replace `/orders` UI with login/list/create flow.
- [ ] Adapt stepper create flow into `/orders`.
- [ ] Add simple status text in list.
- [ ] Ensure create returns to or can navigate back to list.
- [ ] Run relevant tests/build.
- [ ] Manually verify `/orders` end-to-end.
- [ ] Update `master-plan.md` with status/discoveries.

## Completion Notes

Fill in after implementation.

- Status:
- Files changed:
- Tests run:
- Manual checks:
- Discoveries affecting later slices:
