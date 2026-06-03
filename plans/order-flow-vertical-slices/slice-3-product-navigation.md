# Slice 3 Plan — Product Navigation / App Switcher

*Created: 2026-06-03*

## Goal

Add app-level login UX and Google Workspace-like product switching between Scheduler and Invoicer after roles and invoicing auth are in place.

Rules:

- There is no guest-mode app UX for now: unauthenticated users should be prompted to log in immediately on both `/` and `/orders`.
- Scheduling auth cookie remains the shared app auth mechanism.
- Technician users can see and switch to both Scheduler and Invoicer.
- Clinic users can use Scheduler only and should not see the Invoicer product at all.
- Direct `/` access by a clinic user should redirect to `/orders` or show a simple access-denied shell with a link to `/orders`; prefer redirect if straightforward.
- Product navigation is separate from the settings cog.
- Scheduler remains available at `/orders`.
- Invoicer remains available at `/`.

## Dependencies

This slice depends on Slice 2:

- `GET /api/scheduling/auth/me` must expose enough role info, e.g. `role` or `isTechnician`.
- `/api/invoicing/*` must already be technician-only.
- `Web/wwwroot/index.html` must already use `/api/invoicing/*` paths.

If Slice 2 did not implement page-level protection for `/`, do it here. `/` should not show the full invoicer shell until the user is authenticated as a technician.

## UX Target

### Invoicer topbar

Current topbar area in `Web/wwwroot/index.html` has:

- brand block,
- `.actions`,
- settings cog dropdown.

Add product switcher button to the right of the settings cog or as agreed in current UI review. Product switcher should look like a compact app/menu button.

Suggested entries for technician:

- Scheduler / Orders → `/orders`
- Invoicer → `/`

For clinic/non-technician:

- Do not render Invoicer entry.
- If on scheduler, only Scheduler is visible or switcher can be omitted entirely if it would only contain the current product.
- If a clinic user reaches `/`, redirect to `/orders` or show access denied with a Scheduler link.

### Scheduler topbar

`Web/wwwroot/orders.html` should get compatible topbar styling:

- Spark3Dent logo/brand,
- product title/subtitle,
- authenticated actor pill,
- product switcher,
- logout/settings as appropriate.

No need to fully extract shared CSS yet, but avoid obvious divergence.

## Settings Cog Cleanup

Current `index.html` settings dropdown contains an Orders/Scheduler link. Move product navigation out of settings.

After this slice:

- settings cog should contain settings/admin-like actions only, e.g. licenses/import if appropriate,
- product switcher owns Scheduler/Invoicer navigation.

## App-Level Login and Page Access

Target behavior:

1. Unauthenticated `/orders`: show scheduler login immediately (already largely true after Slices 1-2).
2. Unauthenticated `/`: show a login prompt immediately, not a broken invoicer shell.
3. Technician-authenticated `/`: show functional invoicer UI.
4. Clinic-authenticated `/`: redirect to `/orders` or show access denied with a Scheduler link; prefer redirect if straightforward.
5. Clinic-authenticated `/orders`: show scheduler list/create flow.
6. Technician-authenticated `/orders`: show scheduler list/review flow and product switcher.

Implementation options for `/`:

1. Server-side `/` behavior checks scheduling auth and technician role; unauthenticated gets a lightweight login shell, clinic gets redirected to `/orders`, technician gets `index.html`.
2. Client-side early `auth/me` check in `index.html` hides all invoicer UI, shows login if unauthenticated, and redirects clinic users to `/orders`.

Preferred: server-side if route auth helper from Slice 2 makes it straightforward, otherwise client-side is acceptable for UX. `/api/invoicing/*` security remains server-side and technician-only either way.

## Files Expected to Change

- `Web/wwwroot/index.html`
- `Web/wwwroot/orders.html`
- possibly a small shared login shell or duplicated login UI if extraction would be overkill
- `Web/WebProgram.cs` if implementing server-side page redirect/access behavior
- possible shared CSS/JS file if introduced; remember embedded/static serving behavior

## Tests / Verification

Automated UI tests may not exist. At minimum, run build and manually verify.

Manual unauthenticated checks:

1. Clear auth cookie/session.
2. Open `/orders`; confirm login prompt appears immediately.
3. Open `/`; confirm login prompt appears immediately and the invoicer UI does not attempt to operate as a guest.

Manual technician checks:

1. Login as technician from `/` or `/orders`.
2. Open `/`.
3. Confirm product switcher shows Scheduler and Invoicer.
4. Confirm invoice/client data loads through `/api/invoicing/*`.
5. Navigate to `/orders` through switcher.
6. Confirm scheduler switcher shows both products.
7. Navigate back to `/`.

Manual clinic checks:

1. Login as clinic from `/` or `/orders`.
2. Open `/orders`.
3. Confirm Invoicer is not shown in product switcher.
4. Try direct `/`.
5. Confirm user is redirected to `/orders` or sees non-functional/access-denied shell, not the full invoicer.
6. Confirm `/api/invoicing/*` still returns 403 for clinic.

## Implementation Checklist

- [ ] Ensure `auth/me` response includes role/isTechnician from Slice 2.
- [ ] Add unauthenticated login behavior for `/`.
- [ ] Ensure `/` does not render/operate the full invoicer shell before technician auth.
- [ ] Add product switcher markup/CSS/JS to `index.html`.
- [ ] Remove Scheduler link from settings dropdown in `index.html`.
- [ ] Add compatible product switcher/topbar to `orders.html`.
- [ ] Hide Invoicer entry for non-technicians.
- [ ] Implement direct `/` clinic/non-technician behavior.
- [ ] Verify unauthenticated `/` and `/orders` both prompt login immediately.
- [ ] Test technician navigation both ways.
- [ ] Test clinic cannot see/use invoicer navigation.
- [ ] Run `dotnet build Web/Web.csproj` and relevant tests.
- [ ] Update `master-plan.md` and this plan with completion notes.

## Out of Scope

- Edit/cancel order behavior.
- Audit log.
- Major topbar component extraction.
- Advanced admin/settings pages.

## Completion Notes

Fill in after implementation.

- Status:
- Files changed:
- Tests run:
- Manual checks:
- Discoveries affecting later slices:
