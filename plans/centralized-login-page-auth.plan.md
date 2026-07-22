---
name: centralized-login-page-auth
overview: Replace per-page embedded login screens with a centralized /login page, move app HTML documents out of static serving, and introduce a default-fail page registry that handles document-route auth redirects separately from API authorization.
todos:
  - id: define-page-registry
    content: Add a server-side page registry for app document routes with explicit access requirements, resource names, and fallbacks; make unknown/unregistered app documents fail closed.
    status: completed
  - id: move-app-documents-out-of-wwwroot
    content: Move index/orders/iam/scheduling-config HTML documents from Web/wwwroot to a non-static app-pages location and update embedded resource/project loading.
    status: completed
  - id: add-central-login-page
    content: Add a new centralized login document served at /login with shared login UI/JS and returnUrl handling.
    status: completed
  - id: server-auth-redirects
    content: Route protected app document requests through the registry; unauthenticated users redirect to /login?returnUrl=..., wrong-role users redirect to a safe fallback.
    status: completed
  - id: remove-embedded-login-shells
    content: Remove per-page login shells and login handlers from Orders, Invoicer, IAM, and Scheduling Config pages; pages become authenticated app shells.
    status: pending
  - id: centralize-logout-navigation
    content: Update shared app chrome logout behavior/usages so logout revokes the session and location.replace's to /login or a safe login URL.
    status: pending
  - id: update-tests
    content: Add automated integration/browser tests covering unauthenticated redirects, successful login page loads, role fallback behavior, and absence of static .html bypasses.
    status: pending
  - id: build-and-regression
    content: Run build and relevant Web.Tests regression suites; document any manual smoke checks.
    status: pending
isProject: false
---

# Centralized Login and Page Auth Plan

## Goal

Replace the current duplicated per-page login screens with a single centralized `/login` page and a server-side, default-fail app page registry.

This is a navigation/document-serving refactor only. API endpoint authorization remains separate and authoritative.

## Current Problem

The app currently has separate login UI and auth bootstrap code in multiple SPA-ish pages:

- `/orders` via `Web/wwwroot/orders.html` + `Web/wwwroot/js/orders-page.js`
- `/` invoicer via `Web/wwwroot/index.html`
- `/iam` via `Web/wwwroot/iam.html`
- `/scheduling-config` via `Web/wwwroot/scheduling-config.html`

They share `/api/scheduling/auth/login`, `/logout`, and `/me`, but duplicate login DOM, submit handlers, role checks, and signed-out transitions.

Additionally, app documents live under `wwwroot`, so direct static paths such as `/iam.html` or `/scheduling-config.html` can bypass document-route auth checks. API auth still protects data, but app shell documents should not be statically available.

## Decisions Already Made

- Prefer one centralized `/login` page over shared embedded login components.
- Move app HTML documents out of `wwwroot`; keep only static assets in `wwwroot`.
- Preserve only path/query in `returnUrl` initially. Losing hash-route state is acceptable for now.
- Do not introduce role-indicating URL params such as `requiredRole=lab`.
- Use a default-fail page registry: new app pages must be explicitly registered with access metadata.
- Keep API security independent and unchanged in principle.
- Credential clearing behavior is intentionally out of scope for this plan; it can be implemented after centralization.

## Proposed File Layout

Keep true static assets in `wwwroot`:

```txt
Web/wwwroot/css/**
Web/wwwroot/js/**
Web/wwwroot/images/**
```

Move document HTML to a non-static location, e.g.:

```txt
Web/AppPages/index.html
Web/AppPages/orders.html
Web/AppPages/iam.html
Web/AppPages/scheduling-config.html
Web/AppPages/login.html
```

Update `Web/Web.csproj` embedded resources accordingly.

## Page Registry

Add a server-side registry representing every app document route.

Suggested metadata:

```csharp
enum PageAccess
{
    AnyAuthenticated,
    LabOnly
}

record AppPageDefinition(
    string Path,
    string ResourceName,
    PageAccess Access,
    string FallbackPath);
```

Initial registered pages:

| Path | Resource | Access | Wrong-role fallback |
| --- | --- | --- | --- |
| `/` | `index.html` | LabOnly | `/orders` |
| `/orders` | `orders.html` | AnyAuthenticated | `/orders` |
| `/iam` | `iam.html` | LabOnly | `/orders` |
| `/scheduling-config` | `scheduling-config.html` | LabOnly | `/orders` |
| `/login` | `login.html` | Public special-case | n/a |

Important rules:

- Unknown/unregistered `returnUrl` is rejected/falls back safely.
- Unknown/unregistered app document routes should not silently default to any access level.
- `returnUrl` must be local-only; no external/open redirects.
- Server-side route mapping should ideally be driven by this registry to prevent drift.

## Auth Flow

### Unauthenticated protected page request

Example:

```txt
GET /iam
302 /login?returnUrl=%2Fiam
```

### Successful login

1. `/login` submits to `/api/scheduling/auth/login`.
2. API returns actor info.
3. Login page resolves `returnUrl` against known page metadata.
4. If actor may access target, navigate with `location.replace(returnUrl)`.
5. Otherwise navigate with `location.replace(fallbackPath)`.

Examples:

- unauthenticated `/iam` -> login as lab -> `/iam`
- unauthenticated `/iam` -> login as clinic -> `/orders`
- direct `/login` -> login as lab -> `/`
- direct `/login` -> login as clinic -> `/orders`

### Already authenticated `/login`

If a user opens `/login` while already authenticated:

- call `/api/scheduling/auth/me`;
- resolve target/default using actor;
- `location.replace(...)` to the valid destination.

### Logout

Product pages should stop showing embedded login panels on logout.

Target behavior:

```js
await POST /api/scheduling/auth/logout
location.replace('/login')
```

Using `replace` avoids leaving the authenticated app shell as the immediate back destination.

## Hash Routes

For this initial refactor, do not preserve hash fragments through server redirects.

Examples:

```txt
/orders#new/1 -> /login?returnUrl=/orders -> /orders
```

This is acceptable for now because deep hash navigation is not required yet.

Future options if needed:

1. Move orders routing from hash routing to history routing.
2. Serve an unauthenticated lightweight SPA shell that reads `location.hash` and client-redirects to `/login?returnUrl=/orders%23...`.

## Browser History Expectations

Use `location.replace` for auth redirects initiated by client code, especially login-success and logout.

Desired practical behavior:

```txt
external site -> /iam -> /login?returnUrl=/iam -> successful login replace to /iam
Back -> external site or previous non-login location
```

Also consider `pageshow`/bootstrap auth checks on protected pages so browser bfcache does not show stale authenticated UI after logout.

## Implementation Notes

### Server

Likely files:

- `Web/WebProgram.cs`
- `Web/SchedulingEndpointAuth.cs`
- new `Web/AppPageRegistry.cs` or similar
- `Web/Web.csproj`

Server should:

- serve `/login` without requiring auth;
- map registered app pages through auth-aware handlers;
- redirect unauthenticated users to `/login?returnUrl=...`;
- redirect authenticated wrong-role users to fallback;
- keep `UseStaticFiles()` for assets only;
- prevent direct static serving of app document HTML by moving documents out of `wwwroot`.

### Client

Likely files:

- new centralized login page/script, either inline in `login.html` or `Web/wwwroot/js/login-page.js`
- `Web/wwwroot/js/app-chrome.js`
- `Web/wwwroot/js/orders-page.js`
- moved/updated `index.html`, `orders.html`, `iam.html`, `scheduling-config.html`

Client should:

- remove/hide old auth-shell markup from product pages;
- remove per-page login submit handlers;
- keep page-specific authenticated bootstrapping/loading;
- handle unexpected `/api/...` 401/403 by redirecting to `/login?returnUrl=currentPath` where appropriate;
- keep app chrome actor sync based on authenticated bootstrap.

## Testing Requirements

Add automated coverage before considering complete.

### Server/integration tests

Add or update tests in `Web.Tests` for:

- unauthenticated `GET /orders` redirects to `/login?returnUrl=%2Forders`;
- unauthenticated `GET /` redirects to `/login?returnUrl=%2F`;
- unauthenticated `GET /iam` redirects to `/login?returnUrl=%2Fiam`;
- unauthenticated `GET /scheduling-config` redirects to `/login?returnUrl=%2Fscheduling-config`;
- authenticated clinic `GET /orders` returns 200;
- authenticated clinic `GET /` redirects to `/orders`;
- authenticated clinic `GET /iam` redirects to `/orders`;
- authenticated lab `GET /`, `/orders`, `/iam`, `/scheduling-config` return 200;
- `GET /iam.html`, `/scheduling-config.html`, `/orders.html`, `/index.html` no longer serve app documents;
- invalid/external return URLs are not honored by login resolution, if resolution is server-assisted.

### Browser/UI tests

Add Playwright/Puppeteer-style tests if the existing harness supports them, especially:

- opening `/login`, logging in as clinic, lands on `/orders` and orders app loads;
- opening `/login?returnUrl=/iam`, logging in as lab, lands on `/iam` and IAM app loads;
- opening `/login?returnUrl=/iam`, logging in as clinic, lands on `/orders`;
- opening `/login?returnUrl=/scheduling-config`, logging in as lab, scheduling config app loads;
- opening `/login?returnUrl=/`, logging in as lab, invoicer app loads;
- app chrome logout redirects to `/login`.

Use existing seeded credentials from tests where available:

- clinic: `DEMO` / `123456`
- lab: `LAB` / `654321`

## Out of Scope

- Credential clearing behavior after logout/login. This becomes easier after centralization but should be done separately.
- Preserving hash fragments in `returnUrl`.
- Replacing hash routing with history routing.
- Changing API authorization semantics.
- Major redesign of app chrome/product navigation.

## Completion Checklist

- [ ] App documents moved out of static serving.
- [ ] `/login` centralized page added.
- [ ] Page registry added and default-fail behavior documented/tested.
- [ ] Product pages no longer include duplicate login forms.
- [ ] Unauthenticated document requests redirect to `/login?returnUrl=...`.
- [ ] Wrong-role document requests redirect to safe fallback.
- [ ] Login success redirects to valid target/fallback using actor info.
- [ ] Logout redirects via `location.replace('/login')`.
- [ ] Static `.html` bypass paths are closed.
- [ ] Automated integration tests added.
- [ ] Automated browser/page-load tests added where feasible.
- [ ] `dotnet build Web/Web.csproj` passes.
- [ ] Relevant `dotnet test Web.Tests/Web.Tests.csproj` tests pass.
