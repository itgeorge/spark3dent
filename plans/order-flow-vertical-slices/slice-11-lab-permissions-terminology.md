# Slice 11 Plan — Replace Technician Terminology with Lab Permissions

*Created: 2026-06-07*

## Goal

Complete the semantic shift from the temporary `Technician` role to lab-vs-clinic organization permissions.

After Slice 10, auth should be DB-backed and actors should know whether they belong to the singleton Lab or to a Clinic organization. This slice removes/renames remaining technician-facing concepts so code, API DTOs, UI labels, tests, and docs consistently express:

- **Lab member**: business user, can access Invoicer/IAM and manage all orders.
- **Clinic member**: customer clinic user, can access Scheduler for own clinic only.

## Dependencies

Requires Slice 10 to define the actual DB-backed lab/clinic/member actor shape.

Before implementation, read Slice 10 completion notes and update this plan if the actual names differ.

## Target Permission Semantics

Replace checks like:

```csharp
actor.IsTechnician
RequireTechnicianActorAsync
ActorRole.Technician
```

with lab terminology, for example:

```csharp
actor.IsLab
RequireLabActorAsync
OrganizationType.Lab
```

Expected behavior remains:

- Lab members:
  - Invoicer access,
  - Scheduler all-order access,
  - Scheduler create for selected clinic,
  - edit/cancel any order,
  - audit read access,
  - IAM access once Slice 12 exists.
- Clinic members:
  - Scheduler only,
  - own clinic orders only,
  - cannot access Invoicer/Audit/IAM.

## API Response Terminology

Update auth DTOs from technician-centric names to lab-centric names.

Current-ish response fields may include:

```json
{
  "role": "technician",
  "isTechnician": true
}
```

Target response can include compatibility during transition, but new code should prefer:

```json
{
  "organizationType": "lab",
  "isLab": true,
  "isClinic": false
}
```

Recommendation:

- Add `organizationType`, `organizationCode`, `organizationName`, `memberId`, `memberLabel`, `isLab`, `isClinic`.
- Keep old `clinicCode`, `clinicName`, `credentialLabel`, `isTechnician` only temporarily if existing JS/tests need incremental migration.
- Remove compatibility fields if the change is manageable in one slice and tests are updated.

Document final DTO shape in master plan.

## UI Terminology

Update visible labels:

- `Technician` → `Lab`
- `Technician access required` → `Lab access required`
- `technician order review` → `lab order review` or neutral `orders`
- product picker/menu should show IAM only to lab members after Slice 12.

Keep clinic-facing labels simple; do not expose internal org/member concepts unnecessarily.

## Code Cleanup Search Targets

Run searches and either update or intentionally document remaining hits:

```bash
rg -n "Technician|technician|IsTechnician|ActorRole|RequireTechnician" Orders Web Database Utilities Cli* *.sln plans -g '!bin' -g '!obj'
rg -n "ClinicCode|CredentialId|CredentialLabel" Orders Web Database Utilities -g '!bin' -g '!obj'
```

`ClinicCode` remains valid for target clinic/order ownership. It should not be used for lab actor identity.

`Credential*` terminology should be replaced with `Member*` for actor/auth identity where practical. Existing persisted order fields may remain temporarily if Slice 10 chose a compatibility approach; document any remaining compatibility debt.

## Audit Terminology

Audit currently may have technician/clinic naming from earlier slices. Align audit DTOs/log metadata with the final actor model:

- actor organization type/code/name,
- actor member id/label,
- target clinic code/name for orders.

If old audit DB columns remain for compatibility, API/CLI output should prefer new names.

## Files Expected to Change

Likely:

- `Orders/AuthenticatedActor.cs`
- `Orders/Enums.cs` or replacement org type file
- `Orders/SchedulingOrderService.cs`
- `Orders/SchedulingAuthService.cs`
- `Web/SchedulingEndpointAuth.cs`
- `Web/SchedulingApi.cs`
- `Web/Api.cs`
- `Web/wwwroot/index.html`
- `Web/wwwroot/orders.html`
- `Web/wwwroot/js/app-chrome.js`
- `Web.Tests/*`
- `Orders.Tests/*`
- `Database.Tests/*`
- audit CLI/API files if actor fields are renamed
- plan/docs references as needed

## Tests to Add/Update

- lab auth/me response includes `isLab` / `organizationType=lab`.
- clinic auth/me response includes `isClinic` / `organizationType=clinic`.
- clinic cannot access lab-only endpoints and error says lab access required.
- lab can access invoicing/audit/order-management endpoints.
- UI product menu hides Invoicer/IAM from clinic actors based on `isLab`.
- Existing scheduler create/edit/cancel tests pass with lab terminology.

## Manual Verification

1. Login as lab.
2. Confirm actor/account menu says Lab, not Technician.
3. Confirm Invoicer and Scheduler are visible and work.
4. Login as clinic.
5. Confirm account menu says Clinic and Invoicer is hidden.
6. Directly call a lab-only endpoint as clinic and confirm 403 uses lab terminology.
7. Search repo for stale technician terms and document any intentional leftovers.

## Out of Scope

- IAM page implementation (Slice 12).
- IAM organization/member mutation.
- Complex lab sub-roles.
- Removing target clinic terminology from orders; orders still target clinics.

## Implementation Checklist

- [ ] Read Slice 10 completion notes and confirm actual actor/org/member names.
- [ ] Replace `ActorRole.Technician`/`IsTechnician` with lab organization permissions.
- [ ] Rename centralized auth filters/helpers to lab terminology.
- [ ] Update auth/me and login DTOs to expose org/member/lab fields.
- [ ] Update scheduler/invoicing API checks and error messages.
- [ ] Update app chrome/product switcher to use `isLab`.
- [ ] Update UI labels from Technician to Lab.
- [ ] Update audit API/CLI output names where applicable.
- [ ] Update tests.
- [ ] Run repo-wide stale terminology searches and document intentional leftovers.
- [ ] Run relevant tests/build.
- [ ] Update master plan and Slice 12 with final terminology.

## Completion Notes

Fill in after implementation.

- Status:
- Files changed:
- Tests run:
- Manual checks:
- Final auth DTO shape:
- Remaining compatibility aliases/debt:
- Discoveries affecting Slice 12:
