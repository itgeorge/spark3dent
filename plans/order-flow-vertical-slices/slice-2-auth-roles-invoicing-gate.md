# Slice 2 Plan — Technician Role + Centralized Auth + `/api/invoicing` Gate

*Created: 2026-06-03*

## Goal

Introduce technician accounts and central authorization behavior:

1. clinic accounts can only view/create scheduler orders for themselves,
2. technician accounts can view all scheduler orders,
3. all invoice/client APIs are moved under `/api/invoicing/*`,
4. all `/api/invoicing/*` APIs require technician access,
5. avoid endpoint-by-endpoint auth mistakes by grouping/filtering routes centrally.

This slice is intentionally backend/security-heavy. UI navigation is Slice 3.

## Product Decisions for This Slice

- Use `/api/invoicing` instead of generic `/api` for invoicer/client APIs.
- Non-technician should have no access to invoicing/client endpoints.
- Scheduler endpoints remain under `/api/scheduling`.
- Technician order list can reuse or replace the old `/api/scheduling/technician/orders`; target state is preferably one `GET /api/scheduling/orders` whose result depends on actor role.

## Recommended Auth Model

Extend actor identity with role:

```csharp
public enum ActorRole
{
    Clinic,
    Technician
}
```

Update `Orders/AuthenticatedActor.cs` to include:

```csharp
ActorRole Role
bool IsTechnician => Role == ActorRole.Technician;
```

### Config shape recommendation

Smallest change: add role on credential config.

Current `Orders/ClinicConfig.cs`:

```csharp
public sealed record ClinicCredentialConfig
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public string PinHash { get; init; } = "";
    public bool IsActive { get; init; } = true;
}
```

Add:

```csharp
public ActorRole Role { get; init; } = ActorRole.Clinic;
```

Then define a technician credential in scheduling config. If it must belong to a clinic for session storage, either:

- use a special lab/technician clinic code, e.g. `TECH`, or
- allow a clinic credential with `Role: Technician`.

Document the chosen convention in the master plan and config comments/README if any.

## Central Auth/Authorization Approach

### Scheduling auth

Keep scheduling auth cookie/session as the single app auth for now.

Create helper/filter methods so routes do not duplicate auth resolution logic. Options:

1. Minimal route-group extension methods in `Web/SchedulingApi.cs` / new `Web/AuthEndpointFilters.cs`.
2. Endpoint filters that populate actor in `HttpContext.Items`.

Target concepts:

```csharp
RequireSchedulingActor
RequireTechnicianActor
```

### Invoicing route group

Move mapping in `Web/Api.cs` from direct `app.MapGet("/api/clients"...)` calls to a route group:

```csharp
var invoicing = app.MapGroup("/api/invoicing")
    .AddEndpointFilter(... technician-only ...);
```

Then routes become relative:

- `/api/invoicing/clients`
- `/api/invoicing/clients/latest`
- `/api/invoicing/clients/{nickname}`
- `/api/invoicing/invoices`
- `/api/invoicing/invoices/{number}`
- `/api/invoicing/invoices/issue`
- `/api/invoicing/invoices/correct`
- `/api/invoicing/invoices/preview`
- `/api/invoicing/invoices/import/analyze`
- `/api/invoicing/invoices/import/commit`
- `/api/invoicing/invoices/{number}/pdf`

Update all fetch calls in `Web/wwwroot/index.html` from `/api/...` to `/api/invoicing/...`.

## Scheduler Route Behavior After Slice 2

Preferred target:

```http
GET /api/scheduling/orders
```

Behavior:

- clinic actor: returns only own clinic orders,
- technician actor: returns all orders.

Existing route:

```http
GET /api/scheduling/technician/orders
```

Options:

- remove if no frontend still uses it,
- or keep temporarily as alias for technician only and document deprecation.

Be explicit in master plan which choice was made.

## Page Access Behavior

This slice does not need full app switcher UI, but should decide basic `/` protection:

- If `/` is loaded by a non-technician, invoicing APIs must fail anyway.
- Prefer one of:
  - show existing page but API calls fail with 403 (not ideal),
  - server-side redirect non-technicians from `/` to `/orders`,
  - serve an access-denied shell.

Current product direction for Slice 3: non-technician users should not see invoicer at all. If implementing page protection now is straightforward, redirect non-technicians to `/orders`; otherwise leave page behavior to Slice 3 but ensure APIs are protected in Slice 2.

## Files Expected to Change

- `Orders/AuthenticatedActor.cs`
- `Orders/ClinicConfig.cs`
- `Orders/SchedulingAuthService.cs`
- `Orders/SchedulingConfigValidator.cs` if credential validation needs role checks
- `Web/scheduling.walking-skeleton.json` or configured scheduling JSON
- `Web/SchedulingApi.cs`
- `Web/Api.cs`
- possible new `Web/AuthEndpointFilters.cs` / `Web/AuthHelpers.cs`
- `Web/WebProgram.cs`
- `Web/wwwroot/index.html`
- tests in `Orders.Tests` and possibly new/updated Web tests

## Tests to Add/Update

Auth/domain tests:

- login with clinic credential returns actor role clinic,
- login with technician credential returns actor role technician,
- inactive technician credential cannot login,
- config validation accepts explicit roles and defaults missing role to clinic.

Scheduling API/service tests if practical:

- clinic `GET /api/scheduling/orders` returns only own orders,
- technician `GET /api/scheduling/orders` returns all orders,
- unauthenticated returns 401.

Invoicing route tests if practical:

- unauthenticated `/api/invoicing/clients` returns 401,
- clinic-authenticated `/api/invoicing/clients` returns 403,
- technician-authenticated `/api/invoicing/clients` succeeds.

Frontend compile/manual:

- all `index.html` fetch paths updated to `/api/invoicing/...`.

Use `rg` to verify stale paths:

```bash
rg -n '"/api/(clients|invoices)' Web/wwwroot/index.html Web -g '*.cs' -g '*.html'
```

## Manual Verification

1. Login as clinic on `/orders`; confirm list shows only clinic orders.
2. Attempt to call `/api/invoicing/clients`; confirm 403 or 401 depending auth state.
3. Login as technician; confirm scheduler list shows all orders.
4. As technician, open invoicer `/`; confirm invoice/client data loads through `/api/invoicing/*`.
5. Confirm old `/api/clients` and `/api/invoices` paths are removed or intentionally return 404/redirect per implementation notes.

## Out of Scope

- App switcher/topbar navigation polish.
- Edit/cancel order behavior.
- Audit log.
- Technician clinic selector for create/edit, unless needed to keep current create working. If discovered necessary, document and adjust Slice 4.

## Implementation Checklist

- [ ] Add actor role enum/model.
- [ ] Extend credential config with role defaulting to clinic.
- [ ] Add technician credential to walking skeleton config.
- [ ] Update auth service to populate actor role.
- [ ] Add centralized auth/authorization helpers or endpoint filters.
- [ ] Move invoicing/client routes under `/api/invoicing`.
- [ ] Apply technician-only authorization to `/api/invoicing` group.
- [ ] Update `index.html` fetch paths to `/api/invoicing/*`.
- [ ] Update scheduler order listing behavior for clinic vs technician.
- [ ] Decide fate of `/api/scheduling/technician/orders` and document it.
- [ ] Add/update tests.
- [ ] Run relevant tests/build.
- [ ] Manually verify clinic vs technician access.
- [ ] Update `master-plan.md` and later slice plans with discoveries.

## Completion Notes

Fill in after implementation.

- Status:
- Files changed:
- Tests run:
- Manual checks:
- Route migration notes:
- Discoveries affecting later slices:
