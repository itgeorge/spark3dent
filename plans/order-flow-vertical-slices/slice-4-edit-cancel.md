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

- Review/open row,
- Edit,
- Cancel.

Review actions:

- Back,
- Edit,
- Cancel.

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
- on edit: show order's clinic; changing clinic during edit should be carefully considered. Preferred: do not allow changing clinic on edit in v1 unless product explicitly wants reassignment.

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

- [ ] Extend order status with `Cancelled`.
- [ ] Add repository update/cancel persistence support.
- [ ] Add service authorization helpers for own-vs-technician.
- [ ] Add update service method.
- [ ] Add cancel service method.
- [ ] Add `PUT /api/scheduling/orders/{code}`.
- [ ] Add `DELETE /api/scheduling/orders/{code}` soft-cancel.
- [ ] Add technician target clinic handling for create.
- [ ] Add clinic selector UI for technician create.
- [ ] Add review/edit/cancel UI states and actions.
- [ ] Add/update tests.
- [ ] Run relevant tests/build.
- [ ] Manually verify clinic/technician behavior.
- [ ] Update `master-plan.md` and audit slice assumptions.

## Out of Scope

- Full audit log implementation, except possibly metadata choices needed for audit.
- Advanced order status workflow beyond `Created` and `Cancelled`.
- Physical deletion.
- Technician reassignment of an existing order to another clinic unless explicitly added.

## Completion Notes

Fill in after implementation.

- Status:
- Files changed:
- Tests run:
- Manual checks:
- Permission decisions:
- Technician target-clinic behavior:
- Discoveries affecting Slice 5:
