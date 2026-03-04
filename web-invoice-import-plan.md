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
  - (unique company-id mapping conflicts deferred to Phase 4)
- [x] Ensure error shape stays consistent with existing API style (`{ error: ... }`).

Dependencies: Phase 1.

## Phase 3 - DI Seam and Testability Foundation

- [x] **TDD (RED):** add failing Web API tests proving import endpoints can run without real OpenAI/network calls when importer dependency is replaced in test host. (Added DI-override tests in `Web.Tests/InvoiceImportApiTests.cs`)
- [x] **TDD (GREEN):** implement DI seam in `Web/WebProgram.cs` with bootstrap-to-service wiring until tests pass. (`dotnet test Web.Tests`)
- [x] Add true DI seam in `Web/WebProgram.cs` for dependencies currently carried via bootstrap object:
  - invoice-management abstraction (interface around `Accounting/InvoiceManagement.cs`)
  - `IClientRepo`
  - invoice exporters used by current endpoints
  - import service dependency
- [x] Introduce interface abstraction for `InvoiceManagement` usage in Web layer (narrow facade is preferred over exposing whole class surface). (`Web/IInvoiceOperations` + adapter)
- [x] Keep production behavior parity (same concrete runtime behavior after wiring).
- [x] Allow `Web.Tests/ApiTestFixture.cs` to override services via DI for deterministic endpoint tests.
- [x] Ensure all API paths keep existing error style (`{ error: ... }`).

Dependencies: Phase 2.

## Phase 4 - Importer Abstraction and Execution Logic

- [x] **TDD (RED):** add failing importer behavior tests for parity (parse failure skip, duplicate invoice skip, client creation, mixed summary). (`Web.Tests/InvoiceImporterContractTest.cs`)
- [x] **TDD (GREEN):** implement importer flow until behavior tests pass and existing suites remain green. (`dotnet test Web.Tests`)
- [x] Introduce `IInvoiceImporter` owning both operations:
  - `AnalyzeAsync(...)`
  - `CommitAsync(...)`
- [x] Create importer contract tests using base abstract test structure analogous to `Accounting.Tests/ClientRepoContractTest.cs`.
- [x] Add concrete importer test suites:
  - real implementation tests analogous to `Database.Tests/SqliteClientRepoTest.cs`
  - fake implementation tests analogous to `Accounting.Tests/Fakes/FakeClientRepoTest.cs`
- [x] Implement real `InvoiceImporter` to mirror CLI behavior:
  - parse each uploaded PDF via `GptLegacyPdfParser`
  - resolve existing clients by company identifier
  - generate fallback nickname with MOL slug default (per decision)
  - create missing clients before import
  - call `InvoiceManagement.ImportLegacyInvoiceAsync(...)`
- [x] Replace CLI path-based parsing with stream/temp-file strategy for uploaded files:
  - write uploads to temp files for parser compatibility
  - ensure cleanup in `finally`
- [x] Keep idempotent behavior:
  - skip existing invoice numbers (`already exists`)
  - report skipped vs failed separately
- [x] Return per-file diagnostics suitable for UI table rendering.
- [x] Add structured logging for analyze and commit operations (counts + timings; no secret logging).

Dependencies: Phase 3.

## Phase 5 - Web UI: Settings Dropdown and Import UX

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

Dependencies: Phase 2, Phase 3, and Phase 4.

## Phase 6 - Tests (Backend + UI-Surface Expectations)

- [ ] **TDD (RED):** add/expand failing backend tests for remaining edge cases not covered by prior phases.
- [ ] **TDD (GREEN):** implement only missing server behavior required to make these tests pass.
- [ ] Refactor `Web.Tests/InvoiceImportApiTests.cs` to use DI-injected `FakeInvoiceImporter` for non-key-gating tests.
- [ ] Keep dedicated key-gating tests for missing OpenAI key behavior.
- [ ] Add/adjust Web API tests for analyze endpoint:
  - rejects missing files
  - rejects invalid file type/size/count
  - accepts multipart with multiple files
  - returns expected response shape from importer
- [ ] Add/adjust Web API tests for commit endpoint:
  - validates payload contract
  - validates company-id mapping conflicts
  - returns expected summary shape from importer
  - reports mixed outcomes correctly
- [ ] Add/adjust test fixture support in `Web.Tests/ApiTestFixture.cs` for service override registration (`IInvoiceImporter` and related interfaces).
- [ ] Update manual UI checklist in docs (client-side verification remains manual for this scope).

Dependencies: Phases 3-5.

## Phase 7 - Deployment and Documentation

- [ ] **Verification TODO:** confirm env-injected `App__OpenAiKey` behavior in dev/test deployment and confirm missing-key failure messaging is still correct.
- [ ] Update `deployments.md` environment variables table with `App__OpenAiKey` (placeholder value).
- [ ] Update `docker-compose.local.yml` and `docker-compose.hetzner.yml` guidance to pass `App__OpenAiKey` from environment (avoid hardcoding value in committed files).
- [ ] Confirm no sensitive data is committed (no real key in any tracked file).

Dependencies: Phase 1 complete, preferably after Phase 6.

## Phase 8 - Final Validation and Rollout Checklist

- [ ] **Server TDD Closure:** verify every server-side phase has explicit RED and GREEN evidence in checklist/PR notes.
- [ ] Run targeted tests:
  - `dotnet test Web.Tests --filter "Invoice|Client|Startup|Error"`
  - importer contract tests (real + fake)
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
- Agent B: Phase 3 (DI seam and Web testability) after Phase 2 DTO contract is stable.
- Agent C: Phase 4 (importer abstraction + real/fake implementations) after Phase 3 DI seam is in place.
- Agent D: Phase 5 (frontend dropdown + modal workflow) after Phase 4 endpoint internals are stable.
- Agent E: Phase 6 + Phase 7 (tests + docs/deployment updates) once B/C/D merge shape is clear.

## Open Decisions to Confirm Before Coding

- [x] Maximum upload limits (count/size) and expected deployment constraints.
  - Answer: max 500 files, max 1MB per file
- [x] Whether to keep a `dryRun` mode in Web UI (recommended for parity and safer first runs).
  - Answer: yes, for parity with CLI
- [x] Whether nickname fallback default is company-name slug or MOL slug (CLI currently supports both via flag; default is company-name slug).
  - Answer: MOL slug
- [x] Whether to support single-step auto-import when all companies already exist (recommended).
  - Answer: yes, for parity with CLI
