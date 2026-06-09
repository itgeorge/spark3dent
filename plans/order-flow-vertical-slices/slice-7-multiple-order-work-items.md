# Slice 7 Plan — Multiple Order Work Items per Impression

*Created: 2026-06-05*

## Goal

Support orders that contain more than one restoration/work item on the same impression, for example:

- several individual crowns,
- one bridge plus one crown,
- multiple bridges,
- facet ranges mixed with crowns/bridges.

The current one-order/one-tooth-range behavior remains valid and should continue to work. Slice 7 generalizes that behavior so an order can contain one or more **order work items**, each with its own construction type and tooth/range, while material and shade remain order-level for now.

## Product Decisions

1. Use the domain term **order work item** for each crown/bridge/facet tooth selection. UI code may still refer to tooth readout lines where helpful, but domain/API naming should not use `readout`.
2. Material and shade remain order-level in this slice. Per-work-item material/shade is a future follow-up.
3. Work items serialize as JSON unless implementation complexity unexpectedly gets high. CSV with type prefixes is the fallback.
4. Keep existing single-selection fields (`constructionType`, `toothStart`, `toothEnd`, `abutmentTeeth`) as compatibility/primary-work-item fields. For multi-item orders, those fields represent the first work item.
5. API responses should include the new work-item list, and UI surfaces should prefer it when present.
6. Server validation is required and must be covered by tests.
7. Lead time is calculated as if the order were multiple separate single-work-item orders on the same impression: sum the minimum business days for each work item using that work item's construction-derived rule.
8. Order code can keep the existing material + total-teeth-count pattern because material is order-level for now.
9. Orders list/table tooth cells split work items vertically. Other surfaces can use comma+space or a central dot separator; choose the simpler consistent approach during implementation.
10. Mobile remains compact; reduce only the tooth-number readout width/padding enough to fit the added construction cycle button, preserving arrow sizes and surrounding margins.

## Current Relevant Code

Likely affected files/functions:

- UI:
  - `Web/wwwroot/orders.html`
    - `#constructionChoices`
    - `#toothReadouts`
    - `setConstruction`
    - `pickTooth`
    - `nudgeTooth`
    - `setToothRange`
    - `syncToothPickerHighlight`
    - `validateStep`
    - `draft`
    - `fillFormFromOrder`
    - `renderOrders`
    - `renderReview`
    - calendar chip/popup helpers such as `orderTeethLabel`, `orderToothCount`, `orderChipLabel`
- Domain/service:
  - `Orders/OrderDraft.cs`
  - `Orders/OrderRecord.cs`
  - `Orders/ToothRange.cs`
  - `Orders/SchedulingOrderService.cs`
  - `Orders/DescriptiveOrderCodeGenerator.cs`
  - `Orders/WorkRule.cs` / `Orders/SchedulingOptions.cs` rule lookup usage
- API:
  - `Web/SchedulingApi.cs`
- Persistence:
  - `Database/Entities/SchedulingOrderEntity.cs`
  - `Database/SqliteOrderRepo.cs`
  - `Database/AppDbContext.cs`
  - `Database/Migrations/*`
- Tests:
  - `Orders.Tests/*`
  - `Database.Tests/SqliteOrderRepoTest.cs`
  - `Web.Tests/SchedulingApiTests.cs`

## Domain Model Target

Add a domain record similar to:

```csharp
public sealed record OrderWorkItem(
    ConstructionType ConstructionType,
    ToothRange TeethRange);
```

Recommended convenience members:

- `int ToothStart => TeethRange.Start`
- `int ToothEnd => TeethRange.End`
- `int[] Teeth => TeethRange.Teeth`
- `int[] DefaultAbutments => TeethRange.DefaultAbutments(ConstructionType)`
- display/range helper if useful

Update `OrderDraft` to include a collection:

```csharp
IReadOnlyList<OrderWorkItem> WorkItems
```

Backwards compatibility:

- If callers only supply existing `ConstructionType` + `TeethRange`, construct `WorkItems` with exactly one item.
- For new callers, prefer `WorkItems` and derive primary `ConstructionType` / `TeethRange` from the first item where needed.

Update `OrderRecord` to include persisted work items, likely:

```csharp
IReadOnlyList<OrderWorkItem> WorkItems
```

or a serialized string plus parsed helper. Prefer a real typed property in domain records and serialize only in database mapping.

## Serialization / Persistence

Recommended DB addition:

```csharp
public string WorkItemsJson { get; set; } = string.Empty;
```

JSON example:

```json
[
  { "constructionType": "bridge", "toothStart": 11, "toothEnd": 14 },
  { "constructionType": "crown", "toothStart": 23, "toothEnd": 23 },
  { "constructionType": "facet", "toothStart": 41, "toothEnd": 44 }
]
```

Implementation notes:

- Add a simple EF migration for the nullable/string column if practical.
- If `WorkItemsJson` is null/empty for older rows, map from existing `ConstructionType`, `ToothStart`, `ToothEnd`.
- When saving, always populate `WorkItemsJson`.
- Continue populating primary single-item columns from the first work item for compatibility.
- `AbutmentTeeth` should include default abutments for all bridge work items, preferably as a simple comma-separated unique list unless a better small format is useful.

Fallback if JSON becomes too complex:

```text
bridge:11-14,crown:23,facet:41-44
```

If fallback is used, document that choice in the master plan and tests.

## Validation Rules

Server-side validation is mandatory.

Per work item:

- construction type is required/valid,
- tooth numbers are valid FDI teeth,
- crown must be exactly one tooth,
- bridge/facet must be a contiguous same-jaw range,
- bridge/facet must span at least two teeth.

Across the order:

- at least one work item is required,
- selected teeth must not overlap across work items,
- duplicate exact work items are rejected by the no-overlap rule,
- selected teeth should normalize consistently via `ToothRange` before comparison.

Suggested domain helper:

```csharp
public sealed record OrderWorkItems(IReadOnlyList<OrderWorkItem> Items)
{
    public void Validate();
    public int[] AllTeeth { get; }
    public OrderWorkItem Primary { get; }
}
```

A wrapper is optional, but it can keep validation/serialization centralized.

## Lead Time / Date Availability

Current flow calculates one work rule using order-level product/work/material/construction.

Slice 7 target:

- Calculate each work item as if it were its own order.
- Sum all selected work items' rule days.
- Feed the summed business-day count into `DateAvailabilityService.CalculateMinimumDateAsync`.

Because `WorkType` currently depends on construction in the browser (`bridge` => `bridge`, crown/facet => `crown`, PMMA => `temporaryCrownBridge`), introduce or centralize a server-side derivation for each item:

```csharp
WorkType WorkTypeFor(ProductCategory productCategory, Material material, ConstructionType itemConstructionType)
```

Suggested behavior matching current UI:

- `Material.Pmma` => `WorkType.TemporaryCrownBridge`
- `ConstructionType.Bridge` => `WorkType.Bridge`
- `ConstructionType.Crown` or `Facet` => `WorkType.Crown`

Then for each item:

```csharp
var rule = config.FindWorkRule(productCategory, derivedWorkType, material, item.ConstructionType);
requiredBusinessDays += rule.MinBusinessDays;
```

Keep request/response `workType` as primary/backcompat for now, but do not rely on it for multi-item lead-time calculations.

## Order Code

Update `DescriptiveOrderCodeGenerator` so tooth count uses total unique selected teeth across all work items:

```csharp
var toothCount = draft.WorkItems.SelectMany(i => i.TeethRange.Teeth).Distinct().Count();
```

Since validation prevents overlap, distinct count is mostly defensive.

Material code remains unchanged.

## API Target

Extend create/update/date request bodies with optional `workItems`:

```json
{
  "caseName": "Mixed bridge/crown",
  "material": "fullContourZirconia",
  "shade": "A3",
  "workItems": [
    { "constructionType": "bridge", "toothStart": 11, "toothEnd": 14 },
    { "constructionType": "crown", "toothStart": 23, "toothEnd": 23 }
  ],
  "constructionType": "bridge",
  "toothStart": 11,
  "toothEnd": 14
}
```

Rules:

- If `workItems` is present and non-empty, use it.
- If absent, use existing single fields to build a one-item list.
- Responses include `workItems`.
- Existing fields remain in response as the first/primary work item.

Update DTOs in `Web/SchedulingApi.cs`:

- request shape,
- `ToDraft`,
- `ToDto`,
- calendar DTOs if they project fields separately.

## UI Target

### Tooth work-item editor

Replace the separate construction choice row with a compact per-line construction cycle button in the tooth readout area.

Current structure:

- separate `#constructionChoices` buttons above quick tooth picker,
- one `#toothReadouts` line with jaw/start/end fields.

Target structure:

- quick tooth picker remains above,
- `#toothReadouts` contains one or more work-item lines,
- each line has:
  - compact construction cycle button (`Crown` -> `Bridge` -> `Facet` -> `Crown`),
  - jaw toggle/readout,
  - start tooth stepper,
  - end tooth stepper shown for bridge/facet,
  - locked/disabled state for previous lines.
- a separate control row has:
  - `+` add work item,
  - `-` remove last work item, enabled only when more than one item exists.

### Active item behavior

- Only one work item is active/editable at a time.
- New selections in the tooth picker apply to the active item only.
- Adding a work item:
  - requires or should strongly prefer that the current active item is valid first,
  - locks previous items,
  - appends a new active item,
  - clears the new item's tooth selection or initializes to the first available valid default.
- Removing:
  - removes the current/last item,
  - reactivates the previous item,
  - disabled if only one item remains.
- Construction cycle on the active item updates validation/range behavior like the old construction buttons.

### Tooth picker behavior

- Active item selection works the same as today.
- Teeth already selected by locked/previous work items are unavailable for the active item.
- The UI should prevent or ignore clicks/nudges that would overlap previous work items.
- Highlighting should make active selected teeth clear. Optional: show locked item teeth with a muted/secondary highlight. If this complicates implementation, it is acceptable for v1 to only prevent overlap and show active highlight.

### Mobile sizing

- Reduce only `.tooth-stepper .tooth-readout` width/min/max and number padding enough to fit the new construction cycle button.
- Keep arrow sizes and surrounding spacing/margins the same.
- Preserve the month grid behavior from Slice 6.

## UI Display Updates

Prefer new `workItems` display everywhere. Fall back to old `toothStart`/`toothEnd` fields if absent.

Affected surfaces:

- orders list/table:
  - split work item ranges vertically inside the teeth cell,
  - mobile shade line can remain under/next to the tooth display as currently implemented.
- review modal:
  - show all work items with separators,
  - render teeth preview for the union of all selected teeth.
- create/edit overview:
  - summarize all work items,
  - preview union of selected teeth.
- confirmation:
  - show all work items.
- calendar chips/day popup:
  - show concise multi-item label, e.g. `Zirconia · 11-14, 23` or total tooth count if too long.
- audit metadata:
  - include `workItems` on create/update events and mark changed fields when work items change.

## Teeth Preview

Update helper functions so they can render multiple selected teeth:

- existing single-range helpers like `selectedTeethRange()` / `orderTeethRange(o)` should get multi-item equivalents,
- preview crop bounds should use the union of all selected teeth,
- if work items span both jaws, crop may include a larger area; this is acceptable for prototype quality.

## Edit Mode

When editing an existing order:

- populate all work items from `o.workItems`,
- if absent, build one work item from legacy fields,
- allow editing only the active/last item according to the same UI rules,
- save with `PUT /api/scheduling/orders/{code}` using `workItems`,
- do not allow clinic reassignment, unchanged from Slice 4.

## Audit

Update scheduler audit metadata:

- `OrderCreated`: include `workItems`, total tooth count, primary work item.
- `OrderUpdated`: include old/new `workItems` or at least include `WorkItems` in `changedFields` when changed.
- `OrderCancelled`: no special change required, but metadata may include current `workItems` if easy.

Do not log raw request bodies wholesale.

## Tests to Add/Update

### Orders.Tests

Add tests for:

- single legacy draft still validates as one work item,
- multiple valid work items validate,
- crown range rejected per item,
- bridge/facet single tooth rejected per item,
- cross-jaw range rejected per item,
- overlapping teeth across items rejected,
- lead time sums per work item using each item's derived work rule,
- order code tooth count uses total selected teeth.

### Database.Tests

Add/update tests for:

- `WorkItemsJson` persists and round-trips,
- old rows with empty/null `WorkItemsJson` map to one work item from legacy fields,
- primary single fields are populated from the first work item on save,
- list/calendar queries return DTO/domain records with work items intact.

### Web.Tests

Add/update tests for:

- create with `workItems` persists multiple items,
- create without `workItems` still works,
- update with `workItems` works,
- invalid overlap returns 400,
- date availability uses summed work-item lead time,
- DTO includes `workItems` in list/get/calendar responses.

### UI / Smoke

Recommended headless smoke:

1. Login as clinic.
2. Start new order.
3. Select first work item as bridge/range.
4. Add second work item.
5. Confirm previous teeth cannot be reused.
6. Set second item as crown.
7. Complete order.
8. Confirm list shows vertical split teeth cell.
9. Open review; confirm both items and preview are shown.
10. Edit order; confirm both items load and can be modified.
11. Confirm calendar chip/popup handles multi-item order.

## Implementation Checklist

- [x] Add `OrderWorkItem` domain type and validation helpers.
- [x] Extend `OrderDraft` with work items and legacy single-item compatibility.
- [x] Extend `OrderRecord` with typed work items.
- [x] Add `WorkItemsJson` persistence and migration/backfill mapping.
- [x] Update repository mapping to persist/read work items and primary legacy fields.
- [x] Update service validation for per-item and cross-item overlap.
- [x] Update lead-time calculation to sum per-work-item rules.
- [x] Update order code tooth count to total selected teeth.
- [x] Update API request parsing and DTO output for `workItems`.
- [x] Update audit metadata/changed fields for work items.
- [x] Replace separate construction buttons with per-work-item construction cycle button in `orders.html`.
- [x] Add `+` / `-` work-item controls and active/locked item behavior.
- [x] Prevent active selection overlap with previous work items in chart clicks and tooth nudges.
- [x] Reduce mobile tooth-number readout width/padding only.
- [x] Update create/edit/review/list/calendar/confirmation displays.
- [x] Add/update domain, database, and web tests.
- [x] Run affected tests and full test suite if practical.
- [x] Run JS syntax checks for changed static scripts/inline script extraction.
- [x] Perform browser smoke for multi-item create/review/edit/calendar.
- [x] Update master plan and this slice plan with completion notes.

## Out of Scope / Follow-Ups

- Per-work-item material and shade.
- Per-work-item detailed lab instructions.
- Reassigning an existing order to another clinic.
- A richer visual distinction for locked/previous work items if basic non-overlap and active highlighting are sufficient for v1.
- Changing order status workflow beyond Created/Cancelled.

## Completion Notes

- Status: Complete.
- Files changed: `Orders/OrderWorkItem.cs`, `Orders/OrderDraft.cs`, `Orders/OrderRecord.cs`, `Orders/SchedulingOrderService.cs`, `Orders/DescriptiveOrderCodeGenerator.cs`, order tests; `Database/Entities/SchedulingOrderEntity.cs`, `Database/SqliteOrderRepo.cs`, migration `20260605005926_AddSchedulingOrderWorkItems`, database tests; `Web/SchedulingApi.cs`, `Web/wwwroot/orders.html`, web API tests.
- Tests run: `dotnet test Orders.Tests/Orders.Tests.csproj --no-restore`; `dotnet test Database.Tests/Database.Tests.csproj --no-restore`; `dotnet test Web.Tests/Web.Tests.csproj --no-restore`; `dotnet build Web/Web.csproj --no-restore`; `node --check` on extracted `orders.html` inline script.
- Manual checks: Headless Chromium/CDP smoke passed on a temp DB at `http://127.0.0.1:61247`: clinic login, multi-item bridge+crown create, locked-tooth reuse prevention, overview/confirmation/list/review union displays, edit latest work item from tooth 23 to 24, calendar chip/day popup, and review opened from calendar popup.
- Serialization choice: JSON via `SchedulingOrders.WorkItemsJson` using camelCase string construction types. Empty/null JSON falls back to legacy first-item fields. Primary compatibility columns are populated from the first order work item on save.
- Lead-time calculation notes: Server derives work type per order work item (`pmma`/temporary => temporary crown/bridge, bridge => bridge, crown/facet => crown) and sums each matched rule's minimum business days before date availability.
- UI decisions: `orders.html` now uses per-order-work-item construction cycle buttons in the tooth readout, `+`/`-` controls, locked previous lines, active latest-item editing, muted locked tooth highlights, and overlap prevention for active tooth clicks/nudges. Mobile compacting only narrows tooth-number readouts.
- Follow-up discoveries: Existing FDI `ToothRange` normalization remains sequence-based, so upper anterior ranges such as `11-13` persist/display as normalized `13-11` in compatibility fields.
