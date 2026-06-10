---
name: component-refactor-master-plan
overview: Roadmap for incrementally decomposing browser UI code into reusable primitives, widgets, and eventually page/screen modules, starting with Orders and keeping IAM/Invoices reuse in mind.
todos:
  - id: slice-0-record-replay-validation
    content: Establish browser-level Orders record/replay regression tests to run after every refactor slice.
    status: pending
  - id: slice-1-ui-primitives-modals
    content: Extract shared icons, icon buttons, selected marks, and modal/confirm dialog primitives.
    status: pending
  - id: slice-2-shared-interaction-utilities
    content: Extract shared DOM, escape-key/modal-stack, async/action-button, and route/shell utilities.
    status: pending
  - id: slice-3-orders-domain-widgets
    content: Extract Orders-specific widgets such as FDI chart, tooth preview, work item picker, material/shade pickers, and delivery date picker.
    status: pending
  - id: slice-4-orders-screen-modules
    content: Split Orders page into higher-level root/review/flow/created-confirmation modules after smaller components are stable.
    status: pending
isProject: false
---

# Component Refactor Master Plan

## Context

`Web/wwwroot/orders.html` has grown into a large single-file browser app. The goal is to decompose it into reusable browser-side components without adopting a frontend framework or build step.

The refactor should be incremental and bottom-up:

0. Browser-level record/replay validation harness for Orders.
1. Shared UI primitives and modal foundation.
2. Shared interaction utilities.
3. Orders-specific widgets.
4. Larger Orders screen modules.

This ordering reduces risk and gives reusable value to IAM/Invoices early.

## Architectural Guidelines

- No frontend bundler is currently used. Prefer plain JS files loaded with `<script>` tags.
- Use stable global namespaces, e.g.:
  - `window.S3DIcons`
  - `window.S3DButtons`
  - `window.S3DModal`
  - `window.S3DDom`
  - `window.S3DOrdersWidgets`
- Keep modules framework-agnostic and page-agnostic whenever possible.
- Prefer small, composable helpers over large opaque classes.
- Preserve existing CSS class names where practical to minimize visual regressions.
- Extract CSS to shared files only when it is clearly reusable across Orders/IAM/Invoices.
- Avoid broad rewrites. Each slice should leave the page working.
- Do not stage or modify unrelated working tree changes.

## Dependencies / Sequence

- Slice 0 should happen first, or at least before high-risk refactors, so later agents can replay core Orders behavior.
- Slice 1 should happen after Slice 0 when practical.
- Slice 2 can happen after or partially alongside Slice 1, but should avoid changing widget logic.
- Slice 3 should depend on shared primitives/utilities being stable.
- Slice 4 should happen last, after widgets have clear APIs.

## Cross-Page Reuse Targets

Potential consumers beyond Orders:

- `Web/wwwroot/iam.html`
  - modal/dialog shell
  - confirm dialog
  - icon buttons
  - clinic swatch/color helpers
  - escape key / overlay click behavior
- `Web/wwwroot/index.html` / invoices UI
  - modal/dialog shell
  - confirmation dialogs
  - selected marks/check icons
  - loading/action button helpers

## Done Criteria for Whole Refactor Series

- Orders page behavior remains unchanged except for intentional navigation/routing behavior already planned/implemented.
- Shared helpers are documented enough in comments or plan notes for another page to consume.
- IAM/Invoices do not need to be migrated fully during this series, but extracted primitives should not be Orders-specific.
- Tests/smoke checks are run after each slice.

## Validation Baseline

After each slice, run at minimum:

```bash
dotnet test Web.Tests/Web.Tests.csproj --no-restore
```

Slice 0 adds browser-level Orders record/replay tests. After Slice 0 lands, those tests are part of the required validation for every subsequent slice. If the replay tests are categorized, run the focused replay command as well, e.g.:

```bash
dotnet test Web.Tests/Web.Tests.csproj --filter Category=OrdersReplay --no-restore
```

Also smoke manually in browser when UI is touched:

- login/logout
- orders list/calendar toggle
- find order popup
- order review
- new order flow through create confirmation
- edit order flow
- cancel order confirmation
- dirty-flow discard confirmation
