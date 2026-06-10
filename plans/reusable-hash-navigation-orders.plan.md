---
name: reusable-hash-navigation-orders
overview: Add reusable hash-fragment routing infrastructure and wire it into the Scheduler orders page for root/list, order review, new-order steps, and edit-order steps, without making list/calendar view mode part of navigation.
todos:
  - id: add-reusable-hash-router
    content: Add a framework-agnostic browser hash router utility under Web/wwwroot/js for reuse by Orders, IAM, Invoices, etc.
    status: pending
  - id: centralize-orders-screen-transitions
    content: Refactor orders.html screen transition entry points just enough to route through root/review/new/edit states.
    status: pending
  - id: wire-orders-routes
    content: Wire orders routes for empty root, #order/:code, #new/:step, and #edit/:code/:step with validation and async loading.
    status: pending
  - id: handle-dirty-flow-navigation
    content: Add guarded hash navigation for unsaved new/edit order flow changes, including browser back/forward handling.
    status: pending
  - id: update-clicks-and-stepper-to-navigate
    content: Change relevant buttons/row clicks/step buttons to update routes instead of directly switching screens.
    status: pending
  - id: tests-and-smoke
    content: Add focused reusable router tests where feasible plus browser/manual smoke coverage for orders hash navigation.
    status: pending
isProject: false
---

# Reusable Hash Navigation Plan for Orders

## Decision / Scope

Implement browser navigation **now**, before decomposing `Web/wwwroot/orders.html` into reusable UI components.

The routing layer should be reusable across pages later (IAM, Invoices, etc.), but this slice should only wire it into the orders page.

## Important Product Constraints

- The orders page already lives at `/orders`; do **not** introduce `#orders`.
- The root orders state is represented by either:
  - no fragment: `/orders`
  - or empty fragment: `/orders#`
- Orders list/calendar **view mode is not navigation**:
  - Keep it as UI state using the existing `ORDERS_VIEW_MODE_KEY` / localStorage behavior.
  - Do not create `#list`, `#calendar`, `#/list`, or `#/calendar` routes.
- Preserve existing auth behavior: unauthenticated users see login; authenticated users see the route target when possible.
- Preserve existing unsaved-change protection for the order flow.
- Avoid a broad component rewrite. Only refactor enough to make route handlers clean and reliable.

## Proposed Route Grammar

Use hash fragments relative to `/orders`:

| Route | Meaning |
| --- | --- |
| empty / `#` | Orders root/list screen using current saved list/calendar view option |
| `#order/:code` | Read-only order overview/review |
| `#new/:step` | New order flow at step 1-4 only |
| `#created/:code` | Post-create confirmation screen with package-code instructions |
| `#edit/:code/:step` | Edit existing order flow at step 1-4 |

Examples:

- `/orders`
- `/orders#`
- `/orders#order/26-2905-M1AB`
- `/orders#new/1`
- `/orders#new/3`
- `/orders#created/26-2905-M1AB`
- `/orders#edit/26-2905-M1AB/2`

### Route Normalization

- Treat `#/new/1` and `#new/1` consistently if cheap, but generate `#new/1` in app code.
- Clamp/validate steps:
  - invalid new/edit steps should redirect/replace to a valid step, usually step 1 or root.
  - future steps must respect existing `completedSteps`, `validateStep`, and `canGoToStep` rules.
- Order codes should be URL-encoded when generated and decoded in handlers.

## Reusable Router Utility

Add a plain-browser JS utility, likely:

- `Web/wwwroot/js/hash-router.js`

Expose a small global namespace because this project currently uses no frontend bundler:

```js
window.HashRouter = { createHashRouter };
```

Target API shape (implementation can vary, but keep it generic):

```js
const router = HashRouter.createHashRouter({
  routes: [
    { name: 'root', pattern: '', handler: ctx => {} },
    { name: 'orderReview', pattern: 'order/:code', handler: ctx => {} },
    { name: 'newOrder', pattern: 'new/:step', handler: ctx => {} },
    { name: 'createdOrder', pattern: 'created/:code', handler: ctx => {} },
    { name: 'editOrder', pattern: 'edit/:code/:step', handler: ctx => {} }
  ],
  beforeLeave: async (from, to) => true,
  notFound: ctx => {},
  onError: (err, ctx) => {}
});

router.start();
router.navigate('order/26-2905-M1AB');
router.replace('');
router.current();
router.build('edit/:code/:step', { code, step: 2 }); // optional helper
```

Router requirements:

- Framework agnostic; no dependencies on orders-specific globals or DOM ids.
- Supports:
  - `start()` / `stop()`
  - parsing current `location.hash`
  - programmatic `navigate(path)` and `replace(path)`
  - `hashchange` handling
  - async route handlers
  - optional route guards / `beforeLeave`
  - simple `:param` path segments
  - query strings are not required for this slice, but do not design in a way that prevents them later.
- Avoid infinite loops when a guard rejects a browser back/forward hashchange.
- Track an internal `isReplacing` / `isNavigating` flag if needed.
- Prefer small and well-tested over feature-rich.

## Orders Page Refactor Targets

Current relevant functions in `Web/wwwroot/orders.html` include:

- `showLogin()`
- `showList()`
- `startCreateOrder()`
- `backToList()` / `requestBackToList()`
- `showReview(codeToOpen)`
- `closeReview()`
- `fillFormFromOrder(o)`
- `render()` / stepper handling
- `hasUnsubmittedOrderFlowChanges()` and discard modal functions

Refactor only enough to distinguish **state transition intent** from **route mutation**.

Suggested split:

```js
async function showOrdersRootFromRoute() { ... }      // no hash mutation
async function showReviewFromRoute(code) { ... }     // no hash mutation
async function showNewOrderFromRoute(step) { ... }   // no hash mutation
async function showCreatedOrderFromRoute(code) { ... } // no hash mutation
async function showEditOrderFromRoute(code, step) { ... } // no hash mutation

function goOrdersRoot(opts) { router.navigate('', opts); }
function goOrderReview(code, opts) { router.navigate(`order/${encodeURIComponent(code)}`, opts); }
function goNewOrder(step = 1, opts) { router.navigate(`new/${step}`, opts); }
function goCreatedOrder(code, opts) { router.navigate(`created/${encodeURIComponent(code)}`, opts); }
function goEditOrder(code, step = 1, opts) { router.navigate(`edit/${encodeURIComponent(code)}/${step}`, opts); }
```

The `show*FromRoute` functions should perform the existing DOM show/hide/fetch/render work, but should not call `router.navigate()` themselves except for route normalization/redirects with `replace`.

## Orders Route Behavior

### Root route: empty hash / `#`

- Authenticated:
  - show orders root via existing `showList()` behavior.
  - load list or calendar according to existing saved `ordersViewMode`.
- Unauthenticated:
  - keep showing login.
  - after login succeeds, route handler for current hash should run again.

### `#order/:code`

- Load order via existing `/api/scheduling/orders/{code}` path.
- Show review card using existing `renderReview` behavior.
- If order fails to load:
  - show error in orders root area if possible.
  - replace route to root to avoid a broken back-stack entry.
- `closeReview`, Back buttons, app chrome Scheduler brand click should navigate to root instead of direct `showList()`.

### `#new/:step`

- First entry should `resetOrderForm()` and load clinics for lab users as today.
- Preserve an in-memory draft while navigating between `#new/1`, `#new/2`, etc.
- New-order routes are only for draft steps 1-4.
- Step navigation should update hash rather than only setting `step`.
- Use existing validation rules:
  - Backward step navigation allowed.
  - Forward step navigation must satisfy existing `validateBeforeStep` / `canGoToStep` semantics.
  - If direct navigation to a future invalid step occurs, replace to the highest valid step and show existing error messaging if appropriate.
- Do not create or support `#new/5` as a normal route. If encountered, replace to the appropriate current valid route.
- After successful create, navigate to `#created/:code`.

### `#created/:code`

- This route replaces the current in-memory-only step 5 URL concept.
- Fetch the created order by code so the confirmation screen is reload-safe.
- Reuse the existing step 5 confirmation UI as much as practical:
  - show the package/impression instruction prominently,
  - show the generated order code,
  - show the final overview/teeth preview.
- This route is read-only and represents a persisted order, but it is distinct from `#order/:code` because it has the special post-create package-code UX.
- The “Done” action from the confirmation screen should navigate to root.
- Browser refresh on `#created/:code` should still show the confirmation screen if the order exists.
- If the order cannot be loaded, show an error and replace to root.

### `#edit/:code/:step`

- On first entry, load the order then call existing `fillFormFromOrder(o)`.
- Preserve edit state while moving between edit steps for the same code.
- If the code changes, discard current edit state only after passing dirty guard.
- Step validation should mirror new order flow.
- After save succeeds, route to `#order/:code`. The `#created/:code` confirmation route is only for newly created orders.

## Dirty Flow / Browser Back Handling

This is the trickiest part and should be implemented deliberately.

Existing behavior:

- `requestBackToList()` checks `hasUnsubmittedOrderFlowChanges()` and opens `discardOrderFlowPopup`.

Needed route behavior:

- Programmatic navigation away from `#new/...` or `#edit/...` should check dirty state first.
- Browser Back/Forward produces a `hashchange` after the URL already changed; if dirty guard rejects:
  1. Remember the requested target route as `pendingRouteAfterDiscard`.
  2. Replace the hash back to the current route without adding history.
  3. Open the discard popup.
  4. If user confirms, navigate to the pending target with a one-shot `skipDirtyGuard` flag.
  5. If user cancels, remain on current route.

Refactor discard modal functions to support callbacks or pending-route state, while preserving existing button behavior.

## Click / Button Wiring Changes

Change these to navigate instead of directly switching screens:

- `newOrderBtn.onclick` -> `goNewOrder(1)`
- row click / View button / calendar chip / find order result -> `goOrderReview(code)`
- review close/back -> `goOrdersRoot()`
- review edit -> `goEditOrder(reviewOrder.orderCode, 1)`
- order flow Back to Orders / close -> guarded `goOrdersRoot()`
- successful create -> `goCreatedOrder(createdOrder.orderCode)`
- confirmation Done -> `goOrdersRoot()`
- stepper click -> route to `#new/:step` or `#edit/:code/:step`, not direct `advanceTo` only
- app chrome Scheduler brand click -> `goOrdersRoot()` when authenticated

Do **not** route list/calendar toggles. Keep:

- `ordersListModeBtn`
- `ordersCalendarModeBtn`
- `ORDERS_VIEW_MODE_KEY`

as UI preference only.

## Tests / Validation

### Automated tests

If practical, add lightweight tests for the reusable router utility. Since it is plain JS and the existing test stack is .NET/browser-oriented, either:

- add browser-level tests in `Web.Tests` that exercise hash navigation in the orders page, or
- add minimal router tests in a suitable existing JS/browser harness if one exists.

Prioritize tests/smokes that cover behavior, not implementation details.

Suggested `Web.Tests` coverage if feasible:

1. `/orders#order/{code}` loads the review screen after authentication.
2. New order button changes hash to `#new/1`.
3. Stepper navigation updates hash to `#new/2` only after required data is valid.
4. Successful create changes hash to `#created/:code` and shows the package-code confirmation UI.
5. Browser refresh on `#created/:code` reloads the confirmation UI from the API.
6. Browser back from review returns to root orders view.
7. List/calendar toggle does not change hash.
8. Dirty new/edit flow blocks route change and opens discard popup.

### Manual smoke checklist

- Login at `/orders` with no hash -> orders root appears.
- Login at `/orders#new/1` -> new flow appears.
- Login at `/orders#order/<existing>` -> review appears.
- Login at `/orders#created/<existing>` -> package-code confirmation appears.
- Back/forward works between root and review.
- Back/forward works between valid order-flow steps.
- Invalid direct future step normalizes safely.
- Dirty flow prompts before leaving via:
  - Back to Orders button
  - browser Back
  - clicking order review/find result/brand link if available
- List/calendar switch persists as before and does not alter URL fragment.
- Existing orders list paging/find/calendar behavior remains intact.

## Non-goals

- Do not decompose `orders.html` into reusable UI components in this slice.
- Do not change server routes.
- Do not migrate old order data.
- Do not add path-based client routing like `/orders/order/:code`; use hash fragments only.
- Do not add routes for list/calendar view options.

## Handoff Notes / Assumptions

- There are currently unrelated working tree changes in this repo; the implementing agent must avoid staging or modifying unrelated files.
- The plan assumes `InlayOverlay` construction changes are already present.
- It is acceptable for the first reusable router utility to be small. Avoid over-engineering nested routers, route loaders, or a full SPA framework.
- Step 5 URL behavior is resolved: use `#created/:code` for the post-create package-code confirmation screen. Do not implement `#new/5` as a normal route.
