# Slice 1 - Material-Based Lead-Time Deadline Recommendation

## Goal

Replace the old stopgap minimum-date logic with a new material-based lead-time recommendation path that works end-to-end without capacity checks yet.

After this slice, the scheduler should calculate the earliest selectable deadline from:

- the current order material;
- distinct tooth count;
- hardcoded V1 lead-time values for this slice;
- the 11:00 AM lab-local intake cutoff;
- business-day counting;
- weekends/holidays;
- the existing first-business-day-after-weekend/closure deadline block.

Daily/weekly capacity, DB-backed scheduling config, recommendation logs, and override logs are intentionally deferred.

## Requirements Covered

Implement the lead-time/calendar parts of `plans/deadlines/v1-requirements.md`:

- Section 3.1: order impression timestamp, using current creation/request timestamp for now.
- Section 3.2: effective intake business date.
- Section 3.3: lead-time days counted as business days inclusively.
- Section 4.1: 11:00 AM local intake cutoff.
- Section 4.2: lead-time counting.
- Section 4.3: weekends/holidays do not count and are not selectable.
- Section 4.4: first business day after weekend/holiday is blocked only as a deadline, not as a lead-time business day.
- Section 5.2: PFM/PFZ extra lead-time formula, using hardcoded `TeethPerExtraLeadDay = 10` for this slice.
- Section 6: initial lead-time examples, hardcoded for this slice.
- Section 8.3 steps 1-4 and calendar part of step 6.
- Acceptance scenarios 14.1 through 14.8.

Explicitly out of scope for this slice:

- DB-backed material scheduling config.
- Capacity units and daily/weekly capacity checks.
- 60-day no-capacity search failure except where naturally needed to avoid runaway loops while finding a selectable calendar date.
- Recommendation/override persistence logs.
- Capacity-aware commit-time revalidation.
- General override expansion beyond existing lab before-minimum warning behavior.

## Current State / Context

Relevant current files:

- `Orders/SchedulingOrderService.cs`
  - Currently uses `ISchedulingConfigProvider.Current.FindWorkRule(...).MinBusinessDays` and sums lead time across work items.
  - `ValidateDeliveryDateForActorAsync` currently validates using `DateAvailabilityService.GetStatusAsync`.
- `Orders/DateAvailabilityService.cs`
  - Has existing non-working-day and first-business-day-after-closure status logic.
  - Its `CalculateMinimumDateAsync` uses the old non-inclusive min-day semantics and should not be used as-is for V1 lead-time calculation.
- `Orders/NonWorkingDayProvider.cs`
  - Keep this. It provides weekends/Bulgarian holidays.
- `Orders/OrderDraft.cs`, `Orders/OrderRecord.cs`
  - Currently store `DateOnly ImpressionDate`. V1 timestamp input can temporarily use server/order creation timestamp while keeping this persisted date field.
- `Web/SchedulingApi.cs`
  - `/api/scheduling/dates` drives the calendar.
  - `POST /api/scheduling/orders` and `PUT /api/scheduling/orders/{code}` must validate against the new lead-time/calendar logic.
- `Web/wwwroot/js/order-flow-view.js`
  - Sends `impressionDate` today via a hidden field.
  - Existing frontend already supports lab warning for before-minimum dates based on returned date statuses.

The existing JSON scheduling config and `WorkRule` types may remain for auth/session settings and old tests if needed, but they should no longer drive deadline lead-time calculation.

## Desired End State

### Hardcoded material lead-time values for this slice

Use these hardcoded values in a clearly isolated class/service so Slice 2 can replace them with DB-backed config:

| Material | Fixed lead-time business days | Extra lead-time |
| --- | ---: | --- |
| `Pmma` | 2 | none |
| `PmmaTelio` | 2 | none |
| `FullContourZirconia` | 3 | none |
| `GlassCeramics` | 4 | none |
| `Pfm` | 4 | `ceil(distinctToothCount / 10)` |
| `PfzLayeredZrCrown` | 4 | `ceil(distinctToothCount / 10)` |

Naming note: even though the requirements doc says “work type,” in this codebase V1 scheduling config is material-based. Do not add a new work-type concept.

### Effective intake date

For scheduling calculations, build an order impression timestamp as follows:

- For new-order date preview and new-order create: use `IClock.UtcNow` as the temporary impression timestamp.
- For update validation: preferably use the existing order's `CreatedAt` as the temporary impression timestamp, because current requirements say creation timestamp acts as impression timestamp until reservations exist.
- If updating the date-preview endpoint to include edit context is too large for this slice, document any preview/save mismatch risk and keep create/update backend validation authoritative.

Convert the timestamp to lab local time and apply:

```text
<= 11:00 local on a business day => same business date
>  11:00 local on a business day => next business date
weekend/holiday                 => next business date
```

Use Bulgaria/Sofia lab local time. Implement timezone resolution in a small helper that works on Windows and Linux, e.g. try `Europe/Sofia`, then `FLE Standard Time`, or equivalent.

### Lead-time calculation

Calculate total lead-time business days from material + distinct tooth count.

Count business days inclusively from the effective intake business date.

Example for 2 lead-time days:

```text
Effective intake: Tuesday
Lead-time day 1: Tuesday
Lead-time day 2: Wednesday
Earliest deadline candidate after lead time: Thursday
```

The earliest deadline must then be the first calendar-selectable date on/after that post-lead-time candidate:

- skip weekends/holidays;
- skip first business day after weekend/holiday as a selectable deadline;
- do not let first-business-day-after-weekend/holiday affect lead-time counting.

### Date statuses/API behavior

`/api/scheduling/dates` should return statuses consistent with the new minimum/recommended deadline.

For this slice, with no capacity checks, the first selectable status on or after the lead-time minimum is the recommended/earliest selectable date.

Existing date status shape may be reused:

- `isClosed`
- `isFirstBusinessDayAfterClosure`
- `isBeforeMinimum`
- `isSelectable`
- `reason`

No capacity fields are required yet.

### Save validation behavior

On create/update:

- backend must validate the selected deadline against the new lead-time/calendar rules;
- non-lab users cannot save before minimum or on closed/first-after-closure dates;
- preserve the current lab before-minimum override behavior if practical:
  - lab can save before minimum only if the date is not closed and not first-business-day-after-closure;
  - closed and first-after-closure are still blocked in this slice.

Full V1 override behavior is deferred.

## Implementation Plan

### 1. Domain/service design

Introduce a focused deadline recommendation/lead-time service in `Orders`, for example:

- `DeadlineRecommendationService`
- `MaterialLeadTimeConfigProvider` or similar hardcoded provider
- small value records such as `OrderSchedulingInput`, `DeadlineRecommendationResult`, if useful

Keep the V1 rules isolated from `SchedulingOrderService` as much as practical. `SchedulingOrderService` should delegate deadline/minimum calculations to the new service.

Suggested responsibilities:

- derive distinct tooth count from `OrderWorkItem.AllTeeth(...)`;
- resolve material lead-time config;
- calculate PFM/PFZ extra lead days;
- convert UTC timestamp to lab local time;
- resolve effective intake business date;
- calculate first date after inclusive lead-time business days;
- advance to first selectable deadline date using `DateAvailabilityService`/calendar logic.

Consider adding public calendar helpers to `DateAvailabilityService` rather than duplicating logic, e.g.:

- `IsClosedAsync(DateOnly date)`;
- `IsFirstBusinessDayAfterClosureAsync(DateOnly date)`;
- `CanSelectDeadlineAsync(DateOnly date)`;
- `GetStatusAsync(...)` can continue to call the same internals.

### 2. Replace old minimum-date logic

Update `SchedulingOrderService.CalculateMinimumDeliveryDateAsync` to use the new lead-time service.

The old implementation summing work-item-specific `WorkRule.MinBusinessDays` should be removed from the deadline path.

Keep `ISchedulingConfigProvider` only where still needed for auth/session behavior.

### 3. API integration

Update `Web/SchedulingApi.cs` only as needed so:

- `/api/scheduling/dates` returns the new `minimumDate` and statuses;
- order create/update validation uses the new backend validation;
- API tests can prove the new behavior end-to-end.

If adding edit context to `/api/scheduling/dates` is straightforward, allow the request to include an optional order code so update previews can use the existing order's `CreatedAt`. If not, defer and document.

### 4. UI integration

Keep UI changes minimal.

The existing calendar should continue to work from returned statuses. If response shape stays compatible, no UI change may be needed.

If date reasons change, make sure `orders-delivery-date-picker.js` and `order-flow-view.js` still show disabled/unavailable dates and preserve the lab before-minimum confirmation.

### 5. Tests

Use TDD where practical: add failing tests first for the new service and then implement.

Expected test areas:

- unit tests for lead-time calculation and intake cutoff;
- unit tests for calendar rule interaction;
- service tests for create/update validation;
- API tests for `/api/scheduling/dates` if practical.

Existing tests that assert old summed `WorkRule.MinBusinessDays` behavior should be updated or removed because that behavior is intentionally dropped.

## TDD Plan

Add or update tests to cover at least:

### Lead-time unit tests

1. PMMA before cutoff:
   - created/impression timestamp Tuesday 10:30 local;
   - PMMA fixed lead = 2;
   - expected effective intake Tuesday;
   - expected earliest deadline Thursday.

2. PMMA after cutoff:
   - Tuesday 11:30 local;
   - expected effective intake Wednesday;
   - expected earliest deadline Friday.

3. Order timestamp on Saturday:
   - expected effective intake Monday;
   - expected earliest deadline Wednesday for PMMA.

4. Holiday skipped for lead-time count:
   - Tuesday before a Wednesday holiday;
   - Wednesday skipped;
   - expected deadline Friday.

5. First business day after weekend is blocked only as deadline:
   - effective intake Friday;
   - PMMA lead day 1 Friday, lead day 2 Monday;
   - Monday counts as lead time but is not selectable;
   - expected deadline Tuesday.

6. PFM/PFZ extra lead-time:
   - 1 tooth => `4 + ceil(1/10) = 5`;
   - 10 teeth => `4 + ceil(10/10) = 5`;
   - 11 teeth => `4 + ceil(11/10) = 6`.

### SchedulingOrderService tests

- New create/update validation rejects non-lab selected date before the new minimum.
- Lab before-minimum override behavior remains for non-closed/non-first-after-closure dates.
- Closed dates and first-business-day-after-closure dates remain blocked for everyone in this slice.
- Multiple work items count distinct teeth and do not sum independent work-rule lead times.

### API tests if practical

- `/api/scheduling/dates` returns a `minimumDate` matching the new material-based lead-time calculation.
- Returned statuses mark dates before minimum unavailable.
- First business day after weekend/closure remains unavailable even if it is after the minimum.

## Validation Plan

### Automated validation

Run:

```bash
dotnet test Spark3Dent.sln --no-restore --verbosity quiet
```

If a running Web process locks DLLs, stop it and rerun.

Expected: all tests pass.

### Requirements cross-check

At the end of implementation, explicitly check this slice against `plans/deadlines/v1-requirements.md`:

- [ ] Section 4.1 intake cutoff is implemented for scheduling calculations.
- [ ] Section 4.2 inclusive business-day lead-time counting is implemented.
- [ ] Section 4.3 weekends/holidays do not count and are not selectable.
- [ ] Section 4.4 first-business-day-after-weekend/holiday is blocked only as a deadline, not as lead-time.
- [ ] Section 5.2 PFM/PFZ extra lead-time formula is implemented with hardcoded `TeethPerExtraLeadDay = 10` for this slice.
- [ ] Section 6 initial material lead-time examples are represented by hardcoded config.
- [ ] Acceptance scenarios 14.1-14.8 are covered by automated tests or explicitly manually checked.
- [ ] Capacity-related sections are intentionally not implemented in this slice.
- [ ] Logging/override-log sections are intentionally not implemented in this slice.

### Manual end-to-end validation

Perform at least one browser/manual check using the scheduler UI:

1. Start the Web app locally.
2. Log in as a clinic user.
3. Create a new order with one PMMA/temporary item.
4. Open the deadline calendar.
5. Independently calculate the expected earliest selectable date using:
   - current Bulgaria/Sofia local time;
   - 11:00 cutoff;
   - PMMA = 2 business days;
   - weekends/holidays;
   - first-business-day-after-weekend/closure blocked as deadline.
6. Confirm the UI selects/enables that expected earliest date and disables earlier normal-user dates.
7. Attempt to save an earlier disabled/before-minimum date as non-lab if possible; confirm save is rejected by backend.
8. Log in as lab and verify the existing before-minimum warning path still allows a non-closed/non-first-after-closure before-minimum date.

If local auth data is not available, perform an equivalent API-level/manual check with existing test helpers or seeded local credentials and document exactly what was checked.

## Review Notes for Implementing Agent

When handing this slice back for review, include:

- summary of files changed;
- new service/class names and where the hardcoded material config lives;
- tests added/updated;
- full automated test command and result;
- manual validation steps performed and result;
- completed requirements cross-check checklist;
- any deviations from this plan, especially around preview timestamp/edit timestamp behavior.

## Known Follow-ups

Deferred to later slices:

- Replace hardcoded material lead-time values with DB-backed material scheduling config.
- Add `CapacityUnitsPerTooth` to material config.
- Add calculated capacity units on orders.
- Add daily/weekly capacity config and capacity-aware search.
- Add recommendation logs and candidate audit trails.
- Add full lab manual override with reason/rules-bypassed logging.
- Add transaction-backed commit-time capacity revalidation.
