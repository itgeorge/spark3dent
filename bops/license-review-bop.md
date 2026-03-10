# License & Attribution Review TODO

**Purpose:** This file is a handoff for an agent that runs periodically to update the licenses and attribution notices in this repository.

**Action:** Review the current repository state and:

1. **Audit dependencies** – List all NuGet packages (and any other third-party components) across all projects.
2. **Identify missing licenses** – For each dependency, determine its license and whether we have a corresponding entry in `<repo root>/licenses/`.
    2.1 **Also identify redundant licenses** – For each license, check if we do indeed still need that license, or we can remove it because we've removed the dependency
3. **Add or update license files** – Create or update files in `<repo root>/licenses/` with the full license text for each third-party component. Use `<PackageId>.txt` (e.g. `UglyToad.PdfPig.txt`).
4. **Check NOTICE/attribution requirements** – Some packages (especially Apache 2.0) require including their NOTICE or attribution. Ensure we comply.
5. **Update this file** – After each review run, add a dated entry below recording what was done.

### Embedded resources and Licenses page

License files in `<repo root>/licenses/` are embedded in the Web project via `..\licenses\*.txt` in [Web/Web.csproj](../Web/Web.csproj). The Licenses page at `/licenses` displays them.

- **When adding a dependency:** Create `licenses/<PackageId>.txt` with the full license text. It will be embedded and shown on the Licenses page automatically.
- **When removing a dependency:** Delete the corresponding `licenses/<PackageId>.txt` file. It will no longer be embedded or shown.
- Then run tests, especially [Web.Tests/StartupConfigTests.cs](../Web.Tests/StartupConfigTests.cs) to ensure the licenses are embedded correctly and the Licenses page displays them.

---

**Review log:**

| Date       | Notes |
|------------|-------|
| 2025-02-18 | Initial review: Audited 14 .csproj files; identified 13 unique NuGet packages. Created `licenses/` with license files for all: HtmlAgilityPack, PuppeteerSharp, UglyToad.PdfPig (Apache 2.0, incl. EXTERNAL COMPONENTS attribution), Microsoft.Data.Sqlite, Microsoft.EntityFrameworkCore.Sqlite, Microsoft.EntityFrameworkCore.Design, Microsoft.Extensions.Configuration{,.Binder,.Json,.EnvironmentVariables}, Microsoft.NET.Test.Sdk, NUnit, NUnit3TestAdapter. All MIT except PdfPig (Apache 2.0). No redundant licenses (folder was empty). PdfPig attribution satisfied via full license text. |
| 2025-02-19 | Added SixLabors.ImageSharp (Invoices.Tests) – Six Labors Split License v1.0; qualifies for Apache 2.0 under open-source use. Created `licenses/SixLabors.ImageSharp.txt`. No NOTICE required. |
| 2025-02-25 | Audited 19 .csproj files; identified 15 unique NuGet packages. Added `licenses/Microsoft.AspNetCore.Mvc.Testing.txt` (MIT) – used by Web.Tests for integration testing. All 14 existing licenses still required; no redundant licenses. No NOTICE/attribution changes. |
| 2025-03-05 | Added Inter and Cascadia Code fonts (OFL-1.1) for bundled invoice PDF export. Created `licenses/Inter.txt` (rsms/inter) and `licenses/CascadiaCode.txt` (microsoft/cascadia-code). Both fonts embedded in Invoices project for offline PDF generation. |
