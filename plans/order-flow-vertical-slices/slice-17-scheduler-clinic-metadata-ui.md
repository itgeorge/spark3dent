# Slice 17 Plan — Use Clinic Metadata in Scheduler UI

*Created: 2026-06-07*

## Goal

Make the clinic metadata managed through IAM visible and useful in scheduler workflows.

This slice uses clinic organization data such as display color and linked client nickname in the orders list, calendar, review, and lab create target selector.

## Dependencies

Requires IAM read/mutation slices to have stable clinic fields:

- `DisplayColor`,
- `LinkedClientNickname`,
- `DisplayName`,
- active/inactive state.

Can be done after Slice 14 or later. If clinic color editing is not yet implemented, use current read-only/generated color values.

## Product Goals

- Lab users can visually distinguish clinics in list/calendar.
- Clinic users see their own clinic identity but do not need noisy labels on every row.
- Lab create flow clinic selector should show clinic display name, code, linked client nickname, and color.
- Calendar entries use clinic color consistently.
- Deactivated clinics still render historical orders with stored order clinic data; if live metadata is unavailable/inactive, use neutral fallback.

## API/DTO Changes

Order DTOs currently include order-stored clinic fields. Add optional live clinic metadata where useful:

```json
{
  "clinicCode": "DEMO",
  "clinicDisplayName": "Demo Dental Clinic",
  "clinicDisplayColor": "#7c3aed",
  "linkedClientNickname": "demo-client"
}
```

Options:

1. Enrich order DTOs in `SchedulingApi` by looking up clinics for listed orders.
2. Add a clinic metadata map to list/calendar responses.
3. Keep order DTO unchanged and have UI use `/api/scheduling/clinics` for lab-only metadata.

Recommendation: for list/calendar endpoints, return a `clinics` metadata map alongside `items`/`days` to avoid repeated per-order fields and to keep clinic-scoped responses simple. For detail/review, adding optional fields to order DTO is acceptable.

Document the final shape.

## UI Scope

Orders list:

- lab view rows show color chip/swatch for clinic,
- optional linked client nickname in secondary text,
- clinic view can keep simpler layout.

Calendar:

- use clinic display color for order cards/day popup rows,
- ensure contrast/readability,
- use neutral fallback when no color.

Order review:

- show clinic color/client link in metadata area for lab users.

Create/edit:

- lab target clinic selector shows color and linked client nickname.
- inactive clinics should not appear in create target selector.

## Validation/Accessibility

- Ensure color chips have text labels, not color-only meaning.
- Ensure text contrast remains acceptable; use colored border/dot rather than full colored background if needed.
- Missing/invalid color falls back safely.

## Files Expected to Change

Likely:

- `Web/SchedulingApi.cs`
- `Orders/SchedulingOrderService.cs` if service returns clinic metadata maps
- `Orders/Repositories.cs` / identity repo if metadata lookup helper needed
- `Database/SqliteSchedulingIdentityRepo.cs`
- `Web/wwwroot/orders.html`
- `Web.Tests/SchedulingApiTests.cs`
- browser/UI smoke tests if available

## Tests to Add/Update

- lab list/calendar response includes clinic metadata for included orders.
- clinic-scoped list does not leak unrelated clinic metadata.
- inactive clinic historical order behavior is stable/fallback.
- lab target clinic selector includes display color/client nickname.
- UI renders color without failing when null/invalid.

## Manual Verification

1. Create/edit clinic colors in IAM if available.
2. Login as lab.
3. Confirm list rows show clinic color/client metadata.
4. Confirm calendar entries/day popup use clinic color.
5. Confirm lab create clinic selector shows color/client info.
6. Login as clinic and confirm UI remains clean and scoped.

## Out of Scope

- IAM mutation itself.
- Calendar capacity/load calculations.
- Filtering orders by clinic, unless it naturally fits and is explicitly pulled in.
- Color palette management beyond existing display color field.

## Implementation Checklist

- [ ] Choose API DTO shape for clinic metadata.
- [ ] Add metadata enrichment without N+1 query issues.
- [ ] Update list/calendar/review UI.
- [ ] Update lab target clinic selector UI.
- [ ] Add/update tests.
- [ ] Run relevant tests/build.
- [ ] Manual accessibility/readability smoke.
- [ ] Update master plan.

## Completion Notes

Fill in after implementation.

- Status:
- Files changed:
- Tests run:
- Manual checks:
- API metadata shape:
- Discoveries:
