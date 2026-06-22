# Slice 5 - Deadline Recommendation Logging

## Goal

Persist an auditable deadline recommendation log whenever an order is successfully created or updated.

After this slice, the scheduler should retain enough information to explain later why the system accepted or recommended a deadline for an order, including:

- the effective intake business date;
- lead-time values used;
- calculated order capacity;
- minimum deadline from lead time/calendar rules;
- final recommended capacity-aware deadline;
- selected deadline saved on the order;
- material/capacity config values used;
- candidate-date checks considered during the recommendation search.

Manual override logs/reason capture remain out of scope for this slice.

## Requirements Covered

This slice implements the recommendation-log portions of `plans/deadlines/v1-requirements.md`:

- Section 8.2 recommendation output should include an explanation/log object.
- Section 8.3 recommendation algorithm should produce enough decision detail for later inspection.
- Section 9 deadline recommendation logging.
- Section 9.1 required recommendation log fields, adapted to current code concepts.
- Section 9.2 candidate-date audit trail, preferably serialized structured data for this slice.
- Section 9.3 config snapshotting.
- Section 15.2 explanations should be visible enough for debugging.

Behavior from previous slices must remain intact:

- Slice 1 lead-time/calendar logic.
- Slice 2 DB-backed material scheduling config.
- Slice 3 daily/weekly capacity checks and saved order capacity units.
- Slice 4 commit-time capacity revalidation.

Explicitly out of scope for this slice:

- manual override warning/reason flow;
- `DeadlineOverrideLog` persistence;
- allowing lab users to bypass capacity/calendar failures beyond existing before-minimum behavior;
- admin UI for editing scheduling config;
- visual UI for overcapacity days/weeks;
- changing public order create/update request shapes.

## Current State / Context

After Slice 4, the current implementation has:

- `Orders/DeadlineRecommendationService.cs`
  - Calculates effective intake, lead time, capacity-aware statuses, selected-date validation, and recommended dates.
  - Returns `DeadlineValidationResult`, but does not expose a full candidate-date audit trail or persist anything.
- `Orders/SchedulingOrderService.cs`
  - Create/update validate and save inside `ISchedulingWriteTransaction`.
  - Create/update save `CalculatedCapacityUnits` on the order.
  - Audit events are appended after successful order create/update.
- `Database/AppDbContext.cs`
  - Has scheduling order, material config, and capacity config tables.
  - Has no deadline recommendation log table.
- `Web/SchedulingApi.cs`
  - Order create/update responses return the order only.
  - `/api/scheduling/dates` is preview-only and should not create persistent recommendation logs.

Important product alignment already decided:

- Only create persistent recommendation logs when an order is created or updated successfully.
- Logs do not need to be created for every `/api/scheduling/dates` preview request.
- It is acceptable to write logs right after order commit rather than in the same transaction if that keeps the implementation simple.
- Losing a log due to an interruption after the order save is acceptable for V1 simplicity.

## Desired End State

### Recommendation log domain model

Add explicit domain records for recommendation logs. Exact names are flexible; suggested names:

```csharp
public sealed record DeadlineRecommendationLog(...);
public sealed record DeadlineRecommendationCandidateCheck(...);
public interface IDeadlineRecommendationLogRepository
{
    Task<DeadlineRecommendationLog> AddAsync(DeadlineRecommendationLog log, CancellationToken ct = default);
    Task<IReadOnlyList<DeadlineRecommendationLog>> ListForOrderAsync(long orderId, CancellationToken ct = default);
}
```

The persisted log should be connected to the order by at least `OrderId`; also store `OrderCode` if useful for querying/debugging.

### Minimum log fields

Map the requirements terminology to current code concepts. Suggested persisted columns:

```text
Id
OrderId
OrderCode
CreatedAtUtc
CreatedByOrganizationType
CreatedByOrganizationCode
CreatedByMemberId
CreatedByMemberLabel
OrderCreatedAtUtc / ImpressionTimestampUtc
EffectiveIntakeBusinessDate
CutoffTimeUsed
Material
ToothCount
LeadTimeBusinessDaysUsed
FixedLeadTimeBusinessDaysUsed
ExtraLeadTimeBusinessDaysUsed
TeethPerExtraLeadDayUsed
CapacityUnitsPerToothUsed
CalculatedOrderCapacityUnits
MinimumDeadlineDateFromLeadTime
FinalRecommendedDeadlineDate
SelectedDeadlineDate
SearchStartedAtDate
SearchEndedAtDate
SearchLimitDate
ResultStatus
FailureReason
CandidateChecksJson
ConfigSnapshotJson
```

Notes:

- `WorkType` from the requirements should be represented by current `Material` because V1 config is material-based in this codebase.
- `OrderCreatedAtUtc` can use the timestamp currently passed into recommendation logic:
  - create: `_clock.UtcNow` at create time;
  - update: existing order `CreatedAt`, matching current update validation behavior.
- `SelectedDeadlineDate` is useful even when it differs from the system recommended earliest date.
- For successful create/update logs, `ResultStatus` can be `Accepted` or `Success`; if the selected date is valid but later than the recommendation, this is still success.
- Failed create/update saves do not need persistent logs in this slice because no order mutation succeeded.

### Candidate-date audit trail

Store candidate checks as serialized structured JSON in one column for this slice. A child table is optional but not required.

Each candidate check should include the equivalent of:

```text
CandidateDate
IsSelectableDeadline
CalendarBlockReason
DailyCapacityLimitUsed
WeeklyCapacityLimitUsed
ExistingDailyCapacityUsed
ExistingWeeklyCapacityUsed
OrderCapacityUnits
DailyCapacityWouldPass
WeeklyCapacityWouldPass
Accepted
RejectionReasons
```

The candidate trail should cover the recommendation search from `MinimumDeadlineDateFromLeadTime` through the accepted recommended date, or through the search limit on failure if a failure object is produced internally.

For this slice's create/update logs, the typical path is successful, so candidate trail should at least include rejected capacity/calendar candidates and the accepted recommended candidate.

### Config snapshotting

Snapshot enough values to understand the decision even if config changes later.

At minimum snapshot:

- cutoff time used (`11:00`);
- material config used:
  - material;
  - fixed lead-time business days;
  - capacity units per tooth;
  - teeth per extra lead day if applicable;
- capacity limits used for each candidate date checked;
- accepted/recommended date daily and weekly capacity limits;
- calculated capacity units.

A JSON snapshot column is acceptable, but key fields above should also be queryable columns where straightforward.

### Recommendation service changes

Extend `DeadlineRecommendationService` so create/update flows can obtain a log-ready explanation without duplicating all calculations inconsistently.

Suggested approach:

- Add a result type such as `DeadlineRecommendationAudit` or `DeadlineRecommendationExplanation`.
- Add or refactor methods so validation can return:
  - current `DeadlineValidationResult` data;
  - recommendation metadata;
  - candidate checks;
  - material config snapshot;
  - capacity config snapshots.

Avoid introducing a second independent recommendation algorithm just for logging. The logged recommendation and the validation result used to save the order should come from the same underlying calculations.

For Slice 4 compatibility, create/update should build this explanation inside the serialized write transaction using the transaction-scoped order repository for active-order capacity reads. The log may then be persisted after the transaction commits.

### Persistence

Add a migration and SQLite repository for recommendation logs.

Suggested entity/table:

```text
SchedulingDeadlineRecommendationLogs
```

Suggested indexes:

- `OrderId`
- `OrderCode`
- `CreatedAtUtc` or equivalent Unix timestamp

Use existing project conventions for date/time columns. JSON columns can be `TEXT`.

### API/debug visibility

Add a small lab-visible way to inspect logs for manual validation and future debugging.

Suggested endpoint:

```text
GET /api/scheduling/orders/{code}/deadline-recommendation-logs
```

Recommended behavior:

- requires authentication;
- lab users may view any order's logs;
- clinic users may either be denied with `403` or allowed only for their own orders; prefer lab-only if simpler and clearer for debugging/admin use;
- returns logs ordered newest first;
- includes parsed candidate/config JSON if easy, otherwise returns the JSON text fields.

This endpoint is primarily for validation/debugging; no frontend UI is required in this slice.

### Order create/update integration

For create:

1. Validate draft and resolve target clinic as today.
2. Inside `ISchedulingWriteTransaction`:
   - run selected-date validation and produce log explanation using transaction-scoped order reads;
   - create the order with calculated capacity units.
3. After successful commit:
   - append order audit as today;
   - persist a recommendation log connected to the created order.

For update:

1. Inside `ISchedulingWriteTransaction`:
   - re-fetch authorized order;
   - validate/update as today;
   - produce log explanation using the existing order's `CreatedAt` and excluded order id.
2. After successful commit:
   - append update audit as today;
   - persist a recommendation log connected to the saved order.

If log persistence fails after a successful order save, prefer not to roll back the order. Either let the exception surface if that is simplest, or catch/log it if there is an existing logging pattern. Document the chosen behavior in the handoff notes.

## TDD Plan

Use TDD where practical.

### Orders unit tests

1. Recommendation explanation contains lead-time and config snapshot:
   - arrange PMMA or zirconia config;
   - run create/update validation path;
   - assert effective intake, lead days, fixed lead days, capacity units per tooth, calculated capacity, minimum date, and recommended date are populated.

2. PFM/PFZ explanation captures extra lead days:
   - arrange `TeethPerExtraLeadDay = 10`;
   - use 11 teeth;
   - assert fixed lead days, extra lead days, total lead days, and teeth-per-extra are logged/exposed.

3. Candidate trail records capacity rejection:
   - arrange one candidate date daily-full or weekly-full;
   - assert candidate check includes existing usage, limits, order capacity, failed rule, and accepted=false;
   - assert later accepted candidate appears with accepted=true.

4. Create persists one recommendation log:
   - call `CreateOrderAsync`;
   - assert log repository received one log with the created order id/code and selected deadline.

5. Update persists another recommendation log:
   - create then update an order;
   - assert two logs exist for that order, newest from update.

6. Rejected create/update does not persist recommendation log:
   - force capacity validation failure for non-lab user;
   - assert no order mutation and no recommendation log.

### Database tests

1. Migration/model test:
   - EF model snapshot is updated;
   - pending model changes test passes.

2. SQLite log repository round-trip:
   - insert a log with candidate/config JSON;
   - list by order;
   - assert fields and JSON are preserved.

3. Ordering/index behavior:
   - insert multiple logs for the same order;
   - assert `ListForOrderAsync` returns newest first if that is the chosen contract.

### Web/API tests

1. Create order produces retrievable recommendation log:
   - create an order through API;
   - call lab log endpoint;
   - assert one log with selected deadline, recommended deadline, capacity units, and candidate checks.

2. Update order appends another recommendation log:
   - update the order through API;
   - call lab log endpoint;
   - assert two logs.

3. Access control:
   - unauthenticated request returns `401`;
   - non-lab request returns `403` if lab-only is chosen.

4. No logs for date preview:
   - call `/api/scheduling/dates`;
   - assert no recommendation log exists for any order, or at least no log is created as a side effect.

## Validation Plan

### Automated validation

Run:

```bash
dotnet test Spark3Dent.sln --no-restore --verbosity quiet
```

Expected: all tests pass, including new Orders, Database, and Web/API tests.

### Requirements cross-check

At the end of implementation, explicitly check this slice against `plans/deadlines/v1-requirements.md`:

- [x] Section 9 creates a separate recommendation log for successful order create.
- [x] Section 9 allows multiple recommendation logs for an order by appending on update/reschedule.
- [x] Section 9.1 required fields are implemented directly or mapped to current code concepts.
- [x] Section 9.2 candidate-date checks are persisted as structured JSON or child rows.
- [x] Section 9.3 snapshots lead-time/material/capacity config values used.
- [x] Section 15.2 explanation is inspectable enough for debugging, at least through tests and a lab/debug API endpoint.
- [x] `/api/scheduling/dates` preview does not create persistent logs.
- [x] Rejected create/update does not create recommendation logs in this slice.
- [x] Slice 1 lead-time tests still pass.
- [x] Slice 2 material-config tests still pass.
- [x] Slice 3 capacity tests still pass.
- [x] Slice 4 commit-time revalidation/concurrency tests still pass.
- [x] Manual override logging remains intentionally unimplemented until a later slice.

### Manual end-to-end validation

Perform at least one manual/API-level check proving logs are created and inspectable.

Suggested API validation:

1. Start the web app against a migrated local/test database.
2. Log in as a clinic user and create an order on a valid date.
3. Log in as a lab user.
4. Call:

   ```text
   GET /api/scheduling/orders/{code}/deadline-recommendation-logs
   ```

5. Confirm the response includes one log with:
   - order id/code;
   - selected deadline;
   - recommended deadline;
   - effective intake date;
   - lead-time fields;
   - calculated capacity;
   - candidate checks/config snapshot.
6. Update the order deadline or work items.
7. Call the log endpoint again and confirm there are now two logs for the order.

Optional capacity-specific manual check:

1. Configure a day/week so the earliest candidate is capacity-full.
2. Create an order whose recommendation skips that candidate.
3. Inspect the log and confirm the rejected candidate records `DailyCapacityExceeded` or `WeeklyCapacityExceeded` and the accepted later date.

## Review Notes for Implementing Agent

When handing this slice back for review, include:

- files changed;
- migration name and table/column summary;
- recommendation log domain/repository names;
- how create/update obtain log data from the same transaction-time validation/recommendation path;
- whether log persistence happens inside or after the order transaction, and why;
- sample log JSON or API response excerpt;
- tests added/updated;
- full automated test command and result;
- manual/API end-to-end validation performed and result;
- completed requirements cross-check checklist;
- any deviations from this plan.

## Known Follow-ups

Deferred to later slices:

- lab-technician manual override warning/reason/rules-bypassed flow;
- `DeadlineOverrideLog` persistence;
- stronger guarantee that logs are written atomically with orders, if later desired;
- admin UI/API for viewing recommendation logs beyond the debug endpoint;
- admin UI/API for editing material/capacity configs;
- visual capacity utilization indicators in the calendar UI.
