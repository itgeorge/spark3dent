# Slice 8 Plan — Make Work Items the Sole Order Tooth/Construction Source of Truth

*Created: 2026-06-05*

## Goal

Clean up the Slice 7 compatibility layer before there is production usage. Orders should rely entirely on the `WorkItems` list for tooth/construction selections.

Remove order-level single-work-item compatibility fields and all abutment-related code. Also delete the old stepper prototype because `Web/wwwroot/orders.html` is now the real scheduler UI.

## Product / Technical Decisions

1. `OrderWorkItem` is the only source of truth for construction type and selected tooth/range.
2. Order-level material and shade remain order-level for now.
3. Order-level `WorkType` is removed from order/draft/API/persistence. Work type remains as an internal rule-classification enum used by scheduling rules and derived per work item.
4. Order-level `ConstructionType`, `ToothStart`, and `ToothEnd` are removed from order/draft/API/persistence.
5. Abutments are not useful for this order flow and should be removed from live domain/API/UI/tests/persistence mapping.
6. It is acceptable to wipe scheduling/order rows during migration. Keep the DB and invoice/client data.
7. API compatibility is not required; no external users exist.
8. The old prototype `Web/wwwroot/order-prototypes/stepper.html` should be deleted.

## Desired End State

### Domain order shape

Conceptually:

```csharp
OrderDraft
  CaseName
  ImpressionDate
  ProductCategory
  Material
  WorkItems[]
  RequestedDeliveryDate
  Shade
  Notes

OrderRecord
  Id
  OrderCode
  Clinic metadata
  Credential/actor metadata
  CaseName
  ImpressionDate
  ProductCategory
  Material
  WorkItems[]
  RequestedDeliveryDate
  Status
  Shade
  Notes
  Created/Updated metadata
```

No order-level:

- `WorkType`
- `ConstructionType`
- `ToothStart`
- `ToothEnd`
- `AbutmentTeeth`
- `PrimaryWorkItem`

### `WorkType` after cleanup

Keep `WorkType` enum and `WorkRule` if they are still part of scheduling configuration. But do not expose/store `WorkType` on orders.

Server derives per-work-item rule type internally, e.g.:

```csharp
WorkType WorkTypeFor(ProductCategory productCategory, Material material, ConstructionType constructionType)
```

This is used only for rule lookup.

## Database / Migration Plan

Do not wipe the whole DB. Preserve invoice/client data.

Recommended migration behavior:

1. Delete existing scheduling orders because no production order data needs preserving:

```sql
DELETE FROM SchedulingOrders;
```

2. Optionally delete stale scheduling order audit entries to avoid audit rows pointing at deleted order codes:

```sql
DELETE FROM AuditEvents WHERE EntityType = 'SchedulingOrder';
```

3. Drop from `SchedulingOrders`:

- `WorkType`
- `ConstructionType`
- `ToothStart`
- `ToothEnd`
- `AbutmentTeeth`

4. Keep and require `WorkItemsJson` as the persisted work-item payload. If SQLite/EF migration complexity makes non-null conversion awkward, nullable DB column is acceptable as long as repository create/update always writes valid JSON and domain/API validation rejects empty work items.

5. Update `AppDbContextModelSnapshot`.

### Historical migrations note

Prefer a normal forward migration that drops the columns. Do not hand-edit old applied migrations unless the team explicitly chooses to reset/squash migration history. The goal is no abutment or legacy single-item usage in current live code/tests/API/model snapshot; old historical migration files may still describe past schema unless a separate migration-history cleanup is intentionally done.

## Domain Changes

### `OrderWorkItem`

Keep:

- construction type,
- tooth range,
- validation,
- all-teeth helper.

Remove:

- `DefaultAbutments`,
- `AbutmentsCsv`,
- any abutment terminology.

### `ToothRange`

Remove:

```csharp
DefaultAbutments(...)
```

Keep tooth validation/range behavior.

### `OrderDraft`

Remove:

- `WorkType`,
- `ConstructionType`,
- `TeethRange`,
- `PrimaryWorkItem`,
- legacy fallback normalization.

Require:

```csharp
IReadOnlyList<OrderWorkItem> WorkItems
```

Validation should reject null/empty at service/API boundaries.

### `OrderRecord`

Remove:

- `WorkType`,
- `ConstructionType`,
- `ToothStart`,
- `ToothEnd`,
- `AbutmentTeeth`,
- `PrimaryWorkItem`,
- legacy fallback normalization.

Require typed `WorkItems` in the constructor/init path.

## Service Changes

Update `Orders/SchedulingOrderService.cs`:

- all validation uses `draft.WorkItems`,
- create/update builds records with `WorkItems`,
- lead-time summing stays per work item,
- changed-field detection compares only `WorkItems`, not legacy fields,
- audit metadata includes `workItems` and total tooth count, but no `primaryWorkItem` unless a purely display-friendly summary is desired,
- remove any abutment setting.

## Order Code

`Orders/DescriptiveOrderCodeGenerator.cs` already uses total selected teeth from work items. Keep that behavior, but update it to no longer rely on legacy fallback fields.

## API Changes

Update `Web/SchedulingApi.cs`:

### Request shape

Remove from `OrderShape`:

- `WorkType WorkType`
- `ConstructionType ConstructionType`
- `int ToothStart`
- `int ToothEnd`

Require:

```csharp
IReadOnlyList<OrderWorkItemRequest> WorkItems
```

for date availability, create, and update.

### Response DTO

Remove:

- `workType`,
- `constructionType`,
- `toothStart`,
- `toothEnd`,
- `abutmentTeeth`.

Keep:

- `workItems`.

### Validation behavior

- Missing/empty `workItems` returns `400` with a clear message.
- Old single-field-only request bodies return `400` instead of being accepted.

## UI Changes

Update `Web/wwwroot/orders.html`:

- remove `workType()` helper,
- remove `backendConstruction()` if still present,
- remove `primaryWorkItem()` compatibility helper,
- `draft()` sends only `workItems` for tooth/construction selection,
- all displays already using `workItems` should keep doing so,
- ensure create/edit/date availability still work without legacy fields,
- ensure technician target clinic behavior remains unchanged.

## Prototype Cleanup

Delete:

- `Web/wwwroot/order-prototypes/stepper.html`

Then search and update/remove stale references:

```bash
rg -n "order-prototypes|stepper.html|toothStart|toothEnd|constructionType|workType|abutment" Web Orders Database *.Tests plans -g '!bin' -g '!obj'
```

Expected: `ConstructionType` and `WorkType` still appear in `OrderWorkItem`, `WorkRule`, scheduling config/rules, and tests around rule derivation. `toothStart`/`toothEnd` still appear inside work-item DTOs/JSON/UI. They should not appear as order-level fields. Abutment should disappear from live domain/API/UI/tests/persistence mapping.

## Tests to Add / Update

### Orders.Tests

- Update all `OrderDraft`/`OrderRecord` construction helpers to use required `WorkItems`.
- Remove tests for legacy fallback single fields.
- Add/keep tests for:
  - missing/empty work items rejected,
  - crown/bridge/facet per-item validation,
  - overlap rejected,
  - lead time summed per work item,
  - order code counts total selected teeth,
  - no abutment logic remains.
- Add regression test proving request/order-level stale `WorkType` is impossible or ignored because it no longer exists.

### Database.Tests

- Update schema/persistence tests:
  - `WorkItemsJson` persists and round-trips,
  - repository create/update writes valid work items,
  - list/calendar queries preserve work items,
  - old legacy fallback test is removed.
- Add a migration/schema assertion if practical:
  - current model/table no longer has legacy columns or `AbutmentTeeth`.

### Web.Tests

- Update request payloads to use `workItems` only.
- Add/keep tests:
  - create with work items succeeds,
  - create without work items returns `400`,
  - update with work items succeeds,
  - date availability with work items works,
  - list/get/calendar DTOs include `workItems` and do not include legacy fields.

## Manual / Smoke Verification

Recommended headless/browser smoke:

1. Login as clinic.
2. Create a bridge+crown multi-work-item order.
3. Confirm no legacy fields are needed in browser payloads.
4. Confirm list, review, edit, and calendar still show all work items.
5. Edit one work item and save.
6. Confirm API responses have `workItems` and not legacy order-level fields.

## Implementation Checklist

- [x] Delete `Web/wwwroot/order-prototypes/stepper.html` and stale prototype references.
- [x] Remove abutment helpers from `ToothRange` / `OrderWorkItem` and all live call sites.
- [x] Refactor `OrderDraft` to require `WorkItems` and drop legacy single-item fields.
- [x] Refactor `OrderRecord` to require `WorkItems` and drop legacy single-item fields.
- [x] Update `SchedulingOrderService` create/update/validation/audit/changed-fields logic.
- [x] Update `DescriptiveOrderCodeGenerator` if needed to use required work items directly.
- [x] Update `SchedulingOrderEntity` and `SqliteOrderRepo` mapping.
- [x] Add migration that deletes scheduling orders and drops legacy columns.
- [x] Optionally delete scheduling-order audit rows in the same migration.
- [x] Update `Web/SchedulingApi.cs` request/response shape and validation.
- [x] Update `Web/wwwroot/orders.html` payload generation and any legacy references.
- [x] Update tests across Orders/Database/Web.
- [x] Run targeted tests and full test suite if practical.
- [x] Run JS syntax checks for changed static/inline scripts.
- [x] Run browser smoke for create/review/edit/calendar.
- [x] Update master plan and this slice plan completion notes.

## Out of Scope / Follow-Ups

- Per-work-item material and shade.
- Lab organization/IAM restructuring.
- Full audit browser UI.
- Reworking historical migration files for grep-clean history unless explicitly chosen.

## Completion Notes

- Status: Complete.
- Files changed: `Orders/OrderDraft.cs`, `Orders/OrderRecord.cs`, `Orders/OrderWorkItem.cs`, `Orders/ToothRange.cs`, `Orders/SchedulingOrderService.cs`, `Orders/DescriptiveOrderCodeGenerator.cs`, `Database/Entities/SchedulingOrderEntity.cs`, `Database/SqliteOrderRepo.cs`, `Database/AppDbContext.cs`, `Database/Migrations/20260606000000_RemoveSchedulingOrderLegacyFields.cs`, `Database/Migrations/AppDbContextModelSnapshot.cs`, `Web/SchedulingApi.cs`, `Web/wwwroot/orders.html`, `Web/wwwroot/data/vita-shade-guide-reference.json`, tests in `Orders.Tests`, `Database.Tests`, and `Web.Tests`, and stale-reference plan docs. Deleted `Web/wwwroot/order-prototypes/stepper.html`.
- Tests run: `dotnet test Orders.Tests/Orders.Tests.csproj --no-restore -p:UseSharedCompilation=false` (passed, 58); `dotnet test Database.Tests/Database.Tests.csproj --no-restore -p:UseSharedCompilation=false` (passed, 78); `dotnet test Web.Tests/Web.Tests.csproj --no-restore -p:UseSharedCompilation=false` (passed, 97); `dotnet build Web/Web.csproj --no-restore -p:UseSharedCompilation=false` (passed).
- Manual checks: `node --check` passed for the extracted inline script from `Web/wwwroot/orders.html`. Headless Chromium/PuppeteerSharp smoke passed on a temp DB at `http://127.0.0.1:61259`: clinic login, multi-item bridge+crown create, create/date payloads verified `workItems` without legacy fields, list/review display of all items, edit second work item to tooth 24 with update payload verified, calendar display, and calendar-to-review.
- Migration/data deletion notes: migration `20260606000000_RemoveSchedulingOrderLegacyFields` deletes all `SchedulingOrders`, deletes `AuditEvents` where `EntityType = 'SchedulingOrder'`, recreates the scheduling orders table without `WorkType`, `ConstructionType`, `ToothStart`, `ToothEnd`, or `AbutmentTeeth`, and preserves invoice/client tables.
- API contract notes: scheduling create/update/date requests now require `workItems`; DTOs expose `workItems` and no order-level `workType`, `constructionType`, `toothStart`, `toothEnd`, or `abutmentTeeth`. Old single-field-only create requests return 400.
- Abutment cleanup notes: live domain/API/UI/repository/tests no longer include abutment helpers, mapping, DTO fields, or assertions. Historical migrations and the new migration down path still mention legacy columns as migration history only.
- Follow-up discoveries: SQLite cannot use EF `DropColumnOperation` directly in this project, so the migration uses the approved scheduling-order wipe and table recreation approach.
