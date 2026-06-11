---
name: slice-5-follow-up-cleanup
overview: Collect follow-up cleanup tasks discovered during component refactor reviews, to be handled after the main extraction slices or opportunistically when safe.
todos:
  - id: consolidate-escape-handling
    content: Remove or simplify the legacy centralized Orders Escape-key handler once all modals are fully migrated to S3DModal/modal stack handling.
    status: pending
isProject: false
---

# Slice 5: Follow-up Cleanup

## Purpose

This plan collects cleanup items discovered during reviews of the component-refactor slices. Add to this file as more non-blocking cleanup opportunities are found.

These tasks should generally be handled after the main refactor slices are stable, unless a cleanup becomes necessary to unblock a slice.

## Cleanup Items

### 1. Consolidate Orders Escape-key handling

During review of Slices 2 and 3, `Web/wwwroot/orders.html` still had the old centralized Escape-key handler alongside the newer `S3DModal` Escape/modal-stack handling.

Current state is not blocking because `S3DModal` stops propagation for top modals, but the duplication should be cleaned up later to reduce maintenance risk.

Target outcome:

- Bound modals rely on `S3DModal` for Escape handling.
- The legacy document-level Escape handler is removed or reduced to only page-level non-modal behavior, e.g. closing the review screen with Escape if that behavior is still desired.
- Escape priority remains correct for nested/stacked modals.
- Modal stack remains consistent: bound modals are opened/closed only via modal helpers, not direct `classList` mutations.

Validation:

- Find order popup closes with Escape.
- Cancel order confirm closes with Escape.
- Before-minimum confirm closes with Escape.
- Discard changes confirm closes with Escape.
- Orders day popup closes with Escape.
- Escape on review screen still performs the intended action, if retained.
- Orders replay tests pass.

## Standard Validation

Run:

```bash
dotnet test Web.Tests/Web.Tests.csproj --no-restore
dotnet test Web.Tests/Web.Tests.csproj --filter Category=OrdersReplay --no-restore
```
