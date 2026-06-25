# Slice 4 - Dual-Date Reservation Picker UX

## Goal

Replace the basic reservation date controls with a polished same-calendar dual-date picker where users can clearly select both reservation impression date and delivery date in one UI.

This slice is frontend-heavy but still end-to-end: the picker must use backend reservation date availability, enforce impression restrictions, update delivery recommendations from the selected impression date, and save reservations through the existing reservation APIs.

## Requirements Covered

From `plans/reservations/v1-requirements.md`:

- Reservation creation/edit UI mostly mirrors order UI.
- Date step includes both impression date and delivery date in one calendar UI.
- Impression dates follow all non-working-day restrictions, including lab offdays.
- Impression date does not use capacity and does not apply first-business-day-after-closure restriction.
- Delivery date follows normal deadline/capacity rules based on selected impression date after-cutoff semantics.
- Direct order creation remains unchanged.
- Reservation edits can be saved without promotion.

Explicitly out of scope:

- backend reservation persistence if completed in earlier slices;
- month-calendar list display polish from Slice 2;
- promotion logic from Slice 3.

## Current State / Context

After Slice 1, reservation create/edit works from the browser, possibly with simple impression-date controls plus the existing delivery calendar. This slice improves the interaction to match product requirements.

Existing reusable frontend pieces:

- `month-calendar.js`;
- `orders-delivery-date-picker.js`;
- delivery date statuses and reason popovers;
- existing lab override modal pattern.

## Desired End State

### Interaction model

The reservation date step should show one calendar where:

- first click/select chooses impression date when no impression is selected or when user switches to impression mode;
- delivery date can be selected after impression date;
- explicit controls/tabs/buttons make current selection target clear (`Impression` vs `Delivery`);
- selected impression and selected delivery have distinct markers;
- date range between them may be lightly shaded if practical;
- changing impression date clears or revalidates delivery date as appropriate.

A simple explicit selection mode is acceptable and preferred over ambiguous click behavior:

```text
[Select impression] [Select delivery]
```

### Impression date statuses

The backend reservation date availability response should provide impression statuses or enough data for frontend to disable invalid impression dates.

Impression unavailable reasons:

- past/not future;
- weekend/non-working day;
- lab offday/non-working day.

The first-business-day-after-closure rule must not block impression dates.

### Delivery statuses

Delivery statuses should update based on selected impression date.

The UI should display:

- minimum/recommended delivery date;
- capacity/full reasons from existing status DTOs;
- lab override option for delivery dates only.

### Edit mode

For existing reservation edit:

- preselect current impression and delivery dates;
- allow saving changed dates without promotion;
- warn/revalidate if old dates are now invalid due to config/capacity, using existing override rules for lab delivery override where applicable.

### Direct orders unchanged

Do not alter direct order date-selection behavior except for shared CSS/utilities that do not change behavior.

## Implementation Plan

1. Define or refine reservation date availability response:
   - impression statuses for visible range;
   - delivery statuses for visible range;
   - minimum/recommended delivery date.
2. Add tests proving impression status differs from delivery status for first business day after closure and lab offdays.
3. Create a reservation date picker JS module or extend existing delivery picker with mode-aware behavior.
4. Add CSS for impression marker, delivery marker, optional range shading, and legend.
5. Wire reservation flow date step to use this picker only in reservation mode.
6. Ensure date availability reloads when impression date, material, work items, target clinic, or month changes.
7. Preserve order flow date picker unchanged.
8. Add manual/browser validation notes.

## TDD Plan

### API tests

- Impression status rejects lab offday.
- Impression status allows first business day after closure if not non-working.
- Delivery status rejects that same first business day after closure.
- Delivery recommendation changes when impression date changes.
- Delivery statuses include capacity from active orders/reservations.

### Frontend/manual tests

- Create reservation and select impression and delivery in one calendar.
- Confirm selected markers are visually distinct.
- Select a Monday after weekend as impression successfully.
- Confirm same Monday is blocked as delivery.
- Select lab offday as impression and see unavailable reason.
- Change impression date and confirm delivery recommendation/statuses refresh.
- Edit existing reservation and save changed dates.

## Validation Plan

Run:

```bash
dotnet test Spark3Dent.sln --no-restore --verbosity quiet
```

Manual validation should focus on UX and rule differences between impression and delivery dates.

## Review Notes for Implementing Agent

Include:

- reservation date availability response shape;
- frontend module/CSS files changed;
- confirmation direct order picker was unchanged;
- screenshots or written manual validation of dual markers;
- automated test result.
