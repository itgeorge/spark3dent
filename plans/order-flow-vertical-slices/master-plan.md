# Order Flow Integration Vertical-Slice Master Plan

*Created: 2026-06-03*

This master plan tracks the integration of the order stepper prototype into the normal app flow using vertical slices. Each slice should be implementable by a fresh agent. After every slice, update this master file and the slice plan with completion notes, test evidence, and any discovered changes to later slices.

## Current Baseline

Existing relevant files/routes:

- Prototype order stepper retained for reference: `Web/wwwroot/order-prototypes/stepper.html`
- Real scheduler page: `Web/wwwroot/orders.html`, served at `/orders`.
- Invoicer home page/UI: `Web/wwwroot/index.html`, served at `/`.
- Shared app topbar/menu assets:
  - `Web/wwwroot/js/app-chrome.js`
  - `Web/wwwroot/css/app-chrome.css`
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
10. Slice 5 audit boundary: audit contracts (`AuditEvent`, `IAuditLog`) live in `Utilities` to avoid a new project; SQLite persistence lives in `Database`; scheduler logs at service level; invoicing/client logs in route handlers until a fuller app-service layer exists.
11. A simple technician-only audit read endpoint exists at `GET /api/invoicing/audit` for inspection/manual verification.
12. A CLI audit listing command exists for operator inspection/export: `audit list [filters]`, including `--json` and filters such as `--service`, `--operation`, `--entity-type`, `--entity-id`, `--actor-role`, `--actor-clinic`, `--since`, `--until`, `--limit`, and `--db`.

## Slice Index and Status

| Slice | Plan | Status | Summary |
|---|---|---:|---|
| 1 | `plans/order-flow-vertical-slices/slice-1-orders-list-create.md` | Complete | Real `/orders` page served from embedded `orders.html`; clinic-scoped order list and create flow using stepper UX |
| 2 | `plans/order-flow-vertical-slices/slice-2-auth-roles-invoicing-gate.md` | Complete | Actor roles added; `/api/invoicing/*` technician-only; scheduler list is role-aware; old technician list route retired |
| 2.5 | `plans/order-flow-vertical-slices/slice-2.5-read-only-order-review.md` | Complete | Existing orders can be opened from the list in a read-only review view |
| 3 | `plans/order-flow-vertical-slices/slice-3-product-navigation.md` | Complete | Product switcher/topbars added and later extracted to shared AppChrome assets; `/` has technician login gate and redirects clinic users to Scheduler |
| 4 | `plans/order-flow-vertical-slices/slice-4-edit-cancel.md` | Complete | Edit/cancel orders with permissions; technician create supports target clinic selector |
| 5 | `plans/order-flow-vertical-slices/slice-5-audit-log.md` | Complete | Append-only DB audit log for scheduler create/update/cancel and invoicing/client mutations, plus technician read endpoint |

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
- 2026-06-03: Slice 1 complete. Added clinic-scoped `GET /api/scheduling/orders`, repository/service clinic listing, and a real embedded `/orders` page with login, list, `+ New order`, stepper create flow, confirmation, and return-to-list.
- 2026-06-03: Slice 2 complete. Added `ActorRole` on scheduling credentials/actors. Demo technician credential uses clinic `DEMO` with PIN `654321`; this is a temporary walking-skeleton convention and should be restructured after the main flows are complete. Invoicing/client APIs moved to `/api/invoicing/*` and require technician role. Legacy `/api/clients*`, `/api/invoices*`, and `/api/scheduling/technician/orders` are retired/404. Technician order creation is blocked until Slice 4 adds target clinic selection.
- 2026-06-03: Slice 2.5 complete. `Web/wwwroot/orders.html` now has list row/View behavior that fetches `GET /api/scheduling/orders/{code}` and displays a simplified read-only step-5-style review as a modal over a blurred backdrop. It closes via the top Back to orders button, Escape, or backdrop click. Slice 4 should add Edit/Cancel buttons to the review header.
- 2026-06-03: Scheduler order lists now sort by expected/requested delivery date descending, with newest-created first only as a tie-breaker within the same delivery date.
- 2026-06-04: Slice 3 complete. `/` now uses a client-side scheduling-auth gate before initializing invoicing data; non-technician authenticated users are redirected to `/orders`. Product switchers own Scheduler/Invoicer navigation; settings no longer contains Scheduler.
- 2026-06-04: Slice 4 complete. Added `Cancelled` status, `PUT /api/scheduling/orders/{code}`, `DELETE /api/scheduling/orders/{code}` soft-cancel, and technician-only `GET /api/scheduling/clinics`. Clinics can edit/cancel only own non-cancelled orders; non-owned direct access returns 404. Technicians can edit/cancel any order and create orders by selecting target clinic. Existing order clinic reassignment is not supported.
- 2026-06-04: Post-slice UI polish extracted shared app chrome/topbar behavior to `Web/wwwroot/js/app-chrome.js` and `Web/wwwroot/css/app-chrome.css`, used by both `index.html` and `orders.html`. Product switcher and logout now live in the hamburger menu, with settings remaining as an invoicer-specific extra action.
- 2026-06-04: Slice 5 complete. Added append-only `AuditEvents` table/repository and `Utilities` audit contracts. Scheduler create/update/cancel logs happen in `SchedulingOrderService` after persistence and explicitly include acting actor fields separate from target clinic metadata. Invoicing/client route handlers log client create/update/rename, invoice issue/correct, and non-dry-run import commit after successful mutation. Added technician-only `GET /api/invoicing/audit` for inspection.
- 2026-06-04: Post-slice audit inspection polish added CLI support for `audit list [filters]`, with table or JSON output and filters for service, operation, entity, actor, date range, limit, and database path.

## Verification Evidence

- 2026-06-03 Slices 1–2: `dotnet test Orders.Tests/Orders.Tests.csproj` passed (43 tests); `dotnet test Database.Tests/Database.Tests.csproj` passed (71 tests); `dotnet test Web.Tests/Web.Tests.csproj` passed (87 tests); `dotnet build Web/Web.csproj` succeeded with 0 warnings/errors; full `dotnet test` passed (Configuration 10, Storage 41, Orders 43, Accounting 61, Database 71, Invoices 251, Web 87).
- 2026-06-03 Slices 1–2: Headless Chromium browser evaluation passed 19/19 checks against a temp DB on `http://127.0.0.1:61234`: clinic `/orders` login/list/create/confirmation/back-to-list/logout; clinic 403 on `/api/invoicing/clients`; technician `/orders` list with create hidden/notice; role-aware scheduling list 200; retired technician route 404; technician `/` invoicer page and `/api/invoicing/clients` 200; legacy `/api/clients` and `/api/invoices` 404.
- 2026-06-03 Slice 2.5: `dotnet build Web/Web.csproj` passed; `dotnet test Web.Tests/Web.Tests.csproj` passed (87 tests); headless Chromium smoke passed for create order -> list -> View -> read-only review -> Back.
- 2026-06-04 Slice 3: `dotnet build Web/Web.csproj` passed; `dotnet test Web.Tests/Web.Tests.csproj --no-build` passed (87 tests); `node --check` passed for extracted inline scripts from `index.html` and `orders.html`; headless Chromium smoke passed for unauthenticated `/` login prompt, technician `/` login/product switcher/API access, technician `/orders` switcher, clinic `/orders` without Invoicer switcher, and clinic direct `/` redirect to `/orders`.
- 2026-06-04 Slice 4: `dotnet test Orders.Tests/Orders.Tests.csproj --no-restore` passed (50 tests); `dotnet test Database.Tests/Database.Tests.csproj --no-restore` passed (73 tests); `dotnet test Web.Tests/Web.Tests.csproj --no-restore` passed (88 tests); `dotnet build Web/Web.csproj --no-restore` passed; `node --check` passed for extracted `orders.html` inline script; headless Chromium smoke passed for clinic login, seeded order review, edit/save, cancel, and disabled cancelled actions.
- 2026-06-04 Slice 5: `dotnet test Orders.Tests/Orders.Tests.csproj --no-restore` passed (53 tests); `dotnet test Database.Tests/Database.Tests.csproj --no-restore` passed (75 tests); `dotnet test Web.Tests/Web.Tests.csproj --no-restore` passed (91 tests); full `dotnet test --no-restore` passed (Configuration 10, Storage 41, Orders 53, Accounting 61, Database 75, Invoices 251, Web 91); `dotnet build Web/Web.csproj --no-restore` passed.

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
- `GET /api/scheduling/clinics` (technician-only active clinic list for target selection)
- `GET /api/scheduling/orders` (clinic actors see own clinic; technician actors see all)
- `POST /api/scheduling/orders`
- `GET /api/scheduling/orders/{code}`
- `PUT /api/scheduling/orders/{code}`
- `DELETE /api/scheduling/orders/{code}` soft-cancels

Invoicing/client after Slice 2:

- Existing `/api/clients...` and `/api/invoices...` moved to `/api/invoicing/clients...` and `/api/invoicing/invoices...`; old paths now 404.
- All `/api/invoicing/*` endpoints require technician auth.
- `GET /api/invoicing/audit?entityType=&entityId=&limit=100` returns recent audit events for technician inspection.
- CLI: `audit list [filters]` lists audit events newest-first for operator inspection/export; use `--json` for machine-readable output.
- Update `Web/wwwroot/index.html` fetch calls accordingly.

## Files Most Likely to Change by Slice

Slice 1:

- `Web/wwwroot/orders.html` (create new real scheduler page)
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
- `Web/wwwroot/js/app-chrome.js`
- `Web/wwwroot/css/app-chrome.css`
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

- Exact technician config shape remains a future cleanup: current implementation uses role-on-credential, with the demo technician credential under clinic `DEMO` and PIN `654321` as a walking-skeleton convention.
- `/` access behavior is resolved for v1: client-side scheduling-auth gate shows login when unauthenticated and redirects clinic users to `/orders`; `/api/invoicing/*` remains the server-side security boundary.
- Technician order creation target selection is resolved for v1: technician create uses the `GET /api/scheduling/clinics` list and a target clinic selector above the stepper; existing order clinic reassignment is not supported.
- Audit log storage is resolved for v1: append-only SQLite `AuditEvents` table via `IAuditLog`; existing app/file logger remains separate. A technician-only read endpoint exists for inspection, but no UI browser or tamper-proof hash chain is implemented.
