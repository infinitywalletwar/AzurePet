# Implemented Lab 01 code review — 2026-10-02

**Verdict:** acceptable walking-skeleton foundation, with one newly confirmed
minor integration defect and already-recorded UI follow-ups. No Blockers or
Majors were found in the implemented service/container scope. This does not
mean the complete Lab 01 is done: browser tests, README and CI remain pending.

**Scope:** current `lab/01` source, tests, build/package configuration,
Dockerfile and Compose, against AGENTS.md, Lab 01 and the relevant ADRs.
The .NET code, infrastructure and architecture-compliance reviewers inspected
their respective areas independently; the coordinating review also inspected
the Blazor components and performed the runtime checks below.
No application source or accepted design decisions were changed.

## Findings

### C-01 — Minor, confirmed: health method rejection becomes HTTP 400

**Location:** `src/InPolsure.Web/Program.cs:36`.

The global `UseStatusCodePagesWithReExecute("/not-found", ...)` middleware
re-executes the original POST as a POST to the Razor not-found endpoint.
Its validation changes the original method-not-allowed status to Bad Request.

Reproduced against the existing Release container on a temporary loopback port:

```text
GET  /health/live   -> 200, Healthy, Cache-Control: no-store
HEAD /health/ready  -> 200, no body, Cache-Control: no-store
POST /health/live   -> 400, Allow: GET, HEAD
POST /health/ready  -> 400, Allow: GET, HEAD
```

Lab 01 §6.2 and the feature tests expect GET/HEAD only and method rejection
with 405. The feature tests run a minimal host without the global re-execution
middleware; `tests/InPolsure.Web.IntegrationTests/Host/HealthHostTests.cs:31`
covers HEAD on the composed host but has no POST rejection assertion.

**Recommended fix:** restrict HTML not-found re-execution to appropriate
GET/404 requests and preserve method/other protocol errors. Add real-host
POST/405 coverage for both health paths. Keep the existing missing-page and
security-header tests green. This does not break normal GET/HEAD probes.

Framework background: [Razor endpoint POST validation](https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/Components/Endpoints/src/RazorComponentEndpointInvoker.cs).
The finding itself is confirmed on the project's .NET 10.0.12 container.

### C-02 — Known UI follow-up: caller CSS classes are discarded by Button

**Location:** `src/InPolsure.Ui/Components/Button.razor:3`.

The explicit `class="@CssClass"` wins over a caller-supplied `class` in
`AdditionalAttributes`; the component never merges the two. Callers cannot
add layout/style classes through the otherwise-supported unmatched attributes.
The existing bUnit attribute test exercises `aria-label` and `name`, not class.

This is already assigned to T-01.9 in
`docs/labs/lab-01-walking-skeleton.md:658`. Retain that task's fix and regression
test; do not treat it as a newly discovered architectural problem.

### C-03 — Known UI follow-up: focus contrast needs the planned correction

**Location:** `src/InPolsure.Ui/Layout/AppShell.razor.css:1` and
`src/InPolsure.Ui/wwwroot/base.css`.

The skip link uses the primary blue background and inherits the amber focus
outline. The lab records approximately 1.3:1 contrast for that combination and
assigns the contrasting focus treatment to T-01.9
(`docs/labs/lab-01-walking-skeleton.md:653`). The brand link already sets a
white outline, but other content in the header slot does not inherit that
special case. Complete the recorded skip-link/header correction and keyboard
focus verification before accepting the accessibility criteria.

## Implementation quality

| Area | Assessment |
|---|---|
| Host composition | Features are registered through small extension methods; Static SSR is default and the probe opts into Interactive Server. The status-code issue above is the only new concrete integration defect found. |
| Health | Liveness/readiness separation, readiness tags, anonymous access, cache prevention and metric/trace exclusions fit the skeleton. Dependency checks are intentionally deferred. |
| Security | Header middleware uses `OnStarting`, preserves Blazor's additional CSP across re-execution, and validates HSTS options at startup. Probe pages are off outside the explicit development/testing switch. |
| Observability | Service identity/version, conditional OTLP export, JSON console output, query-value redaction and suppression of raw request-URL logging match Lab 01. Existing privacy/exporter tests pass. |
| Shared kernel | Clock abstraction is small and justified; no premature domain/infrastructure framework is present. |
| Blazor/UI | Native controls, component CSS, landmarks, skip link and disposed navigation subscription are sensible. Browser correctness remains unproven until T-01.9; retain C-02/C-03. |
| Container/local runtime | Patch/digest-pinned base images, cached restore, chiseled non-root runtime, root-owned app files and loopback-only Compose ports are appropriate. OTLP stays on the internal Compose network. |
| Architecture compliance | Project/package boundaries and current render modes match the approved lab. Business modules, auth, tenancy, outbox and Azure resources are correctly deferred. |

## Verification performed

| Check | Result |
|---|---|
| `dotnet build InPolsure.slnx -c Release --disable-build-servers -m:1` | Passed: zero warnings, zero errors; SDK 10.0.401. |
| `dotnet test InPolsure.slnx -c Release --no-build --no-restore` | Passed: 174 succeeded, zero failed, zero skipped. |
| `dotnet format InPolsure.slnx --verify-no-changes --no-restore` | Passed. |
| `docker build -f src/InPolsure.Web/Dockerfile -t inpolsure-review:2026-10-02 .` | Passed using existing BuildKit cache; not a cold-cache restore/rebuild. |
| Container user inspection | User `1654`, non-root. |
| Release container startup and local HTTP checks | Passed normal health probes; confirmed C-01 for both POST paths. |

Sandbox restrictions initially prevented NuGet vulnerability lookup and SDK
named pipes; the successful build/test/format checks ran with approved access.
Those initial failures were environment restrictions, not source-code defects.
The temporary review container was stopped and automatically removed.

## Limits and remaining work

- The 174 passing tests include placeholders. In particular,
  `tests/InPolsure.Web.UiTests/PlaceholderTests.cs:5` only checks an assembly
  name. It proves no browser behavior. CSP violations, axe, QuickGrid sorting,
  circuit establishment, reconnect UI and 360px layout need T-01.9.
- T-01.9 also already carries a positive control for the log-privacy test and
  extra error-page header assertions. Preserve those requirements.
- Architecture tests inspect compiled references, as required by the lab.
  They do not detect every unused declared reference or prove future module,
  tenant or EF boundaries. Extend their assembly list/rules when those
  modules arrive. Generated-type namespace filtering is already a recorded
  later follow-up.
- CI, Dependabot and README are intentionally pending T-01.10/T-01.11;
  no GitHub checks or repository security settings were verified here.
- Aspire dashboard ingestion, full browser flows, load, recovery, Azure
  deployment and future business-service behavior were not validated.

**Next step:** complete the approved T-01.9/T-01.10 work and T-01.11 integration;
include C-01 as a small host/test correction with explicit ownership. The
current services do not need a redesign.
