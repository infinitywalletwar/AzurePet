# Lab 01: Walking skeleton

| | |
|---|---|
| **Status** | **Approved** (2026-10-01). Proposed by solution-architect. |
| **Lab branch** | `lab/01` (from `main`) |
| **Version after the lab** | `0.1.0` ([`DELIVERY.md`](../DELIVERY.md) §9) |
| **Roadmap** | [`ROADMAP.md`](../ROADMAP.md#lab-01-walking-skeleton) |
| **Process** | Waves and git workflow per [`DELIVERY.md`](../DELIVERY.md) §4–§6 |

## 1. Goal

Produce the **deployable artefact** every later lab builds on: a .NET 10
solution with the Blazor host (Static SSR by default, one interactive
probe page), health endpoints, security headers and a strict Content
Security Policy, OpenTelemetry to a local Aspire dashboard, the baseline
of the own UI component library, a non-root container image, and a CI
pipeline with the public-repository security gates.

"Deployable" means: the image built by CI is the artefact that Lab 02
pushes to ACR and runs on Container Apps unchanged (port, probes,
configuration by environment variables, no secrets inside). This lab
creates **no Azure resources** and needs no Azure subscription
(NFR-075).

Why now: every later lab needs the solution conventions, the test
harnesses (integration, architecture, browser), the CSP that later UI
must respect (ADR-0017 item 5 asks for this check in the walking
skeleton), and a CI that blocks regressions before any feature exists.

## 2. Scope

### In scope

1. Solution structure, conventions, central package management, analyzers.
2. `InPolsure.Web` host: Blazor Web App, Static SSR default, Interactive
   Server enabled for opt-in pages only; home page; not-found and error
   pages.
3. `InPolsure.SharedKernel` (clock abstraction only).
4. `InPolsure.Ui` Razor class library: layout shell and the first
   components of the ADR-0017 set, base CSS with default brand custom
   properties.
5. Health endpoints `/health/live` and `/health/ready` (no dependency
   checks yet).
6. Security headers and CSP middleware (enforced by default, report-only
   by configuration).
7. Observability baseline: OpenTelemetry traces, metrics and logs,
   exported over OTLP to the Aspire dashboard when configured; JSON
   console logs in containers.
8. UI probe page (Interactive Server, QuickGrid, `ReconnectModal`) behind
   a configuration switch, to run the ADR-0017 CSP check.
9. Tests: unit (incl. bUnit), integration (`WebApplicationFactory`),
   architecture (project dependency rules), browser (Playwright with axe
   and CSP-violation detection).
10. `Dockerfile`, `.dockerignore`, `compose.yaml` (web + Aspire dashboard).
11. `.github/workflows/ci.yml`, `.github/dependabot.yml`, pull-request
    template.
12. `README.md` with local setup.

### Out of scope (later labs)

| Item | Lab |
|---|---|
| Any Azure resource, Bicep, `cd-dev.yml`, ACR push, Azure Monitor exporter, forwarded headers | 02 |
| Database, EF Core, module projects, one-shot commands (`migrate`, `seed`) | 03 |
| Tenant resolution, `Hosting:Mode`, `Deployment:Environment`, host allow-list | 04 |
| `Roles` option (`Web`/`Worker`), hosted services, outbox, email | 05 |
| Authentication, antiforgery-protected forms with data, rate limiting | 07, 08 |
| `/theme.css` and tenant branding (static defaults only now) | 13 |
| Bicep lint and what-if in CI | 02 |

## 3. Requirements and ADRs

| Reference | What this lab does for it |
|---|---|
| NFR-051 | `/health/live`, `/health/ready` |
| NFR-075 | Builds, tests and runs locally with .NET SDK and Docker only |
| NFR-026 | Security headers and strict CSP on every response (baseline; header scan grade is checked in Lab 21) |
| NFR-050 | OTel traces, metrics, logs with W3C trace context (local export) |
| NFR-042 | Query strings redacted in telemetry; no bodies logged |
| NFR-071, NFR-073 | CI on every PR and on `main`; tests fail the build |
| NFR-027 | Dependency review, vulnerable-package check, Dependabot, CodeQL (repository setting) |
| NFR-023 | No secrets anywhere; secret scanning with push protection (repository setting) |
| NFR-080, NFR-081, NFR-082 | axe checks; responsive shell from 360 px; English texts |
| C-01, C-04 | .NET 10, Blazor, GitHub Actions |
| [ADR-0001](../architecture/decisions/0001-modular-monolith.md) | One host, one image; project-per-module convention established (modules arrive from Lab 03) |
| [ADR-0004](../architecture/decisions/0004-compute-hosting.md) | Container image, HTTP port 8080, probe paths |
| [ADR-0008](../architecture/decisions/0008-blazor-render-modes.md) | Default Static SSR; Interactive Server per page only; reconnect UI from the .NET 10 template |
| [ADR-0009](../architecture/decisions/0009-iac-and-cicd.md) | `ci.yml` (items 6 and 12): least privilege, SHA-pinned actions, no `pull_request_target`, no secrets |
| [ADR-0010](../architecture/decisions/0010-observability.md) | Items 1, 3, 4, 6, 10 (OTel, correlation, privacy, health, local dashboard) |
| [ADR-0011](../architecture/decisions/0011-tenant-resolution-and-branding.md) | Item 10: CSP string (tightened, see §6.1) |
| [ADR-0012](../architecture/decisions/0012-secrets-and-configuration.md) | §5 (typed options, `ValidateOnStart`), §6 (local, no cloud secrets), §7 (CI guardrails) |
| [ADR-0017](../architecture/decisions/0017-ui-components.md) | Component library baseline; item 5 (QuickGrid and `ReconnectModal` under CSP: report-only first, then enforce) |

## 4. Target repository layout

```text
/
├─ InPolsure.slnx                       # .NET 10 default solution format
├─ global.json                          # pins the .NET 10 SDK (rollForward: latestFeature)
├─ Directory.Build.props                # common properties, VersionPrefix 0.1.0
├─ Directory.Packages.props             # central package versions
├─ .editorconfig  .gitignore  .gitattributes  .dockerignore
├─ compose.yaml                         # web + aspire-dashboard (local)
├─ README.md
├─ src/
│  ├─ InPolsure.Web/                    # host (Program.cs, Components/, Health/, Security/, Observability/, Dockerfile)
│  ├─ InPolsure.SharedKernel/           # IClock, SystemClock
│  └─ InPolsure.Ui/                     # Razor class library: shell, components, base CSS
├─ tests/
│  ├─ InPolsure.UnitTests/              # SharedKernel + Ui (bUnit)
│  ├─ InPolsure.Web.IntegrationTests/   # WebApplicationFactory (TestServer)
│  ├─ InPolsure.ArchitectureTests/      # project dependency rules (reflection)
│  └─ InPolsure.Web.UiTests/            # Playwright + axe, real Kestrel
├─ .github/
│  ├─ workflows/ci.yml
│  ├─ dependabot.yml
│  └─ pull_request_template.md
└─ docs/                                # architect only
```

**Module convention for later labs (ADR-0001), recorded now:**
`src/Modules/<Module>/InPolsure.<Module>/` (one project; public contract
types in `InPolsure.<Module>.Contracts` namespace, everything else
`internal`), tests in `tests/InPolsure.<Module>.Tests/`. No module
project is created in this lab.

## 5. Conventions (set in wave 0, binding for all labs)

- Target `net10.0`; `Nullable` and `ImplicitUsings` enabled;
  `TreatWarningsAsErrors` true; `AnalysisLevel` `latest-recommended`;
  `EnforceCodeStyleInBuild` true; deterministic builds;
  `ContinuousIntegrationBuild` true when `CI=true`.
- **Central package management** (`ManagePackageVersionsCentrally`);
  no `Version` attributes in project files.
- Root namespace = assembly name. File-scoped namespaces.
- Tests: xUnit v3; test names `Method_or_behaviour_condition_expected`;
  plain xUnit assertions (no assertion library, to avoid licence and
  dependency churn).
- All UI and log texts in English (NFR-082). No inline `style`
  attributes, no `<style>` or `<script>` elements in our markup
  (ADR-0017 item 4).
- Configuration through strongly typed options with
  `ValidateDataAnnotations().ValidateOnStart()` (ADR-0012 §5).
- Every public extension method that wires a feature follows the
  pattern `AddInPolsure<Feature>(this IServiceCollection, IConfiguration)`
  and `UseInPolsure<Feature>` / `MapInPolsure<Feature>` on the app. This
  lets parallel tasks deliver features without editing `Program.cs`
  (only the integration task wires them).

### Packages added in wave 0 (all later tasks use only these)

| Project | Packages (latest stable for `net10.0` at implementation time) |
|---|---|
| `InPolsure.Web` | `Microsoft.AspNetCore.Components.QuickGrid`, `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Instrumentation.AspNetCore`, `OpenTelemetry.Instrumentation.Http`, `OpenTelemetry.Instrumentation.Runtime`, `OpenTelemetry.Exporter.OpenTelemetryProtocol` |
| `InPolsure.Ui` | (framework only: `Microsoft.AspNetCore.App` via Razor SDK) |
| `InPolsure.UnitTests` | `xunit.v3`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `bunit` |
| `InPolsure.Web.IntegrationTests` | `xunit.v3`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `Microsoft.AspNetCore.Mvc.Testing`, `OpenTelemetry.Exporter.InMemory` |
| `InPolsure.ArchitectureTests` | `xunit.v3`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk` |
| `InPolsure.Web.UiTests` | `xunit.v3`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `Microsoft.AspNetCore.Mvc.Testing`, `Microsoft.Playwright`, `Deque.AxeCore.Playwright` |

If a task needs a package that is not listed, it **stops and reports**
instead of editing `Directory.Packages.props` (shared file). The
orchestrator decides whether the package is added by the wave's
integration task (and the task moves to the next wave) or the spec is
changed.

## 6. Hints

### 6.1 CSP and security headers

- Policy (ADR-0011 item 10, tightened with directives that only
  restrict further):
  `default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; font-src 'self'; base-uri 'self'; form-action 'self'; object-src 'none'; frame-ancestors 'none'`,
  plus `upgrade-insecure-requests` when HSTS is enabled. Send it as a
  **response header** (a `<meta>` CSP cannot carry `frame-ancestors`;
  [Microsoft Learn: Blazor CSP](https://learn.microsoft.com/aspnet/core/blazor/security/content-security-policy?view=aspnetcore-10.0)).
- Blazor Web Apps add their own `Content-Security-Policy: frame-ancestors 'self'`
  header for Interactive Server endpoints; set
  `AddInteractiveServerRenderMode(o => o.ContentSecurityFrameAncestorsPolicy = "'none'")`
  so both headers agree (browsers enforce every CSP header).
- The .NET 10 Blazor Web App template has two known CSP traps (same
  Microsoft Learn page): the `NavMenu` component's inline `onclick`
  handler, and the `<ImportMap />` component, which renders an **inline
  import-map `<script>`**. Wave 0 removes the template's `NavMenu`.
  For the import map, **first try removing `<ImportMap />`** (this lab
  loads no JS modules through bare specifiers; framework and our
  scripts are referenced by path). If the UI tests show the import map
  is required, add a **per-request SHA-256 hash of the rendered import
  map** to `script-src` (the documented SRI approach), keep everything
  else as is, and report it: that would be a clarification of
  ADR-0011's literal CSP string, which the architect records.
- Remove Bootstrap and the template's sample pages (Counter, Weather);
  no CSS framework (ADR-0017 item 3).
- Other headers: `X-Content-Type-Options: nosniff`,
  `Referrer-Policy: strict-origin-when-cross-origin`,
  `Permissions-Policy: camera=(), microphone=(), geolocation=(), payment=()`,
  `Cross-Origin-Opener-Policy: same-origin`. HSTS (`max-age` ≥ 1 year)
  only when `Security:Hsts:Enabled` is true (true outside
  Development). TLS is terminated by the Container Apps ingress
  (ADR-0004), so the app listens on HTTP 8080 and does **not** call
  `UseHttpsRedirection` in containers.
- Options: `Security:Csp:ReportOnly` (default `false`) switches the
  header name to `Content-Security-Policy-Report-Only`. Validate the
  options at startup.

### 6.2 Health

- `/health/live`: no checks (process alive). `/health/ready`: only
  checks tagged `ready`; none exist yet (database checks come in
  Lab 03, prod-only per ADR-0010 item 6). Both anonymous,
  `Cache-Control: no-store`, plain-text status, excluded from tracing.

### 6.3 Observability

- `service.name = inpolsure-web`, `service.version` from
  `AssemblyInformationalVersion`.
- Tracing: ASP.NET Core and HttpClient instrumentation; filter out
  `/health/*` and static assets. Metrics: ASP.NET Core, HttpClient,
  runtime. Logs: OTel logging provider with scopes.
- Export with `UseOtlpExporter()` **only when**
  `OTEL_EXPORTER_OTLP_ENDPOINT` is set; nothing is exported otherwise
  (tests, CI). Azure Monitor export is Lab 02.
- Confirm that the ASP.NET Core instrumentation version in use redacts
  query-string values by default (NFR-042); if not, configure it.
- Non-Development: JSON console formatter. Never log request or
  response bodies.

### 6.4 UI

- Static SSR is the app-wide default; `@rendermode InteractiveServer`
  appears only on the probe page in this lab.
- `InPolsure.Ui` contents now: `AppShell` (header with product name,
  `main` landmark, footer, skip link), `Button`, `Notice` (info/warning/
  error with `role` set correctly), `base.css` with
  `--brand-primary`, `--brand-accent`, `--brand-on-primary`,
  text and surface variables as defaults (tenant values come from
  `/theme.css` in Lab 13). Use CSS isolation for component styles.
- The probe page (`/_probe/interactive`, only when
  `Diagnostics:EnableUiProbePages` is true): Interactive Server; a
  QuickGrid over a static in-memory list with one sortable column; a
  button that changes state; the .NET 10 `ReconnectModal` in the
  layout used by interactive pages. Returns 404 when disabled
  (default `false` in `appsettings.json`).
- Not-found page: use the .NET 10 router's not-found page support;
  error page without stack traces outside Development.

### 6.5 Tests

- Integration tests use `WebApplicationFactory<Program>` (expose
  `public partial class Program`). Feature tasks of wave 1 test their
  extension methods against a **minimal host** built in the test
  (`WebApplication.CreateBuilder()` + `UseTestServer()`), because
  `Program.cs` is wired only in the integration task.
- UI tests start the real app with `WebApplicationFactory.UseKestrel()`
  (new in .NET 10;
  [API reference](https://learn.microsoft.com/dotnet/api/microsoft.aspnetcore.mvc.testing.webapplicationfactory-1.usekestrel))
  on a free port, environment `Testing`, probe pages enabled. Chromium
  only in CI (time and cost); Firefox/WebKit can be run locally.
- CSP violations are detected by (a) listening for console messages
  that contain "Content Security Policy" and (b) a `securitypolicyviolation`
  listener added with `AddInitScriptAsync` before navigation. Any
  violation fails the test.
- axe: `Deque.AxeCore.Playwright`, rules tagged `wcag2a`, `wcag2aa`,
  `wcag21a`, `wcag21aa`; zero violations.
- Playwright browsers: `pwsh tests/InPolsure.Web.UiTests/bin/<cfg>/net10.0/playwright.ps1 install --with-deps chromium`
  (PowerShell 7 is preinstalled on GitHub Ubuntu runners; locally it
  needs `pwsh`).
- Architecture tests in this lab need no library: inspect
  `Assembly.GetReferencedAssemblies()` and `Type.Namespace`. The
  library or analyzer for type-level rules (e.g. the
  `IgnoreQueryFilters()` ban) is chosen in Lab 04.

### 6.6 Container

- Multi-stage `src/InPolsure.Web/Dockerfile`, build context = repository
  root (needs `Directory.*.props`, `global.json`). Build on
  `mcr.microsoft.com/dotnet/sdk:10.0`, run on
  `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled` (non-root by
  default, no shell; smaller attack surface). Pin tags; Dependabot keeps
  them current. Restore as a separate cached layer.
- Port 8080 (`ASPNETCORE_HTTP_PORTS`, the image default). No
  `HEALTHCHECK` instruction (Container Apps uses its own probes; the
  chiseled image has no shell or curl).
- `.dockerignore` excludes `bin/`, `obj/`, `tests/`, `docs/`, `.git/`,
  `.github/`, `**/*.user`, `**/secrets.json`, `.env*`.
- `compose.yaml`: `web` (built from the Dockerfile, port 8080 published
  on `127.0.0.1`) and `aspire-dashboard`
  (`mcr.microsoft.com/dotnet/aspire-dashboard` with a pinned tag; UI
  port published on `127.0.0.1` only; anonymous access is acceptable
  because it is bound to loopback and holds synthetic local telemetry).
  `web` gets `OTEL_EXPORTER_OTLP_ENDPOINT=http://aspire-dashboard:18889`
  and `ASPNETCORE_ENVIRONMENT=Development`.

### 6.7 CI

- Triggers: `pull_request` to `main`; `push` to `main` and `lab/*`
  (the glob matches `lab/01` but not task branches); `workflow_dispatch`.
- Top-level `permissions: contents: read`; `concurrency` per ref with
  cancel-in-progress for PRs.
- Every third-party action pinned to a **full commit SHA** with the
  version in a trailing comment (ADR-0009 item 12).
- Jobs and the check names `main` will require ([`DELIVERY.md`](../DELIVERY.md) §3.2):
  `build-test`, `ui-tests`, `container`, `dependency-review` (PR only).
- Vulnerable packages: `dotnet list package --vulnerable --include-transitive --format json`
  (or the .NET 10 `dotnet package list` equivalent) parsed with `jq`;
  fail on `High` or `Critical` (NFR-027).
- `container` job: `docker build` (no push, no registry login), run the
  image, poll `/health/live` and `/health/ready`, assert the CSP header
  on `/`, stop the container.
- CodeQL uses **default setup**, enabled by the owner in repository
  settings (no workflow file), ADR-0009 item 6.
- Dependabot: `nuget` (root), `github-actions`, `docker`
  (`src/InPolsure.Web`), weekly, minor and patch updates grouped.

## 7. Acceptance criteria

| ID | Criterion | Checked by |
|---|---|---|
| AC-01 | `dotnet build InPolsure.slnx -c Release` succeeds with zero warnings | CI `build-test` |
| AC-02 | `dotnet format InPolsure.slnx --verify-no-changes` passes | CI `build-test` |
| AC-03 | All unit, integration and architecture tests pass; no package version appears in any `.csproj` | CI `build-test`, review |
| AC-04 | `GET /` returns 200 with `<html lang="en">`, title containing "InPolsure", rendered by Static SSR (no `_blazor` WebSocket is opened on `/`) | Integration test, UI test |
| AC-05 | `GET /health/live` and `/health/ready` return 200, `Cache-Control: no-store`, anonymous, no external calls | Integration tests |
| AC-06 | Every HTML response carries the CSP of §6.1 as an **enforced** header, plus the other headers of §6.1; HSTS only when enabled; report-only only when configured | Integration tests |
| AC-07 | On `/` and on `/_probe/interactive`: zero CSP violations and zero axe WCAG 2.1 A/AA violations; the probe page establishes a circuit, the button updates the UI and QuickGrid sorting works | UI tests |
| AC-08 | With the connection to the server cut (Playwright offline mode or stopping the server), the `ReconnectModal` dialog becomes visible without CSP violations. If this cannot be simulated reliably in CI, the task reports it and the check is done manually once and recorded in the lab PR | UI test or recorded manual check |
| AC-09 | At 360 px viewport width the home page has no horizontal scroll | UI test |
| AC-10 | `/_probe/interactive` returns 404 unless `Diagnostics:EnableUiProbePages=true`; the default in `appsettings.json` is `false` | Integration test |
| AC-11 | A request to `/` produces a server span with `service.name=inpolsure-web`; `/health/*` produces none; with `OTEL_EXPORTER_OTLP_ENDPOINT` unset nothing is exported | Integration test (in-memory exporter) |
| AC-12 | `docker build` succeeds from the repository root; the container runs as a non-root user, listens on 8080 and answers `/health/live`; the image contains no `secrets.json` or `.env` files | CI `container`, review |
| AC-13 | `docker compose up` serves `/` on `http://localhost:8080` and traces appear in the Aspire dashboard | Manual check by the task agent, recorded in its report |
| AC-14 | Architecture tests fail when `InPolsure.SharedKernel` references another InPolsure assembly or ASP.NET Core, when `InPolsure.Ui` references `InPolsure.Web`, or when any non-test assembly references a test assembly | Architecture tests (each rule has a test that would fail on violation) |
| AC-15 | `ci.yml`: least-privilege permissions, all third-party actions SHA-pinned, no secrets, no `pull_request_target`, jobs `build-test`, `ui-tests`, `container`, `dependency-review`; all green on the lab PR | azure-infra-reviewer, CI run |
| AC-16 | `dependabot.yml` covers NuGet, GitHub Actions and Docker | Review |
| AC-17 | No third-party UI library or CSS framework; no inline `style` attributes, `<style>` or `<script>` blocks in `src/**/*.razor` | Review, simple grep in CI optional |
| AC-18 | `README.md` lets a new developer build, test, run locally and run UI tests in ≤ 15 minutes on a machine with the .NET 10 SDK, Docker and PowerShell 7 | Review |

## 8. Human prerequisites

Before wave 0 (owner; agents must not do these):

1. Commit the current `docs/`, `.claude/`, `AGENTS.md`, `.mcp.json` to
   `main` (the one bootstrap commit made directly on `main`,
   [`DELIVERY.md`](../DELIVERY.md) §11 R0).
2. Create the **public** GitHub repository and push `main`.
3. Repository settings: secret scanning **with push protection**,
   Dependabot alerts and security updates, Actions default
   `GITHUB_TOKEN` permission read-only, approval required for workflows
   from first-time contributors, allow merge commits and squash merging
   (no rebase merging).
4. Protect `main`: require a pull request, block force pushes and
   deletions. Required status checks are added after the lab PR has run
   CI once (DoD item 6).
5. Locally: .NET 10 SDK, Docker, PowerShell 7 (`pwsh`), Git ≥ 2.40.

Before the lab PR is merged: enable CodeQL default setup (C#, Actions).

## 9. Tasks

### T-01.1 Solution skeleton and conventions

- **Wave:** 0 (single agent).
- **Description:** create the solution, all projects of §4 with their
  references, the build and package files of §5, and a minimal working
  host: `Program.cs` with Razor components (Static SSR default,
  Interactive Server render mode registered with
  `ContentSecurityFrameAncestorsPolicy = "'none'"`), `App.razor`,
  `Routes.razor`, a minimal `Home` page, template sample pages,
  `NavMenu` and Bootstrap removed; `IClock`/`SystemClock` in
  SharedKernel; one placeholder test per test project so
  `dotnet test` runs. Add every package of §5 to
  `Directory.Packages.props` and to its project. `Directory.Build.props`
  sets `VersionPrefix` `0.1.0`.
- **Depends on:** none.
- **Owns:** `InPolsure.slnx`, `global.json`, `Directory.Build.props`,
  `Directory.Packages.props`, `.editorconfig`, `.gitignore`,
  `.gitattributes`, `src/**/*.csproj`, `tests/**/*.csproj`,
  `src/InPolsure.Web/Program.cs`, `src/InPolsure.Web/Components/App.razor`,
  `src/InPolsure.Web/Components/Routes.razor`,
  `src/InPolsure.Web/Components/_Imports.razor`,
  `src/InPolsure.Web/Components/Pages/Home.razor`,
  `src/InPolsure.Web/appsettings*.json`, `src/InPolsure.SharedKernel/**`,
  `tests/InPolsure.UnitTests/SharedKernel/**`, placeholder test files.
- **Shared files:** all of the above are shared hotspots created here.
- **Acceptance:** AC-01, AC-02 pass; `dotnet run --project src/InPolsure.Web`
  serves `/` with status 200; layout of §4 exists; no `Version` in any
  project file; conventions of §5 applied.
- **Verify:**
  ```bash
  dotnet build InPolsure.slnx -c Release
  dotnet test InPolsure.slnx -c Release
  dotnet format InPolsure.slnx --verify-no-changes
  ```
- **Review:** dotnet-code-reviewer, architecture-compliance-reviewer
  (it sets the conventions for every later lab).

### T-01.2 Health endpoints

- **Wave:** 1.
- **Description:** `AddInPolsureHealth` / `MapInPolsureHealth` in
  `src/InPolsure.Web/Health/` per §6.2. Tests against a minimal host.
- **Depends on:** T-01.1.
- **Owns:** `src/InPolsure.Web/Health/**`,
  `tests/InPolsure.Web.IntegrationTests/Health/**`.
- **Shared files:** none (wired by T-01.8).
- **Acceptance:** AC-05 on a minimal host.
- **Verify:** `dotnet test tests/InPolsure.Web.IntegrationTests -c Release --filter "FullyQualifiedName~Health"`
- **Review:** dotnet-code-reviewer.

### T-01.3 Security headers and CSP

- **Wave:** 1.
- **Description:** options class, validation, middleware and
  `AddInPolsureSecurityHeaders` / `UseInPolsureSecurityHeaders` in
  `src/InPolsure.Web/Security/` per §6.1 (CSP enforced by default,
  report-only switch, HSTS switch). Tests against a minimal host,
  including the exact CSP string.
- **Depends on:** T-01.1.
- **Owns:** `src/InPolsure.Web/Security/**`,
  `tests/InPolsure.Web.IntegrationTests/Security/**`.
- **Shared files:** none (wired by T-01.8).
- **Acceptance:** AC-06 on a minimal host; invalid options fail at
  startup.
- **Verify:** `dotnet test tests/InPolsure.Web.IntegrationTests -c Release --filter "FullyQualifiedName~Security"`
- **Review:** dotnet-code-reviewer.

### T-01.4 Observability baseline

- **Wave:** 1.
- **Description:** `AddInPolsureObservability` in
  `src/InPolsure.Web/Observability/` per §6.3 (resource, tracing,
  metrics, logging, conditional OTLP export, health and static-asset
  filtering, JSON console outside Development). Tests with the in-memory
  exporter on a minimal host.
- **Depends on:** T-01.1.
- **Owns:** `src/InPolsure.Web/Observability/**`,
  `tests/InPolsure.Web.IntegrationTests/Observability/**`.
- **Shared files:** none (wired by T-01.8).
- **Acceptance:** AC-11 on a minimal host; no exporter registered when
  the OTLP endpoint is unset.
- **Verify:** `dotnet test tests/InPolsure.Web.IntegrationTests -c Release --filter "FullyQualifiedName~Observability"`
- **Review:** dotnet-code-reviewer.

### T-01.5 UI shell, components and probe page

- **Wave:** 1.
- **Description:** `InPolsure.Ui` contents of §6.4 (shell, `Button`,
  `Notice`, `base.css` with default brand variables); `MainLayout` and
  an `InteractiveLayout` (with `ReconnectModal` from the .NET 10
  template) in the web project; not-found and error pages; probe page
  `/_probe/interactive` guarded by `Diagnostics:EnableUiProbePages`
  (options class in `src/InPolsure.Web/Diagnostics/`); remove
  `<ImportMap />` from `App.razor` per §6.1 if not needed. bUnit tests
  for the Ui components (roles, labels, no inline styles).
- **Depends on:** T-01.1.
- **Owns:** `src/InPolsure.Ui/**`, `src/InPolsure.Web/Components/**`
  (including `App.razor` and `Pages/Home.razor`, created in wave 0;
  ownership is per wave, and no other wave-1 task touches them),
  `src/InPolsure.Web/Diagnostics/**`, `src/InPolsure.Web/wwwroot/**`,
  `tests/InPolsure.UnitTests/Ui/**`.
- **Shared files:** none in wave 1.
- **Acceptance:** bUnit tests pass; the app renders the shell on `/`;
  the probe page works when enabled (manual run); AC-17 holds for this
  task's files.
- **Verify:**
  ```bash
  dotnet build InPolsure.slnx -c Release
  dotnet test tests/InPolsure.UnitTests -c Release
  ```
- **Review:** dotnet-code-reviewer.

### T-01.6 Container image and local compose

- **Wave:** 1.
- **Description:** `Dockerfile`, `.dockerignore`, `compose.yaml` per
  §6.6.
- **Depends on:** T-01.1.
- **Owns:** `src/InPolsure.Web/Dockerfile`, `.dockerignore`,
  `compose.yaml`.
- **Shared files:** none.
- **Acceptance:** AC-12 (health check is verified after T-01.8; in this
  task check `/`), AC-13.
- **Verify:**
  ```bash
  docker build -f src/InPolsure.Web/Dockerfile -t inpolsure-web:local .
  docker image inspect inpolsure-web:local --format '{{.Config.User}}'
  docker compose up -d --build
  curl -fsS -o /dev/null -w "%{http_code}\n" http://localhost:8080/
  docker compose down
  ```
  (The chiseled image has no shell, so the user is checked with
  `docker image inspect`; it must not be empty or `root`.)
- **Review:** azure-infra-reviewer.

### T-01.7 Architecture tests

- **Wave:** 1.
- **Description:** reflection-based project dependency and namespace
  rules of AC-14, each with a clear failure message; a short comment
  block describing how module rules will be added from Lab 03.
- **Depends on:** T-01.1.
- **Owns:** `tests/InPolsure.ArchitectureTests/**`.
- **Shared files:** none.
- **Acceptance:** AC-14.
- **Verify:** `dotnet test tests/InPolsure.ArchitectureTests -c Release`
- **Review:** dotnet-code-reviewer.

### T-01.8 Wave 1 integration: host wiring

- **Wave:** 1 (integration, runs after T-01.2 to T-01.7 are merged into
  `lab/01`).
- **Description:** wire all features in `Program.cs` in this order:
  observability → exception handler/HSTS → security headers → static
  assets → antiforgery → health endpoints → Razor components. Add
  configuration to `appsettings.json` (`Security`, `Diagnostics`,
  logging levels) and `appsettings.Development.json`. Add full-host
  integration tests through `WebApplicationFactory<Program>` for AC-04,
  AC-05, AC-06, AC-10, AC-11. Re-run the container check of T-01.6 for
  `/health/live`.
- **Depends on:** T-01.2, T-01.3, T-01.4, T-01.5, T-01.6, T-01.7.
- **Owns:** `src/InPolsure.Web/Program.cs`,
  `src/InPolsure.Web/appsettings*.json`,
  `tests/InPolsure.Web.IntegrationTests/Host/**`.
- **Shared files:** `Program.cs`, `appsettings*.json`.
- **Acceptance:** AC-01 to AC-06, AC-10, AC-11 on the real host;
  container answers `/health/live`.
- **Verify:**
  ```bash
  dotnet build InPolsure.slnx -c Release
  dotnet test InPolsure.slnx -c Release
  dotnet format InPolsure.slnx --verify-no-changes
  docker build -f src/InPolsure.Web/Dockerfile -t inpolsure-web:local .
  docker run -d --rm -p 127.0.0.1:8080:8080 --name inpolsure-web inpolsure-web:local
  curl -fsS http://localhost:8080/health/live && curl -fsSI http://localhost:8080/ | grep -i content-security-policy
  docker stop inpolsure-web
  ```
- **Review:** dotnet-code-reviewer.

### T-01.9 Browser tests: CSP, axe, interactivity

- **Wave:** 2.
- **Description:** Playwright fixture starting the app with
  `WebApplicationFactory.UseKestrel()` per §6.5; tests for AC-04 (no
  circuit on `/`), AC-07, AC-08, AC-09. ADR-0017 item 5: run first with
  `Security:Csp:ReportOnly=true` to collect violations, fix them in the
  owned UI files (wrap or replace offending components; `ImportMap`
  handling per §6.1), then run enforced. Report any change to the CSP
  string.
- **Depends on:** T-01.8.
- **Owns:** `tests/InPolsure.Web.UiTests/**`; for CSP fixes only:
  `src/InPolsure.Ui/**`, `src/InPolsure.Web/Components/**`,
  `src/InPolsure.Web/wwwroot/**`.
- **Shared files:** `src/InPolsure.Web/Components/App.razor` (only if
  the import-map hash is required).
- **Acceptance:** AC-04, AC-07, AC-08 (or recorded manual check), AC-09
  with CSP enforced.
- **Verify:**
  ```bash
  dotnet build tests/InPolsure.Web.UiTests -c Release
  pwsh tests/InPolsure.Web.UiTests/bin/Release/net10.0/playwright.ps1 install --with-deps chromium
  dotnet test tests/InPolsure.Web.UiTests -c Release
  ```
- **Review:** dotnet-code-reviewer.

### T-01.10 Developer README

- **Wave:** 2.
- **Description:** root `README.md`: what InPolsure is (one paragraph,
  links to `docs/`), prerequisites, build and test commands, running
  with `dotnet run` and with `docker compose`, the Aspire dashboard URL,
  installing Playwright browsers, the probe-page switch, where
  configuration lives, the "no secrets in the repository" rule
  (user-secrets only). English.
- **Depends on:** T-01.8.
- **Owns:** `README.md`.
- **Shared files:** none.
- **Acceptance:** AC-18; commands in the README match the real ones.
- **Verify:** follow the README from a clean clone in a fresh worktree:
  ```bash
  dotnet build InPolsure.slnx -c Release && dotnet test InPolsure.slnx -c Release
  ```
- **Review:** architecture-compliance-reviewer (consistency with
  `docs/`).

### T-01.11 Wave 2 integration: CI pipeline and repository automation

- **Wave:** 2 (integration, after T-01.9 and T-01.10 are merged).
- **Description:** `.github/workflows/ci.yml` per §6.7 (jobs
  `build-test`, `ui-tests`, `container`, `dependency-review`),
  `.github/dependabot.yml`, `.github/pull_request_template.md` with the
  checklist from [`DELIVERY.md`](../DELIVERY.md) §7.
- **Depends on:** T-01.9, T-01.10.
- **Owns:** `.github/**`.
- **Shared files:** `.github/workflows/ci.yml` (CI hotspot).
- **Acceptance:** AC-15, AC-16; every command in `ci.yml` has been run
  locally once with the same result; workflow syntax valid.
- **Verify:**
  ```bash
  docker run --rm -v "$PWD:/repo" -w /repo rhysd/actionlint:<pinned-tag> -color
  dotnet build InPolsure.slnx -c Release && dotnet test InPolsure.slnx -c Release
  ```
  The decisive check is the CI run on the lab PR (DoD item 4).
- **Review:** azure-infra-reviewer.

## 10. Waves

| Wave | Tasks | Parallel? | Why parallel / why not | Reviewers | Tag after merge |
|---|---|---|---|---|---|
| 0 | T-01.1 Solution skeleton and conventions | No (one agent) | Creates every shared file and convention the others depend on | dotnet-code-reviewer, architecture-compliance-reviewer | `lab-01-wave-0` |
| 1 | T-01.2 Health; T-01.3 Security headers and CSP; T-01.4 Observability; T-01.5 UI shell and probe page; T-01.6 Container; T-01.7 Architecture tests | **Yes** (6 worktrees) | Each owns its own folder; none edits `Program.cs`, `.slnx`, `Directory.*.props` or `appsettings*.json`; features are exposed as extension methods | dotnet-code-reviewer (T-01.2, .3, .4, .5, .7); azure-infra-reviewer (T-01.6) | — |
| 1-int | T-01.8 Host wiring | No (integration) | Owns the shared `Program.cs` and `appsettings*.json` | dotnet-code-reviewer | `lab-01-wave-1` |
| 2 | T-01.9 Browser tests; T-01.10 README | **Yes** (2 worktrees) | Disjoint paths: tests and UI fixes vs `README.md` | dotnet-code-reviewer (T-01.9); architecture-compliance-reviewer (T-01.10) | — |
| 2-int | T-01.11 CI pipeline and repository automation | No (integration) | Owns the CI hotspot; must run the final test projects | azure-infra-reviewer | `lab-01-wave-2` |
| End | Whole lab diff `main...lab/01` | — | — | architecture-compliance-reviewer | `lab-01` (on `main` after merge) |

Wave 1 runs as two batches (user decision, 2026-10-01):
T-01.2 to T-01.4, then T-01.5 to T-01.7; ownership is disjoint, so the
order does not matter.

## 11. Definition of Done

1. All tasks merged into `lab/01` in task order; tags `lab-01-wave-0`,
   `lab-01-wave-1`, `lab-01-wave-2` exist.
2. Every task passed its `Verify` and its reviewers with no open
   Blockers ([`DELIVERY.md`](../DELIVERY.md) §5.4).
3. `architecture-compliance-reviewer` on `main...lab/01`: COMPLIANT (or
   only non-blocking notes, listed in the PR).
4. PR `lab/01` → `main` with all CI jobs green; AC-01 to AC-18 ticked
   in the PR description (AC-08/AC-13 manual results recorded there).
5. The user merges the PR with a **merge commit**; tag `lab-01` on the
   merge commit; GitHub Release `lab-01` with the lab summary.
6. Owner adds required status checks to `main`: `build-test`,
   `ui-tests`, `container`, `dependency-review`, `CodeQL`; CodeQL
   default setup enabled.
7. Worktrees and task branches removed.
8. Lab report in the PR: FR/NFR IDs closed, deviations (e.g. CSP
   import-map handling), follow-ups for Lab 02.
9. The solution-architect updates `ROADMAP.md` status and writes the
   Lab 02 spec from the merged code.

## 12. Inputs this lab hands to Lab 02

- The image and its runtime contract: port 8080, `/health/live`,
  `/health/ready`, non-root, configuration via environment variables.
- Configuration keys that Bicep will set: `ASPNETCORE_ENVIRONMENT`,
  `Security__Hsts__Enabled`, `OTEL_*`/Application Insights connection
  string (Lab 02 adds the Azure Monitor exporter).
- The CI job names used in branch protection and as the gate for
  `cd-dev.yml`.
