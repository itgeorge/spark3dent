# Spark 3Dent -- Implementation Plan

*Created: 2026-02-17*

This plan covers the full implementation of the invoice-focused dental lab toolset,
from pure domain logic through database persistence to CLI wiring. It follows TDD
methodology: for each component, tests are written/completed first, then the
implementation is built to make them pass.

Work is organized into phases. Each phase builds on the previous ones. Within each
phase, tasks are ordered so that tests come before implementations. Checkbox items
are granular enough for an agent to pick up one (or a small group) at a time.

**Progress tracking:** When completing a task, mark its checkbox `[x]` in this file
as part of the same changeset. This keeps the plan in sync with the actual state of
the codebase at all times.

---

## Assumptions & Decisions

These are inferred from the codebase and architecture doc. Review and correct before
starting implementation.

1. **`DateTime` vs `DateOnly`**: The architecture doc recommends `DateOnly` for
   invoice dates, but `Invoice.InvoiceContent.Date` is currently `DateTime` and all
   existing tests use `DateTime`. **Decision: keep `DateTime` for now** to avoid
   breaking all existing test code. The repo layer will ignore the time component
   when enforcing the date ordering invariant. This can be revisited later.

2. **Seller address source**: The CLI `invoices issue` command only takes a client
   nickname and amount. The buyer address comes from `IClientRepo`. The seller
   address must come from configuration. **Decision: add a `SellerAddress` field to
   `AppConfig`** (as `BillingAddress` or a serializable equivalent) so it can be
   loaded from `appsettings.json`.

3. **PDF export**: Invoices should be exported to PDF upon issue and stored
   via `IBlobStorage`. The CLI should display the file path after export.

4. **`InvoiceManagement` role**: This class in `Accounting` is the service/coordinator
   layer. It should accept high-level parameters (client nickname, amount, date),
   look up the client, build `InvoiceContent`, call the repo, export to PDF, and
   return the result. Its signature should be updated to match this role.

5. **Line items**: For the initial CLI, each invoice has a single line item with a
   fixed description (e.g. "Зъботехнически услуги") and the given amount. Multi-line
   item support can be added later via the GUI.

6. **EF Core project**: A new `Database` project will be created to house the
   `AppDbContext`, entity configurations, and SQLite repository implementations.
   A corresponding `Database.Tests` project will hold the SQLite-specific tests.

7. **`BankTransferInfo`**: Bank transfer is the only payment method for now.
   `BankTransferInfo` (IBAN, BankName, Bic) is required on every `InvoiceContent`
   and populates the pay-grid section of the invoice template. The seller's bank
   details should come from `AppConfig` (alongside `SellerAddress`) for the
   CLI/service layer.

---

## Phase 0: Project Setup & Dependencies

Before any implementation, ensure all required NuGet packages and project references
are in place.

- [x] **0.1** Add `HtmlAgilityPack` NuGet package to `Invoices.csproj`
- [x] **0.2** Add `HtmlAgilityPack` NuGet package to `Invoices.Tests.csproj`
- [x] **0.3** Add `Microsoft.Extensions.Configuration`, `Microsoft.Extensions.Configuration.Json`,
      and `Microsoft.Extensions.Configuration.EnvironmentVariables` NuGet packages to
      `Configuration.csproj`
- [x] **0.4** Create a new `Database` class library project (`Database.csproj`,
      target `net9.0`). Add NuGet packages: `Microsoft.EntityFrameworkCore.Sqlite`,
      `Microsoft.EntityFrameworkCore.Design`. Add project references to `Invoices`,
      `Accounting`, `Configuration`, and `Utilities`
- [x] **0.5** Create a new `Database.Tests` test project (`Database.Tests.csproj`).
      Add NuGet packages: `Microsoft.NET.Test.Sdk`, `NUnit`, `NUnit3TestAdapter`.
      Add project references to `Database`, `Invoices.Tests`, `Accounting.Tests`
      (to inherit contract tests)
- [x] **0.6** Add project references to `Cli.csproj`: `Configuration`, `Invoices`,
      `Accounting`, `Database`, `Storage`, `Utilities`
- [x] **0.7** Add all new projects (`Database`, `Database.Tests`) to `Spark3Dent.sln`
- [x] **0.8** Add `PuppeteerSharp` NuGet package to `Invoices.csproj` for headless
      browser PDF rendering. Configure the build to bundle a specific Chromium
      revision so it ships with the app (no runtime download). Use
      `BrowserFetcher` at build/publish time to download the Chromium binary into
      the output directory, and at runtime point PuppeteerSharp at the local path
      (e.g. via `LaunchOptions.ExecutablePath`). Document the bundled Chromium
      revision in a comment or constant for reproducibility
- [x] **0.9** Verify the solution builds cleanly: `dotnet build Spark3Dent.sln`

---

## Phase 1: Configuration

Implement config loading so all downstream components can use it.

### Tests

- [x] **1.1** Create `Configuration.Tests` project with NUnit. Add project reference
      to `Configuration`
- [x] **1.2** Write tests for `JsonAppSettingsLoader`:
  - Loading a valid `appsettings.json` with all fields populated returns correct
    `Config`
  - Loading an `appsettings.json` with missing optional fields uses defaults
    (`StartInvoiceNumber = 1`, empty paths)
  - Loading when `appsettings.json` does not exist throws a descriptive error
  - Environment variables override JSON values (test with at least one field)
- [x] **1.3** Add `SellerAddress` to `AppConfig` (as a nested record or
      `BillingAddress`-compatible structure). Update tests to verify it loads
      correctly

### Implementation

- [x] **1.4** Implement `JsonAppSettingsLoader.LoadAsync()`:
  - Use `ConfigurationBuilder` with `AddJsonFile("appsettings.json")` and
    `AddEnvironmentVariables()`
  - Bind to `Config` record
  - Return the loaded config
- [x] **1.5** Add `SellerAddress` field to `AppConfig` (in `Config.cs`)
- [x] **1.11** Add `SellerBankTransferInfo` field to `AppConfig` (in `Config.cs`).
      Add placeholder to `Cli/appsettings.json` with IBAN, BankName, Bic.
      Update configuration tests to verify it loads correctly
- [x] **1.6** Create `Cli/appsettings.json` with sensible defaults:
  - `App.StartInvoiceNumber`: `1`
  - `App.SellerAddress`: placeholder seller address fields
  - `Desktop.DatabasePath`: `""` (to be filled with AppData Local default at runtime)
  - `Desktop.BlobStoragePath`: `""` (to be filled with Documents default at runtime)
  - `Desktop.LogDirectory`: `""` (to be filled with AppData Local default at runtime)
- [x] **1.7** Configure `Cli.csproj` to copy `appsettings.json` to output directory
      (`<Content Include="appsettings.json"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></Content>`)
- [x] **1.8** In `CliProgram.Main`, implement default path resolution:
  - If `Desktop.DatabasePath` is empty, default to
    `Path.Combine(Environment.GetFolderPath(SpecialFolder.LocalApplicationData), "Spark3Dent", "spark3dent.db")`
  - If `Desktop.BlobStoragePath` is empty, default to
    `Path.Combine(Environment.GetFolderPath(SpecialFolder.MyDocuments), "Spark3Dent")`
  - If `Desktop.LogDirectory` is empty, default to
    `Path.Combine(Environment.GetFolderPath(SpecialFolder.LocalApplicationData), "Spark3Dent", "logs")`
  - Write the resolved defaults back to `appsettings.json` if defaults were used
- [x] **1.9** Run configuration tests and verify they pass:
      `dotnet test Configuration.Tests`
- [x] **1.10** Add `Configuration.Tests` to `Spark3Dent.sln`

---

## Phase 2: Bulgarian Amount Transcriber

Pure domain logic with no external dependencies -- a good early TDD target.

### Tests

- [x] **2.1** Complete `BgAmountTranscriberTest` test cases:
  - Cents-only values: `0_01` through `0_99` (representative samples for
    single-digit, teens, tens, compound numbers)
  - Whole-only values: `1_00`, `2_00`, ..., `9_00`, `10_00`, `11_00`, ..., `19_00`,
    `20_00`, `30_00`, ..., `90_00`, `100_00`, `200_00`, ..., `999_00`
  - Mixed values covering all digit-count combinations (1-digit whole + 1-digit
    cents, 2-digit whole + 2-digit cents, 3-digit + teens, 4-digit whole, etc.)
  - Boundary: `999999_99` (max below 1M)
  - All existing test case placeholders filled in
- [x] **2.2** Implement the test body for `Transcribe_WhenValidAmount_ThenTranscribes`:
      instantiate `BgAmountTranscriber`, call `Transcribe`, assert expected string
- [x] **2.3** Implement the test body for
      `Transcribe_WhenTranscribingAnyNumberBelow1M_ThenTranscribes`: loop 1..999999,
      call `Transcribe`, assert result is non-null and non-empty
- [x] **2.4** Implement the test body for
      `Transcribe_WhenTranscribing1MOrAbove_ThenThrows`: test `1_000_000_00` and a
      few values above
- [x] **2.5** Implement the test body for
      `Transcribe_WhenNegativeAmount_ThenThrows`: test `-1_00`, `-100_00`

### Implementation

- [x] **2.6** Implement `BgAmountTranscriber.Transcribe()`:
  - Split cents into whole euros and remaining cents
  - Convert whole part to Bulgarian words (handle units, teens, tens, hundreds,
    thousands correctly with proper Bulgarian grammar -- e.g. "и" conjunction)
  - Convert cents part to Bulgarian words
  - Combine: `"{whole} евро и {cents} цента"`, or `"{whole} евро"` if cents == 0,
    or special case for zero
  - Throw for negative amounts or amounts >= 1,000,000 EUR
- [x] **2.7** Run tests: `dotnet test Invoices.Tests --filter BgAmountTranscriberTest`

---

## Phase 3: Invoice HTML Template

Depends on HtmlAgilityPack. Template loading + rendering with field substitution.

### Tests

- [x] **3.1** Uncomment the `using HtmlAgilityPack;` in `InvoiceHtmlTemplateTest.cs`
- [x] **3.2** Implement `GetFieldValue` helper method using HtmlAgilityPack
- [x] **3.3** Implement `Render_GivenValidTemplate_WhenRenderingInvoice_ThenAllFieldsPopulated`:
  - Load template via `InvoiceHtmlTemplate.LoadAsync` with `templateHtmlOverride`
  - Call `Render(invoice)`
  - Assert all fields by ID: `invNo`, `invDate`, `sellerNameTop`,
    `sellerCompanyName`, `sellerCity`, `sellerAddr`, `sellerRepresentativeName`,
    `sellerBulstat`, `sellerVat`, `buyerCompanyName`, `buyerAddr`,
    `buyerRepresentativeName`, `buyerBulstat`, `buyerVat`, `totalWords`,
    `taxBase`, `vat20`, `totalDue`, `placeOfSupply`, `taxEventDate`
  - Assert line items in `#items` tbody: each row has `idx`, `description`, `amount`
    fields populated
- [x] **3.4** Implement `Render_GivenDefaultTemplate_WhenRenderingInvoice_ThenAllFieldsPopulated`:
  - Same assertions but load without `templateHtmlOverride` (uses embedded resource)
- [x] **3.5** Implement `Render_GivenTemplateMissingLineItemField_WhenRenderingInvoice_ThenThrows`:
  - Modify `ValidTemplateHtml` to remove the specified `data-field` attribute
  - Assert `LoadAsync` or `Render` throws with descriptive message
- [x] **3.6** Implement `Render_GivenValidTemplate_WhenRenderingInvoice_ThenCopiesTagAndClassAttributes`:
  - Render multi-line item invoice
  - Parse output HTML, verify each line item row preserves the tag types and CSS
    classes from the template row
- [x] **3.7** Implement `Render_GivenInvalidTemplate_WhenRenderingInvoice_ThenThrows`:
  - Pass malformed HTML, assert descriptive error
- [x] **3.8** Implement `Render_GivenFailingTranscriber_WhenRenderingInvoice_ThenThrows`:
  - Use `FakeTranscriber` with `Fail = true`, assert exception propagates
- [x] **3.9** Implement `Render_GivenDefaultTemplate_WhenRenderingInvoiceWithNegativeAmount_ThenThrows`
- [x] **3.10** Implement `Render_GivenDefaultTemplate_WhenRenderingInvoiceWithZeroAmount_ThenAmountsAreZero`

### Implementation

- [x] **3.11** Implement `InvoiceHtmlTemplate.LoadAsync()`:
  - If `templateHtmlOverride` is not null, use it; otherwise load
    `template.html` embedded resource via `EmbeddedResourceLoader`
  - Parse with `HtmlDocument`
  - Validate all required element IDs exist (`invNo`, `invDate`, `sellerNameTop`,
    etc.) and `#items tbody` has at least one template row with `data-field`
    attributes for `idx`, `description`, `amount`
  - Throw descriptive errors for missing fields
  - Store parsed template and transcriber in private fields
- [x] **3.12** Implement `InvoiceHtmlTemplate.Render()`:
  - Clone the parsed template document
  - Populate all fields by ID with invoice data
  - Format amounts as `{euros}.{cents:D2} €`
  - Format dates as `dd.MM.yyyy г.`
  - For line items: use the first `<tr>` in `#items` as a template row, clone it for
    each line item, set `data-field` values, preserve tag/class attributes, remove
    the original template row
  - Use `IAmountTranscriber` for `#totalWords`
  - Validate no negative amounts
  - Return the rendered HTML string
- [x] **3.13** Run tests:
      `dotnet test Invoices.Tests --filter InvoiceHtmlTemplateTest`

---

## Phase 4: Fake Repositories (TDD Foundation)

In-memory fakes that pass the contract tests. These are used by all higher-level
tests (service layer, CLI) to avoid needing a real database.

### Invoice Fake

- [x] **4.1** Implement `FakeInvoiceRepo` (in `Invoices.Tests/Fakes/`):
  - In-memory `Dictionary<string, Invoice>` storage
  - Thread-safe via `lock` on all operations
  - `CreateAsync`: auto-increment number starting from 1, enforce date ordering
    invariant (new date >= last invoice date), return `Invoice` with assigned number
  - `GetAsync`: look up by number, throw `InvalidOperationException` if not found
  - `UpdateAsync`: look up by number, validate date ordering against adjacent
    invoices (prev and next by number), throw if violated, replace content
  - `LatestAsync`: return invoices sorted by number descending, support limit and
    cursor-based pagination. Cursor is the invoice number to start after
- [x] **4.2** Implement `FakeInvoiceRepoTest.SetUpAsync()`:
  - Return a `FixtureBase` that wraps `FakeInvoiceRepo`
  - `SetUpInvoiceAsync` delegates to `Repo.CreateAsync`
  - `GetInvoiceAsync` delegates to `Repo.GetAsync`
- [x] **4.3** Run fake invoice repo tests:
      `dotnet test Invoices.Tests --filter FakeInvoiceRepoTest`
- [x] **4.4** Verify all 20 contract tests pass for the fake

### Client Contract Tests

- [x] **4.5** Write `ClientRepoContractTest` test cases (in `Accounting.Tests/`):
  - `Add_GivenValidClient_WhenAdding_ThenClientIsRetrievable`
  - `Add_WhenAddingDuplicateNickname_ThenThrows`
  - `Get_GivenExistingClient_WhenGetting_ThenReturnsClient`
  - `Get_GivenNonExistingClient_WhenGetting_ThenThrows`
  - `Update_GivenExistingClient_WhenUpdatingAddress_ThenUpdated`
  - `Update_GivenExistingClient_WhenUpdatingNickname_ThenUpdated`
  - `Update_GivenNonExistingClient_WhenUpdating_ThenThrows`
  - `Delete_GivenExistingClient_WhenDeleting_ThenNotRetrievable`
  - `Delete_GivenNonExistingClient_WhenDeleting_ThenThrows`
  - `List_GivenMultipleClients_WhenListing_ThenReturnedSortedAlphabetically`
  - `List_GivenMultipleClients_WhenListingWithLimit_ThenLimited`
  - `List_GivenMultipleClients_WhenListingWithCursor_ThenPaginated`
  - `List_GivenNoClients_WhenListing_ThenReturnsEmpty`

### Client Fake

- [x] **4.6** Implement `FakeClientRepo` (in `Accounting.Tests/Fakes/`):
  - In-memory `Dictionary<string, Client>` keyed by nickname
  - `AddAsync`: throw if nickname already exists
  - `GetAsync`: throw if not found
  - `UpdateAsync`: support partial updates (null fields = keep current), throw if
    client not found. If nickname changes, re-key the dictionary entry
  - `DeleteAsync`: throw if not found
  - `ListAsync`: return sorted by nickname, support limit + cursor pagination
- [x] **4.7** Implement `FakeClientRepoTest.SetUpAsync()`:
  - Wrap `FakeClientRepo`, delegate `SetUpClientAsync` to `Repo.AddAsync`,
    `GetClientAsync` to `Repo.GetAsync`
- [x] **4.8** Run fake client repo tests:
      `dotnet test Accounting.Tests --filter FakeClientRepoTest`
- [x] **4.9** Verify all contract tests pass for the client fake

---

## Phase 5: Database Layer (SQLite + EF Core)

The real persistence layer. Contract tests ensure it behaves identically to fakes.

### EF Core Setup

- [x] **5.1** Create `Database/AppDbContext.cs`:
  - `DbSet<InvoiceEntity>` for invoices
  - `DbSet<InvoiceLineItemEntity>` for line items
  - `DbSet<ClientEntity>` for clients
  - `DbSet<InvoiceSequenceEntity>` for the sequence table
  - Configure relationships, indexes, and constraints in `OnModelCreating`:
    - Unique index on `InvoiceEntity.Number`
    - Foreign key from `InvoiceLineItemEntity` to `InvoiceEntity`
    - Primary key on `ClientEntity.Nickname`
    - `InvoiceSequenceEntity`: Id (PK, always 1), LastNumber (int, not null)
- [x] **5.2** Create entity classes in `Database/Entities/`:
  - `InvoiceEntity` (Id, Number, Date, seller fields, buyer fields)
  - `InvoiceLineItemEntity` (Id, InvoiceEntityId, Description, AmountCents,
    Currency)
  - `ClientEntity` (Nickname as PK, all BillingAddress fields)
  - `InvoiceSequenceEntity` (Id, LastNumber)
- [x] **5.3** Create mapping helpers to convert between domain records
      (`Invoice`, `Client`) and EF entities

### SQLite Invoice Repository

- [x] **5.4** Create `Database/SqliteInvoiceRepo.cs` implementing `IInvoiceRepo`:
  - Constructor takes `Func<AppDbContext>` factory (new context per operation) and
    `Config` (for `StartInvoiceNumber`)
  - `EnsureSequenceInitialized()`: within a transaction, check if
    `InvoiceSequence` row exists; if not, insert with
    `LastNumber = Config.App.StartInvoiceNumber - 1`
  - `CreateAsync`: begin transaction, read sequence, validate date ordering (new
    date >= highest-numbered invoice's date), increment `LastNumber`, insert
    invoice, commit. Return `Invoice` with the new number
  - `GetAsync`: query by number, throw `InvalidOperationException` if not found,
    map to domain `Invoice`
  - `UpdateAsync`: begin transaction, find by number, fetch adjacent invoices
    (prev/next by number), validate date ordering, update content, commit
  - `LatestAsync`: query ordered by number descending, support limit and cursor
    (where Number < cursor), map to domain records, return `QueryResult<Invoice>`

### SQLite Invoice Repo Tests

- [x] **5.5** Create `Database.Tests/SqliteInvoiceRepoTest.cs` inheriting
      `InvoiceRepoContractTest`:
  - `SetUpAsync`: create a temp SQLite database file, create `AppDbContext`,
    run `EnsureCreated()`, return fixture wrapping `SqliteInvoiceRepo`
  - Tear down: delete temp database file
- [x] **5.6** Add SQLite-specific tests (in same class or separate):
  - Fresh DB + `StartInvoiceNumber=1` -> first invoice number is `"1"`
  - Fresh DB + `StartInvoiceNumber=1000` -> first invoice number is `"1000"`
  - Changing config `StartInvoiceNumber` after first invoice does NOT affect
    numbering (sequence table is source of truth)
  - Concurrent `CreateAsync` calls: spawn parallel tasks, assert all numbers
    unique and contiguous
- [x] **5.7** Run SQLite invoice repo tests:
      `dotnet test Database.Tests --filter SqliteInvoiceRepoTest`
- [x] **5.8** Verify all contract tests + SQLite-specific tests pass

### SQLite Client Repository

- [x] **5.9** Create `Database/SqliteClientRepo.cs` implementing `IClientRepo`:
  - Constructor takes `Func<AppDbContext>` factory
  - `AddAsync`: insert, throw if duplicate nickname
  - `GetAsync`: query by nickname, throw if not found
  - `UpdateAsync`: find by nickname, apply non-null fields, handle nickname rename
  - `DeleteAsync`: find by nickname, throw if not found, remove
  - `ListAsync`: query ordered by nickname, support limit + cursor pagination

### SQLite Client Repo Tests

- [x] **5.10** Create `Database.Tests/SqliteClientRepoTest.cs` inheriting
      `ClientRepoContractTest`:
  - Same temp-DB pattern as invoice repo tests
- [x] **5.11** Run SQLite client repo tests:
      `dotnet test Database.Tests --filter SqliteClientRepoTest`
- [x] **5.12** Verify all contract tests pass

---

## Phase 6: Invoice PDF Export

Headless browser rendering of the HTML template to PDF.

### Tests

- [x] **6.1** Create `Invoices.Tests/InvoicePdfExporterTest.cs`:
  - Test that `Export` returns a non-empty stream
  - Test that the stream contains valid PDF content (check for `%PDF` header)

### Implementation

- [x] **6.2** Implement `InvoicePdfExporter.Export()`:
  - Render the template with `InvoiceHtmlTemplate.Render(invoice)` to get HTML
  - Launch headless browser via PuppeteerSharp with a bundled/embedded Chromium
    revision (shipped alongside the app, not downloaded at runtime)
  - Set page to A4 size, load HTML content
  - Generate PDF with `Page.PdfStreamAsync()` (or equivalent)
  - Return the PDF stream
- [x] **6.3** Run exporter tests:
      `dotnet test Invoices.Tests --filter InvoicePdfExporterTest`

### Manual Testing

- [x] **6.4** Introduce a `invoice` tool in `CliTools/CliToolsProgram.cs` that allows to render a single invoice to PDF and save it to a file. Mimic the existing `template` command, but instead of rendering a template, it renders an invoice and saves it to a file.
- [x] **6.5** Manually test the tool by rendering a few invoices and verifying the output is correct.

### Add regression tests

- [x] **6.6** Now that we have the `invoice` tool, and have tested and reviewed the output, we can use that output to add `InvoicePdfExporterRegressionTest` regression tests for the `InvoicePdfExporter` class that renders an invoice and saves it to a file.
  - Add a test which uses just the default data
  - Add a test which changes the invoice number, date and amount
  - Add a test which changes the buyer address
  - Add a test which changes the seller address
  - Add a test which changes the bank transfer info
  - Add a test which changes the line items

---

## Phase 7: Service Layer (InvoiceManagement)

Coordinates between repos, exporter, and blob storage.

### Tests

- [x] **7.1** Create `Accounting.Tests/InvoiceManagementTest.cs`:
  - Uses `FakeInvoiceRepo`, `FakeClientRepo`, mock/fake `IInvoiceExporter`, and
    a `LocalFileSystemBlobStorage` (temp directory)
  - Test `IssueInvoiceAsync`: given a valid client nickname and amount, creates
    an invoice, exports PDF, stores in blob storage, returns the invoice and
    file path
  - Test `IssueInvoiceAsync` with non-existing client: throws
  - Test `CorrectInvoiceAsync`: given an existing invoice number, updates the
    invoice content, re-exports PDF, returns updated invoice
  - Test `CorrectInvoiceAsync` with non-existing invoice: throws
  - Test `CorrectInvoiceAsync` with invalid date change: throws

### Implementation

- [x] **7.2** Update `InvoiceManagement` class:
  - Constructor takes: `IInvoiceRepo`, `IClientRepo`, `IInvoiceExporter`,
    `InvoiceHtmlTemplate`, `IBlobStorage`, seller `BillingAddress` and
    `BankTransferInfo` (from config)
  - `IssueInvoiceAsync(string clientNickname, int amountCents, DateTime? date)`:
    look up client, build `InvoiceContent` with seller address and bank transfer
    info from config, buyer address from client, create invoice via repo, export
    PDF, upload to blob storage, return `(Invoice, string pdfPath)`
  - `CorrectInvoiceAsync(string invoiceNumber, int? amountCents, DateTime? date)`:
    get existing invoice, build updated content, update via repo, re-export PDF,
    upload to blob storage, return updated invoice
  - `ListInvoicesAsync(int limit)`: delegate to repo `LatestAsync`
- [x] **7.3** Add `IClientRepo` project reference to `Accounting.csproj` (it
      currently only references `Invoices`; add `Utilities` if needed for
      `QueryResult`)
- [x] **7.4** Run service layer tests:
      `dotnet test Accounting.Tests --filter InvoiceManagementTest`

---

## Phase 8: Logging

Add logging throughout all non-test implementations.

- [x] **8.1** Create logging decorator/wrapper pattern. For each interface
      implementation that should be logged, create a logging wrapper:
  - `LoggingInvoiceRepo : IInvoiceRepo` -- wraps an inner `IInvoiceRepo`, logs
    method name + invoice number on each call
  - `LoggingClientRepo : IClientRepo` -- wraps an inner `IClientRepo`, logs
    method name + client nickname
  - `LoggingInvoiceExporter : IInvoiceExporter` -- wraps inner exporter, logs
    invoice number
  - `LoggingBlobStorage : IBlobStorage` -- wraps inner storage, logs bucket +
    object key
  - `LoggingConfigLoader : IConfigLoader` -- wraps inner loader, logs on load
  - Each wrapper takes an `ILogger` in its constructor
  - Log at `Info` level for normal operations, `Error` for exceptions (then
    re-throw)
- [x] **8.2** Place logging wrappers in each project alongside the interface they
      wrap (e.g. `Invoices/LoggingInvoiceRepo.cs`, `Accounting/LoggingClientRepo.cs`,
      `Storage/LoggingBlobStorage.cs`)
- [x] **8.3** Write minimal tests for logging wrappers (verify they delegate
      correctly and don't swallow exceptions)
- [x] **8.4** In `CliProgram.Main`, set up logging:
  - Create `FileLogger` pointing to `{LogDirectory}/spark3dent.log`
  - Wrap with `BufferedLogger` for performance
  - Wrap all dependencies with their logging decorators

---

## Phase 9: CLI Wiring

Wire everything together in the CLI with a command loop.

### Setup & Command Loop

- [ ] **9.1** In `CliProgram.Main`, after config loading and dependency setup:
  - Initialize `AppDbContext`, run migrations/`EnsureCreated()`
  - Create `SqliteInvoiceRepo`, `SqliteClientRepo`
  - Create `LocalFileSystemBlobStorage`, define bucket for invoices PDFs
  - Create `BgAmountTranscriber`, load `InvoiceHtmlTemplate`
  - Create `InvoicePdfExporter`
  - Create `InvoiceManagement`
  - Wrap all with logging decorators
- [ ] **9.2** Implement the command loop:
  - If `args` are provided, parse and execute as a single command, then exit
  - Otherwise, enter interactive mode: display prompt, read line, parse command
    and arguments, execute, repeat until `exit`
  - Wrap command execution in try/catch: log errors and display user-friendly
    message, continue loop (don't crash)

### Commands

- [ ] **9.3** Implement `help` command:
  - Display available commands with brief descriptions and parameter formats
- [ ] **9.4** Implement `exit` command:
  - Dispose loggers, exit cleanly
- [ ] **9.5** Implement `clients add` command:
  - Prompt for: nickname, company name, representative name, company identifier
    (EIK/Bulstat), VAT identifier (optional), address, city, postal code, country
  - Validate nickname is non-empty
  - Call `IClientRepo.AddAsync`
  - Display confirmation
- [ ] **9.6** Implement `clients edit` command:
  - Prompt for nickname, fail if client not found
  - Display current values, prompt for each field (empty = keep current)
  - Call `IClientRepo.UpdateAsync`
  - Display confirmation
- [ ] **9.7** Implement `clients list` command:
  - Call `IClientRepo.ListAsync` with a reasonable limit (e.g. 20)
  - Display clients sorted alphabetically by nickname in a table format
- [ ] **9.8** Implement `invoices issue` command:
  - Parse: `invoices issue <client nickname> <amount> [date]`
  - Parse amount: handle both `.` and `,` as decimal separator, convert to cents
  - Parse date: optional, format `dd-MM-yyyy`, default to today
  - Bank transfer info (IBAN, bank name, BIC) for the pay-grid comes from config
  - Call `InvoiceManagement.IssueInvoiceAsync`
  - Display created invoice number and PDF file path
- [ ] **9.9** Implement `invoices correct` command:
  - Parse: `invoices correct <invoice number> <amount> [date]`
  - Parse amount and date as above
  - Call `InvoiceManagement.CorrectInvoiceAsync`
  - Display updated invoice info
- [ ] **9.10** Implement `invoices list` command:
  - Call `InvoiceManagement.ListInvoicesAsync`
  - Display invoices in a table: number, date, buyer name, total amount
  - Sorted by date, newest first

### CLI Tests (Optional / Manual)

- [ ] **9.11** Manually test the full CLI flow:
  - Add a client
  - Issue an invoice for that client
  - List invoices
  - Correct an invoice
  - List invoices again
  - Verify PDF was created in the blob storage path
  - Verify log file was created and contains entries

---

## Phase 10: Final Verification

- [ ] **10.1** Run the full test suite: `dotnet test Spark3Dent.sln`
- [ ] **10.2** Verify all tests pass (expected: contract tests pass for both fakes
      and SQLite implementations)
- [ ] **10.3** Run the CLI end-to-end with a clean database
- [ ] **10.4** Review log output for completeness and readability
- [ ] **10.5** Review generated PDF invoices for correctness
- [ ] **10.6** Clean up any remaining TODO comments in the codebase that have been
      addressed

---

## Dependency Graph (Implementation Order)

```
Phase 0: Project Setup
    |
    v
Phase 1: Configuration ----+
    |                       |
    v                       v
Phase 2: BgAmountTranscriber    Phase 4: Fake Repos
    |                               |
    v                               v
Phase 3: InvoiceHtmlTemplate    Phase 5: Database Layer (SQLite)
    |                               |
    +----------- + -----------------+
                 |
                 v
          Phase 6: PDF Export
                 |
                 v
          Phase 7: Service Layer (InvoiceManagement)
                 |
                 v
          Phase 8: Logging
                 |
                 v
          Phase 9: CLI Wiring
                 |
                 v
          Phase 10: Final Verification
```

**Note:** Phases 2 and 4 can be worked on in parallel. Phase 3 depends on Phase 2
(needs `BgAmountTranscriber`). Phase 5 depends on Phase 4 (inherits contract tests).
Phase 6 depends on Phase 3. Phase 7 depends on Phases 4, 5, and 6. Phase 8 can
start as soon as there are implementations to wrap. Phase 9 depends on everything.

---

## File Change Summary

| Action | Path | Description |
|--------|------|-------------|
| Create | `Database/Database.csproj` | New project for EF Core + SQLite |
| Create | `Database/AppDbContext.cs` | EF Core DbContext |
| Create | `Database/Entities/*.cs` | EF entity classes |
| Create | `Database/SqliteInvoiceRepo.cs` | SQLite invoice repository |
| Create | `Database/SqliteClientRepo.cs` | SQLite client repository |
| Create | `Database.Tests/Database.Tests.csproj` | Test project for Database |
| Create | `Database.Tests/SqliteInvoiceRepoTest.cs` | SQLite invoice repo tests |
| Create | `Database.Tests/SqliteClientRepoTest.cs` | SQLite client repo tests |
| Create | `Configuration.Tests/Configuration.Tests.csproj` | Test project for Configuration |
| Create | `Configuration.Tests/JsonAppSettingsLoaderTest.cs` | Config loader tests |
| Create | `Cli/appsettings.json` | Default application configuration |
| Modify | `Configuration/Config.cs` | Add `SellerAddress` and `SellerBankTransferInfo` to `AppConfig` |
| Modify | `Configuration/Configuration.csproj` | Add NuGet packages |
| Modify | `Configuration/JsonAppSettingsLoader.cs` | Implement config loading |
| Modify | `Invoices/Invoices.csproj` | Add HtmlAgilityPack + PuppeteerSharp |
| Modify | `Invoices/BgAmountTranscriber.cs` | Implement Bulgarian transcription |
| Modify | `Invoices/InvoiceHtmlTemplate.cs` | Implement template load + render |
| Modify | `Invoices/InvoicePdfExporter.cs` | Implement PDF export |
| Modify | `Invoices.Tests/Invoices.Tests.csproj` | Add HtmlAgilityPack |
| Modify | `Invoices.Tests/BgAmountTranscriberTest.cs` | Complete test cases + bodies |
| Modify | `Invoices.Tests/InvoiceHtmlTemplateTest.cs` | Implement all test methods |
| Modify | `Invoices.Tests/Fakes/FakeInvoiceRepo.cs` | Implement in-memory repo |
| Modify | `Invoices.Tests/Fakes/FakeInvoiceRepoTest.cs` | Implement test fixture |
| Modify | `Accounting/InvoiceManagement.cs` | Implement service layer |
| Modify | `Accounting/Accounting.csproj` | Add project references |
| Modify | `Accounting.Tests/ClientRepoContractTest.cs` | Write all contract tests |
| Modify | `Accounting.Tests/Fakes/FakeClientRepo.cs` | Implement in-memory repo |
| Modify | `Accounting.Tests/Fakes/FakeClientRepoTest.cs` | Implement test fixture |
| Create | `Accounting.Tests/InvoiceManagementTest.cs` | Service layer tests |
| Modify | `Cli/Cli.csproj` | Add project references + appsettings copy |
| Modify | `Cli/CliProgram.cs` | Full CLI implementation |
| Create | `Invoices/LoggingInvoiceRepo.cs` | Logging wrapper |
| Create | `Accounting/LoggingClientRepo.cs` | Logging wrapper |
| Create | `Invoices/LoggingInvoiceExporter.cs` | Logging wrapper |
| Create | `Storage/LoggingBlobStorage.cs` | Logging wrapper |
| Modify | `Spark3Dent.sln` | Add new projects |

---

*End of plan.*
