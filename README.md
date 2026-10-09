# InPolsure

InPolsure is a multi-tenant SaaS ticket management system for the
insurance industry: insurers, brokers and agencies (tenants) get a
branded client portal where their clients raise and track
customer-service requests and claims (including first notice of loss),
and a staff workspace where employees handle them. It is a
production-oriented learning project built on .NET / ASP.NET Core,
Blazor and Microsoft Azure, delivered incrementally in labs. The
repository is still named `AzurePet`.

Design documents (`docs/` is the source of truth):

- [Requirements](docs/REQUIREMENTS.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Roadmap](docs/ROADMAP.md)
- [Delivery process](docs/DELIVERY.md) (branches, CI/CD, environments, releases)
- [Architecture decision records](docs/architecture/decisions/README.md)
- [Lab specifications](docs/labs/) (current: [Lab 01, walking skeleton](docs/labs/lab-01-walking-skeleton.md))

## Prerequisites

| Tool | Version | Used for |
|---|---|---|
| .NET SDK | 10.0.401 or a later 10.0.4xx | Build, test, run (pinned by `global.json`) |
| Docker | Docker Desktop or Docker Engine with Compose v2 | Container image, `docker compose` |
| PowerShell 7 (`pwsh`) | 7.x | Installing Playwright browsers |
| Git | 2.40 or later | Source control, worktrees |

No Azure subscription is needed to build, test or run locally.

Check your setup from the repository root:

```bash
dotnet --version   # must print 10.0.4xx
docker --version
pwsh --version
git --version
```

OS notes: commands are shown for a POSIX shell (macOS, Linux). On
Windows use PowerShell and set environment variables with
`$env:NAME = "value"` instead of the `NAME=value command` prefix. On
Linux, `playwright.ps1 install --with-deps` installs system packages and
may ask for `sudo`.

## Build, test and format

```bash
dotnet build InPolsure.slnx -c Release
dotnet test InPolsure.slnx -c Release
dotnet format InPolsure.slnx --verify-no-changes
```

The build treats warnings as errors, so a clean build has zero
warnings. `dotnet test` on the solution runs the unit, integration,
architecture and browser (UI) tests; install the Playwright browsers
first (see [UI tests](#ui-tests)).

Run one project, or a subset with a filter:

```bash
dotnet test tests/InPolsure.UnitTests -c Release
dotnet test tests/InPolsure.Web.IntegrationTests -c Release --filter "FullyQualifiedName~Health"
```

### Test runner notes (Microsoft.Testing.Platform)

Tests run on Microsoft.Testing.Platform (MTP), selected in `global.json`
(`"test": { "runner": "Microsoft.Testing.Platform" }`), not on VSTest.

- VSTest options such as `--logger trx` and `--collect` do not work.
- A filter that matches zero tests in a project exits with **code 8**.
  This also applies to a filtered run over the whole solution when some
  projects have no matching tests, so run filters against the project
  that contains the tests.
- An unknown option exits with **code 5** (see
  [MTP exit codes](https://aka.ms/testingplatform/exitcodes)).
- TRX reports come from xUnit's built-in MTP reporter:

  ```bash
  dotnet test InPolsure.slnx -c Release --report-xunit-trx --results-directory ./TestResults
  ```

  The generic MTP options `--report-trx` and `--coverage` need the
  `Microsoft.Testing.Extensions.TrxReport` and
  `Microsoft.Testing.Extensions.CodeCoverage` packages, which are not
  referenced yet; today they fail with exit code 5.
- List all options of a test project with
  `dotnet test tests/InPolsure.UnitTests -c Release --help`.

## Run locally

### With `dotnet run`

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/InPolsure.Web
```

The app listens on <http://localhost:5000> (there is no
`launchSettings.json`, so Kestrel's default applies). Stop it with
Ctrl+C. The `Development` environment enables the UI probe page,
disables HSTS and shows the developer exception page.

### With Docker Compose (app and Aspire dashboard)

```bash
docker compose up --build        # add -d to run in the background
docker compose down              # stop and remove the containers
```

| URL | What |
|---|---|
| <http://localhost:8080> | The app, built from `src/InPolsure.Web/Dockerfile` |
| <http://localhost:18888> | Aspire dashboard: traces, metrics and structured logs |

Both ports are bound to `127.0.0.1` only. The dashboard is anonymous,
which is acceptable because it is loopback-only and holds local
telemetry. Compose sets `ASPNETCORE_ENVIRONMENT=Development` and
`OTEL_EXPORTER_OTLP_ENDPOINT=http://aspire-dashboard:18889`.

### What to look at

Use port 5000 (`dotnet run`) or 8080 (Compose):

| Path | Expected |
|---|---|
| `/` | Home page, Static SSR, no Blazor circuit |
| `/_probe/interactive` | UI probe page (Interactive Server, QuickGrid, button); 404 unless probe pages are enabled |
| `/health/live` | `Healthy` (liveness) |
| `/health/ready` | `Healthy` (readiness) |

Check the security headers with a GET request (`HEAD /` returns 405):

```bash
curl -fsS -D - -o /dev/null http://localhost:8080/ | grep -i '^content-security-policy:'
```

## UI tests

Browser tests (Playwright with axe accessibility and CSP checks) live in
`tests/InPolsure.Web.UiTests` and start the app on a real Kestrel port
themselves. Build the project, install Chromium once, then run them:

```bash
dotnet build tests/InPolsure.Web.UiTests -c Release
pwsh tests/InPolsure.Web.UiTests/bin/Release/net10.0/playwright.ps1 install --with-deps chromium
dotnet test tests/InPolsure.Web.UiTests -c Release
```

CI uses Chromium only; you can install `firefox` or `webkit` the same
way to try them locally.

## Configuration

Configuration uses the standard ASP.NET Core sources, later ones
overriding earlier ones:

1. `src/InPolsure.Web/appsettings.json` (production-safe defaults)
2. `src/InPolsure.Web/appsettings.Development.json` (local overrides)
3. Environment variables, with `__` as the section separator
   (`Security:Hsts:Enabled` becomes `Security__Hsts__Enabled`)

Options are validated at startup; an invalid value stops the app.

| Key | Default | Development | Purpose |
|---|---|---|---|
| `Security:Csp:ReportOnly` | `false` | `false` | `true` sends the CSP as `Content-Security-Policy-Report-Only` |
| `Security:Hsts:Enabled` | `true` | `false` | Emits `Strict-Transport-Security` |
| `Security:Hsts:MaxAgeSeconds` | `31536000` | — | HSTS `max-age`, at least one year |
| `Security:Hsts:IncludeSubDomains` | `false` | — | HSTS `includeSubDomains` |
| `Diagnostics:EnableUiProbePages` | `false` | `true` | Enables `/_probe/interactive` |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | unset | unset (Compose sets it) | OTLP export target; nothing is exported when unset |

Example: run locally with the probe page switched off:

```bash
ASPNETCORE_ENVIRONMENT=Development Diagnostics__EnableUiProbePages=false dotnet run --project src/InPolsure.Web
```

The full per-environment map is in [DELIVERY.md §10](docs/DELIVERY.md#10-environments-and-configuration).

## No secrets in the repository

- Never commit secrets, keys, passwords or connection strings with
  credentials, and never commit `.env` files (they are git-ignored).
- Local secrets go only into `dotnet user-secrets`
  ([ADR-0012](docs/architecture/decisions/0012-secrets-and-configuration.md)).
  None are needed yet; the lab that introduces the first one documents
  its keys.
- Deployed environments use managed identities and Key Vault; GitHub
  holds no secrets (OIDC).
- GitHub secret scanning with push protection is enabled and blocks
  pushes that contain secrets.

## Repository layout

```text
/
├─ InPolsure.slnx                 # solution
├─ global.json                    # .NET SDK pin and test runner (MTP)
├─ Directory.Build.props          # common build properties, version
├─ Directory.Packages.props       # central package versions
├─ compose.yaml                   # web + Aspire dashboard (local)
├─ src/
│  ├─ InPolsure.Web/              # host: Program.cs, Components, Health, Security, Observability, Dockerfile
│  ├─ InPolsure.SharedKernel/     # IClock, SystemClock
│  └─ InPolsure.Ui/               # Razor class library: shell, components, base CSS
├─ tests/
│  ├─ InPolsure.UnitTests/              # SharedKernel and Ui (bUnit)
│  ├─ InPolsure.Web.IntegrationTests/   # WebApplicationFactory
│  ├─ InPolsure.ArchitectureTests/      # project dependency rules
│  └─ InPolsure.Web.UiTests/            # Playwright and axe, real Kestrel
├─ .github/                       # CI workflow, Dependabot, PR template
└─ docs/                          # requirements, architecture, ADRs, labs
```

Package versions live only in `Directory.Packages.props`; project files
contain no `Version` attributes.

## Contributing

Work follows [DELIVERY.md](docs/DELIVERY.md): each lab has a branch
`lab/NN` and a spec in `docs/labs/` that splits the work into tasks and
waves. Each task is developed on its own branch `task/NN/T-NN.x-slug`
(in its own git worktree), reviewed, and squash-merged into `lab/NN`;
the lab is merged into `main` through a pull request with green CI.
Before you open a pull request, run the build, test and format commands
above.
