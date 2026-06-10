---
name: slice-4-orders-screen-modules
overview: After primitives/utilities/widgets are extracted, split Orders into high-level screen modules for root, review, order flow, and created confirmation while preserving the hash-router contract.
todos:
  - id: define-screen-boundaries
    content: Inventory remaining orders.html functions and assign ownership to Root, Review, Flow, CreatedConfirmation, routing/bootstrap, or shared modules.
    status: pending
  - id: extract-orders-root-view
    content: Extract orders root/list/calendar/find-order logic into a root view module.
    status: pending
  - id: extract-order-review-view
    content: Extract read-only order review and cancel/edit actions into a review view module.
    status: pending
  - id: extract-order-flow-view
    content: Extract new/edit order flow coordination into a flow module that composes widgets from prior slices.
    status: pending
  - id: extract-created-confirmation-view
    content: Extract #created/:code confirmation screen behavior into a dedicated module.
    status: pending
  - id: slim-orders-html-bootstrap
    content: Reduce orders.html inline script to state bootstrap, dependency wiring, and route registration.
    status: pending
  - id: validate-slice
    content: Run Web.Tests and manual smoke all route/screen transitions.
    status: pending
isProject: false
---

# Slice 4: Orders Screen Modules

## Goal

Split `Web/wwwroot/orders.html` into high-level Orders screen modules after shared primitives/utilities and Orders widgets have been extracted.

This slice should preserve the hash-router contract established by the navigation work:

- `/orders` or `/orders#` -> root orders screen
- `#order/:code` -> read-only order review
- `#new/:step` -> new order flow steps 1-4
- `#created/:code` -> post-create package-code confirmation
- `#edit/:code/:step` -> edit order flow steps 1-4

## Candidate New Files

Possible structure:

- `Web/wwwroot/js/orders-api.js`
- `Web/wwwroot/js/orders-state.js` (optional)
- `Web/wwwroot/js/orders-root-view.js`
- `Web/wwwroot/js/order-review-view.js`
- `Web/wwwroot/js/order-flow-view.js`
- `Web/wwwroot/js/order-created-confirmation-view.js`
- `Web/wwwroot/js/orders-page.js` (bootstrap/composition)

Possible namespace:

```js
window.S3DOrders = window.S3DOrders || {};
S3DOrders.Api = { ... };
S3DOrders.RootView = { create(...) };
S3DOrders.ReviewView = { create(...) };
S3DOrders.FlowView = { create(...) };
S3DOrders.CreatedConfirmationView = { create(...) };
S3DOrders.Page = { start(...) };
```

## Suggested Screen Boundaries

### Orders bootstrap / page coordinator

Responsibilities:

- load shared modules
- initialize app chrome
- initialize router
- hold top-level authenticated actor state or inject a state object
- define route handlers
- coordinate navigation callbacks between views

Should be the only place that knows route grammar.

### Orders API module

Wrap API calls:

- auth/me/login/logout
- orders list/page/calendar/find/get/create/update/delete
- clinics
- date availability

Suggested API:

```js
S3DOrders.Api.create({ fetchJson: api })
```

Keep error handling explicit.

### Root view

Owns:

- orders list/calendar shell
- list/calendar view preference (still not route)
- reload/load more
- find order popup interaction
- orders day popup
- list/calendar rendering
- order highlight after find/cancel/edit

Exposes callbacks:

- `onOpenOrder(code)`
- `onNewOrder()`

### Review view

Owns:

- read-only order overview rendering
- selected teeth preview composition
- cancel order popup/action
- edit button
- close/back button

Exposes callbacks:

- `onBack()`
- `onEdit(code)`
- maybe `onCancelled(code)`

### Order flow view

Owns:

- new/edit flow state
- step validation
- stepper rendering and navigation requests
- work item picker/material/shade/delivery widgets
- overview step
- create/update save action
- dirty-state baseline and dirty detection

Exposes callbacks:

- `onNavigateStep(step)`
- `onCreated(code)` -> route to `#created/:code`
- `onUpdated(code)` -> route to `#order/:code`
- `onBackRequested()` -> route root with dirty guard

### Created confirmation view

Owns:

- package-code confirmation screen (`#created/:code`)
- loads/render persisted order data or receives it from page coordinator
- prominent code/instruction display
- final overview/teeth preview
- Done action -> root

May reuse pieces from Order flow final overview, but should be a separate route-level module conceptually.

## Migration Strategy

1. Inventory remaining functions after Slices 1-3.
2. Create modules with thin wrappers around existing functions first.
3. Move one screen at a time.
4. Keep route handlers working after each move.
5. Avoid changing DOM ids/classes until after behavior is stable.
6. Remove dead inline functions only after tests/smokes pass.

## State Management Recommendation

Avoid introducing a complex central store unless necessary.

A pragmatic state object is enough:

```js
const pageState = {
  actor: null,
  clinics: [],
  orderClinics: {},
  ordersViewMode: 'list',
  pendingFindListHighlightCode: null,
  pendingFindCalendarHighlightDate: null
};
```

Each view can own its local state and expose methods.

## Router Integration

Route handlers should be thin:

```js
routes: [
  { pattern: '', handler: () => rootView.show() },
  { pattern: 'order/:code', handler: ({ params }) => reviewView.show(params.code) },
  { pattern: 'new/:step', handler: ({ params }) => flowView.showNew(params.step) },
  { pattern: 'created/:code', handler: ({ params }) => createdView.show(params.code) },
  { pattern: 'edit/:code/:step', handler: ({ params }) => flowView.showEdit(params.code, params.step) }
]
```

Dirty navigation guard should delegate to `flowView.isDirty()` and `flowView.promptDiscard(...)` rather than reading inline globals.

## Non-Goals

- Do not change route grammar.
- Do not make list/calendar mode route-based.
- Do not redesign screens.
- Do not migrate IAM/Invoices to screen modules.
- Do not introduce a framework/build system.

## Validation

Run:

```bash
dotnet test Web.Tests/Web.Tests.csproj --no-restore
```

Manual smoke route matrix:

- `/orders` root
- `/orders#order/<existing>` review deep-link and refresh
- `/orders#new/1` direct entry after login
- valid new order step navigation with browser back/forward
- invalid future step normalizes safely
- successful create routes to `#created/:code`
- `/orders#created/<existing>` refresh reloads confirmation
- edit route `#edit/:code/1` loads and saves to `#order/:code`
- dirty new/edit flow blocks leaving via button, brand, and browser back
- list/calendar toggle does not change hash
- find order behavior still highlights list/calendar context and routes review

## Review Notes to Provide

- Module boundaries chosen.
- Remaining inline script responsibilities.
- Any screen left partially inline and why.
- Behavior and route smoke results.
