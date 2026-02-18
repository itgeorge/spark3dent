# Spark3Dent CLI -- Agent QA Testing Playbook

This document instructs an AI agent to perform end-to-end manual QA of the
Spark3Dent CLI application. It complements the automated test suites by exercising
the full published binary against a real SQLite database, real file system blob
storage, and real Chromium-based PDF rendering.

**Goal:** Verify that all CLI commands work correctly on a clean deployment, that
outputs are sensible, and that no regressions have been introduced. Report results.

---

## 1. Environment Setup

### 1.1 Run Automated Tests First

Before any manual testing, run the full automated test suite:

```
dotnet test Spark3Dent.sln
```

If any tests fail, **stop here** -- do not proceed to manual testing. Report
the failures as described in section 5.

### 1.2 Publish a Clean Deployment

Publish a self-contained deployment to a temporary staging directory. The
staging directory MUST be a fresh temp folder so that no leftover state from
previous runs can interfere:

```powershell
$stagingDir = Join-Path ([System.IO.Path]::GetTempPath()) "spark3dent-qa-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
dotnet publish Cli/Cli.csproj -c Release -o "$stagingDir/app"
```

Verify the publish succeeded (exit code 0) and that the output directory
contains at minimum: `Cli.dll`, `appsettings.json`, and a `chromium/`
subdirectory.

### 1.3 Prepare Isolated State Directories

Override the config so all state lives inside the staging directory (not in the
user's real Documents/AppData). Edit `$stagingDir/app/appsettings.json` and set
the `Desktop` paths:

```json
{
  "Desktop": {
    "DatabasePath": "<stagingDir>/data/spark3dent.db",
    "BlobStoragePath": "<stagingDir>/blobs",
    "LogDirectory": "<stagingDir>/logs"
  }
}
```

Replace `<stagingDir>` with the actual absolute path. Keep the `App` section
unchanged (it contains real seller address and bank info needed for invoice
rendering).

### 1.4 Define the CLI Runner

All commands below are run via:

```
dotnet <stagingDir>/app/Cli.dll -- <args...>
```

For interactive-mode commands that require piped stdin (like `clients add`),
pipe the input values as newline-separated text. For single-command mode
commands, pass everything as arguments.

---

## 2. Test Scenarios

For every scenario below, **invent realistic test data yourself**. Use
Bulgarian-style company names, addresses, representative names, and EIK numbers
where appropriate -- this is a Bulgarian dental lab invoicing tool. Vary the
data between scenarios; do not reuse the exact same values everywhere.

After each command, check the **exit code** (should be 0 for all non-error
scenarios) and scan the **stdout** for the expected output shape described below.

### 2.1 Help

Run the `help` command. Verify the output lists all available commands with
brief descriptions. The list should include at minimum: `help`, `exit`,
`clients add`, `clients edit`, `clients list`, `invoices issue`,
`invoices correct`, `invoices list`.

### 2.2 Client Management

#### Add multiple clients

Add at least 3 clients with distinct nicknames. For each:
- Pipe all required fields (nickname, company name, representative name,
  company identifier, VAT identifier or empty line, address, city, postal code,
  country) as newline-separated stdin.
- Verify stdout contains a success confirmation mentioning the nickname.

Make at least one client **without** a VAT identifier (empty line for that
field) and at least one **with** a VAT identifier.

#### List clients

Run `clients list`. Verify:
- Output is a table with columns for nickname, company, and representative.
- All clients just added appear in the list.
- Clients are sorted alphabetically by nickname.

#### Edit a client

Pick one of the added clients. Run `clients edit <nickname>` and pipe new
values for some fields (change the company name and address, keep others by
sending empty lines). Verify:
- The command prints the current values before prompting.
- After editing, `clients list` reflects the updated fields.
- Fields left empty were preserved (not blanked out).

#### Error: duplicate nickname

Try adding a client with an already-used nickname. Verify the command outputs
an error message (not a crash/stack trace) mentioning the duplicate.

#### Error: non-existent client edit

Try editing a client with a nickname that doesn't exist. Verify a user-friendly
error message appears.

### 2.3 Invoice Lifecycle

#### Issue invoices

Issue at least 3 invoices for different clients with varying amounts. Use:

```
invoices issue <nickname> <amount>
```

For at least one invoice, supply an explicit date:

```
invoices issue <nickname> <amount> <dd-MM-yyyy>
```

For each, verify:
- stdout reports the created invoice number (sequential, starting from 1 on a
  fresh database).
- stdout reports a PDF file path under the staging blob storage directory.
- The reported PDF file **actually exists** on disk.
- The PDF file is non-empty and starts with the `%PDF` magic bytes (read the
  first 4 bytes of the file).

#### List invoices

Run `invoices list`. Verify:
- Output is a table with number, date, buyer name, and total columns.
- All issued invoices appear.
- The list is ordered newest first (highest invoice number at the top).
- Amounts match what was issued.

#### Correct an invoice

Pick one invoice and correct it with a different amount:

```
invoices correct <number> <new amount>
```

Verify:
- stdout confirms the correction and shows the new total.
- `invoices list` reflects the updated amount.
- The PDF file for that invoice was regenerated (check that its modification
  timestamp is recent, or re-read the `%PDF` header to confirm it's still valid).

#### Amount format variations

Verify the amount parser handles:
- Dot as decimal separator: `150.50`
- Comma as decimal separator: `150,50`
- Whole numbers: `200`
- Small amounts: `0.50`

Issue or correct invoices using each format and verify they succeed.

#### Date ordering invariant

The system enforces that invoice dates are non-decreasing by invoice number.
To test this:
1. Issue an invoice with today's date (or leave date defaulting to today).
2. Try to issue another invoice with a date in the past (e.g. a year ago).
3. Verify the system rejects this with a user-friendly error (not a stack trace).

### 2.4 Error Handling

For each of the following, verify the CLI prints a user-friendly `Error: ...`
message (not an unhandled exception or stack trace) and exits with code 0 (it
catches the error gracefully):

- `invoices issue <non-existent-nickname> 100` -- client not found.
- `invoices correct 99999 100` -- invoice not found.
- `invoices issue <nickname> -50` -- invalid (negative) amount.
- `invoices issue <nickname> abc` -- unparseable amount.
- `invoices issue <nickname> 100 not-a-date` -- unparseable date.
- `invoices correct` -- missing arguments (should print usage).
- `invoices issue` -- missing arguments (should print usage).
- `clients` -- missing subcommand (should print usage).
- `nonsense` -- unknown top-level command.

### 2.5 Log File Verification

After all the above scenarios, read the log file at
`<stagingDir>/logs/spark3dent.log`. Verify:
- The file exists and is non-empty.
- It contains `Spark3Dent started` entries (one per command invocation).
- It contains `Info` level entries for repo/exporter/storage operations
  (e.g. `InvoiceRepo.CreateAsync`, `ClientRepo.AddAsync`,
  `BlobStorage.UploadAsync`).
- It does NOT contain unhandled exception stack traces (errors from section 2.4
  should be logged as `Error` level entries with messages, but the app should
  not have crashed).

### 2.6 PDF Sanity Check

Pick 2-3 of the generated PDF files and verify:
- Each file is > 5 KB (a real rendered invoice is not trivially small).
- Each file starts with `%PDF` (first 4 bytes).
- If the agent has browser capabilities: open one PDF and verify it contains
  recognizable invoice content (invoice number, date, seller/buyer info, amount
  table). This is optional but valuable.

---

## 3. Test Verdict

After running all scenarios, classify the result:

- **GREEN**: All automated tests passed AND all manual scenarios produced the
  expected output shapes. No crashes, no stack traces in user-facing output, log
  file looks healthy, PDFs are valid.
- **RED**: Any automated test failed, any manual scenario produced unexpected
  results, any command crashed with an unhandled exception, or any artifact
  (DB, PDF, log) is missing or corrupted.

---

## 4. Cleanup (GREEN)

If the verdict is GREEN, clean up all staging artifacts:

```powershell
Remove-Item -Recurse -Force "$stagingDir"
```

Report a brief summary:
- Number of scenarios run
- Number of clients created / invoices issued / invoices corrected
- Confirmation that all PDFs were valid
- Confirmation that the log file was healthy
- The staging directory was cleaned up

---

## 5. Error Report (RED)

If the verdict is RED, do NOT delete the staging directory. Instead, produce
a structured error report containing:

### 5.1 Summary

One paragraph describing which scenario(s) failed and the nature of the
failure (wrong output, crash, missing file, etc.).

### 5.2 Reproduction Steps

For each failing scenario, list the exact command that was run and the full
stdout/stderr output.

### 5.3 Automated Test Results

If `dotnet test` had failures, include the full test output.

### 5.4 Log Contents

Include the full contents of `<stagingDir>/logs/spark3dent.log`.

### 5.5 Database State

Run the following to dump the database state for investigation:

```powershell
dotnet tool install --global dotnet-ef 2>$null
# Or alternatively, use sqlite3 directly if available:
# sqlite3 "<stagingDir>/data/spark3dent.db" ".dump"
```

If neither tool is available, just note the file size and path.

### 5.6 Staging Directory Contents

List all files in the staging directory tree (`Get-ChildItem -Recurse`) so the
investigator can see what was and wasn't created.

### 5.7 Preserve Staging Directory

Print the staging directory path so the developer can inspect it manually.
Do NOT delete it.

---

## Notes for the Testing Agent

- **Be creative with test data.** The scenarios above describe intentions, not
  exact values. Invent plausible Bulgarian dental lab clients, realistic invoice
  amounts (tens to thousands of euros), and reasonable dates.
- **Pipe stdin for interactive commands.** `clients add` and `clients edit`
  prompt interactively. Provide all answers via piped newline-separated text.
- **Check every exit code.** A non-zero exit code from any normal command is a
  bug, even if stdout looks fine.
- **Read files to verify artifacts.** Don't just trust that a path was printed;
  confirm the file exists and has sensible content.
- **The app needs Chromium for PDF export.** If the `chromium/` directory is
  missing from the publish output, the PDF export will attempt to download
  Chromium at runtime. This is acceptable for testing but should be noted.
- **Each command invocation is a separate process.** The CLI runs one command
  per invocation (args mode), not a REPL session. Each `dotnet Cli.dll -- ...`
  call starts fresh, reads the DB, does its work, and exits.
- **PowerShell quoting.** When piping multi-line input, use here-strings:
  ```powershell
  @"
  line1
  line2
  "@ | dotnet "$stagingDir/app/Cli.dll" -- clients add
  ```
