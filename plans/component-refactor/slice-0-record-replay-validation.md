---
name: slice-0-record-replay-validation
overview: Establish a browser-level record/replay regression harness for the Orders page before component refactoring, then run it after every refactor slice.
todos:
  - id: choose-browser-harness
    content: Use existing Web.Tests + PuppeteerSharp/Chromium setup or add the smallest needed test dependency to drive real browser interactions.
    status: pending
  - id: create-orders-fixture-seed
    content: Seed deterministic clinic/lab auth and at least one active order for review/edit/calendar flows.
    status: pending
  - id: record-core-orders-scenarios
    content: Encode core Orders user journeys as replayable scripted browser actions.
    status: pending
  - id: capture-stable-assertions
    content: Assert stable DOM/URL/state outcomes instead of brittle full-page pixel snapshots.
    status: pending
  - id: document-replay-command
    content: Document the exact test command and require it after each component-refactor slice.
    status: pending
isProject: false
---

# Slice 0: Orders Record/Replay Validation Harness

## Goal

Before refactoring `Web/wwwroot/orders.html` into components, create a browser-level regression harness that can replay core Orders interactions after every refactor slice.

This should catch behavior regressions that normal API tests and manual smoke checks may miss.

## Why This Comes Before Slice 1

The component refactor will touch markup, event handlers, modals, and routing. A replayable browser test gives agents a safety net and makes reviews easier.

This slice should be implemented before or alongside Slice 1.

## Harness Recommendation

Prefer using the existing .NET test ecosystem:

- Add tests under `Web.Tests`.
- Use the existing ASP.NET test fixture/server pattern.
- Use PuppeteerSharp if available through existing dependencies, or add an explicit `PackageReference` if needed.
- Use the Chromium executable already downloaded/bundled by the build if practical.

Avoid introducing a separate Node/Playwright toolchain unless there is a strong reason.

## Record/Replay Definition

“Record/replay” here does not need to mean a literal binary browser recording. It means:

- encode user journeys as deterministic browser action scripts,
- replay them in CI/local tests,
- assert URL fragments, visible panels, key text, and API-visible results.

Avoid brittle full HTML snapshots. Prefer stable semantic assertions.

## Core Scenarios to Replay

### 1. Login and root route

- Navigate to `/orders`.
- Confirm login form is visible if unauthenticated.
- Login with deterministic lab or clinic credentials.
- Assert root orders screen is visible.
- Assert URL hash remains empty.

### 2. List/calendar view is not routing

- Toggle list/calendar view.
- Assert `location.hash` does not become `#list`, `#calendar`, etc.
- Assert chosen view is visible.

### 3. Order review deep link

- Seed or create an order via API.
- Navigate to `/orders#order/<code>`.
- Login if needed.
- Assert review screen is visible.
- Assert order code/case info is present.
- Press Back/close.
- Assert root route and root screen.

### 4. New order flow route steps

- Click New order.
- Assert hash is `#new/1`.
- Fill enough step-1 data to advance.
- Click Next.
- Assert hash is `#new/2`.
- Continue through steps as minimally as feasible.

### 5. Created confirmation route

- Complete a new order.
- Assert hash is `#created/<code>`.
- Assert package-code confirmation UI is visible.
- Reload page on that hash.
- Assert confirmation UI reloads from API.
- Click Done.
- Assert root route.

### 6. Edit flow route

- From an existing order review, click Edit.
- Assert hash is `#edit/<code>/1`.
- Change a safe field like note/case name.
- Save.
- Assert hash is `#order/<code>`.
- Assert review reflects change.

### 7. Dirty navigation guard

- Enter `#new/1` or `#edit/<code>/1`.
- Make a change.
- Attempt to navigate root via Back to Orders or browser back.
- Assert discard confirmation is visible.
- Cancel prompt and assert still on current route.
- Repeat and confirm discard, assert target route reached.

### 8. Modal basics

- Open Find order popup.
- Assert focus is in input.
- Press Escape.
- Assert popup closed.
- Open cancel confirmation from review if order is cancellable.
- Assert Escape/overlay/Back behaviors as supported.

## Stable Assertions

Prefer assertions like:

- `await Page.EvaluateExpressionAsync<string>("location.hash")`
- element is visible / hidden by id:
  - `#login`
  - `#list`
  - `#app`
  - `#reviewCard`
  - `#findOrderPopup`
  - `#discardOrderFlowPopup`
- text content includes order code or case name
- route-specific controls visible:
  - `#code` on created confirmation
  - `#reviewCode` on review
  - step buttons/classes for current step

Avoid exact pixel screenshots unless visual layout becomes a concern.

## Test Data

Use deterministic data:

- Known clinic/lab credentials from existing scheduling test fixture, or seed them explicitly.
- Known order code/case where possible.
- For created-order scenarios, capture returned/generated code from the UI or API and use that for subsequent assertions.

## Commands

At minimum, after this slice and every later component-refactor slice run:

```bash
dotnet test Web.Tests/Web.Tests.csproj --no-restore
```

If the browser replay tests are separated by category, document the focused command, e.g.:

```bash
dotnet test Web.Tests/Web.Tests.csproj --filter Category=OrdersReplay --no-restore
```

## Non-Goals

- Do not create a massive end-to-end suite.
- Do not test every material/shade/date combination.
- Do not use brittle full DOM snapshots.
- Do not require external SaaS/browser services.

## Done Criteria

- At least the root, review deep-link, new-order route, created confirmation, and dirty guard scenarios are automated.
- The replay suite fails if key panels do not become visible or expected hashes are not set.
- The replay command is documented in this file and referenced from the master component refactor plan.
