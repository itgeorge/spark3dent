# Slice 2.5 Plan — Read-Only Order Review

*Created/Completed: 2026-06-03*

## Goal

Add a read-only review screen for existing orders from the scheduler list, reusing the stepper confirmation/review visual language.

## Scope

- In `Web/wwwroot/orders.html`, allow users to open an existing order from the list.
- Fetch details with `GET /api/scheduling/orders/{code}`.
- Show read-only order details with:
  - shortened/full order code,
  - status,
  - case name,
  - notes,
  - requested delivery date,
  - clinic/credential metadata,
  - product/work/material/construction/teeth/shade fields,
  - selected teeth preview where available.
- Present review as a modal over a blurred backdrop.
- Provide a single Back to orders control at the top.
- Close on Escape or backdrop click.
- Leave edit/cancel implementation to Slice 4.

## Implementation Checklist

- [x] Add list row/View button behavior.
- [x] Add read-only review card/view.
- [x] Add order detail fetch using existing `GET /api/scheduling/orders/{code}`.
- [x] Add a modal/backdrop presentation.
- [x] Add a single top Back to orders control.
- [x] Add Escape and backdrop-click close behavior.
- [x] Update Slice 4 plan so Edit/Cancel buttons are added to this read-only review UI header.
- [x] Run build/tests/browser smoke.
- [x] Update master plan.

## Completion Notes

- Status: Complete
- Files changed: `Web/wwwroot/orders.html`, `plans/order-flow-vertical-slices/master-plan.md`, `plans/order-flow-vertical-slices/slice-4-edit-cancel.md`, this plan.
- Tests run: `dotnet build Web/Web.csproj`; `dotnet test Web.Tests/Web.Tests.csproj`.
- Manual checks: Headless Chromium smoke passed: create clinic order, return to list, click View, read-only review opens, order code/case/note are present, Back returns to list. Follow-up cleanup removed the extra metadata grid and bottom Back button for a simpler step-5-style layout, then converted the review to a modal with blurred backdrop plus Escape/backdrop close behavior.
- Discoveries affecting later slices: Slice 4 should attach Edit/Cancel actions to the read-only review header next to the single Back to orders button.
