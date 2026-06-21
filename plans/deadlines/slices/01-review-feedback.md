# Slice 1 Review Feedback

Review of implementation for `plans/deadlines/slices/01-lead-time-recommendation.md`.

Automated validation run by reviewer:

```bash
dotnet test Spark3Dent.sln --no-restore --verbosity quiet
```

Result: all tests passed.

Overall, the implementation is a good fit for the slice: the new `DeadlineRecommendationService`, hardcoded material lead-time config, Sofia timezone cutoff handling, inclusive business-day counting, PFM/PFZ extra lead-time formula, and backend create/update validation are all in place.

Please address or explicitly resolve the following before we consider Slice 1 final.

## 1. Edit-mode date preview uses current time, while save validation uses existing `CreatedAt`

`SchedulingOrderService.UpdateOrderAsync` correctly validates updates using the existing order's `CreatedAt` as the temporary impression timestamp.

However, `/api/scheduling/dates` still calls the general draft/date-status path, which uses `_clock.UtcNow`. The edit UI also does not appear to pass an order code/edit context to the date-status endpoint.

This can cause an edit-preview/save mismatch for existing orders:

- an older order may still be valid on save because validation uses its original `CreatedAt`;
- but the edit calendar preview uses "now", may mark the current selected deadline unavailable, and may auto-advance the selected date in the UI.

Preferred fix for this slice:

- Add optional edit context to the date availability request, e.g. `orderCode`.
- In `SchedulingApi` `/api/scheduling/dates`, when `orderCode` is present:
  - load the order;
  - enforce actor visibility;
  - use that order's `CreatedAt` as the recommendation timestamp.
- Update `order-flow-view.js` to include the editing order code in the dates request when in edit mode.
- Add an API or service test proving edit date statuses use existing order `CreatedAt`, not current clock.

If this is intentionally deferred, please add a clear Known Follow-up note explaining the mismatch and why it is acceptable temporarily.

## 2. Remove or quarantine stale old lead-time config from the order deadline path

The old `WorkRule.MinBusinessDays` logic no longer drives scheduling, which is correct. There are still a couple of stale references that may confuse future work:

- `SchedulingOrderService` still accepts `ISchedulingConfigProvider configProvider` in its constructor but does not use it.
- `/api/scheduling/config` still returns old `workRules` / `defaultMinBusinessDays`, even though those are no longer deadline scheduling inputs.

Please at least remove the unused constructor dependency from `SchedulingOrderService` and its tests/DI call sites.

For `/api/scheduling/config`, either remove the obsolete lead-time fields from that response or add a clear comment/test naming that this endpoint is legacy/session-related and not used for deadline lead-time. Since the user explicitly said the old minimum config can be dropped, removing or minimizing this stale API surface is preferred if it does not cause churn.

## 3. Clean scratch files before final handoff

There is an untracked `.tmp/` directory containing helper scripts/html files. Please remove it from the working tree before final handoff unless it is intentionally needed and documented.

Do not remove unrelated existing repository artifacts unless you created them.

## Optional follow-up / naming clarity

`CalculateMinimumDeliveryDateAsync` now returns the earliest selectable deadline, not just the raw post-lead-time date. That matches the current API shape, but as later slices add logs with both `MinimumDeadlineDateFromLeadTime` and `FinalRecommendedDeadlineDate`, consider renaming or adding a richer result-returning method to avoid ambiguity.
