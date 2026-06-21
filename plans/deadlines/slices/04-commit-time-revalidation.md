# Slice 4 - Commit-Time Capacity Revalidation

## Goal

Make order create/update saves concurrency-safe for deadline capacity.

After this slice, capacity validation and order persistence should happen inside the same serialized write operation so two simultaneous saves cannot both overbook a daily or weekly capacity bucket.

The key user-facing behavior is:

- users may preview availability based on current data;
- on save, the backend revalidates against the latest committed orders;
- if capacity has been consumed since preview, the save is rejected with a clear error;
- concurrent creates/updates for the last available capacity slot result in only one successful save.

Recommendation logs, override logs, and full lab-technician capacity override remain out of scope.

## Requirements Covered

This slice implements `plans/deadlines/v1-requirements.md` Section 12:

- backend revalidates capacity when the deadline is committed/saved;
- stale UI availability is not enough to allow overbooking;
- if the selected date is no longer valid, save fails clearly;
- lab-technician override remains deferred to a later slice.

It also protects the Slice 3 behavior from races:

- Section 4.6 daily and weekly capacity checks remain required on save;
- Section 4.7 normal users/customers cannot overbook;
- Section 4.8 active non-cancelled orders consume capacity;
- Section 4.9 update/reschedule counts only the current selected deadline and excludes the current order during validation.

Explicitly out of scope for this slice:

- manual override flow for technicians;
- override reason capture/logging;
- recommendation logs/candidate audit trails;
- admin UI/API for editing capacity config;
- distributed locking beyond the app's SQLite database write-lock model.

## Current State / Context

After Slice 3:

- `Orders/DeadlineRecommendationService.cs`
  - Calculates capacity-aware recommendations/statuses.
  - Validates a requested date against lead-time/calendar/daily/weekly capacity.
  - Reads existing active orders through `IOrderRepository.ListActiveOrdersByDeadlineRangeAsync`.
- `Orders/SchedulingOrderService.cs`
  - Validates requested date immediately before create/update.
  - Saves `CalculatedCapacityUnits` on create/update.
  - Validation and persistence are still separate repository operations and therefore not atomic.
- `Database/SqliteOrderRepo.cs`
  - Each repository method currently opens its own `AppDbContext` and saves independently.
- `Database/SqliteImmediateTransaction.cs`
  - Already exists and can run operations inside a SQLite `BEGIN IMMEDIATE` transaction.
  - This should be used or adapted so validation reads and order writes are protected by the same SQLite write lock.

The race to close:

```text
Daily capacity = 1
Order A and Order B both validate Friday as available
Order A saves Friday
Order B saves Friday based on stale validation
Friday becomes overbooked
```

After this slice, Order B must be rejected unless a later explicit override flow is used.

## Desired End State

### Serialized save operation

Create/update should run the capacity-relevant part of the mutation inside a serialized write operation:

```text
BEGIN IMMEDIATE / acquire write lock
  re-read current order if updating
  re-run deadline validation using the same transaction's view of active orders
  calculate/save capacity units
  insert/update order
COMMIT
```

For create:

1. Validate draft shape outside or inside the transaction.
2. Resolve target clinic outside or inside the transaction.
3. Inside serialized write operation:
   - re-run full requested-date validation against latest orders;
   - build order with calculated capacity;
   - generate/order-code retry as needed;
   - insert order.
4. Append audit after successful persistence, as today.

For update:

1. Inside serialized write operation:
   - re-fetch the order by code;
   - re-check actor authorization and cancelled status;
   - re-run full requested-date validation using the re-fetched order's `CreatedAt` and excluding its `Id`;
   - update order fields and recalculated capacity.
2. Append audit after successful persistence, as today.

This avoids stale update state as well as stale capacity state.

### Abstraction guidance

Keep `Orders` independent of `Database`.

A recommended approach is to add a small transaction abstraction in `Orders`, for example:

```csharp
public interface ISchedulingWriteTransaction
{
    Task<T> ExecuteAsync<T>(Func<IOrderRepository, Task<T>> operation, CancellationToken ct = default);
}
```

Then implement it in `Database` using `SqliteImmediateTransaction` and a transaction-scoped order repository/context.

Implementation details are flexible, but the important property is:

- the `IOrderRepository` used by `DeadlineRecommendationService` during validation must read through the same transaction/context/connection that will persist the order;
- the write lock must be acquired before validation starts and held until the order insert/update is saved.

Possible implementation patterns:

1. Transaction runner creates one `AppDbContext`, begins `SqliteImmediateTransaction`, and passes a context-bound `SqliteOrderRepo` into the operation.
2. `SqliteOrderRepo` gains an internal context-bound mode that does not dispose the shared transaction context per method.
3. `SchedulingOrderService` creates or receives a `DeadlineRecommendationService` for the transaction-scoped repository, or the deadline service is refactored to accept an order repository/capacity source parameter for validation calls.

Avoid a solution that merely wraps `_orders.CreateOrderAsync` or `_orders.UpdateOrderAsync` in a transaction after validation has already happened; that would not fix the race.

### In-memory/test behavior

Test fakes should also serialize the validate+save operation, usually with a lock/semaphore around `ExecuteAsync`, so unit tests can exercise the same service flow.

### Error behavior

When stale capacity is detected at commit time:

- return the existing style of `400` API response;
- include the capacity reason, e.g. `Daily capacity exceeded` or `Weekly capacity exceeded`;
- do not persist the losing order/update;
- do not append order-created/order-updated audit for rejected saves.

### Existing public API compatibility

No endpoint shape changes are required.

Existing endpoints should continue to be used:

- `POST /api/scheduling/dates` for preview;
- `POST /api/scheduling/orders` for create;
- `PUT /api/scheduling/orders/{code}` for update.

The change is backend save semantics, not UI shape.

## Implementation Plan

### 1. Add write transaction abstraction

- Add an interface in `Orders`, e.g. `ISchedulingWriteTransaction`.
- Add a no-op or lock-based in-memory implementation in test support.
- Decide whether the transaction abstraction passes a transaction-scoped `IOrderRepository` or a narrower capacity/order mutation context.
- Keep naming explicit that this is for scheduling write serialization, not generic business transactions.

### 2. Implement SQLite transaction runner

- Add a `Database` implementation that uses `SqliteImmediateTransaction` or equivalent `BEGIN IMMEDIATE` behavior.
- Ensure the transaction starts before capacity validation reads.
- Ensure the same database connection/transaction is used for:
  - active order capacity reads;
  - current-order re-fetch on update;
  - create/update persistence.
- Register the implementation in `Web/WebProgram.cs`.

### 3. Refactor deadline validation to work with transaction-scoped reads

Current `DeadlineRecommendationService` captures `IOrderRepository` in its constructor. Adjust one of these ways:

- allow validation methods to accept an `IOrderRepository`/capacity source override;
- create a transaction-scoped `DeadlineRecommendationService` inside the transaction;
- extract capacity usage reads behind an interface that can be transaction-scoped.

Do not lose existing tests for lead-time/calendar/capacity behavior.

### 4. Refactor create flow

In `SchedulingOrderService.CreateOrderAsync`:

- keep draft validation and target-clinic resolution behavior equivalent;
- move full requested-date validation and order insert into the serialized write transaction;
- calculate and persist `CalculatedCapacityUnits` from the transaction-time validation result;
- preserve order-code retry behavior;
- append audit only after successful transaction commit.

### 5. Refactor update flow

In `SchedulingOrderService.UpdateOrderAsync`:

- move current-order re-fetch, authorization check, cancelled check, validation, and update into the serialized write transaction;
- use the transaction-fetched order's `CreatedAt` and `Id` for validation/exclusion;
- preserve changed-fields audit metadata by comparing the transaction-fetched original to the saved updated order;
- append audit only after successful transaction commit.

### 6. Preserve cancellation/list behavior

This slice does not need to serialize `CancelOrderAsync`, but consider whether cancellation should use the same write transaction for consistency. It is acceptable to leave cancel as-is if tests and behavior remain correct.

The important mutation paths for this slice are create/update deadline commits.

## TDD Plan

Use TDD where practical.

### Orders unit tests

1. Create flow revalidates inside transaction:
   - arrange capacity daily limit = 1;
   - transaction fake injects/commits a competing active order after pre-preview but before create validation if needed;
   - assert create rejects with `Daily capacity exceeded`.

2. Concurrent create serialization:
   - two concurrent `CreateOrderAsync` calls target the same last-capacity date;
   - fake transaction serializes operations;
   - assert exactly one succeeds and one fails.

3. Update flow re-fetches inside transaction:
   - load an order for editing;
   - mutate repository state before update transaction to fill target date;
   - assert update rejects based on latest capacity.

4. Rejected create/update does not append mutation audit:
   - capacity validation fails inside transaction;
   - assert no `OrderCreated`/`OrderUpdated` audit event was appended.

### Database tests

1. SQLite write transaction serializes operations:
   - run two concurrent transaction operations with a controlled delay;
   - assert the second does not enter the critical section until the first commits, or assert final capacity-safe behavior through repository/service.

2. Transaction-scoped order repository sees writes consistently:
   - inside one transaction, read active orders, insert/update order, and read again if needed;
   - assert operations use the same DB state and commit successfully.

3. Rollback behavior:
   - throw after a write inside transaction;
   - assert write is rolled back.

### Web/API tests

1. Concurrent create cannot overbook daily capacity:
   - set daily capacity to 1 and weekly capacity high;
   - send two concurrent `POST /api/scheduling/orders` requests for the same valid date;
   - assert exactly one response is `201` and one response is `400` containing capacity error;
   - assert the database contains only one active order for that date.

2. Concurrent create cannot overbook weekly capacity:
   - set weekly capacity to 1 and daily capacity high;
   - send two concurrent creates in the same Monday-Sunday week;
   - assert exactly one succeeds.

3. Stale preview save is rejected:
   - call `/api/scheduling/dates` and observe a date selectable;
   - create a different order consuming the remaining capacity;
   - attempt to create using the stale date;
   - assert `400` capacity error and no extra order persisted.

4. Concurrent update cannot overbook:
   - create two existing orders;
   - configure only one target slot available;
   - concurrently update both to the same target date;
   - assert at most one update succeeds.

Prefer deterministic tests using barriers/delays in test doubles where possible. API concurrency tests are valuable but should avoid timing flakiness.

## Validation Plan

### Automated validation

Run:

```bash
dotnet test Spark3Dent.sln --no-restore --verbosity quiet
```

Expected: all tests pass, including database and web/API concurrency tests.

### Requirements cross-check

At the end of implementation, explicitly check this slice against `plans/deadlines/v1-requirements.md`:

- [ ] Section 12 backend revalidates selected deadline at commit/save time.
- [ ] Section 12 stale UI availability cannot cause overbooking.
- [ ] Section 12 if selected date is no longer valid, save fails clearly.
- [ ] Create validation and create persistence are protected by the same serialized write operation.
- [ ] Update current-order re-fetch, validation, and update persistence are protected by the same serialized write operation.
- [ ] Daily capacity races are covered by automated or manual validation.
- [ ] Weekly capacity races are covered by automated or manual validation.
- [ ] Rejected commit-time validation does not persist an order/update.
- [ ] Rejected commit-time validation does not append create/update audit.
- [ ] Slice 1/2/3 lead-time, DB config, and capacity behavior still passes.
- [ ] Lab-technician override remains intentionally unimplemented until a later slice.
- [ ] Recommendation and override logs remain intentionally unimplemented until later slices.

### Manual end-to-end validation

Perform at least one manual/API-level check proving stale/concurrent saves are rejected.

Suggested API-level validation:

1. Start with a migrated local/test database.
2. Set capacity config so a known selectable date has `DailyCapacityUnits = 1.0` and high weekly capacity.
3. Log in as a clinic user.
4. Call `/api/scheduling/dates` for an order and confirm the target date is selectable.
5. Send two near-simultaneous `POST /api/scheduling/orders` requests for equivalent orders on that same target date.
6. Confirm exactly one succeeds with `201`.
7. Confirm the other fails with `400` and a capacity reason.
8. Query/list orders or inspect the DB and confirm there is only one active order on that target date.

If true concurrent manual requests are inconvenient, perform stale-preview validation:

1. Preview target date as selectable.
2. Create one order on that date.
3. Attempt to save another order on the same date using the stale selection.
4. Confirm the second save is rejected and no second active order exists for that date.

## Review Notes for Implementing Agent

When handing this slice back for review, include:

- files changed;
- transaction abstraction and implementation names;
- explanation of how validation reads and writes share the same serialized write operation;
- how create order-code retry behaves inside the transaction;
- how update re-fetch/authorization/cancelled checks are handled;
- tests added/updated;
- full automated test command and result;
- manual/API end-to-end validation performed and result;
- completed requirements cross-check checklist;
- any deviations from this plan.

## Known Follow-ups

Deferred to later slices:

- recommendation logs and candidate-date audit trail persistence;
- config snapshots in recommendation logs;
- full lab override warning/reason/rules-bypassed flow;
- `DeadlineOverrideLog` persistence;
- admin UI/API for editing scheduling config, if needed;
- richer UI display of capacity utilization and over-capacity warnings.
