# Slice 13 Plan — Operator CLI Lab Bootstrap and Credential Secret Policy

*Created: 2026-06-07*

## Goal

Add an operator-only CLI bootstrap path for creating/resetting the singleton lab and first lab member, then remove the development/test startup identity seed from the Web app.

This slice also relaxes credential secret validation so users are not limited to six numeric digits. IAM UI may still generate a six-digit default secret, but the submitted secret may be custom.

## Product Decisions

- Lab remains singleton-like in the app, but its code/display name are operator-configurable through CLI bootstrap.
- If any lab already exists, bootstrap must refuse unless an explicit reset flag is supplied.
- With reset, replace/update the singleton lab code/name and first lab member data, revoke existing lab sessions, and preserve historical orders/audit.
- No API endpoint may perform bootstrap.
- Raw credential secrets are never stored. Store only `PinHasher` hash.
- Secret is shown/entered only at creation/reset time.
- Soft-deletion/deactivation remains the model for later IAM operations.

## Proposed CLI UX

Use the existing `Cli` project style. Suggested commands:

```bash
dotnet run --project Cli -- iam bootstrap-lab --db <path>
dotnet run --project Cli -- iam bootstrap-lab --db <path> --reset
```

Interactive prompts when flags are omitted:

- database path if not supplied,
- lab code, default `LAB`,
- lab display name,
- first member id, default `lab-1`,
- first member label,
- credential secret twice, hidden input if practical.

Non-interactive flags are acceptable if existing CLI patterns prefer them:

```bash
--lab-code LAB
--lab-name "Spark3Dent Lab"
--member-id lab-1
--member-label "Lab Admin"
--secret-stdin
--reset
```

Avoid putting raw secrets in command-line arguments because shell history can leak them. Prefer prompt or stdin.

## Reset Semantics

If no lab exists:

- create lab row,
- create first lab member,
- optionally audit `LabBootstrapped` with CLI/system actor metadata.

If a lab exists and `--reset` is absent:

- exit non-zero with clear message.

If a lab exists and `--reset` is present:

- update singleton lab code/display name,
- deactivate or replace existing lab members according to implementation simplicity,
- ensure the requested first member exists and has the new hash/label/active state,
- revoke existing lab sessions,
- audit `LabBootstrapReset` or `LabBootstrapped` with metadata showing reset.

Because lab code may change, update `SchedulingMembers` lab `OrganizationCode` and `SchedulingAuthSessions` lab `OrganizationCode` consistently or revoke/delete lab sessions so stale sessions cannot authenticate.

## Credential Secret Validation Change

Current `PinHasher.ValidatePinShape` may enforce exactly six numeric digits. Change it to accept custom secrets.

Suggested policy:

- trim? Prefer **do not trim** the secret itself; prompt/UI can trim accidental whitespace before submit if desired.
- minimum length: 6 characters,
- maximum length: 128 characters,
- reject all-whitespace,
- allow digits, letters, and symbols,
- error message should say `Invalid credential secret.` without revealing policy details in auth responses.

Tests should cover:

- six-digit generated secret still works,
- longer alphanumeric/custom secret works,
- too short fails,
- empty/whitespace fails,
- verification remains backward-compatible with existing six-digit hashes.

## Remove Web Startup Seed

Current `SchedulingIdentitySeed` seeds lab/clinic/member rows in Development/LanDev/Test.

After CLI bootstrap exists:

- remove production/dev Web startup seed path,
- delete `Web/SchedulingIdentitySeed.cs` or restrict it to tests only if truly necessary,
- tests should seed identity explicitly via test helpers or fixtures,
- document local dev bootstrap command.

If deleting the seed would make many tests too noisy in this slice, keep a test-only helper outside Web runtime and document the remaining seam. Do not leave automatic runtime seed in normal app startup.

## Audit

If audit infrastructure is available, append a CLI/system event:

- `ServiceName = "IAM"`
- `Operation = "LabBootstrapped"` or `"LabBootstrapReset"`
- `EntityType = "Lab"`
- `EntityId = lab code`
- actor fields null or synthetic system fields,
- metadata includes `{ "source": "cli", "reset": true|false, "memberId": "..." }`.

Never audit raw secrets.

## Files Expected to Change

Likely:

- `Cli/CliProgram.cs`
- `Orders/PinHasher.cs`
- `Orders.Tests/PinHasherTest.cs`
- `Database/SqliteSchedulingIdentityRepo.cs` or new write-capable identity repo methods
- `Orders/Repositories.cs`
- `Database/SqliteAuthSessionRepo.cs` if additional revocation helpers are needed
- `Web/WebProgram.cs`
- delete or modify `Web/SchedulingIdentitySeed.cs`
- `Web.Tests/ApiTestFixture.cs` and related test helpers
- tests in `Orders.Tests`, `Database.Tests`, `Web.Tests`
- docs/plans/deployment notes

## Tests to Add/Update

- CLI bootstrap creates lab/member in an empty DB.
- CLI bootstrap refuses when lab exists without `--reset`.
- CLI bootstrap with `--reset` updates lab/member and revokes lab sessions.
- Web app no longer auto-seeds identities in normal development/test startup; tests seed explicitly.
- Secret policy tests for custom secrets and old six-digit compatibility.
- Auth login works with custom secret.

## Manual Verification

1. Create empty local DB.
2. Run CLI bootstrap with custom lab code/name/member and custom secret.
3. Start Web app against DB.
4. Login as lab with custom secret.
5. Confirm Invoicer/Scheduler/IAM visible.
6. Run bootstrap again without `--reset`; confirm refusal.
7. Run bootstrap with `--reset`; confirm old session no longer authenticates and new secret works.

## Out of Scope

- IAM browser mutation UI.
- Clinic creation/editing.
- Member management beyond first lab bootstrap member.
- Capacity/lab settings UI.

## Implementation Checklist

- [ ] Define final CLI command name/options.
- [ ] Relax `PinHasher.ValidatePinShape` to custom secret policy.
- [ ] Add/update PinHasher tests.
- [ ] Add write-capable identity repository methods or direct CLI DB logic.
- [ ] Implement bootstrap create path.
- [ ] Implement explicit reset path and lab session revocation.
- [ ] Add audit event without raw secret.
- [ ] Remove Web runtime identity seed.
- [ ] Update tests to seed identity explicitly.
- [ ] Add CLI tests or integration tests as practical.
- [ ] Run relevant tests/build.
- [ ] Update master plan and deployment notes.

## Completion Notes

Fill in after implementation.

- Status:
- Files changed:
- Tests run:
- Manual checks:
- CLI command shape:
- Secret validation policy:
- Seed removal/test-seed strategy:
- Discoveries affecting IAM mutation slices:
