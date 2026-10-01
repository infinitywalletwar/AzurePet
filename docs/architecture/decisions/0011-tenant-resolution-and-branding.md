# ADR-0011: Tenant resolution by subdomain and white-label theming

- **Status:** Accepted (2026-10-01)
- **Date:** 2026-10-01 (amended 2026-10-01 after the architecture review: pre-domain mode moved to ADR-0016, hostname source of truth in the parameter files (ADR-0009), on-demand prod and certificates; amended again after the compliance re-review: invitation order aligned with ADR-0002)
- **Deciders:** the user (approver), solution-architect
- **Requirements:** FR-002, FR-004, FR-006, FR-010–FR-016, FR-073, NFR-018, NFR-026, NFR-030, NFR-031, NFR-032, NFR-070, NFR-075, A-05

**Why a separate ADR:** PLAN §7 lists tenant resolution and white-label
theming as their own ADR topics. Both depend on hosting (ADR-0004) and
identity (ADR-0003), but neither is part of them.

## Context

Each tenant is reachable at its own address under the platform domain,
and the tenant must be known before any tenant data is read (FR-004).
Custom domains come later (FR-006). Suspended tenants show a neutral
notice (FR-002). Branding covers name, logo, favicon, colours and
texts, applied to the portal, staff header, sign-in pages and emails
(FR-010, FR-013, FR-073). Tenants must not upload scripts or arbitrary
CSS or HTML (FR-011). Contrast must meet WCAG AA (FR-012). Changes
appear within 5 minutes (FR-014). The CSP must not allow tenant scripts
(NFR-026). Hosting is Container Apps: free managed certificates do not
cover wildcards, and issuance needs the app to be publicly reachable
([Microsoft Learn](https://learn.microsoft.com/azure/container-apps/custom-domains-managed-certificates)).

**No platform domain is owned yet** (A-05, user decision 2026-10-01).
How the system runs before a domain exists (local `<slug>.localhost`,
one demo tenant on the default Azure hostname in dev/test, startup
validation that blocks this in prod) is decided in **ADR-0016**. This
ADR describes the target model, which applies locally from day one and
in Azure once the domain gate in ADR-0016 is passed.

## Decision

### Tenant resolution

1. **Subdomain per tenant:** `<slug>.<platform-domain>` in prod,
   `<slug>.dev.<platform-domain>` and `<slug>.test.<platform-domain>` in
   the other environments, and `<slug>.localhost` locally. Before a
   domain exists, Azure dev/test use the single-tenant default-host
   mode of ADR-0016 instead.
2. **Middleware order:** forwarded headers (trust the Container Apps
   ingress), then a host allow-list (only the configured platform
   suffixes, or the single default FQDN in pre-domain mode), then
   **tenant resolution**, then authentication and authorization, then
   endpoints. Resolution maps `Host` → slug → tenant catalog entry. The
   catalog (platform-scoped table, ADR-0002) is cached in memory with a
   5-minute TTL and invalidated locally when an admin changes it.
3. **Outcomes:** unknown slug → 404 neutral page. *Suspended* → neutral
   notice and all sign-ins refused (FR-002). Reserved hosts: `admin`
   (Platform scope, superadmin only, ADR-0003), `auth` (OAuth
   callbacks, Platform scope, ADR-0003), `www` and the apex. Reserved
   slugs are rejected at tenant creation.
4. **Propagation:** a scoped `TenantContext` is set from the catalog,
   then pushed to SQL `SESSION_CONTEXT` (ADR-0002) and to telemetry as
   `tenant.id` (NFR-032). A **missing context denies data access**
   (NFR-031).
5. **Cookie binding:** host-only auth cookies plus a `tenant_id` claim
   check (ADR-0003).
6. **Hostname and TLS provisioning (once a domain exists):** each tenant
   hostname gets a **CNAME + `asuid` TXT record in Azure DNS** and a
   **free managed certificate binding** on the web Container App.
   **The single source of truth is the per-environment
   `tenantHostnames` list in the `.bicepparam` file** (ADR-0009 item 5).
   The app module renders the complete `ingress.customDomains` array and
   the DNS records from that list on every deployment, because the array
   is replaced as a whole on each deployment and bindings added by CLI or
   portal would be dropped. Onboarding (FR-001) is therefore:
   the superadmin creates the tenant in the app (catalog row *Active*,
   hostname *Pending*) → a pull request adds the hostname to the list →
   the pipeline deploys → the app marks the hostname *Ready* when
   `https://<host>/health/ready` answers → the tenant admin invitation
   is created and sent (outbox event `TenantHostnameReady`, ADR-0002
   §2; not on `TenantCreated`, so the link never points to an unbound
   host). Where no binding is needed (`*.localhost` locally, pre-domain
   mode, ADR-0016) the hostname is *Ready* at creation and the
   invitation follows immediately. The earlier separate
   `onboard-tenant` CLI workflow is dropped.
   - **Spike when the domain is bought:** check whether Container Apps
     accepts a **wildcard hostname** (`*.<domain>`) with a BYO wildcard
     certificate from Key Vault. If yes, one wildcard entry replaces the
     per-tenant list (zero per-tenant infra steps), recorded in a
     superseding ADR.
   - **Prod is recreated per lab (ADR-0014):** the DNS zone is permanent
     (shared resource group), the CNAME targets and certificate bindings
     are re-rendered by the same deployment, and managed certificates
     are re-issued each time prod is created. The release workflow waits
     for certificate binding before the smoke test.
7. **Custom domains (FR-006, Later)** use the same list and binding
   mechanism with the tenant's own CNAME to the app. The catalog maps
   several hostnames to one tenant.

### White-label theming

8. **Settings, not code:** `TenantBranding` holds the display name, logo
   and favicon (blob references), `PrimaryColor` and `AccentColor` (hex,
   validated by regex), and texts and links (privacy notice URL or text,
   terms, support contact, footer; plain text or URLs, HTML-encoded on
   output) (FR-011, FR-013).
9. **Contrast check (FR-012):** on save, compute the WCAG 2.1 contrast
   ratio of each brand colour against the text colours the theme uses on
   it (white or near-black). Reject if below 4.5:1 for normal text. The
   admin panel shows the ratio live.
10. **Delivery:** a `GET /theme.css` endpoint renders only **CSS custom
    properties** (`:root { --brand-primary: #0a4d8c; … }`) from the
    validated values. It sends `Cache-Control: public, max-age=300` and
    an ETag, and output caching varies by host. Shared component CSS
    uses these properties (ADR-0017). Logo and favicon come from
    `/branding/logo` and `/branding/favicon` (blob-backed, cached). No
    inline `<style>` or `<script>` is needed, so the CSP is
    `default-src 'self'; script-src 'self'; style-src 'self';
    img-src 'self' data:; frame-ancestors 'none'` (NFR-026).
11. **Propagation (FR-014):** cache TTLs ≤ 5 min (catalog, settings,
    `theme.css`). Changes are visible on new page loads within 5
    minutes.
12. **Emails (FR-073):** templates use the same settings. The logo is an
    absolute URL to `https://<tenant host>/branding/logo` and colours
    are inline in the email HTML. That is acceptable, because email HTML
    is not subject to the site CSP. Before a domain exists, emails are
    captured (Mailpit locally, blob drop in Azure dev/test, ADR-0007),
    so the logo URL only has to work for the person reviewing them.

## Alternatives considered

| Option | Pros | Cons | Verdict |
|---|---|---|---|
| **Subdomain per tenant (chosen)** | Clear tenant identity per origin; host-only cookies isolate sessions by browser design; matches A-05; easy custom-domain path | Needs DNS + TLS per tenant (or wildcard); OAuth callbacks need the central host; needs a domain in Azure (ADR-0016) | **Chosen** |
| Path-based (`app.<domain>/t/<slug>/…`) | One hostname and certificate; would work on the default Azure FQDN without a domain | All tenants share one origin: cookies, local storage and the same-origin policy no longer separate tenants; branding of a shared origin; weaker isolation story | Rejected (NFR-030; A-05). Also rejected as a pre-domain workaround: it would add a second resolution mode with weaker isolation (ADR-0016) |
| Tenant chosen after login (one shared portal) | Simplest routing | Branded pre-login pages impossible (FR-010); same email in several tenants is confusing (FR-022) | Rejected |
| Wildcard DNS + Azure Front Door with a wildcard certificate | One edge config for all tenants; WAF | Front Door Standard base fee (~35 USD/month) plus BYO wildcard certificate; over budget at run load | Not now. **Trigger:** real data needs a WAF anyway (ADR-0015); then wildcard at the edge removes per-tenant bindings |
| Hostnames bound by a CLI onboarding workflow | No pull request per tenant | The next Bicep deploy replaces `customDomains` and drops them | Rejected (ADR-0009) |
| Tenant-uploaded CSS | Maximum flexibility | CSS injection risks (data exfiltration via selectors and URLs), CSP weakening; violates FR-011 | Rejected |
| Theme injected as an inline `<style>` per page | No extra request | Needs `style-src 'unsafe-inline'` or nonces; not cacheable across pages | Rejected in favour of a cacheable stylesheet |

## Consequences

**Trade-offs**

- (+) Strong, browser-enforced separation between tenants' sessions.
- (+) Branding is safe by construction: only typed, validated values
  reach CSS, and CSP stays strict.
- (+) One reviewed list per environment is the only place hostnames are
  defined, so deployments cannot silently drop bindings.
- (−) **Per-tenant hostname bindings** need a pull request per
  onboarding (minutes) until the wildcard spike succeeds or Front Door
  is introduced (risk R-02).
- (−) Recreating prod re-issues managed certificates; issuance needs
  DNS and the app reachable, and adds minutes to each prod creation
  (ADR-0014).
- (−) Until the domain gate, subdomain routing is proven only locally and
  in CI (ADR-0016).
- (−) Theme flexibility is limited to colours, logo and texts. This is
  intended (FR-011).

**Scalability**

- Resolution is an in-memory lookup per request. The catalog has 100–250
  rows.
- Container Apps custom-domain count per app: the limit must be checked
  during the spike. If it is below 250, the wildcard binding or Front
  Door path is needed before the tenant count reaches that limit
  (NFR-018).

**Operations**

- Onboarding runbook (with a domain): create tenant → PR adding the
  hostname → pipeline deploy → hostname *Ready* → invitation is sent.
- DNS zone, records and bindings are in Bicep. There are no portal
  clicks (NFR-070).

**Cost**

- Now: 0 USD (no domain, default Azure hostname, ADR-0016).
- After the domain gate: free managed certificates; Azure DNS zone about
  0.50 USD/month plus queries (cents); domain about 10–15 USD/year.
- Front Door (if triggered later): from about 35 USD/month (Standard),
  more with a managed WAF ruleset (Premium).
- Design load: unchanged (in-memory resolution; DNS cents).
