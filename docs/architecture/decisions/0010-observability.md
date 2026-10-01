# ADR-0010: Observability: OpenTelemetry to Application Insights and Log Analytics

- **Status:** Accepted (2026-10-01)
- **Date:** 2026-10-01 (amended 2026-10-01 after the architecture review: alert latency split, availability test tied to prod's lifetime, permanent workspaces, superadmin sign-in signal)
- **Deciders:** the user (approver), solution-architect
- **Requirements:** NFR-001, NFR-032, NFR-040, NFR-042, NFR-044, NFR-050–NFR-054, NFR-060, NFR-061, NFR-075, FR-024

## Context

We need structured logs, metrics and distributed traces across the host
and background jobs, correlated by an ID (NFR-050). Also: health
endpoints (NFR-051); technical alerts within 5 minutes on SLO burn,
error spikes and failed background jobs, and budget alerts within Cost
Management's own latency (NFR-052, v2.1); dashboards for SLO,
per-tenant traffic and cost (NFR-053); retention of at least 30 days
within budget (NFR-054). Telemetry must carry the tenant ID (NFR-032),
must not contain ticket content or direct personal data (NFR-042), and
must stay in Poland Central (NFR-040). NFR-001 is measured with
synthetic checks every minute. Locally, telemetry must work without
Azure (NFR-075).

## Decision

1. **Instrumentation: OpenTelemetry .NET** (vendor-neutral API) with
   ASP.NET Core, HttpClient, SqlClient/EF Core and runtime
   instrumentation, plus custom `ActivitySource`/`Meter` for the outbox,
   tickets and auth events.
2. **Export: Azure Monitor OpenTelemetry Distro** to a **workspace-based
   Application Insights** resource backed by a **Log Analytics
   workspace per environment**, all in Poland Central. Workspace and
   Application Insights are in the **permanent base layer** (ADR-0014),
   so telemetry from a prod live window (SLO evidence for NFR-001)
   survives prod's teardown for the 30-day retention.
3. **Correlation:** W3C trace context. The outbox stores the
   `traceparent` of the originating request and the dispatcher continues
   the trace. Every log scope includes `TraceId`, `tenant.id` and
   pseudonymous `user.id` (NFR-032, NFR-050).
4. **Privacy:** no request or response bodies are logged. Query strings
   are scrubbed of tokens. `Microsoft.Extensions.Compliance` redaction
   handles classified data (email, names, free text). A test checks log
   output for known personal-data samples (NFR-042).
5. **Cost controls:** a **daily cap** per workspace (dev 0.05 GB, test
   0.05 GB, prod 0.1 GB at run load), **30-day retention** (31 days are
   included in Analytics-logs ingestion), and sampling: 100% at run load,
   rate-limited sampling at design load. Verbose container console logs
   are sent to cheaper Basic logs or not collected.
6. **Health:** `/health/live` (process only) and `/health/ready`
   (database connectivity with a cached result, 30–60 s). In **dev and
   test the readiness check does not touch the database**, so probes do
   not keep the serverless database awake (ADR-0005).
7. **Availability (NFR-001):** an Application Insights **standard
   availability test** once per minute against `/health/ready` and a
   lightweight read-only page. It is part of prod's on-demand workload
   template, so it **exists exactly while prod exists**; no toggling.
   The SLO report covers only time when prod existed (A-11).
8. **Alerts (NFR-052)** via one action group (email to the owner), in
   two latency classes:
   - **Technical, ≤ 5 minutes:** metric alerts (1-minute evaluation)
     for availability-test failures (2 of 3 locations), 5xx rate > 2% for
     5 min, outbox failed messages > 0 or oldest pending > 10 min (custom
     metrics); SQL free-amount remaining < 10% (dev/test); Log Analytics
     daily cap reached. These also act as **fast cost proxies** for the
     usual misconfigurations (a database kept awake, log floods).
   - **Budget, hours:** Cost Management budget alerts at 50/80/100%.
     Cost data typically arrives 8–24 h after usage, budgets are
     evaluated about every 24 h, and emails follow within about an hour
     ([Microsoft Learn](https://learn.microsoft.com/azure/cost-management-billing/costs/tutorial-acm-create-budgets)).
     The scheduled teardown of expired on-demand environments (ADR-0014)
     is the fast guard against forgotten resources.
   - **Security:** every superadmin sign-in emits an audit event, a
     security email to all superadmins (ADR-0003) and the
     `auth.superadmin.signin` metric shown on the platform dashboard. No
     extra log-alert rule is needed.
9. **Dashboards (NFR-053):** Azure Workbooks for SLO, per-tenant request
   and error rates (by `tenant.id`), outbox health and cost (Cost
   Management view linked).
10. **Local:** the **.NET Aspire dashboard** as an OTLP endpoint in dev
    containers. The same instrumentation, exported over OTLP instead of
    Azure Monitor (NFR-075).

## Alternatives considered

| Option | Pros | Cons | Verdict |
|---|---|---|---|
| **OpenTelemetry + Azure Monitor (App Insights / Log Analytics) (chosen)** | Managed, in-region, integrated alerts, availability tests and workbooks; 5 GB/month free per billing account; OTel keeps the code portable | Pay-per-GB beyond the free amount (about 2.30 USD/GB Analytics logs); availability tests billed per execution | **Chosen** |
| Classic Application Insights SDK (non-OTel) | Familiar | Microsoft is moving to OTel; less portable | Rejected |
| Self-hosted Prometheus + Grafana + Loki + Tempo on Container Apps | Free software, powerful | Several always-on containers and storage (~20–50 USD), we operate them; breaks scale-to-zero | Rejected |
| Azure Managed Grafana + Azure Monitor workspace (Prometheus) | Great dashboards | Grafana instance cost exceeds budget | Rejected at run load; reconsider at design load |
| Third-party SaaS (Grafana Cloud, Datadog, …) | Rich features, free tiers | Telemetry leaves Azure (residency review); another sub-processor | Rejected (NFR-040) |
| Classic URL ping tests (free) | Free | **Retired 30 September 2026** (standard tests replace them) | Not available |
| GitHub Actions scheduled job as uptime probe | Free | Minimum 5-minute schedule, unreliable timing; fails "every minute" | Rejected |

## Consequences

**Trade-offs**

- (+) One managed place for logs, traces, metrics, availability tests
  and alerts, in Poland Central.
- (+) OTel instrumentation works locally and could be exported elsewhere
  later without code changes.
- (−) **Daily caps drop data** when exceeded. Sampling and log levels
  must keep normal traffic well under the cap. An alert fires when the
  cap is hit.
- (−) Availability tests cost money per execution (about 0.0005 USD).
  They run only while prod exists, so NFR-001 is measured only then,
  which matches A-11.
- (−) Budget alerts cannot catch a cost spike within the same day; the
  technical cost proxies and the teardown schedule cover that gap.
- (−) Log-search alert rules carry a small per-rule monthly charge. Use
  metric alerts where possible and keep log alerts few.

**Scalability**

- At design load (300 req/s), unsampled telemetry would be tens of GB
  per day. Sampling (e.g. 10–20%) plus metrics for SLOs keeps it at
  5–10 GB/day. Commitment tiers start at 100 GB/day and do not apply.
- Per-tenant cardinality (100–250 tenants) is fine for log dimensions.
  Custom metrics use `tenant.id` only where needed, to limit
  time-series count.

**Operations**

- Each environment has its own workspace, so dev noise does not hit prod
  alerts or caps.
- Alert routing: email to the owner at first. On-call tooling is out of
  scope.
- Runbooks link from alert descriptions (hardening lab).

**Cost (approximate)**

- Run load: 0–5 USD/month for ingestion (mostly inside 5 GB free) plus
  about 0.03 USD per live prod hour per test location for availability
  tests (about 1–4 USD/month at ~40 live hours; 24/7 would be about 22
  USD per location). Idle workspaces cost nothing beyond retained data
  inside the included 31 days.
- Design load: 350–700 USD/month at 5–10 GB/day; load tests are short,
  so the real cost is a few USD per run.
