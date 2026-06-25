# Slice 7 - Scheduling Configuration Management UI

## Goal

Add a lab-only configuration management page for V1 deadline scheduling configuration.

After this slice, lab actors should be able to:

- view current and historical daily/weekly capacity configuration rows;
- create a new date-effective daily/weekly capacity configuration row;
- edit existing capacity configuration rows for correction/tuning;
- view material scheduling configuration for all materials;
- edit per-material lead-time and capacity-unit settings;
- validate that changed configuration affects date recommendation and order save behavior end-to-end.

The UI should follow the general form/modal/card style from `Web/wwwroot/iam.html`, but it should **not** use a master-detail organization picker layout. Instead, use two independent cards:

1. Daily/weekly capacity configuration history + new config creation.
2. Material scheduling configuration table + material edit forms.

## Requirements Covered

This slice supports and operationalizes these parts of `plans/deadlines/v1-requirements.md`:

- Section 5.1 order type/material configuration persisted in the database.
- Section 5.2 PFM/PFZ `TeethPerExtraLeadDay` database configuration.
- Section 5.3 daily and weekly capacity configuration as date-effective database rows.
- Section 5.3 lookup rule: latest row where `ActiveFromDate <= candidateDeadlineDate`.
- Section 7.1 capacity formula uses configured `CapacityUnitsPerTooth`.
- Section 9.3 recommendation logs snapshot config values, so later changes should not rewrite historical decisions.
- Section 12 commit-time validation should use latest committed config values available at save time.

This slice does **not** change the scheduling algorithm itself; it provides safe lab-facing configuration management for values the algorithm already uses.

Explicitly out of scope:

- role model beyond `actor.IsLab`;
- phase/resource production scheduling;
- visual calendar overcapacity heatmaps;
- importing/exporting config;
- approval workflow for config changes;
- effective-date history for material config. Material config remains current-row-per-material for V1.

## Current State / Context

Current implementation has:

- `SchedulingMaterialConfigs` DB table with one current row per `Material`.
- `SchedulingCapacityConfigs` DB table with date-effective rows.
- Read-only providers:
  - `IMaterialSchedulingConfigProvider` / `SqliteMaterialSchedulingConfigProvider`.
  - `ISchedulingCapacityConfigProvider` / `SqliteSchedulingCapacityConfigProvider`.
- `/api/scheduling/config` currently returns material scheduling configs only.
- No UI/API for editing material or capacity configuration.
- Capacity seed values are permissive placeholders, e.g. `DailyCapacityUnits = 100`, `WeeklyCapacityUnits = 500`, and per-material `CapacityUnitsPerTooth` values are currently placeholder-like.

Motivating example:

- If all materials have `CapacityUnitsPerTooth = 1.0` and daily capacity is `100`, a clinic can schedule an order with more than 30 teeth because the configured daily limit is 100 capacity units. That is algorithmically correct, but the configuration is not tuned/visible/editable. This slice makes that configuration manageable.

## Desired End State

### Page and navigation

Add a new lab-only page, suggested route/path:

```text
/scheduling-config
```

Suggested title/product label:

```text
Scheduling Config
```

Add it to `Web/wwwroot/js/app-chrome.js` as a lab-visible product/menu item.

Server-side page access should require lab auth, similar to `/iam`.

The page should use the same general visual language as `iam.html`:

- app chrome header;
- auth shell when signed out;
- cards with `.head`, `.body`, `.toolbar`, `.msg`, `.btn`, modal overlays;
- form validation messages near forms;
- responsive single-column layout on small screens.

Unlike `iam.html`, do not use a master-detail layout. Use a simple vertical or responsive two-card layout:

```text
[ Daily/Weekly Capacity Config History ]
[ Material Scheduling Config ]
```

or side-by-side on wide screens if readable.

### Capacity config card

The capacity card should show all `SchedulingCapacityConfigs` rows, newest/effective-most-relevant first or clear chronological order. It should identify the currently active row for today's date.

Columns/fields:

```text
ActiveFromDate
DailyCapacityUnits
WeeklyCapacityUnits
CreatedAt
UpdatedAt
Status/label: Current, Future, Historical
Actions: Edit
```

Actions:

1. **New capacity config**
   - Opens a modal/form.
   - Required fields:
     - `ActiveFromDate`
     - `DailyCapacityUnits`
     - `WeeklyCapacityUnits`
   - Creates a new date-effective row.
   - If `ActiveFromDate` already exists, API should reject clearly.

2. **Edit capacity config**
   - Opens a modal/form for an existing row.
   - Allows changing daily/weekly capacity units.
   - Prefer not to allow changing `ActiveFromDate` on edit to avoid ambiguity; if the date was wrong, lab can create a new row or the API can support delete later. If implementing agent chooses to allow date edits, preserve unique-date validation.
   - Historical row editing is allowed for correction/tuning but should show a small warning that it can affect future recalculations/recommendations for dates using that row. Existing recommendation logs still preserve snapshots.

Validation:

- `ActiveFromDate` required.
- `DailyCapacityUnits > 0`.
- `WeeklyCapacityUnits > 0`.
- Use decimal parsing with invariant/server-side validation.
- Optionally warn if weekly capacity is less than daily capacity; do not necessarily block if product wants that flexibility.

### Material config card

The material card should list all `SchedulingMaterialConfigs` rows sorted by `SortOrder`, then material.

Columns/fields:

```text
Material
DisplayName
FixedLeadTimeBusinessDays
CapacityUnitsPerTooth
TeethPerExtraLeadDay
IsActive
SortOrder
UpdatedAt
Actions: Edit
```

Edit modal/form fields:

```text
DisplayName
FixedLeadTimeBusinessDays
CapacityUnitsPerTooth
TeethPerExtraLeadDay
IsActive
SortOrder
```

Rules:

- `FixedLeadTimeBusinessDays > 0`.
- `CapacityUnitsPerTooth > 0`.
- For `Pfm` and `PfzLayeredZrCrown`, `TeethPerExtraLeadDay` is required and `> 0`.
- For non-PFM/PFZ materials, `TeethPerExtraLeadDay` may be blank/null. The UI can disable or hide it for non-PFM/PFZ.
- `SortOrder` should be an integer.
- Material key itself should not be editable.

### Backend/domain API

Add write-capable configuration abstractions. Suggested shape:

```csharp
public interface IMaterialSchedulingConfigAdminRepository : IMaterialSchedulingConfigProvider
{
    Task<MaterialSchedulingConfig> UpdateAsync(Material material, MaterialSchedulingConfigUpdate update, DateTimeOffset now, CancellationToken ct = default);
}

public sealed record MaterialSchedulingConfigUpdate(
    string? DisplayName,
    int FixedLeadTimeBusinessDays,
    decimal CapacityUnitsPerTooth,
    int? TeethPerExtraLeadDay,
    bool IsActive,
    int SortOrder);

public interface ISchedulingCapacityConfigAdminRepository : ISchedulingCapacityConfigProvider
{
    Task<SchedulingCapacityConfig> CreateAsync(SchedulingCapacityConfigCreate create, DateTimeOffset now, CancellationToken ct = default);
    Task<SchedulingCapacityConfig> UpdateAsync(long id, SchedulingCapacityConfigUpdate update, DateTimeOffset now, CancellationToken ct = default);
}

public sealed record SchedulingCapacityConfigCreate(DateOnly ActiveFromDate, decimal DailyCapacityUnits, decimal WeeklyCapacityUnits);
public sealed record SchedulingCapacityConfigUpdate(decimal DailyCapacityUnits, decimal WeeklyCapacityUnits);
```

Alternatively, extend the existing provider interfaces if preferred. Keep domain validation centralized and tested.

SQLite repositories should update `CreatedAt`/`UpdatedAt` fields consistently.

### API endpoints

Add lab-only endpoints under `/api/scheduling/config` or `/api/scheduling/admin/config`.

Suggested endpoints:

```text
GET  /api/scheduling/config
```

Enhance existing response to include both:

```json
{
  "materialSchedulingConfigs": [...],
  "capacityConfigs": [...]
}
```

Write endpoints:

```text
POST /api/scheduling/config/capacity
PUT  /api/scheduling/config/capacity/{id}
PUT  /api/scheduling/config/materials/{material}
```

Access control:

- unauthenticated: `401`;
- clinic/non-lab: `403`;
- lab: allowed.

Error behavior:

- invalid decimals/integers/dates: `400` with clear message;
- duplicate capacity `ActiveFromDate`: `409` or `400` with clear message;
- missing material/config row: `404`;
- validation failures: `400`.

### Auditing

Prefer to append audit events for configuration changes using existing `IAuditLog` conventions.

Suggested audit event values:

```text
ServiceName: Scheduling
Operation: SchedulingCapacityConfigCreated
Operation: SchedulingCapacityConfigUpdated
Operation: SchedulingMaterialConfigUpdated
EntityType: SchedulingCapacityConfig / SchedulingMaterialConfig
EntityId: capacity config id or material name
```

Metadata should include old and new values where practical.

If auditing is deferred for simplicity, explicitly document that as a deviation. However, since configuration changes affect business scheduling behavior, auditing is strongly recommended.

### UI implementation

Create new files, suggested:

```text
Web/wwwroot/scheduling-config.html
Web/wwwroot/js/scheduling-config-api.js
Web/wwwroot/js/scheduling-config-page.js
```

Or keep JS inline if consistent with `iam.html`; separate JS files are preferred if manageable.

Expected page behavior:

1. On load, authenticate using existing scheduling auth cookie/me endpoint.
2. If not authenticated, show login shell similar to IAM.
3. If authenticated but not lab, show/redirect/deny clearly.
4. Mount app chrome with active product `schedulingConfig` or similar.
5. Load config via API.
6. Render capacity card and material card.
7. Editing/creating config refreshes the data and shows success/error messages.

Use modal forms similar to IAM edit/create modals.

### Configuration behavior expectations

Changing config should affect future recommendation/validation immediately after save because providers read from DB per request.

Existing saved orders:

- continue to use their saved `CalculatedCapacityUnits` when present;
- legacy/null capacity orders may fallback to current material config as already implemented;
- recommendation logs retain config snapshots from when the order was saved.

## TDD Plan

Use TDD where practical.

### Orders/domain tests

1. Material config update validation:
   - rejects non-positive `FixedLeadTimeBusinessDays`;
   - rejects non-positive `CapacityUnitsPerTooth`;
   - rejects missing/non-positive `TeethPerExtraLeadDay` for PFM/PFZ;
   - allows null `TeethPerExtraLeadDay` for non-PFM/PFZ.

2. Capacity config create/update validation:
   - rejects non-positive daily/weekly values;
   - rejects duplicate active-from date on create.

If validation lives in Database/API layer rather than Orders, place tests accordingly.

### Database tests

1. Material config repository update round-trip:
   - update a material's `CapacityUnitsPerTooth` and lead-time fields;
   - reload and assert values/`UpdatedAt` changed.

2. Capacity config create round-trip:
   - create a future row;
   - assert `GetForDateAsync` uses old row before `ActiveFromDate` and new row on/after it.

3. Capacity config update round-trip:
   - update daily/weekly values;
   - assert list/get reflect changes.

4. Duplicate `ActiveFromDate` create rejects clearly.

5. Pending model changes test still passes.

### Web/API tests

1. Access control:
   - unauthenticated write returns `401`;
   - clinic write returns `403`;
   - lab write succeeds.

2. GET config includes both material and capacity configs.

3. Lab can update material capacity units:
   - `PUT /api/scheduling/config/materials/pmma` changes `CapacityUnitsPerTooth`;
   - subsequent config GET reflects it.

4. Lab can create capacity row:
   - `POST /api/scheduling/config/capacity`;
   - subsequent config GET includes row.

5. Lab can update capacity row:
   - `PUT /api/scheduling/config/capacity/{id}`;
   - subsequent config GET reflects new daily/weekly limits.

6. Config change affects scheduling end-to-end:
   - set `DailyCapacityUnits = 30` for an active date range;
   - ensure `CapacityUnitsPerTooth = 1.0` for PMMA or chosen material;
   - attempt clinic create with 31 distinct teeth on a candidate date;
   - assert save is rejected with `DailyCapacityExceeded` or date status shows unavailable.

7. Audit events are created for config changes if auditing is implemented.

### UI/manual tests

Automated browser tests are optional if not already in use.

Manual validation should cover:

1. Lab can open `/scheduling-config` from app chrome.
2. Clinic user cannot access the page.
3. Lab sees capacity history card and material config card.
4. Lab creates a new capacity row and sees it in history.
5. Lab edits daily capacity to `30` for the active row/date.
6. Lab edits a material's `CapacityUnitsPerTooth`.
7. Scheduler behavior changes accordingly:
   - a clinic order above the configured capacity is blocked;
   - a smaller order remains schedulable if capacity is available.

## Validation Plan

### Automated validation

Run:

```bash
dotnet test Spark3Dent.sln --no-restore --verbosity quiet
```

Expected: all tests pass, including new database/API tests.

### Requirements cross-check

At the end of implementation, explicitly check this slice against `plans/deadlines/v1-requirements.md`:

- [ ] Section 5.1 material scheduling config can be viewed by lab users.
- [ ] Section 5.1 material scheduling config can be edited by lab users.
- [ ] Section 5.2 PFM/PFZ `TeethPerExtraLeadDay` can be edited and is validated.
- [ ] Section 5.3 daily/weekly capacity config history can be viewed by lab users.
- [ ] Section 5.3 new date-effective daily/weekly capacity rows can be created.
- [ ] Section 5.3 latest-row-by-`ActiveFromDate` lookup remains correct after create/update.
- [ ] Section 7.1 updated `CapacityUnitsPerTooth` affects future capacity calculations.
- [ ] Normal clinic users cannot change scheduling config.
- [ ] Recommendation logs continue to snapshot config used at order save time.
- [ ] Existing scheduling, override, logging, and concurrency tests still pass.

### Manual end-to-end validation

Perform a manual flow demonstrating the motivating issue is configurable:

1. Log in as lab.
2. Open `/scheduling-config`.
3. Set active daily capacity to `30`.
4. Ensure the test material has `CapacityUnitsPerTooth = 1.0`.
5. Log in as clinic or use clinic API.
6. Attempt to create an order with 31 distinct teeth on one deadline date.
7. Confirm it is rejected or the date is marked unavailable due to `DailyCapacityExceeded`.
8. Reduce the order to a capacity under the limit and confirm it can be scheduled when calendar/weekly rules pass.

## Review Notes for Implementing Agent

When handing this slice back for review, include:

- files changed;
- new page path and navigation/menu changes;
- API endpoint summary and request/response shapes;
- repository/interface changes;
- validation rules implemented;
- whether config-change audit events were implemented;
- tests added/updated;
- full automated test command and result;
- manual validation performed and result, especially the daily-capacity-30 scenario;
- completed requirements cross-check checklist;
- any deviations from this plan.

## Post-Implementation Notes / Intentional Deviations

The current implementation intentionally differs from the initial Slice 7 plan in a few places:

- Capacity configuration is create-only in the lab UI/API. Existing rows are displayed with current/future/historical status, but there is no supported edit endpoint for capacity rows. Corrections should currently be made by creating a new date-effective row; capacity-row edit/delete can be planned separately if needed.
- Material scheduling config evolved to date-effective history rows. The lab UI supports add/edit of scheduling values and history inspection, while display labels/order come from `Orders.MaterialOptions` rather than editable `DisplayName`, `IsActive`, and `SortOrder` columns.
- The material picker and config APIs use `MaterialOptions.All` as the canonical material catalog. Tests/guards ensure it covers the `Material` enum and preserve server ordering on the client.
- The current daily-capacity rule intentionally allows a single oversized order on an otherwise empty day. This differs from the original daily-capacity-30 validation wording below; daily capacity now guards against stacking multiple orders on the same date, while weekly capacity remains the primary hard rough-cut capacity control.

## Known Follow-ups

Potential later work:

- richer diff/history UI for material config changes if we later add material effective-date history;
- edit/delete/deactivate capacity config rows;
- config import/export;
- visual calendar capacity utilization/overcapacity indicators;
- localizing labels/messages;
- role granularity beyond `actor.IsLab`.
