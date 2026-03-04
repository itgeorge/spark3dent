# Web Invoice Import Plan

## How to Use This Plan

- This file is the implementation backlog for adding a Web UI version of `invoices import` from `Cli/CliProgram.cs`.
- Each task is a checklist item. Agents should claim a phase, complete tasks in order, and only then mark items as done.
- Mark completion by changing `[ ]` to `[x]` and appending short evidence in parentheses, e.g. `(PR #123, tests: Web.Tests.InvoiceImportApiTests)`.
- Do not skip prerequisites. Many tasks depend on earlier phases.
- If scope changes, add new tasks under the relevant phase instead of editing completed history.
- Server-side phases are TDD-gated and must follow this order:
  1. add/adjust tests first,
  2. run tests and confirm RED (new tests fail for expected reason),
  3. implement minimal code,
  4. rerun tests and confirm GREEN.
- Client-side phase uses manual UI validation instead of automated UI tests.

## Goal and Constraints

- Add invoice import to the Web app (`Web/Web.csproj`) through the settings cog menu in `Web/wwwroot/index.html`.
- Web import must support selecting multiple PDF files (not directory recursion in browser UI).
- Import logic should reuse existing domain behavior (`GptLegacyPdfParser`, `InvoiceManagement.ImportLegacyInvoiceAsync`, `IClientRepo`).
- OpenAI key must be read from app configuration (`App` section) and injected via environment variables (no secrets committed to source control).
- Preserve current behavior for:
  - legacy PDF storage in blob (`legacy/imported-{number}.pdf`)
  - marking imported invoices as `isLegacy`
  - preventing edits of legacy invoices

## Current Codebase Findings (Baseline)

- CLI import behavior is implemented in `Cli/CliProgram.cs` (`InvoicesImportAsync`):
  - reads PDFs recursively from directory
  - parses each PDF with `GptLegacyPdfParser.TryParseAsync(pdfPath, apiKey)`
  - resolves nickname via existing client lookup (`FindByCompanyIdentifierAsync`) or prompt/cached mapping
  - imports via `InvoiceManagement.ImportLegacyInvoiceAsync(data, pdfPath)`
  - supports `--dry-run`, `--limit`, and `--nickname-from-mol`
- Web backend currently has no invoice import endpoint in `Web/Api.cs`.
- Web frontend currently has a top-right settings button placeholder (`#btnTopSettings`) with no dropdown/menu action in `Web/wwwroot/index.html`.
- Config models in `Configuration/Config.cs` currently do not include OpenAI key under `App`.
- Web config is loaded with environment variable support (`AddEnvironmentVariables` in `Configuration/JsonAppSettingsLoader.cs` and `Web/WebProgram.cs` binding), so `App__...` overrides are already supported.
- Web tests exist for API behavior (`Web.Tests` with `ApiTestFixture`), but none for invoice import endpoints yet.

## Proposed Web Import Design

- Add a settings dropdown entry: **Import legacy invoices (PDF)**.
- Use browser file picker with `multiple` and `.pdf` accept filter.
- Use a two-step API flow to preserve CLI nickname resolution behavior without terminal prompts:
  1. **Analyze step**: upload selected PDFs, parse metadata + recipient info, return per-file parse result and unresolved company identifiers.
  2. **Import step**: submit selected parse results + nickname mappings for unresolved companies; create clients when missing; import invoices.
- Keep a fast path option: auto-generate nicknames from company name or MOL slug (CLI parity with `--nickname-from-mol`).
- Return detailed import summary: imported/skipped/failed + per-file messages.

---

## Phase 1 - Configuration and Secret Wiring

- [x] **TDD (RED):** add/update server-side tests that assert import endpoints return an error when OpenAI key is not configured. (InvoiceImportApiTests)
- [x] **TDD (GREEN):** confirm those tests pass once key is supplied via config/env.
- [x] Add `OpenAiKey` to `AppConfig` in `Configuration/Config.cs` (nullable string).
- [x] Keep `Web/appsettings.json` without real key (placeholder/empty only; never commit secret).
- [x] Read key in Web import API path from config (`setup.Config.App.OpenAiKey`) with optional fallback to `OPENAI_API_KEY` for compatibility.
- [x] Add validation/error message for missing key in import endpoints (`400` with actionable error).
- [x] Document env injection usage:
  - Desktop: `App__OpenAiKey=...`
  - Update instructions in `deployments.md` for Docker Compose local/hetzner: pass `App__OpenAiKey` through environment (or env file not committed).

Dependencies: none.

## Phase 2 - Backend Import API Contract

- [x] **TDD (RED):** add failing API tests for `POST /api/invoices/import/analyze` and `POST /api/invoices/import/commit` (invalid payloads, missing files, response shape).
- [x] **TDD (GREEN):** implement endpoint contract until these tests pass and existing API tests remain green.
- [x] Define request/response DTOs in `Web/ImportDtos.cs`:
  - analyze request options (`nicknameFromMol`, optional `limit` from form)
  - analyze result (`files`, `unresolvedCompanies`)
  - import request (`items`, `companyIdentifier -> nickname` map, dry-run flag optional)
  - import result summary (`imported`, `skipped`, `failed`, item statuses)
- [x] Add endpoint `POST /api/invoices/import/analyze` accepting `multipart/form-data` with multiple PDF files.
- [x] Add endpoint `POST /api/invoices/import/commit` accepting JSON payload from analysis output plus nickname map.
- [x] Validate input constraints:
  - only `.pdf`
  - non-empty upload list
  - max file count (500) and max file size (1MB) guardrails
  - (unique company-id mapping conflicts deferred to Phase 3)
- [x] Ensure error shape stays consistent with existing API style (`{ error: ... }`).

Dependencies: Phase 1.

## Phase 3 - Backend Import Execution Logic

- [ ] **TDD (RED):** add failing behavior tests for import execution parity (parse failure skip, duplicate invoice skip, client creation, mixed summary).
- [ ] **TDD (GREEN):** implement coordinator and flow until behavior tests pass; verify no regression in existing suites.
- [ ] Implement reusable import coordinator (in `Web` or shared service) that mirrors CLI behavior:
  - parse each uploaded PDF via `GptLegacyPdfParser`
  - resolve existing clients by company identifier
  - generate fallback nickname by company name / MOL slug (`ToSlug` parity with CLI)
  - create missing clients before import
  - call `invoiceManagement.ImportLegacyInvoiceAsync(...)`
- [ ] Replace CLI path-based parsing with stream/temp-file strategy for uploaded files:
  - write uploads to temp files for parser compatibility
  - ensure cleanup in `finally`
- [ ] Keep idempotent behavior:
  - skip existing invoice numbers (`already exists`)
  - report skipped vs failed separately
- [ ] Return per-file diagnostics suitable for UI table rendering.
- [ ] Add structured logging for analyze and commit operations (counts + timings; no secret logging).

Dependencies: Phase 2.

## Phase 4 - Web UI: Settings Dropdown and Import UX

- [ ] **Manual Review Baseline:** capture pre-change behavior notes for settings button, modal interactions, and list refresh behavior.
- [ ] Convert `#btnTopSettings` from placeholder into dropdown trigger in `Web/wwwroot/index.html`.
- [ ] Add dropdown menu markup + styles with at least:
  - Import legacy invoices (PDF)
  - (optional) future placeholder items disabled
- [ ] Add hidden `<input type="file" multiple accept=".pdf,application/pdf">`.
- [ ] Implement import modal flow in existing script block:
  - step 1: file selection
  - step 2: analyze (loading state)
  - step 3: unresolved company nickname mapping UI
  - step 4: commit import and show summary
- [ ] Reuse existing modal/error/loading patterns where possible (`modalError`, `loadingOverlay`, `toast`).
- [ ] Refresh invoice/client lists after successful import (`dataNeedsRefresh = true`, refresh active view).
- [ ] Add UX safeguards:
  - disable import actions while request in flight
  - show parse/import failures per file
  - keep modal keyboard/escape behavior consistent with current dialogs
- [ ] **Manual Review Validation:** run UI checklist (select files -> analyze -> resolve nicknames -> commit -> data refresh) and record pass/fail notes.

Dependencies: Phase 2 and Phase 3.

## Phase 5 - Tests (Backend + UI-Surface Expectations)

- [ ] **TDD (RED):** add/expand failing backend tests for remaining edge cases not covered by prior phases.
- [ ] **TDD (GREEN):** implement only missing server behavior required to make these tests pass.
- [ ] Add Web API tests for analyze endpoint:
  - rejects missing files
  - rejects missing OpenAI key
  - accepts multipart with multiple files
  - returns unresolved companies when client lookup misses
- [ ] Add Web API tests for commit endpoint:
  - imports with provided nickname map
  - skips duplicate invoice number
  - creates missing clients then imports
  - reports mixed outcomes correctly
- [ ] Add test fixture support for import-key configuration injection (`App:OpenAiKey`) using in-memory config in `Web.Tests/ApiTestFixture.cs`.
- [ ] Update manual UI checklist in docs (client-side verification remains manual for this scope).

Dependencies: Phases 2-4.

## Phase 6 - Deployment and Documentation

- [ ] **Verification TODO:** confirm env-injected `App__OpenAiKey` behavior in dev/test deployment and confirm missing-key failure messaging is still correct.
- [ ] Update `deployments.md` environment variables table with `App__OpenAiKey` (placeholder value).
- [ ] Update `docker-compose.local.yml` and `docker-compose.hetzner.yml` guidance to pass `App__OpenAiKey` from environment (avoid hardcoding value in committed files).
- [ ] Confirm no sensitive data is committed (no real key in any tracked file).

Dependencies: Phase 1 complete, preferably after Phase 5.

## Phase 7 - Final Validation and Rollout Checklist

- [ ] **Server TDD Closure:** verify every server-side phase has explicit RED and GREEN evidence in checklist/PR notes.
- [ ] Run targeted tests:
  - `dotnet test Web.Tests --filter "Invoice|Client|Startup|Error"`
  - newly added import tests
- [ ] Manual web verification:
  - settings dropdown opens/closes correctly
  - multi-PDF selection works
  - unresolved client mapping works
  - imported invoice appears in lists and opens PDF
  - imported invoice marked legacy and cannot be edited
- [ ] Validate environment-only key path in each runtime mode:
  - Desktop
  - LocalDocker
  - HetznerDocker
- [ ] Capture known limitations and follow-up tasks (e.g., batching, cancellation, progress streaming).

Dependencies: all previous phases.

---

## Suggested Agent Parallelization

- Agent A: Phase 1 + Phase 2 (config + API contract).
- Agent B: Phase 3 (import coordinator/service internals) after Phase 2 DTO contract is stable.
- Agent C: Phase 4 (frontend dropdown + modal workflow) after endpoint contracts are defined.
- Agent D: Phase 5 + Phase 6 (tests + docs/deployment updates) once A/B/C merge shape is clear.

## Open Decisions to Confirm Before Coding

- [x] Maximum upload limits (count/size) and expected deployment constraints.
  - Answer: max 500 files, max 1MB per file
- [x] Whether to keep a `dryRun` mode in Web UI (recommended for parity and safer first runs).
  - Answer: yes, for parity with CLI
- [x] Whether nickname fallback default is company-name slug or MOL slug (CLI currently supports both via flag; default is company-name slug).
  - Answer: MOL slug
- [x] Whether to support single-step auto-import when all companies already exist (recommended).
  - Answer: yes, for parity with CLI
