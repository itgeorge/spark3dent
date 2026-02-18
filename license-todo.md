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
| (pending)  | Initial review – add entries as reviews are completed |
