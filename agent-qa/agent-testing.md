# Spark3Dent CLI -- Agent QA Testing Playbook

This document instructs an AI agent to perform end-to-end manual QA of the
Spark3Dent CLI application. It complements the automated test suites by exercising
the full published binary against a real SQLite database, real file system blob
storage, and real Chromium-based PDF/PNG rendering.

**Goal:** Verify that all CLI commands work correctly on a clean deployment, that
outputs are sensible, and that no regressions have been introduced. Report results.

All manual testing commands use the **QaHarness** project, which handles
staging, configuration, stdin piping, file inspection, and cleanup. Every
command is a `dotnet run` invocation -- no shell-specific scripting required.

---

## 1. Environment Setup

### 1.1 Run Automated Tests First

Before any manual testing, run the full automated test suite from the project
root (the directory containing `Spark3Dent.sln`):

```
dotnet test Spark3Dent.sln
```

If any tests fail, **stop here** -- do not proceed to manual testing. Report
the failures as described in section 5.

### 1.2 Stage a Clean Deployment

From the project root, run:

```
dotnet run --project QaHarness -- stage
```

This single command:
- Creates a fresh temporary staging directory.
- Runs `dotnet publish Cli/Cli.csproj -c Release` into it.
- Patches `appsettings.json` so database, blob storage, and logs live inside the
  staging directory (preserving Cyrillic/UTF-8 text in the App section).
- Verifies that `Cli.dll`, `appsettings.json`, and `chromium/` are present.
- Saves the staging directory path to a state file so subsequent commands find it.

Verify the command exits with code 0 and prints "Staging ready: ...".

### 1.3 CLI Runner

All CLI commands below are run through the harness `run` subcommand:

```
dotnet run --project QaHarness -- run <cli-args...>
```

For interactive commands that require piped stdin (like `clients add` and
`clients edit`), use the `--stdin-file` option to pipe input from a predefined
UTF-8 text file in `QaHarness/testdata/`:

```
dotnet run --project QaHarness -- run --stdin-file <filename> <cli-args...>
```

The harness reads the file, pipes its content to the CLI's stdin, then closes
stdin. One line per prompted field. Empty lines leave that field at its default.

The harness prints the CLI's stdout, then any stderr (prefixed with `STDERR:`),
then a final line `EXIT_CODE: <n>`. Check this exit code line after every
command (should be 0 for all non-error scenarios).

### 1.4 Predefined Test Data Files

All interactive input is stored in `QaHarness/testdata/`. These files contain
Bulgarian-style company names, addresses, representative names, and EIK numbers
appropriate for a Bulgarian dental lab invoicing tool. The files are:

| File                       | Purpose                                                    |
|----------------------------|------------------------------------------------------------|
| `client-add-1.txt`        | Add client **dental-pro** (Дентал Про ЕООД, no VAT)       |
| `client-add-2.txt`        | Add client **smile-studio** (Смайл Студио ООД, with VAT)  |
| `client-add-3.txt`        | Add client **zdravko-dent** (Здравко Дентал ЕООД, no VAT)  |
| `client-edit-1.txt`       | Edit **dental-pro** (change company name and address only) |
| `client-add-duplicate.txt`| Add with nickname **dental-pro** again (triggers error)    |

Each file has one field per line in the order the CLI prompts:
nickname, company name, representative, EIK, VAT (or empty), address, city,
postal code, country.

---

## 2. Test Scenarios

### 2.1 Help

```
dotnet run --project QaHarness -- run help
```

Verify the output lists all available commands with brief descriptions. The list
should include at minimum: `help`, `exit`, `clients add`, `clients edit`,
`clients list`, `invoices issue`, `invoices correct`, `invoices list`.

### 2.2 Client Management

#### Add three clients

```
dotnet run --project QaHarness -- run --stdin-file client-add-1.txt clients add
dotnet run --project QaHarness -- run --stdin-file client-add-2.txt clients add
dotnet run --project QaHarness -- run --stdin-file client-add-3.txt clients add
```

For each, verify:
- stdout contains a success confirmation mentioning the nickname.
- `EXIT_CODE: 0`.
- `client-add-1.txt` and `client-add-3.txt` have no VAT; `client-add-2.txt`
  has VAT identifier `BG987654321`.

#### List clients

```
dotnet run --project QaHarness -- run clients list
```

Verify:
- Output is a table with columns for nickname, company, and representative.
- All three clients appear: `dental-pro`, `smile-studio`, `zdravko-dent`.
- Clients are sorted alphabetically by nickname.

#### Edit a client

```
dotnet run --project QaHarness -- run --stdin-file client-edit-1.txt clients edit dental-pro
```

Verify:
- The command prints the current values before prompting.
- After editing, `clients list` reflects the updated company name
  ("Ново Име ЕООД") and address ("бул. Витоша 42").
- Fields left empty (nickname, representative, EIK, VAT, city, postal code,
  country) were preserved -- not blanked out.

#### Error: duplicate nickname

```
dotnet run --project QaHarness -- run --stdin-file client-add-duplicate.txt clients add
```

Verify the command outputs a user-friendly error message (not a crash/stack
trace) mentioning the duplicate nickname. `EXIT_CODE: 0`.

#### Error: non-existent client edit

```
dotnet run --project QaHarness -- run clients edit non-existent-client
```

Verify a user-friendly error message appears. `EXIT_CODE: 0`.
(No `--stdin-file` needed -- the error occurs before any prompts.)

### 2.3 Invoice Lifecycle

**Important:** Pass `--exportPng` on every `invoices issue` and
`invoices correct` command so that PNG images are generated alongside PDFs for
visual verification in section 2.6.

#### Issue invoices

Issue at least 3 invoices for different clients with varying amounts:

```
dotnet run --project QaHarness -- run invoices issue dental-pro 250.50 --exportPng
dotnet run --project QaHarness -- run invoices issue smile-studio 1899,99 --exportPng
dotnet run --project QaHarness -- run invoices issue zdravko-dent 75 --exportPng
```

For each, verify:
- stdout reports the created invoice number (sequential, starting from 1 on a
  fresh database).
- stdout reports a PDF file path under the staging blob storage directory.
- stdout reports a PNG file path (from the `--exportPng` flag).

Use `check-file` to verify each reported PDF exists and starts with `%PDF`:

```
dotnet run --project QaHarness -- check-file blobs/invoices/invoice-0000000001.pdf
dotnet run --project QaHarness -- check-file blobs/invoices/invoice-0000000002.pdf
dotnet run --project QaHarness -- check-file blobs/invoices/invoice-0000000003.pdf
```

#### List invoices

```
dotnet run --project QaHarness -- run invoices list
```

Verify:
- Output is a table with number, date, buyer name, and total columns.
- All issued invoices appear.
- The list is ordered newest first (highest invoice number at the top).
- Amounts match what was issued (250.50, 1899.99, 75.00).

#### Correct an invoice

```
dotnet run --project QaHarness -- run invoices correct 2 2100.00 --exportPng
```

Verify:
- stdout confirms the correction and shows the new total.
- `invoices list` reflects the updated amount for invoice 2.

#### Amount format variations

Issue additional invoices to verify the amount parser handles all formats:

```
dotnet run --project QaHarness -- run invoices issue dental-pro 0.50 --exportPng
dotnet run --project QaHarness -- run invoices issue smile-studio 200 --exportPng
```

These test small decimal amounts and whole numbers. The three initial invoices
already covered dot-decimal (`250.50`) and comma-decimal (`1899,99`).

#### Date ordering invariant

The system enforces that invoice dates are non-decreasing by invoice number.
To test this:
1. Note that invoices above were issued with today's date.
2. Try to issue an invoice with a date in the past:

```
dotnet run --project QaHarness -- run invoices issue dental-pro 100 01-01-2025
```

3. Verify the system rejects this with a user-friendly error (not a stack trace).
   `EXIT_CODE: 0`.

### 2.4 Error Handling

For each of the following, verify the CLI prints a user-friendly message
(`Error: ...`, `Invalid amount`, `Usage: ...`, etc.) rather than an unhandled
exception or stack trace. Check that `EXIT_CODE: 0` appears (it catches the
error gracefully):

```
dotnet run --project QaHarness -- run invoices issue non-existent-client 100
dotnet run --project QaHarness -- run invoices correct 99999 100
dotnet run --project QaHarness -- run invoices issue dental-pro -50
dotnet run --project QaHarness -- run invoices issue dental-pro abc
dotnet run --project QaHarness -- run invoices issue dental-pro 100 not-a-date
dotnet run --project QaHarness -- run invoices correct
dotnet run --project QaHarness -- run invoices issue
dotnet run --project QaHarness -- run clients
dotnet run --project QaHarness -- run nonsense
```

### 2.5 Log File Verification

After all the above scenarios, read the log file:

```
dotnet run --project QaHarness -- read-log
```

Verify:
- The output is non-empty.
- It contains `Spark3Dent started` entries (one per command invocation).
- It contains `Info` level entries for repo/exporter/storage operations
  (e.g. `InvoiceRepo.CreateAsync`, `ClientRepo.AddAsync`,
  `BlobStorage.UploadAsync`).
- It does NOT contain unhandled exception stack traces (errors from section 2.4
  should be logged as `Error` level entries with messages, but the app should
  not have crashed).

### 2.6 Invoice Image Verification

This replaces traditional PDF magic-byte checks with visual verification of
the rendered invoice content.

First, list all generated invoice PNGs:

```
dotnet run --project QaHarness -- invoice-images
```

This prints the absolute path and size for each PNG. Pick 2-3 of these images
and **open each one using the Read tool** (which supports image files). For each
image, visually verify it contains:
- The invoice number.
- The invoice date.
- Seller information (company name, EIK, address).
- Buyer information (company name, EIK, address).
- A line item description and the correct total amount.
- Bank transfer information (IBAN, bank name, BIC).

Flag any image that appears blank, truncated, or is missing expected data.

---

## 3. Test Verdict

After running all scenarios, classify the result:

- **GREEN**: All automated tests passed AND all manual scenarios produced the
  expected output shapes. No crashes, no stack traces in user-facing output, log
  file looks healthy, invoice images contain correct content.
- **RED**: Any automated test failed, any manual scenario produced unexpected
  results, any command crashed with an unhandled exception, or any artifact
  (DB, PDF/PNG, log) is missing or corrupted.

---

## 4. Cleanup (GREEN)

If the verdict is GREEN, clean up all staging artifacts:

```
dotnet run --project QaHarness -- cleanup
```

Report a brief summary:
- Number of scenarios run
- Number of clients created / invoices issued / invoices corrected
- Confirmation that invoice images were visually verified
- Confirmation that the log file was healthy
- The staging directory was cleaned up

---

## 5. Error Report (RED)

If the verdict is RED, do NOT clean up. Instead, produce a structured error
report containing:

### 5.1 Summary

One paragraph describing which scenario(s) failed and the nature of the
failure (wrong output, crash, missing file, etc.).

### 5.2 Reproduction Steps

For each failing scenario, list the exact `dotnet run --project QaHarness -- run ...`
command that was run and the full stdout/stderr output.

### 5.3 Automated Test Results

If `dotnet test` had failures, include the full test output.

### 5.4 Log Contents

```
dotnet run --project QaHarness -- read-log
```

Include the full output.

### 5.5 Database State

Note the database file size and path. Use `check-file` to report it:

```
dotnet run --project QaHarness -- check-file data/spark3dent.db
```

### 5.6 Staging Directory Contents

```
dotnet run --project QaHarness -- list-files
```

Include the output so the investigator can see what was and wasn't created.

### 5.7 Preserve Staging Directory

```
dotnet run --project QaHarness -- staging-dir
```

Print the staging directory path so the developer can inspect it manually.
Do NOT run `cleanup`.

### 5.8 Document Failures for Debug Agent

Create a failure report file that a different agent can use to investigate and fix
the issues. This file will be passed to the debug/fix agent as context.

**Step 1 — Create the file with staging context**

Run the harness `agenterrorreport` command. It creates `agent-qa/failures/<yyyyMMdd>-<hhmm>.md`
with a header and a **Staging Directory** section containing the path and a short
description. The command prints the full file path to stdout so the agent can pick
it up:

```
dotnet run --project QaHarness -- agenterrorreport
```

Or, to pass the staging directory explicitly (e.g. if state is unavailable):

```
dotnet run --project QaHarness -- agenterrorreport <staging-dir>
```

**Step 2 — Continue writing the description**

Use the printed file path and append (or edit) the following sections:

1. **Issue summary** — Which scenario(s) failed, exception type, exit code, and any retry behavior.
2. **Reproduction steps** — Exact command(s) that failed, full stdout/stderr (including stack trace), and execution context (staging path, command order, concurrency).
3. **Investigation** — Relevant code locations (file paths and line numbers from stack traces), model/schema details, and a hypothesized root cause.
4. **Data for debugging** — Automated test results, scenarios that passed, staging directory path, and links to relevant docs or prior issues.
5. **Suggested fix directions** — Concrete approaches to try (e.g. mutex, exception handling, migrations).
6. **Verification checklist** — Steps the fix agent should run to confirm the fix (e.g. re-run playbook, stress-test concurrency).

See `agent-qa/failures/20260221-0130.md` for a worked example.

---

## Notes for the Testing Agent

- **All interactive input comes from predefined files.** Use `--stdin-file`
  for `clients add` and `clients edit` commands. The test data files are in
  `QaHarness/testdata/` and contain UTF-8 encoded Bulgarian text. Do not
  attempt to pass Cyrillic or newline-containing text as shell arguments.
- **Non-interactive commands need no `--stdin-file`.** Invoice and error
  handling commands take all input as command-line arguments (ASCII-safe
  nicknames, numbers, dates, flags).
- **Check every `EXIT_CODE` line.** The harness always prints `EXIT_CODE: <n>`
  as the last line of output. A non-zero exit code from any normal command is a
  bug, even if the output looks fine.
- **Always pass `--exportPng`** on `invoices issue` and `invoices correct`
  commands so that PNG images are generated for visual verification.
- **Use `check-file` to verify artifacts.** Don't just trust that a path was
  printed; confirm the file exists and has sensible content.
- **Use the Read tool on PNG paths** from `invoice-images` to visually inspect
  the rendered invoices. This is the primary method for verifying PDF/invoice
  output quality.
- **The app needs Chromium for PDF/PNG export.** If the `chromium/` directory is
  missing from the publish output, export will attempt to download Chromium at
  runtime. This is acceptable but should be noted.
- **Each `run` invocation is a separate process.** The CLI runs one command per
  invocation, not a REPL session. Each call starts fresh, reads the DB, does its
  work, and exits.
- **When verdict is RED**, run `agenterrorreport` then continue writing the
  failure report per section 5.8 so another agent can debug and fix the issues.
