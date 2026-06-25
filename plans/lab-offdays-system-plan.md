# Lab Offdays System Implementation Plan

## Goal

Allow lab users to configure additional lab holiday/offday date ranges from `/scheduling-config`.

Configured lab offdays must behave exactly like existing non-working days:

- dates are closed/unselectable for order deadlines;
- the first business day after a lab offday/closure is unselectable;
- lead-time business-day counting skips them;
- weekly capacity is proportionally reduced when a lab offday falls on a weekday;
- existing orders/calendar UI shows those dates as non-working.

## Current Context

Relevant current pieces:

- `Orders/INonWorkingDayProvider` is the abstraction used by `DateAvailabilityService`.
- `BulgariaHardcodedNonWorkingDayProvider` currently returns weekends + hardcoded Bulgarian official holidays.
- `DateAvailabilityService` derives:
  - closed dates;
  - first business day after closure;
  - date status/reason.
- `DeadlineRecommendationService` consumes `DateAvailabilityService` for lead-time, selectability, and weekly holiday capacity reduction.
- `/api/scheduling/non-working-days` returns provider dates for calendar shading.
- `/api/scheduling/config` and `Web/wwwroot/scheduling-config.html` are lab-only configuration surfaces.
- The orders page calendar already uses `MonthCalendar` and `/api/scheduling/non-working-days` to shade non-working dates.

## Assumptions

- A lab offday record is an inclusive date range: `StartDate` through `EndDate`.
- UI creation/editing only needs start and end browser date pickers.
- No title/reason field is required for V1.
- Overlapping lab offday ranges should be rejected to keep edit/delete behavior clear.
- Removing an offday should hard-delete the row, with audit logging preserving the administrative action.
- Official holidays remain hardcoded/read-only; only lab offday records are editable.

## Data Model

Add a new EF entity/table:

```text
SchedulingLabOffdays
- Id                  INTEGER PK autoincrement
- StartDate           DateOnly, required
- EndDate             DateOnly, required
- CreatedAt           DateTimeOffset, required
- UpdatedAt           DateTimeOffset, required
```

Indexes:

```text
IX_SchedulingLabOffdays_StartDate
IX_SchedulingLabOffdays_EndDate
```

Validation:

- `StartDate` required.
- `EndDate` required.
- `EndDate >= StartDate`.
- No overlap with another row:
  - create: reject if `existing.StartDate <= new.EndDate && new.StartDate <= existing.EndDate`.
  - update: same, excluding the updated row.

Files likely touched:

- `Database/Entities/SchedulingLabOffdayEntity.cs`
- `Database/AppDbContext.cs`
- new EF migration under `Database/Migrations/`
- `Database.Tests/` repository/migration coverage

## Domain/API Contracts

Add domain records and repository interface, probably in `Orders/SchedulingConfigAdmin.cs` or a new `Orders/LabOffdays.cs`:

```csharp
public sealed record LabOffdayCreate(DateOnly StartDate, DateOnly EndDate);
public sealed record LabOffdayUpdate(DateOnly StartDate, DateOnly EndDate);
public sealed record LabOffdayRecord(long Id, DateOnly StartDate, DateOnly EndDate, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public interface ILabOffdayRepository
{
    Task<IReadOnlyList<LabOffdayRecord>> ListIntersectingAsync(DateOnly start, DateOnly end, CancellationToken ct = default);
    Task<IReadOnlyList<LabOffdayRecord>> ListAllAsync(CancellationToken ct = default);
    Task<LabOffdayRecord> CreateAsync(LabOffdayCreate create, DateTimeOffset now, CancellationToken ct = default);
    Task<LabOffdayRecord> UpdateAsync(long id, LabOffdayUpdate update, DateTimeOffset now, CancellationToken ct = default);
    Task DeleteAsync(long id, CancellationToken ct = default);
}
```

Implement with SQLite:

- `Database/SqliteLabOffdayRepository.cs`
- Register in `Web/WebProgram.cs`.

## Non-working Day Integration

Replace the direct registration of `BulgariaHardcodedNonWorkingDayProvider` as the app-wide `INonWorkingDayProvider` with a provider that unions official holidays/weekends with database lab offdays.

Recommended approach:

```text
DbBackedLabNonWorkingDayProvider : INonWorkingDayProvider
- depends on BulgariaHardcodedNonWorkingDayProvider or another base INonWorkingDayProvider
- depends on Func<AppDbContext> or ILabOffdayRepository
- GetNonWorkingDaysAsync(year):
  1. get base provider dates for year
  2. query lab offday rows intersecting Jan 1-Dec 31
  3. expand ranges to individual dates within that year
  4. return union
```

Because `DateAvailabilityService` is currently registered singleton, either:

1. implement the composite provider using the existing singleton-safe `Func<AppDbContext>` context factory; or
2. change `DateAvailabilityService` and consumers to scoped lifetimes.

Prefer option 1 for minimal lifetime churn.

Expected effect after this integration:

- `/api/scheduling/non-working-days` automatically includes lab offdays.
- `DateAvailabilityService.IsClosedAsync`, `CanSelectDeadlineAsync`, and `GetStatusAsync` automatically treat lab offdays as closures.
- `DeadlineRecommendationService` automatically skips lab offdays and reduces weekly capacity for weekday lab offdays.

## Lab API Endpoints

Add lab-only endpoints to `Web/SchedulingApi.cs` near existing scheduling config routes:

```text
GET    /api/scheduling/config/lab-offdays?start=YYYY-MM-DD&end=YYYY-MM-DD
POST   /api/scheduling/config/lab-offdays
PUT    /api/scheduling/config/lab-offdays/{id}
DELETE /api/scheduling/config/lab-offdays/{id}
```

`GET` response:

```json
{
  "start": "2026-03-01",
  "end": "2026-04-30",
  "items": [
    { "id": 1, "startDate": "2026-03-03", "endDate": "2026-03-03", "createdAt": "...", "updatedAt": "..." }
  ],
  "dates": ["2026-03-03"]
}
```

Notes:

- `dates` is expanded lab offday dates, useful for calendar rendering.
- Keep `/api/scheduling/non-working-days` as the source for all non-working day shading; the lab-offday endpoint is for editable lab-owned records.
- Validate max query range similarly to existing endpoints, e.g. 93 days for calendar requests, or use a larger admin-safe cap such as 370 days.
- All endpoints require `actor.IsLab`.
- Use `IAuditLog` for create/update/delete actions:
  - `SchedulingLabOffdayCreated`
  - `SchedulingLabOffdayUpdated`
  - `SchedulingLabOffdayDeleted`

## Scheduling Config UI

Extend `Web/wwwroot/scheduling-config.html` with a third card:

```text
[ Lab Offdays ]
- view mode toggle: Calendar | List
- Refresh
- New lab offday
```

### Calendar view

Use existing shared assets:

- `/css/month-calendar.css`
- `/js/month-calendar.js`

Behavior:

- Load visible calendar bounds via `MonthCalendar.bounds(month)`.
- Fetch both:
  - `/api/scheduling/non-working-days?start=...&end=...` for all non-working shading.
  - `/api/scheduling/config/lab-offdays?start=...&end=...` for editable lab offday records.
- Render all non-working days with existing calendar `non-working-day` styling.
- Add a distinct badge/chip on days covered by lab offday records, e.g. `Lab offday`.
- Clicking a lab offday badge opens the edit popup for that record.
- Clicking an empty date can optionally open the new popup with both dates prefilled to that date.

### List view

Show editable lab offday records, sorted by `StartDate` descending or upcoming-first:

```text
Start date | End date | Duration | Created | Updated | Actions
```

Actions:

- Edit
- Remove

### Create/Edit popup

Simple modal with browser date inputs:

```text
Start date [type=date]
End date   [type=date]
```

Buttons:

- Cancel
- Save
- Remove (edit mode only; either in the edit modal or as a row action with confirmation)

Client validation:

- both dates required;
- end date must be on/after start date.

Server remains source of truth for overlap/range validation.

## Tests

### Orders.Tests

Add tests around provider/date availability behavior:

- lab offday date is closed/unselectable;
- first business day after lab offday is unselectable;
- lead-time recommendation skips lab offday;
- weekday lab offday reduces effective weekly capacity, using the already implemented proportional capacity logic.

### Database.Tests

Add repository tests:

- create/list intersecting range;
- update row;
- delete row;
- reject `EndDate < StartDate`;
- reject overlapping ranges on create/update.

### Web.Tests

Add API tests:

- lab auth required for lab offday CRUD;
- clinic/non-lab access forbidden;
- create/update/delete responses and audit side effects;
- `/api/scheduling/non-working-days` includes lab offdays;
- `/api/scheduling/dates` marks lab offdays closed and first business day after closure blocked;
- order creation cannot select a lab offday without lab override path, matching existing holiday behavior.

### UI/manual smoke

- Open `/scheduling-config` as lab.
- Create a one-day offday.
- Verify it appears in calendar and list.
- Verify orders calendar shades it as non-working.
- Verify delivery date picker rejects it and the first business day after it.
- Edit the date range and verify calendars update.
- Remove it and verify calendars/date availability update.

## Implementation Slices

### Slice 1: Data model and repository

- Add entity/table/migration.
- Add domain records/interface.
- Implement SQLite repository.
- Add repository tests.

### Slice 2: Provider integration

- Add composite DB-backed non-working day provider.
- Update DI registration.
- Add tests proving lab offdays flow through `DateAvailabilityService` and `DeadlineRecommendationService`.

### Slice 3: API and audit

- Add lab-only CRUD endpoints.
- Include expanded date response for calendar convenience.
- Add audit logging.
- Add Web API tests.

### Slice 4: Scheduling config UI

- Add Lab Offdays card to `scheduling-config.html`.
- Include `month-calendar.css/js`.
- Implement calendar/list toggle, loading, chips, and popup create/edit/remove.
- Reuse existing modal/message/button styles where possible.

### Slice 5: End-to-end validation

- Run targeted tests:
  - `dotnet test Orders.Tests/Orders.Tests.csproj --no-restore`
  - `dotnet test Database.Tests/Database.Tests.csproj --no-restore`
  - `dotnet test Web.Tests/Web.Tests.csproj --no-restore --filter Scheduling`
- Manual smoke through `/scheduling-config` and `/orders`.

## Risks / Notes

- Provider lifetime is the main design point. Avoid injecting scoped EF repositories into singleton `DateAvailabilityService`; use the existing `Func<AppDbContext>` or adjust lifetimes carefully.
- If overlapping ranges are allowed, edit/remove UI becomes ambiguous when a date has multiple records. Rejecting overlap keeps V1 simpler.
- Deleting historical offdays can change future recalculations/recommendations for deadlines in those ranges. Audit logs should capture the admin action; recommendation logs already snapshot scheduling decisions for saved orders.
