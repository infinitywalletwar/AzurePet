# ADR-0005: Relational database: Azure SQL Database serverless General Purpose (free offer for dev and test)

- **Status:** Accepted (2026-10-01)
- **Date:** 2026-10-01 (amended 2026-10-01 after the architecture review: paid serverless for on-demand prod, no audit partitioning, purge procedure, migrations from a Container Apps Job, free-offer fallback)
- **Deciders:** the user (approver), solution-architect
- **Requirements:** NFR-004, NFR-005, NFR-006, NFR-010–NFR-013, NFR-018, NFR-022, NFR-024, NFR-030–NFR-033, NFR-040, NFR-044, NFR-060, NFR-062, NFR-074, FR-053, FR-102, C-05, C-06, A-11, A-12

## Context

All business data is relational and transactional: tickets, messages,
timeline, outbox, audit and identity. Use cases need ACID transactions
across several tables (ADR-0001, ADR-0007). The isolation model needs
Row-Level Security (ADR-0002). Design load: 5M tickets/year, about 30M
messages/year, about 100 GB/year of structured data, p95 ≤ 1.5 s on agent
search for a tenant with 3M tickets. Recovery: RPO ≤ 15 min, 30-day
backup retention, RTO ≤ 4 h, demonstrated while prod exists (A-11).
Budget: 50–100 USD/month. Region: Poland Central (no paired region).
Prod is created on demand and deleted after use (ADR-0014).

## Decision

Use **Azure SQL Database, vCore General Purpose, serverless compute**,
one database on one logical server per environment:

| Setting | Dev (permanent) | Test (on demand) | Prod (on demand) |
|---|---|---|---|
| Offer | **Free offer** (100,000 vCore-s, 32 GB data, 32 GB backup per DB per month; up to 10 DBs per subscription) | Free offer | **Normal (paid) serverless GP**: 30-day PITR is needed and the free offer's auto-pause mode limits PITR to 7 days ([Learn](https://learn.microsoft.com/azure/azure-sql/database/free-offer)) |
| Behaviour at free limit | Auto-pause until next month | Auto-pause until next month | n/a |
| vCores | 0.5–2 | 0.5–2 | 0.5–4 (raised in scaling lab) |
| Auto-pause delay | 15 min (minimum) | 15 min | 60 min; **disabled while the SLO is measured** |
| PITR retention | 7 days (offer limit) | 7 days | **30 days** (NFR-006) |
| Backup redundancy | LRS | LRS | ZRS if offered for serverless GP in Poland Central, else LRS (in-region; NFR-040) |
| Authentication | **Microsoft Entra only** (SQL auth disabled). SQL Entra admin = the environment's `migrator` identity; the app uses the `app` identity (ADR-0012) | | |
| Network | Public endpoint, TLS 1.2 minimum, firewall rule "Allow Azure services and resources" (ADR-0015) | | |

Supporting decisions:

- **EF Core** (SQL Server provider), one `DbContext` per module and
  schema (ADR-0001). `EnableRetryOnFailure` handles transient errors and
  auto-resume; `rowversion` for optimistic concurrency (FR-053).
- **Row-Level Security** with `SESSION_CONTEXT` (ADR-0002).
- **Migrations** run from the Container Apps Job `migrate` inside Azure
  (ADR-0009), not from GitHub runners: runner IP addresses change, and a
  Consumption environment without a VNet has no stable outbound IP to
  allow-list, so neither side can be pinned by IP. Entra-only
  authentication is the control (ADR-0015).
- **Audit events (FR-102)** go in one regular table `audit.AuditEvents`
  (tenant-scoped, RLS). **No partitioning at MVP.** The app identity has
  `INSERT, SELECT` and `DENY UPDATE, DELETE` on the table. Retention
  (2 years) is enforced by the stored procedure
  `audit.usp_PurgeExpired`, which deletes only rows older than 2 years,
  in batches of a few thousand rows. It is owned by `dbo` like the table,
  so ownership chaining lets the app identity execute it without holding
  `DELETE` itself. A maintenance job calls it once per tenant scope per
  day (RLS still applies). No application user, superadmins included, can
  change or delete other audit rows (FR-102).
  **Trigger for monthly partitioning** (partition switch-out instead of
  delete): the daily purge takes longer than about 10 minutes, or the
  audit table exceeds about 100 GB (design load reaches ~50 GB/year).
  Append-only **ledger tables were rejected** because they forbid
  deleting rows, which makes the 2-year retention impossible.
- **Search** (FR-050): tenant-leading indexes and keyset paging; Azure
  SQL full-text is the first step for FR-052 (Later).

### Fallback if the free offer is not available in Poland Central

The free offer is region-bound per subscription (the first free DB's
region applies to all). The first data lab confirms it can be created in
Poland Central. If not, dev and test use **paid serverless GP with
auto-pause** (billed per second when active, plus storage of about
0.12 USD/GB-month; a few USD/month at dev usage) or **DTU Basic** (about
5 USD/month, 2 GB, 7-day PITR, RLS supported). The choice is made on the
measured dev usage; nothing else changes.

## Alternatives considered

| Option | Cost at run load | Idle / scale-to-zero | Availability SLA | Backups / RPO | Fit (RLS, EF Core, scale path) | Poland Central | Verdict |
|---|---|---|---|---|---|---|---|
| **Azure SQL DB serverless GP; free offer for dev/test, paid for on-demand prod (chosen)** | Dev/test 0 USD within free limits; prod ≈ 0.26–0.52 USD per active hour at 0.5–1 vCore | Auto-pause (min delay 15 min); per-second billing when active ([Learn](https://learn.microsoft.com/azure/azure-sql/database/serverless-tier-overview)) | 99.99% | Log backups ~every 10 min, PITR 1–35 days ([Learn](https://learn.microsoft.com/azure/azure-sql/database/automated-backups-overview)) | RLS, `SESSION_CONTEXT`, rowversion, full-text, mature EF Core provider; Hyperscale for growth | Serverless up to 80 vCores (no zone support at 80) ([Learn](https://learn.microsoft.com/azure/azure-sql/database/region-availability#serverless-region-availability)) | **Chosen** |
| Free offer in prod with "continue using database for additional charges" | 0 within free limits | Same | 99.99% | PITR limit for this mode undocumented | Same | Same | Rejected: cannot be reverted, 30-day PITR unverified; on-demand prod makes paid serverless cheap enough |
| Azure SQL DB provisioned GP or DTU Basic/S0 | GP 2 vCore several hundred USD; Basic ~5, S0 ~15 USD | No pause | 99.99% | Basic: PITR max 7 days | Basic/S0 too small for design load | Yes | Basic is the dev/test fallback; provisioned GP is the **design-load** tier |
| Azure Database for PostgreSQL Flexible, Burstable B1ms | ~13–15 USD/month per env | Stop ≤ 7 days | 99.9% without HA | PITR 7–35 days | RLS, Npgsql; no serverless | Not verified (not chosen) | Rejected: cost, weaker idle story |
| Azure Cosmos DB | Free tier 1,000 RU/s + 25 GB | Serverless | 99.99% | Continuous backup | No RLS; cross-partition transactions; relational queries hard | Yes | Rejected: relational workload |
| SQL Server/PostgreSQL in a container | Compute only | Yes | None | We own backups | Full features | Yes | Rejected: fails NFR-004/006 without significant work |

## Consequences

**Trade-offs**

- (+) Dev and test databases cost about 0 USD; prod costs only while it
  exists.
- (+) 99.99% SLA and ~10-minute log backups meet RPO ≤ 15 min (NFR-004).
- (+) RLS, `SESSION_CONTEXT`, rowversion and full-text are built in.
- (+) A single audit table with a batched purge is simpler than
  partitions; the trigger keeps the cheaper purge path available.
- (−) **Auto-pause cold start** in dev/test: the first connection waits
  while the database resumes; retry logic and a "starting" state. Probes
  and pollers must not keep the database awake (ADR-0007, ADR-0010).
- (−) **Free-offer limits** (dev/test): 32 GB, 100,000 vCore-s/month
  (≈ 55 h at 0.5 vCore), 7-day PITR, LRS backups.
- (−) **Prod backups do not outlive prod.** Deleting the logical server
  deletes its backups. NFR-004/006 are demonstrated while prod exists:
  30-day PITR configured, a point-in-time restore drill to a new
  database during a live window (NFR-005, hardening lab). Accepted
  because data is synthetic (A-07, A-11).
- (−) **No geo-redundant backups**: Poland Central has no paired region;
  accepted by A-12.
- (−) Single-tenant restore needs restore-to-new-database plus a
  filtered copy (hardening-lab runbook).

**Scalability**

- Serverless GP up to 80 vCores in Poland Central. Design load estimated
  at 8–16 vCores provisioned GP or Hyperscale (scaling lab).
- ~100 GB/year fits General Purpose for several years; Hyperscale
  standard-series is the path (premium-series not listed for Poland
  Central).
- Large tenants move via `DataLocation` (ADR-0002, NFR-033).
- Zone redundancy is a configuration change on GP or Hyperscale.

**Operations**

- Migrations: `migrate` job with the `migrator` identity, expand/contract
  (ADR-0009, NFR-074).
- Monitoring: database metrics and Query Store; alert on the free-offer
  "free amount remaining" metric below 10% in dev/test (NFR-061).

**Cost (approximate, US list prices)**

- Run load: dev 0, test 0. Prod about 0.26–0.52 USD per active hour
  (serverless GP ≈ 0.52 USD per vCore-hour; auto-pause between uses)
  plus storage prorated (~0.12 USD/GB-month); about 10–20 USD in a month
  with ~40 live hours.
- Design load: 1,500–3,000 USD/month (provisioned GP 8–16 vCores
  zone-redundant, or Hyperscale), only during load tests at first.
