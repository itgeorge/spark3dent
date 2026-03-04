# Legacy Invoice Integration Tests

Integration tests that use real PDF invoice files to verify `LegacyPdfParser` against actual data.

**These tests are NOT part of CI** – they require real invoice PDFs which may contain sensitive data.

## Setup

1. Create the folder `Invoices/test-data/legacy-invoices/` (it is gitignored).
2. Copy sample PDF invoices into that folder.
3. For each PDF you want to test, add a matching `.expected.txt` file with the same base name (e.g. `invoice.pdf` → `invoice.expected.txt`).

### Expected file format

Key=value lines (one per line). Supported keys:

| Key | Example |
|-----|---------|
| Number | `106` |
| Date | `2024-01-15` or `01.02.2024` |
| TotalCents | `27000` |
| Currency | `Bgn` |
| Recipient.Name | `Company EOOD` |
| Recipient.CompanyIdentifier | `123456789` |

Lines starting with `#` are ignored. Only PDFs that have a `.expected.txt` file are tested.

## Running

```bash
dotnet test Invoices.Tests --filter "LegacyImportIntegration"
```

If the folder does not exist or no PDFs have `.expected.txt` files, the tests are skipped.

**Do not commit PDF files** – they may contain sensitive business data.
