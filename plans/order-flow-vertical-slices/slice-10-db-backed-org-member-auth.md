# Slice 10 Plan — DB-Backed Lab/Clinic Members Auth, No IAM UI

*Created: 2026-06-07*

## Goal

Replace JSON-backed scheduling clinic credentials with database-backed identity data while preserving current user-visible behavior.

This slice introduces the correct identity model foundation:

- a singleton-like **Lab** entity representing the business user receiving orders and issuing invoices,
- separate **Clinic organization** entities representing customer clinics,
- DB-backed members/PIN credentials for both lab and clinics,
- auth sessions tied to organization/member identity instead of `ClinicCode`/`CredentialId`,
- no IAM UI yet.

The scheduler and invoicer should continue to work after this slice.

## Current Problem

Current auth uses `ClinicConfig` with credentials nested under clinics. The lab/technician user is represented as a `Technician` credential under demo clinic `DEMO`, which lets it view all orders and submit as selected clinics. This is a walking-skeleton shortcut and should be replaced.

## Target Model

### Lab is separate from clinics

Do **not** model the lab as just another clinic. Add a separate lab entity/table so lab-specific fields can live there later, for example capacity settings, lab calendar settings, branding, invoicing defaults, etc.

Recommended initial entities:

- `SchedulingLabEntity`
  - singleton row, e.g. `Id = 1` or `Code = "LAB"`,
  - `DisplayName`,
  - `IsActive`,
  - optional future `SettingsJson`,
  - timestamps.
- `SchedulingClinicEntity`
  - `Code`,
  - `DisplayName`,
  - `LinkedClientNickname`,
  - optional `DisplayColor`,
  - `IsActive`,
  - timestamps.
- `SchedulingMemberEntity`
  - `Id`,
  - owner discriminator: `OrganizationType` = `Lab | Clinic`,
  - owner key: `OrganizationCode` (`LAB` or clinic code),
  - `Label`,
  - `PinHash`,
  - `IsActive`,
  - timestamps.

A shared member table is acceptable even though lab and clinic org data are separate. If implementation prefers separate `LabMember` and `ClinicMember` tables, document that choice in this plan and master plan.

### Actor model

Replace clinic-centric actor fields with org-centric fields while keeping compatibility properties where helpful during the transition.

Recommended domain shape:

```csharp
public enum OrganizationType
{
    Clinic,
    Lab
}

public sealed record AuthenticatedActor(
    OrganizationType OrganizationType,
    string OrganizationCode,
    string OrganizationDisplayName,
    string MemberId,
    string MemberLabel,
    string MemberPinHashFingerprint,
    string SessionId)
{
    public bool IsLab => OrganizationType == OrganizationType.Lab;
    public bool IsClinic => OrganizationType == OrganizationType.Clinic;
}
```

Temporary aliases like `ClinicCode` can remain only if they reduce churn, but new code should use organization terminology.

### Existing order target clinic fields remain

Order records still need target clinic fields:

- `OrderRecord.ClinicCode`,
- `OrderRecord.ClinicDisplayName`.

For lab-created orders, these identify the selected target clinic, not the acting lab member. Actor/member attribution belongs in audit/session data.

## Data Source Transition

Move auth/org source-of-truth from `Web/scheduling.walking-skeleton.json` to DB.

Recommended approach for v1:

1. Keep work rules and non-auth scheduling options in JSON for now.
2. Move clinics/credentials to DB.
3. Add startup development seed that creates:
   - singleton lab,
   - demo lab member with PIN `654321`,
   - demo clinic `DEMO`,
   - demo clinic member with PIN `123456`.
4. Seed must be idempotent and not overwrite existing hashes unless explicitly designed.
5. Document the seed behavior and any environment/config switch.

If implementation keeps a one-time migration/import from existing JSON, document how it avoids duplicate members.

## Sessions

Update auth session persistence from:

- `ClinicCode`,
- `CredentialId`,

to:

- `OrganizationType`,
- `OrganizationCode`,
- `MemberId`.

Existing dev sessions may be invalidated by the migration. That is acceptable if documented.

## Audit Compatibility

Audit already exists from Slice 5. This slice should stop treating lab actors as clinic actors.

Recommended minimum:

- extend audit contracts/entities/DTOs with actor organization fields:
  - `ActorOrganizationType`,
  - `ActorOrganizationCode`,
  - `ActorMemberId`,
  - `ActorMemberLabel`.
- Keep old `ActorClinicCode` / `ActorCredential*` columns only as compatibility aliases if needed.
- Scheduler audit metadata should continue to include target clinic code separately.

If full audit migration is too large, document a compatibility fallback clearly and add a follow-up task.

## Backend Permissions After This Slice

Behavior should remain equivalent to current permissions:

- Lab members:
  - can access Invoicer,
  - can view all orders,
  - can create orders for selected clinic,
  - can edit/cancel any order,
  - can use audit read endpoint.
- Clinic members:
  - can access Scheduler only,
  - can view/create/edit/cancel only their clinic's orders.

Do not add IAM endpoints/UI in this slice.

## Files Expected to Change

Likely:

- `Orders/AuthenticatedActor.cs`
- `Orders/ClinicConfig.cs` or replacement config/domain files
- `Orders/AuthSession.cs`
- `Orders/Repositories.cs`
- `Orders/SchedulingAuthService.cs`
- `Orders/SchedulingOrderService.cs`
- `Orders/SchedulingConfigValidator.cs` if auth config is removed from JSON
- `Database/Entities/SchedulingAuthSessionEntity.cs`
- new `Database/Entities/SchedulingLabEntity.cs`
- new `Database/Entities/SchedulingClinicEntity.cs`
- new `Database/Entities/SchedulingMemberEntity.cs`
- `Database/AppDbContext.cs`
- new/updated SQLite repositories
- `Database/Migrations/*`
- `Web/SchedulingEndpointAuth.cs`
- `Web/SchedulingApi.cs`
- `Web/Api.cs`
- `Web/WebProgram.cs` DI registrations/seeding
- `Web/scheduling.walking-skeleton.json`
- `Web/wwwroot/index.html`
- `Web/wwwroot/orders.html`
- `Web/wwwroot/js/app-chrome.js`
- audit files in `Utilities`, `Database`, `Web` if adding actor organization fields
- tests in `Orders.Tests`, `Database.Tests`, `Web.Tests`

## Tests to Add/Update

Auth/domain:

- lab member login succeeds and actor has `OrganizationType.Lab` / `IsLab`.
- clinic member login succeeds and actor has `OrganizationType.Clinic` / `IsClinic`.
- inactive lab/clinic/member cannot login.
- old technician-under-clinic shortcut is no longer required.
- auth sessions resolve DB-backed member and org state.
- revoking/deactivating member invalidates future auth.

Permissions:

- lab can call `/api/invoicing/*`.
- clinic gets 403 for `/api/invoicing/*`.
- lab sees all scheduler orders.
- clinic sees only own orders.
- lab can create for selected clinic.
- clinic cannot create for another clinic.

Persistence:

- startup seed/migration creates exactly one lab.
- singleton lab uniqueness is enforced.
- clinic linked client nickname/display color persist.

Audit if changed:

- lab actor audit events include lab org/member fields and target clinic metadata.

## Manual Verification

1. Start with a clean dev DB.
2. Login as lab using demo lab credentials.
3. Confirm Invoicer is accessible and Scheduler shows all orders/default lab behavior.
4. Create an order as lab for demo clinic.
5. Logout/login as demo clinic.
6. Confirm only demo clinic orders are visible and Invoicer/IAM are hidden/inaccessible.
7. Confirm old `Technician` wording is not introduced in new backend outputs, even if UI terminology cleanup is completed in Slice 11.

## Out of Scope

- IAM page/UI.
- IAM organization/member CRUD.
- Client-prefill onboarding UI.
- Complex lab roles such as admin/accountant/technician.
- Capacity configuration UI or capacity scheduling logic.
- Removing every `Technician` string from UI/tests; that is Slice 11.

## Implementation Checklist

- [x] Add lab, clinic, and member DB entities.
- [x] Add auth-session org/member fields and migration.
- [x] Add repositories for lab/clinic/member auth lookup.
- [x] Add idempotent dev seed for singleton lab, lab member, demo clinic, demo clinic member.
- [x] Update auth service to use DB-backed org/member auth.
- [x] Update actor model with organization type/code/member terminology.
- [x] Update permission checks to use `IsLab`/`IsClinic` internally where practical.
- [x] Keep scheduler work rules/config loading intact.
- [x] Update scheduling and invoicing route auth to work with lab actors.
- [x] Update audit actor metadata or document compatibility fallback.
- [x] Add/update tests.
- [x] Run relevant tests/build.
- [x] Manually verify lab and clinic login flows.
- [x] Update master plan and next slice plans with actual implemented shape.

## Completion Notes

- Status: Complete
- Files changed: `Orders/*` auth/session/actor/order-service files; `Database/*` identity entities/repos/context/migration; `Web/*` scheduling/invoicing auth, seed, and routing; `Web.Tests/*`; `Orders.Tests/*`; `Database.Tests/*`; `Cli/CliProgram.cs`; `plans/order-flow-vertical-slices/master-plan.md`
- Tests run: `dotnet build Web/Web.csproj --no-restore -p:UseSharedCompilation=false`; `dotnet test Orders.Tests/Orders.Tests.csproj --no-restore -p:UseSharedCompilation=false`; `dotnet test Database.Tests/Database.Tests.csproj --no-restore -p:UseSharedCompilation=false`; `dotnet test Web.Tests/Web.Tests.csproj --no-restore -p:UseSharedCompilation=false`; `dotnet test --no-restore -p:UseSharedCompilation=false`
- Manual checks: Headless Chromium browser verification passed on `2026-06-07` against a temp DB. Verified unauthenticated `/iam` shows login gate; lab login on `/orders` succeeds with LAB/654321; lab create flow exposes target clinic selector; lab browser session can create an order for DEMO and retrieve it; clinic login on `/orders` succeeds with DEMO/123456 and remains clinic-scoped.
- Lab/clinic/member DB model chosen: separate `SchedulingLabs` and `SchedulingClinics` tables plus shared `SchedulingMembers` table keyed by `(OrganizationType, OrganizationCode, Id)`
- Seed/migration behavior: migration `20260607172254_AddSchedulingIdentityTablesAndLabAuth` adds lab/clinic/member tables and `SchedulingAuthSessions.OrganizationType`; dev/test-only startup seed creates lab `LAB`, lab member `lab-1`/`654321`, clinic `DEMO`, clinic member `assistant-1`/`123456`, and active clinic `OTHER` for test/dev coverage; seed is idempotent and does not overwrite existing members
- Audit compatibility notes: audit/session/order code now uses organization/member terminology while EF column mappings intentionally keep legacy column names (`ActorRole`, `ActorClinicCode`, `Credential*`) underneath to avoid a larger data migration in this slice
- Discoveries affecting Slice 11/12: final auth DTO shape uses `organizationType`, `organizationCode`, `organizationName`, `memberId`, `memberLabel`, `isLab`, `isClinic`; login accepts `organizationCode` and still tolerates old `clinicCode` input for transition
