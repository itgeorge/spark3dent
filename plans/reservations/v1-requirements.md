# Reservations Support - V1 Requirements

## 1. Purpose

Add scheduling reservations alongside orders.

A reservation represents expected future lab work before it becomes a real order. It contains almost the same clinical/case data as an order, but:

- it is filed against a future impression date;
- it consumes delivery-date daily/weekly capacity like an order;
- it has no order code while it is only a reservation;
- it is visible only to the lab and to the reserving/target clinic;
- it can later be promoted to an order, at which point an order code is generated.

The goal of V1 is to make expected future work visible and capacity-accounted without forcing clinics/lab staff to create real orders before impressions are physically taken.

---

## 2. Current System Context

The current order scheduler has:

- `SchedulingOrders` with clinical/case data, requested delivery date, status, calculated capacity units, audit metadata, and work items.
- `DeadlineRecommendationService` that uses:
  - material scheduling config;
  - selected teeth/work items;
  - an impression timestamp;
  - non-working days and first-business-day-after-closure delivery restriction;
  - daily/weekly capacity from orders;
  - commit-time revalidation through `ISchedulingWriteTransaction`.
- Explicit lab deadline override support with reason and override logs.
- Recommendation logs for successful order create/update.
- `/api/scheduling/orders/calendar` for list/calendar display and lab capacity indicators.
- `orders.html` order flow with steps for constructions, material/shade, deadline, overview, and confirmation.
- `scheduling-config.html` lab config UI for material/capacity config and lab offdays.

Reservation support should extend these concepts rather than introducing a separate inconsistent scheduling algorithm.

---

## 3. Definitions

### 3.1 Reservation

A reservation is a schedulable future case with:

- target clinic / reserving clinic;
- member/actor metadata for creator/updater;
- case name;
- future impression date;
- product category;
- material;
- work items / selected teeth;
- shade and optional color note;
- optional lab note;
- requested delivery date;
- calculated capacity units;
- status.

A reservation does not have an order code.

### 3.2 Impression date

The date when the clinic expects to physically take the impression and deliver/send the case to the lab.

For reservation scheduling, delivery lead-time calculations should be based on the selected reservation impression date, not on reservation creation time.

Because V1 captures only a date and not an impression time, reservation scheduling must treat every reservation impression as **after** the 11:00 lab cutoff. In practice, lead-time counting starts from the next business day after the selected impression date.

### 3.3 Promotion

Promotion is the act of converting an active reservation into an actual order.

Promotion is expected to be available to the reserving clinic and to the lab. Clinics are expected to promote their own reservations when the physical impression is taken; otherwise the reservation expires/gets ignored by the automatic rule below.

Promotion must:

- generate an order code;
- create a real order with the reservation's current case data;
- mark the reservation as promoted/replaced so it no longer consumes capacity independently;
- preserve traceability between reservation and created order.

### 3.4 Active reservation

An active reservation is one that is:

- not cancelled/deleted;
- not promoted;
- not automatically ignored/expired.

Only active reservations consume capacity.

### 3.5 Automatically ignored / expired reservation

A reservation is automatically ignored by the system when it has not been promoted to an order by the end of the calendar day after its impression date, in the lab's local time zone.

Example:

```text
Impression date: 2026-06-24
Still considered active through: 2026-06-25 23:59:59 local time
Ignored from: 2026-06-26 00:00:00 local time
```

Ignored reservations should no longer appear in ordinary active reservation/order displays and should no longer consume scheduling capacity.

---

## 4. Scope

### 4.1 In scope

V1 covers:

- create reservation from a separate `+ Reservation` UI entry point;
- edit existing active reservations;
- cancel/delete active reservations;
- promote an active reservation to an order;
- capacity-aware delivery date selection for reservations;
- automatic ignoring/expiry of reservations after the day following their impression date has passed;
- selected impression date and selected delivery date in one calendar UI;
- order/reservation list and calendar views showing both entity types;
- reservation visibility scoped to lab + reserving clinic;
- calendar display of reservation delivery dates and impression-date indicators;
- commit-time capacity revalidation for reservation create/update/promotion;
- audit/recommendation/override logging extensions sufficient to debug reservation decisions.

### 4.2 Out of scope

V1 does not cover:

- recurring reservations;
- waitlists or tentative/no-capacity holds;
- partial capacity allocation across production phases;
- patient-level appointment management;
- notifications to clinics/lab on promotion/cancellation;
- payment/invoicing behavior for reservations.

---

## 5. Core Business Rules

### 5.1 Reservation code behavior

- Reservations have no order code.
- Reservations do not need a separate human-readable reference.
- For display purposes, use an appropriate compact case summary, mostly number of teeth and material, plus a clear `Reservation` indicator.
- Any internal database id may exist for API routing, but it must not be shown or treated as an order code.
- An order code is generated only when a reservation is promoted to an order.

### 5.2 Visibility

Reservation visibility matches order visibility, with reservation-specific scoping:

- lab actors can see all reservations;
- clinic actors can see only reservations for their clinic;
- one clinic must never see another clinic's reservations;
- unauthenticated users cannot see reservations.

### 5.3 Impression date rules

Reservation impression dates must be in the future.

Impression-date availability rules:

- all configured non-working days are restricted, including weekends, official holidays, and lab offdays;
- first-business-day-after-weekend/holiday/closure restriction does **not** apply;
- capacity is irrelevant for impression date availability;
- selected impression date should be visually distinct in the date picker.

### 5.4 Delivery date rules

Reservation delivery date rules are the same as order delivery date rules, except the scheduling base is the selected reservation impression date.

Delivery-date selection must respect:

- material lead time;
- selected teeth/work-item capacity units;
- delivery non-working-day restrictions;
- first-business-day-after-weekend/holiday/closure restriction;
- daily/weekly capacity including active orders and active reservations;
- lab override rules, if the lab explicitly overrides a blocked delivery date.

### 5.5 Capacity consumption

Active reservations consume capacity exactly like active orders:

- full calculated capacity is assigned to the requested delivery date for daily capacity;
- full calculated capacity is assigned to the Monday-Sunday week containing the requested delivery date for weekly capacity;
- cancelled/deleted reservations do not consume capacity;
- promoted reservations do not consume capacity after promotion because the created order replaces them;
- automatically ignored/expired reservations do not consume capacity;
- override-promoted or override-scheduled reservations still consume capacity while they remain active.

### 5.6 Capacity replacement on promotion

When promoting a reservation to an order:

- the reservation's existing capacity hold must be excluded from validation of the created order;
- after commit, capacity must be counted once: by the order, not by both the order and reservation;
- this must happen atomically enough that capacity cannot be double-counted or freed incorrectly during promotion.

### 5.7 Reservation editing

Editing an active reservation should allow changing the same editable fields as an order plus the impression date.

On edit save:

- recalculate capacity units using the current selected material/work items and selected delivery date;
- revalidate delivery capacity/rules while excluding the current reservation;
- if the impression date changes, recompute delivery-date minimum/recommendation from the new impression date;
- preserve audit trail.

### 5.8 Automatic ignore/expiry

Reservations must be automatically ignored if they are not converted to orders when the date after the impression date has passed.

Rule:

```text
ignoreAtLocal = impressionDate + 2 calendar days at 00:00 local lab time
```

Examples:

- impression date `2026-06-24` -> ignore from `2026-06-26 00:00` local time;
- impression date `2026-06-25` -> ignore from `2026-06-27 00:00` local time.

Required behavior:

- ignored reservations do not consume capacity;
- ignored reservations do not appear in ordinary active list/calendar views;
- ignored reservations cannot be promoted without an explicit recovery/reopen flow, which is out of scope for V1 unless added later;
- the system may implement this as a computed status based on lab-local current time, or as a persisted status updated by a background/job-on-read process.

### 5.9 Reservation cancellation/deletion

V1 should support manually removing a reservation from active scheduling before automatic ignore/expiry.

Acceptable V1 behavior:

- soft-cancel reservation with status `Cancelled`, or
- soft-delete/deactivate reservation.

In either case, inactive reservation capacity must be released.

### 5.10 Direct orders remain supported

The existing `+ New order` flow remains available.

Direct orders continue to create an order code at confirmation.

---

## 6. UI Requirements

### 6.1 Entry points

On the orders root page, add a separate button alongside `+ New order`:

```text
+ Reservation
```

Visual requirements:

- use different coloring from the primary order button;
- keep it visible to both lab and clinic actors;
- lab users creating a reservation must select a target clinic, same as lab-created orders.

### 6.2 Reservation flow

The reservation creation/editing UI should mostly reuse the existing order flow:

1. Constructions / target clinic.
2. Material + shade.
3. Impression + delivery dates.
4. Overview / case name / note.
5. Reservation save and/or promotion action.

Reservation-specific UI labels should clearly say `Reservation`, not `Order`, where appropriate.

Existing reservations must support saving changes without promoting to an order. Promotion is a separate explicit action available from the reservation flow/detail UI.

### 6.3 Dual date picker

The date step for reservations must allow selecting both:

- impression date;
- delivery date.

Both dates should be visible in the same calendar UI, similar to room-booking or return-flight date selection.

Expected behavior:

- impression date and delivery date have different visual markers;
- if both dates are in a range, optionally shade the interval between them;
- changing impression date updates delivery minimum/recommendation;
- delivery dates before the computed delivery minimum are blocked for normal clinic users;
- lab users may override invalid delivery dates through the existing explicit override pattern.

### 6.4 Existing order date picker

The direct order flow should stay as-is for V1 and continue to use the current hidden/today impression behavior. Editable impression-date selection is reservation-only in V1.

The dual-date picker may be built in a reusable way, but direct order creation should not change as part of this feature.

### 6.5 List view display

Order/reservation list views should show both active orders and active reservations.

Reservation rows must be clearly marked, for example:

- a `Reservation` badge;
- compact material + tooth-count summary instead of an order code/reference;
- impression date and delivery date visible enough to distinguish expected intake from due date.

Cancelled, promoted, and automatically ignored/expired reservations must disappear from ordinary active list views. A separate historical/debug page for such reservations is optional follow-up work.

### 6.6 Calendar delivery-date display

The main calendar should display active non-expired reservations alongside orders on their requested delivery date.

Reservation delivery chips/rows should:

- be semi-transparent or visually lighter than orders;
- have an explicit `Reservation` indicator;
- use the reserving clinic color/accent for lab users;
- be clickable to open the reservation flow.

### 6.7 Calendar impression-date indicators

The main calendar should also show reservation impression dates as small indicators in the relevant date cell.

Expected behavior:

- indicator is smaller than delivery chips;
- indicator does not look like an order due item;
- clicking an impression indicator opens the reservation flow;
- if multiple reservation impressions are on the same date, provide a clear way to choose/open the relevant reservation (e.g. day popup list).

### 6.8 Day popup

When a calendar date has orders and reservations, day popups should include both.

Each item should identify:

- entity type: order or reservation;
- case/material/teeth summary;
- clinic identity for lab users;
- whether the date is the reservation's delivery date or impression date if relevant.

---

## 7. Backend / Domain Requirements

### 7.1 Reservation domain model

Add a reservation domain concept with fields equivalent to order fields where applicable.

Suggested fields:

```text
Id
ClinicCode
ClinicDisplayName
CreatedByMemberId
CreatedByMemberLabel
CaseName
ImpressionDate
ProductCategory
Material
WorkItems
RequestedDeliveryDate
Status
Shade
Notes
ColorNote
CalculatedCapacityUnits
CreatedAt
UpdatedAt
CreatedIp
CreatedUserAgent
PromotedOrderId nullable
PromotedOrderCode nullable
PromotedAt nullable
```

Suggested statuses:

```text
Active
Cancelled
Promoted
Expired/Ignored
```

`Expired/Ignored` may be a computed effective status rather than a persisted status, as long as capacity and ordinary display queries exclude such reservations consistently using lab-local time.

Created orders that came from reservations should expose traceability back to the reservation, preferably with a nullable `PromotedFromReservationId` field on the order/entity/DTO.

### 7.2 Reservation repository queries

The system needs repository support for:

- create reservation;
- get reservation by id/public id;
- update reservation;
- cancel reservation;
- promote reservation / mark promoted;
- list reservations for actor;
- list active non-expired reservations by delivery date range for capacity;
- list active non-expired reservations by impression date range for calendar indicators;
- list active non-expired reservations by either delivery or impression date for calendar display.

### 7.3 Capacity source abstraction

Capacity calculations must include both orders and active reservations.

Recommended requirement:

- extract capacity usage reads behind an abstraction that returns schedulable capacity consumers rather than only orders; or
- extend existing capacity queries to combine active orders + active reservations in application code.

Capacity queries must support excluding:

- current order id when editing an order;
- current reservation id when editing a reservation;
- current reservation id when promoting it to an order.

Capacity queries must also exclude reservations whose automatic ignore time has passed.

### 7.4 Scheduling input

The scheduling/recommendation service should accept an explicit impression timestamp/date for reservations.

For reservation V1, the UI captures only a date. The backend must convert that date to a deterministic scheduling timestamp that is treated as **after** the 11:00 lab cutoff.

Required behavior:

```text
selected reservation impression date => after-cutoff impression timestamp in lab local time
```

This means a reservation with an impression on a business day starts lead-time counting from the next business day, matching the product decision that reservations are considered after cutoff.

### 7.5 Commit-time revalidation

Reservation create, reservation update, and reservation promotion must revalidate capacity inside the same serialized write operation used for persistence.

The same race protections that exist for order create/update should apply.

### 7.6 Promotion transaction

Promotion must be a single domain operation.

Required behavior inside the serialized write operation:

1. Re-fetch reservation.
2. Verify actor can see/promote it. The reserving clinic and lab can promote an active reservation.
3. Verify reservation is active.
4. Re-run delivery validation using reservation impression date and excluding that reservation's capacity.
5. If invalid, apply existing lab override rules or reject.
6. Generate unique order code.
7. Create order from reservation data.
8. Mark reservation promoted and link to order.
9. Commit.

Rejected promotion must not create an order and must not change reservation status.

### 7.7 Audit and logs

Reservation mutations should create audit events:

- ReservationCreated;
- ReservationUpdated;
- ReservationCancelled;
- ReservationPromoted.

Recommendation logs should be extended or paralleled so a lab can inspect why a reservation's delivery date was accepted/recommended.

Minimum acceptable V1:

- successful reservation create/update writes a recommendation log linked to reservation, or a reservation-specific recommendation log;
- successful promotion writes the normal order recommendation log for the created order;
- lab overrides on reservation create/update/promotion write override logs with rules bypassed and reason.

If recommendation/override log schemas remain order-only, V1 must add clear reservation linkage fields or separate reservation log tables.

---

## 8. API Requirements

Exact paths are implementation-plan details, but V1 should provide API coverage equivalent to the following.

### 8.1 Reservation CRUD

```text
POST   /api/scheduling/reservations
GET    /api/scheduling/reservations/{id}
PUT    /api/scheduling/reservations/{id}
DELETE /api/scheduling/reservations/{id}
POST   /api/scheduling/reservations/{id}/promote
```

Access control:

- unauthenticated: `401`;
- clinic accessing another clinic's reservation: `404` or `403` with no data leak;
- reserving clinic: allowed to view, edit, cancel, and promote its own active reservations;
- lab: allowed for all reservations.

### 8.2 Reservation date availability

Provide a date-availability endpoint that can evaluate both impression and delivery dates for a reservation draft.

It may be a new endpoint or an extension of `/api/scheduling/dates`.

Response should include:

- impression date statuses;
- delivery date statuses;
- minimum/recommended delivery date;
- order/reservation capacity fields already available for lab/debug views;
- enough failed-rule data for lab override prompts.

### 8.3 Combined list/calendar APIs

Existing list/calendar APIs may be extended or parallel APIs may be introduced.

Required data for calendar:

- active orders by delivery date;
- active reservations by delivery date;
- active reservations by impression date;
- lab capacity indicators that include both active orders and active reservations.

DTOs must include an entity type discriminator, e.g.:

```json
{ "type": "order", ... }
{ "type": "reservation", ... }
```

### 8.4 Promotion response

Promotion should return at least:

```json
{
  "reservation": { ...promoted reservation... },
  "order": { ...created order with orderCode... }
}
```

The UI should route to the created order confirmation/review after successful promotion.

---

## 9. Scheduling Algorithm Requirements

For reservation create/update/promotion:

1. Validate work items and material.
2. Validate selected impression date:
   - future date;
   - not weekend/non-working day;
   - first-business-day-after-closure allowed;
   - no capacity check.
3. Resolve effective intake date from selected impression date using after-cutoff semantics.
4. Calculate material lead-time from config effective for the candidate delivery date.
5. Calculate minimum selectable delivery date.
6. Calculate reservation capacity units.
7. Evaluate candidate delivery dates against:
   - delivery calendar rules;
   - minimum lead time;
   - active order capacity;
   - active reservation capacity;
   - current entity exclusion where applicable.
8. Save only if selected delivery is valid, unless explicit lab override is supplied.

---

## 10. Acceptance Scenarios

### 10.1 Clinic creates future reservation

Given a clinic user selects a valid future impression date and available delivery date,
when they create a reservation,
then no order code is generated,
and the reservation appears for that clinic and the lab,
and its capacity is consumed on the selected delivery date/week.

### 10.2 Reservation blocks later order capacity

Given an active reservation consumes the remaining weekly capacity,
when a clinic tries to create an order in the same week,
then the order date is rejected/recommended later unless lab override is used.

### 10.3 Order blocks later reservation capacity

Given active orders consume capacity on a date/week,
when a clinic tries to create a reservation for that delivery date/week,
then reservation delivery selection respects that used capacity.

### 10.4 Editing reservation excludes itself

Given an active reservation already consumes capacity on Friday,
when it is edited without changing delivery date,
then validation excludes that reservation so it does not block itself.

### 10.5 Promotion replaces reservation capacity

Given an active reservation consumes capacity on Friday,
when the reserving clinic or lab promotes it to an order,
then an order is created with a generated code,
and the reservation becomes promoted,
and Friday capacity is counted once, not twice,
and the created order exposes traceability to the source reservation id.

### 10.6 Clinic visibility isolation

Given Clinic A and Clinic B have reservations,
when Clinic A views list/calendar,
then Clinic A sees only Clinic A reservations and orders.

### 10.7 Lab sees all reservations

Given multiple clinics have reservations,
when lab views list/calendar,
then lab sees all reservations and can identify clinics.

### 10.8 Impression-date restrictions differ from delivery-date restrictions

Given a Monday is the first business day after a weekend,
when choosing a reservation impression date,
then Monday is selectable if not otherwise non-working.

Given that same Monday is a delivery date,
then it remains blocked by the first-business-day-after-closure delivery rule unless lab override is used.

### 10.9 Impression date has no capacity check

Given a date has many reservations with the same impression date,
when another reservation selects that same impression date,
then impression selection is not blocked by capacity.

### 10.10 Calendar shows both reservation dates

Given a reservation has impression Wednesday and delivery Friday,
when viewing the calendar,
then Wednesday shows a small reservation impression indicator,
and Friday shows a semi-transparent reservation delivery item.

Clicking either opens the reservation flow.

### 10.11 Cancelled reservation releases capacity

Given a reservation consumes capacity,
when it is cancelled/deleted,
then subsequent date availability no longer counts its capacity.

### 10.12 Lab override for reservation delivery

Given a reservation delivery date violates weekly capacity,
when clinic tries to save it,
then save is rejected.

When lab saves it with explicit override confirmation and reason,
then reservation is saved and an override log is recorded.

### 10.13 Reservation automatically stops counting after impression grace day

Given a reservation has impression date `2026-06-24` and has not been promoted,
when local lab time reaches `2026-06-26 00:00`,
then the reservation no longer appears in ordinary active list/calendar views,
and it no longer consumes daily or weekly capacity.

### 10.14 Reservation uses after-cutoff impression semantics

Given a reservation has impression date Tuesday,
when delivery lead time is calculated,
then Tuesday is treated as after the cutoff and the effective intake business date is Wednesday, assuming Wednesday is a business day.

### 10.15 Reservation can be saved without promotion

Given an active reservation exists,
when the clinic or lab edits reservation details and saves without choosing promotion,
then the reservation remains a reservation, no order code is generated, and capacity is recalculated from the saved reservation data.

### 10.16 Lab offdays block impression dates

Given a date is configured as a lab offday,
when a user chooses a reservation impression date,
then that date is unavailable for impression selection even though impression-date capacity is not checked.

---

## 11. Resolved Product Decisions

1. Reservation impression dates respect all configured non-working days, including lab offdays.
2. Reservation impressions are treated as after the 11:00 cutoff.
3. Clinics can and are expected to promote their own reservations to orders; lab can also promote reservations.
4. Reservation edits can be saved without promotion. Promotion is a separate explicit action.
5. Cancelled and expired/ignored reservations disappear from normal views. A separate page for historical/cancelled/expired reservations is optional follow-up.
6. Reservations do not need a human-readable reference. Display compact material/tooth-count summaries instead.
7. Direct order creation remains as-is and does not gain editable impression date selection in V1.
8. Promoted orders should expose source reservation traceability, preferably `PromotedFromReservationId`.

Open follow-up: decide later whether to add a lab-only historical/debug page for cancelled, promoted, and expired/ignored reservations.

---

## 12. Summary

V1 reservations should behave as first-class capacity consumers without being real orders.

A reservation is visible to lab and its clinic, blocks capacity on its delivery date/week, displays alongside orders, shows its impression date as a calendar indicator, and can be atomically promoted into an order that replaces the reservation's capacity hold and generates the real order code.
