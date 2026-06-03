# Slice 3 Plan — Product Navigation / App Switcher

*Created: 2026-06-03*

## Goal

Add Google Workspace-like product switching between Scheduler and Invoicer after roles and invoicing auth are in place.

Rules:

- Technician users can see and switch to both Scheduler and Invoicer.
- Non-technician users should not see the Invoicer product at all.
- Product navigation is separate from the settings cog.
- Scheduler remains available at `/orders`.
- Invoicer remains available at `/`.

## Dependencies

This slice depends on Slice 2:

- `GET /api/scheduling/auth/me` must expose enough role info, e.g. `role` or `isTechnician`.
- `/api/invoicing/*` must already be technician-only.
- `Web/wwwroot/index.html` must already use `/api/invoicing/*` paths.

If Slice 2 did not implement page-level protection for `/`, do it here or at least ensure non-technicians cannot reach functional invoicer UI.

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

For non-technician:

- Do not render Invoicer entry.
- If on scheduler, only Scheduler is visible or switcher can be omitted entirely if it would only contain the current product.

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

## Page Access for Non-Technicians

Implement one of these; preferred target is redirect or access shell, not broken invoice page:

1. Server-side `/` behavior checks auth and technician role, redirect non-technician to `/orders`.
2. Client-side early `auth/me` check hides all invoicer UI and redirects non-technician to `/orders`.
3. Access-denied page with link to `/orders`.

Preferred: server-side redirect if route auth helper from Slice 2 makes it straightforward. If not, client-side redirect is acceptable for UX, with `/api/invoicing/*` security already enforced server-side.

## Files Expected to Change

- `Web/wwwroot/index.html`
- `Web/wwwroot/orders.html`
- `Web/WebProgram.cs` if implementing server-side page redirect/access behavior
- possible shared CSS/JS file if introduced; remember embedded/static serving behavior

## Tests / Verification

Automated UI tests may not exist. At minimum, run build and manually verify.

Manual technician checks:

1. Login as technician.
2. Open `/`.
3. Confirm product switcher shows Scheduler and Invoicer.
4. Navigate to `/orders` through switcher.
5. Confirm scheduler switcher shows both products.
6. Navigate back to `/`.

Manual clinic checks:

1. Login as clinic.
2. Open `/orders`.
3. Confirm Invoicer is not shown in product switcher.
4. Try direct `/`.
5. Confirm user is redirected to `/orders` or sees non-functional/access-denied shell, not the full invoicer.
6. Confirm `/api/invoicing/*` still returns 403 for clinic.

## Implementation Checklist

- [ ] Ensure `auth/me` response includes role/isTechnician from Slice 2.
- [ ] Add product switcher markup/CSS/JS to `index.html`.
- [ ] Remove Scheduler link from settings dropdown in `index.html`.
- [ ] Add compatible product switcher/topbar to `orders.html`.
- [ ] Hide Invoicer entry for non-technicians.
- [ ] Implement direct `/` non-technician behavior.
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
