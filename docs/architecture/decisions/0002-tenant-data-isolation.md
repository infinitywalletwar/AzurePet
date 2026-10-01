# ADR-0002: Tenant data isolation: shared database with TenantId and Row-Level Security

- **Status:** Accepted (2026-10-01)
- **Date:** 2026-10-01 (amended 2026-10-01 after the architecture review: platform scope, table classes, no RLS-bypass user, deferred connection factory; amended again after the compliance re-review: audit only for superadmin scope switches, `seed` on the scope-factory allow-list, invitation after hostname *Ready*)
- **Deciders:** the user (approver), solution-architect
- **Requirements:** NFR-030, NFR-031, NFR-032, NFR-033, NFR-014, NFR-012, NFR-018, FR-022, FR-024, FR-028, FR-082, FR-083, FR-100, FR-102, C-06; PLAN principle 7

## Context

InPolsure hosts 100 tenants (design headroom to 250) with 50,000
clients on average, 500,000 in the largest, and about 5M in total.
Cross-tenant data access must be impossible by construction and covered
by tests (NFR-030, NFR-031). It must be possible to move one large
tenant to dedicated capacity later without changing behaviour
(NFR-033). One tenant's FNOL surge (10× for 2 hours) must not break
other tenants (NFR-014). The run budget is 50–100 USD/month (C-06).
The relational store is Azure SQL Database (ADR-0005).

Not every row belongs to a tenant. Superadmin accounts and platform
audit events (FR-024, FR-100), the tenant catalog that is read **before**
a tenant is known (FR-004), the outbox that a dispatcher drains for all
tenants (ADR-0007), and the social-login handoff read by the central
auth host (ADR-0003) all need a defined place in a fail-closed model.

## Decision

Use a **shared database with a shared schema**. Isolation is enforced in
**two independent layers** (EF Core and Azure SQL Row-Level Security),
with an explicit, tested list of exemptions.

### 1. Scopes: every request and job runs in exactly one scope

| Scope | `TenantId` in `TenantContext` and `SESSION_CONTEXT` | Set by |
|---|---|---|
| **Tenant** | The resolved tenant's ID | Tenant-resolution middleware on a tenant host (ADR-0011, ADR-0016) |
| **Platform** | A fixed, well-known **`PlatformTenantId`** (a reserved catalog row of kind `Platform`; it has no slug and cannot be resolved from a host) | Middleware on the `admin` and `auth` hosts; the outbox dispatcher and maintenance jobs while they read platform tables |
| **None** | — | Anything else. A connection without a scope is refused by the connection interceptor, and RLS returns no rows (fail closed, NFR-031) |

Treating the platform as a pseudo-tenant means **no `NULL` tenant
values and one RLS predicate for all tenant-scoped tables**. Superadmin
accounts (ADR-0003) and platform audit events have
`TenantId = PlatformTenantId`. Platform code therefore cannot read
tenant rows by accident: under Platform scope the predicate returns only
platform rows.

Switching scope happens only through **`ITenantScopeFactory`**, which
opens a new DI scope and a new connection for one tenant. Allowed
callers (enforced by an architecture test): the outbox dispatcher (one
scope per message), maintenance jobs (one scope per tenant), the `seed`
command of the `migrate` job (one scope per seeded tenant; not allowed
in prod, ADR-0012), and Tenancy's superadmin commands for tenant-admin
accounts (FR-083). **Every superadmin-initiated switch** from Platform
scope into a tenant writes an audit event (FR-083). System callers
(outbox dispatcher, maintenance jobs, `seed`) are exempt from audit:
they would write one audit row per message or per tenant per run
without a human actor; their switches are recorded in telemetry
instead (span with `tenant.id` and the caller name, ADR-0010).

### 2. Table classes

| Class | Rule | Tables (MVP) |
|---|---|---|
| **Tenant-scoped** | Non-null `TenantId`; EF global query filter; RLS **filter and block** predicate `TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier)` | `identity.*` (users incl. superadmins under `PlatformTenantId`, roles, logins, passkeys, tokens), `tenancy.TenantBranding`, `tenancy.TenantSettings`, `tickets.*` (incl. claim details, timeline, counters, dashboard views), `attachments.*`, `notifications.*`, `privacy.*` (consents, export jobs), `audit.AuditEvents` |
| **Platform-scoped** (no RLS) | Contains **no ticket content and no tenant-owned personal data beyond what is listed**; accessed only by its owning module's code; listed by name in an exemption list in code | `tenancy.Tenants` (catalog: slug, type, state, hostnames, `DataLocation`), `tenancy.TenantUsageSnapshots` (counts only, FR-082), `platform.OutboxMessages` (`TenantId` column, payload of IDs only), `identity.ExternalLoginHandoffs` (`TargetTenantId`, payload encrypted with ASP.NET Core Data Protection, 60 s TTL, single use) |

Why each exemption is needed:

- **Catalog:** tenant resolution must read it before any tenant is
  known.
- **Usage snapshots:** FR-082 shows counts across tenants. A maintenance
  job computes them **inside each tenant's scope** and writes one row per
  tenant, so no database principal ever bypasses RLS. This replaces the
  earlier "separate superadmin database user with aggregate views".
- **Outbox:** the dispatcher claims due rows for all tenants in one
  query, then handles each message in its own tenant's scope. A row's
  `TenantId` is the scope its handler runs in; rows created by platform
  use cases carry `PlatformTenantId` (e.g. a superadmin notification) or
  the target tenant (e.g. `TenantHostnameReady` → the handler creates
  and sends the tenant admin's invitation inside the new tenant). The
  invitation waits for the hostname, not for `TenantCreated`: with a
  domain, *Ready* is set when the bound hostname answers (ADR-0011
  item 6); where no binding is needed (`*.localhost` locally,
  pre-domain mode, ADR-0016) the hostname is *Ready* at creation, so
  the invitation follows immediately.
- **Handoffs:** written and read by the auth host under Platform scope;
  the tenant host redeems a handoff only if `TargetTenantId` equals its
  resolved tenant. The login *request* is not stored at all: it travels
  in a data-protected, 5-minute token and in the OAuth `state`
  (ADR-0003).

### 3. Application layer (EF Core)

- Global query filters on every tenant-scoped entity, bound to the
  scoped `TenantContext`.
- A `SaveChanges` interceptor stamps `TenantId` on inserts and throws if
  a modified or deleted entity belongs to another tenant, or if there is
  no scope.
- `IgnoreQueryFilters()` is banned (analyzer / architecture test). There
  is no "platform query path" that bypasses filters any more.

### 4. Database layer (Row-Level Security)

- A connection interceptor runs
  `sp_set_session_context @key=N'TenantId', @value=…, @read_only=1` on
  every connection open (pooled connections are reset between uses).
- One security policy, one inline table-valued predicate function, added
  to every tenant-scoped table by migration.
- RLS applies to all database principals, including the migration
  identity; jobs that touch many tenants iterate tenants instead of
  bypassing RLS (ADR-0012).

### 5. Indexes

Indexes on tenant-scoped tables lead with `TenantId`, e.g.
`(TenantId, Status, CreatedAt)` and `(TenantId, Number)` (NFR-012).

### 6. Moving a tenant (NFR-033)

The catalog keeps a `DataLocation` column from day one, set to `shared`
for every tenant. **The per-tenant connection factory is not built in
MVP**: all `DbContext`s use one connection string. Trigger to build it:
the scaling lab or a tenant contract requires moving a tenant to a
dedicated database. Because every tenant-owned row carries `TenantId`
and nothing joins across tenants, the move is a filtered copy plus a
`DataLocation` switch.

### 7. Tests (NFR-030, NFR-073)

Run in CI against a real SQL Server container, so RLS is exercised:

1. **Cross-tenant suite:** two seeded tenants. Every endpoint, query,
   file link, export and job is executed as tenant A against tenant B
   identifiers and must fail.
2. **Coverage check:** every table with a `TenantId` or `TargetTenantId`
   column is either in the RLS policy or in the exemption list (with a
   reason). A new table that is in neither fails the build.
3. **Platform-scope tests:** under Platform scope, reads of tenant
   tables return only platform rows (no tenant tickets, users or audit
   events).
4. **Exemption tests:** a handoff for tenant A cannot be redeemed on
   tenant B; an outbox message for tenant A is handled only in A's scope;
   usage snapshots contain counts only; a connection without a scope
   throws.
5. **Scope-switch test:** only the allow-listed callers use
   `ITenantScopeFactory`.

## Alternatives considered

| Option | Isolation strength | Cost at run load | Ops effort | Scale to 100–250 tenants | Verdict |
|---|---|---|---|---|---|
| **Shared DB, shared schema, TenantId + EF filters + RLS, platform pseudo-tenant (chosen)** | Logical; two independent layers; RLS catches application bugs | 1 DB per env (free offer) → ~0 USD | One migration run; one DB to monitor | Good; tenant-leading indexes; scale-up via vCores/Hyperscale | Chosen |
| Same, but `NULL` TenantId for platform rows and a second `Scope` session key in predicates | Same | Same | Predicate logic per table; NULL handling in every filter and unique index | Same | Rejected: more predicate variants to test for no benefit |
| Same, but an RLS-exempt database user for superadmin aggregates | Same | Same | A second app principal with broader read rights | Same | Rejected: usage snapshots computed per tenant need no bypass |
| Shared DB, TenantId with EF filters only (no RLS) | One layer; a missed filter or raw SQL leaks data | Same | Slightly simpler | Same | Rejected: NFR-031 asks for central, fail-safe enforcement |
| Schema per tenant | Stronger separation | 1 DB per env | Migrations per schema × 100–250; drift risk | Poor operationally | Rejected |
| Database per tenant | Strongest | 100 DBs exceed the free offer (10 per subscription); elastic pool far above budget | Migrations × N, monitoring × N | Good, costly | Rejected for MVP; the **NFR-033 target for individual large tenants** |

## Consequences

**Trade-offs**

- (+) Cheapest and simplest: one schema, one migration, one backup per
  environment, one predicate.
- (+) Two layers must both fail for data to leak; RLS also protects raw
  SQL and dashboard views.
- (+) No principal bypasses RLS; cross-tenant needs (usage counts,
  retention purge, dispatch) iterate tenant scopes.
- (−) Isolation is logical, not physical. A single-tenant restore means
  restoring to a new database and copying that tenant's rows (hardening
  lab runbook).
- (−) Four platform-scoped tables rely on application code alone. They
  are kept free of ticket content, listed explicitly and covered by
  tests 2 and 4.
- (−) Maintenance jobs that iterate 100–250 tenants run one small query
  per tenant. That is negligible at this tenant count.

**Scalability**

- One database grows by about 100 GB/year at design load; serverless GP
  scales to 80 vCores in Poland Central, then Hyperscale (ADR-0005).
- Noisy neighbour (NFR-014): per-tenant rate and concurrency limits
  (ADR-0013) first; if the burst test fails, build the connection
  factory and move the bursting tenant (section 6).
- Up to 250 tenants needs no redesign (NFR-018).

**Operations**

- Onboarding a tenant is a catalog row (plus a hostname once a domain
  exists, ADR-0011). No schema or database is created.
- Every new tenant-owned table must join the RLS policy; the coverage
  check enforces it.

**Cost**

- Run load: about 0 USD extra (free-offer database in dev/test,
  ADR-0005).
- Design load: one larger database (1,500–3,000 USD/month indicative)
  instead of 100+ databases or an elastic pool sized for them; moving a
  large tenant later adds only that database's cost.
