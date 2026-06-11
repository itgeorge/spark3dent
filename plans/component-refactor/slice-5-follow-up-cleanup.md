---
name: slice-5-follow-up-cleanup
overview: Collect follow-up cleanup tasks discovered during component refactor reviews, to be handled after the main extraction slices or opportunistically when safe.
todos:
  - id: consolidate-escape-handling
    content: Remove or simplify the legacy centralized Orders Escape-key handler once all modals are fully migrated to S3DModal/modal stack handling.
    status: pending
  - id: deepen-orders-screen-modules
    content: Move the remaining Orders screen implementation internals out of orders-page.js into the Slice 4 Root/Review/Flow/Created modules once route-level wrapper boundaries have stabilized.
    status: pending
  - id: consolidate-orders-api-access
    content: Route remaining raw Orders api(...) calls in orders-page.js through S3DOrders.Api so screen modules can depend on a stable API facade.
    status: pending
isProject: false
---

# Slice 5: Follow-up Cleanup

## Purpose

This plan collects cleanup items discovered during reviews of the component-refactor slices. Add to this file as more non-blocking cleanup opportunities are found.

These tasks should generally be handled after the main refactor slices are stable, unless a cleanup becomes necessary to unblock a slice.

## Cleanup Items

### 1. Consolidate Orders Escape-key handling

During review of Slices 2 and 3, `Web/wwwroot/orders.html` still had the old centralized Escape-key handler alongside the newer `S3DModal` Escape/modal-stack handling.

Current state is not blocking because `S3DModal` stops propagation for top modals, but the duplication should be cleaned up later to reduce maintenance risk.

Target outcome:

- Bound modals rely on `S3DModal` for Escape handling.
- The legacy document-level Escape handler is removed or reduced to only page-level non-modal behavior, e.g. closing the review screen with Escape if that behavior is still desired.
- Escape priority remains correct for nested/stacked modals.
- Modal stack remains consistent: bound modals are opened/closed only via modal helpers, not direct `classList` mutations.

Validation:

- Find order popup closes with Escape.
- Cancel order confirm closes with Escape.
- Before-minimum confirm closes with Escape.
- Discard changes confirm closes with Escape.
- Orders day popup closes with Escape.
- Escape on review screen still performs the intended action, if retained.
- Orders replay tests pass.

### 2. Deepen Orders screen modules

Slice 4 introduced route-level screen modules and moved Orders bootstrap into `orders-page.js`, but the lowest-risk implementation intentionally kept most screen internals in the page coordinator and exposed them through Root/Review/Flow/Created wrappers.

Follow-up target:

- Move root list/calendar/find rendering internals from `orders-page.js` into `orders-root-view.js`.
- Move review rendering/cancel internals into `order-review-view.js`.
- Move order flow state, validation, dirty baseline, and save behavior into `order-flow-view.js`.
- Move created-confirmation-specific rendering into `order-created-confirmation-view.js` or share a small overview renderer with Flow.
- Keep route grammar and existing DOM ids/classes unchanged while doing this.

### 3. Consolidate Orders API access through `S3DOrders.Api`

Slice 4 added `Web/wwwroot/js/orders-api.js`, but `orders-page.js` still has remaining direct `api(...)` calls for some order, date, find, and cancel operations. This is acceptable for the initial screen split, but later module extraction will be cleaner if all browser API access goes through the shared Orders API facade.

Follow-up target:

- Replace remaining raw `api(...)` calls in Orders screen code with methods on `S3DOrders.Api`.
- Add API facade methods only where they represent reusable Orders operations, not one-off UI concerns.
- Preserve legacy request semantics, auth handling, response/error behavior, and endpoint URLs.
- Keep screen/view modules depending on the facade rather than on low-level fetch details.

Validation:

- Login/logout still works.
- List and calendar loading still work.
- Find order, review, edit, create, and cancel flows still work.
- Orders replay tests pass.

## Standard Validation

Run:

```bash
dotnet test Web.Tests/Web.Tests.csproj --no-restore
dotnet test Web.Tests/Web.Tests.csproj --filter Category=OrdersReplay --no-restore
```
