# Slice 6 - Lab Manual Deadline Override

## Goal

Add explicit lab-technician manual override for deadline scheduling rules.

After this slice, normal clinic users still cannot save dates that violate lead-time, calendar, daily capacity, or weekly capacity rules. Lab users (`actor.IsLab`) may intentionally save an otherwise-invalid deadline only after confirming the override and providing a reason. The override must be logged, the order must still save with calculated capacity units, and the overridden order must still consume capacity for future scheduling checks.

This slice should replace the existing legacy/silent lab before-minimum allowance with the explicit V1 override behavior.

## Requirements Covered

This slice implements these parts of `plans/deadlines/v1-requirements.md`:

- Section 4.6 capacity checks remain required unless a valid lab override is used.
- Section 4.7 capacity overbooking is allowed only through explicit lab-technician override.
- Section 4.8 manually overridden orders still consume capacity.
- Section 9 recommendation logs should remain available; override logs may reference the related recommendation log when available.
- Section 10 manual override logging.
- Section 10.1 only lab-technician users may create overrides.
- Section 10.1 UI must warn before saving an override when selected deadline violates scheduling rules.
- Section 10.1 override still creates/updates the order deadline.
- Section 10.2 override log fields, adapted to current code concepts.
- Section 10.3 rules bypassed values.
- Section 12 commit-time revalidation remains required; if a date became invalid, non-lab users are rejected and lab users need explicit override confirmation/reason.
- Section 13 `OverrideDeadline`-style behavior.
- Acceptance scenarios 14.13 and 14.14.

Behavior from previous slices must remain intact:

- Slice 1 lead-time/calendar recommendation.
- Slice 2 DB-backed material config.
- Slice 3 daily/weekly capacity checks and saved capacity units.
- Slice 4 commit-time revalidation/concurrency protection.
- Slice 5 recommendation logs.

Explicitly out of scope for this slice:

- admin UI for viewing/managing override logs beyond a simple debug/API inspection path;
- visual calendar overcapacity indicators;
- editing material/capacity config;
- detailed production scheduling;
- automatic optimization or moving existing orders.

## Current State / Context

Current implementation after Slice 5:

- `DeadlineRecommendationService`
  - Can validate requested dates and return failed rules via `DeadlineValidationResult`.
  - Can produce recommendation audit data used for `DeadlineRecommendationLog`.
- `SchedulingOrderService`
  - Create/update validate and save inside `ISchedulingWriteTransaction`.
  - Create/update persist a recommendation log after successful order persistence.
  - Currently still allows lab users to save before-minimum dates when not closed/first-business-day/capacity-exceeded, without explicit backend override reason/logging.
- `DeliveryDateStatus`
  - Carries `IsBeforeMinimum`, calendar closure flags, daily/weekly capacity flags, reason, usage, and limits.
- `DeadlineValidationRule`
  - Already includes `MinimumLeadTime`, `CalendarDeadlineBlocked`, `DailyCapacityExceeded`, `WeeklyCapacityExceeded`, `SearchFailure`, `Other`.
- Web UI
  - Existing date picker disables most unavailable dates.
  - Existing lab before-minimum UI warning exists, but has no override reason and does not cover calendar/capacity failures.
- DB
  - Has recommendation logs but no override log table.

## Desired End State

### Override request shape

Add explicit override fields to create/update order requests. Suggested names:

```csharp
public bool ConfirmDeadlineOverride { get; init; }
public string? DeadlineOverrideReason { get; init; }
```

Alternative names are acceptable if clear and consistent.

Behavior:

- If the selected deadline is valid, ignore/omit override fields and save normally.
- If the selected deadline is invalid:
  - clinic/non-lab users must receive `400` and no order/update is persisted;
  - lab users must receive `400` unless `ConfirmDeadlineOverride == true` and `DeadlineOverrideReason` is non-empty;
  - lab users with explicit confirmation and non-empty reason save the order/update and create an override log.

Do not rely on client-side checks only. The backend must revalidate inside the serialized write transaction and decide whether the request is a valid override based on current data.

### Override domain model

Add explicit domain records. Suggested names:

```csharp
public sealed record DeadlineOverrideLog(...);
public interface IDeadlineOverrideLogRepository
{
    Task<DeadlineOverrideLog> AddAsync(DeadlineOverrideLog log, CancellationToken ct = default);
    Task<IReadOnlyList<DeadlineOverrideLog>> ListForOrderAsync(long orderId, CancellationToken ct = default);
}
```

Suggested fields adapted from requirements:

```text
Id
OrderId
OrderCode
CreatedAtUtc
CreatedByOrganizationType
CreatedByOrganizationCode
CreatedByMemberId
CreatedByMemberLabel
SelectedDeadlineDate
SystemRecommendedDeadlineDate
OrderCapacityUnits
RulesBypassedJson or RulesBypassedCsv
OverrideReason
RecommendationLogId nullable
DailyCapacityLimitUsed
WeeklyCapacityLimitUsed
DailyCapacityAfterOverride
WeeklyCapacityAfterOverride
```

Recommended extra fields if easy:

```text
ExistingDailyCapacityUsed
ExistingWeeklyCapacityUsed
MinimumDeadlineDate
CalendarReason
```

`RulesBypassed` should use the existing `DeadlineValidationRule` names where possible:

- `MinimumLeadTime`
- `CalendarDeadlineBlocked`
- `DailyCapacityExceeded`
- `WeeklyCapacityExceeded`
- `SearchFailure`
- `Other`

### Override logging semantics

Create an override log only when the saved selected deadline failed one or more scheduling rules and was saved because of explicit lab override.

No override log should be created for:

- normal valid create/update saves;
- rejected non-lab saves;
- rejected lab saves without confirmation/reason;
- `/api/scheduling/dates` preview.

If a recommendation log is created for the same successful order mutation, prefer to persist the recommendation log first and set `RecommendationLogId` on the override log. If this complicates the implementation too much, a null recommendation reference is acceptable but must be documented in the handoff.

### Override validation/save behavior

Refactor `SchedulingOrderService` create/update validation around an explicit scheduling decision, for example:

```csharp
private sealed record DeadlineCommitDecision(
    DeadlineValidationWithAuditResult ValidationWithAudit,
    bool IsOverride,
    IReadOnlyList<DeadlineValidationRule> RulesBypassed,
    string? OverrideReason);
```

Inside the existing `ISchedulingWriteTransaction` create/update flow:

1. Re-run selected-date validation using the transaction-scoped repository.
2. If valid, save normally.
3. If invalid:
   - if `!actor.IsLab`, reject;
   - if missing explicit confirmation/reason, reject with a clear error that override is required;
   - if lab + confirmed + reason, save anyway and mark the mutation as override.
4. Always calculate/save `CalculatedCapacityUnits` from the validation result.
5. For update, continue excluding the current order id from capacity usage.

Important: override saves must not alter capacity usage by shifting capacity elsewhere. The order's full capacity remains assigned to the selected deadline date and containing Monday-Sunday week.

### Capacity values for override log

For the selected override date, log:

- selected date's daily/weekly limits if a capacity config exists;
- existing daily/weekly usage excluding the current order on update;
- order capacity units;
- daily/weekly usage after override.

For calendar-blocked closed dates/weekends/holidays, current `EvaluateDateAsync` may skip capacity config/usage. For this slice, choose one clear behavior and test it:

- preferred: still compute capacity usage/limits for override logging even when calendar-blocked, because override orders still consume capacity;
- acceptable simplification: record null capacity limits/usages for closed/calendar-blocked override dates and document it, while still ensuring the saved order contributes to future weekly/daily usage calculations.

### API error behavior

When a selected date is invalid and no valid override is present, return a clear `400` message. For lab users, include enough information for the UI to warn accurately.

Suggested response shape for create/update validation failures:

```json
{
  "error": "Delivery date 2026-06-05 is not available: Weekly capacity exceeded.",
  "overrideAllowed": true,
  "failedRules": ["WeeklyCapacityExceeded"],
  "recommendedDate": "2026-06-08"
}
```

Existing simple `{ error }` responses may be preserved for non-scheduling errors. The exact shape is flexible, but Web tests should assert lab invalid saves expose failed rules/override availability in some form.

### API/debug visibility

Add a lab-visible endpoint for override logs. Suggested endpoint:

```text
GET /api/scheduling/orders/{code}/deadline-override-logs
```

Recommended behavior:

- unauthenticated: `401`;
- non-lab: `403`;
- lab: list logs newest first;
- missing order: `404`.

No full frontend admin page is required.

### UI behavior

Update the order creation/edit flow enough to validate the full slice end-to-end from the browser.

Minimum expected UI behavior:

- For lab users, allow selecting a deadline that is unavailable due to lead-time, first-business-day/calendar block, or capacity.
- Before saving an invalid selected deadline, show a warning listing the failed/bypassed rules or reason.
- Require a non-empty override reason.
- Send override confirmation + reason to create/update API.
- For clinic users, blocked dates remain unselectable/rejected and no override prompt is shown.

Implementation suggestions:

- Replace/extend the existing before-minimum confirmation modal into a general deadline override modal.
- Store the selected date's `DeliveryDateStatus` client-side so the modal can show `reason`, capacity flags, and minimum/recommended date if available.
- If commit-time revalidation rejects a lab save because the date became invalid after preview, show the backend's failed rules and allow resubmitting with override reason.
- Keep UI minimal; this does not need a polished admin flow.

### Recommendation logs interaction

Successful override create/update should still create the normal recommendation log from Slice 5. This log explains the system recommendation/validation context. The override log explains the explicit bypass.

The override log should reference the recommendation log id if practical.

## TDD Plan

Use TDD where practical.

### Orders unit tests

1. Non-lab invalid date still rejects:
   - arrange daily or weekly capacity full;
   - clinic user attempts create/update;
   - assert save rejects and no override log is created.

2. Lab invalid date without override confirmation rejects:
   - arrange invalid date;
   - lab user attempts save without override fields;
   - assert clear error and no order/update/override log.

3. Lab invalid date with empty reason rejects:
   - confirmation true but blank reason;
   - assert reject and no override log.

4. Lab capacity override saves and logs:
   - arrange daily or weekly full date;
   - lab user saves with confirmation and reason;
   - assert order saved with selected date and calculated capacity;
   - assert override log has `DailyCapacityExceeded` or `WeeklyCapacityExceeded`, reason, selected date, recommended date, capacity after override.

5. Lab minimum/calendar override saves and logs:
   - before-minimum date and/or first-business-day-after-closure date;
   - assert override log has `MinimumLeadTime` and/or `CalendarDeadlineBlocked`.

6. Override order consumes future capacity:
   - lab overbooks a date/week;
   - subsequent normal clinic save for same date/week should see usage including overridden order and reject or recommend later.

7. Update override excludes current order:
   - update existing order to an invalid target date;
   - assert capacity calculations exclude the order's previous selected date/current id as appropriate.

8. Valid lab save does not create override log.

### Database tests

1. Override log repository round-trip:
   - insert a log with rules JSON/CSV and capacity fields;
   - list by order;
   - assert fields preserved and newest-first ordering.

2. Migration/model snapshot:
   - pending model changes test passes.

### Web/API tests

1. Clinic cannot override capacity:
   - create request with override fields from clinic user;
   - assert `400`/forbidden validation behavior and no override log.

2. Lab can override capacity:
   - create request with selected date capacity-full + confirmation/reason;
   - assert `201`, selected deadline saved, override log retrievable.

3. Lab cannot override without reason:
   - assert `400` and helpful error.

4. Lab can override calendar/minimum blocked date:
   - first business day after weekend or before-minimum date;
   - assert save succeeds with explicit reason and log records expected rule.

5. Override logs endpoint access control:
   - unauthenticated `401`;
   - clinic `403`;
   - lab `200`.

6. Commit-time stale capacity with override:
   - preview date available;
   - another order fills capacity;
   - clinic save rejects;
   - lab resubmits with override and reason;
   - assert save succeeds and override log records capacity rule.

### UI/manual tests

Automated frontend tests are optional if not already established. At minimum, perform manual browser or API validation below.

## Validation Plan

### Automated validation

Run:

```bash
dotnet test Spark3Dent.sln --no-restore --verbosity quiet
```

Expected: all tests pass, including new Orders, Database, and Web/API tests.

### Requirements cross-check

At the end of implementation, explicitly check this slice against `plans/deadlines/v1-requirements.md`:

- [ ] Section 4.7 normal users cannot overbook.
- [ ] Section 4.7 lab override can overbook daily/weekly capacity.
- [ ] Section 4.8 manually overridden orders still consume capacity.
- [ ] Section 10.1 only `actor.IsLab` can override.
- [ ] Section 10.1 UI/API requires explicit warning/confirmation and reason before override save.
- [ ] Section 10.1 override creates or updates the selected deadline.
- [ ] Section 10.2 override log records required fields or documented equivalents.
- [ ] Section 10.3 rules bypassed are recorded using expected rule names.
- [ ] Section 12 commit-time revalidation still happens before normal save or override decision.
- [ ] Acceptance scenario 14.13 override can overbook is covered.
- [ ] Acceptance scenario 14.14 non-technician cannot overbook is covered.
- [ ] Recommendation logs from Slice 5 still work for normal and override saves.
- [ ] `/api/scheduling/dates` preview does not create override logs.

### Manual end-to-end validation

Perform at least one manual/API-level check proving override works end-to-end.

Suggested API validation:

1. Start the web app against a migrated local/test database.
2. Configure or use a test setup where a known selectable date has no remaining daily or weekly capacity.
3. As a clinic user, attempt to create an order on that date.
   - Confirm it fails with a capacity reason.
4. As a lab user, attempt to create the same order/date without override reason.
   - Confirm it fails and indicates override/reason is required.
5. As a lab user, create the order with override confirmation and a reason.
   - Confirm it succeeds and the selected deadline is the blocked date.
6. Call:

   ```text
   GET /api/scheduling/orders/{code}/deadline-override-logs
   ```

   Confirm the response includes the reason and `DailyCapacityExceeded` or `WeeklyCapacityExceeded`.
7. Call the recommendation log endpoint from Slice 5 and confirm a recommendation log also exists.
8. Attempt another normal clinic order on the same date/week and confirm the overridden order contributes to capacity usage.

Suggested browser validation:

1. Log in as lab.
2. Choose a blocked date in the calendar.
3. Confirm an override warning appears.
4. Enter a reason and save.
5. Confirm order is created/updated on that date.

## Review Notes for Implementing Agent

When handing this slice back for review, include:

- files changed;
- migration name and table/column summary;
- override request field names and API response shape;
- override domain/repository names;
- how create/update decide between normal reject and lab override inside the serialized write transaction;
- how rules bypassed are computed;
- whether override log references recommendation log id;
- how override orders continue to consume capacity;
- tests added/updated;
- full automated test command and result;
- manual/API/browser validation performed and result;
- completed requirements cross-check checklist;
- any deviations from this plan.

## Known Follow-ups

Deferred to later slices:

- polished admin UI for browsing recommendation/override logs;
- visual capacity utilization and overcapacity indicators on calendar days/weeks;
- admin UI/API for editing material/capacity configs;
- making recommendation/override log writes atomic with order save if later desired;
- richer user-facing explanation text and localization.
