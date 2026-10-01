# ADR-0013: Rate limiting and abuse controls

- **Status:** Accepted (2026-10-01)
- **Date:** 2026-10-01 (amended 2026-10-01 after the architecture review: burst throttling at about 3× accepted by the user, per-replica semantics, SignalR circuits out of the HTTP limiter's reach)
- **Deciders:** the user (approver), solution-architect
- **Requirements:** NFR-015, NFR-014, NFR-025, NFR-028, FR-020, FR-061, FR-081, C-06

**Why a separate ADR:** NFR-015 explicitly requires the default limits
to be set in an ADR. Open registration for non-policyholders (FR-020)
makes abuse control a first-class concern.

## Context

- **NFR-015:** per-tenant and per-user rate limits on public endpoints,
  with a clear "too many requests" response.
- **NFR-025:** throttling or lockout against credential stuffing now;
  bot protection later.
- **NFR-028:** verified email before the first ticket; at most 5 new
  tickets per client per day; at most 200 MB of uploads per client per
  day; staff can deactivate abusive accounts.
- **NFR-014:** one tenant's 10× FNOL burst must not break other
  tenants.

There is no edge WAF at run load (cost, ADR-0015). Replicas are 1–2
at run load and 4–6 at design load. Staff areas use Interactive Server
(ADR-0008): after the initial HTTP requests, a staff user's actions are
SignalR messages inside one long-lived WebSocket, which HTTP middleware
does not see.

## Decision

1. **ASP.NET Core rate-limiting middleware** (`Microsoft.AspNetCore.RateLimiting`),
   in-memory per replica, applied after tenant resolution so partitions
   can use the tenant:

   | Policy | Partition key | Default limit (configurable) | Applies to |
   |---|---|---|---|
   | `auth-strict` | client IP | 10 requests / minute, sliding window | Sign-in, register, password reset, email resend, external-login start |
   | `anonymous` | client IP | 120 requests / minute | Other anonymous pages (portal landing, theme, branding) |
   | `user` | user ID | 300 requests / minute (token bucket) | Authenticated pages and endpoints |
   | `upload` | user ID | 30 uploads / 10 minutes | Attachment endpoints |
   | `tenant` | tenant ID | 6,000 requests / minute, plus a **concurrency limiter** of 200 in-flight requests per tenant per replica | All tenant hosts (noisy-neighbour guard, NFR-014) |

   Excess requests get **HTTP 429** with `Retry-After`. Portal pages
   render a friendly "too many requests, try again in N seconds" page.
   Every rejection increments a metric tagged by policy and tenant
   (ADR-0010).

   **All limits are per replica.** Counters live in each replica's
   memory, so the effective ceiling is roughly *configured limit ×
   number of replicas*, and it moves when autoscale adds or removes
   replicas. The configured values are therefore set for the expected
   replica count of each environment and recalibrated in the scaling lab
   (NFR-013, NFR-014).

   **Burst policy (accepted by the user, 2026-10-01):** during the
   catastrophe-event scenario (one tenant at 10× its normal rate,
   REQUIREMENTS §3), the bursting tenant is served up to **about 3× its
   normal peak share**; requests above that receive 429 and the portal's
   "try again" page. This protects the other tenants' latency targets
   (NFR-014) at the price of delaying some of the bursting tenant's FNOL
   submissions. The value is a tenant setting, so a tenant with a
   contract for more capacity can get a higher limit (and, if needed,
   dedicated capacity, ADR-0002 section 6).

   **Interactive Server circuits are not covered by these policies.**
   The HTTP limiter sees only the circuit's negotiate and connect
   requests. Inside an open circuit the guards are the circuit and hub
   limits of ADR-0008 rule 4 (message size, parallel invocations, max
   circuits per user), the domain business limits below (which apply to
   every command, whatever the transport), and the SQL command timeouts
   and per-tenant connection usage. Staff are authenticated, MFA-protected
   and few (≤ ~3,000 at design load), so this is accepted.
2. **Account protection:** ASP.NET Core Identity **lockout** after 5
   failed attempts for 15 minutes (per account) together with
   `auth-strict` (per IP). Generic error messages. Sign-in failures are
   audited (FR-100).
3. **Business limits (NFR-028)** are enforced in the domain, not in
   middleware, because they must hold across replicas and restarts:
   - Ticket creation requires `EmailConfirmed` (FR-020).
   - **Max 5 new tickets per client per UTC day** and **max 200 MB
     uploaded per client per UTC day**, counted with indexed queries.
     Defaults live in tenant settings so they can be changed per tenant
     later.
   - Clear validation message when a limit is exceeded.
   - Tenant admins can deactivate a client (FR-081). A deactivated client
     cannot sign in and existing sessions are invalidated (security stamp).
4. **Bot protection on registration (NFR-025, Later):** a privacy-friendly
   CAPTCHA (e.g. a challenge service with EU processing) behind an
   `IHumanVerification` port. **Trigger:** real data, or observed abuse
   such as a spike in registrations without verified email.
5. **Edge protection (Later):** Azure Front Door with WAF and rate-limit
   rules. **Trigger:** real data (ADR-0015) or a volumetric attack.

## Alternatives considered

| Option | Pros | Cons | Verdict |
|---|---|---|---|
| **In-app limiter (in-memory) + domain limits (chosen)** | Free; tenant-aware; easy to test locally | Per-replica counters, so the effective limit is ×N replicas; no protection before traffic reaches the app | **Chosen** for MVP. Per-replica inaccuracy is acceptable at 1–2 replicas, and business limits are exact because they are DB-based |
| Distributed limiter (Redis) | Exact limits across replicas | Azure Managed Redis cost (tens of USD+/month); another dependency | Not now. **Trigger:** design load with many replicas shows limits too loose, or abuse exploits per-replica limits |
| Front Door + WAF rate limiting | Stops abuse at the edge, DDoS protection, bot rules | ~35 USD/month base (Standard), managed WAF rules need Premium | Later (trigger above) |
| Container Apps IP restrictions only | Free | Static allow/deny lists; no per-user or per-tenant logic | Insufficient alone |
| CAPTCHA from day one | Stronger anti-bot | UX friction, third-party processing; data is synthetic now | Later (trigger above) |

## Consequences

**Trade-offs**

- (+) Meets NFR-015, NFR-025 (MVP part) and NFR-028 at zero cost.
- (+) Tenant partitioning protects other tenants from one tenant's burst
  at the app tier (NFR-014).
- (−) Limits are approximate across replicas. At design load the
  effective per-IP, per-user and per-tenant limits are multiplied by the
  replica count, and change with autoscale. Recalibrate in the scaling
  lab.
- (−) **A bursting tenant is throttled above about 3× its normal peak**
  (user-accepted). Some FNOL submissions during a catastrophe event are
  delayed by "try again" responses rather than accepted at once.
- (−) Staff actions inside Interactive Server circuits bypass the HTTP
  limiter; only circuit/hub limits and domain limits apply.
- (−) No protection against volumetric attacks before the app. Container
  Apps platform DDoS protection is basic. Accepted while data is
  synthetic.
- (−) NAT'd users (offices, mobile carriers) share an IP, so the
  per-IP `auth-strict` limit could block legitimate bursts. 10/min is
  generous for one office; monitor the 429 metrics.

**Scalability**

- The `tenant` policy limits are tuned in the burst test (NFR-014). The
  default (6,000/min per tenant) is about 3× the peak share of the
  largest tenant (10% of 300 req/s = 1,800/min). Because it is per
  replica, at design load with 4–6 replicas the per-replica value is set
  to roughly 6,000 ÷ replica count so the tenant-wide ceiling stays near
  3×; the scaling lab confirms the value.
- Database protection: SQL command timeouts, and the tenant concurrency
  limiter keeps one tenant from monopolising connection-pool slots.

**Operations**

- The limits are configuration (options) and can be changed without code.
- Dashboards show 429s by policy and tenant. An alert fires on sustained
  429 spikes, which can mean abuse or limits that are too tight.

**Cost**

- Run load: 0 USD.
- Design load: 0 USD for the in-app limiter (CPU in existing replicas).
- Later: Redis (if triggered) tens of USD/month; Front Door 35+ USD/month.
