# Architecture Decision Records

This folder holds the Architecture Decision Records (ADRs) for
InPolsure. Each ADR records one significant decision: the context, the
decision, the alternatives considered and the consequences (trade-offs,
scalability, operations, cost). See
[`../../ARCHITECTURE.md`](../../ARCHITECTURE.md) for how the decisions
fit together.

## Rules

- File name: `NNNN-short-title.md`, numbered sequentially.
- Template: Title / Status / Date / Context / Decision / Alternatives
  considered / Consequences (trade-offs, scalability, operations, cost).
- Status values: **Proposed** → **Accepted** (by the user) →
  **Superseded by NNNN** (never rewrite an accepted ADR; write a new one
  that supersedes it).
- Every ADR states approximate monthly cost at run load and at design
  load (NFR-064), and checks Poland Central availability where it
  matters (C-05).

## Index

| ADR | Title | Status | Date | Key requirements |
|---|---|---|---|---|
| [0001](0001-modular-monolith.md) | Modular monolith in one ASP.NET Core host | Accepted | 2026-10-01 | C-06, C-08, NFR-018, NFR-075 |
| [0002](0002-tenant-data-isolation.md) | Tenant data isolation: shared database with TenantId and Row-Level Security | Accepted | 2026-10-01 | NFR-030–NFR-033, NFR-014 |
| [0003](0003-identity.md) | Identity: ASP.NET Core Identity, tenant-scoped accounts, social login, staff MFA | Accepted | 2026-10-01 | FR-020–FR-033, FR-010 |
| [0004](0004-compute-hosting.md) | Compute hosting: Azure Container Apps (Consumption) | Accepted | 2026-10-01 | NFR-001, NFR-002, NFR-062 |
| [0005](0005-relational-database.md) | Relational database: Azure SQL Database serverless GP (free offer for dev and test, paid for on-demand prod) | Accepted | 2026-10-01 | NFR-004–NFR-006, NFR-010–NFR-012 |
| [0006](0006-attachment-storage.md) | Attachments: Blob Storage with app-mediated upload and short-lived SAS download | Accepted | 2026-10-01 | FR-060–FR-066, NFR-028 |
| [0007](0007-background-processing-and-email.md) | Background processing: SQL outbox + in-process worker; email via Brevo | Accepted | 2026-10-01 | FR-070–FR-075, NFR-003, NFR-017 |
| [0008](0008-blazor-render-modes.md) | Blazor render modes per area | Accepted | 2026-10-01 | NFR-016, NFR-080, FR-047–FR-053 |
| [0009](0009-iac-and-cicd.md) | IaC with Bicep; CI/CD with GitHub Actions, OIDC and environment promotion | Accepted | 2026-10-01 | NFR-070–NFR-075, NFR-023 |
| [0010](0010-observability.md) | Observability: OpenTelemetry to Application Insights and Log Analytics | Accepted | 2026-10-01 | NFR-050–NFR-054, NFR-042 |
| [0011](0011-tenant-resolution-and-branding.md) | Tenant resolution by subdomain and white-label theming | Accepted | 2026-10-01 | FR-004, FR-010–FR-016, NFR-026 |
| [0012](0012-secrets-and-configuration.md) | Secrets and configuration: managed identity first, Key Vault for the rest | Accepted | 2026-10-01 | NFR-023, NFR-024 |
| [0013](0013-rate-limiting-and-abuse-controls.md) | Rate limiting and abuse controls | Accepted | 2026-10-01 | NFR-015, NFR-025, NFR-028, NFR-014 |
| [0014](0014-environments-lifecycle.md) | Environments lifecycle: permanent dev, on-demand test and prod | Accepted | 2026-10-01 | C-06, C-07, A-11, NFR-060–NFR-063, NFR-072 |
| [0015](0015-public-endpoints-entra-only-access.md) | Public PaaS endpoints with Entra-only data-plane access (no private endpoints in MVP) | Accepted | 2026-10-01 | NFR-020–NFR-024, NFR-040, C-06 |
| [0016](0016-pre-domain-mode.md) | Pre-domain mode: local subdomain multi-tenancy, single demo tenant on the default Azure hostname, domain as a future gate | Accepted | 2026-10-01 | A-05, FR-004, FR-021, FR-070–FR-075, NFR-075 |
| [0017](0017-ui-components.md) | UI components: own accessible component set, no third-party library in MVP | Accepted | 2026-10-01 | NFR-016, NFR-026, NFR-080 |

ADRs 0001–0013 were amended on 2026-10-01 after the architecture review
(each ADR's date line lists what changed). All ADRs 0001–0017 were
**Accepted** by the user on 2026-10-01.

## Topics deliberately not yet decided

These come from REQUIREMENTS §9 and PLAN §7. Each is covered in
ARCHITECTURE.md as a current approach with a trigger, and gets an ADR
when the trigger fires or the related lab needs it:

| Topic | Current approach (ARCHITECTURE.md) | ADR when |
|---|---|---|
| Dedicated search service | SQL indexes; SQL full-text for FR-052 | NFR-012 fails in the scaling lab |
| Messaging service (Service Bus) | SQL outbox only (ADR-0007) | Multiple consumers or worker extraction |
| Distributed cache (Redis) | In-memory per replica | Design-load measurements (§9.8) |
| Private networking and edge WAF | Public endpoints with Entra-only access (ADR-0015) | Triggers in ADR-0015 (before real data) |
| Platform domain, DNS, sender domain | Pre-domain mode (ADR-0016) | Domain gate in ADR-0016 |
| Wildcard hostname binding | Per-tenant list in parameter files (ADR-0009, ADR-0011) | Spike at the domain gate |
| Cross-region backup / DR | In-region backups (A-12 deferred) | Real data or a tenant contract |
| Malware scanning service | Deferred (A-09); Defender for Storage planned (ADR-0006) | Before real data |
| Audit log partitioning / storage beyond SQL | One SQL table, batched purge, no partitions (ADR-0005) | Purge > ~10 min or table > ~100 GB |
| Per-tenant connection factory (`DataLocation`) | Column only, one connection string (ADR-0002) | A tenant must move to a dedicated database |
| Third-party UI component library | Own component set (ADR-0017) | Trigger in ADR-0017 |
