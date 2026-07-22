# Order owner rollout prep: disable creation + member reassignment script

## Context

We are preparing to change scheduling order visibility so clinic members see only orders assigned to them. Existing order rows already have `CredentialId` / `CredentialLabel` columns, exposed in code as `MemberId` / `MemberLabel`, but historical LAB-created orders may have the LAB member stored there instead of the intended clinic member.

Before implementing member-scoped visibility and LAB member selection, we will run a controlled data reassignment while new order creation is disabled. Current production usage has known quiet windows, but creation should still be blocked at the API level during the migration window.

## Goals

1. Add a small temporary code change that disables new scheduling order creation.
2. Add a one-off CLI/admin helper to report, validate, and apply in-place order owner reassignment.
3. Keep existing order codes, IDs, timestamps, audit logs, recommendation logs, and override logs intact.
4. Avoid EF schema migrations for this prep step.
5. Produce clear artifacts before and after applying changes.

## Non-goals

- Do not implement member-scoped order visibility in this step.
- Do not implement LAB selection of target clinic member in the order creation UI in this step.
- Do not create duplicate replacement orders.
- Do not delete/cancel old orders.
- Do not add a config flag for disabling order creation; this rollout uses two deployments instead.

## Deployment sequence

### Deployment 1: disable creation + add reassignment helper

Deploy a commit containing:

- API-level block for `POST /api/scheduling/orders`.
- Optional UI affordance hiding/disabling `+Нова поръчка`, but API blocking is mandatory.
- CLI/admin helper for the reassignment workflow.

During this deployment, existing order list/review/edit/cancel behavior may remain available.

### Data reassignment window

1. Confirm no users are active.
2. Back up the database.
3. Run report mode.
4. Developer reviews generated assignment template and fills target clinic member IDs.
5. Run validate mode.
6. Run apply mode.
7. Save generated before/after reports with the backup.

### Deployment 2: owner-supporting implementation

Deploy the later implementation that:

- scopes clinic member reads to assigned orders,
- requires LAB order creation to specify clinic + clinic member,
- writes the selected clinic member to `MemberId` / `MemberLabel`,
- keeps actual LAB actor attribution in audit/deadline logs,
- re-enables order creation.

## Temporary order creation disable

### Required behavior

In `Web/SchedulingApi.cs`, `POST /api/scheduling/orders` should return a non-2xx response before reading/processing the order draft.

Suggested response:

- HTTP `503 Service Unavailable` or `423 Locked`.
- JSON body:

```json
{ "error": "Order creation is temporarily disabled for maintenance." }
```

### Acceptance checks

- Anonymous request still returns unauthenticated if authentication is checked first, or maintenance response if the block is intentionally first. Prefer preserving auth-first behavior.
- Authenticated clinic request to create order receives the maintenance response.
- Authenticated LAB request to create order receives the maintenance response.
- Existing endpoints still work:
  - `GET /api/scheduling/orders`
  - `GET /api/scheduling/orders/{code}`
  - `PUT /api/scheduling/orders/{code}`
  - `DELETE /api/scheduling/orders/{code}`
  - `GET /api/scheduling/orders/calendar`

### Tests to update/add

- Add/adjust API test proving authenticated order creation is blocked with the expected error/status.
- Existing create-flow tests may need temporary adjustment/skip only in this branch if they call the blocked endpoint. Prefer a targeted helper/constant so Deployment 2 can easily remove the block and restore tests.

## Reassignment helper

### Preferred location

Add to `Cli` as a scheduling subcommand, because `Cli` already wires config, SQLite, EF migrations, IAM helpers, and scheduling helpers.

Example command family:

```bash
dotnet run --project Cli -- scheduling order-owner-report --db <path> --out <path>
dotnet run --project Cli -- scheduling order-owner-validate --db <path> --assignments <path>
dotnet run --project Cli -- scheduling order-owner-apply --db <path> --assignments <path> --backup-confirmed --out <path>
```

Exact names can vary, but keep separate report/validate/apply modes.

### File format

Use JSON for less ambiguous structured data. CSV is acceptable if easier, but JSON should reduce escaping and nested member-list issues.

Report/template output should include one item per order:

```json
{
  "generatedAtUtc": "2026-07-22T00:00:00Z",
  "orders": [
    {
      "orderId": 123,
      "orderCode": "26-0605-Z1AA",
      "status": "Created",
      "clinicCode": "DEMO",
      "clinicDisplayName": "Demo Dental Clinic",
      "currentMemberId": "lab-1",
      "currentMemberLabel": "Lab Member 1",
      "caseName": "Case",
      "requestedDeliveryDate": "2026-06-05",
      "createdAtUtc": "2026-05-31T12:00:00Z",
      "activeClinicMembers": [
        { "memberId": "assistant-1", "memberLabel": "Assistant 1" }
      ],
      "targetMemberId": ""
    }
  ]
}
```

Developer edits only `targetMemberId` for each order that needs reassignment. It is fine for already-correct orders to either keep `targetMemberId` blank or set it equal to the current member; validation/apply behavior must document which convention is used.

Recommended convention:

- blank `targetMemberId` means no change,
- non-blank `targetMemberId` means update to that active member in the order's clinic.

### Report mode requirements

For every order in `SchedulingOrders`, output:

- `Id`
- `OrderCode`
- `Status`
- `ClinicCode`
- `ClinicDisplayName`
- current `CredentialId` / `CredentialLabel`
- `CaseName`
- `RequestedDeliveryDate`
- `CreatedAt`
- active clinic members for that order's clinic
- an empty `targetMemberId` field for developer review

Nice-to-have:

- include inactive clinic members separately only if useful for investigation, but apply should require active target members unless explicitly overridden.
- include a flag like `currentMemberMatchesActiveClinicMember`.
- include latest `OrderCreated` audit actor if easy, but do not make the helper depend on audit availability.

### Validate mode requirements

Read the edited assignments file and fail with non-zero exit code if any error exists.

Validate each order with a non-blank `targetMemberId`:

- order exists by `orderId` and/or `orderCode`, preferably requiring both to match if both are present,
- order clinic has not changed since report generation,
- target member exists,
- target member belongs to the order's clinic,
- target member is active,
- duplicate order entries are rejected,
- no raw PIN hashes or fingerprints are read/written to output.

Print a summary:

- total orders in assignment file,
- number with no change,
- number to update,
- per-clinic update counts,
- any validation errors.

### Apply mode requirements

Apply must do everything validate does, then update in place:

- Update `SchedulingOrders.CredentialId` to target member ID.
- Update `SchedulingOrders.CredentialLabel` to target member label.
- Do not change order `Id`, `OrderCode`, status, created/updated timestamps, case data, deadline data, or notes.
- Do not touch `CredentialPinHashFingerprint`; leave existing compatibility behavior intact.
- Run in a transaction.
- Require an explicit safety switch such as `--backup-confirmed`.
- Write a before/after output file.

Before/after output should include:

```json
{
  "appliedAtUtc": "2026-07-22T00:00:00Z",
  "updatedOrders": [
    {
      "orderId": 123,
      "orderCode": "26-0605-Z1AA",
      "clinicCode": "DEMO",
      "caseName": "Case",
      "oldMemberId": "lab-1",
      "oldMemberLabel": "Lab Member 1",
      "newMemberId": "assistant-1",
      "newMemberLabel": "Assistant 1"
    }
  ]
}
```

### Audit trail

Because this is an administrative data correction before the feature rollout, either:

- write a single audit event per updated order with operation like `OrderOwnerReassigned`, actor organization `System`, and metadata containing old/new member, source assignment file name, and `backupConfirmed: true`; or
- write a standalone before/after report if keeping audit untouched is preferred.

Preferred: append audit events if straightforward using existing `SqliteAuditLog`, while still producing the external before/after report.

### Idempotency

The apply command should be safe to rerun:

- if an order already has the target member, report it as unchanged/already applied,
- do not fail solely because the current member now equals the target member,
- still fail if the order was changed to some other member than expected and the assignment file includes old-member guard fields.

Recommended: include `currentMemberId` in the assignment file and warn/fail if current DB member differs from both `currentMemberId` and `targetMemberId`, unless an explicit `--force-current-mismatch` flag is supplied.

## Safety checklist for running in production

Before apply:

- [ ] Deployment 1 is live and `POST /api/scheduling/orders` is blocked.
- [ ] Database backup completed and backup file path recorded.
- [ ] Report file generated from the same DB that will be updated.
- [ ] Assignment file reviewed and saved.
- [ ] Validate mode passes.

After apply:

- [ ] Before/after report saved next to backup.
- [ ] Spot-check several orders in DB/API.
- [ ] Keep creation blocked until Deployment 2 is live.

## Suggested validation commands

After implementation of this prep step:

```bash
dotnet build Spark3Dent.sln --no-restore
dotnet test Spark3Dent.sln --no-build
```

Manual smoke checks against a local copy of production DB:

```bash
dotnet run --project Cli -- scheduling order-owner-report --db <copy.db> --out artifacts/order-owner-report.json
dotnet run --project Cli -- scheduling order-owner-validate --db <copy.db> --assignments artifacts/order-owner-report-reviewed.json
dotnet run --project Cli -- scheduling order-owner-apply --db <copy.db> --assignments artifacts/order-owner-report-reviewed.json --backup-confirmed --out artifacts/order-owner-apply-result.json
```

## Handoff notes for implementation agent

- Keep this branch narrowly focused on disabling creation and the reassignment helper.
- Do not start member-scoped visibility or LAB member-selection implementation here.
- Prefer direct EF/DbContext access in the helper if it keeps validation/reporting clear.
- Be careful with SQLite date/time JSON serialization; existing domain mappings can be reused where helpful.
- Update tests only as necessary for this temporary deployment branch.
