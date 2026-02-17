# Dental Lab Toolset -- Database & Persistence Architecture

*Last updated: 2026-02-16 20:40:11 UTC*

------------------------------------------------------------------------

## 1. Architectural Overview

This project starts as a **desktop application** using a local database
and file system, with a planned future migration to a **cloud-based
deployment (Cloud Run)**.

To support this evolution, we made the following architectural
decisions:

-   Use **SQLite + EF Core** for local persistence.
-   Abstract all database operations behind repository interfaces.
-   Abstract file storage behind `IBlobStorage`.
-   Persist invoice numbering using a dedicated **sequence table**.
-   Ensure all invoice numbering and date validation is **atomic and
    transactional**.
-   Design the system so that migration to **PostgreSQL + Cloud
    Storage** later is straightforward.

The application must remain safe, deterministic, and concurrency-aware
from the start.

------------------------------------------------------------------------

## 2. Technology Decisions

### 2.1 Database

-   Local: **SQLite**
-   ORM: **EF Core**
-   Cloud future: **PostgreSQL (Cloud SQL)**

SQLite is chosen because: - Single-file database - ACID compliant -
Supports transactions - Easy migration path to PostgreSQL - Works
cleanly with EF Core

------------------------------------------------------------------------

## 3. Repository Responsibilities

Repositories are responsible for:

-   Atomic invoice number allocation
-   Enforcing business invariants
-   Managing transactions
-   Persisting entities
-   Preventing invalid state transitions

Application layer must NOT: - Generate invoice numbers - Manually
compute MAX() and assign numbers - Bypass repository logic

All write operations must go through repositories.

------------------------------------------------------------------------

## 4. Invoice Numbering Design

### 4.1 Core Rule

Invoice numbers: - Are strictly increasing integers - Increment by +1 -
Are never reused - Are assigned ONLY by the repository

### 4.2 Sequence Table

Create a dedicated table:

    InvoiceSequence
    ---------------
    Id (PK, always = 1)
    LastNumber (int, not null)

This table persists the last allocated invoice number.

The database is the **single source of truth**.

------------------------------------------------------------------------

## 5. Initialization Behavior

The `SqliteInvoiceRepo` repository must ensure the sequence table is initialized exactly
once.

### On first use:

1.  Start a write transaction.
2.  Check if `InvoiceSequence` row with `Id = 1` exists.
3.  If not:
    -   Set `LastNumber = Config.StartInvoiceNumber - 1`
    -   Insert row.
4.  Commit.

`Config.StartInvoiceNumber`: - Comes from `IConfigLoader.Load()` - Is
only used during initial DB setup - Is never consulted again once the
sequence row exists

If config changes later, it does NOT affect numbering.

NOTE: this numbering behavior should be tested in a `SqliteInvoiceRepoTest` class which inherits `InvoiceRepoContractTest` - that will combine the repo-specific behavior test with the general contract for the repository.

------------------------------------------------------------------------

## 6. Invoice Creation (Atomic Operation)

`CreateAsync(InvoiceContent content)` must:

1.  Begin a transaction.
2.  Read `InvoiceSequence` row.
3.  Validate invoice date (see section 7).
4.  Increment `LastNumber`.
5.  Save updated sequence row.
6.  Insert new Invoice with assigned Number.
7.  Commit.

Add a **unique index** on `Invoice.Number` for safety.

All number allocation must happen inside the transaction.

------------------------------------------------------------------------

## 7. Invoice Date Invariant

Business rule:

If invoice A has a higher number than invoice B, then A.InvoiceDate must
be \>= B.InvoiceDate.

Dates must be non-decreasing with invoice number.

### 7.1 On Create

Inside the same transaction:

-   Query the invoice with highest number.
-   Validate: `newInvoiceDate >= lastInvoiceDate`

Reject if violated.

### 7.2 On Edit

When updating an invoice's date:

-   Fetch previous invoice (max Number \< current)
-   Fetch next invoice (min Number \> current)

Validate: - newDate \>= previousDate (if exists) - newDate \<= nextDate
(if exists)

All inside a transaction.

Use `DateOnly` for invoice dates to avoid timezone issues.

------------------------------------------------------------------------

## 8. Concurrency Rules

-   Never share DbContext across threads.
-   Each repository operation uses its own DbContext instance.
-   All create/update operations use transactions.
-   SQLite will serialize writes; transactions must be used correctly.

Optional hardening (future): - Add database triggers to enforce date
ordering at DB level.

------------------------------------------------------------------------

## 9. Database Schema Guidelines

Minimum tables:

-   Clients
-   Invoices
-   InvoiceLineItems
-   InvoiceSequence
-   (Optional) Documents metadata

Add: - Primary keys - Foreign keys - Unique index on Invoice.Number -
Index on Invoice.Number for ordering - Index on Invoice.InvoiceDate if
needed for reporting

Do NOT store PDF blobs in the database. Use `IBlobStorage` for file
storage.

------------------------------------------------------------------------

## 10. Testing Requirements

### 10.1 Initialization Tests

-   Fresh DB + StartInvoiceNumber=1 → first invoice = 1
-   Fresh DB + StartInvoiceNumber=1000 → first invoice = 1000
-   Changing config after creation does not affect numbering

### 10.2 Concurrency Tests

Using a temporary file-based SQLite database:

-   Spawn multiple parallel CreateAsync calls
-   Assert:
    -   All invoice numbers are unique
    -   Numbers are contiguous
    -   Count matches number of creates

### 10.3 Date Validation Tests

-   Cannot create invoice with date earlier than last invoice
-   Cannot edit invoice to violate ordering

------------------------------------------------------------------------

## 11. Cloud Migration Path

When migrating to Cloud Run:

-   Replace SQLite with PostgreSQL
-   Replace Local `IBlobStorage` with Cloud Storage implementation
-   Keep repository interfaces unchanged
-   Reuse sequence table design (works identically in PostgreSQL)

No architectural changes required.

------------------------------------------------------------------------

## 12. Summary of Key Decisions

-   SQLite + EF Core for local
-   PostgreSQL later
-   Repository allocates invoice numbers
-   Dedicated sequence table
-   Date invariant enforced transactionally
-   Unique constraint on invoice numbers
-   File storage abstracted
-   No business logic outside repositories
-   Config only used during initial DB bootstrap

------------------------------------------------------------------------

End of document.
