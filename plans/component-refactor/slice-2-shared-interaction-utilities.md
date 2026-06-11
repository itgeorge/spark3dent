---
name: slice-2-shared-interaction-utilities
overview: Extract reusable browser interaction utilities such as DOM escaping/builders, modal Escape stack, action-button async state, dirty navigation helpers, and authenticated shell helpers.
todos:
  - id: inventory-ad-hoc-utilities
    content: Inventory repeated DOM, escape, popup, focus, async button, and navigation guard code in Orders/IAM/Invoices.
    status: pending
  - id: add-dom-utility-module
    content: Add shared DOM utility module for escaping, element creation, class toggles, focus deferral, and safe text/html patterns.
    status: pending
  - id: add-modal-stack-escape-manager
    content: Add a reusable Escape-key/modal stack helper and migrate low-risk Orders modal Escape handling.
    status: pending
  - id: add-action-button-helper
    content: Add reusable async action-button state helper for loading/disabled text restoration.
    status: pending
  - id: add-dirty-navigation-helper
    content: Extract reusable dirty navigation guard pattern compatible with the hash router.
    status: pending
  - id: validate-slice
    content: Run Web.Tests and manual smoke route/modal/dirty behavior.
    status: pending
isProject: false
---

# Slice 2: Shared Interaction Utilities

## Goal

Extract page-agnostic browser interaction helpers that reduce boilerplate and support future reuse in IAM and Invoices.

This slice should not extract large UI widgets. It should make the existing pages easier to refactor safely.

## Candidate New Files

- `Web/wwwroot/js/dom-utils.js`
- `Web/wwwroot/js/modal-stack.js` or include in `modal-dialog.js` if Slice 1 created it
- `Web/wwwroot/js/action-button.js`
- `Web/wwwroot/js/dirty-navigation.js`

Possible namespaces:

```js
window.S3DDom = { esc, el, clear, setHidden, deferFocus };
window.S3DActionButton = { runWithBusyState };
window.S3DDirtyNavigation = { createDirtyGuard };
```

Keep APIs small and practical.

## Inventory Targets

Inspect:

- `Web/wwwroot/orders.html`
- `Web/wwwroot/iam.html`
- `Web/wwwroot/index.html`
- `Web/wwwroot/js/hash-router.js`
- existing shared JS files like `app-chrome.js`, `month-calendar.js`

Look for repeated/ad-hoc code:

- `esc(...)` implementations
- manual `classList.add('hidden')` / remove patterns
- repeated `setTimeout(() => input.focus(), 0)` focus patterns
- repeated async button states:
  - disable button
  - change text to Loading/Search/Cancelling
  - restore in finally
- repeated Escape key modal handling
- overlay click-to-close handlers
- dirty form/navigation guard patterns

## Suggested Utilities

### DOM utils

Keep it minimal:

```js
S3DDom.esc(value)
S3DDom.setHidden(element, hidden)
S3DDom.deferFocus(elementOrFn, { select: false })
S3DDom.replaceChildrenHtml(element, html) // only if safe pattern is clear
```

Avoid adding a large virtual-DOM abstraction.

### Action button helper

Example:

```js
await S3DActionButton.run(button, {
  busyText: 'Searching…',
  disable: [button, cancelButton],
  action: async () => { ... }
});
```

Use only where it simplifies existing code without hiding important state.

### Modal/Escape stack

Current Orders Escape handling is a long priority chain. A shared stack can centralize:

- topmost modal handles Escape first
- close callbacks registered on open
- unregister on close

Keep backwards compatibility with explicit overlay click handling.

### Dirty navigation helper

The Orders hash-routing implementation has a dirty guard pattern:

- detect unsubmitted changes
- reject route transition
- restore URL
- show discard dialog
- continue to pending route on confirm

Extract only if it can remain page-agnostic:

```js
const dirtyGuard = S3DDirtyNavigation.createGuard({
  isDirty,
  isSameSafeRoute,
  showPrompt,
  navigateAfterConfirm
});
```

If extracting the full guard is too risky, document the intended API and only extract smaller helper pieces.

## Orders Migration Scope

- Replace Orders `esc` with shared `S3DDom.esc` or have `esc` delegate to it.
- Replace easy focus deferrals in popups with shared helper.
- Optionally migrate find-order/cancel buttons to action-button helper.
- Optionally migrate Escape handling for Orders modals to a modal stack if Slice 1 modal helper supports it.
- Do not change route grammar or flow behavior.

## Non-Goals

- Do not rewrite all DOM rendering.
- Do not build a framework.
- Do not migrate IAM/Invoices fully.
- Do not extract Orders widgets.

## Validation

Run:

```bash
dotnet test Web.Tests/Web.Tests.csproj --no-restore
```

Manual smoke:

- all Orders modals still close in expected priority with Escape
- overlay click close still works where supported
- async buttons restore state after success/failure
- dirty flow prompt still blocks root/review/edit/new route changes
- login and hash deep-link behavior still works

## Review Notes to Provide

- New utility files and global namespaces.
- Existing Orders helpers replaced or delegated.
- Any helpers intentionally left local because extraction was too risky.
