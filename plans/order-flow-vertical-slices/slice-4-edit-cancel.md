# Slice 4 Plan — Edit Mode + Cancellation + Permissions

*Created: 2026-06-03*

## Goal

Add order review actions:

- edit existing order,
- cancel existing order using `DELETE`,
- enforce permissions for clinic vs technician,
- allow technician to create/edit for any clinic via clinic selection UI.

Cancelled orders remain visible in the list with simple status text.

## Dependencies

Requires Slice 2 role model and centralized auth:

- actor role available in backend,
- technician vs clinic permissions established,
- scheduler order list behavior depends on role.

Benefits from Slice 3 UI navigation/topbar, but not strictly required.

## REST/API Target

### Update order

```http
PUT /api/scheduling/orders/{code}
```

Request body shape can reuse/create a sibling of current `CreateOrderRequest`:

- caseName,
- impressionDate,
- productCategory,
- workType,
- material,
- constructionType,
- toothStart,
- toothEnd,
- shade,
- notes,
- requestedDeliveryDate,
- for technicians only: target clinic code/credential handling if needed.

Behavior:

- authenticated required,
- clinics can update own non-cancelled orders only,
- technicians can update any non-cancelled order,
- validates teeth/date rules like create,
- preserves order code and created metadata,
- updates `UpdatedAt`.

### Cancel order

```http
DELETE /api/scheduling/orders/{code}
```

Important: this is a soft delete/cancel.

Behavior:

- authenticated required,
- clinics can cancel own non-cancelled orders only,
- technicians can cancel any non-cancelled order,
- sets `Status = Cancelled`,
- updates `UpdatedAt`,
- returns updated order DTO or `{ ok: true }`; prefer updated order DTO for UI refresh.

Do not physically delete DB rows.

## Domain/Persistence Changes

### OrderStatus

Current `Orders/Enums.cs`:

```csharp
public enum OrderStatus { Created }
```

Change to:

```csharp
public enum OrderStatus
{
    Created,
    Cancelled
}
```

If display text needs “Submitted”, handle it in UI while preserving enum value unless doing a deliberate migration.

### Repository

Add/update methods in `Orders/Repositories.cs`:

```csharp
Task<OrderRecord> UpdateOrderAsync(OrderRecord order, CancellationToken ct = default);
```

Optional separate cancel method if simpler:

```csharp
Task<OrderRecord?> CancelOrderAsync(string orderCode, DateTimeOffset updatedAt, CancellationToken ct = default);
```

But prefer service owns permission/status decisions and repository persists a supplied updated record.

### Service

Add methods to `Orders/SchedulingOrderService.cs`:

```csharp
Task<OrderRecord> UpdateOrderAsync(AuthenticatedActor actor, string orderCode, OrderDraft draft, ...)
Task<OrderRecord> CancelOrderAsync(AuthenticatedActor actor, string orderCode, ...)
```

Service should:

- fetch existing order,
- authorize actor,
- reject cancelled orders for edit/cancel if already cancelled,
- validate draft for update,
- validate delivery date,
- preserve created metadata,
- update updated timestamp.

### Technician create/edit for any clinic

Current create likely uses current actor clinic metadata. In technician mode, we need selected target clinic.

Implementation options:

1. Backend accepts optional `clinicCode` on create/update when actor is technician, resolves clinic display/config, and records technician credential as acting credential or target clinic credential according to final domain decision.
2. Technician must impersonate/select a clinic credential before creating. This is less convenient and conflicts with technician account concept.

Preferred for v1: backend accepts target `clinicCode` for technician create/update. Record actor metadata separately once audit log exists. Until audit log, decide how `CredentialId/Label` should be stored:

- Option A: store target clinic code/display and technician credential id/label as creator.
- Option B: store target clinic code/display and a synthetic credential label like `Technician: X`.

Document chosen behavior in master plan because it affects audit slice.

## Frontend Scope

`Web/wwwroot/orders.html` should support:

Views:

```js
login | list | create | review | edit
```

List row actions:

- Review/open row (already added in Slice 2.5),
- Edit (optional shortcut; primary placement should be in the read-only review header),
- Cancel (optional shortcut; primary placement should be in the read-only review header).

Review actions:

- Back,
- Edit,
- Cancel.

Slice 2.5 introduced the read-only review UI in `Web/wwwroot/orders.html`. Add Edit and Cancel buttons to the review header next to the single Back to orders button, rather than only in the list row. List row actions can remain secondary/optional.

Edit mode:

- pre-fill current stepper state from order DTO,
- call `PUT /api/scheduling/orders/{code}` instead of `POST`,
- after success, show review/list with updated values.

Cancel UX:

- simple confirmation prompt/modal,
- call `DELETE /api/scheduling/orders/{code}`,
- refresh list/review,
- keep cancelled order visible with status text,
- disable edit/cancel controls on cancelled orders.

Technician clinic selector:

- visible only to technicians,
- on create: select target clinic before or above the stepper,
- on edit: show order's clinic; changing clinic during edit should be carefully considered. Preferred: do not allow changing clinic on edit in v1 unless product explicitly wants reassignment,
- add a small technician-only way to discover active target clinics (for example `GET /api/scheduling/clinics` returning code/display name) rather than hardcoding clinic choices in the browser.

## Files Expected to Change

- `Orders/Enums.cs`
- `Orders/OrderRecord.cs` if record shape changes for target/actor metadata
- `Orders/Repositories.cs`
- `Orders/SchedulingOrderService.cs`
- `Database/Entities/SchedulingOrderEntity.cs` if fields change; status enum string may not require entity change
- `Database/SqliteOrderRepo.cs`
- EF migrations if status only expands no migration needed; if new fields added, migration needed
- `Web/SchedulingApi.cs`
- `Web/wwwroot/orders.html`
- tests in `Orders.Tests`, `Database.Tests`

## Tests to Add/Update

Domain/service:

- clinic can update own created order,
- clinic cannot update another clinic order,
- technician can update any order,
- cannot edit cancelled order,
- cancellation sets status to Cancelled,
- cancelling already-cancelled order returns clear error or idempotent result; choose and document,
- date/teeth validation still applies on update,
- clinic cannot create for arbitrary clinic code,
- technician can create for selected clinic.

Repository:

- update persists changed fields and updated timestamp,
- cancelled order remains listable.

Route tests if practical:

- `PUT` permission behavior,
- `DELETE` permission behavior,
- unauthenticated returns 401.

## Manual Verification

Clinic:

1. Login as clinic.
2. Create order.
3. Edit order; confirm list/review updates.
4. Cancel order; confirm status text changes and order remains visible.
5. Confirm edit/cancel disabled or rejected for cancelled order.

Cross-clinic/security:

1. Login as another clinic.
2. Attempt direct GET/PUT/DELETE for first clinic's order code.
3. Confirm 404/403 per selected behavior; prefer 404 for not-owned order details to avoid leaking existence.

Technician:

1. Login as technician.
2. Confirm all orders visible.
3. Create order using clinic selector.
4. Edit/cancel order from any clinic.

## Implementation Checklist

- [x] Extend order status with `Cancelled`.
- [x] Add repository update/cancel persistence support.
- [x] Add service authorization helpers for own-vs-technician.
- [x] Add update service method.
- [x] Add cancel service method.
- [x] Add `PUT /api/scheduling/orders/{code}`.
- [x] Add `DELETE /api/scheduling/orders/{code}` soft-cancel.
- [x] Add technician target clinic handling for create.
- [x] Add clinic selector UI for technician create.
- [x] Add review/edit/cancel UI states and actions.
- [x] Add/update tests.
- [x] Run relevant tests/build.
- [x] Manually verify clinic/technician behavior.
- [x] Update `master-plan.md` and audit slice assumptions.

## Out of Scope

- Full audit log implementation, except possibly metadata choices needed for audit.
- Advanced order status workflow beyond `Created` and `Cancelled`.
- Physical deletion.
- Technician reassignment of an existing order to another clinic unless explicitly added.

## Completion Notes

- Status: Complete (2026-06-04)
- Files changed:
  - `Orders/Enums.cs`
  - `Orders/Repositories.cs`
  - `Orders/SchedulingOrderService.cs`
  - `Database/SqliteOrderRepo.cs`
  - `Web/SchedulingApi.cs`
  - `Web/wwwroot/orders.html`
  - `Orders.Tests/SchedulingOrderServiceTest.cs`
  - `Orders.Tests/TestSupport.cs`
  - `Database.Tests/SqliteOrderRepoTest.cs`
  - `Web.Tests/SchedulingApiTests.cs`
  - `plans/order-flow-vertical-slices/master-plan.md`
  - `plans/order-flow-vertical-slices/slice-4-edit-cancel.md`
  - `plans/order-flow-vertical-slices/slice-5-audit-log.md`
- Tests run:
  - `dotnet test Orders.Tests/Orders.Tests.csproj --no-restore` — passed, 50 tests.
  - `dotnet test Database.Tests/Database.Tests.csproj --no-restore` — passed, 73 tests.
  - `dotnet test Web.Tests/Web.Tests.csproj --no-restore` — passed, 88 tests.
  - `dotnet build Web/Web.csproj --no-restore` — passed.
  - Extracted inline script from `Web/wwwroot/orders.html`; `node --check` passed.
- Manual checks:
  - Headless Chromium smoke verified clinic login, order appears in list, review actions appear, edit/save updates review, cancel sets Cancelled status, and cancelled review disables Edit/Cancel.
- Permission decisions:
  - Clinics can update/cancel only orders whose `ClinicCode` matches their actor clinic.
  - Non-owned direct GET/PUT/DELETE returns 404 `Order not found.` to avoid existence leaks.
  - Edit/cancel of already-cancelled orders returns 400; UI disables those controls after cancellation.
  - Technicians can update/cancel any order.
  - Existing order clinic reassignment is not supported in v1 edit mode.
- Technician target-clinic behavior:
  - Added technician-only `GET /api/scheduling/clinics` returning active clinic code/display entries.
  - Technician create requires `clinicCode` in the create body and the UI shows a target clinic selector above the stepper.
  - Clinic create ignores same-clinic target but rejects a different target clinic.
  - Created technician-targeted orders store the target clinic code/display and store the technician credential id/label/fingerprint in the existing credential fields until Slice 5 adds explicit audit actor metadata.
- Discoveries affecting Slice 5:
  - Audit should distinguish target clinic from acting credential/actor. Slice 4 intentionally reuses existing credential fields for the acting technician when a technician creates for a target clinic.
  - Audit operations should include order create/update/cancel endpoint names: `POST /api/scheduling/orders`, `PUT /api/scheduling/orders/{code}`, `DELETE /api/scheduling/orders/{code}`.
