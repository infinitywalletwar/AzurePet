# ADR-0014: Environments lifecycle: permanent dev, on-demand test and prod

- **Status:** Accepted (2026-10-01)
- **Date:** 2026-10-01 (amended 2026-10-01 after the compliance re-review: unattended `teardown` identity, reduced load-test dataset per Q-B3)
- **Deciders:** the user (approver), solution-architect
- **Requirements:** C-06, C-07, A-07, A-11, NFR-001, NFR-002, NFR-004–NFR-006, NFR-052, NFR-060–NFR-063, NFR-070–NFR-072, NFR-074

## Context

The user decided on 2026-10-01 that **prod is on demand**: it is
created for the labs that need it and deleted afterwards (A-11). The
budget is ≤ 50 USD/month target, 100 USD ceiling (NFR-060), and idle
non-prod environments must cost close to zero (NFR-062). An environment
must be creatable from the repository in ≤ 1 h (NFR-072). Data in every
environment is synthetic (A-07). NFR-001 (99.9%) and the recovery
targets (NFR-004 to NFR-006) are demonstrated only while prod exists
(A-11). Cost Management budget alerts arrive hours after usage
(NFR-052), so they cannot catch a forgotten environment quickly.

Some resources are cheap to keep and painful to recreate: managed
identities (their principal IDs are referenced by SQL users and role
assignments), Key Vault (soft delete and purge protection reserve the
name for the retention period), and the Log Analytics workspace (SLO
evidence from a live window must outlive the window). Others carry most
of the cost and are fast to recreate from Bicep: compute, database,
storage, availability tests.

No platform domain is owned yet; prod requires one (ADR-0016).

## Decision

### 1. Environments

| Env | Lifetime | Purpose | Created / deleted by | Data |
|---|---|---|---|---|
| **local** | Developer machine | Daily development, full multi-tenancy on `<slug>.localhost`, social login and email (Mailpit) (ADR-0016) | Developer (`docker compose` / Aspire) | Seed command |
| **dev** | **Permanent**, scales to zero | Integration of `main`, smoke tests, single demo tenant on the default Azure hostname (ADR-0016) | `cd-dev.yml` on every merge | Synthetic seed, kept |
| **test** | **On demand** (architect's extension of the user's prod decision, to keep idle cost near zero, NFR-062; accepted by the user, Q-B2) | Pre-prod verification, E2E and fault-injection tests | `release.yml` creates or updates it; `teardown.yml` deletes it | Synthetic seed, recreated each time |
| **prod** | **On demand** (user decision); **requires the domain gate** (ADR-0016) | Labs that measure the SLO, zero-downtime deploys, restore drill, real email delivery | `release.yml` after owner approval; `teardown.yml` deletes it | Synthetic, handled as real (A-07); **not kept between windows** |
| **loadtest** (Later) | Hours | Scaling lab (NFR-013, NFR-014) | `loadtest.yml` creates, runs and destroys it | Synthetic; full design dataset (100 tenants, 5M clients) if it fits the NFR-063 cap, otherwise a **reduced dataset with documented extrapolation** to design load (NFR-013 v2.2, user decision Q-B3) |

### 2. Layers: what is persistent and what is on demand

| Layer | Resources | Dev | Test | Prod | Deployed by |
|---|---|---|---|---|---|
| **Shared** (permanent) | `rg-inpolsure-shared`: ACR Basic, `deploy-{env}` identities and the `teardown` identity with federated credentials, the teardown custom role (ADR-0012 §1), subscription budget and Azure Policy (region allow-list). Azure DNS zone **only after the domain gate** | — | — | — | Owner, from Bicep, during bootstrap and on rare reviewed changes |
| **Base** (permanent per env) | `rg-inpolsure-{env}` (kept even when empty), `app` and `migrator` identities and their AcrPull assignments, Key Vault (with its secrets and the Data Protection wrapping key), Log Analytics workspace + Application Insights, action group | permanent | permanent | permanent | Owner, from Bicep (`base.bicep`), during bootstrap and on rare reviewed changes |
| **Workload** | Container Apps environment, web app, `migrate` job, SQL logical server + database, storage account, alert rules, availability test (prod) | **permanent** | **on demand** | **on demand** | Pipeline (`workload.bicep`) with the `deploy-{env}` identity |

Why split this way: the base layer holds the identities, secrets and
telemetry that must survive teardown (ADR-0010, ADR-0012) and costs
about 0 USD idle; the workload layer holds every resource with a
running cost. Infrastructure bootstrap (shared and base layers) is run
by the owner from the repository's Bicep, after a reviewed pull
request; **application deployments and all workload changes happen only
from the pipeline** (NFR-071).

### 3. Creating an on-demand environment

`release.yml` (ADR-0009), parameter `expiresAt` (default: now + 3 days
for test, + 7 days for prod (A); maximum 14 days):

1. Deploy `workload.bicep` with the environment's `.bicepparam` and the
   `expiresAt` tag on every workload resource.
2. Start the `migrate` job (`migrate`, then `seed` in test; in prod
   `migrate` and `bootstrap-superadmin` only; the job refuses `seed`
   in prod, ADR-0012).
3. Deploy the image digest already tested in dev.
4. Prod only: wait for certificate binding of the tenant hostnames
   (ADR-0011), then run the smoke test and send one real email to the
   owner (ADR-0007).
5. Prod only: availability test and alert rules come up with the
   workload, so SLO measurement starts when prod exists (ADR-0010).

Target: test in ≤ 30 min, prod in ≤ 60 min (NFR-072). The first
creation of each is timed and recorded in the environments lab.

### 4. Teardown

- `teardown.yml` runs **manually** (owner ends a window) and **on a
  schedule every 6 hours (A)**. It deletes the **workload resources**
  of test and prod whose `expiresAt` has passed, leaving the resource
  group and base layer. A window can be extended by re-running
  `release.yml` with a later `expiresAt`.
- **Identity:** `teardown.yml` runs in the GitHub environment
  `teardown` (no required reviewer, `main` only) with the
  `id-inpolsure-teardown` identity (ADR-0009 items 7–8, ADR-0012 §1).
  Its custom role can only read and delete workload resource types in
  `rg-inpolsure-test` and `rg-inpolsure-prod`, so the scheduled run can
  remove a forgotten prod **without the owner's approval**, while
  creating or changing prod still needs the approval-gated `deploy-prod`
  token. The workflow selects resources by the expired `expiresAt` tag;
  RBAC additionally keeps it away from dev, the base layer and the
  resource groups themselves.
- Before deleting prod it exports, to the permanent workspace, a short
  run record (window start/end, SLO numbers, restore-drill result),
  which A-11 needs to report "time when prod did not exist".
- Teardown never touches dev, the shared layer or any base layer.
- Deletion order: Container App and job, Container Apps environment,
  SQL server, storage account, alert rules. Storage soft delete and SQL
  backups go with their parents.

### 5. Data

- **Dev:** synthetic seed (three demo tenants in the database; one of
  them is mapped to the default hostname, ADR-0016), kept.
- **Test:** re-seeded on every creation; nothing carried over.
- **Prod:** created empty, then the superadmin bootstrap. Tenants are
  created through the superadmin panel (FR-001) and synthetic users and
  tickets through the application's public flows (a scripted synthetic
  traffic run), so prod never receives data by a back door. **Nothing is kept between windows**: deleting
  the SQL server deletes its backups, and deleting the storage account
  deletes blobs, versions and the Data Protection key ring. NFR-004 to
  NFR-006 are demonstrated within a window: 30-day PITR configured,
  point-in-time restore to a new database during the window (hardening
  lab). This is accepted because data is synthetic (A-07, A-11).
- **Before real data**, this ADR is superseded: prod becomes permanent,
  backups must outlive any teardown, and private networking applies
  (ADR-0015).

### 6. Cost guards

- Scheduled teardown is the **fast guard** (≤ 6 h; unattended, with the
  `teardown` identity, §4); budget alerts at
  50/80/100% are the slow guard (hours, NFR-052); technical alerts on
  SQL free-amount and Log Analytics cap act as cost proxies (ADR-0010).
- Every workload resource is tagged `env`, `expiresAt`, `app`, `owner`;
  a Cost Management view groups cost by `env`.

## Alternatives considered

| Option | Idle cost | Fidelity | Effort | Verdict |
|---|---|---|---|---|
| **Permanent dev, on-demand test and prod, permanent base layer (chosen)** | Dev ≈ 0 (scale to zero, free offer); test and prod 0 when absent; base ≈ 0 | Prod is recreated each window, so NFR-072 is exercised often; SLO only per window | Two Bicep entry points; teardown workflow | **Chosen** |
| Everything permanent, prod scaled to zero outside windows (v1 design) | Prod SQL storage, availability test toggling, certificate renewal on an idle app; 10–30 USD/month more if a replica stays on | Continuous prod | Simple | Rejected by the user's decision |
| Test permanent (scale to zero), only prod on demand | Test ≈ 0–2 USD/month (free-offer DB, scale-to-zero) | Test always ready | Slightly simpler | Viable; not chosen because on-demand test costs nothing and keeps test identical to how prod is built. Reverting is a parameter change |
| Delete whole resource groups (including identities, Key Vault, workspace) | 0 | Telemetry of the window lost; Key Vault name blocked by purge protection; identities and SQL users recreated each time | Simple teardown | Rejected |
| Azure Deployment Environments / `azd down` | 0 | Same | Extra service or tool; not needed for one person | Rejected |
| Stop/start (keep resources, stop compute) | SQL storage, storage account, availability test still cost; certificate renewal issues | Continuous config | Medium | Rejected |

## Consequences

**Trade-offs**

- (+) Lowest possible idle cost; the budget is spent only during labs.
- (+) Environments are proven reproducible every time (NFR-072).
- (+) Identities, secrets and telemetry are stable across windows.
- (−) **Prod backups and data do not outlive prod**; NFR-004 to NFR-006
  are shown inside a window only (A-11).
- (−) Each prod creation re-issues managed certificates and takes up to
  about an hour.
- (−) The bootstrap of shared and base layers is an owner-run step
  outside the pipeline (rare, reviewed, still Bicep).
- (−) **Prod cannot be created before the domain gate** (ADR-0016);
  labs that need prod must wait for, or trigger, the domain purchase.
- (−) Forgotten extensions can keep test or prod alive up to their
  `expiresAt` (max 14 days).

**Scalability**

- The loadtest environment uses the same workload template with larger
  parameters; nothing in the lifecycle changes at design load.
- When prod becomes permanent (real data), the base/workload split stays;
  only teardown is disabled for prod.

**Operations**

- Runbooks: create window, extend window, end window, recover from a
  failed teardown (re-run is idempotent).
- The owner checks the Cost Management view after each window.

**Cost (approximate)**

- Shared: ACR Basic about 5 USD/month; DNS zone about 0.50 USD/month
  after the domain gate.
- Base per env: about 0 USD idle (identities free; Key Vault per
  operation; workspace pays only for ingestion beyond the free 5 GB per
  billing account).
- Dev workload: about 0 USD (free grant, free offer).
- Test: about 0 USD per window (free offer, scale to zero).
- Prod: about 0.3–0.7 USD per live hour (1–2 replicas, paid serverless
  SQL, availability test), so about 15–30 USD in a month with ~40 live
  hours, 0 in months without a prod lab (ARCHITECTURE §9.7).
- Design load (steady state, if prod became permanent at full scale):
  see ARCHITECTURE §9.7; the lifecycle itself adds nothing.
