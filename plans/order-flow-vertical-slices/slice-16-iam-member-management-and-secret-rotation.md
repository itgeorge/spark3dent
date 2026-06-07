# Slice 16 Plan — IAM Member Management and Secret Rotation

*Created: 2026-06-07*

## Goal

Allow lab members to manage members for lab and clinic organizations from IAM.

This slice adds:

- add member,
- edit member label,
- soft-deactivate/reactivate member,
- rotate/update member credential secret.

## Product Rules

- Lab-only.
- Member id is immutable after creation.
- Member label is editable.
- Member deactivation is soft-delete.
- Deactivating a member revokes that member's active sessions.
- Rotating/updating a member secret revokes that member's active sessions.
- Server stores only hash.
- UI default generated secret is six numeric digits, generated on the client.
- User can edit generated secret to any valid custom secret accepted by Slice 13 policy.
- Raw secret is shown only in the create/rotate UI local state and is never retrievable later.

## API Shape

Suggested endpoints:

```http
POST /api/iam/organizations/{code}/members
PUT /api/iam/organizations/{code}/members/{memberId}
DELETE /api/iam/organizations/{code}/members/{memberId}
POST /api/iam/organizations/{code}/members/{memberId}/reactivate
POST /api/iam/organizations/{code}/members/{memberId}/secret
```

Add member request:

```json
{
  "id": "assistant-2",
  "label": "Assistant 2",
  "secret": "123456"
}
```

Update member request:

```json
{
  "label": "Updated Assistant"
}
```

Secret update request:

```json
{
  "secret": "custom secret"
}
```

If there is ambiguity between lab and clinic organization code, include org type in route/query. Because lab and clinics are separate entities but share `/api/iam/organizations/{code}`, either:

- reserve lab code and look up both like existing detail endpoint,
- or add explicit routes for lab members vs clinic members.

Document final choice.

## Backend Behavior

- Validate lab actor.
- Resolve target organization including inactive if needed for management.
- Reject adding active member to inactive organization unless product decides otherwise. Recommendation: disallow member creation for inactive orgs.
- Validate member id:
  - stable code-like string,
  - unique within organization,
  - no whitespace-only,
  - max length.
- Validate label and secret.
- Hash secret with `PinHasher`.
- Revoke sessions when member is deactivated or secret is changed.
- Audit:
  - `MemberCreated`,
  - `MemberUpdated`,
  - `MemberDeactivated`,
  - `MemberReactivated`,
  - `MemberSecretRotated`.
- Never audit raw secret.

## UI Scope

Enhance `/iam` organization detail members section:

- Add member button.
- Edit member label action.
- Deactivate/reactivate action.
- Rotate secret action.
- For add/rotate:
  - generate default six-digit secret client-side,
  - editable secret field,
  - copy/reveal control if desired,
  - warning that it cannot be viewed later.
- After mutation, refresh organization detail.

## Files Expected to Change

Likely:

- `Web/IamApi.cs`
- `Web/wwwroot/iam.html`
- identity repository write methods
- `Database/SqliteSchedulingIdentityRepo.cs`
- `Orders/Repositories.cs`
- `Database/SqliteAuthSessionRepo.cs`
- tests in `Web.Tests/IamApiTests.cs`, `Database.Tests`, `Orders.Tests` if validation lives in Orders

## Tests to Add/Update

- lab can add clinic member with custom secret.
- added member can login.
- duplicate member id rejected.
- clinic actor cannot manage members.
- member label update works.
- member deactivate revokes sessions and prevents login.
- member reactivate allows login if org active and secret unchanged.
- secret rotation revokes sessions and only new secret works.
- raw secret is not returned by API detail endpoints and not stored/audited.
- lab member management works for lab org as well as clinic org if supported.

## Manual Verification

1. Login as lab.
2. Open IAM clinic detail.
3. Add a member with generated default secret; login as that member.
4. Add or rotate using a custom non-six-digit secret; verify login works.
5. Deactivate member; verify existing session/API calls fail and login fails.
6. Reactivate/rotate; verify new login works.
7. Confirm raw secret not visible after closing the create/rotate flow.

## Out of Scope

- Organization creation/editing (Slices 14-15).
- Fine-grained lab roles.
- Self-service clinic member management.
- Hard deletion.

## Implementation Checklist

- [x] Add member create/update/deactivate/reactivate/secret repository methods.
- [x] Add session revocation on member deactivate/secret change.
- [x] Add lab-only API endpoints.
- [x] Add audit events without raw secrets.
- [x] Add IAM members UI actions.
- [x] Add generated editable secret UI for add/rotate.
- [x] Add/update tests.
- [x] Run relevant tests/build.
- [x] Manually verify add/deactivate/rotate flows.
- [x] Update master plan.

## Completion Notes

- Status: Complete (2026-06-08).
- Files changed: `Web/IamApi.cs`, `Web/wwwroot/iam.html`, `Orders/Repositories.cs`, `Database/SqliteSchedulingIdentityRepo.cs`, `Web.Tests/IamApiTests.cs`, `Database.Tests/SqliteSchedulingIdentityRepoTest.cs`.
- Tests run: `dotnet test Orders.Tests/Orders.Tests.csproj --no-restore -p:UseSharedCompilation=false` (66 passed); `dotnet test Database.Tests/Database.Tests.csproj --no-restore -p:UseSharedCompilation=false` (84 passed); `dotnet test Web.Tests/Web.Tests.csproj --no-restore -p:UseSharedCompilation=false` (106 passed); `dotnet build Web/Web.csproj --no-restore -p:UseSharedCompilation=false` passed; `node --check` on extracted `iam.html` inline script passed; full `dotnet test --no-restore -p:UseSharedCompilation=false` passed (Configuration 10, Storage 41, Orders 66, Accounting 61, Database 84, Invoices 251, Web 106).
- Manual checks: Headless Chromium browser smoke passed for member add, secret rotation, member deactivate/reactivate, rotated member login, and deactivated-clinic login rejection after clinic deactivation. API tests covered clinic member add/edit/deactivate/reactivate/secret rotation plus adding a lab member.
- Endpoint shape: `POST /api/iam/organizations/{code}/members`, `PUT /api/iam/organizations/{code}/members/{memberId}`, `DELETE /api/iam/organizations/{code}/members/{memberId}`, `POST /api/iam/organizations/{code}/members/{memberId}/reactivate`, `POST /api/iam/organizations/{code}/members/{memberId}/secret`. Lab and clinic orgs are resolved by the existing `/organizations/{code}` convention, with lab taking precedence when the code matches the singleton lab.
- Secret handling: create/rotate requests accept custom secrets and store only `PinHasher` hashes; detail responses expose only fingerprints. Raw secrets are not audited.
- Session revocation behavior: member deactivate and secret rotation call `SchedulingAuthService.RevokeMemberSessionsAsync`; tests confirm old sessions and old secrets stop working.
- Discoveries: member id is immutable and unique case-insensitively per org; adding members to inactive orgs is blocked.
