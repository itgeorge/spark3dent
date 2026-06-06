# Slice 9 Plan — Orders List Cursor Paging and Find-by-Code Navigation

*Created: 2026-06-06*

## Goal

Improve the Scheduler orders browsing experience before larger identity/org work:

1. Add cursor paging to the orders **list view**.
2. Add a Find button/input that searches for an order by code on the server, navigates the UI to where that order belongs in the current display mode, and opens the order in View mode.

The list view should scale beyond the current fixed `limit=100`. The find flow should work from either list or calendar mode and should respect the same clinic/technician permissions as all other order reads.

## Current Baseline

Relevant current behavior:

- Real scheduler page: `Web/wwwroot/orders.html`.
- List endpoint: `GET /api/scheduling/orders?limit=100`.
- Calendar endpoint: `GET /api/scheduling/orders/calendar?start=YYYY-MM-DD&end=YYYY-MM-DD`.
- Order detail endpoint: `GET /api/scheduling/orders/{code}`.
- Clinic actors can access only their clinic's orders; technician actors can access all orders.
- List ordering is newest requested delivery date first, then newest created time/id first.
- Calendar excludes cancelled orders; list includes active and cancelled orders.
- Cancelled orders show in list, but delivery cell is currently displayed as `—` in list UI even though `requestedDeliveryDate` still exists on the DTO.

## Product Decisions

1. Cursor paging is for list view only. Calendar remains month/range based.
2. List view continues to include active and cancelled orders.
3. Calendar view continues to exclude cancelled orders.
4. Find by code should search exact order code on the server, with reasonable normalization for user-entered shortened codes if possible.
5. Find must not leak cross-clinic orders. A clinic searching another clinic's order should receive the same effective behavior as not found.
6. After find succeeds:
   - In list mode: show a list page/context where the order fits in sorted order, highlight it, then open the review view.
   - In calendar mode: navigate the calendar to the order's requested-delivery month/date, load that month, make the day/order visible, then open the review view.
7. If a found order is cancelled while in calendar mode, calendar cannot show it because cancelled orders are intentionally excluded. In that case, switch to list mode, show it in list context, highlight, then open review.
8. After review closes, the user should see the list/calendar context that was navigated to by the find operation.

## Backend Design

### Cursor paging endpoint

Extend the existing list endpoint:

```http
GET /api/scheduling/orders?limit=50&cursor=<opaque-cursor>
```

Response target:

```json
{
  "items": [ ...orderDtos ],
  "nextCursor": "opaque-or-null",
  "hasMore": true
}
```

Cursor requirements:

- Opaque to the browser.
- Encodes enough of the last item to continue the current sort:
  - `requestedDeliveryDate`
  - `createdAtUnixTimeMilliseconds` or `createdAt`
  - `id`
  - optionally `orderCode` as defensive tie-breaker if needed.
- Cursor must be validated. Invalid cursors should return `400` with a clear message.
- Limit should be clamped, e.g. default `50`, max `100` or `200`.
- Existing no-cursor request returns the first page.
- Role scoping remains enforced by actor in service/repository.

Sort order should remain:

1. `RequestedDeliveryDate` descending,
2. `CreatedAtUnixTimeMilliseconds` descending,
3. `Id` descending.

Cursor predicate for descending sort should be equivalent to:

```sql
WHERE RequestedDeliveryDate < cursorDelivery
   OR (RequestedDeliveryDate = cursorDelivery AND CreatedAtUnixTimeMilliseconds < cursorCreatedMs)
   OR (RequestedDeliveryDate = cursorDelivery AND CreatedAtUnixTimeMilliseconds = cursorCreatedMs AND Id < cursorId)
```

Fetch `limit + 1` rows to determine `hasMore` and `nextCursor`.

### Find endpoint

Add a dedicated server endpoint rather than making the client repeatedly page until an order is found.

Recommended endpoint:

```http
GET /api/scheduling/orders/find?code=<order-code>&limit=50
```

Response target:

```json
{
  "order": { ...orderDto },
  "listPage": {
    "items": [ ...orderDtos ],
    "nextCursor": "opaque-or-null",
    "hasMore": true
  },
  "listModeRecommended": false,
  "reason": null
}
```

Behavior:

- Auth required.
- Exact order lookup by full code and, if practical, shortened code.
- Applies same visibility rules as `GET /api/scheduling/orders/{code}`:
  - technician can find any order,
  - clinic can find only own orders,
  - non-owned and missing orders return `404`.
- `listPage` should contain the found order in sorted context. The simplest acceptable implementation is the first page whose cursor window contains the found order. A better implementation is an around-order context with some rows before and after the found order.
- If implementing a containing page is complex, return enough data for the UI to choose calendar navigation for active orders and direct review for found order, but the preferred UX is to show the list context for list mode and cancelled/calendar edge cases.
- If order is cancelled, include a flag/reason telling UI that calendar cannot show it, e.g. `listModeRecommended: true`, `reason: "Cancelled orders are only visible in list view."`.

Alternative acceptable design:

- `GET /api/scheduling/orders/{code}/context?limit=50`
- Same response semantics.

Document whichever route is chosen in the master plan.

### Repository/service additions

Likely additions:

```csharp
public sealed record OrderPage(IReadOnlyList<OrderRecord> Items, string? NextCursor, bool HasMore);

Task<OrderPage> ListOrdersPageForActorAsync(AuthenticatedActor actor, int limit, string? cursor, CancellationToken ct = default);
Task<OrderFindResult> FindOrderContextForActorAsync(AuthenticatedActor actor, string code, int limit, CancellationToken ct = default);
```

Repository can expose clinic/all variants or accept nullable clinic code:

```csharp
Task<OrderPage> ListOrdersPageAsync(string? clinicCode, int limit, OrderCursor? cursor, CancellationToken ct = default);
Task<OrderPage> ListOrdersPageContainingOrderAsync(string? clinicCode, OrderRecord target, int limit, CancellationToken ct = default);
```

Keep cursor encoding/decoding either in domain/service or repository, but keep route handlers thin.

### Shortened-code search

Order DTO exposes `shortenedOrderCode`. Users may type what they see.

Recommended behavior:

1. Try exact `OrderCode` match.
2. If not found and input does not contain the year prefix, support suffix search by shortened code cautiously:
   - `OrderCode.EndsWith(inputNormalized)`.
   - If multiple matches, return `409 Conflict` or `400` with “Multiple orders match this code; enter the full code.”
   - Apply actor visibility before deciding multiple/not found.

If shortened-code search is deferred, document that find requires full code for now. But the UI displays shortened codes, so supporting shortened codes is preferred.

## Frontend UX

### List view paging

In `Web/wwwroot/orders.html`:

- Replace fixed `GET /api/scheduling/orders?limit=100` with paged loading.
- Maintain state:

```js
let ordersNextCursor = null;
let ordersHasMore = false;
let ordersLoadingPage = false;
```

- Initial list load clears rows and fetches first page.
- Add a `Load more` button below the table, visible/enabled when `hasMore`.
- Refresh resets cursor and reloads first page.
- Creating/editing/cancelling should refresh the current view. For list mode, simplest is to reset to first page and highlight if the affected order is in that page; if not, use find/context for saved order when needed.

### Find control

Add near the list header actions, e.g. next to refresh/new order:

- `Find` button, opening compact inline popover/modal, or an input that expands.
- Input label/placeholder: `Order code`.
- Submit by Enter or Find button.
- Show not-found/permission-safe errors.
- Normalize whitespace/case.

### Find behavior in list mode

On find success:

1. Set orders view mode to `list` if current mode is list or if backend recommends list mode.
2. Render the returned `listPage.items` as the current list page/context.
3. Set next cursor from response.
4. Highlight the found row.
5. Open review with the found order.
6. When review closes, list remains at that context.

### Find behavior in calendar mode

On find success for non-cancelled orders:

1. Keep or switch to calendar mode.
2. Set `ordersCalendarMonth` to `requestedDeliveryDate` month.
3. Load that calendar range.
4. Ensure the order's day/card is present.
5. Open review.
6. When review closes, calendar remains at that month/date context.

For cancelled orders found while in calendar mode:

- Switch to list mode and use the returned list context because calendar intentionally excludes cancelled orders.
- Show a small message if useful: “Cancelled orders are shown in list view.”

### Navigation details

The review screen currently hides list/app and then restores list on close. Ensure close review does not reset the list/calendar context loaded by find.

## Files Expected to Change

- `Orders/Repositories.cs`
- `Orders/SchedulingOrderService.cs`
- possibly new order paging/cursor records in `Orders/*`
- `Database/SqliteOrderRepo.cs`
- `Web/SchedulingApi.cs`
- `Web/wwwroot/orders.html`
- tests:
  - `Orders.Tests/SchedulingOrderServiceTest.cs`
  - `Database.Tests/SqliteOrderRepoTest.cs`
  - `Web.Tests/SchedulingApiTests.cs`

No EF migration should be required.

## Tests to Add / Update

### Database.Tests

- First page returns newest delivery-date order first and `hasMore/nextCursor` when more than limit.
- Second page using cursor returns following rows with no duplicates.
- Cursor respects clinic filter.
- Invalid cursor handling if cursor decoding is in repo/service.
- Find/context page includes the target order and respects sort order.

### Orders.Tests

- Actor-scoped paged list for clinic vs technician.
- Find own clinic order succeeds.
- Clinic find other clinic order returns not found.
- Technician find any order succeeds.
- Find cancelled order succeeds and marks/list-recommends if that result model is used.
- Shortened code exact/ambiguous behavior if implemented in service.

### Web.Tests

- `GET /api/scheduling/orders?limit=...` returns `items`, `nextCursor`, `hasMore`.
- Cursor request returns next page.
- Invalid cursor returns `400`.
- Find endpoint returns order and list page/context.
- Clinic cannot find another clinic's order.
- Shortened code behavior if implemented.
- Cancelled find returns data suitable for list navigation.

### Browser smoke

Recommended:

1. Seed/create more orders than one page.
2. Login as clinic.
3. Verify first list page and Load more appends next page without duplicates.
4. Find an order not on the first page; list navigates to context, row highlights, review opens.
5. Switch to calendar mode; find an active order in another month; calendar navigates to that month and review opens.
6. Find a cancelled order while in calendar mode; UI switches to list context and opens review.

## Implementation Checklist

- [ ] Add order cursor/page records and cursor encode/decode helpers.
- [ ] Add repository cursor-paged list implementation with actor/clinic scoping.
- [ ] Add service methods for paged list and find/context.
- [ ] Add find endpoint and extend list endpoint with `cursor` response fields.
- [ ] Support shortened code search or document full-code-only behavior.
- [ ] Update list UI to initial page + Load more.
- [ ] Add Find UI and server-backed find flow.
- [ ] Implement list-mode find context rendering/highlight/review.
- [ ] Implement calendar-mode find navigation/review, with cancelled fallback to list mode.
- [ ] Preserve find-loaded context after review closes.
- [ ] Add/update database, service, and web tests.
- [ ] Run relevant tests/build.
- [ ] Run JS syntax checks.
- [ ] Run browser smoke for paging and find.
- [ ] Update master plan and this slice plan with completion notes.

## Out of Scope / Follow-Ups

- Full-text search by clinic/case/material/date.
- Filtering/sorting UI beyond current fixed sort.
- Infinite scroll; use explicit Load more for now.
- Calendar paging; calendar remains month/range based.
- Audit logging for read/find operations. Current audit scope is mutations only.

## Completion Notes

Fill in after implementation.

- Status:
- Files changed:
- Tests run:
- Manual checks:
- Cursor format notes:
- Find endpoint/short-code behavior:
- UI decisions:
- Follow-up discoveries:
