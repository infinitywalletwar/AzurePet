# ADR-0016: Pre-domain mode: local subdomain multi-tenancy, single demo tenant on the default Azure hostname, domain as a future gate

- **Status:** Accepted (2026-10-01)
- **Date:** 2026-10-01
- **Deciders:** the user (approver), solution-architect
- **Requirements:** A-05, FR-004, FR-010, FR-021, FR-022, FR-024, FR-070–FR-075, NFR-030, NFR-031, NFR-073, NFR-075, C-06, C-07

**Why a separate ADR:** ADR-0011 describes the target tenant-resolution
model. Running without a domain affects resolution (ADR-0011), social
login (ADR-0003), email (ADR-0007), hosting (ADR-0004), pipelines
(ADR-0009) and environments (ADR-0014). One ADR keeps the rules and the
exit criteria in one place.

## Context

The user decided on 2026-10-01 **not to buy a platform domain now**, and
there is no commitment to buy one (A-05). The target design needs one:
tenant subdomains (FR-004), the central OAuth callback host for Google
and Microsoft sign-in (FR-021, ADR-0003), and an authenticated sender
domain for real email (SPF, DKIM, DMARC; ADR-0007).

What is available without a domain:

- **Locally:** browsers resolve any `*.localhost` name to the loopback
  address, so `<slug>.localhost`, `admin.localhost` and `localhost`
  give real, separate origins with no DNS. Google and the Microsoft
  identity platform accept `http://localhost` redirect URIs for
  development. Mailpit captures SMTP.
- **In Azure:** each Container App gets one default FQDN
  (`<app>.<environment-id>.polandcentral.azurecontainerapps.io`) with a
  Microsoft-managed certificate. We cannot create subdomains under it.

## Decision

### 1. Hosting modes

A typed option `Hosting:Mode` selects one of two modes, validated at
startup:

| Mode | Where | Tenant resolution | Reserved hosts |
|---|---|---|---|
| **`Subdomain`** (target) | Local, CI, and any Azure environment after the domain gate | `<slug>.<suffix>` per ADR-0011; suffix `localhost` locally, `dev.<domain>` / `test.<domain>` / `<domain>` in Azure | `admin.<suffix>` (superadmin), `auth.<suffix>` or `localhost` (OAuth callbacks) |
| **`SingleTenantDefaultHost`** (pre-domain) | Azure dev and test only, until the domain gate | The configured default FQDN maps to **one demo tenant**, set by `Hosting:DefaultHostTenantSlug`; every other host is rejected by the host allow-list | None: no admin host, no auth host in Azure |

In both modes the request goes through the **same** middleware
pipeline: host allow-list → tenant resolution (catalog lookup by slug)
→ `TenantContext` → `SESSION_CONTEXT` → authentication with host-only
cookies and the `tenant_id` claim check. Pre-domain mode only changes
how the slug is derived from the host; it adds no bypass and no path-
based routing.

### 2. Startup validation (blocks the mode in prod)

The options validator fails startup (`ValidateOnStart`) when:

- `Hosting:Mode = SingleTenantDefaultHost` and the deployment
  environment is `prod` (`Deployment:Environment`, set by Bicep from the
  parameter file), or
- `Hosting:Mode = Subdomain` in Azure without `Hosting:PlatformDomain`.

The `release.yml` prod job also fails fast if the prod parameter file
has no platform domain (ADR-0009). Prod therefore **cannot exist before
the domain gate** (ADR-0014).

### 3. What each environment exercises

| Capability | Local / CI | Azure dev / test (pre-domain) | Prod (after domain gate) |
|---|---|---|---|
| Multi-tenant subdomain routing, host-only cookie isolation | **Yes** (`<slug>.localhost`; CI tests send `Host` headers to the test server) | No (one demo tenant); RLS and EF filters still run with that tenant's scope | Yes |
| Cross-tenant test suite (NFR-030) | **Yes**, in CI against SQL Server container | Smoke tests only | Smoke tests |
| Superadmin panel and sign-in (passkey, ADR-0003) | Yes (`admin.localhost`) | **No** (admin host does not exist; tenants come from the `seed` job) | Yes (`admin.<domain>`) |
| Google / Microsoft sign-in | **Yes**, local OAuth clients with `localhost` redirect URIs | Off (buttons hidden, endpoints 404) | Yes (`auth.<domain>`) |
| Email | **Yes**, SMTP to Mailpit | Blob drop: rendered `.eml` to `maildrop` (ADR-0007) | Brevo SMTP |
| Managed identity, Key Vault, SQL RLS in Azure, scale-to-zero, deploy pipeline, migrations job | — | **Yes** | Yes |

### 4. The domain gate (a future gate, not a plan)

Buying a domain is **not scheduled**. It becomes necessary when the user
wants any of: prod (SLO measurement, zero-downtime check in prod,
restore drill in prod), multi-tenant routing in Azure, superadmin
sign-in in Azure, social login in Azure, or real email delivery. When
the gate is passed, the work is (one lab, recorded in a short ADR that
references this one):

1. Buy a domain (about 10–15 USD/year for `.com` or `.pl`) and add an
   **Azure DNS zone** to the shared layer (ADR-0014).
2. Set `platformDomain` in the parameter files; switch dev and test to
   `Subdomain` mode; fill each environment's `tenantHostnames` list
   (ADR-0009 item 5, ADR-0011); run the wildcard spike (ADR-0011).
3. Register Google and Microsoft OAuth clients per Azure environment
   with redirect URIs on `auth.<env-domain>`; store credentials in Key
   Vault (ADR-0012).
4. Brevo: authenticate `notifications@<domain>` with SPF, DKIM and DMARC
   records in Bicep; put the SMTP key in prod's Key Vault (ADR-0007).

Until then nothing in the code or Bicep needs the domain; the target
paths are already exercised locally.

## Alternatives considered

| Option | What it proves in Azure | Isolation and security | Cost | Verdict |
|---|---|---|---|---|
| **Single demo tenant on the default FQDN + full multi-tenancy locally (chosen)** | Platform plumbing (identity, RLS with a real tenant scope, Key Vault, pipeline, scaling); not subdomain routing | Same code path and controls as the target; no weaker mode | 0 USD | **Chosen** |
| Path-based tenants on the default FQDN (`/t/<slug>`) | Multi-tenancy in Azure | All tenants share one origin and cookie jar; a second resolution mode to secure and test; rejected in ADR-0011 for the target | 0 USD | Rejected |
| One Container App (or revision label) per demo tenant, each with its own default FQDN | Several tenants in Azure | Separate origins, but the deployment model differs from the target (one app for all tenants) and multiplies apps | Small, but more resources | Rejected |
| Free dynamic-DNS or "wildcard IP" services (e.g. `*.nip.io`-style names) | Subdomains in Azure | Third-party DNS controls our hostnames; certificates and OAuth registration on a shared public suffix; unsuitable for a security-focused design | 0 USD | Rejected |
| Buy a domain now | Everything | Target design | ~10–15 USD/year + ~6 USD/year DNS zone | Not chosen by the user; kept as the gate |
| Social login via the default FQDN as callback host | Social login in Azure dev | Callback host would be the tenant host itself; needs a different flow from the target central-host design | 0 USD | Rejected: two flows to secure; local proof is enough until the gate |

## Consequences

**Trade-offs**

- (+) Zero cost; no domain decision is forced on the user.
- (+) No second, weaker tenancy mode: the Azure demo tenant goes through
  the same resolution, cookie, claim and RLS checks as any tenant.
- (+) All multi-tenant, social-login and email behaviour is still
  built and tested (locally and in CI).
- (−) Azure environments do not demonstrate subdomain routing,
  superadmin sign-in, social login or real email before the gate
  (risk R-14).
- (−) **Prod is blocked until the gate** (ADR-0014), so NFR-001,
  NFR-002 in prod and the restore drill in prod wait for it (risk R-17).
  Their mechanics can be rehearsed in test.
- (−) Passkeys created locally are bound to `*.localhost` relying
  parties and do not carry over to Azure (expected).

**Scalability**

- None: a configuration mode; the target design is unchanged.

**Operations**

- The demo tenant slug and the default FQDN are parameters; the FQDN
  changes when the test environment is recreated, and Bicep passes the
  new value to the app.
- Local setup documents: hosts `acme.localhost`, `globex.localhost`,
  `admin.localhost`; OAuth test clients; Mailpit UI.

**Cost**

- Now: 0 USD.
- At the gate: domain about 10–15 USD/year, DNS zone about 0.50 USD/month
  plus queries (cents).
- Design load: not applicable (the gate is passed long before).
