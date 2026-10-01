# ADR-0015: Public PaaS endpoints with Entra-only data-plane access (no private endpoints in MVP)

- **Status:** Accepted (2026-10-01)
- **Date:** 2026-10-01
- **Deciders:** the user (approver), solution-architect
- **Requirements:** NFR-020, NFR-021, NFR-022, NFR-023, NFR-024, NFR-040, NFR-041, NFR-060, NFR-062, FR-062, C-06, C-10, A-07, A-09

## Context

The app uses Azure SQL Database, Blob Storage, Key Vault and Container
Registry. Each can be reached through a public endpoint or through a
private endpoint in a VNet. Private networking reduces the attack
surface (NFR-020) but costs money and complexity: a VNet-integrated
Container Apps environment, one private endpoint per service (list price
about 0.01 USD per hour, about 7.30 USD/month, plus about 0.01 USD per GB
processed; check the
[pricing page](https://azure.microsoft.com/pricing/details/private-link/)
and calculator), private DNS zones, and for ACR the Premium tier (Basic
does not support private endpoints). The budget is ≤ 50 USD/month
target (C-06). All data is synthetic (A-07). Test and prod exist only on
demand (ADR-0014).

Two facts shape the choice:

- Container Apps on the Consumption plan **without a VNet** has no
  stable, dedicated outbound IP, so IP allow-listing the app at the SQL,
  Storage or Key Vault firewall is not reliable. GitHub-hosted runners
  have changing IPs too, which is why migrations run inside Azure as a
  Container Apps Job (ADR-0009).
- Browsers download attachments directly from Blob Storage with a
  short-lived user-delegation SAS (ADR-0006), so the storage endpoint
  must be reachable from the internet as long as that pattern is used.

The user decided on 2026-10-01 to accept public endpoints with
Entra-only access in the MVP.

## Decision

All PaaS endpoints stay **public**, and **identity is the perimeter**:
data-plane access requires a Microsoft Entra token for a managed
identity with an explicit role (ADR-0012). No keys, passwords or
connection-string secrets exist for Azure services.

| Service | Public network access | Authentication | Settings |
|---|---|---|---|
| Azure SQL | Enabled; firewall rule *Allow Azure services and resources to access this server* only (no client IP ranges, except a temporary owner IP in dev for debugging) | **Microsoft Entra-only** (`azureADOnlyAuthentication = true`; SQL logins disabled). `app` and `migrator` identities only | Minimal TLS 1.2; auditing to the environment's Log Analytics workspace (Later in hardening lab) |
| Blob Storage | Enabled | Entra RBAC; **shared key access disabled**; anonymous blob access disabled; user-delegation SAS only (no account-key SAS) | HTTPS only, TLS 1.2; soft delete and versioning in prod (ADR-0006) |
| Key Vault | Enabled | RBAC mode; `app` identity only (secrets user, crypto service encryption user) | Soft delete, purge protection |
| Container Registry (Basic) | Enabled | Admin user disabled; `AcrPull` for `app`/`migrator`, `AcrPush` for `deploy-dev` only | Images are not secret (public repository) but the registry is not anonymous |
| Container Apps ingress | Public (it is the application) | Application authentication (ADR-0003) | HTTPS only, TLS 1.2+, HSTS (NFR-021); rate limits (ADR-0013) |

Note on *Allow Azure services*: it opens the SQL endpoint at the network
layer to any Azure-hosted client, including other customers' resources.
The control that matters is Entra-only authentication with two named
identities. This is the accepted trade-off of this ADR.

Supporting rules:

- An **Azure Policy audit** (not deny) reports any resource that
  enables shared key, SQL authentication or anonymous blob access; the
  azure-infra-reviewer checks Bicep for the same.
- Defender for Cloud **free** recommendations (foundational CSPM) are
  reviewed in the hardening lab; paid Defender plans are not enabled in
  MVP (except the malware scanning decision in ADR-0006 before real
  data).

### Triggers to revisit (each leads to a superseding ADR)

1. **Before any real personal data** (A-07, A-09): VNet-integrated
   Container Apps environment (workload profiles), private endpoints for
   SQL, Blob and Key Vault, public network access disabled on them,
   private DNS zones, Front Door with WAF at the edge, and attachment
   downloads either streamed through the app or served through a
   Storage endpoint reachable only via the edge (ADR-0006). Prod only.
2. A **tenant contract or regulator expectation** (e.g. DORA-driven
   outsourcing requirements of an insurer, C-10) that asks for private
   connectivity.
3. A **security finding or incident** involving a data-plane endpoint.
4. **Prod becomes permanent** at design load (ADR-0014 superseded): the
   fixed cost of private networking is then small relative to the total.

## Alternatives considered

| Option | Security | Cost at run load | Complexity | Verdict |
|---|---|---|---|---|
| **Public endpoints, Entra-only data plane (chosen)** | Identity-based; no secrets; network exposure accepted while data is synthetic | 0 USD extra | Low | **Chosen** for MVP |
| Private endpoints for SQL, Blob, Key Vault in every environment | Strongest network isolation | 3 endpoints × ~7.30 USD ≈ 22 USD per environment per month while it exists, plus private DNS zones (~0.50 USD each); ACR Premium (~50 USD/month) if the registry is also private; migrations and debugging need in-VNet access (jump host or job) | High | Rejected for MVP (budget, C-06) |
| Private endpoints in prod only | Prod isolated | ~25 USD per month of prod existence plus ACR question | Medium; prod differs from test | Planned at trigger 1 |
| Service endpoints / VNet rules instead of private endpoints | Restricts SQL/Storage to a subnet; no per-endpoint fee | Needs a VNet-integrated environment; Storage must stay public for browser SAS downloads | Medium | Considered at trigger 1 as a cheaper step |
| IP allow-lists on the PaaS firewalls | Looks restrictive | Consumption Container Apps without VNet has no stable dedicated outbound IP; a NAT gateway for a static IP costs about 30+ USD/month and needs a VNet anyway | Brittle | Rejected |
| Front Door + WAF now | Edge protection for the app | ~35 USD/month Standard (managed WAF rules need Premium) | Medium | Deferred (trigger 1; ADR-0013) |

## Consequences

**Trade-offs**

- (+) Zero extra cost and the simplest network; local development,
  migration jobs and pipelines need no network plumbing.
- (+) No credential exists that could leak from the public repository
  and grant data access.
- (−) Data-plane endpoints are reachable from the internet; a
  compromised managed identity token or a misassigned role is not
  stopped by the network (risk R-08).
- (−) No WAF in front of the app; abuse is handled in the app
  (ADR-0013).
- (−) The move to private networking later touches hosting (VNet
  environment), attachment downloads and debugging access; it is planned
  as one hardening step for prod.

**Scalability**

- No effect on throughput. Private endpoints at design load add latency
  of well under a millisecond and no throughput limits relevant here.

**Operations**

- Fewer moving parts; diagnostics and the owner's dev debugging use the
  public endpoints with Entra login.
- Reviews must watch for any re-enabled shared key or SQL login.

**Cost**

- Run load: 0 USD.
- After trigger 1 (prod only): roughly 25–35 USD per month of prod
  existence for private endpoints and DNS, plus Front Door from about
  35 USD/month, plus ACR Premium (~50 USD/month) only if the registry
  must also be private.
- Design load: about 100–450 USD/month for private endpoints, DNS, ACR
  Premium and Front Door Premium with WAF (ARCHITECTURE §9.7).
