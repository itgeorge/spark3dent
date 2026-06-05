# Orders/Scheduling Walking Skeleton Plan

*Created: 2026-05-31*

This plan covers the first end-to-end orders/scheduling walking skeleton for Spark3Dent. The goal is to validate the real clinic-facing flow while the feature is still used only by the dental technician / current organization for product-development feedback.

For the follow-on integration of the stepper prototype into the normal app flow, use the vertical-slice handoff plan at `plans/order-flow-vertical-slices/master-plan.md`. That master plan tracks the per-slice implementation plans, handoff protocol, route/auth decisions, and post-slice course corrections.

## How to Use This Plan

- Complete tasks in order unless a task explicitly says it can be done independently.
- When completing a task, change `[ ]` to `[x]` in this file and append short evidence, e.g. `(tests: Scheduling.Tests.DateAvailabilityTests)`.
- Server-side work should be TDD-gated where practical:
  1. add/adjust tests first,
  2. run tests and confirm RED,
  3. implement minimal code,
  4. rerun tests and confirm GREEN.
- Scope changes discovered during implementation should be added as new unchecked tasks rather than being lost.
- Follow-up topics that are not implemented in this walking skeleton must be carried forward into the next detailed plan as explicit TODOs.

---

## Product Assumptions and Decisions

1. **v1 meaning:** v1 is production-like but initially available only to the dental technician/current organization. The technician will go through the real clinic flow to expose UX and process issues before real clinics are onboarded.
2. **v2 meaning:** v2 is the later external clinic release. Capacity, stronger review workflows, privacy/cookie page, and more polished credential management are v2/v1.5 concerns unless explicitly pulled forward.
3. **No invoicing integration in this plan.** Orders are independent from invoices for the walking skeleton.
4. **No capacity scheduling in this plan.** Date availability uses lead time + closed days only. Weighted workload/capacity is a follow-up.
5. **Clinic access:** clinic code + PIN. A clinic can have multiple active PIN credentials so actions can be attributed to a specific credential/employee-like code.
6. **Clinic vs invoicing client:** scheduling clinics are independent from invoice clients. A clinic may optionally link to an existing invoicing `Client`, but new clinics without invoice setup must be supported.
7. **PIN storage:** store hashed PINs only. Do not store raw PINs in production config. Add a small CLI/helper for generating hashes and adding credentials.
8. **Auth sessions:** use opaque server-side sessions in an auth cookie. Cookie contains only a random token; DB stores token hash and session metadata.
9. **Session expiry:** use sliding expiration and an optional absolute expiration field. Access near the end of the sliding window refreshes the sliding expiry.
10. **Cookie/privacy:** auth-only cookies are considered necessary cookies. No banner for the walking skeleton. A dedicated privacy/cookie page is deferred to v1.5/v2.
11. **Technician impersonation for v1:** the technician should select clinic code + PIN and use the same flow a clinic will later use. Avoid a separate shortcut flow for order creation in this plan.
12. **Order status:** only `Created` is required for the walking skeleton.
13. **Case reference:** use `CaseName`, not `PatientName`.
14. **Teeth selection:** the initial walking skeleton supported numeric tooth/range input; prototyping evolved this into a visual FDI tooth picker that was later promoted to `/orders` and the obsolete prototype file was removed in Slice 8. Crown = single tooth. Non-crown = contiguous same-jaw range. Abutment support was later removed from the live order flow.
15. **Delivery date selection:** user may select any valid date after/on the calculated minimum date, not only the earliest date.
16. **Delivery availability plain-English rules:** a delivery date is selectable when it is on/after the minimum calculated date, not weekend, not a closed/holiday day, and not the first business day after a closed period.
17. **Walking-skeleton date filtering:** implement at least a weekend-only filter in this plan so filtering is demonstrably active. Bulgarian holiday automation is a follow-up unless pulled into this plan later.
18. **Configuration:** clinics, credentials, and work rules are loaded from uploaded/server JSON for the walking skeleton. Config reload requires app restart; the temporary reload endpoint was removed after prototyping.
19. **Config reload endpoint:** removed before preview hardening; do not reintroduce without an explicit admin/security design.
20. **Order code:** generation is hidden behind an interface. The current implementation uses descriptive order-code stems based on delivery date/material/tooth count plus a short suffix; final Bulgarian/Latin ambiguity research and normalization remain follow-ups.

---

## Phase 0 - Baseline and Project Placement

- [x] Inspect current solution boundaries and decide whether scheduling domain code belongs in a new `Orders`/`Scheduling` project or existing projects.
- [x] Add chosen project(s) and test project(s) to `Spark3Dent.sln` if new projects are needed.
- [x] Add project references so Web can use the scheduling application services without leaking EF/database details into UI code.
- [x] Document the chosen boundary in this plan before implementing persistence.

---

## Phase 1 - Scheduling Domain Model

- [x] Define core order enums/value types:
  - product category: temporary/permanent or initial equivalent,
  - work type,
  - material,
  - construction type,
  - order status with only `Created` required initially.
- [x] Define `Clinic` domain model independent from invoicing `Client`, with optional invoice-client link.
- [x] Define `ClinicCredential` model with label, active/revoked state, and hashed PIN fields.
- [x] Define `Order` model with at least:
  - order code,
  - clinic id,
  - credential id,
  - case name,
  - impression/collection date,
  - product/work/material/construction selections,
  - teeth range,
  - historical derived/default abutment teeth for bridges (later removed in Slice 8),
  - requested delivery date,
  - status `Created`,
  - created/updated timestamps,
  - IP/user-agent audit metadata.
- [x] Add validation tests for crown single-tooth and non-crown range behavior.
- [x] Add validation tests for bridge default abutments = range endpoints. (Historical; abutment logic was removed in Slice 8.)

---

## Phase 2 - JSON Configuration for Walking Skeleton

- [x] Define JSON config schema for clinics and credentials using hashed PINs only.
- [x] Define JSON config schema for allowed work combinations and minimum business-day lead times.
- [x] Add default work-rule values suitable for walking-skeleton testing.
- [x] Implement startup-only loading from a configured file path.
- [x] Add validation errors for malformed config, duplicate clinic codes, duplicate credential labels within a clinic, inactive/missing credentials, and unsupported work-rule combinations.
- [x] Initially added a temporary authenticated/dev-only API endpoint to force config reload without restarting the app.
- [x] Removed the temporary config reload endpoint after prototyping; config changes now require restart unless a future admin design reintroduces reload safely.

---

## Phase 3 - PIN Hash CLI/Helper

- [x] Choose PIN hashing approach appropriate for low-entropy six-digit PINs, including salt and server-side pepper/config secret if practical.
- [x] Add tests for PIN verification success/failure and inactive credential rejection.
- [x] Add a small CLI/helper command to generate a credential hash from a PIN and label, suitable for pasting into the JSON config.
- [x] Ensure CLI/helper never logs raw PINs except unavoidable local terminal input/output during generation.
- [x] Document example JSON credential entry with placeholder hash only.

---

## Phase 4 - Server-Side Cookie Session Auth

- [x] Add persistence model for auth sessions:
  - id,
  - clinic id,
  - credential id,
  - token hash,
  - created at,
  - last seen at,
  - sliding expires at,
  - optional absolute expires at,
  - revoked at,
  - created IP/user-agent.
- [x] Implement login endpoint accepting clinic code + PIN.
- [x] On successful login, create random opaque token, store only token hash, and set HttpOnly/Secure/SameSite auth cookie.
- [x] Implement authenticated request resolver that checks token hash, expiry, revocation, active clinic, and active credential.
- [x] Implement sliding expiry refresh: a valid request extends expiry by the configured session lifetime.
- [x] Implement logout current session by revoking the session and clearing the cookie.
- [x] Add repository/service methods to revoke all sessions for a credential and all sessions for a clinic, even if no UI calls them yet.
- [x] Add auth tests for invalid PIN, revoked credential, expired session, revoked session, and sliding-expiry refresh.

---

## Phase 5 - Persistence

- [x] Add EF entities/mappings for clinics, clinic credentials if persisted, auth sessions, and orders.
- [x] Decide which clinic/credential data is persisted versus loaded from JSON for the walking skeleton. If credentials remain JSON-only, persist only enough references/snapshots to audit created orders.
- [x] Add unique index on order code.
- [x] Add indexes for order list queries by clinic, delivery date, created date, and status.
- [x] Implement order repository/service create flow.
- [x] Ensure order creation revalidates delivery date server-side immediately before saving.
- [x] Add persistence tests for create/list/get order and duplicate order-code handling.

---

## Phase 6 - Date Availability Walking Skeleton

- [x] Define date availability interfaces, e.g. `INonWorkingDayProvider` / `IHolidayCalendarProvider`, so the holiday source can be replaced later.
- [x] Implement initial weekend-only non-working-day provider or equivalent minimal stub.
- [x] Implement lead-time calculation using minimum business days from config.
- [x] Implement delivery-date validation:
  - on/after minimum date,
  - not weekend/closed day according to provider,
  - not first business day after a closed period.
- [x] Add tests for normal weekend behavior: Saturday/Sunday unavailable, Monday first-after-weekend unavailable for delivery, Tuesday available if lead time allows.
- [x] Add tests for user may select any valid date on/after the calculated minimum date.
- [x] Add API endpoint to return available/disabled dates or validation metadata for a visible calendar range.

---

## Phase 7 - Order Code Interface and Best-Guess Implementation

- [x] Define `IOrderCodeGenerator` and, if needed, `IOrderCodeNormalizer`.
- [x] Implement initial generator; evolved during prototyping into descriptive order codes with delivery date/material/tooth-count stem and short letter suffix.
- [x] Avoid obviously ambiguous characters such as `0/O`, `1/I/L`, and known problematic Bulgarian/Latin examples where practical.
- [x] Ensure generated codes are checked for uniqueness before order save.
- [x] Add tests for format, uniqueness retry behavior, and basic normalization if implemented.

---

## Phase 8 - Backend Order API

- [x] Add authenticated endpoint to get current clinic/session info.
- [x] Add endpoint to get scheduling config needed by the order form: allowed work/material/construction combinations and lead-time metadata.
- [x] Add endpoint to validate/calculate delivery dates for selected order details.
- [x] Add endpoint to create an order.
- [x] Add endpoint to fetch order confirmation by order code for the authenticated clinic/session.
- [x] Add simple technician/internal endpoint or page data endpoint to list all created orders for review.
- [x] Ensure API error shape is consistent with existing Web style (`{ error: ... }`).
- [x] Add API tests for unauthenticated access, valid login, order creation, invalid delivery date rejection, and confirmation retrieval.

---

## Phase 9 - Walking Skeleton Web UI

- [x] Add simple clinic login page/form: clinic code + PIN.
- [x] Add order creation form:
  - case name,
  - impression/collection date with Today shortcut,
  - product/work/material/construction selection,
  - tooth/range input,
  - shade as a first-class order field, including `Unspecified`,
  - delivery date selection with disabled dates/validation messages,
  - submit.
- [x] Show clear UI explanation when a date is disabled because it is weekend/closed or first business day after closure.
- [x] Show order confirmation with large order code and instruction to write it on the impression/package.
- [x] Add simple order list view for the technician/current organization.
- [x] Add logout UI.
- [x] Prototype a visual FDI tooth chart and stepper UX; this was later promoted to the real `/orders` UI and the obsolete prototype file was removed in Slice 8.

---

## Phase 10 - Audit Logging

- [x] Record order-created audit data: clinic id, credential id, timestamp, IP, user agent.
- [x] Add general audit model/service if needed for future order updates/cancellations.
- [x] Add tests or repository assertions proving created orders preserve credential attribution.
- [x] Ensure no raw PIN or session token is logged.

---

## Phase 11 - Manual Validation and Deployment Notes

- [x] Add sample non-secret scheduling JSON config for local development. (`Web/scheduling.walking-skeleton.json`)
- [x] Add deployment note for uploading scheduling JSON to the Hetzner server. (config path supported via `App:SchedulingConfigPath`; detailed Hetzner doc follow-up remains)
- [x] Add deployment note for restarting the app after config changes. The temporary reload endpoint has been removed; restart is required for JSON config changes.
- [x] Manually validate full flow on local app: (API smoke + headless-browser screenshots under `agent-qa/orders-skeleton/`)
  - generate PIN hash,
  - add clinic credential config,
  - login,
  - create order,
  - see confirmation code,
  - see order in technician list,
  - logout.
- [-] Manually validate full flow on Hetzner/staging-like deployment before marking walking skeleton complete. (Skipped for now by decision; revisit during deployment/pre-release work.)

---

## Phase 12 - Agent QA: Technician Pilot Simulation

Before marking the walking skeleton complete, the coding agent must run a realistic QA pass as the dental technician using the clinic-facing flow.

- [x] Create/use at least one test clinic and one hashed PIN credential from the walking-skeleton JSON config.
- [x] Log in through the normal clinic login form/API using clinic code + PIN; do not bypass auth through direct DB inserts.
- [x] Create at least three representative orders:
  - crown with a single tooth,
  - bridge with a tooth range,
  - temporary crown/bridge or other configured temporary case.
- [x] While creating orders, verify date filtering is visible and enforced:
  - weekend dates are unavailable,
  - first business day after weekend/closure is unavailable where implemented,
  - valid later dates can be selected.
- [x] Confirm each created order displays a clear order code and instruction to write it on the impression/package.
- [x] Review the created orders through the technician/internal order list and verify clinic, credential, case name, teeth/range, delivery date, and order code are visible.
- [x] Test logout and verify authenticated order endpoints/pages are no longer accessible.
- [x] Capture QA evidence in the final implementation response: commands used, API/browser path tested, created test order codes, and any defects or UX issues found.
- [x] If browser automation is unavailable in the agent environment, perform API-level smoke tests plus list the remaining manual browser checks explicitly.

---


---

## Walking Skeleton Implementation Notes

Completed locally on 2026-05-31. Assumptions/decisions made during implementation:

- Scheduling domain code lives in a new `Orders` project. EF persistence lives in `Database`; Web owns HTTP endpoints/UI wiring.
- Walking-skeleton clinics/credentials/work rules are loaded from JSON at startup (`Web/scheduling.walking-skeleton.json`). The temporary authenticated reload endpoint was removed; config changes require restart.
- Clinics and credentials are JSON-configured for now; orders and auth sessions are persisted in SQLite. Orders snapshot clinic/credential labels and PIN-hash fingerprint for audit.
- Session cookies are opaque server-side sessions. The local HTTP dev cookie sets `Secure` only when the incoming request is HTTPS so local testing works; production behind TLS should be reviewed with forwarded headers before v1.5.
- Date availability uses the planned minimal weekend-only provider. Monday/first-business-day-after-weekend is delivery-disabled while still counting for lead time.
- Order-code generator uses descriptive codes behind `IOrderCodeGenerator`; final BG/Latin ambiguity research and normalization remain follow-ups.
- SQLite order listing now sorts by a persisted `CreatedAtUnixTimeMilliseconds` column to avoid provider limitations around ordering by `DateTimeOffset`.
- Shade is now a first-class order property (`Shade` enum) persisted through API/SQLite; UI includes `Unspecified` as an explicit option.
- Historical note: the preferred prototype stepper flow was later integrated into the production `/orders` page; the obsolete prototype file was removed in Slice 8.

Validation evidence:

- `dotnet build Spark3Dent.sln --no-restore` passed.
- `dotnet test Spark3Dent.sln` passed: 510 total tests.
- API QA created these orders as clinic `DEMO` / credential `assistant-1`: `KQ5-G46`, `EMC-XFC`, `8P3-MF6`.
- Browser QA created order `D89-6SQ` and captured screenshots:
  - `agent-qa/orders-skeleton/01-login-page.png`
  - `agent-qa/orders-skeleton/02-authenticated-order-list.png`
  - `agent-qa/orders-skeleton/03-created-order-confirmation.png`

## Follow-Up TODOs to Carry Forward

These are intentionally not detailed here. When the next detailed plan is created, copy these items forward, refine them based on what was learned during the walking skeleton, and keep any still-deferred items as follow-up TODOs in that next plan.

- [x] **Stepper-to-real-orders UI integration plan:** Completed through the order-flow vertical slices; the real `/orders` page now owns the scheduler flow and the obsolete prototype was removed.
- [ ] Calendar/holiday hardening plan.
  - Include Bulgarian public holiday provider behind `INonWorkingDayProvider` / `IHolidayCalendarProvider`.
  - Use an always-fetch-and-parse implementation for `https://xn--b1aekbb1acci5f.com/` for v1 unless research reveals a better source.
  - Add parser tests using saved HTML fixtures.
  - Add first-business-day-after-closure tests for holiday-adjacent closures.
  - Add manual override/review/save design for later v1.5/v2.
  - Carry any unresolved non-calendar follow-up TODOs from this section into that plan.
- [ ] Order code alphabet research/finalization plan.
  - Investigate Bulgarian/Latin visual and spoken ambiguity.
  - Finalize allowed alphabet and normalization rules.
  - Add rejection/normalization tests.
  - Carry still-deferred TODOs into the next plan.
- [ ] Product/work-rule configuration refinement plan.
  - Review default combinations with dental technician.
  - Finalize explicit min-business-days config before v1 finalization.
  - Add UI filtering for allowed combinations.
  - Carry still-deferred TODOs into the next plan.
- [ ] Teeth UX refinement plan.
  - Continue polishing the visual FDI tooth selector in the production scheduler UI as needed.
  - Carry still-deferred TODOs into the next plan.
- [ ] v1.5 preview-readiness plan.
  - Confirm the temporary config reload endpoint remains removed.
  - Add privacy/cookie page.
  - Improve credential management beyond JSON upload.
  - Add audit viewing/export if needed.
  - Add holiday admin review/save if not already implemented.
  - Carry still-deferred TODOs into the next plan.
- [ ] v2 capacity/workload plan.
  - Add weighted workload units.
  - Add capacity configuration.
  - Make delivery date availability capacity-aware and transactionally safe.
  - Carry still-deferred TODOs into the next plan.
