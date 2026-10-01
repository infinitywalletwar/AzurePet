# ADR-0009: Infrastructure as code with Bicep; CI/CD with GitHub Actions, OIDC and environment promotion

- **Status:** Accepted (2026-10-01)
- **Date:** 2026-10-01 (amended 2026-10-01 after the architecture review: public repository, migrations as a Container Apps Job, hostname source of truth, base/workload layers, release and teardown workflows; amended again after the compliance re-review: unattended `teardown` identity and environment, email verification in E2E)
- **Deciders:** the user (approver), solution-architect
- **Requirements:** C-03, C-04, C-07, NFR-002, NFR-023, NFR-024, NFR-027, NFR-061, NFR-062, NFR-063, NFR-070–NFR-075, NFR-003, FR-004, FR-020, FR-024, FR-075

## Context

Bicep and GitHub Actions are constraints (C-03, C-04). Dev is permanent;
test and prod are created on demand and deleted (C-07, ADR-0014). All
infrastructure is code (NFR-070). Every change to `main` is built,
tested and scanned; test and prod deploy only from the pipeline; prod
needs manual approval (NFR-071). An environment can be created from
scratch in ≤ 1 h (NFR-072). Schema changes are versioned and
zero-downtime compatible (NFR-074). No secrets in code or pipelines
(NFR-023). **The repository is public** (user decision, 2026-10-01).

## Decision

### Infrastructure as code

1. **Bicep modules** under `infra/` (layout in the first deployment lab
   spec), with two entry templates per environment (ADR-0014):
   - **`base.bicep`** (permanent per environment): resource group
     contents that must survive teardown: user-assigned identities
     (`app`, `migrator`), Key Vault, Log Analytics + Application
     Insights, action group, role assignments.
   - **`workload.bicep`** (dev: permanent; test/prod: on demand):
     Container Apps environment, web app, `migrate` job, SQL server and
     database, storage account, alert rules, availability test (prod).
   - **`.bicepparam` files per environment** (`dev`, `test`, `prod`).
2. **Resource layout:** one subscription. `rg-inpolsure-shared` (ACR,
   GitHub deploy identities and the `teardown` identity with federated
   credentials, the teardown custom role definition, budget; the DNS
   zone is added only when a domain exists) and permanent, empty-able
   `rg-inpolsure-{dev|test|prod}`, all in Poland Central. A **Cost
   Management budget** at subscription scope with 50/80/100% alerts
   (NFR-061).
3. **Naming and tags:** `env`, `app=inpolsure`, `owner`, plus
   `expiresAt` on on-demand workload resources (teardown, ADR-0014). An
   Azure Policy assignment denies regions other than Poland Central (and
   global resources) (NFR-040).
4. **Validation:** `bicep lint` and `az deployment group what-if` on pull
   requests. Normal deployments use incremental mode; only the teardown
   workflow removes resources (ADR-0014).
5. **Tenant hostnames: the repository is the source of truth.** A
   Container App's `ingress.customDomains` is a single array that every
   deployment of the app resource replaces, so bindings added outside the
   template are dropped on the next deploy. Decision: each environment's
   `.bicepparam` holds the complete `tenantHostnames` list; the app
   module always renders all of them (with `bindingType: 'Auto'`,
   available from the `Microsoft.App` API version 2025-07-01, so managed
   certificate and binding deploy in one pass), together with the DNS
   records. Onboarding a hostname is a reviewed pull request that adds
   one line. CLI or portal bindings are not allowed. Before a domain
   exists the list is empty (ADR-0016); if the wildcard spike succeeds
   (ADR-0011) the list disappears.

### CI/CD

6. **Workflows:**
   - `ci.yml` (pull requests and `main`): restore, build, unit tests,
     integration tests against a **SQL Server container** (RLS
     exercised), architecture tests, cross-tenant suite, CSP and axe UI
     checks, format check, Bicep lint and what-if. Security gates (free
     for public repositories): **CodeQL** (default setup), **dependency
     review** on pull requests, **Dependabot** alerts and version
     updates, `dotnet list package --vulnerable`, and **GitHub secret
     scanning with push protection** (blocks pushes that contain
     secrets). High or critical findings fail the build (NFR-027).
   - `cd-dev.yml` (on `main` after CI): **build the image once**, tag
     with the git SHA, push to ACR, record the digest; deploy dev
     (`workload.bicep`, then the `migrate` job, then the new revision);
     smoke tests.
   - `release.yml` (manual): create or update **test** from Bicep →
     `migrate` (and `seed` if new) → deploy the same digest → E2E and
     fault-injection tests (email delivery verified as below) →
     **approval (environment `prod`)** → create
     or update **prod** → `migrate` → deploy → synthetic zero-failure
     check (NFR-002). The prod job fails fast if no platform domain is
     configured (ADR-0016).
     **Email verification in test (FR-020, NFR-003):** test has no real
     email; rendered messages land in the `maildrop` container
     (ADR-0007). The E2E job reads them with the `deploy-test` token
     (*Storage Blob Data Reader* on `maildrop` only, ADR-0012 §1),
     matching on recipient and the event-ID header: registration tests
     extract and follow the verification link; notification tests assert
     exactly one message per event and recipient (FR-075). The
     fault-injection test makes the email adapter fail for a period,
     then asserts that the user action succeeded and the message appears
     in `maildrop` after the failure ends (NFR-003). Flows that do not
     need a link (ticket creation, replies, assignment) sign in as
     **seeded, pre-verified users** from the test `seed`.
   - `teardown.yml` (manual, and scheduled every 6 h (A)): deletes
     on-demand workload resources whose `expiresAt` has passed
     (ADR-0014). It runs in GitHub environment **`teardown`** with the
     `teardown` identity (item 7), so it never waits for the `prod`
     approval.
   - `loadtest.yml` (Later): creates, runs and destroys the load-test
     environment with a time box (NFR-063).
7. **Authentication to Azure: OIDC workload identity federation** via
   `azure/login`. One **user-assigned managed identity per environment**
   (`deploy-{env}`) with a federated credential whose subject is
   **`repo:<owner>/<repo>:environment:<env>`**, so only jobs that target
   that GitHub environment (after its protection rules pass) can obtain
   a token. Each has **Contributor plus Role Based Access Control
   Administrator constrained by condition to the data-plane roles the
   app needs** on its own resource group; `deploy-dev` also has `AcrPush`
   because the image is built once in the dev pipeline. No client
   secrets (NFR-023). Deploy identities have **no SQL rights** and never
   connect to the database. `deploy-dev` and `deploy-test` can read the
   `maildrop` container (email verification above), nothing else on the
   data plane. A separate **`teardown` identity** (subject
   `repo:<owner>/<repo>:environment:teardown`) has only a custom
   read-and-delete role for workload resource types on the test and
   prod resource groups (ADR-0012 §1), so it can remove a forgotten prod
   without approval but cannot create or change one.
8. **GitHub environments** `dev`, `test`, `prod`, `teardown` (available
   with protection rules on public repositories). `prod`: **required
   reviewer = the owner**, deployments only from `main`. `test`:
   deployments only from `main`. `teardown`: **no required reviewer**,
   deployments only from `main` (scheduled runs use the default branch),
   used only by `teardown.yml`. Environment variables hold non-secret
   settings.
9. **Database migrations: Container Apps Job `migrate`.** The same image
   started with the `migrate` command applies each module's EF Core
   migrations in order and an idempotent grants script (creates the
   `app` database user and its role memberships, ADR-0012). It runs with
   the `migrator` identity, which is the server's SQL Entra admin. The
   workflow starts it with `az containerapp job start` and waits for
   success **before** the new revision. GitHub runners never connect to
   SQL, so no runner IP rules are needed. **Expand/contract**: additive
   changes in release N, destructive changes in N+1 or later (NFR-074,
   NFR-002). The same job runs `seed` (synthetic data) and
   `bootstrap-superadmin` (FR-024).
10. **Deploy step:** update the Container App to the new digest
    (single-revision mode); traffic moves when the readiness probe
    passes; a synthetic check records zero failed requests on prod
    (NFR-002).
11. **Rollback:** redeploy the previous digest; expand/contract keeps the
    schema compatible with N−1.
12. **Public-repository hardening:** least-privilege `permissions:` per
    job; third-party actions pinned by commit SHA; no
    `pull_request_target` workflows that check out PR code; workflows
    from first-time or fork contributors require approval; fork pull
    requests never receive OIDC tokens or environment secrets (GitHub
    default); repository holds no secrets at all (NFR-023).

## Alternatives considered

| Option | Pros | Cons | Verdict |
|---|---|---|---|
| **Bicep + GitHub Actions + environment-scoped OIDC + per-env identities (chosen)** | Meets C-03/C-04; no stored secrets; per-env least privilege; same artefact promoted; protection rules free on public repos | Some setup per env | **Chosen** |
| Private repository on the Free plan | Code not public | Environment required reviewers, secret scanning/push protection and CodeQL are not free for private repos on Free; would need workarounds (gitleaks, `workflow_dispatch` restricted to owner) | Rejected by the user's decision |
| Service principal with client secret in GitHub | Simple | Long-lived secret; fails NFR-023 | Rejected |
| One identity for all environments | Fewer objects | Dev workflow could change prod; fails NFR-024 | Rejected |
| Migrations from the GitHub runner (efbundle) | No job resource | Runner IPs change; SQL would need broad firewall openings; runner identity would need DDL rights | Rejected |
| Migrations at app startup | No pipeline step | Races between replicas; app needs DDL rights | Rejected |
| Hostnames bound by CLI in an onboarding workflow | No PR per tenant | Next Bicep deploy overwrites `customDomains` and drops them | Rejected |
| Hostname bindings in a separate template deployed after the app | Onboarding independent of app deploys | Two templates write one array; ordering bugs | Rejected; one list in the param file is simpler |
| `azd` as deployment engine | Fast scaffolding | Extra abstraction | Optional local tool |
| Terraform / Azure DevOps | — | Conflict with C-03 / C-04 | Rejected |
| Separate subscriptions per environment | Strong isolation | More admin | Not now. **Trigger:** real data or a second person with prod access |
| GitHub Container Registry | Free | Pull secret in Container Apps (NFR-023) | Rejected |

## Consequences

**Trade-offs**

- (+) Reproducible environments; test and prod are recreated often, so
  NFR-072 is exercised continuously.
- (+) No credentials in GitHub; each environment's blast radius is its
  resource group; the prod deploy token exists only after the owner
  approves. The unattended teardown token can only read and delete
  workload resources in test and prod (ADR-0012 §1).
- (+) Build once, promote the same digest.
- (−) **Public code:** anyone can read the code, workflows and Bicep.
  Security must not depend on secrecy of the design; no secrets are ever
  committed (NFR-023, push protection); the hardening rules in item 12
  apply.
- (−) Expand/contract needs discipline: two releases for destructive
  schema changes.
- (−) The `migrator` identity is powerful (SQL admin), but it is attached
  only to the job and the job runs only from the pipeline.
- (−) Onboarding a hostname needs a pull request (minutes), until the
  wildcard spike or Front Door removes the list.

**Scalability**

- The pipeline shape does not change with load; the load-test workflow
  uses the same Bicep with larger parameters.

**Operations**

- New environment: one-time, owner-run bootstrap from the repository's
  Bicep (shared layer with the deploy identity and federated credential,
  then `base.bicep`; ADR-0014), then `workload.bicep` only via the
  workflows; target ≤ 1 h (NFR-072). Identities and roles follow the
  inventory in ADR-0012.
- Drift: a scheduled what-if on dev, and on test/prod while they exist
  (NFR-070).

**Cost**

- GitHub Actions minutes on standard runners are free for public
  repositories; CodeQL, dependency review, secret scanning, push
  protection and Dependabot are free for public repositories.
- Azure: deployments are free; ACR Basic about 5 USD/month (ADR-0004);
  the `migrate` job runs for a minute or two per deploy (inside the free
  grant). Design load: no change.
