# ADR-0008: Blazor render modes per area

- **Status:** Accepted (2026-10-01)
- **Date:** 2026-10-01 (amended 2026-10-01 after the architecture review: session revalidation and idle timeout in circuits, SignalR limits, component constraint ADR-0017)
- **Deciders:** the user (approver), solution-architect
- **Requirements:** NFR-016, NFR-010, NFR-080, NFR-081, NFR-026, NFR-002, NFR-015, FR-002, FR-010, FR-020–FR-027, FR-031, FR-040–FR-050, FR-053, FR-080–FR-083, C-01

## Context

Blazor Web Apps (.NET 10) can render each page or component with
**Static SSR**, **Interactive Server** (SignalR circuit),
**Interactive WebAssembly** or **Interactive Auto**. The areas have
different needs:

- **Client portal:** anonymous and authenticated clients on phones over
  4G. LCP ≤ 2.5 s at p75 (NFR-016). Up to 10,000 concurrent clients at
  peak. Mostly forms and lists.
- **Agent workspace:** up to about 3,000 concurrent staff on desktops.
  Interactive queue filtering, assignment, status changes, internal
  notes, concurrency conflicts (FR-047–FR-053).
- **Tenant admin and superadmin panels:** low concurrency, forms and
  grids.
- **Identity pages:** sign-in must set cookies on a real HTTP response.

Hosting is Container Apps, which supports sticky sessions in
single-revision mode (ADR-0004). Auth is same-host cookies with no
tokens in the browser (ADR-0003). The data is sensitive (NFR-041).

## Decision

| Area | Render mode | Notes |
|---|---|---|
| Client portal | **Static SSR** + enhanced navigation + enhanced forms + streaming rendering | No circuit, no WASM. Post/Redirect/Get forms. Uploads via multipart post to the attachments endpoint (ADR-0006) |
| Identity pages (all hosts) | **Static SSR** | As in the Blazor Web App Identity template |
| Agent workspace | **Interactive Server**, per page | Queue, ticket detail, assignment, internal notes. `InputFile` streaming for uploads |
| Tenant admin panel | **Interactive Server** | Staff, categories, branding (with live contrast check), client accounts, dashboard |
| Superadmin panel | **Interactive Server** | Tenants list, create, suspend |

Rules:

1. The **default is Static SSR**. Interactivity is opted into **per page
   or per layout area** (`@rendermode InteractiveServer` on staff pages),
   not globally.
2. Components in the **staff areas** may use interactive features.
   **Portal components** must work without interactivity, and render
   their state from the server.
3. The UI talks to modules **in-process** through their contracts
   (ADR-0001). There is no internal HTTP API and no ticket JSON is sent to
   the browser.
4. **Circuit hygiene:** keep circuit state small (IDs, not graphs), use
   short-lived `DbContext` instances per operation (`IDbContextFactory`),
   set circuit options (short disconnected-circuit retention, e.g. 3
   minutes; max retained circuits) and SignalR hub limits (maximum
   receive message size, parallel invocations per client), plus a cap on
   concurrent circuits per user (A: 5). HTTP rate limiting does not see
   messages inside an open circuit, so these limits are the circuit-side
   guard (ADR-0013).
5. **Sessions inside circuits (FR-002, FR-023, FR-031, FR-081):** the
   staff and admin areas use a **revalidating authentication state
   provider** (security stamp, account active, tenant *Active*; every
   1 minute) and a **circuit idle timeout** with forced sign-out, as
   defined in ADR-0003 item 4. A circuit therefore never outlives a
   deactivation, suspension or idle timeout by more than about a minute.
6. **Antiforgery** on all SSR forms and upload endpoints. A strict
   **CSP**: scripts and styles only from `'self'`, no inline scripts and
   no inline styles (NFR-026). This constrains the component choice
   (ADR-0017). The reconnect UI uses the .NET 10 template's
   `ReconnectModal` component with static CSS/JS.
7. **Accessibility:** WCAG 2.1 AA components (ADR-0017), plus automated
   axe checks in UI tests (NFR-080).

## Alternatives considered

| Option | First load (NFR-016) | Server cost / scale | Interactivity | Security / auth | Effort | Verdict |
|---|---|---|---|---|---|---|
| **Per-area: SSR portal, Interactive Server staff (chosen)** | Portal: HTML only, best LCP | Portal stateless; circuits only for staff (≤ ~3,000 at design load) | Rich where needed | Cookies, data stays server-side | Single model (C# components), no API layer | **Chosen** |
| Interactive Server everywhere | Good first paint, but every client opens a WebSocket | ~13,000 concurrent circuits at peak; memory per circuit × replicas; sticky sessions for all; scale-to-zero less effective | Rich | Same | Low | Rejected: circuit cost on the largest, least interactive audience |
| Interactive WebAssembly everywhere | Portal must download the .NET runtime and app (MBs) on 4G, risking NFR-016 | Server stateless | Rich, offline-capable | Needs an HTTP API for all data; cookie BFF still possible; ticket JSON in browser | High: API + contracts + auth for everything | Rejected |
| Interactive Auto (Server first, then WASM) for staff | Fast first load, then client-side | Lower circuit time | Rich | Needs both an API and server rendering paths | Highest: dual hosting model | Rejected for MVP |
| Razor Pages/MVC for the portal | Excellent | Stateless | Low | Same | A second UI technology alongside Blazor | Rejected: Blazor Static SSR gives the same output with one component model (C-01) |
| JS SPA (React/Angular) | Depends | Stateless | Rich | Token or BFF | New stack, conflicts with C-01 | Rejected |

## Consequences

**Trade-offs**

- (+) The portal is fast, cacheable at the HTML level, works with
  scale-to-zero, and has no WebSocket dependency on mobile networks.
- (+) Staff UIs get interactivity without building and securing a
  separate API. Sensitive data is never serialised to the browser as
  API payloads.
- (−) **Interactive Server needs a stable connection and sticky
  sessions.** Network blips show the reconnect UI. Deploys and replica
  restarts drop circuits, so unsaved input in staff forms can be lost
  (risk R-05). Mitigations: small forms, explicit save, drafts for long
  replies, and the improved reconnection and state-persistence features
  in recent Blazor versions, to be evaluated in the environments lab.
- (−) The portal has less "app-like" interactivity (full form posts with
  enhanced navigation). That is enough for the FRs. Small progressive
  enhancements are allowed as static JS modules from `'self'`.
- (−) Revalidation costs one small query per open circuit per minute
  (about 50 queries/s at ~3,000 circuits; acceptable, configurable).
- (−) Two interaction styles must be kept consistent in shared
  components (layout, branding). Staff and portal components live in
  separate folders.

**Scalability**

- Portal: horizontally scalable, stateless.
- Staff: about 3,000 circuits at design load spread over several
  replicas with sticky sessions. Memory per circuit is measured in the
  scaling lab.
- **Triggers:** (a) circuits per replica or memory exceed the measured
  safe limit → add **Azure SignalR Service** (Standard units, about
  1,000 concurrent connections per unit; check pricing then) so circuits
  scale independently of sticky replicas; or (b) latency or reconnect
  problems measured for staff → move the agent workspace to
  **Interactive WebAssembly** with API endpoints, reusing the module
  contracts as the shared contract project.

**Operations**

- Container Apps ingress: session affinity on, single-revision mode
  (ADR-0004). WebSockets work over the default ingress.
- Telemetry: circuit count and duration metrics, plus reconnect events
  (ADR-0010).

**Cost**

- Run load: no extra cost; everything runs in the existing replicas.
- Design load: staff circuits add replica memory, roughly one to two
  extra replicas, included in the ADR-0004 estimate. Azure SignalR
  Service is added only if the trigger fires (a few Standard units, tens
  to low hundreds of USD per month).
