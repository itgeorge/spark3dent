# Slice 4b - Reservation Date Picker UX Iteration

## Goal

Refine the dual-date reservation picker after manual smoke testing so clinic users can efficiently try multiple impression dates and compare automatically calculated delivery outcomes.

The key behavior change: selecting an impression date should keep the picker in impression-selection mode instead of automatically switching to delivery-selection mode. Delivery should still be automatically recommended/selected by the existing availability logic, while remaining manually adjustable when the user explicitly chooses delivery mode.

## Requirements / UX Changes

### Stay in impression mode after impression selection

Typical reservation behavior is expected to be:

1. Clinic selects an impression date.
2. System automatically calculates/selects the best delivery date.
3. Clinic tries another nearby impression date.
4. Clinic compares how delivery changes.

Therefore:

- selecting an impression date must **not** auto-switch to delivery mode;
- the selected delivery date should still update automatically based on the chosen impression date;
- users can still manually change delivery by explicitly selecting `Select delivery`.

Current code to revisit:

- `Web/wwwroot/js/order-flow-view.js`
  - `applyImpressionDateSelection(...)` currently sets `reservationDateSelectionMode = 'delivery'`.
  - the hidden `impression` input change handler also switches to delivery mode.

### Add previous/next date arrows

Add left/right nudge controls for both selected dates. The intended placement is directly on the selected calendar cells, so the controls are spatially tied to the date being nudged:

```text
[selected impression cell]  [←] 12 Jun 2026 [→]
[selected delivery cell]    [←] 18 Jun 2026 [→]
```

The top date cards/summary should remain focused on mode switching and displaying the selected impression/delivery values; the nudge arrows should live on the selected calendar date cells.

Behavior:

- Impression arrows move the impression date to the previous/next selectable impression date.
- Delivery arrows move the delivery date to the previous/next selectable delivery date.
- Arrows skip unavailable dates.
  - Example: Friday impression right arrow jumps to Monday if weekend days are unavailable.
  - Example: Friday delivery right arrow may jump to Tuesday if Monday is first business day after closure and is not selectable.
- Left arrow is disabled when there is no previous selectable date.
- Right arrow is disabled when there is no next selectable date in the known/searchable range.
- Nudge behavior must update the same state as clicking a calendar date, including dirty state and availability reloads.

### Auto-correct invalid impression date

If the current impression date is not selectable because it is a weekend/lab offday/otherwise non-working, the reservation picker should automatically jump to the first selectable impression date rather than hovering on the invalid date.

Expected cases:

- New reservation defaults should choose the first future selectable impression date, not merely tomorrow if tomorrow is non-working.
- If month/status loading reveals the current impression date is invalid, replace it with the first selectable impression date in the available status range.
- If no selectable impression date exists in the loaded range, show a clear warning and disable delivery/date progression until one is selected.

Be careful in edit mode: if an existing reservation's saved impression date has become invalid due to later calendar changes, auto-correction should be visible as an unsaved form change and normal save validation remains authoritative.

## Current State / Context

After Slice 4:

- reservation mode uses one calendar with explicit `impression` / `delivery` modes;
- `/api/scheduling/reservations/dates` returns `impressionDates` and delivery `dates`;
- `reservationImpressionStatusesByDate` is populated in `order-flow-view.js`;
- `orders-delivery-date-picker.js` renders dual markers;
- delivery date auto-selection is handled by `syncDeliverySelection(j.dates || [])`;
- direct order date picking is still handled by the same shared delivery picker and must remain unchanged.

Relevant files:

- `Web/wwwroot/js/order-flow-view.js`
- `Web/wwwroot/js/orders-delivery-date-picker.js`
- `Web/wwwroot/orders.html`
- possibly `Web/wwwroot/js/month-calendar.js` if helper support is needed

## Desired End State

### Impression selection flow

When a user clicks/selects an impression date:

- `impression.value` updates;
- delivery value is cleared/recomputed as today;
- delivery availability reloads;
- recommended/first selectable delivery is selected if available;
- picker remains in impression mode;
- selected-date summary updates both impression and delivery.

### Delivery adjustment flow

When a user explicitly switches to delivery mode:

- clicking a selectable delivery date updates delivery;
- delivery arrows adjust only delivery;
- impression remains unchanged;
- picker may stay in delivery mode after delivery edits.

### Nudge controls

Render nudge controls on the selected impression and delivery calendar cells. If both dates are the same cell, show both controls in that cell in a compact, distinguishable way.

Use the currently loaded status arrays/maps as the source of selectable dates:

- impression: `reservationImpressionStatusesByDate`, status `isSelectable === true`;
- delivery: current `byDate`/delivery statuses, status `isSelectable === true` for reservation mode.

If the adjacent selectable date is outside the currently loaded month-calendar bounds, either:

- load/navigate the adjacent month and then apply the nudge; or
- disable the arrow until the user changes month.

Preferred: support loaded calendar bounds first, and clearly disable when not known. Do not introduce complicated cross-month prefetch unless needed.

## Implementation Plan

1. Stop auto-switching to delivery mode:
   - remove `reservationDateSelectionMode = 'delivery'` from impression selection paths;
   - keep auto delivery recalculation via existing date availability reload.
2. Track the latest loaded delivery statuses in a map/list, not only inside `loadDates`, so nudge controls can inspect selectable delivery dates.
3. Add UI controls for impression/delivery nudges on the selected calendar cells:
   - render dynamically from `orders-delivery-date-picker.js` or equivalent cell-rendering code;
   - only show the impression nudge on the selected impression cell and the delivery nudge on the selected delivery cell;
   - keep the top date cards as mode selectors/value summaries, not arrow controls.
4. Implement helper functions:
   - `selectableImpressionDates()`;
   - `selectableDeliveryDates()`;
   - `previousSelectableDate(current, dates)`;
   - `nextSelectableDate(current, dates)`;
   - `syncReservationDateNudgeControls()`.
5. Wire impression nudge buttons to the same path as selecting an impression date.
6. Wire delivery nudge buttons to the same path as selecting a delivery date.
7. Auto-correct invalid impression date after `impressionDates` load:
   - if reservation mode and current impression is missing/unselectable, choose first selectable impression date;
   - clear/recompute delivery as with normal impression selection;
   - avoid infinite reload loops by only changing when the selected ISO actually changes.
8. Keep direct order flow unchanged:
   - nudge controls hidden outside reservation mode;
   - no behavior changes to normal order delivery picker.
9. Add/update CSS for compact nudge rows and disabled states.

## TDD / Validation Plan

### Automated tests

Frontend behavior is mostly browser-side, so automated coverage may be limited unless there is an existing JS harness. Backend tests from Slice 4 should continue to pass.

Run:

```bash
dotnet test Spark3Dent.sln --no-restore --verbosity quiet
node --check Web/wwwroot/js/order-flow-view.js
node --check Web/wwwroot/js/orders-delivery-date-picker.js
node --check Web/wwwroot/js/month-calendar.js
```

### Manual tests

- New reservation opens with first selectable impression date, not a weekend/lab offday.
- Select Friday impression; right impression arrow jumps to Monday when weekend is unavailable.
- Delivery auto-calculates after each impression change while mode remains `Select impression`.
- Select Friday delivery; right delivery arrow skips weekend and first-business-day-after-closure if blocked.
- Left arrows disable when there is no earlier selectable date in the known range.
- Switch to `Select delivery`, manually change delivery, then verify impression nudges still work after switching back.
- Direct order creation date picker behaves as before.

## Review Notes for Implementing Agent

Include:

- where nudge controls were added;
- how selectable previous/next dates are computed;
- how invalid impression auto-correction avoids reload loops;
- confirmation that impression selection no longer auto-switches to delivery mode;
- confirmation direct order picker was unchanged;
- automated and manual validation results.
