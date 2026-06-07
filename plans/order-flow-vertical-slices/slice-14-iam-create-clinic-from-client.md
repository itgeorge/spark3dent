# Slice 14 Plan — IAM Create Clinic from Client with Initial Member

*Created: 2026-06-07*

## Goal

Allow lab members to create a clinic organization from IAM, preferably prefilled from an existing invoicing client, and require creation of the clinic's first member in the same flow.

This is the first IAM mutation slice.

## Product Rules

- Lab-only.
- Clinics cannot access the APIs or UI controls.
- Creating a clinic requires an initial active member.
- UI should guide the lab user through clinic details first, then initial member details.
- UI generates a default six-digit secret on the client, but the user may edit it to any valid credential secret accepted by Slice 13's relaxed secret policy.
- Server stores only the secret hash.
- The submitted raw secret is not retrievable later. Since the lab user entered/edited it in the UI, the response does not need to return it; UI can display a reminder to copy/share before submitting or immediately after submit from local state only.
- Organization code is stable after creation and should not be editable later.
- Organization/member deletes are soft-deactivation in later slices.

## Client Prefill

Add lab-only client search/prefill using `Accounting.Client`:

```csharp
public record Client(string Nickname, BillingAddress Address);
```

Suggested endpoints:

```http
GET /api/iam/clients?query=&limit=20
GET /api/iam/clients/{nickname}/prefill
```

Minimum client list fields:

- nickname,
- display/billing name (`Address.Name`),
- company identifier,
- city.

Prefill output:

- suggested clinic code from client nickname,
- display name from billing name,
- linked client nickname,
- generated display color.

## Code and Color Generation

Clinic code:

- uppercase,
- alphanumeric plus `-` or `_` only (choose one and document),
- max length, suggested 32,
- cannot be empty,
- cannot equal lab code or reserved words like `LAB`, `IAM`, `INVOICING`, `SCHEDULER`,
- must be unique case-insensitively.

Display color:

- default generated deterministically from client nickname/display name,
- user can edit before create,
- validate `#RRGGBB`,
- optional if implementation chooses, but generated default should be present.

## API Shape

Suggested create endpoint:

```http
POST /api/iam/organizations
```

Request:

```json
{
  "code": "DEMO",
  "displayName": "Demo Dental Clinic",
  "linkedClientNickname": "demo-client",
  "displayColor": "#7c3aed",
  "initialMember": {
    "id": "assistant-1",
    "label": "Assistant 1",
    "secret": "123456"
  }
}
```

Response:

```json
{
  "organization": { ...clinic detail dto... }
}
```

Do not include raw secret in response.

## Backend Behavior

- Validate lab actor.
- Validate clinic code/display name/color/client link/member id/member label/secret.
- If linked client nickname is provided, verify it exists if practical. If verifying through client repo is difficult, accept string for now but document the tradeoff.
- Create `SchedulingClinic` row active.
- Create initial `SchedulingMember` row active.
- Hash secret using `PinHasher`.
- Ensure operation is atomic/transactional.
- Audit `ClinicCreated` and `MemberCreated` or a single `ClinicCreated` event with initial member metadata. Prefer both if easy, but never audit raw secret.

## UI Scope

Enhance `/iam`:

- Add `New clinic` action visible only to lab.
- Modal or dedicated panel flow:
  1. Optional client search/select.
  2. Clinic details prefilled/editable: code, display name, linked client, display color.
  3. Initial member: member id, label, generated secret.
  4. Review/create.
- Secret field:
  - client-generated default six-digit numeric,
  - editable text/password field with reveal/copy controls if desired,
  - warning: secret is not recoverable later.
- After create:
  - show created clinic detail,
  - keep the submitted secret visible from local state until user dismisses/copies, or show a clear final confirmation using client-held value.

## Files Expected to Change

Likely:

- `Web/IamApi.cs`
- `Web/wwwroot/iam.html`
- `Orders/Repositories.cs` identity write methods or new IAM service
- `Database/SqliteSchedulingIdentityRepo.cs`
- `Web/Api.cs` only if reusing client DTO helpers is necessary
- tests in `Web.Tests/IamApiTests.cs`
- possible `Database.Tests` for identity repo writes
- audit tests if added at route/service level

## Tests to Add/Update

API:

- unauthenticated create returns 401.
- clinic actor create returns 403.
- lab actor can search clients.
- client prefill returns suggested code/name/client/color.
- lab actor can create clinic + initial member.
- duplicate clinic code returns 400/409.
- invalid color rejected.
- missing initial member rejected.
- too-short/invalid secret rejected according to Slice 13 policy.
- created clinic member can login.
- audit event(s) created without raw secret.

UI/manual:

- client search/prefill populates fields.
- generated six-digit secret can be edited.
- create succeeds and new clinic appears in organization list.
- login as new clinic member works.

## Out of Scope

- Editing existing clinics.
- Deactivating/reactivating clinics.
- Adding additional members after clinic creation.
- Rotating member secrets.
- Scheduler display use of clinic color.

## Implementation Checklist

- [ ] Add client search/prefill endpoint(s).
- [ ] Add clinic/member create request/validation.
- [ ] Add transactional identity repository create method(s).
- [ ] Add lab-only `POST /api/iam/organizations`.
- [ ] Audit clinic/member creation without raw secret.
- [ ] Add IAM UI create flow with generated editable secret.
- [ ] Add/update tests.
- [ ] Manually verify create-from-client and new clinic login.
- [ ] Update master plan and next slices with implemented DTO shape.

## Completion Notes

Fill in after implementation.

- Status:
- Files changed:
- Tests run:
- Manual checks:
- Endpoint shape:
- Validation rules:
- Secret handling:
- Discoveries affecting edit/member slices:
