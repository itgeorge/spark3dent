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

After Slice 1, reservations can be created/edited/cancelled and consume capacity. Current order calendar frontend uses:

- `/api/scheduling/orders/calendar`;
- `orders-root-view.js` calendar loader;
- `orders-calendar-cells.js` shared cell rendering;
- `orders-delivery-date-picker.js` for delivery picker cells.

Calendar DTOs currently assume `orders` arrays, and order chips open order review.

## Desired End State

### API shape

Extend the calendar/list API or add a new combined endpoint so the frontend receives typed entries.

Preferred additive response for `/api/scheduling/orders/calendar`:

```json
{
  "days": [
    {
      "date": "2026-06-24",
      "orders": [ ...existing order dtos... ],
      "reservationDeliveries": [ ...reservation dtos... ],
      "reservationImpressions": [ ...reservation dtos... ],
      "capacity": { "used": 10, "limit": 30 },
      "weeklyCapacity": { "used": 100, "limit": 150 }
    }
  ],
  "clinics": { ... }
}
```

Alternatively use a unified entry array with `type` and `dateRole`, as long as existing order consumers remain compatible or are updated.

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

The list view should render active non-expired reservations among orders or in a combined list ordering that remains understandable.

Requirements:

- order rows remain unchanged;
- reservation rows have a `Reservation` badge;
- reservation primary text is compact material + tooth count + case name;
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

1. Decide combined DTO shape and update API tests first.
2. Extend calendar backend query to load active non-expired reservations by delivery and impression range.
3. Build clinic metadata map from orders plus reservations for lab users.
4. Update frontend API handling maps:
   - `ordersCalendarByDate` remains for orders;
   - add `reservationsByDeliveryDate`;
   - add `reservationsByImpressionDate`.
5. Extend `orders-calendar-cells.js` or add `reservations-calendar-cells.js` helpers.
6. Add CSS classes for reservation chips and impression dots.
7. Update day popup rendering and click routing.
8. Ensure reservation opening route exists from Slice 1; if not, add minimal route wiring.

## TDD Plan

### API tests

- Calendar response includes reservation delivery on requested delivery date.
- Calendar response includes reservation impression indicator on impression date.
- Expired/cancelled reservations are excluded.
- Clinic sees only own reservations; lab sees all.
- Capacity values include active reservation capacity.

### Frontend/manual tests

- Create reservation with impression Wednesday and delivery Friday.
- Calendar shows small indicator on Wednesday and reservation chip on Friday.
- Clicking either opens reservation edit/detail.
- Day popup distinguishes order vs reservation and impression vs delivery.
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
