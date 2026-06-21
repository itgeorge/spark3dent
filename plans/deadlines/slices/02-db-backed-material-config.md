# Slice 2 - DB-Backed Material Scheduling Config

## Goal

Replace the Slice 1 hardcoded material lead-time configuration with persisted database configuration while preserving the same end-to-end deadline recommendation behavior.

After this slice, deadline lead-time calculations should read material scheduling config from the database for:

- fixed lead-time business days;
- PFM/PFZ teeth-per-extra-lead-day value;
- capacity units per tooth, persisted now for use by later capacity slices.

Daily/weekly capacity checks, saved order capacity units, recommendation logs, and full manual override logging remain out of scope.

## Requirements Covered

This slice implements the DB-backed material config parts of `plans/deadlines/v1-requirements.md`:

- Section 5.1, adapted to this codebase: config is keyed by current `Material`, not by a new work-type concept.
- Section 5.1 `FixedLeadTimeBusinessDays` persisted in DB.
- Section 5.1 `CapacityUnitsPerTooth` persisted in DB as `.NET decimal` application data, but not used for capacity checks yet.
- Section 5.2 `TeethPerExtraLeadDay` persisted in DB for PFM/PFZ.
- Section 9.3 config snapshotting remains out of scope until recommendation logs are added.

Behavior from Slice 1 must remain intact:

- Section 4.1 intake cutoff.
- Section 4.2 inclusive business-day lead-time counting.
- Section 4.3 weekends/holidays.
- Section 4.4 first-business-day-after-weekend/holiday blocked only as deadline.
- Acceptance scenarios 14.1-14.8 should still pass.

Explicitly out of scope for this slice:

- daily/weekly capacity configuration and capacity checks;
- storing calculated capacity units on orders;
- capacity usage queries;
- recommendation logs/candidate audit trails;
- override logs/reason capture;
- admin UI for editing material config.

## Current State / Context

Slice 1 has introduced:

- `Orders/DeadlineRecommendationService.cs`
  - Uses `MaterialLeadTimeConfigProvider` for hardcoded lead-time values.
  - Calculates effective intake, lead-time days, and earliest selectable deadline.
- `Orders/MaterialLeadTimeConfigProvider.cs`
  - Hardcoded values:
    - `Pmma` / `PmmaTelio`: 2 fixed business days;
    - `FullContourZirconia`: 3 fixed business days;
    - `GlassCeramics`: 4 fixed business days;
    - `Pfm` / `PfzLayeredZrCrown`: 4 fixed business days + `ceil(teeth / 10)`.
- `Web/SchedulingApi.cs`
  - `/api/scheduling/config` currently returns hardcoded material lead-time config.
  - `/api/scheduling/dates` supports optional `orderCode` for edit-mode preview timestamp alignment.
- `Web/WebProgram.cs`
  - Registers `MaterialLeadTimeConfigProvider` and `DeadlineRecommendationService` as singletons.
- `Database/AppDbContext.cs`
  - No material scheduling config table yet.

The old JSON `SchedulingOptions` / `ISchedulingConfigProvider` still exists for auth/session settings. Do not reintroduce it into deadline scheduling.

## Desired End State

### Database table

Add a scheduling material config table. Suggested entity/table name:

- Entity: `SchedulingMaterialConfigEntity`
- Table: `SchedulingMaterialConfigs`

Suggested columns:

```text
Material TEXT PRIMARY KEY
DisplayName TEXT NULL
FixedLeadTimeBusinessDays INTEGER NOT NULL
CapacityUnitsPerTooth decimal NOT NULL
TeethPerExtraLeadDay INTEGER NULL
IsActive INTEGER/BOOLEAN NOT NULL DEFAULT 1
SortOrder INTEGER NOT NULL DEFAULT 0
CreatedAt TEXT NOT NULL
UpdatedAt TEXT NOT NULL
```

Notes:

- `Material` should store the current `Orders.Material` enum name as a string, consistent with existing scheduling order material storage.
- `CapacityUnitsPerTooth` should be exposed as `decimal` in application/domain code.
- PFM/PFZ extra lead-time applicability should remain hardcoded by material (`Pfm`, `PfzLayeredZrCrown`); the database stores the configurable `TeethPerExtraLeadDay` value.
- Do not add a new work-type concept.

### Seed data

The migration should insert initial rows matching Slice 1 behavior:

| Material | DisplayName | FixedLeadTimeBusinessDays | TeethPerExtraLeadDay | CapacityUnitsPerTooth | IsActive | SortOrder |
| --- | --- | ---: | ---: | ---: | --- | ---: |
| `Pmma` | PMMA | 2 | null | 1.0 | true | 10 |
| `PmmaTelio` | PMMA Telio | 2 | null | 1.0 | true | 20 |
| `FullContourZirconia` | Full Contour Zirconia | 3 | null | 1.0 | true | 30 |
| `GlassCeramics` | Glass Ceramics / LiSi | 4 | null | 1.0 | true | 40 |
| `Pfm` | PFM | 4 | 10 | 1.0 | true | 50 |
| `PfzLayeredZrCrown` | PFZ Layered Zr Crown | 4 | 10 | 1.0 | true | 60 |

`CapacityUnitsPerTooth = 1.0` is an initial placeholder because capacity checks are deferred. Slice 3 can tune or use these values when capacity behavior is introduced.

### Domain/provider API

Replace the hardcoded provider with a DB-backed provider behind an interface in `Orders`, for example:

```csharp
public interface IMaterialSchedulingConfigProvider
{
    Task<MaterialSchedulingConfig> GetAsync(Material material, CancellationToken ct = default);
    Task<IReadOnlyList<MaterialSchedulingConfig>> ListAsync(CancellationToken ct = default);
}
```

Suggested domain record:

```csharp
public sealed record MaterialSchedulingConfig(
    Material Material,
    string? DisplayName,
    int FixedLeadTimeBusinessDays,
    decimal CapacityUnitsPerTooth,
    int? TeethPerExtraLeadDay,
    bool IsActive,
    int SortOrder);
```

Implementation guidance:

- Put the interface/domain record in `Orders`.
- Put the SQLite implementation in `Database`, following existing repository patterns such as `SqliteOrderRepo`.
- Prefer reading from DB on each call rather than caching, so simple DB edits take effect without app restart. If caching is introduced, document invalidation behavior.
- `DeadlineRecommendationService` should depend on the interface, not directly on Database types.
- `DeadlineRecommendationService` methods may need to become async where they currently calculate lead time synchronously.

### Validation rules

The provider or recommendation service should fail clearly if configuration is invalid:

- missing material config row;
- inactive material config row used for scheduling;
- `FixedLeadTimeBusinessDays <= 0`;
- `CapacityUnitsPerTooth <= 0`;
- `Pfm`/`PfzLayeredZrCrown` missing `TeethPerExtraLeadDay` or having `TeethPerExtraLeadDay <= 0`.

For non-PFM/PFZ materials, `TeethPerExtraLeadDay` should not affect lead-time calculation.

### API behavior

Update `/api/scheduling/config` to return DB-backed material scheduling config, including at least:

```text
material
fixedLeadTimeBusinessDays
capacityUnitsPerTooth
teethPerExtraLeadDay
isActive
sortOrder
displayName
```

Update `/api/scheduling/dates`, create, and update flows indirectly by making `DeadlineRecommendationService` read DB-backed config.

The JSON response shape for `/api/scheduling/dates` should remain compatible with the current UI.

## Implementation Plan

### 1. Add domain interface and records

- Rename or replace `MaterialLeadTimeConfigProvider` concepts with material scheduling config names.
- Keep names explicit that config is material-based and scheduling-related.
- Preserve the hardcoded PFM/PFZ formula in `DeadlineRecommendationService`; only the values come from DB.

### 2. Add database entity, DbSet, model config, and migration

- Add `Database/Entities/SchedulingMaterialConfigEntity.cs`.
- Add `DbSet<SchedulingMaterialConfigEntity>` to `AppDbContext`.
- Configure key, required fields, conversions/column types as needed.
- Add an EF migration, e.g. `AddSchedulingMaterialConfigs`.
- Ensure `Database/Migrations/AppDbContextModelSnapshot.cs` is updated.
- Seed the six initial rows in the migration.

Run the migration generator rather than hand-writing snapshot changes if practical:

```bash
dotnet ef migrations add AddSchedulingMaterialConfigs --project Database --startup-project Cli
```

If the EF command is unavailable in the environment, document how the migration/snapshot was produced and verify `PendingModelChangesTest` passes.

### 3. Add DB-backed provider/repository

- Implement `IMaterialSchedulingConfigProvider` in `Database`, e.g. `SqliteMaterialSchedulingConfigProvider`.
- Use `Func<AppDbContext>` like other SQLite repositories.
- Map entity strings to `Orders.Material` with clear errors for unknown values.
- Return configs ordered by `SortOrder`, then `Material` for list calls.

### 4. Wire DI

In `Web/WebProgram.cs`:

- remove registration of the hardcoded provider;
- register the DB-backed provider;
- adjust `DeadlineRecommendationService` lifetime if needed.

A scoped provider/service is fine. A singleton service must not depend on scoped services.

Test fixtures in `Orders.Tests` can use an in-memory implementation of `IMaterialSchedulingConfigProvider`.

### 5. Update service and API usage

- Update `DeadlineRecommendationService` to load config asynchronously from `IMaterialSchedulingConfigProvider`.
- Update tests and callers affected by signature changes.
- Update `/api/scheduling/config` to read from DB-backed provider asynchronously.
- Keep `/api/scheduling/dates` response compatible.

### 6. Remove or quarantine hardcoded provider

- Delete `MaterialLeadTimeConfigProvider` if no longer needed.
- Or keep only test/in-memory helpers under test projects.
- Avoid production deadline scheduling falling back to hardcoded values. Missing DB config should be visible as an error, not silently hidden.

## TDD Plan

Use TDD where practical. Suggested tests to add/update before implementation:

### Orders unit tests

1. `DeadlineRecommendationService` uses provider values, not hardcoded values:
   - configure `Pmma` fixed lead-time as 3 in a fake provider;
   - assert PMMA recommendation changes accordingly.

2. PFM/PFZ use configured `TeethPerExtraLeadDay`:
   - configure `Pfm` fixed = 4, teeth-per-extra = 5;
   - 6 teeth should produce `4 + ceil(6/5) = 6` lead-time days.

3. Missing config fails clearly:
   - fake provider missing material row;
   - recommendation throws a useful `InvalidOperationException`.

4. Invalid PFM/PFZ config fails clearly:
   - `Pfm` with null/zero `TeethPerExtraLeadDay`;
   - recommendation throws a useful `InvalidOperationException`.

5. Non-PFM material ignores `TeethPerExtraLeadDay`:
   - configure `Pmma` with a stray `TeethPerExtraLeadDay`;
   - recommendation uses fixed lead only.

### Database tests

1. Migration creates and seeds `SchedulingMaterialConfigs`:
   - migrate a temp SQLite DB;
   - assert all six material rows exist;
   - assert Slice 1 seed values are present.

2. DB provider returns material config:
   - read `Pfm` and assert fixed = 4, teeth-per-extra = 10, capacity units = 1.0m.

3. DB provider reflects DB edits:
   - update a row directly through EF in test;
   - provider returns updated fixed lead-time without using hardcoded values.

4. `PendingModelChangesTest` remains green.

### Web/API tests

1. `/api/scheduling/config` returns DB-backed material config and includes `capacityUnitsPerTooth` and `teethPerExtraLeadDay`.

2. `/api/scheduling/dates` reflects DB-edited lead time:
   - update PMMA fixed lead-time in the test DB;
   - call the dates endpoint;
   - assert `minimumDate` changes accordingly.

3. Create/update validation reflects DB-edited lead time:
   - if feasible, edit FullContourZirconia or PMMA fixed lead-time in the test DB;
   - attempt to save a date valid under old config but invalid under edited config;
   - assert non-lab save is rejected.

## Validation Plan

### Automated validation

Run:

```bash
dotnet test Spark3Dent.sln --no-restore --verbosity quiet
```

If migration or generated files require restore/build first, run the appropriate `dotnet test` command without `--no-restore` and document it.

Expected: all tests pass, including `Database.Tests.PendingModelChangesTest`.

### Requirements cross-check

At the end of implementation, explicitly check this slice against `plans/deadlines/v1-requirements.md`:

- [ ] Section 5.1 material scheduling config is persisted in DB, adapted to `Material` rather than a new work-type concept.
- [ ] Section 5.1 `FixedLeadTimeBusinessDays` is read from DB by deadline recommendation.
- [ ] Section 5.1 `CapacityUnitsPerTooth` exists in DB and application config model as `decimal`.
- [ ] Section 5.2 `TeethPerExtraLeadDay` is read from DB for PFM/PFZ.
- [ ] Section 5.2 formula remains hardcoded while the teeth-per-extra value is configurable.
- [ ] Slice 1 lead-time/calendar behavior still works after DB-backed config replacement.
- [ ] Daily/weekly capacity behavior remains intentionally unimplemented until Slice 3.
- [ ] Recommendation/override logging remains intentionally unimplemented until later slices.

### Manual end-to-end validation

Perform at least one manual/API-level check that proves the app uses DB config:

1. Start with a migrated local database.
2. Confirm `SchedulingMaterialConfigs` contains the six seed rows.
3. Log in to the scheduler as a clinic or lab user.
4. Open/create a PMMA order and observe the earliest deadline using the calendar.
5. Change `SchedulingMaterialConfigs.FixedLeadTimeBusinessDays` for `Pmma` from `2` to `3` directly in the local DB.
6. Refresh/reopen the order flow or call `/api/scheduling/dates` again.
7. Confirm the PMMA earliest deadline shifts later according to the new DB value.
8. Change `Pmma` back to `2` after validation if using a persistent local dev DB.

If direct browser auth is inconvenient, perform the equivalent with API test helpers or a small local API request and document the exact request/response.

## Review Notes for Implementing Agent

When handing this slice back for review, include:

- files changed;
- migration name and summary;
- new table/entity/provider names;
- whether hardcoded provider was deleted or retained only for tests;
- tests added/updated;
- full automated test command and result;
- manual validation performed and result;
- completed requirements cross-check checklist;
- any deviations from this plan.

## Known Follow-ups

Deferred to later slices:

- use `CapacityUnitsPerTooth` to calculate and save order capacity units;
- add daily/weekly capacity config rows;
- add daily/weekly capacity usage queries and recommendation checks;
- add transaction-backed commit-time capacity revalidation;
- add recommendation logs and config snapshots;
- add full lab override reason/rules-bypassed logging;
- add admin UI/API for editing material scheduling config, if needed.
