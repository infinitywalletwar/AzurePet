# Architecture, requirements and delivery review — 2026-10-02

**Verdict:** keep the architectural direction. It is a sensible educational
system with a plausible production evolution path. The documents are unusually
explicit about scope, budget and accepted compromises. They do **not** yet
establish that every decision is technically proven or that production targets
are met. Several implementation contracts need clarification before their labs.

**Scope:** AGENTS.md, PLAN, REQUIREMENTS, ARCHITECTURE, accepted ADRs
0001–0017, and the delivery/task observations below. This is a design review,
not an application security audit, Azure deployment validation, load test,
recovery drill or production readiness certification. Existing accepted
documents and ADRs were not changed. Findings describe documented gaps or
contradictions, not demonstrated defects in future application code.

## What is sound

- One ASP.NET Core host with modules, Blazor and a small HTTP surface fits the
  actual consumers. Separate API and UI deployments add no current capability.
- Relational transactional data fits SQL; files belong in Blob Storage.
- Static SSR for the large client audience and Interactive Server for staff is
  a deliberate, reasonable trade-off. Measure staff circuit costs before moving
  to WebAssembly.
- Tenant-scoped accounts follow FR-022; managed identity, separate migration
  identity, explicit platform-table exemptions and SQL RLS are useful controls.
- Outbox processing, one promoted image digest, Bicep and explicit evolution
  triggers avoid premature distributed infrastructure.
- Synthetic data, on-demand environments, public endpoints, deferred malware
  scanning, approximate per-replica limits and the domain gate are **already
  accepted learning-phase trade-offs**. Their existence alone is not a defect.

## Findings to resolve before the relevant implementation

Priority **P1** means resolve before the affected security/data capability is
implemented or relied on. **P2** means correct the specification or evidence
claim before that feature is accepted. None requires discarding Lab 01.

### A-01 — P1: request tenancy does not define circuit tenancy

Evidence: `docs/ARCHITECTURE.md:429`,
`docs/architecture/decisions/0011-tenant-resolution-and-branding.md:47`,
`0008-blazor-render-modes.md:50`.

Middleware fills a request-scoped tenant context; staff operations execute in a
different, long-lived Blazor circuit scope. The documents do not define its
initialisation or propagation into per-operation DbContexts. A correctly
fail-closed implementation would reject staff queries; an improvised fallback
could compromise isolation. Microsoft distinguishes request scopes from circuit
scopes and highlights tenant-sensitive factory lifetime.

**Required:** define an immutable circuit tenant derived from the trusted host
and checked against the authenticated tenant claim; propagate it explicitly to
operation scopes and tenant-aware context factories/interceptors. Never derive
it from a client-supplied ticket ID or assume a live HttpContext. Test two
tenants in simultaneous circuits, reconnect, missing context and account/tenant
revocation. Keep one shared database and short-lived DbContexts.

Sources: [Blazor DI scopes](https://learn.microsoft.com/en-us/aspnet/core/blazor/fundamentals/dependency-injection?view=aspnetcore-10.0),
[EF Core multitenancy](https://learn.microsoft.com/en-us/ef/core/miscellaneous/multitenancy).

### A-02 — P1: cross-module atomicity needs a concrete transaction contract

Evidence: `docs/architecture/decisions/0001-modular-monolith.md:35,69`,
`docs/ARCHITECTURE.md:279,564,651`.

The design promises ACID registration/ticket operations across module-owned
DbContexts, consent, attachments, audit and the outbox. Sharing a database or
connection string does not enlist those contexts in one transaction. The ticket
sequence also depicts Tickets inserting Attachments' data without showing the
owning module's contract. Retry-on-failure adds another requirement.

**Required:** document which synchronous commands share the same DbConnection
and DbTransaction, which module contracts participate, who commits, and how the
whole operation runs under EF's execution strategy. Prove rollback when a
consent/audit/outbox write fails and retry without duplicate tickets/events.
Do not introduce a generic transaction framework before this small integration
spike establishes what is needed. Asynchronous email remains outside the SQL
transaction; blob writes need reconciliation (A-04).

Sources: [EF cross-context transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions),
[retrying transactions](https://learn.microsoft.com/en-us/ef/core/miscellaneous/connection-resiliency).

### A-03 — P1: bind the final OAuth handoff to the initiating browser

Evidence: `docs/architecture/decisions/0003-identity.md:84–102`,
`docs/ARCHITECTURE.md:609–623`.

The handoff is tenant-bound, encrypted, short-lived and single-use, which is
good. The target host's redemption does not explicitly verify an initiating
browser correlation value. A nonce inside an authentic token/state alone does
not specify that binding. An attacker could complete a valid login and forward
the still-unused completion URL to a victim, logging the victim into the
attacker's account. Provider callback protection does not automatically protect
this additional application redirect.

**Required:** create a random, short-lived host-only correlation cookie at the
tenant start, bind its nonce to the protected request and handoff, verify it at
tenant redemption, and consume the handoff atomically. Reject another browser,
replay, mismatched tenant, expired request and external return URLs. Specify
safe external-account linking rather than silently linking by matching email.
This is a security design gap, not a proven current vulnerability.

Source: [OAuth security BCP, CSRF protections](https://www.rfc-editor.org/rfc/rfc9700.html#section-4.7).

### A-04 — P1: staged attachment finalisation is missing

Evidence: `docs/ARCHITECTURE.md:648–658`,
`docs/architecture/decisions/0006-attachment-storage.md:31–35`.

The flow uploads to `staging`, links those blobs in SQL and commits. All
staging blobs then expire after one day. No final copy/promotion or change to
the referenced path is described. Implementing the sequence literally risks
deleting committed files. SQL and Blob Storage cannot share this transaction.

**Required:** specify a finalisation/reconciliation state machine. Only mark an
attachment Available when its durable final blob exists; cleanup must not
delete referenced data. Test failures before/after SQL commit and blob
finalisation and an interrupted retry. Define the exact content validation for
DOCX/XLSX: recognising a ZIP header alone does not establish either OOXML type.

### A-05 — P1: tenant relationships and exact quotas need write invariants

Evidence: `docs/architecture/decisions/0002-tenant-data-isolation.md:98–113`,
`docs/ARCHITECTURE.md:329–354`,
`docs/architecture/decisions/0013-rate-limiting-and-abuse-controls.md:80–89`.

RLS checks a row's TenantId; it does not prove that the row's client, category,
assignee, parent message or attachment belongs to that same tenant. Composite
tenant-qualified keys/references are not specified. Likewise, counting today's
tickets/upload bytes before inserting does not make an exact quota safe when
requests run concurrently across replicas.

**Required:** define `(TenantId, Id)` parent keys and tenant-qualified foreign
keys where appropriate, with contract-level same-tenant checks across module
boundaries. Include AFTER INSERT and AFTER UPDATE RLS block coverage and raw
SQL write tests. Enforce quota consumption atomically (for example a locked
per-user/day usage row), including pending uploads and retries. Test two
simultaneous requests at the last remaining ticket/byte allowance.

Source: [SQL RLS filter/block semantics](https://learn.microsoft.com/en-us/sql/relational-databases/security/row-level-security?view=sql-server-ver17).

### A-06 — P2: notification guarantees conflict with the accepted mechanism

Evidence: `docs/REQUIREMENTS.md:211,278`,
`docs/architecture/decisions/0007-background-processing-and-email.md:48–65`.

FR-075 prohibits duplicates, while ADR-0007 explicitly accepts a duplicate
after provider acceptance followed by a crash before the delivery record.
This is a normal SMTP limitation; the accepted compromise needs to be visible
in the requirement. The five-minute lease also needs renewal or bounded
handler steps for long exports, so two workers cannot process the same live
job merely because its lease expired. Retry exhaustion after three days needs
a durable failed-event/requeue rule to preserve NFR-003.

**Required:** retain at-least-once processing with deduplication; state the
provider-acceptance crash exception in FR-075 if the user retains it. Specify
lease ownership, renewal/fencing, progress commits and failed-event retention.
Test a worker crash after send, concurrent reclaim and export longer than one
lease. Idempotent handlers remain essential even with leases.

### A-07 — P2: availability evidence does not yet match the user-flow SLO

Evidence: `docs/REQUIREMENTS.md:276–277`,
`docs/architecture/decisions/0010-observability.md:49–61`,
`docs/architecture/decisions/0008-blazor-render-modes.md:96–103`.

The SLO names sign-in/read/create/reply; `/health/ready` and a read-only page
cannot establish those flows' availability. Standard tests normally run every
five minutes per location; Microsoft describes five locations as averaging
one test/minute. That does not establish the ADR's one-minute check at each of
three locations, or a worst-case alert within five minutes. For 99.9% of a
40-hour live window the allowance is **2.4 minutes**, not 43 minutes. Service
SLA multiplication is an illustrative estimate, not application SLO evidence.

The accepted circuit-loss risk also limits the phrase "no user-visible
downtime": zero failed synthetic HTTP requests does not prove no reconnect
interruption or loss of an agent's unsaved reply.

**Required:** define eligible live-window minutes, exclusions, SLI numerator,
denominator and flow probes; choose a supported cadence and measured alert
latency. Record HTTP deployment continuity separately from staff-session and
draft preservation. Preserve the agreed ephemeral prod model.

Source: [Application Insights availability tests](https://learn.microsoft.com/en-us/azure/azure-monitor/app/availability).

### A-08 — P2: budget estimates do not cover the default live window

Evidence: `docs/architecture/decisions/0014-environments-lifecycle.md:60–61,181–183`,
`docs/architecture/decisions/0007-background-processing-and-email.md:44–46`,
`docs/architecture/decisions/0005-relational-database.md:36`,
`docs/REQUIREMENTS.md:341`.

Prod keeps a replica and polls SQL every 30 seconds. SQL therefore cannot
auto-pause during that window. The cost estimate assumes roughly 40 live
hours/month, while the default seven-day expiry permits **168 hours**. At the
documented 0.3–0.7 USD/live-hour this is about **50–118 USD**, before shared
costs and teardown lag. Alerts and expiry help, but do not enforce a hard
100 USD monthly ceiling.

**Required:** estimate costs from actual window hours and always-on polling;
add a per-window duration/cost admission check and conservative remaining
monthly allowance, or clarify that the ceiling is an operational target with
alert latency. Use shorter defaults for short labs; accepted seven-day windows
can remain available with a deliberate projected-cost check. Reprice against
the actual region/SKUs at the deployment gate. Autoscale caps and teardown are
complementary guards, not a spending guarantee.

### A-09 — P2: Azure SignalR does not offload Blazor circuit state

Evidence: `docs/ARCHITECTURE.md:237`,
`docs/architecture/decisions/0008-blazor-render-modes.md:116–120`.

SignalR Service can offload connection handling. Interactive Server UI/circuit
state still runs in the application; server affinity remains required. It
cannot make circuit memory scale independently of web replicas or migrate an
existing circuit to another replica by itself.

**Required:** correct this evolution claim. Keep ACA sticky sessions initially;
measure circuit memory, active/disconnected circuits, scale-in and deployment
behaviour. Add SignalR only for a demonstrated connection-management need;
use app scaling or an approved WASM/API transition for circuit-resource limits.

Source: [Azure SignalR client negotiation and Blazor Required sticky mode](https://learn.microsoft.com/en-us/azure/azure-signalr/signalr-concept-client-negotiation).

### A-10 — P2: SAS download permission and future networking need clear limits

Evidence: `docs/REQUIREMENTS.md:196`,
`docs/architecture/decisions/0006-attachment-storage.md:104–111`,
`docs/architecture/decisions/0015-public-endpoints-entra-only-access.md:73–78`.

An issued SAS is an intentionally accepted bearer link: anybody holding it
can download until expiry. FR-062's "only authorised users" should describe
authorised **issuance** and this limited delegation exception. Logged issuance
does not prove a file was fetched. Revoking a session does not instantly revoke
an existing SAS.

The proposed future option "public access restricted to SAS requests" is not
a network isolation control. SAS authorisation does not override Storage
firewall rules or a disabled public endpoint. Private Storage requires app
proxying or a specifically designed supported private-origin edge path.

**Required:** record the five-minute bearer/deactivation exception and audit
semantics; use app-mediated downloads if immediate revocation becomes required.
Correct the future fallback before the private-networking ADR is written.

Source: [Storage firewall rules and SAS](https://learn.microsoft.com/en-us/azure/storage/common/storage-network-security).

## Targeted requirement improvements

The domain is already specific enough to build. Improve exact acceptance
semantics instead of adding a much longer requirements document.

| Area | Clarification needed |
|---|---|
| Ticket transitions | FR-043's state graph and FR-049's client close action need a role/transition matrix: which states may a client close, who resolves, and whether closed tickets can receive replies. |
| Privacy erasure | FR-113 needs a concrete synthetic-data rule for legal holds, retained fields, free text, staff-authored messages, attachment versions, audit IP addresses and backup expiry. Anonymising an identifier alone does not anonymise free text. |
| Residency | NFR-040 literally says all personal data is processed in Azure Poland Central/EU, but Brevo and external identity flows are exceptions. Name those bounded third-party transfers and the provider/DPA verification gate; an EU company address alone is not evidence of every processing location. |
| Email minimisation | FR-074 allows the subject, while ARCHITECTURE §9.2 says no ticket content. Subjects can contain medical/personal details. State whether subjects are allowed; preferably use number plus generic event and authenticated link before real data. |
| Abuse | Define atomic counters, UTC reset, idempotent retry semantics and pending/failed upload accounting (A-05). Keep approximate per-replica HTTP limits as the accepted learning trade-off. |
| Workload test | Add traffic mix, think time, staff circuit count, read/write/attachment mix, tenant skew, history size and arrival model. Separate intentional 429s during the burst from unexpected errors. |
| Reduced dataset | The accepted extrapolation path is useful evidence, not a full-scale proof. Preserve the largest-tenant/index working set where possible, publish limitations and avoid claiming linearly proven six-year search performance. |
| Accessibility | Axe is useful but cannot certify WCAG AA. Keep keyboard, focus, error announcement, dialog and representative screen-reader checks in feature acceptance. |
| Recovery | Specify the failure class covered by RTO: accidental deployment/data loss in a functioning region versus regional outage. Deferred cross-region DR is valid, but a four-hour region-recovery guarantee is not established. |

## Verdict for every accepted ADR

"Keep" retains the direction. "Clarify" adds an implementation contract or
corrects evidence. "Spike" validates uncertain feasibility before its gate.
"Revisit" means a stated guarantee/mechanism needs correction, not that the
entire selected technology is wrong. Accepted ADRs should remain historical;
material changes belong in new **Proposed** ADRs for the user's acceptance.

| ADR | Verdict | Action |
|---|---|---|
| 0001 Modular monolith | Keep + clarify | Cross-context transaction spike (A-02); retain one host. |
| 0002 Isolation | Keep + clarify | Circuit scope, relationship constraints and raw-write tests (A-01/A-05). RLS does not protect against a fully compromised shared app identity. |
| 0003 Identity | Keep + clarify/spike | Browser-bound handoff; tenant store, MFA/passkey enrolment/recovery and circuit expiry tests (A-01/A-03). |
| 0004 Hosting | Keep + spike | Confirm Poland Central capabilities, WebSockets/affinity, quotas and scale-in/deploy behaviour; correct availability claims. |
| 0005 SQL | Keep + clarify | Atomic transaction participation, quota invariants, actual free-offer/SKU availability and always-on prod cost. |
| 0006 Attachments | Keep + clarify | Finalisation, OOXML validation, bearer-link exception and private-download path (A-04/A-10). |
| 0007 Outbox/email | Keep + clarify | Deduplication exception, durable failed events and long-job ownership (A-06). Verify provider before real delivery. |
| 0008 Render modes | Keep + revisit claim | Correct SignalR offload claim; define circuit tenancy and deployment/session acceptance (A-01/A-09). |
| 0009 Bicep/CI | Keep + clarify | Cross-module migration/transaction contracts; least-privilege deployment gates and supported hostname binding API must be verified in their lab. |
| 0010 Observability | Keep + revisit measurement | Flow SLI, supported cadence, live-window denominator and alert evidence (A-07). |
| 0011 Resolution/branding | Keep + spike | Circuit scope, trusted host handling, custom-domain limit and wildcard/TLS gate. Avoid double cache TTLs exceeding FR-014. |
| 0012 Secrets/config | Keep + clarify | Tenant-aware context lifetime; review actual grants and identity attachment. Contributor can reconfigure workloads, so "no SQL role" is not a complete compromise boundary. |
| 0013 Limits | Keep + clarify | Atomic business quotas and explicit transport coverage (A-05); accepted approximate per-replica limits stay. |
| 0014 Lifecycle | Keep + clarify | Window-duration budget admission and live-window SLO/recovery limits (A-07/A-08). |
| 0015 Public endpoints | Keep for synthetic data | Preserve real-data gate; correct future SAS/networking assumption (A-10); review full control-plane blast radius. |
| 0016 Pre-domain mode | Keep | Honest local/CI versus Azure capability matrix; retain domain gate. |
| 0017 UI components | Keep | Small semantic component set; retain library trigger and manual accessibility checks. |

## Delivery/task and agent review

The independent delivery review finds the ownership/wave model sound overall.
The next tasks T-01.9 (browser tests/follow-ups) and T-01.10 (README) own disjoint
paths; T-01.11 correctly follows both and integrates CI. Keep this sequence.

### D-01 — P2: isolated task acceptance must not depend on later wiring

`docs/labs/lab-01-walking-skeleton.md:408,542–544,557–562,592–596`:
T-01.6 depends only on foundation T-01.1 but accepts compose traces in Aspire
(AC-13), whose host wiring belongs to later T-01.8. T-01.5's enabled probe
check similarly depends on later diagnostics configuration. Assign isolated
feature verification to parallel tasks and full-host verification to the
integration/browser task. Wave 1 is already complete: record or reconcile its
integration evidence at lab completion rather than reopening finished work
solely because the original task graph was imprecise.

### D-02 — P2: acceptance gates must include unaccepted Major findings

`docs/DELIVERY.md:251–263` requires Major fixes or user acceptance, but the pass
gate checks only Blockers. The lab DoD has the same omission
(`docs/labs/lab-01-walking-skeleton.md:773–774`). Clarify the gate as **no open
Blockers and no unaccepted Majors**. Re-run reviewers after material fixes;
carry genuinely minor follow-ups with explicit ownership and acceptance.

### D-03 — P2: finish the recorded what-if decision before Lab 02

G-1 in `docs/DELIVERY.md:662` is decided, but ADR-0009:44–46,63–66,
DELIVERY:102,367 and ARCHITECTURE:966 still describe PR what-if. Complete the
already-planned decision reconciliation: unauthenticated PR lint/build;
authenticated what-if during dev CD. This is an upcoming Lab 02 prerequisite,
not a Lab 01 blocker. Use a new Proposed clarification/superseding ADR for a
material accepted-decision change; preserve the accepted record's history.

### Agent organisation

Separate technical focus is useful; separate permanent agents or deployables
are unnecessary. Keep the existing roles and give each developer task a focus.

| Responsibility | Agent/profile | Ownership rule |
|---|---|---|
| Requirements, architecture, ADRs and lab specs | solution-architect | Design documentation only |
| Domain, persistence, auth and needed HTTP endpoints | dotnet-azure-developer, backend focus | One approved task's paths/contracts |
| Blazor pages, components, browser tests and accessibility | dotnet-azure-developer, Blazor focus | One approved UI task; shared contracts agreed first |
| Bicep, Docker, workflows and deployment mechanics | dotnet-azure-developer, infrastructure focus | One approved infrastructure task; Azure execution remains the human step |

Use **two implementers by default**, a third only for a genuinely disjoint
ready task, and one integration owner for Program.cs, solution/package files,
shared persistence configuration and pipelines. Existing batching already caps
the six-task wave at three developers (`lab-01...md:765–767`). Keep independent
architecture/code/infra reviewers according to the changed files. The
full-stack profile avoids costly handoffs for small vertical tasks; specialist
focus helps where the work is substantial.

The 21-lab roadmap is coherent but not evidence that every future task will
fit one session. Identity/social login/privacy labs in particular need smaller
tasks and waves when their detailed specs are written. Keep just-in-time specs
(`docs/ROADMAP.md:16–19`). Nine platform labs before the first ticket feature
is a learning-sequence trade-off: retain it unless the user prefers an earlier
product demonstration. Do not silently resequence an approved roadmap.

## Recommended next step

Complete the approved Lab 01 browser/README/CI work. Before the data,
multitenancy and identity labs, record A-01–A-05 as testable design contracts
and focused spikes. Carry A-06–A-10 into their feature/deployment gates.
Reconcile requirements where they contradict accepted trade-offs. Ask the
user to accept only material changes to guarantees or accepted decisions;
routine clarification should not restart all design phases.
