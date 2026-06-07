# Slice 15 Plan — IAM Edit, Deactivate, and Reactivate Clinic Organizations

*Created: 2026-06-07*

## Goal

Allow lab members to manage existing clinic organization metadata from IAM.

This slice covers clinic organization editing and soft-deactivation/reactivation. It does not manage members beyond effects required by organization status changes.

## Product Rules

- Lab-only.
- Clinic code is immutable after creation.
- Editable clinic fields:
  - display name,
  - linked client nickname,
  - display color.
- Display color is optional or generated but must validate as `#RRGGBB` if supplied.
- `DELETE`/deactivate is always soft-delete.
- Deactivating a clinic revokes all sessions for that clinic's members.
- Deactivated clinics remain visible in IAM when `includeInactive=true`.
- Historical orders and audits remain intact and continue to show stored clinic code/display data.
- Reactivation is explicit.

## API Shape

Suggested endpoints:

```http
PUT /api/iam/organizations/{code}
DELETE /api/iam/organizations/{code}
POST /api/iam/organizations/{code}/reactivate
```

Update request:

```json
{
  "displayName": "Updated Clinic Name",
  "linkedClientNickname": "client-nickname-or-null",
  "displayColor": "#0ea5e9"
}
```

Response returns the same clinic detail DTO used by read-only IAM.

## Backend Behavior

Update:

- Validate lab actor.
- Reject lab singleton edits through clinic endpoint; lab profile editing is a future slice if needed.
- Validate display name/client nickname/display color.
- If linked client nickname supplied, verify client exists if practical.
- Update `UpdatedAt`.
- Audit `ClinicUpdated` with old/new changed fields where practical.

Deactivate:

- Validate lab actor.
- Set `IsActive=false`, update timestamp.
- Revoke sessions for that clinic organization.
- Audit `ClinicDeactivated`.
- Do not delete members, orders, or audit rows.
- Idempotency: choose either idempotent success or 400 if already inactive. Recommendation: idempotent success returning current detail.

Reactivate:

- Validate lab actor.
- Set `IsActive=true`, update timestamp.
- Audit `ClinicReactivated`.
- Does not automatically reactivate members.

## UI Scope

Enhance `/iam` detail panel:

- Add Edit button for clinic details.
- Add Deactivate/Reactivate button based on current state.
- Edit form fields:
  - display name,
  - linked client nickname/client picker if Slice 14 added client search,
  - display color picker/text input.
- Show destructive confirmation before deactivate.
- After save/deactivate/reactivate:
  - refresh organization list/detail,
  - preserve selected org where possible.

## Files Expected to Change

Likely:

- `Web/IamApi.cs`
- `Web/wwwroot/iam.html`
- identity repository/service write methods
- `Database/SqliteSchedulingIdentityRepo.cs`
- `Orders/Repositories.cs`
- auth session repository if org session revocation helper needs adjustment
- `Web.Tests/IamApiTests.cs`
- `Database.Tests` if repository writes are tested
- audit tests

## Tests to Add/Update

- lab can update clinic display name/client/color.
- clinic actor cannot update/deactivate/reactivate.
- code cannot be changed via update payload.
- invalid color rejected.
- linked client validation behavior tested if implemented.
- deactivate marks inactive and revokes clinic sessions.
- deactivated clinic cannot login.
- deactivated clinic remains visible with `includeInactive=true`.
- reactivate marks active; active member can login again if member is active.
- audit events created without sensitive data.

## Manual Verification

1. Login as lab.
2. Create or use demo clinic.
3. Edit display name/color/client link; verify IAM reflects change.
4. Login as clinic in a separate browser/session.
5. Deactivate clinic as lab; verify clinic session is invalidated or subsequent authenticated calls fail.
6. Verify clinic login fails while inactive.
7. Reactivate clinic; verify active member can login.

## Out of Scope

- Editing lab profile/settings.
- Creating/deactivating members.
- PIN rotation.
- Hard deletion.
- Scheduler color display use.

## Implementation Checklist

- [ ] Add update/deactivate/reactivate identity repository methods.
- [ ] Add lab-only API endpoints.
- [ ] Revoke clinic sessions on deactivate.
- [ ] Add audit events.
- [ ] Add IAM UI edit/deactivate/reactivate controls.
- [ ] Add/update tests.
- [ ] Run relevant tests/build.
- [ ] Manually verify session revocation and reactivation behavior.
- [ ] Update master plan.

## Completion Notes

Fill in after implementation.

- Status:
- Files changed:
- Tests run:
- Manual checks:
- Endpoint shape:
- Session revocation behavior:
- Discoveries affecting member-management slice:
