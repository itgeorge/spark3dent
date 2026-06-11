---
name: slice-1-shared-ui-primitives-and-modals
overview: Extract reusable icon, button, selected-mark, modal, and confirm-dialog primitives from Orders while preserving current UI behavior.
todos:
  - id: inventory-repeated-ui
    content: Inventory repeated SVGs/buttons/modal structures in orders.html, iam.html, and invoice index.html before editing.
    status: pending
  - id: add-icons-module
    content: Add shared icon helper module for close, plus, search, check, back/close circle, and refresh-style icons.
    status: pending
  - id: add-button-primitives
    content: Add shared helpers/CSS conventions for circular icon buttons and add/remove buttons without visual regressions.
    status: pending
  - id: add-selected-mark-helper
    content: Extract picker selected checkmark markup used by material, shade, and calendar selections.
    status: pending
  - id: add-modal-dialog-module
    content: Add reusable modal/dialog shell and confirm dialog helper with focus, overlay click, and Escape behavior hooks.
    status: pending
  - id: migrate-orders-small-surface
    content: Replace Orders inline SVG/modal boilerplate where low-risk, keeping existing ids/classes and behavior.
    status: pending
  - id: validate-slice
    content: Run Web.Tests and manually smoke Orders modals and icon buttons.
    status: pending
isProject: false
---

# Slice 1: Shared UI Primitives and Modals

## Goal

Create reusable, framework-free UI primitives that can be used by Orders now and IAM/Invoices later.

This slice intentionally avoids extracting large Orders widgets or page-level modules.

## Candidate New Files

- `Web/wwwroot/js/ui-icons.js`
- `Web/wwwroot/js/ui-buttons.js` (optional; only if helper functions add value)
- `Web/wwwroot/js/modal-dialog.js`
- `Web/wwwroot/css/ui-primitives.css` (optional; use only for truly shared styles)

Use global namespaces because there is no bundler:

```js
window.S3DIcons = { close, plus, search, check, backClose, refresh };
window.S3DButtons = { iconButton, circleButton, addButton, removeButton };
window.S3DModal = { createModal, bindModal, confirm };
```

Exact API may vary, but keep it small and page-agnostic.

## Inventory Targets

Before editing, inspect at least:

- `Web/wwwroot/orders.html`
- `Web/wwwroot/iam.html`
- `Web/wwwroot/index.html`

Look for repeated patterns:

### SVG/Icon snippets

- close/X icon
- plus icon
- search icon
- selected/checkmark icon
- back/close circular icon
- refresh icon/text button

### Special buttons

- `.btn.circle-back`
- `.btn.circle-icon`
- `.work-item-add`
- `.work-item-remove`
- add/remove buttons with embedded SVGs

### Picker selected mark

- `.picker-selected-mark`
- `.picker-selected-mark-icon`
- Used in material choices, shade cards, delivery date cells.

### Modal/dialog patterns

- find order popup
- cancel order confirm popup
- before-minimum confirm popup
- discard order flow popup
- orders day popup
- IAM add/edit/confirm-style dialogs
- invoice modal overlays

## Implementation Guidance

### Icons

Prefer helpers that return DOM nodes or HTML strings consistently.

Suggested simple shape:

```js
S3DIcons.svg('close', { className: '...' })
S3DIcons.close({ className: 'circle-back-icon' })
S3DIcons.html.close({ className: '...' }) // if string helpers are easier for current templates
```

Keep SVG paths centralized. Do not change icon appearance unless intentional.

### Buttons

Do not over-abstract behavior. The main value is consistent markup/classes.

Examples:

- `S3DButtons.circleIcon({ id, label, title, icon })`
- `S3DButtons.roundAdd({ id, label, title })`
- `S3DButtons.roundRemove({ label, title })`

If creating buttons via JS would require too much HTML restructuring, expose only icon helpers first.

### Selected mark

Extract the purple checkmark markup used by pickers.

Current equivalent function in Orders:

```js
function pickerSelectedMarkHtml(){ ... }
```

Move to shared helper, e.g.:

```js
S3DIcons.selectedMarkHtml()
```

Then Orders can call it from material/shade/calendar code.

### Modal dialog shell

A reusable modal helper should support:

- overlay element
- card element
- open/close
- overlay click-to-close option
- Escape close option
- focus target on open
- lifecycle hooks

It should not assume Orders-specific ids.

Possible API:

```js
const modal = S3DModal.bind({
  overlay: $('findOrderPopup'),
  initialFocus: () => orderFindInput,
  closeOnOverlay: true,
  closeOnEscape: true,
  onClose: () => {}
});
modal.open();
modal.close();
```

Confirm dialog helper can be a wrapper but should not force immediate use everywhere.

## Orders Migration Scope

Keep migration focused:

- Replace repeated inline SVG icon string generation only where straightforward.
- Replace `pickerSelectedMarkHtml()` implementation with shared helper.
- Optionally wire existing Orders modals through `S3DModal.bind(...)`, but keep ids/classes and existing callbacks.
- Do not change order routing, data loading, flow logic, or calendar/list behavior.

## Non-Goals

- Do not extract tooth chart/work item picker.
- Do not split Orders into screen modules.
- Do not fully migrate IAM/Invoices in this slice.
- Do not redesign modals.

## Validation

Run:

```bash
dotnet test Web.Tests/Web.Tests.csproj --no-restore
```

Manual smoke:

- find order popup opens/closes/focuses input
- cancel order popup opens/closes/confirm still works
- before-minimum confirm still works
- discard changes confirm still works
- orders day popup still works
- material/shade/calendar selected marks still display
- add/remove work item buttons still display and work

## Review Notes to Provide

- List new shared APIs and files.
- List which Orders elements were migrated.
- Note any repeated patterns intentionally left for later slices.
