# Orders Technical Hardening & Refactoring Plan

*Created: 2026-05-31*

This plan hardens the orders/scheduling walking skeleton technically while intentionally keeping the business/domain logic naive. The goal is to make the service/repository boundaries strong enough that follow-up business work (holidays, order-code alphabet, tooth UI, capacity) can proceed safely.

## Review Findings

1. **Monolithic Orders files:** `Orders/Domain.cs` and `Orders/Services.cs` contain unrelated enums, domain records, config loading, auth, date availability, PIN hashing, repository interfaces, and order services. This slows focused changes and differs from the clearer file boundaries used elsewhere in the solution.
2. **`IClock` belongs in Utilities:** time abstraction is cross-cutting and should not live in the Orders domain project.
3. **Repository interface is too broad:** `ISchedulingRepository` combines session persistence and order persistence. This makes tests and future DB changes less focused.
4. **Order-code allocation is not atomic enough:** `SchedulingOrderService` checks `OrderCodeExistsAsync` before insert, then inserts later. Concurrent requests can both observe a free code and race at insert time. Unique DB index protects data but currently turns the race into a failed request instead of a retry.
5. **No explicit duplicate-code exception:** persistence cannot communicate “retry with another code” without relying on provider-specific exceptions.
6. **Order creation assembly is too large/nested in service:** validation, audit snapshot, code allocation, and repository calls are all inline.
7. **SQLite order listing sorts in memory:** current EF provider cannot order by `DateTimeOffset`; instead of loading all rows, persist sortable timestamp text or a numeric sort column.
8. **Config provider uses sync file IO and lives with domain models:** acceptable for skeleton, but should be isolated in its own file and made easier to test.
9. **PIN hashing lives with domain model:** should be isolated as auth/security infrastructure in Orders.
10. **Date availability provider refetches non-working-day set repeatedly:** with the future always-fetch holiday source this would be expensive. The availability service should fetch per-year sets once per calculation/range.
11. **Auth service catches all config lookup failures broadly:** should reduce broad catch/nesting and keep invalid-session behavior explicit.
12. **Plan status overstated a few tests:** some checklist items mention tests more detailed than currently implemented; hardening pass should either add focused tests or adjust evidence truthfully.

---

## Phase 1 - Safety Tests First

- [x] Add a focused test that reproduces the order-code race: two concurrent creates receive the same first generated code; repository accepts one and reports duplicate for the other; service must retry and both requests should succeed with unique codes. Confirm it fails before refactor.
- [x] Add repository-level test proving duplicate order code maps to an explicit domain/persistence exception rather than provider-specific exception leakage.
- [x] Add/adjust auth tests for invalid PIN, revoked/expired session, and sliding expiry if missing or too shallow.
- [x] Add date availability test proving provider calls are not repeated per-date for the same year/range.

## Phase 2 - Split Orders Project Files

- [x] Split enums and value records into focused files:
  - `Enums.cs`
  - `ToothRange.cs`
  - `WorkRule.cs`
  - `ClinicConfig.cs`
  - `SchedulingOptions.cs`
  - `AuthSession.cs`
  - `AuthenticatedActor.cs`
  - `OrderDraft.cs`
  - `OrderRecord.cs`
- [x] Move JSON config loading into `JsonSchedulingConfigProvider.cs`.
- [x] Move PIN hashing into `PinHasher.cs`.
- [x] Move date availability types into separate files.
- [x] Move order-code generation into separate files.
- [x] Move auth and order services into separate files.

## Phase 3 - Move Clock to Utilities

- [x] Add `IClock` and `SystemClock` to `Utilities`.
- [x] Update Orders, Web DI, and tests to use `Utilities.IClock`.
- [x] Remove Orders-local clock definitions.

## Phase 4 - Repository Boundary Refactor

- [x] Replace broad `ISchedulingRepository` with `IAuthSessionRepository` and `IOrderRepository` in Orders.
- [x] Split database implementation into focused classes:
  - `SqliteAuthSessionRepo`
  - `SqliteOrderRepo`
  - shared mapping helpers if useful.
- [x] Update Web DI to register focused repositories.
- [x] Keep database entities unchanged unless required by sorting/atomicity tasks.

## Phase 5 - Atomic Order-Code Allocation

- [x] Remove pre-insert `OrderCodeExistsAsync` from service/repo contract.
- [x] Introduce explicit `DuplicateOrderCodeException` in Orders.
- [x] Make `SqliteOrderRepo.CreateOrderAsync` catch unique-constraint failures for order code and throw `DuplicateOrderCodeException`.
- [x] Make `SchedulingOrderService.CreateOrderAsync` retry code generation + insert on `DuplicateOrderCodeException` up to a bounded retry count.
- [x] Keep server-side delivery-date validation immediately before the retry loop.
- [x] Ensure concurrent race test from Phase 1 is green.

## Phase 6 - Query/Date Technical Hardening

- [x] Avoid loading all orders to sort. Add a sortable `CreatedAtUnixTimeMilliseconds` (or equivalent) column/property, migration, mapping, and order-list query using SQL ordering.
- [x] Refactor `DateAvailabilityService` to load non-working days once per relevant year per calculation/range instead of per status/date.
- [x] Keep weekend-only provider behavior unchanged.

## Phase 7 - API/DI Cleanup

- [x] Reduce repeated auth/error boilerplate in `SchedulingApi` where practical without over-abstracting.
- [x] Keep API response shape compatible with the walking skeleton UI/tests.
- [x] Ensure reload endpoint TODO remains explicit for v1.5 removal.

## Phase 8 - Plan, Tests, and QA

- [x] Update this plan with decisions made during implementation.
- [x] Run `dotnet build Spark3Dent.sln --no-restore`.
- [x] Run `dotnet test Spark3Dent.sln`.
- [x] Run a smoke API flow or existing scheduling API tests after refactor.
- [x] Commit the hardening/refactor changes separately from the walking skeleton commit.

---

## Implementation Notes

Completed locally on 2026-05-31. Decisions and evidence:

- Added safety tests first. The new order-code race test failed against the pre-refactor implementation with `Duplicate order code: ABC-234`; the date availability cache test failed with repeated provider calls. Both are now green.
- Split the `Orders` project into focused files for enums/value types/config/auth/date availability/order code/repositories/services.
- Moved `IClock` and `SystemClock` to `Utilities/Clock.cs` and updated Orders/Web/tests to use the shared abstraction.
- Replaced broad `ISchedulingRepository` with `IAuthSessionRepository` and `IOrderRepository`. Database implementations are now `SqliteAuthSessionRepo` and `SqliteOrderRepo`.
- Removed pre-insert order-code existence checking. `SqliteOrderRepo` relies on the unique DB index, maps order-code unique-constraint failures to `DuplicateOrderCodeException`, and `SchedulingOrderService` retries code generation + insert up to a bounded attempt count.
- Added `CreatedAtUnixTimeMilliseconds` to `SchedulingOrderEntity` and migration `20260531094252_AddSchedulingOrderCreatedAtSort` so order listing can sort in SQL instead of loading all rows and sorting in memory.
- Refactored `DateAvailabilityService` to cache non-working-day sets per year within each calculation/range operation. This keeps weekend-only behavior unchanged and prepares for the future always-fetch Bulgarian holiday provider.
- Kept `SchedulingApi` response shape and route behavior stable. API boilerplate was left mostly endpoint-local to avoid over-abstracting the walking-skeleton minimal API.

Validation:

- `dotnet build Spark3Dent.sln --no-restore` passed.
- `dotnet test Spark3Dent.sln` passed. Test totals after hardening: 516 passed, 0 failed.
- Existing scheduling API smoke tests (`Web.Tests/SchedulingApiTests.cs`) still pass after the repository/service refactor.
