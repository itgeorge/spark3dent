# Invoice PDF Regression References

These PDFs are golden/snapshot references for `InvoicePdfExporterRegressionTest`. The tests compare export output against these files; if rendering changes (template, PuppeteerSharp, Chromium), the test fails so you can review why.

`generated.txt` records when the references were last regenerated (format `yyyy-MM-dd:HHmmss`).

## Regenerating references

After an intentional change (e.g. template update, PuppeteerSharp upgrade):

1. Set `REGENERATE_INVOICE_REFS=1` and run the regression tests:
   ```bash
   # PowerShell
   $env:REGENERATE_INVOICE_REFS="1"; dotnet test Invoices.Tests --filter InvoicePdfExporterRegressionTest

   # cmd / bash
   set REGENERATE_INVOICE_REFS=1 && dotnet test Invoices.Tests --filter InvoicePdfExporterRegressionTest
   ```
2. Clear the variable so the next test run does not overwrite references:
   ```bash
   # PowerShell
   Remove-Item Env:REGENERATE_INVOICE_REFS -ErrorAction SilentlyContinue

   # cmd
   set REGENERATE_INVOICE_REFS=

   # bash
   unset REGENERATE_INVOICE_REFS
   ```
3. Review the updated PDFs and `generated.txt` in this folder.
4. Commit the changes.
