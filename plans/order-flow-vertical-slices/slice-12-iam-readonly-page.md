# Slice 12 Plan — Lab-Only IAM Read-Only Page

*Created: 2026-06-07*

## Goal

Add the first IAM product surface for lab members: a read-only organization/member management page.

This slice does **not** add create/edit/delete/PIN generation yet. It provides the navigable IAM product and read-only APIs/UI needed to inspect lab/clinic identity data after Slices 10-11.

## Product Rules

- IAM is visible in the product picker only to lab members.
- Clinic members cannot see IAM in navigation.
- Direct `/iam` access by unauthenticated users should show login or redirect to a login gate consistent with `/` and `/orders`.
- Direct `/iam` access by clinic members should redirect to `/orders` or show a simple access-denied shell.
- Lab members can view:
  - singleton lab details,
  - lab members,
  - clinic organizations,
  - clinic members,
  - linked invoicing client nickname,
  - optional display color,
  - active/inactive status.

## Dependencies

Requires:

- Slice 10 DB-backed lab/clinic/member auth.
- Slice 11 lab terminology and `isLab`/organization auth DTOs.

Before implementation, read completion notes for Slices 10 and 11 and update endpoint/entity names in this plan if needed.

## Suggested Routes

Page:

```http
GET /iam
```

API group, lab-only:

```http
GET /api/iam/lab
GET /api/iam/organizations?includeInactive=true|false
GET /api/iam/organizations/{code}
GET /api/iam/clients?query=&limit=20
```

The client endpoint is optional in the read-only slice, but useful for previewing future “create org from client” work. If included, it should list invoicing clients with enough info to associate a clinic org with `Accounting.Client.Nickname`.

Alternative: place IAM under `/api/invoicing/iam`. Preferred for clarity: `/api/iam/*`, protected by lab-only auth.

## API DTOs

Recommended list DTO:

```json
{
  "items": [
    {
      "code": "DEMO",
      "displayName": "Demo Dental Clinic",
      "linkedClientNickname": "demo-client",
      "displayColor": "#7c3aed",
      "isActive": true,
      "memberCount": 2,
      "activeMemberCount": 1
    }
  ]
}
```

Recommended detail DTO:

```json
{
  "code": "DEMO",
  "displayName": "Demo Dental Clinic",
  "linkedClientNickname": "demo-client",
  "displayColor": "#7c3aed",
  "isActive": true,
  "createdAt": "...",
  "updatedAt": "...",
  "members": [
    {
      "id": "assistant-1",
      "label": "Assistant 1",
      "isActive": true,
      "createdAt": "...",
      "updatedAt": "...",
      "pinFingerprint": "..."
    }
  ]
}
```

Do not return PIN hashes or raw PINs.

Lab detail DTO can show singleton lab profile and lab members. Keep lab-specific settings read-only/placeholder if added by Slice 10.

## UI Scope

Create `Web/wwwroot/iam.html` or equivalent embedded/static page.

Recommended layout:

- shared `AppChrome` topbar with product `iam`, visible only to lab actors,
- login gate matching existing app behavior,
- left/list panel of clinic organizations,
- detail panel for selected organization,
- lab profile card/section,
- status chips for active/inactive,
- linked client nickname display,
- display color swatch if present,
- members table/list.

Because this is read-only, disabled or absent mutation controls are fine. If showing future controls, clearly mark them as not implemented.

## AppChrome/Product Picker

Update shared product list in `Web/wwwroot/js/app-chrome.js`:

- Invoicer: lab only,
- Scheduler: all authenticated actors,
- IAM: lab only.

If `AppChrome` currently hardcodes only Invoicer/Scheduler, extend it to accept product visibility based on `actor.isLab`.

## Accounting Client Integration

For read-only IAM, the minimum connection is displaying `LinkedClientNickname` and optionally resolving it to client display data.

Use `Accounting/Client.cs` shape:

```csharp
public record Client(string Nickname, BillingAddress Address);
```

If implementing `GET /api/iam/clients`, return:

- nickname,
- billing name (`Address.Name`),
- city/company id if useful for disambiguation.

Do not create or mutate clients in this slice.

## Backend Scope

Add read-only IAM service/endpoints around the org/member repositories introduced in Slice 10.

- Lab-only auth filter.
- List clinics.
- Get clinic detail + members.
- Get lab detail + lab members.
- Optional client search/list for future prefill.

Soft-deleted/inactive behavior:

- default list can show active only or all; choose and expose `includeInactive`.
- detail should allow viewing inactive orgs by code for audit/admin inspection.

## Files Expected to Change

Likely:

- new `Web/wwwroot/iam.html`
- `Web/Web.csproj` if embedded resource is used
- `Web/WebProgram.cs` page route
- new `Web/IamApi.cs` or additions to existing API mapping
- `Web/wwwroot/js/app-chrome.js`
- `Web/wwwroot/css/app-chrome.css` if menu/layout needs tweaks
- IAM CSS/JS if split out
- org/member repositories from Slice 10
- `Accounting/Client.cs` only if DTO helper needs extension methods; avoid changing it if possible
- `Web.Tests/*` for IAM auth/API tests
- docs/plans

## Tests to Add/Update

API/auth:

- unauthenticated `/api/iam/organizations` returns 401.
- clinic member gets 403.
- lab member gets organization list.
- lab member gets organization detail with members but not PIN hashes.
- inactive org/member visibility follows chosen `includeInactive` behavior.
- optional client search/list returns client nickname and display fields for lab only.

UI/page tests if available:

- clinic product menu hides IAM.
- lab product menu shows IAM.
- lab `/iam` page loads org list/detail.
- direct clinic `/iam` redirects or access-denied.

## Manual Verification

1. Login as lab.
2. Confirm hamburger/product picker includes IAM.
3. Open IAM.
4. Confirm lab profile and clinic org list render.
5. Open demo clinic detail and confirm members are shown without raw hashes/PINs.
6. Confirm linked client nickname/display color render if present.
7. Logout/login as clinic.
8. Confirm IAM is hidden and direct `/iam` is blocked/redirected.

## Out of Scope

- Creating/editing/deactivating organizations.
- Generating or rotating PINs.
- Creating clinic orgs from clients.
- Client mutation.
- Capacity configuration UI.
- Fine-grained lab roles.

## Implementation Checklist

- [ ] Read Slice 10/11 completion notes and confirm entity/API names.
- [ ] Add lab-only IAM API group.
- [ ] Add lab detail endpoint.
- [ ] Add clinic organization list endpoint.
- [ ] Add clinic organization detail + members endpoint.
- [ ] Optionally add lab-only client list/search endpoint for future prefill.
- [ ] Add `/iam` page route and page file.
- [ ] Extend `AppChrome` products with IAM visible to lab only.
- [ ] Implement read-only IAM UI.
- [ ] Ensure no PIN hashes/raw PINs are returned to browser.
- [ ] Add/update tests.
- [ ] Run relevant tests/build.
- [ ] Manually verify lab vs clinic IAM access.
- [ ] Update master plan with implemented shape and next-slice recommendations.

## Completion Notes

Fill in after implementation.

- Status:
- Files changed:
- Tests run:
- Manual checks:
- IAM endpoint shape:
- AppChrome/product picker behavior:
- Discoveries for future IAM mutation slices:
