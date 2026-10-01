# ADR-0003: Identity: ASP.NET Core Identity with tenant-scoped accounts, social login and staff MFA

- **Status:** Accepted (2026-10-01)
- **Date:** 2026-10-01 (amended 2026-10-01 after the architecture review: superadmin hardening, session revalidation for Interactive Server, social login before a domain exists, handoff storage)
- **Deciders:** the user (approver), solution-architect
- **Requirements:** FR-020–FR-033, FR-002, FR-010, FR-070, FR-081, FR-100, NFR-020, NFR-024, NFR-025, NFR-028, NFR-040, NFR-075, C-06, A-05

## Context

Four audiences: end clients who self-register (FR-020), staff created by
tenant admins (FR-023), tenant admins, and superadmins (FR-024).
Clients can use Google or Microsoft accounts (FR-021). MFA is mandatory
for staff, tenant admins and superadmins (FR-025). The key constraints:

- **FR-022:** the same email in two tenants means **two independent
  accounts** with no shared data or visibility.
- **FR-010:** sign-in and registration pages carry the **tenant's
  branding**. There are 100–250 tenants.
- **FR-002, FR-023, FR-031, FR-081:** suspension, deactivation and idle
  timeouts must end sessions, including long-lived Interactive Server
  circuits (ADR-0008), which make no HTTP requests after they start.
- **NFR-040:** identity data stays in Poland or the EU.
- **A-05:** no platform domain exists yet (ADR-0016).
- **Scale:** 5M registered clients, about 500,000 monthly active.
- **Budget:** 50–100 USD/month (C-06).

## Decision

Use **ASP.NET Core Identity** (.NET 10) with our own EF Core store in
the Azure SQL database (`identity` schema). The web host issues
**cookie authentication**; no tokens are stored in the browser.

1. **Tenant-scoped accounts.** `User` has `TenantId`, unique on
   `(TenantId, NormalizedEmail)` and `(TenantId, NormalizedUserName)`,
   with a custom user store so lookups always include the resolved
   tenant. Same email in two tenants means two rows, two passwords, two
   MFA setups (FR-022).
2. **Superadmin accounts (hardened).** Superadmins are users of the
   **Platform pseudo-tenant** (`TenantId = PlatformTenantId`, ADR-0002),
   so they never have a `NULL` tenant and are covered by the same RLS
   predicate. Rules:
   - Sign-in **only on the admin host** (`admin.<domain>`; locally
     `admin.localhost`). The admin host does not exist in Azure
     pre-domain mode (ADR-0016), so superadmin sign-in in Azure starts
     once a domain exists.
   - **Passkey required** (phishing-resistant); TOTP is not accepted for
     superadmins. Recovery codes are issued once and stored by the
     operator offline.
   - **Idle timeout 15 minutes (A)**, absolute session lifetime 8 hours
     (A).
   - **Notification on every superadmin sign-in**: an audit event plus a
     security email to all superadmin accounts through the outbox
     (ADR-0007), and a `auth.superadmin.signin` metric (ADR-0010).
   - Created only by another superadmin or by the
     `bootstrap-superadmin` one-shot command, run as a Container Apps
     Job by the pipeline (ADR-0009), with an audit event (FR-024).
   - **Evolution option:** the platform owner's **Entra ID workforce
     tenant** for superadmins (Conditional Access, Entra MFA). Trigger:
     real personal data, or more than 2 platform operators.
3. **Roles:** `Client`, `Agent`, `TenantAdmin` (tenant-scoped) and
   `Superadmin` (platform). Policy-based authorization plus resource
   checks (owner of ticket, same tenant) (FR-028).
4. **Cookies and sessions.** Cookies are host-only (no `Domain`
   attribute), `Secure`, `HttpOnly`, `SameSite=Lax`. The ticket holds a
   `tenant_id` claim that middleware compares with the resolved tenant
   (ADR-0011). Sliding idle timeout: 30 minutes for staff, 60 for
   clients, 15 for superadmins (FR-031).
   **Interactive Server sessions** (staff and admin areas) additionally:
   - A **revalidating authentication state provider**
     (`RevalidatingServerAuthenticationStateProvider`) checks every
     **1 minute (A)** that the user's security stamp is unchanged, the
     account is active, and the tenant is *Active*. If any check fails,
     the circuit's user becomes anonymous and the UI redirects to sign-in.
     So deactivation (FR-023, FR-081), role changes and suspension
     (FR-002) take effect within about a minute, also in open circuits.
   - A **circuit idle timeout**: a `CircuitHandler` tracks inbound
     activity; after the role's idle period without user input the
     circuit forces a sign-out (FR-031).
   - To keep the cookie's sliding expiry in step with an active circuit,
     staff layouts load a small static JS module (from `'self'`, CSP
     compliant) that calls a `POST /account/session/refresh` endpoint at
     most every 5 minutes while the user is active.
5. **Social login (FR-021)**: the built-in Google and Microsoft account
   handlers; the Microsoft app registration accepts personal and work
   accounts. Neither provider accepts wildcard redirect URIs, so all
   callbacks go to **one central auth host**, followed by a single-use
   handoff to the tenant host (ARCHITECTURE §7.2):
   - The tenant host creates a **data-protected login-request token**
     (tenant, return URL, nonce; 5-minute lifetime) and redirects to the
     auth host, which carries it through the challenge in the
     handler-protected OAuth `state`. No database row is needed.
   - After the callback the auth host stores a **handoff** in
     `identity.ExternalLoginHandoffs` (platform-scoped, encrypted
     payload, 60 s, single use, `TargetTenantId`; ADR-0002) and
     redirects to `<tenant host>/account/external-complete?code=…`. The
     tenant host redeems it only if the target tenant matches, then signs
     in or creates the tenant-local account after the relationship and
     consent form. An external login links to exactly one tenant-local
     account.
   - **Auth host per mode (ADR-0016):** with a domain, `auth.<domain>`
     (per environment `auth.dev.<domain>` etc.). **Before a domain
     exists**, social login is exercised **locally only**: the auth host
     is `localhost` (loopback redirect URIs are accepted by Google and
     the Microsoft identity platform), handing off to `<slug>.localhost`.
     In Azure dev/test pre-domain mode social login is switched off by
     configuration (buttons hidden, endpoints return 404).
   - A provider email counts as verified only when the provider asserts
     it (`email_verified` for Google). Otherwise our own verification
     email is required before the first ticket (FR-020).
6. **MFA (FR-025)**: tenant admins and agents must enrol an
   **authenticator app (TOTP)** or a **passkey** (.NET 10 Identity) on
   first sign-in and get recovery codes; superadmins must use a passkey
   (item 2). An authorization policy requires an MFA-satisfied sign-in
   for all staff areas. Client MFA (FR-026) is Later. Passkeys are bound
   to the host as relying-party ID, which keeps them tenant-scoped
   (FR-022). Local check in the identity lab: passkey registration on
   `*.localhost` hosts works in the target browsers.
7. **Account protection (NFR-025)**: Identity lockout (5 failures → 15
   minutes), rate limits on sign-in, registration and reset endpoints
   (ADR-0013), generic error messages, audit of sign-in success and
   failure (FR-100). Bot protection (CAPTCHA) on registration is Later.
8. **Account emails (FR-070, FR-027)** go through the outbox and
   `IEmailSender` (ADR-0007), branded per tenant.
9. **Data Protection** keys (cookies, tokens, login-request tokens,
   handoff payloads) are stored in Blob Storage and wrapped by a Key
   Vault key, shared across replicas (ADR-0012).

## Alternatives considered

| Option | FR-022 (same email, separate accounts) | FR-010 (per-tenant branding of sign-in) | MFA for staff | Residency (NFR-040) | Cost at 500k MAU (design) | Ops / effort | Verdict |
|---|---|---|---|---|---|---|---|
| **ASP.NET Core Identity, own store (chosen)** | Native: unique per tenant | Full: our own Blazor pages per tenant | TOTP, passkeys (.NET 10), recovery codes | Data in our Poland Central DB | 0 USD licence | We own password security, flows and UI (template scaffolds most of it) | **Chosen** |
| Microsoft Entra External ID, **one** external tenant for all InPolsure tenants | Conflicts: one local account per email per directory; password and MFA shared across tenants | Directory-wide company branding, no per-application branding ([Learn](https://learn.microsoft.com/entra/external-id/customers/concept-branding-customers)) | Email OTP, SMS, passkey; no authenticator-app TOTP for self-service users ([Learn](https://learn.microsoft.com/entra/external-id/customers/concept-supported-features-customers)) | EU datacentres; Go-Local only for Australia and Japan ([Learn](https://learn.microsoft.com/entra/external-id/external-identities-pricing)) | MAU-billed above the free tier ([pricing](https://aka.ms/ExternalIDPricing)) | Managed security features | Rejected: fails FR-022 and FR-010 |
| Entra External ID, **one external tenant per InPolsure tenant** | Satisfied | Satisfied | As above | As above | Same MAU cost | 100–250 directories, manual step per onboarding | Rejected |
| Azure AD B2C | Custom policies can emulate | Per-policy branding | Yes | EU | MAU-billed | Not offered to new customers; complex custom policies | Rejected |
| Separate token server (OpenIddict / Duende) + Identity | Satisfied | Satisfied | Satisfied | Satisfied | OpenIddict free; Duende licence above threshold | An OAuth server with no consumer (same-host cookie UI) | Rejected for MVP; reconsider with the integration API or a WebAssembly UI |
| Self-hosted Keycloak | Realm per tenant | Themes per realm | Yes | Self-hosted | ~20–40 USD/month extra | Operate and patch a Java IdP | Rejected |
| Superadmins via the platform owner's **Entra ID workforce tenant** | n/a | n/a | Entra MFA / Conditional Access | EU | Free tier (security defaults) | Second auth scheme on the admin host | Not chosen for MVP (simplicity; hardened Identity accounts suffice for synthetic data); **evolution option**, trigger in item 2 |

## Consequences

**Trade-offs**

- (+) Meets FR-022 and FR-010 exactly; full sign-in UX under our control.
- (+) No licence cost, no extra service, runs locally (NFR-075).
- (−) **We own credential security** (hashing, lockout, reset tokens,
  breach response); ASVS L2 review (NFR-020); breached-password check is
  a Later item.
- (−) The most privileged accounts live in our own store. Mitigated by
  passkey-only sign-in, a separate admin host, a short idle timeout and
  sign-in notifications; Entra workforce is the documented next step.
- (−) Revalidation adds one small indexed query per open circuit per
  minute: at design load (~3,000 staff circuits) about 50 queries/s,
  acceptable; the interval is configurable.
- (−) The central callback and handoff are custom security-sensitive
  code with focused tests: single use, 60 s expiry, tenant binding,
  `state`/nonce validation.
- (−) Until a domain exists, social login is not demonstrated in Azure
  (ADR-0016).

**Scalability**

- 5M users with tenant-leading indexes is well within Azure SQL limits;
  hashing is CPU-bound and scales with replicas.
- Tenant SSO (FR-030, Later): per-tenant OIDC/SAML schemes registered
  dynamically from the catalog; no redesign.

**Operations**

- OAuth client registrations: now one Google client and one Microsoft
  app registration **for local development** (localhost redirect URIs;
  credentials in `dotnet user-secrets`, never in the repository). Once a
  domain exists: one per Azure environment with redirect URIs only on
  `auth.<env-domain>`, credentials in Key Vault (ADR-0012); the
  Microsoft registration can use a certificate or federated credential.
- Superadmin bootstrap is a one-off pipeline job with an audit event.

**Cost**

- 0 USD licence at any scale; compute for hashing is in the replicas;
  emails per ADR-0007. Revalidation queries are negligible at run load
  and part of the ADR-0005 design-load estimate.
- External ID would be free at run load but MAU-billed at design load,
  and would still fail FR-022/FR-010.
