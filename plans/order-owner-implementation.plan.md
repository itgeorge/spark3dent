# Order owner implementation plan

## Context

We are changing scheduling order visibility for clinic users: each clinic member should see and manage only the orders assigned to that member.

A separate prep step will disable order creation and run an in-place reassignment helper so existing orders have correct clinic-member ownership in the existing `SchedulingOrders.CredentialId` / `CredentialLabel` columns before this implementation is deployed.

After that prep, we can treat order `MemberId` / `MemberLabel` as the **clinic member owner** of the order.

Actual actor attribution remains separate and already exists in:

- audit events (`ActorOrganizationType`, `ActorOrganizationCode`, `ActorMemberId`, `ActorMemberLabel`),
- deadline recommendation logs (`CreatedBy...`),
- deadline override logs (`CreatedBy...`).

## Goals

1. Clinic members see only orders assigned to their own member ID.
2. Clinic members can get/find/edit/cancel only their own assigned orders.
3. Clinic calendar shows only the clinic member's assigned orders.
4. LAB members continue to see all orders.
5. LAB order creation requires selecting both:
   - target clinic,
   - target clinic member.
6. LAB-created orders are stored with the selected clinic member in `OrderRecord.MemberId` / `MemberLabel`.
7. Audit/deadline logs for LAB-created orders still record the LAB actor, not the selected clinic member.
8. No DB schema migration is required for this implementation.

## Non-goals

- Do not change order codes, IDs, timestamps, or existing audit/deadline log schemas.
- Do not remove legacy `CredentialPinHashFingerprint` column.
- Do not implement clinic-wide shared inbox or manager roles in this slice.
- Do not add feature flags/config flags for ownership behavior.

## Ownership semantics

`OrderRecord.MemberId` and `OrderRecord.MemberLabel` should be treated as:

> The clinic member owner/assignee for whom this order is visible.

Rules:

- Clinic-created order:
  - owner = authenticated clinic actor.
- LAB-created order:
  - owner = selected active member of selected target clinic.
- LAB actor attribution:
  - remains in audit/deadline logs via authenticated actor.

This distinction should be reflected in naming where reasonable, but a broad domain rename is not required in this slice.

## Backend implementation

### 1. Repository scope support

Current repository methods mostly accept only `clinicCode` scope. Add optional `memberId` scope for read/display methods.

Update `Orders/Repositories.cs`:

```csharp
Task<IReadOnlyList<OrderRecord>> ListOrdersForClinicAsync(string clinicCode, int limit = 100, CancellationToken ct = default);
Task<IReadOnlyList<OrderRecord>> ListOrdersForClinicMemberAsync(string clinicCode, string memberId, int limit = 100, CancellationToken ct = default);
Task<OrderPage> ListOrdersPageAsync(string? clinicCode, string? memberId, int limit, OrderCursor? cursor, CancellationToken ct = default);
Task<OrderPage> ListOrdersPageContainingOrderAsync(string? clinicCode, string? memberId, OrderRecord target, int limit, CancellationToken ct = default);
Task<IReadOnlyList<OrderRecord>> FindOrdersByCodeSuffixAsync(string? clinicCode, string? memberId, string codeSuffix, int limit = 2, CancellationToken ct = default);
Task<IReadOnlyList<OrderRecord>> ListActiveOrdersForCalendarAsync(string? clinicCode, string? memberId, DateOnly start, DateOnly end, CancellationToken ct = default);
```

Alternative: introduce an `OrderVisibilityScope` record to avoid signature churn:

```csharp
public sealed record OrderVisibilityScope(string? ClinicCode, string? MemberId)
{
    public static readonly OrderVisibilityScope All = new(null, null);
}
```

Either approach is acceptable. A scope record is cleaner if the changes stay localized.

Update `Database/SqliteOrderRepo.cs`:

- Filter by `ClinicCode` when provided.
- Filter by `MemberId` when provided.
- Keep existing ordering/cursor behavior.
- Keep `ListActiveOrdersByDeadlineRangeAsync(...)` unscoped because capacity calculations must count all active orders.

Consider adding an index for performance eventually:

- `(ClinicCode, CredentialId)`

There is no need to add it in this slice unless query performance is a concern.

### 2. Service authorization

Update `Orders/SchedulingOrderService.cs`.

Current visibility:

```csharp
actor.IsLab || order.ClinicCode == actor.OrganizationCode
```

New visibility:

```csharp
actor.IsLab ||
(
    string.Equals(order.ClinicCode, actor.OrganizationCode, StringComparison.OrdinalIgnoreCase) &&
    string.Equals(order.MemberId, actor.MemberId, StringComparison.OrdinalIgnoreCase)
)
```

Apply this to:

- `CanActorSeeOrder(...)`,
- `GetAuthorizedOrderAsync(...)`,
- `FindOrderContextForActorAsync(...)`,
- `ListOrdersPageForActorAsync(...)`,
- `ListCalendarOrdersAsync(...)`,
- any other service-level actor-scoped read.

For actor scopes:

- LAB: `clinicCode = null`, `memberId = null`.
- Clinic: `clinicCode = actor.OrganizationCode`, `memberId = actor.MemberId`.

### 3. Date preview authorization

`Web/SchedulingApi.cs` has `ResolveDatePreviewOrderAsync(...)` with duplicated clinic-only authorization.

Change it to use the same ownership check. Prefer exposing a service method like:

```csharp
Task<OrderRecord> GetAuthorizedOrderForActorAsync(AuthenticatedActor actor, string orderCode, CancellationToken ct)
```

or add a service method specifically for date preview. Avoid keeping separate authorization logic in API code.

### 4. Get order endpoint authorization

`GET /api/scheduling/orders/{code}` currently duplicates clinic-only authorization in `Web/SchedulingApi.cs`.

Change to call service authorization rather than direct `GetOrderByCodeAsync(...)` plus API-side checks.

Expected behavior:

- LAB can get any order.
- Clinic member can get only owned order.
- Non-owned order returns 404.

### 5. Create order owner resolution

Current `CreateOrderAsync(...)` takes `targetClinicCode` for LAB-created orders.

Add a target clinic member argument, for example:

```csharp
CreateOrderAsync(
    AuthenticatedActor actor,
    OrderDraft draft,
    string ip,
    string userAgent,
    string? targetClinicCode,
    string? targetClinicMemberId,
    DeadlineOverrideRequest? deadlineOverride,
    CancellationToken ct = default)
```

For clinic actors:

- target clinic must be blank or equal to actor org code.
- target member must be blank or equal to actor member ID.
- owner member = actor member.

For LAB actors:

- target clinic required.
- target clinic member required.
- target clinic must exist and be active.
- target member must exist, be active, and belong to target clinic.
- owner member = selected target clinic member.

Implementation shape:

```csharp
private sealed record ResolvedOrderTarget(
    SchedulingClinic Clinic,
    string OwnerMemberId,
    string OwnerMemberLabel);
```

Replace `ResolveTargetClinicAsync(...)` with `ResolveOrderTargetAsync(...)`.

Then `BuildOrder(...)` should accept `ResolvedOrderTarget` and use:

- `target.Clinic.Code`,
- `target.Clinic.DisplayName`,
- `target.OwnerMemberId`,
- `target.OwnerMemberLabel`.

Audit metadata for `OrderCreated` should include selected owner info for clarity:

```json
{
  "targetClinicCode": "DEMO",
  "targetClinicDisplayName": "Demo Dental Clinic",
  "ownerMemberId": "assistant-1",
  "ownerMemberLabel": "Assistant 1"
}
```

But audit actor fields should remain the authenticated actor.

### 6. API request/response changes

Update `Web/SchedulingApi.cs`.

`CreateOrderRequest` currently has:

```csharp
public string? ClinicCode { get; init; }
```

Add:

```csharp
public string? ClinicMemberId { get; init; }
```

Then pass `body.ClinicMemberId` into service creation.

For order DTOs, current `memberId` / `memberLabel` should continue to be returned. In UI language these will now represent order owner clinic member.

### 7. Clinic member lookup endpoint for LAB UI

Add a scheduling endpoint for LAB users to list active clinic members without exposing PIN fingerprints.

Suggested endpoint:

```http
GET /api/scheduling/clinics/{clinicCode}/members
```

Behavior:

- Requires auth.
- Requires LAB actor.
- Returns 403 for clinic actors.
- Returns 404 if clinic not found/inactive.
- Returns active clinic members only.
- DTO:

```json
{
  "items": [
    { "memberId": "assistant-1", "memberLabel": "Assistant 1" }
  ]
}
```

Do not use IAM member DTO here because IAM currently includes `pinFingerprint`.

Alternative: extend `/api/scheduling/clinics` to include active members. Separate endpoint is preferred to keep clinic list light and avoid leaking member lists unless needed.

## Frontend implementation

Files likely affected:

- `Web/AppPages/orders.html`
- `Web/wwwroot/js/orders-api.js`
- `Web/wwwroot/js/order-flow-view.js`
- optionally `Web/wwwroot/js/order-review-view.js`
- optionally `Web/wwwroot/js/orders-root-view.js`

### 1. Add LAB target clinic member picker

In the existing `#techClinicPicker`, add:

```html
<label for="targetClinicMember">Член на клиниката</label>
<select id="targetClinicMember"></select>
```

Keep this visible only for LAB new-order flow, same as clinic picker.

### 2. API client

Add to `orders-api.js`:

```js
clinicMembers: function(clinicCode) {
  return jsonRequest('/api/scheduling/clinics/' + encodeURIComponent(clinicCode) + '/members', undefined, { items: [] });
}
```

### 3. Flow state

In `order-flow-view.js`:

- Track selected target clinic member.
- When clinic changes:
  - reset member select,
  - fetch active members for selected clinic,
  - populate member choices.
- Validation step 1 for LAB new-order requires both target clinic and target clinic member.
- Draft payload includes:

```js
if(actor()?.isLab && !editMode) {
  d.clinicCode = targetClinic.value;
  d.clinicMemberId = targetClinicMember.value;
}
```

- Dirty snapshot should include selected target member.
- Reset should clear selected target member.

### 4. UX copy

Suggested Bulgarian labels:

- Clinic picker: existing `Клиника получател`.
- Member picker: `Член на клиниката` or `Създаващ член от клиниката`.

Given the data meaning is owner/visibility, prefer clear wording:

- `Поръчката ще бъде видима за` might be better as helper text.

## Tests

### Orders service tests

Update/add in `Orders.Tests/SchedulingOrderServiceTest.cs`:

1. Clinic create assigns owner to authenticated clinic member.
2. LAB create requires target clinic member.
3. LAB create rejects missing/unknown/inactive/wrong-clinic target member.
4. LAB create stores selected clinic member, not LAB member.
5. OrderCreated audit still records LAB actor but metadata includes owner member.
6. Clinic member list/page shows only own orders.
7. Clinic member find by full/short code cannot find another member's order.
8. Clinic member get/update/cancel another member's order returns not found.
9. Clinic calendar shows only own active orders.
10. LAB list/find/get/update/cancel/calendar remains all-order capable.

Test support needs members in `InMemorySchedulingIdentityRepository`, e.g.:

- DEMO / `cred-1`,
- DEMO / `cred-2`,
- OTHER / `cred-other`.

### Repository tests

Update/add in `Database.Tests/SqliteOrderRepoTest.cs`:

1. `ListOrdersPageAsync` respects clinic + member scope.
2. `FindOrdersByCodeSuffixAsync` respects clinic + member scope.
3. `ListActiveOrdersForCalendarAsync` respects clinic + member scope.
4. All-order LAB scope still returns all.
5. Capacity method `ListActiveOrdersByDeadlineRangeAsync` remains unscoped.

### API tests

Update/add in `Web.Tests/SchedulingApiTests.cs`:

1. LAB create without `clinicMemberId` returns 400.
2. LAB create with `clinicCode` + `clinicMemberId` succeeds and response order has selected member.
3. Clinic member cannot see another member's order in list/calendar/find/get.
4. Clinic member cannot update/cancel another member's order.
5. LAB can list active members for a clinic.
6. Clinic actor cannot call LAB member-list endpoint.
7. Member-list endpoint does not include PIN fingerprint or hash-like fields.

Test fixture should seed at least two DEMO clinic members and one OTHER clinic member.

## Compatibility assumptions

Before deploying this implementation:

- order creation has been disabled,
- reassignment script has run,
- existing active/relevant orders have correct clinic member ownership in `CredentialId` / `CredentialLabel`,
- deployment then re-enables creation with required owner support.

If any historical order remains assigned to a non-clinic member, it will become invisible to clinic users but still visible to LAB users.

## Rollout acceptance checks

After Deployment 2:

- LAB can create an order only after selecting clinic and clinic member.
- Created order displays selected clinic member in `memberId` / `memberLabel`.
- A clinic member sees their assigned order in list/calendar.
- A different clinic member in same clinic does not see it and receives 404 by direct code.
- LAB still sees the order.
- Audit event for LAB-created order has actor member = LAB member and metadata owner member = selected clinic member.
- Deadline capacity calculation still considers all active orders regardless of owner.

## Suggested implementation order

1. Repository scope changes + repository tests.
2. Service authorization/creation owner resolution + service tests.
3. API request/endpoint changes + API tests.
4. Frontend LAB member picker and payload changes.
5. Manual smoke test.
6. Remove temporary order creation disable from prep deployment.

## Suggested validation commands

```bash
dotnet build Spark3Dent.sln --no-restore
dotnet test Spark3Dent.sln --no-build
```

Manual smoke flow:

1. Login as LAB.
2. Open new order flow.
3. Select target clinic.
4. Select clinic member.
5. Create order.
6. Login as selected clinic member; verify order visible.
7. Login as another member of same clinic; verify order not visible and direct code 404.
8. Login as LAB; verify order visible.
