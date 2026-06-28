# Slice 2 - Combined Calendar and Reservation Display

## Goal

Make reservations visible in normal scheduling views alongside orders, including delivery-date chips and impression-date indicators on the month calendar.

After this slice, users can evaluate expected work visually: active reservations appear semi-transparently on delivery dates, and their impression dates appear as small indicators. Clicking either opens the reservation flow/detail UI.

## Requirements Covered

From `plans/reservations/v1-requirements.md`:

- Active orders and active reservations appear in list/calendar views.
- Reservations are visible only to lab and reserving clinic.
- Reservation delivery entries are semi-transparent/lighter and clearly marked.
- Reservation impression dates display as small date-cell indicators.
- Day popups include orders and reservations.
- Cancelled/promoted/expired reservations disappear from ordinary active views.
- Calendar capacity indicators include active reservations.

Explicitly out of scope:

- promotion to order;
- polished dual-date picker internals beyond opening reservation edit flow;
- historical expired/cancelled reservations page.

## Current State / Context

After Slice 1, reservations can be created/edited/cancelled and consume capacity. Some combined display groundwork already exists:

- `/api/scheduling/orders/calendar` already injects active non-expired reservations into each day DTO using current field names:
  - `reservations` for reservations whose requested delivery date is that day;
  - `impressionReservations` for reservations whose impression date is that day.
- Calendar capacity indicators already use backend capacity usage that includes active reservations.
- Root/list view already loads active reservations through `/api/scheduling/reservations` and renders reservation rows mixed with orders.
- Reservation routes already exist (`reservation/:id`, `reservation-edit/:id/:step`).

The main remaining gap for this slice is the month calendar/day-popup UI: `orders-root-view.js` currently still maps only `d.orders` into `ordersCalendarByDate`, and `orders-calendar-cells.js` still assumes entries are orders with `orderCode`. Reservation delivery chips, reservation impression indicators, and mixed day-popup click routing are not yet implemented.

Relevant frontend files:

- `Web/wwwroot/js/orders-root-view.js` calendar loader and day popup;
- `Web/wwwroot/js/orders-calendar-cells.js` shared cell rendering;
- `Web/wwwroot/js/orders-delivery-date-picker.js` if shared calendar cell helpers are reused in the reservation/order flow;
- `Web/wwwroot/orders.html` CSS block for chip/indicator styling.

## Desired End State

### API shape

Use the existing additive response shape from Slice 1 unless there is a strong reason to rename fields:

```json
{
  "days": [
    {
      "date": "2026-06-24",
      "orders": [ ...existing order dtos... ],
      "reservations": [ ...reservation dtos whose delivery date is this day... ],
      "impressionReservations": [ ...reservation dtos whose impression date is this day... ],
      "capacity": { "used": 10, "limit": 30 },
      "weeklyCapacity": { "used": 100, "limit": 150 }
    }
  ],
  "clinics": { ... }
}
```

Do not remove or rename `orders`; existing order rendering depends on it. If field names are changed, update all frontend consumers and tests deliberately.

Reservation DTOs should include enough display data:

```text
id
entityType = reservation
clinicCode / clinicDisplayName / clinicDisplayColor for lab users
caseName
material
workItems
impressionDate
requestedDeliveryDate
shade
status
calculatedCapacityUnits
```

No order code should be emitted for reservations.

### Root list view

Slice 1 already includes a basic active reservation list display mixed with orders. For this slice, preserve that behavior and only adjust it if needed to support calendar/day-popup consistency.

Requirements to keep true:

- order rows remain unchanged;
- reservation rows have a `Reservation` badge/label;
- reservation primary text is compact material + tooth count and/or case name;
- delivery date remains visible;
- impression date is visible in row secondary text or date column;
- clicking opens reservation detail/edit flow.

### Month calendar delivery display

On requested delivery date:

- render reservation chips beside order chips;
- use semi-transparent/lighter styling;
- show clear reservation marker/badge;
- use clinic accent for lab actors;
- clicking opens reservation detail/edit flow.

### Month calendar impression display

On impression date:

- render a small dot/badge/indicator for each active reservation impression;
- use a distinct color/style from delivery chips;
- if multiple fit poorly, aggregate with a count;
- clicking opens reservation detail/edit flow or a day popup chooser.

### Day popup

Day popup should include:

- orders due on that date;
- reservations due on that date;
- reservation impressions on that date.

Each row should identify entity type and, for reservations, whether the clicked date is `Impression` or `Delivery` or both.

### Capacity display

Capacity used/limit indicators for lab users must include active reservations from Slice 1. If Slice 1 already changed backend capacity usage, this slice should ensure calendar capacity DTOs and UI match it.

## Implementation Plan

1. Keep or explicitly confirm the existing calendar DTO shape (`orders`, `reservations`, `impressionReservations`) and add/adjust API tests around it.
2. Verify backend calendar query loads active non-expired reservations by both delivery and impression range; fix only if tests expose a gap.
3. Verify clinic metadata map includes clinics referenced by orders and reservations for lab users.
4. Update frontend API handling maps in `orders-root-view.js`:
   - keep `ordersCalendarByDate` for orders;
   - add `reservationsCalendarByDeliveryDate` (or similar) from `d.reservations`;
   - add `reservationsCalendarByImpressionDate` (or similar) from `d.impressionReservations`.
5. Extend `orders-calendar-cells.js` or add `reservations-calendar-cells.js` helpers for:
   - reservation delivery chips;
   - reservation impression dot/count indicators;
   - mixed order/reservation popup rows.
6. Add CSS classes for reservation chips and impression dots.
7. Update day popup rendering and click routing:
   - orders call `onOpenOrder(orderCode)`;
   - reservations call `onOpenReservation(id)`;
   - rows/labels distinguish `Delivery`, `Impression`, or both.
8. Ensure delivery-date picker cells do not accidentally render reservation entries as clickable orders. If shared day-order rendering receives reservations, pass explicit entry type/click handlers or keep reservation entries static in the picker until later slices.

## TDD Plan

### API tests

- Calendar response includes reservation delivery in `reservations` on requested delivery date.
- Calendar response includes reservation impression in `impressionReservations` on impression date.
- If impression date and delivery date are the same visible day, the reservation is represented in both roles or otherwise clearly indicates both roles.
- Expired/cancelled reservations are excluded.
- Clinic sees only own reservations; lab sees all.
- Capacity values include active reservation capacity.

### Frontend/manual tests

- Create reservation with impression Wednesday and delivery Friday.
- Calendar shows small indicator on Wednesday and semi-transparent reservation chip on Friday.
- Clicking either opens reservation detail/edit.
- Day popup distinguishes order vs reservation and impression vs delivery.
- Verify a date that has both an order and reservation renders both.
- Verify a reservation whose impression and delivery are both in the visible month appears in both roles.
- Clinic cannot see another clinic reservation on calendar.

## Validation Plan

Run:

```bash
dotnet test Spark3Dent.sln --no-restore --verbosity quiet
```

Manual browser validation should cover list and calendar views for both clinic and lab users.

## Review Notes for Implementing Agent

Include:

- chosen calendar DTO shape;
- frontend routes/click behavior;
- CSS classes added;
- how expired/cancelled reservations are filtered;
- screenshots or concise manual validation notes if practical;
- automated test result.
