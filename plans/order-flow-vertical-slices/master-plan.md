# Order Flow Integration Vertical-Slice Master Plan

*Created: 2026-06-03*

This master plan tracks the integration of the order stepper prototype into the normal app flow using vertical slices. Each slice should be implementable by a fresh agent. After every slice, update this master file and the slice plan with completion notes, test evidence, and any discovered changes to later slices.

## Current Baseline

Existing relevant files/routes:

- Prototype order stepper: `Web/wwwroot/order-prototypes/stepper.html`
- Current walking-skeleton orders page: `Web/wwwroot/orders.html`
- Invoicer home page/UI: `Web/wwwroot/index.html`
- Orders API: `Web/SchedulingApi.cs`
- Invoicing/client API: `Web/Api.cs`
- Page routing/static serving: `Web/WebProgram.cs`
- Web embedded resources: `Web/Web.csproj`
- Orders domain/services:
  - `Orders/AuthenticatedActor.cs`
  - `Orders/ClinicConfig.cs`
  - `Orders/Enums.cs`
  - `Orders/OrderRecord.cs`
  - `Orders/Repositories.cs`
  - `Orders/SchedulingAuthService.cs`
  - `Orders/SchedulingOrderService.cs`
- Persistence:
  - `Database/Entities/SchedulingOrderEntity.cs`
  - `Database/SqliteOrderRepo.cs`
  - `Database/AppDbContext.cs`
  - `Database/Migrations/*`
- Tests:
  - `Orders.Tests/*`
  - `Database.Tests/SqliteOrderRepoTest.cs`
  - `Web` currently appears to have minimal/no route auth tests; add if practical.

## Product Decisions

1. `/orders` is the standalone scheduler URL.
2. Initially no UI navigation is added; scheduler is reached directly at `/orders`.
3. The scheduler list shows both active and cancelled orders with a simple status text.
4. Technician accounts can view all orders; clinic accounts can view only their own orders.
5. Invoicing/client routes should move from generic `/api/...` to `/api/invoicing/...` and be technician-only.
6. Non-technician users should not see the invoicer product in app navigation.
7. Cancelling an order uses `DELETE /api/scheduling/orders/{code}` as a soft delete: set status to `Cancelled`, do not physically delete.
8. Technician create/edit flow needs a clinic selector because technicians can create/modify for any clinic.
9. Audit logging is implemented after the main scheduler/invoicer auth and order modification flows are in place.

## Slice Index and Status

| Slice | Plan | Status | Summary |
|---|---|---:|---|
| 1 | `plans/order-flow-vertical-slices/slice-1-orders-list-create.md` | Not started | `/orders` auth + order list + add order create flow |
| 2 | `plans/order-flow-vertical-slices/slice-2-auth-roles-invoicing-gate.md` | Not started | Technician role + centralized auth + `/api/invoicing` route migration |
| 3 | `plans/order-flow-vertical-slices/slice-3-product-navigation.md` | Not started | Product switcher/topbar; hide invoicer from non-technicians |
| 4 | `plans/order-flow-vertical-slices/slice-4-edit-cancel.md` | Not started | Edit/cancel orders with permissions and technician clinic selector |
| 5 | `plans/order-flow-vertical-slices/slice-5-audit-log.md` | Not started | Audit log for scheduling/invoicing/client operations |

Statuses: `Not started`, `In progress`, `Blocked`, `Complete`, `Needs revision`.

## Mandatory Handoff Protocol After Each Slice

Before handing off to another agent:

1. Update this master plan:
   - set slice status,
   - add short completion summary,
   - list tests/build commands run,
   - document discovered issues or scope changes,
   - revise future slice assumptions if needed.
2. Update the completed slice plan:
   - check off completed tasks,
   - add exact files changed,
   - add test evidence,
   - add manual verification steps/results.
3. If later slices need adjustment:
   - edit the relevant slice plan directly,
   - add a note under `Cross-Slice Discoveries` below.
4. Keep route/API changes explicit in this file so future agents do not rely on stale endpoint names.

## Cross-Slice Dependencies

- Slice 1 intentionally uses current auth shape and should avoid large role/security changes.
- Slice 2 changes auth semantics and endpoint paths; it should update Slice 1 UI/API calls if needed.
- Slice 3 depends on Slice 2 actor role info from auth/me.
- Slice 4 depends on Slice 2 permissions and Slice 3 may already expose technician/clinic mode in UI.
- Slice 5 should audit the final operation names/endpoints from Slices 2-4.

## Cross-Slice Discoveries / Course Corrections

Append dated notes here after each slice.

- 2026-06-03: Initial planning docs created. No implementation started.

## Global Verification Commands

Use commands appropriate to the environment. Suggested defaults:

```bash
dotnet test
```

If full test suite is too slow/noisy, at minimum run affected test projects and document why full suite was skipped:

```bash
dotnet test Orders.Tests/Orders.Tests.csproj
dotnet test Database.Tests/Database.Tests.csproj
```

For Web compile checks:

```bash
dotnet build Web/Web.csproj
```

Manual browser checks should be documented per slice.

## Route Target State

Scheduling:

- `POST /api/scheduling/auth/login`
- `POST /api/scheduling/auth/logout`
- `GET /api/scheduling/auth/me`
- `POST /api/scheduling/dates`
- `GET /api/scheduling/orders`
- `POST /api/scheduling/orders`
- `GET /api/scheduling/orders/{code}`
- `PUT /api/scheduling/orders/{code}`
- `DELETE /api/scheduling/orders/{code}` soft-cancels

Invoicing/client after Slice 2:

- Existing `/api/clients...` and `/api/invoices...` move to `/api/invoicing/clients...` and `/api/invoicing/invoices...`.
- All `/api/invoicing/*` endpoints require technician auth.
- Update `Web/wwwroot/index.html` fetch calls accordingly.

## Files Most Likely to Change by Slice

Slice 1:

- `Web/wwwroot/orders.html`
- `Web/SchedulingApi.cs`
- `Orders/Repositories.cs`
- `Orders/SchedulingOrderService.cs`
- `Database/SqliteOrderRepo.cs`
- tests in `Orders.Tests` / `Database.Tests`

Slice 2:

- `Orders/AuthenticatedActor.cs`
- `Orders/ClinicConfig.cs`
- `Orders/SchedulingAuthService.cs`
- scheduling config JSON, likely `Web/scheduling.walking-skeleton.json`
- `Web/SchedulingApi.cs`
- `Web/Api.cs`
- `Web/WebProgram.cs`
- `Web/wwwroot/index.html`
- tests for auth/permissions/routes

Slice 3:

- `Web/wwwroot/index.html`
- `Web/wwwroot/orders.html`
- possibly shared topbar CSS/JS if extracted
- `Web/WebProgram.cs` if page access behavior changes

Slice 4:

- `Orders/Enums.cs`
- `Orders/OrderRecord.cs`
- `Orders/Repositories.cs`
- `Orders/SchedulingOrderService.cs`
- `Database/Entities/SchedulingOrderEntity.cs`
- `Database/SqliteOrderRepo.cs`
- EF migration files
- `Web/SchedulingApi.cs`
- `Web/wwwroot/orders.html`
- tests

Slice 5:

- new `Audit` or shared project/domain types if chosen
- `Database/Entities/*Audit*`
- `Database/AppDbContext.cs`
- migrations
- scheduling/invoicing/client services
- tests

## Open Questions to Resolve During Implementation

- Exact technician config shape: role on credential vs separate technician section. Current recommendation: role on credential for smallest change.
- Whether `/` unauthenticated/non-technician should redirect to `/orders` or show a small access-denied/login shell. Current preference: hide invoicer in navigation and block APIs; page behavior can be decided in Slice 2/3.
- Whether technician order creation chooses clinic before opening stepper or inside stepper. Current preference: select clinic before/above stepper in Slice 4.
- Whether audit log should be append-only DB table only or also file logging. Current preference: DB append-only; existing app logger can remain separate.
