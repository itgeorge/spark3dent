# Order Flow Integration Vertical-Slice Master Plan

*Created: 2026-06-03*

This master plan tracks the integration of the order stepper prototype into the normal app flow using vertical slices. Each slice should be implementable by a fresh agent. After every slice, update this master file and the slice plan with completion notes, test evidence, and any discovered changes to later slices.

## Current Baseline

Existing relevant files/routes:

- Real scheduler page: `Web/wwwroot/orders.html`, served at `/orders`.
- The old order stepper prototype is obsolete and scheduled for deletion in Slice 8.
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
13. Orders calendar view is a display mode of the scheduler list. It defaults to calendar for technician/lab actors and list for clinic actors, persists the selected mode in `localStorage`, excludes cancelled orders, and uses a dedicated calendar API endpoint.
14. Orders may contain multiple order work items on the same impression. Each work item has its own construction type and tooth/range; material and shade remain order-level for now. Slice 7 added JSON serialization and temporary single-selection compatibility fields.
15. Slice 8 should make `WorkItems` the sole source of truth, remove order-level `WorkType`/`ConstructionType`/`ToothStart`/`ToothEnd`, remove all abutment-related live code, wipe scheduling order rows via migration while preserving invoice/client data, and delete the obsolete stepper prototype.

## Slice Index and Status

| Slice | Plan | Status | Summary |
|---|---|---:|---|
| 1 | `plans/order-flow-vertical-slices/slice-1-orders-list-create.md` | Complete | Real `/orders` page served from embedded `orders.html`; clinic-scoped order list and create flow using stepper UX |
| 2 | `plans/order-flow-vertical-slices/slice-2-auth-roles-invoicing-gate.md` | Complete | Actor roles added; `/api/invoicing/*` technician-only; scheduler list is role-aware; old technician list route retired |
| 2.5 | `plans/order-flow-vertical-slices/slice-2.5-read-only-order-review.md` | Complete | Existing orders can be opened from the list in a read-only review view |
| 3 | `plans/order-flow-vertical-slices/slice-3-product-navigation.md` | Complete | Product switcher/topbars added and later extracted to shared AppChrome assets; `/` has technician login gate and redirects clinic users to Scheduler |
| 4 | `plans/order-flow-vertical-slices/slice-4-edit-cancel.md` | Complete | Edit/cancel orders with permissions; technician create supports target clinic selector |
| 5 | `plans/order-flow-vertical-slices/slice-5-audit-log.md` | Complete | Append-only DB audit log for scheduler create/update/cancel and invoicing/client mutations, plus technician read endpoint |
| 6 | `plans/order-flow-vertical-slices/slice-6-orders-calendar-view.md` | Complete | Calendar/list display mode with shared month-calendar component, dedicated active-orders calendar API, persisted mode preference, smart cell aggregation, and day popup |
| 7 | `plans/order-flow-vertical-slices/slice-7-multiple-order-work-items.md` | Complete | Multiple order work items per order/impression with JSON persistence, server validation, summed lead-time rules, API/display updates, and per-line tooth UI |
| 8 | `plans/order-flow-vertical-slices/slice-8-work-items-source-of-truth-cleanup.md` | Complete | `workItems` is now the sole order tooth/construction source across domain/API/persistence/UI; legacy single fields and live abutment code removed; scheduling rows/audits wiped by migration; obsolete prototype deleted |

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
- Slice 6 depends on Slice 4 order status/review behavior and should avoid assumptions that block the future lab-organization role refactor.
- Slice 7 depends on Slice 4 create/edit/review behavior, Slice 5 audit metadata, and Slice 6 list/calendar display surfaces.
- Slice 8 depends on Slice 7 work-item support and should simplify before lab organization/IAM work adds more identity complexity.

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
- 2026-06-05: Added Slice 6 plan for an orders calendar display mode. Calendar mode should use a dedicated `/api/scheduling/orders/calendar` endpoint, exclude cancelled orders, default to calendar for technician/lab actors and list for clinic actors, persist view mode in `localStorage`, and extract a generic month-calendar component after first renaming the current delivery picker calendar classes to delivery-specific names.
- 2026-06-05: Slice 6 complete. Added `GET /api/scheduling/orders/calendar?start=YYYY-MM-DD&end=YYYY-MM-DD` before the `{code}` route, with auth, role scoping, active-only filtering, inclusive delivery-date range, and 93-day max range. `orders.html` now has persisted List/Calendar mode; technician defaults to calendar, clinic defaults to list; list still shows cancelled orders while calendar excludes them. Delivery picker now uses shared `MonthCalendar` assets with delivery-specific classes.
- 2026-06-05: Added Slice 7 plan for multiple order work items per impression. The plan uses order-level material/shade, per-work-item construction and tooth range, JSON work-item serialization with legacy first-item fields retained, server-side no-overlap validation, summed per-item lead-time rules, and multi-item display updates across list/review/calendar/audit.
- 2026-06-05: Slice 7 complete. Added `OrderWorkItem` domain support with JSON `SchedulingOrders.WorkItemsJson`, legacy primary compatibility columns, mandatory per-item/no-overlap validation, summed per-item lead time, total-tooth order code counts, `workItems` API DTOs, audit metadata, and multi-item create/edit/list/review/calendar UI. Existing sequence-based FDI normalization still means some ranges normalize to jaw order (for example `11-13` becomes `13-11` in compatibility fields). Headless browser smoke passed after implementation.
- 2026-06-05: Added Slice 8 cleanup plan. Because there is no production order/API usage, Slice 8 should remove legacy single-work-item order fields, remove all live abutment-related code/tests, make `workItems` required in scheduling APIs, wipe scheduling order rows through migration while preserving invoice/client data, and delete the obsolete order stepper prototype.
- 2026-06-06: Slice 8 complete. `OrderDraft`/`OrderRecord` require work items and no longer carry order-level `WorkType`, `ConstructionType`, tooth range, abutments, or primary compatibility fields. Scheduling APIs require `workItems` and reject old single-field-only payloads with 400. SQLite migration `20260606000000_RemoveSchedulingOrderLegacyFields` deletes scheduling orders and scheduling-order audit rows, then recreates `SchedulingOrders` without legacy columns while preserving invoice/client data. The old `Web/wwwroot/order-prototypes/stepper.html` prototype was deleted.

## Verification Evidence

- 2026-06-03 Slices 1–2: `dotnet test Orders.Tests/Orders.Tests.csproj` passed (43 tests); `dotnet test Database.Tests/Database.Tests.csproj` passed (71 tests); `dotnet test Web.Tests/Web.Tests.csproj` passed (87 tests); `dotnet build Web/Web.csproj` succeeded with 0 warnings/errors; full `dotnet test` passed (Configuration 10, Storage 41, Orders 43, Accounting 61, Database 71, Invoices 251, Web 87).
- 2026-06-03 Slices 1–2: Headless Chromium browser evaluation passed 19/19 checks against a temp DB on `http://127.0.0.1:61234`: clinic `/orders` login/list/create/confirmation/back-to-list/logout; clinic 403 on `/api/invoicing/clients`; technician `/orders` list with create hidden/notice; role-aware scheduling list 200; retired technician route 404; technician `/` invoicer page and `/api/invoicing/clients` 200; legacy `/api/clients` and `/api/invoices` 404.
- 2026-06-03 Slice 2.5: `dotnet build Web/Web.csproj` passed; `dotnet test Web.Tests/Web.Tests.csproj` passed (87 tests); headless Chromium smoke passed for create order -> list -> View -> read-only review -> Back.
- 2026-06-04 Slice 3: `dotnet build Web/Web.csproj` passed; `dotnet test Web.Tests/Web.Tests.csproj --no-build` passed (87 tests); `node --check` passed for extracted inline scripts from `index.html` and `orders.html`; headless Chromium smoke passed for unauthenticated `/` login prompt, technician `/` login/product switcher/API access, technician `/orders` switcher, clinic `/orders` without Invoicer switcher, and clinic direct `/` redirect to `/orders`.
- 2026-06-04 Slice 4: `dotnet test Orders.Tests/Orders.Tests.csproj --no-restore` passed (50 tests); `dotnet test Database.Tests/Database.Tests.csproj --no-restore` passed (73 tests); `dotnet test Web.Tests/Web.Tests.csproj --no-restore` passed (88 tests); `dotnet build Web/Web.csproj --no-restore` passed; `node --check` passed for extracted `orders.html` inline script; headless Chromium smoke passed for clinic login, seeded order review, edit/save, cancel, and disabled cancelled actions.
- 2026-06-04 Slice 5: `dotnet test Orders.Tests/Orders.Tests.csproj --no-restore` passed (53 tests); `dotnet test Database.Tests/Database.Tests.csproj --no-restore` passed (75 tests); `dotnet test Web.Tests/Web.Tests.csproj --no-restore` passed (91 tests); full `dotnet test --no-restore` passed (Configuration 10, Storage 41, Orders 53, Accounting 61, Database 75, Invoices 251, Web 91); `dotnet build Web/Web.csproj --no-restore` passed.
- 2026-06-05 Slice 6: `dotnet test Orders.Tests/Orders.Tests.csproj --no-restore` passed (54 tests); `dotnet test Database.Tests/Database.Tests.csproj --no-restore` passed (76 tests); `dotnet test Web.Tests/Web.Tests.csproj --no-restore` passed (93 tests); `dotnet build Web/Web.csproj --no-restore` passed; `node --check Web/wwwroot/js/month-calendar.js` and `node --check` on extracted `orders.html` inline script passed; full `dotnet test --no-restore` passed (Configuration 10, Storage 41, Orders 54, Accounting 61, Database 76, Invoices 251, Web 93); headless Chromium/CDP smoke passed for clinic default list, cancelled-present list, active-only calendar, `localStorage` persistence, mobile 7-column month grid/count aggregation/day popup, popup row review, delivery picker shared component render, and technician default calendar.
- 2026-06-05 Slice 7: `dotnet test Orders.Tests/Orders.Tests.csproj --no-restore` passed (60 tests); `dotnet test Database.Tests/Database.Tests.csproj --no-restore` passed (78 tests); `dotnet test Web.Tests/Web.Tests.csproj --no-restore` passed (96 tests); `dotnet build Web/Web.csproj --no-restore` passed; `node --check` on extracted `orders.html` inline script passed; headless Chromium/CDP smoke passed on a temp DB at `http://127.0.0.1:61247` for clinic login, multi-item create, locked-tooth prevention, overview/confirmation/list/review displays, edit latest work item, calendar day popup, and popup-to-review.
- 2026-06-06 Slice 8: `dotnet test Orders.Tests/Orders.Tests.csproj --no-restore -p:UseSharedCompilation=false` passed (58 tests); `dotnet test Database.Tests/Database.Tests.csproj --no-restore -p:UseSharedCompilation=false` passed (78 tests); `dotnet test Web.Tests/Web.Tests.csproj --no-restore -p:UseSharedCompilation=false` passed (97 tests); `dotnet build Web/Web.csproj --no-restore -p:UseSharedCompilation=false` passed; `node --check` on extracted `orders.html` inline script passed. Browser smoke was not run for this slice.

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
- `GET /api/scheduling/orders/calendar?start=YYYY-MM-DD&end=YYYY-MM-DD` (active orders only, role-aware, inclusive delivery-date range, 93-day max range)

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

Slice 6:

- `Web/wwwroot/orders.html`
- `Web/wwwroot/js/month-calendar.js` (planned)
- `Web/wwwroot/css/month-calendar.css` (planned)
- `Web/SchedulingApi.cs`
- `Orders/Repositories.cs`
- `Orders/SchedulingOrderService.cs`
- `Database/SqliteOrderRepo.cs`
- tests in `Orders.Tests` / `Database.Tests` / `Web.Tests`

Slice 7:

- `Orders/OrderDraft.cs`
- `Orders/OrderRecord.cs`
- new `Orders/OrderWorkItem.cs` or equivalent
- `Orders/ToothRange.cs`
- `Orders/SchedulingOrderService.cs`
- `Orders/DescriptiveOrderCodeGenerator.cs`
- `Database/Entities/SchedulingOrderEntity.cs`
- `Database/SqliteOrderRepo.cs`
- `Database/Migrations/*`
- `Web/SchedulingApi.cs`
- `Web/wwwroot/orders.html`
- tests in `Orders.Tests` / `Database.Tests` / `Web.Tests`

Slice 8:

- `Orders/OrderDraft.cs`
- `Orders/OrderRecord.cs`
- `Orders/OrderWorkItem.cs`
- `Orders/ToothRange.cs`
- `Orders/SchedulingOrderService.cs`
- `Orders/DescriptiveOrderCodeGenerator.cs`
- `Database/Entities/SchedulingOrderEntity.cs`
- `Database/SqliteOrderRepo.cs`
- `Database/Migrations/*`
- `Web/SchedulingApi.cs`
- `Web/wwwroot/orders.html`
- delete `Web/wwwroot/order-prototypes/stepper.html`
- tests in `Orders.Tests` / `Database.Tests` / `Web.Tests`

## Open Questions to Resolve During Implementation

- Exact technician config shape remains a future cleanup: current implementation uses role-on-credential, with the demo technician credential under clinic `DEMO` and PIN `654321` as a walking-skeleton convention.
- `/` access behavior is resolved for v1: client-side scheduling-auth gate shows login when unauthenticated and redirects clinic users to `/orders`; `/api/invoicing/*` remains the server-side security boundary.
- Technician order creation target selection is resolved for v1: technician create uses the `GET /api/scheduling/clinics` list and a target clinic selector above the stepper; existing order clinic reassignment is not supported.
- Audit log storage is resolved for v1: append-only SQLite `AuditEvents` table via `IAuditLog`; existing app/file logger remains separate. A technician-only read endpoint exists for inspection, but no UI browser or tamper-proof hash chain is implemented.
- Per-work-item material/shade is a known follow-up after Slice 7; Slice 7 keeps material and shade order-level while allowing multiple construction/tooth work items.
- Abutment support is no longer desired in the order flow and should be removed from live code/tests/persistence mapping in Slice 8.
