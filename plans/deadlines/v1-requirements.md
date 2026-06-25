# Dental Lab Order Deadline Recommendation - V1 Requirements

## 1\. Purpose

This document defines the first simplified version of the dental lab order deadline recommendation logic.

The goal is to calculate the earliest recommended deadline that a user may select for an order, based on:

* the order's material/work type;
* the number of teeth in the order;
* configured minimum lead-time rules;
* configured daily and weekly capacity limits;
* existing non-cancelled/non-deleted orders;
* calendar availability rules, including weekends and holidays;
* existing manual override rules.

This V1 algorithm is intentionally simpler than a full production scheduler. It does not simulate individual production phases such as casting, scanning, CAD/CAM, milling, ceramics, glazing, or oven time.

Instead, it uses:

1. a minimum lead-time gate;
2. a daily capacity gate;
3. a weekly capacity gate;
4. existing calendar deadline availability rules.

This is intended to be conservative, understandable, and easy to tune before introducing a more detailed scheduling model later.

\---

## 2\. Scope

### 2.1 In scope

This specification covers:

* calculating the earliest selectable deadline for a new or edited order;
* calculating material/work-type lead time;
* applying an 11:00 AM local intake cutoff;
* applying business-day, weekend, and holiday logic;
* checking daily capacity;
* checking weekly capacity;
* assigning capacity to the selected deadline date and its containing week;
* allowing lab-technician manual override;
* logging deadline recommendation decisions;
* logging manual overrides;
* snapshotting the configuration used for a recommendation;
* enforcing a 60-calendar-day search limit.

### 2.2 Out of scope

This specification does not cover:

* detailed phase-based production scheduling;
* modeling gypsum cast setting time;
* modeling CAD/CAM resource availability;
* modeling external milling-center delivery lead time separately;
* modeling ceramics oven batching;
* automatic optimization of existing orders;
* visual UI for overcapacity days/weeks;
* partial capacity release when an order is practically complete;
* material-capacity effective-date history beyond the current order type configuration;
* completion-state tracking for orders.

\---

## 3\. Definitions

### 3.1 Order impression date/time

The timestamp when the order impression is taken from the patient and delivered to the lab, or when the relevant scheduling calculation is requested. 
The current implementation makes it equal to the order creation date. But that is temporary and will likely change soon when we add "reservations" which allow entering an impression date in the future.
If this doc mentions "creation" date or similar as the base for date calculations, assume that's referring to the date when the order impression is/will be taken.

This timestamp must be interpreted in the lab's local time zone.

### 3.2 Effective intake business date

The business date from which lead-time counting begins.

This date depends on:

* the order impression timestamp;
* the 11:00 AM local cutoff;
* whether the impression date is a business day;
* weekends;
* holidays.

### 3.3 Lead-time days

The configured number of business days required for an order type before the order may be completed.

Lead-time days are counted inclusively starting from the effective intake business date.

The deadline itself is the first valid selectable business date after the configured lead-time days have elapsed.

### 3.4 Deadline date

The user-selected order completion date.

The deadline does not include delivery or travel time.

### 3.5 Capacity units

A decimal value representing the amount of simplified lab capacity consumed by an order.

Capacity units are calculated from the order's material/work type and tooth count.

### 3.6 Daily capacity

The maximum normal capacity units allowed for a specific deadline date.

Daily capacity is primarily a safeguard against too many orders being due on the same day.

Implementation note (post-V1 slices): the current scheduler intentionally treats daily capacity as a same-day stacking guard rather than a hard maximum for a single order. A single large order may exceed the nominal daily capacity when the day has no other active orders; once a day already has usage, additional orders are blocked if `existingDailyUsed + orderCapacityUnits > dailyLimit`. Weekly capacity remains a hard rough-cut cap unless explicitly overridden by a lab user.

### 3.7 Weekly capacity

The maximum normal capacity units allowed for the week containing a candidate deadline date.

Weekly capacity is the primary rough-cut production-capacity control.

### 3.8 Override

A manual lab-technician action that allows an order to be scheduled on a date that would otherwise be blocked by the recommendation rules.

Override orders still consume capacity and may push a day or week over configured capacity limits. They do NOT overconsume or shift over to the next week/day's capacity.

\---

## 4\. Core Business Rules

### 4.1 Intake cutoff

The order intake cutoff is always:

```text
11:00 AM local lab time
```

Rules:

* If an order is created before or exactly at 11:00 AM on a business day, the current business day is the effective intake business date.
* If an order is created after 11:00 AM on a business day, the effective intake business date is the next business day.
* If an order is created on a weekend or holiday, the effective intake business date is the next business day.
* The cutoff rule does not apply to non-business impression dates, because non-business days can never be effective intake business dates.

Recommended implementation detail:

```text
createdAtLocal.TimeOfDay <= 11:00 => before cutoff
createdAtLocal.TimeOfDay > 11:00  => after cutoff
```

### 4.2 Lead-time counting

Lead-time days are counted as business days, inclusively from the effective intake business date.

The candidate deadline is the first valid selectable date after the lead-time days have elapsed.

Equivalent conceptual formula:

```text
minimumDeadlineDate = first selectable business date after N counted lead-time business days
```

Example with 2 configured lead-time business days:

```text
Effective intake date: Tuesday
Lead-time day 1: Tuesday
Lead-time day 2: Wednesday
Earliest deadline: Thursday
```

### 4.3 Weekends and holidays

Weekends and holidays:

* do not count as lead-time days;
* do not receive capacity;
* are not selectable as deadline dates.

### 4.4 Existing first-business-day-after-weekend/holiday rule

The existing calendar system has a rule that disallows orders on each first business day after a weekend or holiday, due to potential delivery issues.

This rule must be applied only to deadline selection.

It must not affect capacity and must not affect lead-time counting.

Therefore, a first business day after a weekend or holiday:

* may count as a lead-time business day;
* may receive capacity;
* may belong to the weekly capacity calculation;
* must not be selectable as a normal deadline date unless manually overridden.

Example with 2 configured lead-time business days right before a weekend (no holidays):
```text
Effective intake date: Friday
Lead-time day 1: Friday
Lead-time day 2: Monday (still counted as a lead-time day even though deliveries not allowed)
Earliest deadline: Tuesday
```

Example with 2 configured lead-time business days before a weekend (no holidays) and that would have landed on a Monday if not for the first-business-day-after-weekend/holiday rule:
```text
Effective intake date: Thursday
Lead-time day 1: Thursday
Lead-time day 2: Monday
Earliest deadline: Tuesday (no deliveries on Monday even though lead time would fit)
```

### 4.5 Capacity assignment

In V1, an order's full calculated capacity load is assigned to:

* the selected deadline date for daily capacity purposes;
* the week containing the selected deadline date for weekly capacity purposes.

This is intentionally simplified. The system does not attempt to distribute the order's capacity across the actual production days before the deadline.

### 4.6 Capacity checks

A candidate deadline date is acceptable only if all of the following pass:

1. the candidate date is selectable according to the calendar system;
2. the candidate date satisfies the minimum lead-time rule;
3. the candidate date has enough available daily capacity;
4. the week containing the candidate date has enough available weekly capacity.

Daily and weekly capacity checks are an AND condition.

Both must pass unless the order is being scheduled through a valid lab-technician manual override.

### 4.7 Capacity overbooking

Normal user/customer date selection must not allow overbooking.

Overbooking is allowed only through explicit lab-technician override.

When an override is used:

* the order must still consume capacity like any other order;
* the day may exceed its daily capacity limit;
* the week may exceed its weekly capacity limit;
* the override must be logged;
* the warning shown to the technician should clearly indicate which rules are being bypassed.

### 4.8 Orders included in capacity

Orders that consume capacity:

* all non-cancelled orders;
* all non-deleted orders;
* existing orders even if they may be practically completed;
* manually overridden orders.

Orders that do not consume capacity:

* cancelled orders;
* deleted orders.

Important: the system currently does not have completed markers. Even if completed markers are introduced later, this V1 model should not automatically free weekly capacity just because a week's orders were completed early. Completing a week's worth of orders by Wednesday does not imply that another full week's worth of orders should become schedulable by Friday.

### 4.9 Order rescheduling

When an order is rescheduled:

* its capacity should be counted only against the currently active selected deadline date;
* old historical deadlines must not continue consuming capacity;
* previous recommendation and override logs should remain available for auditing.

### 4.10 Search limit

The recommendation algorithm must search at most 60 calendar days forward (from order impression date).

Implementation note (post-V1 slices): the current code applies the 60-calendar-day search window from the resolved effective intake business date, not directly from the raw impression timestamp. This was kept intentionally because all recommendation candidates are evaluated from the lab-effective intake date after cutoff/weekend/holiday adjustment.

If no acceptable deadline date is found within the search window, the system must return an error/manual-scheduling-required result.

This prevents infinite loops caused by invalid configuration, such as zero capacity for all future dates.

\---

## 5\. Configurable Data

### 5.1 Order type configuration

Each material/work type must have persisted database configuration.

Required fields:

```text
WorkType
FixedLeadTimeBusinessDays
CapacityUnitsPerTooth
```

Optional/recommended fields:

```text
DisplayName
IsActive
SortOrder
```

Capacity values must use the .NET `decimal` type in application logic.

### 5.2 PFM/PFZ extra lead-time configuration

For PFM and PFZ, the formula for extra lead time is hard-coded as:

```text
extraLeadDays = ceil(teethCount / configuredTeethPerExtraLeadDay)
totalLeadDays = fixedLeadDays + extraLeadDays
```

The number of teeth per extra lead day must be configured in the database.

Required field:

```text
TeethPerExtraLeadDay
```

Example:

```text
fixedLeadDays = 4
teethPerExtraLeadDay = 10

1 tooth  => 4 + ceil(1 / 10)  = 5 lead-time days
10 teeth => 4 + ceil(10 / 10) = 5 lead-time days
11 teeth => 4 + ceil(11 / 10) = 6 lead-time days
```

The formula itself should be hard-coded. The teeth-per-extra-lead-day value should be configurable.

### 5.3 Daily and weekly capacity configuration

Daily and weekly capacity limits must be configured in the database as date-effective rows.

Each capacity configuration row contains:

```text
ActiveFromDate
DailyCapacityUnits
WeeklyCapacityUnits
```

Lookup rule:

```text
For a candidate deadline date, use the latest capacity configuration row where ActiveFromDate <= candidateDeadlineDate.
```

No separate material-capacity effective-date system is required for V1.

### 5.4 Calendar configuration

The existing calendar system is responsible for knowing:

* weekends;
* holidays;
* non-selectable dates;
* special date limits;
* the existing first-business-day-after-weekend/holiday restriction;
* manual override behavior where applicable.

The deadline recommendation algorithm must call into or otherwise respect this calendar system.

\---

## 6\. Recommended Initial Lead-Time Examples

The exact values should be configured in the database. The following examples reflect the agreed V1 semantics after converting to standard lead-time phrasing where the effective intake day counts as part of the lead time.

Example values:

```text
PMMA:              2 business days
Full Contour Zr:   3 business days
LiSi:              4 business days
PFM/PFZ:           4 fixed business days + ceil(teeth / configuredTeethPerExtraLeadDay)
```

With `configuredTeethPerExtraLeadDay = 10`:

```text
PFM/PFZ, 1 tooth:   5 business days
PFM/PFZ, 10 teeth:  5 business days
PFM/PFZ, 11 teeth:  6 business days
```

\---

## 7\. Capacity Calculation

### 7.1 Basic formula

For V1:

```text
capacityUnits = teethCount \* CapacityUnitsPerTooth
```

Where:

* `teethCount` is the number of teeth in the order;
* `CapacityUnitsPerTooth` is configured per material/work type;
* the result is a decimal.

### 7.2 Future extension note

A future version may add a fixed base capacity per order, complexity multipliers, or status-based remaining capacity.

These are out of scope for V1 and should not be implemented unless explicitly planned.

\---

## 8\. Recommendation Algorithm

### 8.1 Inputs

The algorithm requires:

```text
OrderId or draft order identifier
Order impression timestamp
Material/work type
Tooth count
Existing active orders
Calendar system
Order type configuration
Daily/weekly capacity configuration
Lab local time zone
```

### 8.2 Output

The algorithm returns either:

```text
Success:
    Earliest recommended deadline date
    Explanation/log object

Failure:
    Manual scheduling required
    Error reason
    Explanation/log object
```

### 8.3 High-level algorithm

```text
1. Convert the order impression timestamp to local lab time.

2. Resolve the effective intake business date:
   - if impression date is weekend/holiday: next business day;
   - else if impression time > 11:00 AM: next business day;
   - else: current business day.

3. Determine the order's configured lead-time business days:
   - for normal types: fixed lead-time days;
   - for PFM/PFZ: fixed lead-time days + ceil(teeth / teethPerExtraLeadDay).

4. Calculate the minimum deadline date:
   - count lead-time business days inclusively from the effective intake business date;
   - choose the first valid selectable business date after those lead-time days have elapsed;
   - skip weekends/holidays and other non-selectable deadline dates as needed.

5. Calculate the order's capacity units.

6. Starting from the minimum deadline date, iterate candidate dates forward:
   - skip dates that are not selectable deadlines;
   - load the capacity configuration row active for the candidate date;
   - calculate existing daily capacity usage for the candidate date;
   - calculate existing weekly capacity usage for the week containing the candidate date;
   - check whether adding this order's capacity units would stay within both limits;
   - if yes, return candidate date;
   - if no, move to the next calendar date and continue.

7. Stop after 60 calendar days from the starting search date.
   - if no valid date is found, return manual-scheduling-required/error.
```

### 8.4 Pseudocode

```csharp
DateOnly FindEarliestRecommendedDeadline(Order order, DateTime createdAtUtc)
{
    var createdAtLocal = ConvertToLabLocalTime(createdAtUtc);

    var effectiveIntakeDate = ResolveEffectiveIntakeBusinessDate(createdAtLocal);

    var leadTimeDays = CalculateLeadTimeBusinessDays(order);

    var minimumDeadlineDate =
        CalculateFirstSelectableDateAfterLeadTime(effectiveIntakeDate, leadTimeDays);

    var orderCapacity = CalculateCapacityUnits(order);

    var searchLimitDate = minimumDeadlineDate.AddDays(60);

    for (var candidate = minimumDeadlineDate;
         candidate <= searchLimitDate;
         candidate = candidate.AddDays(1))
    {
        if (!Calendar.CanSelectDeadline(candidate))
            continue;

        var capacityConfig = GetCapacityConfigForDate(candidate);

        var dailyUsage = GetDailyCapacityUsage(candidate, excludingOrderId: order.Id);
        var weeklyUsage = GetWeeklyCapacityUsage(candidate, excludingOrderId: order.Id);

        var dailyPasses =
            dailyUsage + orderCapacity <= capacityConfig.DailyCapacityUnits;

        var weeklyPasses =
            weeklyUsage + orderCapacity <= capacityConfig.WeeklyCapacityUnits;

        if (dailyPasses \&\& weeklyPasses)
            return candidate;
    }

    throw new ManualSchedulingRequiredException(
        "No acceptable deadline found within 60 calendar days.");
}
```

\---

## 9\. Deadline Recommendation Logging

Each recommendation attempt should create a separate database entity connected to the order.

Multiple recommendation logs per order are allowed, because the lab may edit or reschedule orders.

### 9.1 Required fields

Recommended fields:

```text
Id
OrderId
CreatedAtUtc
CreatedByUserId or CreatedBySystem
OrderCreatedAtUtc
EffectiveIntakeBusinessDate
CutoffTimeUsed
WorkType
ToothCount
LeadTimeBusinessDaysUsed
FixedLeadTimeBusinessDaysUsed
ExtraLeadTimeBusinessDaysUsed
TeethPerExtraLeadDayUsed, if applicable
CapacityUnitsPerToothUsed
CalculatedOrderCapacityUnits
MinimumDeadlineDateFromLeadTime
FinalRecommendedDeadlineDate
SearchStartedAtDate
SearchEndedAtDate
SearchLimitDate
ResultStatus
FailureReason, if any
```

### 9.2 Candidate-date audit trail

The log should preferably include candidate-date checks, either as child rows or serialized structured data.

For each candidate date checked:

```text
CandidateDate
IsSelectableDeadline
CalendarBlockReason, if any
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

This is important for debugging and for future tuning.

### 9.3 Config snapshotting

The recommendation log must snapshot enough configuration to understand the decision later, even if current configuration changes.

At minimum, snapshot:

```text
cutoff time used
work type lead-time config used
PFM/PFZ teeth-per-extra-lead-day config used, if applicable
capacity-units-per-tooth config used
daily capacity limit used for accepted date
weekly capacity limit used for accepted date
candidate-date capacity values used during the search
```

\---

## 10\. Manual Override Logging

Manual overrides must be logged as separate database entities connected to the order.

### 10.1 Required behavior

Only users with the lab-technician role may create overrides.

The UI must show a warning before saving an override if the selected deadline violates one or more scheduling rules.

The override must still create or update the order deadline.

The overridden order must still consume capacity.

### 10.2 Required fields

Recommended fields:

```text
Id
OrderId
CreatedAtUtc
CreatedByUserId
SelectedDeadlineDate
SystemRecommendedDeadlineDate
OrderCapacityUnits
RulesBypassed
OverrideReason
RecommendationLogId, if available
DailyCapacityLimitUsed
WeeklyCapacityLimitUsed
DailyCapacityAfterOverride
WeeklyCapacityAfterOverride
```

### 10.3 Rules bypassed

The system should record which checks failed for the overridden date.

Possible values:

```text
MinimumLeadTime
CalendarDeadlineBlocked
DailyCapacityExceeded
WeeklyCapacityExceeded
SearchFailure
Other
```

Example:

```text
Chosen deadline: Wednesday
System recommended earliest deadline: Friday
Rules bypassed:
- MinimumLeadTime
- WeeklyCapacityExceeded
```

\---

## 11\. Existing Order Capacity Queries

### 11.1 Daily usage

Daily capacity usage for a candidate date is:

```text
sum(capacityUnits) of all non-cancelled/non-deleted orders
where selectedDeadlineDate == candidateDate
```

When recalculating a deadline for an existing order, exclude the current order from the existing usage calculation to avoid double-counting it.

### 11.2 Weekly usage

Weekly capacity usage for a candidate date is:

```text
sum(capacityUnits) of all non-cancelled/non-deleted orders
where selectedDeadlineDate is in the same week as candidateDate
```

When recalculating a deadline for an existing order, exclude the current order from the existing usage calculation to avoid double-counting it.

### 11.3 Week definition

The system must use the same week definition consistently for:

* capacity configuration;
* capacity usage;
* UI display;
* logging.

Recommended default for Bulgaria/local business context:

```text
Week starts on Monday.
Week ends on Sunday.
```

If the system already has a calendar/week definition, use the existing one.

\---

## 12\. Concurrency and Commit-Time Revalidation

The system must revalidate capacity when the deadline is committed/saved, not only when rendering available dates in the UI.

Reason:

```text
Order A sees Friday available.
Order B sees Friday available.
Both are saved.
Friday becomes overbooked unless capacity is rechecked at commit time.
```

Required behavior:

* The UI may show recommended available dates based on current data.
* On save, the backend must re-run the relevant checks.
* If the selected date is no longer valid, the save must either:

  * fail with a clear message; or
  * require lab-technician override if the user has permission.

Implementation details may depend on the database and transaction model.

\---

## 13\. API / Service Expectations

The implementation should provide a scheduling/deadline recommendation service with behavior equivalent to:

```csharp
DeadlineRecommendationResult RecommendDeadline(OrderSchedulingInput input);
DeadlineValidationResult ValidateSelectedDeadline(OrderSchedulingInput input, DateOnly selectedDeadline);
OverrideResult OverrideDeadline(OrderSchedulingInput input, DateOnly selectedDeadline, string reason);
```

### 13.1 Recommendation result

Suggested fields:

```text
RecommendedDeadlineDate
MinimumDeadlineDateFromLeadTime
OrderCapacityUnits
Explanation
RecommendationLogId
Warnings
```

### 13.2 Validation result

Suggested fields:

```text
IsValid
SelectedDeadlineDate
OrderCapacityUnits
FailedRules
RecommendedDeadlineDate, if selected date is invalid
Explanation
```

### 13.3 Override result

Suggested fields:

```text
OverrideSaved
SelectedDeadlineDate
RulesBypassed
OverrideLogId
RecommendationLogId
```

\---

## 14\. Acceptance Test Scenarios

### 14.1 PMMA before cutoff

Given:

```text
Work type: PMMA
Configured PMMA lead time: 2 business days
Order created: Tuesday 10:30 local time
Tuesday and Wednesday are business days
Thursday is a selectable deadline
Capacity is available
```

Expected:

```text
Effective intake date: Tuesday
Lead-time day 1: Tuesday
Lead-time day 2: Wednesday
Earliest recommended deadline: Thursday
```

### 14.2 PMMA after cutoff

Given:

```text
Work type: PMMA
Configured PMMA lead time: 2 business days
Order created: Tuesday 11:30 local time
Wednesday and Thursday are business days
Friday is a selectable deadline
Capacity is available
```

Expected:

```text
Effective intake date: Wednesday
Lead-time day 1: Wednesday
Lead-time day 2: Thursday
Earliest recommended deadline: Friday
```

### 14.3 Order created on Saturday

Given:

```text
Work type: PMMA
Configured PMMA lead time: 2 business days
Order created: Saturday
Monday and Tuesday are business days
Wednesday is a selectable deadline
Capacity is available
```

Expected:

```text
Effective intake date: Monday
Lead-time day 1: Monday
Lead-time day 2: Tuesday
Earliest recommended deadline: Wednesday
```

### 14.4 Order created before a holiday

Given:

```text
Work type: PMMA
Configured PMMA lead time: 2 business days
Order created: Tuesday 10:30 local time
Tuesday is a business day
Wednesday is a holiday
Thursday is a business day
Friday is a selectable deadline
Capacity is available
```

Expected:

```text
Effective intake date: Tuesday
Lead-time day 1: Tuesday
Wednesday is skipped
Lead-time day 2: Thursday
Earliest recommended deadline: Friday
```

### 14.5 First business day after weekend is blocked only as deadline

Given:

```text
Work type: PMMA
Configured PMMA lead time: 2 business days
Order created: Friday 10:30 local time
Friday and Monday are business days
Saturday and Sunday are weekend days
Monday is the first business day after a weekend
The calendar blocks Monday as a selectable deadline
Tuesday is selectable
Capacity is available
```

Expected:

```text
Effective intake date: Friday
Lead-time day 1: Friday
Saturday and Sunday are skipped
Lead-time day 2: Monday
Monday may count as lead time
Monday may receive capacity
Monday is not selectable as a deadline
Earliest recommended deadline: Tuesday
```

### 14.6 PFM 1 tooth lead time

Given:

```text
Work type: PFM
Fixed lead time: 4 business days
Teeth per extra lead day: 10
Tooth count: 1
```

Expected:

```text
Extra lead days: ceil(1 / 10) = 1
Total lead-time business days: 5
```

### 14.7 PFM 10 teeth lead time

Given:

```text
Work type: PFM
Fixed lead time: 4 business days
Teeth per extra lead day: 10
Tooth count: 10
```

Expected:

```text
Extra lead days: ceil(10 / 10) = 1
Total lead-time business days: 5
```

### 14.8 PFM 11 teeth lead time

Given:

```text
Work type: PFM
Fixed lead time: 4 business days
Teeth per extra lead day: 10
Tooth count: 11
```

Expected:

```text
Extra lead days: ceil(11 / 10) = 2
Total lead-time business days: 6
```

### 14.9 Daily capacity full

Given:

```text
Minimum deadline from lead time: Thursday
Thursday is selectable
Thursday daily capacity would be exceeded
Friday is selectable
Friday daily capacity is available
Weekly capacity is available
```

Expected:

```text
Recommended deadline: Friday
Recommendation log records that Thursday was rejected because DailyCapacityExceeded
```

### 14.10 Weekly capacity full

Given:

```text
Minimum deadline from lead time: Thursday
Thursday and Friday are selectable
The current week would exceed weekly capacity for both Thursday and Friday
The following Monday is selectable
The following week has available capacity
```

Expected:

```text
Recommended deadline: following Monday
Recommendation log records that Thursday and Friday were rejected because WeeklyCapacityExceeded
```

### 14.11 Existing completed-in-practice orders still consume capacity

Given:

```text
Several non-cancelled/non-deleted orders are due in the current week
The technician has practically completed them early
The system has not cancelled or deleted those orders
```

Expected:

```text
Those orders still consume weekly capacity.
The current week should not become fully available again merely because the technician worked ahead.
```

### 14.12 Cancelled/deleted orders do not consume capacity

Given:

```text
An order was previously scheduled for Friday
The order is now cancelled or deleted
```

Expected:

```text
The order does not contribute to Friday daily capacity.
The order does not contribute to that week's weekly capacity.
```

### 14.13 Override can overbook

Given:

```text
A lab technician selects a deadline that exceeds weekly capacity
The system warns the technician
The technician confirms the override and provides a reason
```

Expected:

```text
The order deadline is saved.
The order consumes capacity.
The week may now be over capacity.
A manual override log is created.
The log records WeeklyCapacityExceeded as a bypassed rule.
```

### 14.14 Non-technician cannot overbook

Given:

```text
A non-technician user selects a deadline that exceeds daily or weekly capacity
```

Expected:

```text
The deadline is rejected.
No override is created.
The user is shown the earliest recommended valid deadline.
```

### 14.15 Commit-time revalidation

Given:

```text
Two users view the same available date
The first user saves an order using that date
The second user attempts to save another order using the same date
The second save would exceed capacity
```

Expected:

```text
The second save is rejected unless the second user is a lab technician and confirms an override.
```

### 14.16 No date found within 60 days

Given:

```text
Capacity is configured as zero or all candidate dates are blocked
No acceptable date exists within 60 calendar days
```

Expected:

```text
The recommendation returns a manual-scheduling-required/error result.
The recommendation log records the failure reason.
```

\---

## 15\. Implementation Notes

### 15.1 Keep V1 intentionally simple

Do not introduce detailed production phases yet.

Do not model:

* cast preparation;
* cast setting;
* scan preparation;
* CAD design time;
* external milling wait;
* metal filing;
* ceramics work;
* glazing;
* oven cycles.

Those belong to a future coarse phase-based scheduler.

### 15.2 Make explanations visible enough for debugging

At minimum, internal/admin users should be able to inspect why a date was recommended.

Example explanation:

```text
Minimum date from lead time: Thursday
Thursday rejected: daily capacity exceeded
Friday rejected: weekly capacity exceeded
Monday accepted
Final recommended deadline: Monday
```

### 15.3 Prefer explicit domain concepts

The implementation should use domain names such as:

```text
EffectiveIntakeBusinessDate
LeadTimeBusinessDays
CapacityUnits
DailyCapacityLimit
WeeklyCapacityLimit
DeadlineRecommendationLog
DeadlineOverrideLog
```

Avoid ambiguous names such as:

```text
MinDays
Weight
Score
DateLimit
```

unless they are already established in the system.

\---

## 16\. Future Evolution Path

This V1 implementation should not block a later move to a hybrid rough-capacity plus coarse phase scheduler.

A future version may add:

* phase templates per work type;
* resource calendars;
* CAD/CAM capacity;
* ceramics bench capacity;
* oven capacity;
* external milling center lead-time modeling;
* status-aware remaining capacity;
* base capacity per order;
* complexity multipliers;
* visual overcapacity indicators;
* production schedule views.

To support that future direction, keep the V1 logic isolated in a scheduling/deadline recommendation service rather than spreading the rules throughout UI code.

\---

## 17\. Summary

The V1 deadline recommendation system should be a conservative, explainable promise-date guardrail.

A date is normally selectable only if:

```text
date >= minimum deadline from lead time
AND date is selectable according to calendar rules
AND daily capacity remains available
AND weekly capacity remains available
```

Manual lab-technician override may bypass these checks, but the override must be explicit, warned, logged, and still counted toward capacity.

The algorithm should be simple enough to tune from real lab behavior, while creating useful data for a later more detailed scheduling system.

