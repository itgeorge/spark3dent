# License & Attribution Review TODO

**Purpose:** This file is a handoff for an agent that runs periodically to update the licenses and attribution notices in this repository.

**Action:** Review the current repository state and:

1. **Audit dependencies** – List all NuGet packages (and any other third-party components) across all projects.
2. **Identify missing licenses** – For each dependency, determine its license and whether we have a corresponding entry in `licenses/`.
    2.1 **Also identify redundant licenses** – For each license, check if we do indeed still need that license, or we can remove it because we've removed the dependency
3. **Add or update license files** – Create or update files in `licenses/` with the full license text for each third-party component. Use `licenses/<PackageId>.txt` (e.g. `UglyToad.PdfPig.txt`).
4. **Check NOTICE/attribution requirements** – Some packages (especially Apache 2.0) require including their NOTICE or attribution. Ensure we comply.
5. **Update this file** – After each review run, add a dated entry below recording what was done.

---

**Review log:**

| Date       | Notes |
|------------|-------|
| 2025-02-18 | Initial review: Audited 14 .csproj files; identified 13 unique NuGet packages. Created `licenses/` with license files for all: HtmlAgilityPack, PuppeteerSharp, UglyToad.PdfPig (Apache 2.0, incl. EXTERNAL COMPONENTS attribution), Microsoft.Data.Sqlite, Microsoft.EntityFrameworkCore.Sqlite, Microsoft.EntityFrameworkCore.Design, Microsoft.Extensions.Configuration{,.Binder,.Json,.EnvironmentVariables}, Microsoft.NET.Test.Sdk, NUnit, NUnit3TestAdapter. All MIT except PdfPig (Apache 2.0). No redundant licenses (folder was empty). PdfPig attribution satisfied via full license text. |
