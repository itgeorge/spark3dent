# Slice 5 Plan — Audit Log for Scheduling/Invoicing/Client Operations

*Created: 2026-06-03*

## Goal

Introduce an append-only audit log so we can answer:

- who did what,
- to which entity and id,
- from which role/account,
- when,
- with enough metadata to investigate changes.

Audit logging should cover scheduler operations and key invoicing/client operations.

## Dependencies

This slice should happen after Slices 2-4 because:

- actor role and technician/clinic permissions must be finalized,
- order create/update/cancel operation shapes must be finalized,
- `/api/invoicing` route grouping must be finalized.

Before implementation, review completion notes from:

- `slice-2-auth-roles-invoicing-gate.md`, especially actor/role model,
- `slice-4-edit-cancel.md`, especially technician target-clinic metadata behavior.

## Audit Event Shape

Recommended domain record:

```csharp
public sealed record AuditEvent(
    long Id,
    string ServiceName,
    string Operation,
    string EntityType,
    string EntityId,
    string? EntityDisplay,
    string ActorRole,
    string? ActorClinicCode,
    string? ActorCredentialId,
    string? ActorCredentialLabel,
    string? ActorSessionId,
    DateTimeOffset OccurredAt,
    string? Ip,
    string? UserAgent,
    string? MetadataJson);
```

Suggested values:

- `ServiceName`: `Scheduling`, `Invoicing`, `Clients`
- `EntityType`: `SchedulingOrder`, `Invoice`, `Client`
- `Operation`:
  - `OrderCreated`
  - `OrderUpdated`
  - `OrderCancelled`
  - `ClientCreated`
  - `ClientUpdated`
  - `InvoiceIssued`
  - `InvoiceCorrected`
  - `InvoiceImportAnalyzed` optional
  - `InvoiceImportCommitted`

Keep metadata JSON small and non-sensitive. Do not store PINs, raw auth tokens, or full confidential payloads unless explicitly required.

## Persistence

Add DB entity, e.g.:

- `Database/Entities/AuditEventEntity.cs`
- `DbSet<AuditEventEntity> AuditEvents` in `Database/AppDbContext.cs`
- migration adding `AuditEvents` table.

Indexes:

- `OccurredAt` descending or unix timestamp equivalent,
- `(EntityType, EntityId)`,
- `(ActorClinicCode, OccurredAt)` if helpful,
- `(ServiceName, Operation, OccurredAt)` optional.

## Repository/Service Placement

Add a small audit abstraction in a shared/domain-appropriate project. Options:

1. New `Audit` project if you want clean boundaries.
2. Put audit contracts in `Utilities` or a new app-level service in `Web`.
3. Put repository in `Database` and interface in domain projects.

Recommended: create a small `Audit` project if future expansion is expected; otherwise use `Utilities` for `IAuditLog` to minimize project churn. Document chosen boundary in the master plan.

Suggested interface:

```csharp
public interface IAuditLog
{
    Task AppendAsync(AuditEvent auditEvent, CancellationToken ct = default);
}
```

## Where to Log

Prefer service-level logging, not endpoint-level logging, for domain operations:

- `Orders/SchedulingOrderService.cs` logs create/update/cancel.
- Invoicing operations are currently coordinated in `Web/Api.cs` using `IInvoiceOperations`, `IClientRepo`, importer services. If there is no single invoicing service layer, either:
  - add audit calls in route handlers temporarily, or
  - introduce thin app service wrappers for client/invoice operations.

Given this is v1, route-handler audit calls for invoicing/client may be acceptable, but scheduler audit should be in `SchedulingOrderService`.

## Actor Context

Audit logging needs actor details. By Slice 2, scheduling auth should resolve an `AuthenticatedActor`.

For `/api/invoicing/*`, technician-only filter should make actor available to handlers, e.g.:

```csharp
ctx.Items["Actor"] = actor;
```

or through an injected request-scoped actor accessor:

```csharp
public interface ICurrentActorAccessor
{
    AuthenticatedActor? Actor { get; }
}
```

Preferred if implementing central filters: actor accessor avoids repeated `HttpContext.Items` casts.

## Scheduler Audit Requirements

Log after successful persistence:

### OrderCreated

Metadata suggestions:

- orderCode,
- clinicCode target,
- caseName,
- requestedDeliveryDate,
- status.

Slice 4 note: technician-created orders store the selected target clinic in `OrderRecord.ClinicCode` / `ClinicDisplayName`, while the existing credential fields may contain the acting technician credential. Audit should explicitly record both acting actor metadata and target clinic metadata instead of inferring actor from order credential fields.

### OrderUpdated

Metadata suggestions:

- orderCode,
- changed fields if practical,
- old/new requestedDeliveryDate if changed,
- old/new status not needed unless update changes status.

For v1, it is acceptable to store a coarse event without full diff if diff implementation is too costly. Document this.

### OrderCancelled

Metadata suggestions:

- orderCode,
- previousStatus,
- newStatus.

## Invoicing/Client Audit Requirements

Log after successful operation:

- client create/update,
- invoice issue/correct,
- import commit summary.

Be careful with preview/analyze:

- `POST /api/invoicing/invoices/preview` probably does not need audit because it does not mutate state.
- import analyze may not mutate permanent state; audit optional.
- import commit mutates state; audit required.

## Optional Audit Read Endpoint

Not required unless explicitly desired, but useful for technician inspection:

```http
GET /api/invoicing/audit?entityType=&entityId=&limit=100
```

Technician-only.

If added, keep simple and read-only.

## Files Expected to Change

- new audit domain/interface files, location TBD
- `Database/Entities/AuditEventEntity.cs`
- `Database/AppDbContext.cs`
- `Database/*Audit*Repo.cs`
- `Database/Migrations/*`
- `Web/WebProgram.cs` DI registration
- `Orders/SchedulingOrderService.cs`
- `Web/Api.cs` or new invoicing app service wrappers
- tests in new/existing test projects

## Tests to Add/Update

Scheduler:

- create order appends `OrderCreated`,
- update order appends `OrderUpdated`,
- cancel order appends `OrderCancelled`,
- failed validation/authorization does not append mutation event.

Persistence:

- audit events are append-only/persisted,
- entity/id query works if implementing read API.

Invoicing/client:

- client create/update writes audit,
- invoice issue/correct writes audit,
- import commit writes audit summary.

## Manual Verification

1. Login as technician.
2. Create/edit/cancel an order.
3. Confirm audit rows in DB or via audit endpoint if implemented.
4. Create/update client.
5. Issue/correct invoice.
6. Confirm audit rows contain actor role/credential and entity ids.
7. Confirm no raw PIN/token data is present.

## Implementation Checklist

- [x] Review Slice 2 and Slice 4 completion notes before starting.
- [x] Choose audit project/boundary and document it in master plan.
- [x] Add audit domain/interface.
- [x] Add DB entity/repository/migration.
- [x] Register audit service in DI.
- [x] Make current actor available to services/handlers.
- [x] Add scheduler audit logging in service methods.
- [x] Add invoicing/client audit logging for mutations.
- [x] Add tests.
- [x] Run relevant tests/build.
- [x] Manually verify DB audit entries.
- [x] Update `master-plan.md` with final status and any future audit enhancements.

## Out of Scope

- Tamper-proof cryptographic audit chains.
- Full UI audit browser unless explicitly added.
- Logging request/response bodies wholesale.
- Auditing non-mutating preview/list reads.

## Completion Notes

- Status: Complete (2026-06-04)
- Files changed:
  - `Utilities/AuditEvent.cs`
  - `Database/Entities/AuditEventEntity.cs`
  - `Database/SqliteAuditLog.cs`
  - `Database/AppDbContext.cs`
  - `Database/Migrations/20260604171331_AddAuditEvents.cs`
  - `Database/Migrations/20260604171331_AddAuditEvents.Designer.cs`
  - `Database/Migrations/AppDbContextModelSnapshot.cs`
  - `Orders/SchedulingOrderService.cs`
  - `Web/SchedulingApi.cs`
  - `Web/Api.cs`
  - `Web/WebProgram.cs`
  - `Orders.Tests/SchedulingOrderServiceTest.cs`
  - `Database.Tests/SqliteAuditLogTest.cs`
  - `Web.Tests/AuditApiTests.cs`
  - `Web.Tests/ApiTestFixture.cs`
  - `plans/order-flow-vertical-slices/master-plan.md`
  - `plans/order-flow-vertical-slices/slice-5-audit-log.md`
- Post-slice audit inspection polish files changed:
  - `Cli/CliProgram.cs`
- Tests run:
  - `dotnet test Orders.Tests/Orders.Tests.csproj --no-restore` — passed, 53 tests.
  - `dotnet test Database.Tests/Database.Tests.csproj --no-restore` — passed, 75 tests.
  - `dotnet test Web.Tests/Web.Tests.csproj --no-restore` — passed, 91 tests.
  - `dotnet test --no-restore` — passed: Configuration 10, Storage 41, Orders 53, Accounting 61, Database 75, Invoices 251, Web 91.
  - `dotnet build Web/Web.csproj --no-restore` — passed.
- Manual checks:
  - Verified audit rows through automated DB inspection in `Web.Tests/AuditApiTests` after real authenticated HTTP mutations.
  - Verified technician read path with `GET /api/invoicing/audit?entityType=Invoice&limit=10` returning issued/corrected invoice audit rows.
  - Verified metadata assertions do not contain demo raw PINs (`123456`, `654321`).
- Audit boundary chosen:
  - `Utilities` owns small audit contracts (`AuditEvent`, `IAuditLog`, `NoOpAuditLog`) to avoid adding a new project.
  - `Database` owns SQLite persistence (`AuditEventEntity`, `SqliteAuditLog`, migration).
  - Scheduler audit writes are service-level in `SchedulingOrderService` after successful repository persistence.
  - Invoicing/client audit writes are route-handler-level in `Web/Api.cs` after successful mutation, because the invoicing/client API currently coordinates repositories/services directly there.
- Post-slice audit inspection polish:
  - Added CLI command `audit list [filters]` in `Cli/CliProgram.cs`.
  - Supported filters include `--service`, `--operation`, `--entity-type`, `--entity-id`, `--actor-role`, `--actor-clinic`, `--actor-credential`, `--since`, `--until`, `--limit`, and `--db`.
  - Supports `--json` for machine-readable export.
- Future audit enhancements:
  - Add a dedicated invoicing/client application service layer and move route-handler audit calls into it.
  - Add richer but still safe diffs for client/invoice corrections.
  - Add pagination/cursoring and UI for audit browsing if needed.
  - Consider tamper-evident hash chaining/signing for regulated deployments.
  - Consider audit retention/export policies and filters by actor/session.
