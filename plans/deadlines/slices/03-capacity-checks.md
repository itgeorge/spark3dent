# Slice 3 - Daily and Weekly Capacity Checks

## Goal

Add V1 simplified capacity behavior end-to-end.

After this slice, the scheduler should:

- calculate an order's capacity units from material config and selected tooth count;
- save recalculated capacity units on order create/update;
- read date-effective daily/weekly capacity limits from the database;
- recommend the earliest deadline that passes calendar, lead-time, daily capacity, and weekly capacity;
- mark capacity-full dates as unavailable in `/api/scheduling/dates`;
- reject create/update saves that would exceed daily or weekly capacity.

Recommendation logs, override logs/reason capture, and transaction-level concurrency protection remain out of scope for later slices.

## Requirements Covered

This slice implements these parts of `plans/deadlines/v1-requirements.md`:

- Section 4.5 capacity assignment: full order capacity assigned to selected deadline date and containing week.
- Section 4.6 capacity checks: candidate date must pass calendar, lead-time, daily capacity, and weekly capacity.
- Section 4.7 normal users cannot overbook. Manual override behavior remains mostly deferred.
- Section 4.8 orders included in capacity: non-cancelled orders consume capacity; cancelled orders do not.
- Section 4.9 rescheduling: current selected deadline consumes capacity; old deadlines do not.
- Section 5.3 daily/weekly capacity configuration as date-effective DB rows.
- Section 7.1 capacity formula: `teethCount * CapacityUnitsPerTooth` using `.NET decimal` application logic.
- Section 8.3 recommendation algorithm step 5/6 capacity portions.
- Section 11 daily/weekly usage queries, including excluding the current order on update.
- Section 11.3 week starts Monday and ends Sunday.
- Acceptance scenarios 14.9, 14.10, 14.11, 14.12, and the non-override part of 14.14.

Behavior from Slices 1 and 2 must remain intact:

- Section 4.1 intake cutoff.
- Section 4.2 inclusive business-day lead-time counting.
- Section 4.3 weekends/holidays.
- Section 4.4 first-business-day-after-weekend/holiday blocked only as deadline.
- Section 5.1/5.2 DB-backed material scheduling config.
- Acceptance scenarios 14.1-14.8.

Explicitly out of scope for this slice:

- lab-technician capacity overbooking override flow;
- override reason capture and `DeadlineOverrideLog`;
- recommendation logs and candidate-date audit trail persistence;
- transaction-backed concurrent writer serialization/retry behavior;
- admin UI/API for editing capacity configs.

## Current State / Context

Current implementation after Slice 2:

- `Orders/DeadlineRecommendationService.cs`
  - Calculates effective intake, lead-time days, and earliest selectable deadline from material DB config and calendar rules.
  - Does not know about existing orders or capacity.
- `Orders/MaterialSchedulingConfig.cs`
  - `MaterialSchedulingConfig.CapacityUnitsPerTooth` exists and is DB-backed.
- `Database/SchedulingMaterialConfigs`
  - Seeded material config exists, currently with placeholder `CapacityUnitsPerTooth = 1.0` values.
- `Orders/SchedulingOrderService.cs`
  - Uses `DeadlineRecommendationService` for date preview and create/update validation.
  - Allows legacy lab before-minimum selection when the date is not closed and not first business day after closure.
- `Orders/OrderRecord.cs` and `Database/Entities/SchedulingOrderEntity.cs`
  - No saved calculated capacity units yet.
- `IOrderRepository` / `SqliteOrderRepo`
  - Can list calendar orders by deadline range, but has no capacity-specific active-order query.
- `Web/SchedulingApi.cs`
  - `/api/scheduling/dates` returns `DeliveryDateStatus` objects compatible with the current UI.

## Desired End State

### Capacity units on orders

Add saved calculated capacity units to scheduling orders.

Suggested domain/API shape:

```csharp
public sealed record OrderRecord(..., decimal? CalculatedCapacityUnits = null);
```

Use nullable storage if that simplifies migration/backward compatibility. New and updated orders must always save a non-null value.

Migration guidance:

- Add `CalculatedCapacityUnits` to `SchedulingOrders`.
- Prefer SQLite `TEXT` storage for decimal values, consistent with `SchedulingMaterialConfigEntity.CapacityUnitsPerTooth`, unless the implementing agent deliberately chooses another safe strategy and documents it.
- Existing rows may have `NULL` capacity units. Capacity usage calculation should still count them by falling back to current material config and work-item tooth count until those orders are edited and saved with a calculated value.

The JSON order DTO may expose `calculatedCapacityUnits`; this is useful for API/manual validation and harmless for the UI.

### Capacity configuration table

Add a date-effective capacity config table.

Suggested entity/table:

- Entity: `SchedulingCapacityConfigEntity`
- Table: `SchedulingCapacityConfigs`

Suggested columns:

```text
Id INTEGER PRIMARY KEY AUTOINCREMENT
ActiveFromDate DATE NOT NULL
DailyCapacityUnits decimal/TEXT NOT NULL
WeeklyCapacityUnits decimal/TEXT NOT NULL
CreatedAt TEXT NOT NULL
UpdatedAt TEXT NOT NULL
```

Suggested indexes/constraints:

- unique index on `ActiveFromDate`;
- check constraints or application validation for positive daily/weekly capacity where practical.

Seed one initial row with generous placeholder limits so existing development flows are not unexpectedly blocked. Suggested seed:

| ActiveFromDate | DailyCapacityUnits | WeeklyCapacityUnits |
| --- | ---: | ---: |
| `2026-01-01` | `100.0` | `500.0` |

Tests can edit or insert tighter rows to prove capacity behavior.

Lookup rule:

```text
For a candidate deadline date, use the latest row where ActiveFromDate <= candidate deadline date.
```

Missing capacity config for a candidate date should fail clearly.

### Domain services and records

Introduce explicit capacity concepts in `Orders`, for example:

```csharp
public sealed record SchedulingCapacityConfig(
    long Id,
    DateOnly ActiveFromDate,
    decimal DailyCapacityUnits,
    decimal WeeklyCapacityUnits);

public interface ISchedulingCapacityConfigProvider
{
    Task<SchedulingCapacityConfig> GetForDateAsync(DateOnly date, CancellationToken ct = default);
    Task<IReadOnlyList<SchedulingCapacityConfig>> ListAsync(CancellationToken ct = default);
}

public sealed record CapacityUsage(decimal DailyUsed, decimal WeeklyUsed);
```

Recommended service responsibilities:

- `DeadlineRecommendationService` should remain the orchestrator for recommendation/validation if practical.
- Add helper methods or a small `OrderCapacityService` if it keeps responsibilities clearer.
- Capacity calculation should use:

```text
distinct selected tooth count * materialConfig.CapacityUnitsPerTooth
```

- Weekly usage should use Monday-Sunday weeks.
- Capacity usage should include all non-cancelled orders across all clinics, not actor-scoped calendar visibility.
- When validating an update, exclude the current order id so it is not double-counted.
- When checking existing orders with `CalculatedCapacityUnits == null`, calculate a fallback capacity from that order's current material/work-items and current material config. This is only for legacy/backward compatibility; newly created/updated orders must persist the calculated snapshot.

### Repository support

Add repository methods that support capacity without relying on SQLite decimal aggregation.

Suggested approach:

```csharp
Task<IReadOnlyList<OrderRecord>> ListActiveOrdersByDeadlineRangeAsync(DateOnly start, DateOnly end, CancellationToken ct = default);
```

Then sum capacity in application code using `decimal`.

This avoids SQLite decimal SUM/mapping pitfalls and makes fallback calculation for legacy rows straightforward.

Alternative repository-level daily/weekly usage methods are acceptable if they preserve decimal correctness and handle legacy null capacity intentionally.

### Recommendation behavior

Extend recommendation to search capacity-aware candidate dates:

1. Compute minimum deadline from Slice 1 lead-time/calendar rules.
2. Calculate new order capacity.
3. For each candidate date from minimum deadline through `minimumDeadlineDate.AddDays(60)`:
   - skip dates that are not selectable deadlines;
   - load active capacity config for that date;
   - calculate existing daily usage, excluding current order if editing;
   - calculate existing weekly usage, excluding current order if editing;
   - accept only if both `dailyUsed + orderCapacity <= dailyLimit` and `weeklyUsed + orderCapacity <= weeklyLimit`.
4. If no date is found, return/throw a clear manual-scheduling-required style error.

A dedicated result type is recommended so API/date status code can distinguish failed rules:

```csharp
public enum DeadlineValidationRule
{
    MinimumLeadTime,
    CalendarDeadlineBlocked,
    DailyCapacityExceeded,
    WeeklyCapacityExceeded,
    SearchFailure,
    Other
}
```

The exact type names are flexible, but keep domain names explicit.

### Date-status API behavior

`/api/scheduling/dates` should remain compatible with the current UI but become capacity-aware:

- dates before minimum remain unavailable with reason `Before minimum lead time`;
- closed dates remain unavailable;
- first business day after closure remains unavailable;
- dates whose daily capacity would be exceeded are unavailable with reason like `Daily capacity exceeded`;
- dates whose weekly capacity would be exceeded are unavailable with reason like `Weekly capacity exceeded`;
- if both daily and weekly fail, include a clear combined reason.

It is acceptable to extend `DeliveryDateStatus` with additive JSON fields such as:

```csharp
bool IsDailyCapacityExceeded
bool IsWeeklyCapacityExceeded
decimal? OrderCapacityUnits
decimal? ExistingDailyCapacityUsed
decimal? ExistingWeeklyCapacityUsed
decimal? DailyCapacityLimit
decimal? WeeklyCapacityLimit
```

Do not remove or rename existing fields the frontend relies on: `date`, `isClosed`, `isFirstBusinessDayAfterClosure`, `isBeforeMinimum`, `isSelectable`, `reason`.

### Save validation behavior

Post-implementation note: the current code intentionally permits a single large order to exceed the daily capacity limit when the selected day has no existing active usage. Daily capacity is treated as a same-day stacking guard; once `ExistingDailyCapacityUsed > 0`, additional orders are rejected if they exceed the daily limit. Weekly capacity still rejects oversized/overbooked weeks unless a valid lab override is used.

Create/update must revalidate against capacity before saving.

For this slice:

- Non-lab users cannot save dates that violate daily or weekly capacity.
- Lab users also cannot save dates that violate capacity yet; full capacity override is deferred to the manual override slice.
- Existing lab before-minimum override behavior may remain, but only when the selected date's only failed rule is `MinimumLeadTime`. If the same selected date also violates daily/weekly capacity or calendar closure rules, reject it until the full override slice.
- New/updated orders must persist `CalculatedCapacityUnits` from the current material config and selected teeth.

Transaction-backed race protection is explicitly deferred to Slice 4. However, the normal save path should perform validation immediately before create/update as it does today.

## Implementation Plan

### 1. Add capacity domain types

- Add `SchedulingCapacityConfig`, `ISchedulingCapacityConfigProvider`, `CapacityUsage`, and failed-rule enum/result types in `Orders`.
- Add shared Monday-week helper if useful, e.g. `SchedulingWeek.GetWeekRange(DateOnly date)`.
- Add capacity calculation helper using distinct teeth and `MaterialSchedulingConfig.CapacityUnitsPerTooth`.

### 2. Add DB capacity config entity and migration

- Add `Database/Entities/SchedulingCapacityConfigEntity.cs`.
- Add `DbSet<SchedulingCapacityConfigEntity>` and EF model config in `AppDbContext`.
- Add migration, e.g. `AddSchedulingCapacityConfigsAndOrderCapacity`.
- Migration should:
  - add `SchedulingCapacityConfigs` table;
  - seed initial capacity config row;
  - add nullable `CalculatedCapacityUnits` column to `SchedulingOrders`.
- Update `AppDbContextModelSnapshot` and ensure `PendingModelChangesTest` remains green.

If using EF CLI:

```bash
dotnet ef migrations add AddSchedulingCapacityConfigsAndOrderCapacity --project Database --startup-project Cli
```

### 3. Add DB-backed capacity config provider

- Implement `ISchedulingCapacityConfigProvider` in `Database`, e.g. `SqliteSchedulingCapacityConfigProvider`.
- `GetForDateAsync(date)` should return latest config with `ActiveFromDate <= date`.
- Validate positive daily/weekly limits in provider or service with useful errors.
- Register provider in DI in `Web/WebProgram.cs` and tests as needed.

### 4. Extend order model and repository

- Add `CalculatedCapacityUnits` to `OrderRecord`, entity mapping, DTO output, and tests.
- Update `SqliteOrderRepo` serialization/mapping.
- Add repository method for active orders in a deadline range across all clinics.
- Implement in-memory test repository support.
- Ensure cancelled orders are excluded from capacity queries.

### 5. Extend recommendation and validation logic

- Extend `OrderSchedulingInput` with optional current/excluded order id/code if needed for update validation.
- Keep the Slice 1 lead-time calculation intact.
- Add capacity-aware recommendation search.
- Add selected-date validation that returns failed rules instead of relying only on `DateAvailabilityService.GetStatusAsync`.
- Update `SchedulingOrderService`:
  - create: calculate capacity, validate selected deadline, save capacity;
  - update: use existing order's `CreatedAt` as impression timestamp, exclude existing order id from capacity, recalculate and save capacity.

### 6. Extend date statuses

- Update `SchedulingOrderService.GetDateStatusesAsync` or add a deadline service method that returns capacity-aware statuses.
- Preserve current JSON compatibility.
- Ensure edit-mode date preview passes `orderCode` through so the edited order is excluded from capacity checks when appropriate.
  - Current `/api/scheduling/dates` already accepts optional `orderCode` for timestamp alignment; extend that flow to pass the existing order id as the exclusion.

### 7. Update API and frontend only as needed

- API should return additive capacity fields in date statuses and order DTOs.
- The existing frontend should automatically disable capacity-full dates if `isSelectable` is false and show `reason` in the existing reason popover.
- No new capacity UI is required in this slice.

## TDD Plan

Use TDD where practical. Suggested tests to add/update before or alongside implementation:

### Orders unit tests

1. Capacity calculation:
   - material config capacity units = `1.5m`;
   - order has 4 distinct teeth;
   - calculated capacity = `6.0m`.

2. Daily capacity rejection:
   - minimum deadline is Thursday;
   - Thursday selectable but existing daily usage + new order exceeds daily limit;
   - Friday has capacity;
   - recommendation returns Friday and Thursday status/reason indicates `DailyCapacityExceeded`.

3. Weekly capacity rejection:
   - Thursday/Friday are selectable;
   - current week capacity is full;
   - following Monday is selectable and next week has capacity;
   - recommendation returns following Monday.

4. Cancelled orders do not consume capacity:
   - existing cancelled order due Friday would otherwise fill capacity;
   - Friday remains accepted.

5. Update excludes current order:
   - existing order consumes capacity on Friday;
   - updating the same order without changing date should not double-count itself.

6. Existing null capacity fallback:
   - existing active order has `CalculatedCapacityUnits == null`;
   - capacity usage still counts it based on current material config and teeth.

7. Lab before-minimum legacy override does not bypass capacity:
   - lab-selected early date fails minimum and daily capacity;
   - save validation rejects until full override slice.

### Database tests

1. Migration creates and seeds `SchedulingCapacityConfigs`.
2. `SchedulingOrders.CalculatedCapacityUnits` is persisted and read back as decimal/null.
3. Capacity config provider uses latest `ActiveFromDate <= candidateDate`.
4. Capacity config provider fails clearly when no row applies.
5. Active-order deadline-range query excludes cancelled orders and returns orders across all clinics.
6. `PendingModelChangesTest` remains green.

### Web/API tests

1. `/api/scheduling/dates` marks a daily-full candidate unavailable and later candidate selectable.
2. `/api/scheduling/dates` marks a weekly-full candidate unavailable and recommends next week.
3. Creating an order persists `calculatedCapacityUnits` in the order DTO and DB.
4. Non-lab create/update that would exceed capacity returns `400` with a clear error and no order mutation.
5. Updating an order excludes itself from capacity checks.
6. Cancelling an order releases its capacity for subsequent recommendations.

## Validation Plan

### Automated validation

Run:

```bash
dotnet test Spark3Dent.sln --no-restore --verbosity quiet
```

If migrations/generated files require restore/build first, run the appropriate `dotnet test` command without `--no-restore` and document it.

Expected: all tests pass, including `Database.Tests.PendingModelChangesTest`.

### Requirements cross-check

At the end of implementation, explicitly check this slice against `plans/deadlines/v1-requirements.md`:

- [ ] Section 4.5 capacity assignment implemented for selected deadline date/week.
- [ ] Section 4.6 candidate dates require calendar, lead-time, daily capacity, and weekly capacity.
- [ ] Section 4.7 normal users cannot overbook capacity.
- [ ] Section 4.8 active non-cancelled orders consume capacity; cancelled orders do not.
- [ ] Section 4.9 rescheduling only counts current selected deadline; update validation excludes current order.
- [ ] Section 5.3 date-effective daily/weekly capacity config exists in DB and uses latest `ActiveFromDate <= candidateDate`.
- [ ] Section 7.1 capacity units use `teethCount * CapacityUnitsPerTooth` with `.NET decimal` application logic.
- [ ] Section 8.3 recommendation search includes daily/weekly capacity checks and stops after roughly 60 days from minimum deadline.
- [ ] Section 11 daily and weekly usage match V1 semantics.
- [ ] Section 11.3 week starts Monday and ends Sunday.
- [ ] Acceptance scenarios 14.9 and 14.10 are covered by automated or manual tests.
- [ ] Acceptance scenarios 14.11 and 14.12 are covered by automated or manual tests.
- [ ] Slice 1/2 lead-time and DB material config behavior still passes.
- [ ] Recommendation logs remain intentionally unimplemented until a later slice.
- [ ] Full manual override logging/reason flow remains intentionally unimplemented until a later slice.
- [ ] Transaction-backed concurrent commit-time revalidation remains intentionally deferred to Slice 4.

### Manual end-to-end validation

Perform at least one manual/API-level validation that proves capacity affects the working scheduler flow.

Suggested API-level check:

1. Start with a migrated local/test database.
2. Log in as a clinic user.
3. Insert or edit a `SchedulingCapacityConfigs` row so a known candidate date has a very low daily limit, e.g. `DailyCapacityUnits = 1.0` and a high weekly limit.
4. Create an order due on that candidate date with capacity `1.0`.
5. Call `/api/scheduling/dates` for a second equivalent order over a range containing that candidate and the next selectable day.
6. Confirm the candidate date is returned with `isSelectable: false` and reason `Daily capacity exceeded`, and the next suitable date is selectable.
7. Attempt to create the second order on the full candidate date.
8. Confirm the API rejects it with a clear `400` error.
9. Create the second order on the next recommended/selectable date and confirm it succeeds and returns `calculatedCapacityUnits`.
10. Cancel the first order.
11. Call `/api/scheduling/dates` again and confirm the original candidate date becomes selectable again.

Also perform a quick browser check if practical:

- open the order flow;
- confirm capacity-full dates are disabled in the calendar and show the capacity reason popover;
- confirm selecting a later available date still creates an order successfully.

## Review Notes for Implementing Agent

When handing this slice back for review, include:

- files changed;
- migration name and summary;
- new table/entity/provider names;
- decimal storage/query strategy used for capacity values;
- how existing orders with null/missing capacity are handled;
- tests added/updated;
- full automated test command and result;
- manual/API end-to-end validation performed and result;
- completed requirements cross-check checklist;
- any deviations from this plan.

## Known Follow-ups

Deferred to later slices:

- transaction-backed commit-time capacity revalidation/race protection;
- recommendation logs and candidate-date audit trail persistence;
- config snapshots in recommendation logs;
- full lab override warning/reason/rules-bypassed flow for capacity and calendar/minimum violations;
- `DeadlineOverrideLog` persistence;
- admin UI/API for editing material and capacity scheduling config, if needed;
- visual UI indicators for over-capacity days/weeks beyond basic disabled-date reasons.
