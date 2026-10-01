# ADR-0004: Compute hosting: Azure Container Apps (Consumption plan)

- **Status:** Accepted (2026-10-01)
- **Date:** 2026-10-01 (amended 2026-10-01 after the architecture review: on-demand prod with min 1 replica, Container Apps Jobs for one-shot commands, regional fallbacks, pre-domain ingress)
- **Deciders:** the user (approver), solution-architect
- **Requirements:** NFR-001, NFR-002, NFR-010, NFR-013, NFR-017, NFR-018, NFR-043, NFR-051, NFR-060, NFR-062, NFR-074, NFR-075, FR-004, C-05, C-06, C-07, A-11

## Context

ADR-0001 gives one container image that serves Blazor (Static SSR and
Interactive Server with SignalR WebSockets), HTTP endpoints and
background hosted services, plus one-shot commands (`migrate`, `seed`,
`bootstrap-superadmin`). The compute must:

- Cost close to zero when dev is idle (NFR-062). Test and prod exist only
  on demand (ADR-0014).
- Deploy without downtime (NFR-002) and support 99.9% while prod exists
  (NFR-001).
- Keep background work (emails ≤ 2 min, exports ≤ 24 h) running while
  prod exists (NFR-017, NFR-043).
- Support WebSockets with **session affinity** (ADR-0008).
- Serve a default Azure hostname now (ADR-0016) and many tenant hostnames
  with TLS once a domain exists (FR-004, ADR-0011).
- Scale to 300 req/s and about 3,000 concurrent staff circuits (NFR-013).
- Run in Poland Central (C-05).

## Decision

Host the web host on **Azure Container Apps, Consumption profile of a
workload-profiles environment**, one environment per Azure environment:

| Setting | Dev (permanent) | Test (on demand) | Prod (on demand) |
|---|---|---|---|
| Replica size | 0.5 vCPU / 1 GiB | 0.5 vCPU / 1 GiB | 0.5–1 vCPU / 1–2 GiB (tuned in scaling lab) |
| Min / max replicas | 0 / 1 | 0 / 2 | **1 / 5 whenever prod exists** (2 / 5 while the SLO is measured) |
| Revision mode | Single | Single | Single (needed for sticky sessions) |
| Session affinity | Sticky | Sticky | Sticky |
| Scale rule | HTTP concurrency | HTTP concurrency | HTTP concurrency (tuned) |
| Probes | Startup, liveness `/health/live`, readiness `/health/ready` | same | same |
| Termination grace period | 60 s (dispatcher stops claiming, releases leases; ADR-0007) | same | same |
| Ingress | External HTTPS, HTTP/1.1 + WebSockets, TLS 1.2+; **default Azure FQDN** (pre-domain, ADR-0016) | same | Tenant hostnames under the platform domain (prod requires a domain, ADR-0016) |
| Identity | User-assigned managed identity `app` (ADR-0012) | same | same |
| Networking | No VNet; public ingress (ADR-0015) | same | same until the private-networking trigger |

**Container Apps Job `migrate`** (manual trigger, parallelism 1, same
image, its own `migrator` identity) runs the one-shot commands from the
pipeline (ADR-0009). The GitHub runner never connects to SQL.

There is no "prod scaled to zero" state any more: prod is **deleted**
between labs (ADR-0014), so while it exists it keeps at least one
replica. This makes background work in prod independent of incoming
traffic, and removes the earlier concerns about managed-certificate
renewal on a stopped app.

### Regional availability and fallbacks (C-05)

The first deployment lab checks, before writing Bicep:
`az provider show -n Microsoft.App` (region list) and
`az containerapp env workload-profile list-supported -l polandcentral`
(workload profiles). Container Apps is reported as operating in Poland
Central by the per-region status feed
([statusfield](https://statusfield.com/services/microsoft-azure-europe/azure-container-apps/azure-container-apps-poland-central));
no Microsoft Learn page listing Container Apps regions could be
retrieved. If the check fails, in this order:

1. **Consumption-only environment** (no workload profiles) in Poland
   Central: same app model, scale-to-zero and Jobs; fewer future options
   (no Dedicated profiles). Configuration change only.
2. **App Service in Poland Central**: dev on Basic B1 (accepting a
   restart on deploy in dev only), test and prod on **Premium v3 P0v3
   with a staging slot** for zero-downtime swaps (NFR-002). Because test
   and prod are on demand, P0v3 is paid only while they exist (roughly
   0.1 USD per hour; check the calculator). Background work runs
   in-process as now; one-shot commands run as a pipeline step through
   a WebJob or a slot-scoped startup command (spike).
3. **Container Apps in another EU region** (e.g. Germany West Central
   or Sweden Central) for **all** resources, documented as an NFR-040
   exception, so the app and the database stay in one region
   (cross-region database calls would add about 10–20 ms per round trip).

Each fallback is recorded in a superseding ADR if used.

## Alternatives considered

| Option | Idle cost (NFR-062) | Zero-downtime deploy (NFR-002) | WebSockets + affinity | Hostnames / TLS | Scale to design load | Monthly cost at run load | Verdict |
|---|---|---|---|---|---|---|---|
| **Container Apps, Consumption (chosen)** | Scales to zero; free grant 180,000 vCPU-s, 360,000 GiB-s, 2M requests per subscription per month ([Learn](https://learn.microsoft.com/azure/container-apps/billing)) | Revisions with readiness-gated traffic switch | Yes; sticky sessions in single-revision mode ([Learn](https://learn.microsoft.com/azure/container-apps/sticky-sessions)) | Default FQDN with TLS; custom domains with free managed certs (no wildcard) or BYO certs ([Learn](https://learn.microsoft.com/azure/container-apps/custom-domains-managed-certificates)) | Autoscale; Dedicated profiles if needed | Dev ~0; prod ~0.05–0.11 USD per live hour (1–2 replicas) | **Chosen** |
| App Service Basic B1 | No scale to zero; ~13 USD/month per plan | No slots in Basic → restart on deploy | Yes | Custom domains, managed certs | Limited (3 instances) | ~13 USD per env | Rejected: fails NFR-002; kept only for dev in fallback 2 |
| App Service Premium v3 P0v3 | No scale to zero | Slots | Yes | Yes | Autoscale | ~60+ USD always on; cheap per hour on demand | Fallback 2 for test/prod |
| Azure Functions (Flex Consumption) | Scale to zero | Yes | Blazor Server circuits do not fit | — | — | — | Rejected |
| AKS | Node pool ≥ 1 VM | Yes | Yes | Yes | Excellent | ~70+ USD | Rejected: ops and cost; PLAN non-goal |
| Container Apps, Dedicated workload profile | Plan fee + instances | Yes | Yes | Yes | Predictable | ~50+ USD | **Design-load option** |

**Availability (NFR-001):** App Service (Basic and above) and Container
Apps carry a 99.95% SLA, Azure SQL Database 99.99%
([Microsoft SLA for Online Services](https://www.microsoft.com/licensing/docs/view/Service-Level-Agreements-SLA-for-Online-Services));
check the wording when the environments lab starts. The composite of
about 99.94% is above the 99.9% target; the application's own
availability then depends on replicas and deployments, hence 2 replicas
while the SLO is measured.

## Consequences

**Trade-offs**

- (+) Lowest idle cost and readiness-gated zero-downtime deploys on the
  cheapest plan.
- (+) The image runs identically on a laptop (NFR-075) and in CI.
- (+) **Jobs** run migrations, seeding and bootstrap inside Azure, next
  to the database, with their own identity.
- (−) **Cold starts** in dev and test (replica plus paused database,
  ADR-0005). No targets apply there.
- (−) **Sticky sessions require single-revision mode**: no traffic
  splitting; old circuits reconnect after a deploy (risk R-05).
- (−) **Tenant hostnames** (once a domain exists): managed certificates
  are not wildcard; hostnames are bound from a list in the parameter
  files (ADR-0009, ADR-0011).
- (−) Zone redundancy needs a VNet-integrated environment; not enabled
  at run load (accepted while data is synthetic; design target enables
  it).
- (−) Needs a registry: ACR Basic (~5 USD/month, shared, pulled with
  managed identity). GitHub Container Registry would need a stored pull
  secret (NFR-023).

**Scalability**

- HTTP-concurrency autoscale; design load estimated at 4–6 replicas of
  2 vCPU / 4 GiB (scaling lab, NFR-013). Dedicated profile is a
  configuration change. Worker split (ADR-0007) is a second Container
  App from the same image.

**Operations**

- Bicep defines environment, app, job and scale rules (ADR-0009);
  rollback redeploys the previous digest.
- System and console logs go to the environment's Log Analytics
  workspace with a daily cap (ADR-0010).

**Cost (approximate, US list prices)**

- Run load: dev 0 USD (free grant). Test about 0 USD per window (scales
  to zero). Prod about 0.05 USD per live hour per 0.5 vCPU / 1 GiB
  replica (0.000024 USD/vCPU-s, 0.000003 USD/GiB-s), largely inside the
  free grant at ~40 live hours/month. ACR Basic ~5 USD/month.
- Design load: 600–1,000 USD/month for 4–6 large replicas (or a
  Dedicated profile); load tests are time-boxed (NFR-063).
