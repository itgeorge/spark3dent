# Slice 6 Plan — Orders Calendar View

*Created: 2026-06-05*

## Goal

Add a calendar display mode for the scheduler orders list.

Calendar mode should show active/submitted orders grouped by requested delivery date in a month grid. It should reuse a newly extracted generic month-calendar component that also powers the order creation delivery-date picker.

## Product Decisions

1. Orders page has two display modes:
   - `calendar`
   - `list`
2. Default view mode:
   - lab/technician actors: `calendar`
   - clinic actors: `list`
3. User-selected view mode persists in `localStorage`.
4. Calendar view does **not** show cancelled orders. List view remains the place to see active and cancelled orders.
5. Calendar view uses a month grid on mobile too; do not switch to agenda-only mobile layout.
6. Small calendar cells aggregate orders into a clickable count card. Larger cells show some chips plus a `View all Y` control, where `Y` is the total order count for the day. Count card / `View all Y` opens a day popup listing all orders for that date.
7. Individual order chips and day popup rows open the existing order review flow.
8. Use a dedicated calendar API endpoint, not the existing list endpoint.
9. Before extracting the generic calendar component, rename the current order-flow delivery picker CSS/DOM classes to be delivery-specific so generic class names are free for the shared component.

## Dependencies

Requires completed Slices 1-4:

- real scheduler page in `Web/wwwroot/orders.html`,
- role-aware scheduling auth,
- existing order list/review/edit/cancel flow,
- `Cancelled` status,
- technician create target clinic selector.

This slice should be completed before larger auth/org/IAM restructuring if possible, but it should avoid making assumptions that prevent replacing the current `technician` role with a future `lab` organization role.

## API Target

Add a dedicated calendar endpoint:

```http
GET /api/scheduling/orders/calendar?start=2026-06-01&end=2026-07-05
```

Behavior:

- authenticated scheduling actor required,
- role-aware authorization:
  - clinic actor sees active orders for own clinic only,
  - technician/lab actor sees active orders for all clinics,
- filters by `RequestedDeliveryDate` inclusive range,
- excludes `Cancelled` orders,
- does not use arbitrary `limit=100`,
- validates `start <= end`,
- imposes a reasonable max range, e.g. 62 or 93 days, to avoid accidental huge queries,
- returns enough order summary data to render chips and open review by code.

Recommended response shape:

```json
{
  "start": "2026-06-01",
  "end": "2026-07-05",
  "days": [
    {
      "date": "2026-06-10",
      "orders": [
        {
          "orderCode": "...",
          "shortenedOrderCode": "...",
          "clinicCode": "DEMO",
          "clinicDisplayName": "Demo Clinic",
          "caseName": "...",
          "toothStart": 11,
          "toothEnd": 11,
          "shade": "A2",
          "requestedDeliveryDate": "2026-06-10",
          "status": "created"
        }
      ]
    }
  ]
}
```

Returning only days that have orders is acceptable if the client maps missing dates to empty arrays. Returning every requested date is also acceptable; document the chosen shape in completion notes.

## Backend Implementation Notes

Likely additions:

- Repository method in `Orders/Repositories.cs`, implemented in `Database/SqliteOrderRepo.cs`, e.g.:

```csharp
Task<IReadOnlyList<OrderRecord>> ListActiveOrdersForCalendarAsync(
    string? clinicCode,
    DateOnly start,
    DateOnly end,
    CancellationToken ct = default);
```

`clinicCode == null` can mean all clinics for technician/lab actors, or use separate service methods if clearer.

- Service method in `Orders/SchedulingOrderService.cs` that applies actor permissions:

```csharp
Task<IReadOnlyList<OrderRecord>> ListCalendarOrdersAsync(
    AuthenticatedActor actor,
    DateOnly start,
    DateOnly end,
    CancellationToken ct = default);
```

- API route in `Web/SchedulingApi.cs`:

```csharp
app.MapGet("/api/scheduling/orders/calendar", ...)
```

Important route ordering: map `/api/scheduling/orders/calendar` before `/api/scheduling/orders/{code}` so `calendar` is not captured as an order code.

## Frontend Implementation Plan

### Phase 0 — Delivery picker class-name refactor

Before extracting any generic component, make the existing delivery-date picker classes in `Web/wwwroot/orders.html` delivery-specific.

Current generic-ish classes include:

- `.calendar`
- `.calendar-head`
- `.dates`
- `.weekday`
- `.date-cell`
- `.datebtn`
- `.date-main`
- `.date-num`
- `.date-weekday`
- `.date-reason-pop`
- `.date-create-order`

Rename them to a delivery-specific namespace, for example:

- `.delivery-calendar`
- `.delivery-calendar-head`
- `.delivery-calendar-grid`
- `.delivery-calendar-weekday`
- `.delivery-calendar-cell`
- `.delivery-calendar-date`
- `.delivery-calendar-date-main`
- `.delivery-calendar-date-num`
- `.delivery-calendar-date-weekday`
- `.delivery-calendar-reason-pop`
- `.delivery-calendar-create-order`

Update JS selectors/class toggles accordingly. Keep behavior identical. Add or update tests/smoke before moving on if practical.

### Phase 1 — Extract generic month-calendar component

Create shared component assets, suggested names:

- `Web/wwwroot/js/month-calendar.js`
- `Web/wwwroot/css/month-calendar.css`

The generic component should own:

- calendar bounds calculation for a visible month,
- weekday header rendering,
- previous/next/current month controls if useful,
- outside-month metadata/classes,
- today marker if useful,
- date cell shell markup,
- cell renderer callback/hook for use-case-specific content,
- optional day popup positioning helper if it keeps code simple.

Use generic class names in the extracted component, such as:

- `.month-calendar`
- `.month-calendar-head`
- `.month-calendar-grid`
- `.month-calendar-weekday`
- `.month-calendar-cell`
- `.month-calendar-day-number`

Use use-case-specific content classes for the two consumers:

- delivery picker content: `.delivery-calendar-*`
- orders calendar content: `.orders-calendar-*`

The component should not know scheduling business rules. It should render dates and call callbacks.

### Phase 2 — Move delivery picker onto shared component

Refactor the stepper delivery date picker in `Web/wwwroot/orders.html` to use the extracted month-calendar component while preserving existing behavior:

- availability status from `POST /api/scheduling/dates`,
- disabled unavailable dates,
- reason tooltip/popover behavior,
- selected date styling,
- impression-day marker,
- previous/next month behavior,
- automatic delivery-date selection logic.

This phase should be behavior-preserving.

### Phase 3 — Orders calendar UI mode

In `Web/wwwroot/orders.html`:

- add view mode state:

```js
let ordersViewMode = loadOrdersViewMode(); // calendar | list
let ordersCalendarMonth = new Date(...);
let ordersCalendarDays = new Map();
```

- localStorage key suggestion:

```js
const ORDERS_VIEW_MODE_KEY = 's3d.orders.viewMode';
```

- default mode:

```js
actor?.isTechnician ? 'calendar' : 'list'
```

Note: when later replacing `technician` with `lab` organization role, this condition should become the lab/business-user predicate.

- add toggle buttons in the Orders card header:
  - `List`
  - `Calendar`

- when list mode is active:
  - keep current `loadOrders()` / table behavior,
  - continue showing cancelled orders.

- when calendar mode is active:
  - call new `GET /api/scheduling/orders/calendar?start=...&end=...`,
  - request the full visible grid bounds, including outside-month leading/trailing days,
  - exclude cancelled orders via API,
  - render month grid,
  - show month navigation,
  - show smart per-cell aggregation.

### Calendar cell rendering behavior

For each day:

- 0 orders: empty cell with date number.
- Very small cell: show one clickable aggregate card, e.g. `4 orders`.
- Larger cell: show some order chips plus `View all Y` if needed, where `Y` is the total order count for the day.
- Individual chip click: open existing `showReview(orderCode)`.
- Aggregate card / `View all Y` click: open day popup with all active orders for that date.

Implementation options for smart sizing:

1. CSS container queries if sufficient.
2. `ResizeObserver` to calculate a per-cell or per-grid `maxVisibleChips` value.
3. Conservative CSS-only fallback that shows aggregate cards on narrow screens and chips on wider screens.

Prefer a simple robust implementation over exact density optimization.

### Day popup

Day popup should show:

- date heading,
- all active orders for that date,
- clinic name for technician/lab actors,
- order code, case name, teeth, shade,
- each row clickable to open existing review.

Close on:

- explicit close/back button,
- Escape,
- outside click/backdrop if using modal/popover.

## Files Expected to Change

- `Web/wwwroot/orders.html`
- `Web/wwwroot/js/month-calendar.js` (new)
- `Web/wwwroot/css/month-calendar.css` (new)
- `Web/SchedulingApi.cs`
- `Orders/Repositories.cs`
- `Orders/SchedulingOrderService.cs`
- `Database/SqliteOrderRepo.cs`
- tests:
  - `Orders.Tests/SchedulingOrderServiceTest.cs`
  - `Database.Tests/SqliteOrderRepoTest.cs`
  - `Web.Tests/SchedulingApiTests.cs`
- `plans/order-flow-vertical-slices/master-plan.md`
- this plan file

If `Web/Web.csproj` has explicit static/embedded resource handling that needs new assets, update it. Static files under `wwwroot` are normally served by `UseStaticFiles`; `orders.html` remains embedded.

## Tests to Add/Update

Backend tests:

- calendar endpoint rejects unauthenticated request,
- clinic calendar endpoint returns only own active orders,
- technician/lab calendar endpoint returns active orders across clinics,
- cancelled orders are excluded,
- delivery-date range filtering is inclusive,
- invalid range returns 400,
- range larger than max returns 400 if max range is implemented,
- route `/api/scheduling/orders/calendar` does not get captured by `{code}` route.

Repository/service tests:

- active calendar query excludes `Cancelled`,
- query filters by requested delivery date, not created date,
- results sort predictably by delivery date then clinic/case/code or created order; document actual ordering.

Frontend/manual tests:

- default clinic view is list,
- default technician view is calendar,
- selected mode persists in `localStorage`,
- calendar month navigation fetches correct ranges,
- calendar chips open existing review,
- aggregate card / `View all Y` opens day popup,
- day popup rows open existing review,
- cancelled orders are absent from calendar but present in list,
- delivery-date picker still works after component extraction.

## Manual Verification

Clinic:

1. Clear `localStorage` key and login as clinic.
2. Confirm Orders defaults to list view.
3. Switch to calendar; confirm mode persists after refresh.
4. Confirm only active own orders appear in calendar.
5. Confirm cancelled orders do not appear in calendar but do appear in list.
6. Create/edit order and confirm delivery-date picker still works.

Technician/lab actor:

1. Clear `localStorage` key and login as technician.
2. Confirm Orders defaults to calendar view.
3. Confirm active orders from multiple clinics appear.
4. Navigate months and confirm data updates.
5. Click chip -> existing review opens.
6. Force multiple orders on one date; confirm aggregate/`View all Y` opens day popup.
7. Confirm list mode still shows cancelled orders.

Responsive:

1. Check targeted phone widths.
2. Confirm month grid remains visible.
3. Confirm small cells aggregate instead of overflowing badly.

## Implementation Checklist

- [x] Rename current delivery-picker calendar CSS/DOM classes to delivery-specific names without behavior changes.
- [x] Add shared generic month-calendar JS/CSS component.
- [x] Refactor delivery picker to use shared month-calendar component.
- [x] Add repository/service support for active calendar order range query.
- [x] Add `GET /api/scheduling/orders/calendar` before `{code}` route.
- [x] Add backend tests for auth, role scoping, date range, and cancelled exclusion.
- [x] Add list/calendar view-mode state and localStorage persistence.
- [x] Add mode toggle to orders header.
- [x] Implement technician/lab default calendar and clinic default list.
- [x] Implement calendar month navigation and API fetching.
- [x] Render calendar cells with chips/aggregate behavior.
- [x] Add day popup listing all active orders for a date.
- [x] Wire chips/popup rows to existing order review.
- [x] Verify list mode still includes cancelled orders.
- [x] Verify delivery-date picker still works via script/build checks; browser manual smoke not run in this handoff.
- [x] Run relevant tests/build and headless browser smoke.
- [x] Update master plan and this plan completion notes.

## Out of Scope

- Audit logging.
- Lab organization role restructuring.
- IAM/organization management.
- Drag/drop rescheduling.
- Capacity/load balancing.
- Showing cancelled orders in calendar.
- Agenda-only mobile mode.

## Completion Notes

- Status: Complete on 2026-06-05.
- Files changed: `Web/wwwroot/orders.html`, `Web/wwwroot/js/month-calendar.js`, `Web/wwwroot/css/month-calendar.css`, `Web/SchedulingApi.cs`, `Orders/Repositories.cs`, `Orders/SchedulingOrderService.cs`, `Database/SqliteOrderRepo.cs`, `Orders.Tests/SchedulingOrderServiceTest.cs`, `Database.Tests/SqliteOrderRepoTest.cs`, `Web.Tests/SchedulingApiTests.cs`, this plan, and `master-plan.md`.
- Tests run: `dotnet test Orders.Tests/Orders.Tests.csproj --no-restore` (54 passed); `dotnet test Database.Tests/Database.Tests.csproj --no-restore` (76 passed); `dotnet test Web.Tests/Web.Tests.csproj --no-restore` (93 passed); `dotnet build Web/Web.csproj --no-restore` passed; `node --check Web/wwwroot/js/month-calendar.js` passed; `node --check` on extracted `orders.html` inline script passed; full `dotnet test --no-restore` passed (Configuration 10, Storage 41, Orders 54, Accounting 61, Database 76, Invoices 251, Web 93).
- Manual checks: Headless Chromium smoke via CDP passed on a temp DB. Verified clinic defaults to list; list includes a cancelled order; switching to calendar persists in `localStorage`; calendar contains active orders and excludes cancelled orders; mobile width remains a 7-column month grid and shows aggregate count cards; aggregate count opens the day popup; popup row opens existing review; delivery-date picker renders through the shared component and auto-selects a date; technician login defaults to calendar.
- Calendar component API chosen: `window.MonthCalendar.create(root, { month, renderCell, onMonthChange, weekdays, titleFormatter, className })`, plus helpers `MonthCalendar.bounds`, `addDays`, `toIsoDate`, and `isSameMonth`. The component owns month bounds, header/nav, weekday headers, outside-month/weekend/today classes, and a cell shell; consumers render delivery-picker or orders-specific cell content.
- API response shape chosen: `{ start, end, days }`, returning only dates with active orders. Each day is `{ date, orders }`, and each order reuses the existing order DTO (`orderCode`, `shortenedOrderCode`, clinic fields, case, teeth, shade, `requestedDeliveryDate`, status, etc.).
- Discoveries affecting lab/IAM/audit slices: Current API tests seed an extra clinic order directly because the walking-skeleton web config only defines `DEMO`; future lab/org work should add realistic multi-clinic test config. The frontend still uses `actor.isTechnician` as the lab/business-user predicate as planned.
