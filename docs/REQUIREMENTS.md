# InPolsure: Requirements

| | |
|---|---|
| **Product** | InPolsure. The repository is still named `AzurePet`. |
| **Status** | **Approved** v2.2 (confirmed by the user on 2026-10-01 together with the ADRs). v2.1 and v2.2 apply the user's architecture-review decisions (see "Changes in v2.1" and "Changes in v2.2"). |
| **Phase** | 1: Requirements (approved) |
| **Owner** | solution-architect |
| **Last updated** | 2026-10-01 |
| **Inputs** | [`PLAN.md`](PLAN.md) v3.1; the user's answers of 2026-10-01 (domain and scope, the v1 §10 open questions, and the architecture-review decisions) |

This document states **what** the system must do and **how well**. It
does not choose Azure services, the tenancy data-isolation model or any
other design. Those are listed in §9 as decisions for upcoming ADRs.

**Conventions**

- `FR-xxx` functional requirement, `NFR-xxx` non-functional requirement,
  `C-xx` constraint, `A-xx` assumption, `Q-xx` question.
- Priority: **MVP** (needed for the first complete release) or
  **Later** (candidate for a later lab; may be dropped).
- **(A)** marks a detail that the user did not give (e.g. a timeout or a
  file-type list). It is the architect's default; the user may correct it
  when approving this document. Items confirmed by the user carry no
  marker.

**Changes in v2:** ticket scope covers customer service **and** claims
intake (first notice of loss); tenants can be insurers, brokers or
agencies; non-policyholders can register and raise tickets (with abuse
controls); workload, RPO/RTO, backup and retention numbers confirmed;
English-only UI; statutory complaint deadline dropped; superadmin
support access is an audited self-grant; product named InPolsure.

**Changes in v2.1 (architecture review, user decisions of 2026-10-01):**
test and prod are created on demand and deleted after use, dev is
permanent (C-06, C-07, A-11, NFR-001); no platform domain is owned yet,
so a "pre-domain mode" applies until one is bought (A-05); budget alerts
follow Cost Management's real latency of hours, while the 5-minute target
stays for technical alerts (NFR-052).

**Changes in v2.2 (user decisions Q-B2 and Q-B3 of 2026-10-01, recorded
in `ARCHITECTURE.md` §10.3):** the load test may use a reduced synthetic
dataset with documented extrapolation to design load, to stay within the
NFR-063 cap (NFR-013, NFR-012); test on demand is now a user decision
(C-07).

---

## 1. Product summary and goals

**InPolsure** is a multi-tenant SaaS **ticket management system for the
insurance industry**. A platform owner sells it to insurance companies,
brokers and agencies (tenants). Each tenant gets a branded client portal
where its clients (policyholders, prospects and third-party claimants)
raise and track **customer-service requests** and **claims, including
first notice of loss (FNOL)**, and a staff workspace and admin panel
where its employees handle them. The platform owner runs the platform
through a superadmin panel.

The product **records, tracks and communicates** about policies and
claims. It does not adjudicate or pay claims and does not integrate
with or replace a tenant's policy or claims systems (A-01).

| ID | Goal |
|---|---|
| G-1 | Clients can raise and follow service requests and claims (including FNOL) online, at any time, with documents attached. |
| G-2 | Tenant staff handle requests and claims efficiently from one queue with full history. |
| G-3 | The platform owner can onboard and operate 100+ tenants on one platform, each with its own branding and settings. |
| G-4 | Personal data is protected, isolated per tenant, kept in Poland/EU and handled in line with GDPR. |
| G-5 | The system is reproducible from code, deployed by pipeline, observable, and runs within a 50–100 USD/month budget during the learning phase. |
| G-6 | The design can reach the stated scale and availability targets, proven by load tests rather than assumed. |

## 2. Personas and roles

| Role | Scope | Who | Main goals | Access (summary) |
|---|---|---|---|---|
| **Superadmin** | Platform | Platform owner's operators (at least 2 accounts) | Onboard, suspend and monitor tenants; manage tenant admins; platform health | Tenant management and platform-level data. **No access to ticket content** except through audited support access (FR-028, FR-029). |
| **Tenant Admin** | One tenant | Service-desk manager or IT admin of the insurer, broker or agency | Configure branding, categories and settings; manage staff and client accounts; handle GDPR requests; view reports | Everything within own tenant; nothing outside it |
| **Tenant Agent** | One tenant | Customer-service or claims staff | Work the ticket queue, register claims, reply to clients, add internal notes, resolve tickets | All tickets of own tenant (narrowed by teams/queues later, FR-051) |
| **End Client** | One tenant | Anyone who self-registers on the tenant's portal: a **policyholder**, a **prospect** or a **third-party claimant** (FR-032) | Raise service requests and claims, upload documents, follow progress, get email updates | Own profile and own tickets only |

Notes:

- A person may hold roles in different tenants, but each tenant
  membership is a separate account with no shared visibility (FR-022).
- The tenant type (insurer, broker, agency) is descriptive only; all
  tenant types get the same features (A-03).
- A dedicated *Supervisor* role or a *platform support operator* role
  may be split out later if requirements show a need.

## 3. Workload model and scale targets

Proposed by the architect and **confirmed by the user** on 2026-10-01.

| Quantity | Design target |
|---|---|
| Tenants | 100 (headroom to 250 without redesign) |
| End clients per tenant | 50,000 average; largest tenant 500,000 |
| End clients total (registered) | 5,000,000 |
| Staff per tenant | 50 average; largest 500 → about 5,000 total |
| Monthly active clients | 10% of registered → 500,000 |
| Tickets per registered client per year | 1 → 5,000,000 tickets/year |
| Tickets per working day | about 20,000 average (250 working days) |
| Messages (replies, notes) per ticket | 6 average → about 30,000,000/year |
| Peak hour | 15% of a working day's traffic |
| Peak concurrent users | 10,000 clients + 3,000 staff |
| Peak request rate (app requests, excl. static files) | 300 req/s sustained in peak hour |
| Burst scenario ("catastrophe event") | One tenant receives 10× its normal ticket rate for 2 hours (e.g. FNOL surge after a storm or flood) |
| Attachments | 50% of tickets have files; 2 files average; 1 MB average file → about 5 TB/year |
| Attachment limits | 20 MB per file, 10 files per message |
| Retention | Closed tickets and attachments 6 years (default, configurable per tenant); audit events 2 years |
| Emails sent | about 5 per ticket → about 25,000,000/year; peak about 300/min |
| Structured data growth | about 50 GB/year tickets and messages; about 50 GB/year audit events (rough order of magnitude) |

**Design target vs run target.** The design targets above are what the
architecture must be able to reach. They are proven with synthetic data
and short-lived load tests (NFR-013). Day-to-day environments run at
learning load within the budget (C-06).

## 4. Functional requirements

### 4.1 Tenancy and onboarding

| ID | Requirement | Priority |
|---|---|---|
| FR-001 | The superadmin can create a tenant with a display name, a unique short identifier (slug), a tenant type (*Insurer*, *Broker* or *Agency*; descriptive only) and the initial tenant admin's email. The initial admin receives an invitation email. | MVP |
| FR-002 | A tenant has a lifecycle state: *Active* or *Suspended*. In a suspended tenant no user can sign in and the portal shows a neutral notice; data is kept. | MVP |
| FR-003 | Additional lifecycle states *Offboarding* and *Deleted* support contract termination (FR-005). | Later |
| FR-004 | Each tenant is reachable at its own address under the platform domain (e.g. `<slug>.<platform-domain>`, A-05). The tenant is determined from the request before any tenant data is read. | MVP |
| FR-005 | Tenant offboarding: the superadmin can export all of a tenant's data in a machine-readable format and then permanently delete it. Deletion completes within 30 days of the request; backups expire within the backup retention period. | Later |
| FR-006 | Custom domain per tenant (e.g. `support.insurer.pl`) with a valid TLS certificate. | Later |
| FR-007 | Tenant plans with limits (max staff accounts, storage quota, request-rate tier) set by the superadmin. | Later |

### 4.2 Branding and white-label UI

| ID | Requirement | Priority |
|---|---|---|
| FR-010 | The tenant admin can set the tenant's display name, logo, favicon, primary and accent colours. They apply to the client portal, the staff workspace header, sign-in and registration pages (as far as the chosen identity solution allows) and emails. | MVP |
| FR-011 | Branding is limited to defined settings (name, images, colours, texts). Tenants cannot upload scripts or arbitrary CSS or HTML. | MVP |
| FR-012 | The system rejects colour combinations that fail WCAG 2.1 AA contrast for text. | MVP |
| FR-013 | The tenant admin can set tenant-specific texts and links: privacy notice, terms of use, support contact, footer. | MVP |
| FR-014 | Branding changes take effect for new page loads within 5 minutes. | MVP |
| FR-015 | Preview of branding changes before publishing. | Later |
| FR-016 | Tenant-editable email template texts. | Later |

### 4.3 Identity and access

| ID | Requirement | Priority |
|---|---|---|
| FR-020 | Anyone can self-register on a tenant's portal with email and password; holding a policy is not required. The email address is verified before the first ticket can be created. | MVP |
| FR-021 | End clients can register and sign in with a Google account or a Microsoft account (personal or work). | MVP |
| FR-022 | An account belongs to exactly one tenant. The same email address used in two tenants results in two independent accounts with no shared data or visibility. | MVP |
| FR-023 | The tenant admin can invite staff by email, assign a role (Agent or Tenant Admin), change the role and deactivate the account. Staff cannot self-register. | MVP |
| FR-024 | Superadmin accounts are created only by another superadmin or through a documented bootstrap procedure. | MVP |
| FR-025 | Multi-factor authentication is mandatory for superadmins, tenant admins and agents. | MVP |
| FR-026 | Optional MFA for end clients. | Later |
| FR-027 | Users can reset a forgotten password and change their email address, both with email verification. | MVP |
| FR-028 | Authorization: end clients access only their own profile and tickets; agents access only their own tenant's tickets; tenant admins manage only their own tenant; superadmins manage tenants and tenant admin accounts but cannot read ticket content, messages or attachments outside support access (FR-029). | MVP |
| FR-029 | Support access: a superadmin can self-grant time-limited (max 24 h (A)) access to one tenant's data, stating a reason. No tenant approval is required. Every grant and every action under it is audited, and grants are visible to the tenant admin once FR-104 exists. | Later |
| FR-030 | Staff of a tenant can sign in through the tenant's own identity provider (enterprise SSO federation). | Later |
| FR-031 | Staff sessions end after 30 minutes of inactivity (A); client sessions after 60 minutes (A). | MVP |
| FR-032 | At registration the client states their relationship to the tenant: *Policyholder*, *Prospect* or *Third-party claimant*. The client can change it in the profile. It is self-declared, not verified by the system, and shown to agents on the client's profile and tickets. | MVP |
| FR-033 | Client verification: an agent can mark a client as *verified* (e.g. after checking identity or policy outside the system), with an audit event; agents can filter by it. | Later |

### 4.4 Ticket lifecycle

| ID | Requirement | Priority |
|---|---|---|
| FR-040 | An end client can create a ticket with: category (from the tenant's list), subject, description, optional policy number, optional claim number, optional attachments. Claim categories collect additional fields (FR-120). | MVP |
| FR-041 | An agent can create a ticket on behalf of a registered end client (e.g. after a phone call). | MVP |
| FR-042 | Each ticket gets a human-readable number unique within the tenant (e.g. `T-000123`), shown in the UI and emails. | MVP |
| FR-043 | Ticket statuses and allowed transitions: *New* → *Open* → *Waiting for client* ⇄ *Open* → *Resolved* → *Closed*; *Resolved* → *Open* when the client reopens within the tenant's reopen window (default 14 days (A)). Invalid transitions are rejected. | MVP |
| FR-044 | A client reply on a *Waiting for client* ticket moves it back to *Open*. | MVP |
| FR-045 | Resolved tickets close automatically after the reopen window ends. | Later |
| FR-046 | Agents set priority: Low, Normal, High, Urgent. Default comes from the category. | MVP |
| FR-047 | Agents can assign a ticket to themselves or another agent of the tenant, and unassign it. Unassigned tickets form a shared queue. | MVP |
| FR-048 | Conversation: public replies are visible to the client; internal notes are visible only to staff. | MVP |
| FR-049 | End clients can list their tickets, open a ticket, reply, add attachments and close their own ticket. | MVP |
| FR-050 | Agents can list tickets of their tenant and filter by status, priority, category, category kind (FR-055), assignee, client relationship (FR-032), creation date; sort by date and priority; find by ticket number, client email, policy number or claim number. Results are paginated. | MVP |
| FR-051 | Teams/queues and routing of new tickets by category. | Later |
| FR-052 | Full-text search over subject, description and messages within a tenant. | Later |
| FR-053 | Concurrent changes to the same ticket never silently overwrite each other; the second user is told the ticket changed and can retry. | MVP |
| FR-054 | Every ticket shows a timeline of all changes (status, assignee, priority, category, claim number) with actor and time. | MVP |
| FR-055 | The tenant admin manages ticket categories: name, kind (*Service request* or *Claim*), default priority, active/inactive. | MVP |
| FR-056 | SLA policies per tenant and priority/category: first-response and resolution targets (due dates), warning before breach, escalation. This also covers tenants that want complaint deadlines. | Later |
| FR-057 | *Removed in v2.* Statutory complaint deadline tracking is out of scope (Q-10); per-tenant due dates are covered by FR-056. | — |
| FR-058 | Canned responses, ticket merge and linking. | Later |
| FR-059 | Email-to-ticket: create tickets and replies from inbound email. | Later |

### 4.5 Attachments

| ID | Requirement | Priority |
|---|---|---|
| FR-060 | Clients and agents can attach files when creating a ticket, replying or adding an internal note. | MVP |
| FR-061 | Allowed file types: PDF, JPEG, PNG, DOCX, XLSX (A). Max 20 MB per file and 10 files per message. Type is checked by content, not only by extension. | MVP |
| FR-062 | Files can be downloaded only by users authorised for the ticket. Download links cannot be guessed and expire within 15 minutes (A). | MVP |
| FR-063 | Attachments on internal notes are never visible to the client. | MVP |
| FR-064 | Files are scanned for malware before anyone can download them; infected files are quarantined and the uploader is told. | Later (risk accepted for MVP, see A-09) |
| FR-065 | Image thumbnails and in-browser preview. | Later |
| FR-066 | Per-tenant storage quota and usage shown to the tenant admin and superadmin. | Later |

### 4.6 Notifications

| ID | Requirement | Priority |
|---|---|---|
| FR-070 | Account emails: verification, staff invitation, password reset, email change. | MVP |
| FR-071 | Emails to the client: ticket created (with number), new public reply, status changed to *Waiting for client*, ticket resolved. | MVP |
| FR-072 | Emails to agents: ticket assigned to them; client replied on a ticket assigned to them. | MVP |
| FR-073 | Emails use the tenant's branding (name, logo, colours). Language: English (NFR-082). | MVP |
| FR-074 | Emails contain no description text, message text or attachments; they show the ticket number, subject and a link to the portal. | MVP |
| FR-075 | Emails are sent asynchronously, so that a slow or failed email provider does not block or fail the user's action. Failed sends are retried; a recipient does not receive duplicates for the same event. | MVP |
| FR-076 | User notification preferences (opt out of non-essential emails). | Later |
| FR-077 | Other channels (SMS, push) and daily digest for agents. | Later |

### 4.7 Admin panels

| ID | Requirement | Priority |
|---|---|---|
| FR-080 | **Tenant admin panel:** manage staff (FR-023), categories (FR-055), branding (FR-010, FR-013) and tenant settings (reopen window). | MVP |
| FR-081 | Tenant admin can search end-client accounts of the tenant, view a profile and deactivate or reactivate an account (also used against abusive accounts, NFR-028). | MVP |
| FR-082 | **Superadmin panel:** list tenants with type, state and basic usage (client count, staff count, ticket count). | MVP |
| FR-083 | Superadmin can create, suspend and reactivate tenants (FR-001, FR-002) and resend or reset the tenant admin invitation. | MVP |
| FR-084 | Superadmin can see storage use per tenant and a link to platform health dashboards. | Later |
| FR-085 | Superadmin can publish an announcement banner to all or selected tenants' staff. | Later |

### 4.8 Reporting

| ID | Requirement | Priority |
|---|---|---|
| FR-090 | Tenant dashboard: open tickets by status and category kind, tickets created and resolved per day (last 30 days), open tickets per agent. | MVP |
| FR-091 | Service metrics per period and category: median and 90th percentile first-response and resolution time, reopen rate. | Later |
| FR-092 | CSV export of tickets for a period (no attachments). | Later |
| FR-093 | Superadmin cross-tenant usage report (counts only, no ticket content). | Later |

### 4.9 Audit

| ID | Requirement | Priority |
|---|---|---|
| FR-100 | The system records audit events for: sign-in success and failure, MFA changes, role and account changes, tenant lifecycle changes, branding and settings changes, attachment downloads, personal-data export and erasure, support-access grants and actions. | MVP |
| FR-101 | Each audit event records: UTC timestamp, tenant, actor, actor role, action, target, outcome, source IP address, correlation ID. | MVP |
| FR-102 | Audit events are append-only: no application user, including superadmins, can change or delete them. They are kept for 2 years. | MVP |
| FR-103 | Staff reads of a ticket (view events) are audited. | Later |
| FR-104 | Tenant admins can view and filter their tenant's audit events (including support-access grants); superadmins can view platform-level events. | Later |

### 4.10 Privacy and GDPR

| ID | Requirement | Priority |
|---|---|---|
| FR-110 | At registration the client accepts the tenant's privacy notice and terms. The version accepted and the time are stored. | MVP |
| FR-111 | Clients can view and correct their own profile data. | MVP |
| FR-112 | Data export: a tenant admin can produce a machine-readable export of one client's personal data (profile, tickets, messages, attachments). | MVP |
| FR-113 | Erasure: a tenant admin can erase a client's personal data. Data that must be kept for legal reasons is anonymised instead of deleted. The action is audited. | MVP |
| FR-114 | Clients can request export and deletion themselves from the portal. | Later |
| FR-115 | Automatic deletion or anonymisation of closed tickets and attachments after the tenant's retention period (default 6 years). | Later |

### 4.11 Claims and first notice of loss

| ID | Requirement | Priority |
|---|---|---|
| FR-120 | First notice of loss: a ticket in a *Claim* category also records date of loss, type of loss (from a fixed list per tenant category, e.g. damage, theft, injury), place of loss (free text, optional) and the client's relationship to the claim (FR-032). Policy number is optional, so prospects and third-party claimants can report. | MVP |
| FR-121 | Agents can add or change the claim number on a ticket (the reference assigned in the tenant's own claims system); the change appears in the timeline (FR-054) and the number is searchable (FR-050). | MVP |
| FR-122 | Tenant-configurable extra fields for claim categories. | Later |

Claim adjudication, reserves, payouts and status synchronisation with a
tenant's claims system are out of scope (§8, A-01).

## 5. Non-functional requirements

Unless stated otherwise, targets apply to **prod** at the design load of
§3. Dev and test have no availability or performance targets.

### 5.1 Availability and resilience

| ID | Requirement | Target | Priority |
|---|---|---|---|
| NFR-001 | Availability of the core user flows (sign-in, view ticket list and detail, create ticket, reply) in prod. | ≥ 99.9% per calendar month (≤ about 43 min unavailability), measured by synthetic checks every minute while prod exists. Prod is created on demand for labs and deleted afterwards; time when prod does not exist is excluded and recorded (A-11). | MVP (measured from the environments lab on) |
| NFR-002 | Deployments cause no user-visible downtime. | 0 failed requests caused by a deployment in a synthetic test during deploy | MVP |
| NFR-003 | No accepted user action is lost when a background dependency (e.g. email) is down. | Notifications are delivered after the dependency recovers; 0 lost events in a fault-injection test | MVP |
| NFR-004 | Recovery point objective for prod data (database and files). | RPO ≤ 15 min | MVP |
| NFR-005 | Recovery time objective for prod after loss of the main deployment. | RTO ≤ 4 h, proven by a restore drill | Later (drill in hardening lab) |
| NFR-006 | Backup retention for prod. | ≥ 30 days; backup location per A-12 | MVP |

### 5.2 Scalability and performance

| ID | Requirement | Target | Priority |
|---|---|---|---|
| NFR-010 | Read latency for interactive requests (ticket list, ticket detail, dashboards) at design load. | p95 ≤ 500 ms, p99 ≤ 1.5 s, server-side | MVP (verified in scaling lab) |
| NFR-011 | Write latency (create ticket, reply, status change), excluding file transfer. | p95 ≤ 1 s | MVP |
| NFR-012 | Agent ticket search and filter (FR-050) on the largest tenant (500,000 clients, about 3M tickets after 6 years). In the load test this may be shown on a reduced dataset with documented extrapolation (NFR-013, v2.2). | p95 ≤ 1.5 s | MVP |
| NFR-013 | Load test proves NFR-010 to NFR-012 at the design load of §3 with synthetic data for 100 tenants and 5M clients, **or** on a reduced synthetic dataset (fewer tenants, clients or ticket history) when seeding the full dataset would exceed the NFR-063 cap; the run report then documents the dataset size, the measured results and the extrapolation method to design load (v2.2, Q-B3). | Error rate < 0.1% during a 30-minute peak test | Later (scaling lab) |
| NFR-014 | Noisy-neighbour protection: the burst scenario of §3 on one tenant does not break other tenants' targets. | Other tenants keep NFR-010 and NFR-011 during the burst | Later (scaling lab) |
| NFR-015 | Per-tenant and per-user rate limits on public endpoints; excess requests get a clear "too many requests" response. | Limits configurable; defaults set in an ADR | MVP |
| NFR-016 | Client portal first load on a mid-range phone over 4G. | Largest Contentful Paint ≤ 2.5 s (p75) | MVP |
| NFR-017 | Email sent after the triggering event. | p95 ≤ 2 min | MVP |
| NFR-018 | Growth to 250 tenants and 10M clients needs only configuration or scale changes, not a redesign. | Documented scaling path in `ARCHITECTURE.md` | MVP (design) |

### 5.3 Security

| ID | Requirement | Target | Priority |
|---|---|---|---|
| NFR-020 | Security baseline. | OWASP ASVS 4.0 Level 2 as reference checklist; reviewed in the hardening lab | MVP |
| NFR-021 | Encryption in transit. | TLS 1.2 or higher on all public and internal connections; HSTS on web endpoints | MVP |
| NFR-022 | Encryption at rest for all data stores and backups. | 100% of stores | MVP |
| NFR-023 | No secrets in source code, pipeline definitions or container images; service-to-service access without stored credentials where the platform allows. | 0 secrets found by a secret scan in CI | MVP |
| NFR-024 | Least privilege for people and workloads; no standing human write access to prod data. | Reviewed per ADR | MVP |
| NFR-025 | Protection against credential stuffing and fake registrations: account lockout or throttling, and bot protection on registration. | Throttling MVP; bot protection Later (trigger: real data or observed abuse, NFR-028) | MVP / Later |
| NFR-026 | Web security headers and a Content Security Policy that does not allow inline or third-party scripts from tenant branding (FR-011). | Security-header scan grade A (A) | MVP |
| NFR-027 | Dependency and code scanning in CI; high or critical vulnerabilities block release. | 0 open high/critical findings at release | MVP |
| NFR-028 | Abuse and spam control for open registration (FR-020, non-policyholders): verified email before the first ticket; per-client limits on new tickets and attachment volume; staff can deactivate abusive accounts (FR-081). | Defaults (A): max 5 new tickets per client per day, max 200 MB uploads per client per day; configurable; excess rejected with a clear message | MVP |

### 5.4 Tenant isolation

| ID | Requirement | Target | Priority |
|---|---|---|---|
| NFR-030 | No user can read or change another tenant's data through any UI, API, file link, export, search, cache or background job. | 0 cross-tenant access in automated tests covering every data-access path | MVP |
| NFR-031 | Tenant context is mandatory for every tenant data access; a missing tenant context fails safely (denies access). | Enforced centrally and covered by tests | MVP |
| NFR-032 | Logs, metrics and traces carry the tenant ID so that per-tenant behaviour can be analysed. | 100% of request telemetry | MVP |
| NFR-033 | The isolation model allows a later move of a single large tenant to dedicated capacity without changing application behaviour. | Documented in the isolation ADR | MVP (design) |

### 5.5 Privacy, GDPR and data residency

| ID | Requirement | Target | Priority |
|---|---|---|---|
| NFR-040 | All personal data, including files, logs and telemetry, is stored and processed in Azure Poland Central, or another EU region only where Poland Central cannot provide the capability (documented per ADR). Backup copies may be in another EU region (A-12). | 100% of data stores | MVP |
| NFR-041 | Ticket content, messages and attachments are treated as potentially containing special-category data (e.g. health data in claim documents, GDPR Art. 9). | Access restricted (FR-028) and auditable (FR-100) | MVP |
| NFR-042 | Telemetry contains no ticket content and no direct personal data beyond pseudonymous IDs. | Checked by review and a log-content test | MVP |
| NFR-043 | Data-subject requests (export, erasure) can be fulfilled within the GDPR deadline. | Export and erasure for one client complete in ≤ 24 h of the admin action | MVP |
| NFR-044 | Records needed for breach assessment (who accessed what, when) are available within 72 h of detection. | Audit events queryable within 1 h of occurrence | MVP |
| NFR-045 | Data minimisation: only fields needed for ticket handling are collected; no national ID numbers (PESEL) required (A). | Reviewed per feature | MVP |

### 5.6 Observability and operations

| ID | Requirement | Target | Priority |
|---|---|---|---|
| NFR-050 | Structured logs, metrics and distributed traces across all components, correlated by a correlation ID. | 100% of requests and background jobs | MVP |
| NFR-051 | Health endpoints (liveness and readiness) for every deployable unit. | MVP | MVP |
| NFR-052 | Alerts on availability SLO burn, error-rate spikes, failed background jobs and budget thresholds. | Technical alerts (availability, errors, background jobs): within 5 min of the condition. Budget alerts: within Cost Management's own latency (cost data typically arrives 8–24 h after usage and budgets are evaluated about every 24 h, [Microsoft Learn](https://learn.microsoft.com/azure/cost-management-billing/costs/tutorial-acm-create-budgets)) | MVP |
| NFR-053 | Dashboards for SLOs, per-tenant traffic and errors, and cost. | MVP | MVP |
| NFR-054 | Telemetry retention is limited to what the budget allows. | ≥ 30 days (A) | MVP |

### 5.7 Cost

| ID | Requirement | Target | Priority |
|---|---|---|---|
| NFR-060 | Total Azure spend for all environments during the learning phase. | ≤ 100 USD/month hard ceiling; ≤ 50 USD/month target (C-06) | MVP |
| NFR-061 | Budgets and alerts exist from the first deployment. | Alerts at 50%, 80% and 100% of the monthly budget | MVP |
| NFR-062 | Idle non-prod environments cost close to zero: they scale to zero or are created on demand and deleted after use. | Idle dev/test cost ≤ 10 USD/month combined (A) | MVP |
| NFR-063 | Load-test runs are scripted, time-boxed and torn down automatically. | ≤ 20 USD per run (A) | Later |
| NFR-064 | Every ADR states approximate monthly cost at run load and at design load. | 100% of ADRs | MVP |

### 5.8 Maintainability and delivery

| ID | Requirement | Target | Priority |
|---|---|---|---|
| NFR-070 | All infrastructure is defined in Bicep; no manual changes to shared environments. | 100% | MVP |
| NFR-071 | Every change to `main` is built, tested and scanned by GitHub Actions; deployments to test and prod happen only from the pipeline; prod requires manual approval. | 100% | MVP |
| NFR-072 | An environment can be created from scratch from the repository. | ≤ 1 h, documented | MVP |
| NFR-073 | Automated tests for domain rules, authorization and tenant isolation in every lab. | CI fails on test failure | MVP |
| NFR-074 | Database schema changes are versioned and applied by the pipeline, compatible with zero-downtime deployment (NFR-002). | MVP | MVP |
| NFR-075 | Each lab can be built and run locally without an Azure subscription. | MVP | MVP |

### 5.9 Usability, accessibility and localisation

| ID | Requirement | Target | Priority |
|---|---|---|---|
| NFR-080 | Accessibility of the client portal and staff workspace. | WCAG 2.1 AA | MVP |
| NFR-081 | Responsive UI. | Usable from 360 px width; last 2 versions of Chrome, Edge, Firefox, Safari | MVP |
| NFR-082 | Language: English only for all UI and email texts. Multi-language support and localisation are out of scope (§8). | 100% of UI and email texts in English | MVP |
| NFR-083 | Times stored in UTC and shown in Europe/Warsaw time (A). | MVP | MVP |

## 6. Constraints

| ID | Constraint | Source |
|---|---|---|
| C-01 | Application: .NET 10+ / ASP.NET Core; UI: Blazor. | User / AGENTS.md |
| C-02 | Cloud: Microsoft Azure. | User |
| C-03 | Infrastructure as code: Bicep. | User |
| C-04 | CI/CD: GitHub Actions. Azure DevOps is not used now. | User |
| C-05 | Primary region Azure Poland Central; personal data stays in Poland/EU. Poland Central has 3 availability zones and **no paired region** ([Microsoft Learn](https://learn.microsoft.com/azure/reliability/regions-list)), so in-region resilience relies on zones, and any cross-region copy needs an explicitly chosen EU target region. | User; Microsoft Learn |
| C-06 | Budget: free tier or about 50–100 USD/month in total. This conflicts with 99.9% availability (many free/lowest tiers have no SLA), three environments and the scale target. Resolution: **design** for the targets, **run** cheap (lowest suitable tiers, scale-to-zero, on-demand environments, prod created only for the labs that need it, A-11). Tier and service choices are made in ADRs. | User; architect |
| C-07 | Environments: dev, test, prod. Dev is permanent and scales to zero; test and prod are created from Bicep on demand and deleted after use. | User (2026-10-01: prod on demand; test on demand proposed by the architect and accepted by the user, Q-B2; ADR-0014) |
| C-08 | One part-time learner; time budget unknown; labs must be small. | User |
| C-09 | No specific Azure services are assumed until the matching ADR is accepted. | AGENTS.md |
| C-10 | Regulatory context: GDPR applies (insurance data is personal and may be special-category). Insurers, and to a lesser extent brokers and agencies, are regulated financial entities in the EU (e.g. DORA, Polish financial supervisor guidance on cloud outsourcing), which affects contracts and resilience expectations. The project designs for these but does not seek certification or legal compliance sign-off. | Architect |

## 7. Assumptions

| ID | Assumption | Status |
|---|---|---|
| A-01 | Tickets cover customer-service requests and claims handling, including first notice of loss. The product does not adjudicate, calculate or pay claims and does not integrate with tenants' core policy or claims systems. | Confirmed (scope); no-integration part remains an architect assumption, accepted with the scope answer |
| A-02 | End clients log in and raise tickets themselves. They can be policyholders, prospects or third-party claimants; relationship is self-declared (FR-032) and not verified against policy data in the MVP. | Confirmed |
| A-03 | Tenants can be insurers, brokers or agencies. Tenant type is descriptive; all types get the same features and data model. | Confirmed (type); "same features" is an architect assumption |
| A-04 | Tenants are onboarded by the superadmin; there is no self-service tenant sign-up and no billing. | Assumed |
| A-05 | The target tenant address is a subdomain of a platform domain; custom domains come later. **No platform domain is owned now and there is no commitment to buy one.** Until one exists, local development uses `<slug>.localhost` (full multi-tenancy) and Azure dev/test use the default Azure hostname mapped to one demo tenant. Social login and real email delivery are exercised locally only. Buying a domain is an explicit gate before prod, multi-tenant use in Azure or real email delivery. | Confirmed (user, 2026-10-01) |
| A-06 | Workload numbers in §3, RPO/RTO/backup retention (NFR-004 to NFR-006) and data retention (FR-102, FR-115). Remaining (A) details in FRs and NFRs are architect defaults. | Confirmed (numbers listed); (A) details assumed |
| A-07 | All data in every environment is synthetic. It is handled as if real. | Assumed |
| A-08 | Legal roles: each tenant is the data controller for its clients' data; the platform owner is the processor. A data-processing agreement is out of scope for the project but assumed to exist. | Assumed |
| A-09 | Malware scanning of attachments is deferred; the risk is accepted for the MVP because data is synthetic. Open registration (FR-020) raises this risk; scanning must be in place before any real data is processed. | Assumed |
| A-10 | UI and email language is English only. | Confirmed |
| A-11 | Prod exists only while a lab needs it: it is created from Bicep, used, and then deleted. NFR-001, NFR-004 and NFR-006 are demonstrated only during these live windows (configuration checks, synthetic checks and a restore drill while prod exists). Time when prod does not exist is excluded from the availability SLO. Prod data is synthetic and is not kept between windows (A-07). | Confirmed (user, 2026-10-01) |
| A-12 | Backup and DR location is **not a current requirement** (user, 2026-10-01: deferred). Default platform backups are used; backup copies may be stored in another EU region. Revisit when real data or a tenant contract requires it. | Deferred |

## 8. Out of scope

- Claims adjudication, reserves, payout, policy administration, premium
  calculation; integration or status sync with tenants' core systems.
- Statutory complaint-deadline tracking (per-tenant due dates via
  FR-056 are a Later candidate).
- Multi-language UI and localisation (English only).
- Billing, payments and subscription invoicing for tenants.
- Self-service tenant sign-up.
- Automated identity or policy verification of clients (manual
  verification flag is FR-033, Later).
- Live chat, phone/telephony integration, video.
- SMS and push notifications (later candidates, FR-077).
- AI features (classification, suggested replies).
- Native mobile apps.
- Multi-region active-active deployment; cross-region DR as a
  requirement (A-12).
- Formal certification (ISO 27001, SOC 2) and legal compliance sign-off.

## 9. Decisions for upcoming ADRs

These are deliberately **not** decided here.

| Topic | Driven by |
|---|---|
| Tenancy data-isolation model (shared schema with tenant key, schema or database per tenant, hybrid) | NFR-030 to NFR-033, NFR-014, C-06 |
| Tenant resolution (subdomain, path, custom domain) and routing | FR-004, FR-006 |
| Compute/hosting for web app and background work | NFR-001, NFR-002, NFR-062, C-06 |
| Relational data store and tier; scaling path to 5M clients | NFR-010 to NFR-018, NFR-004 |
| File storage for attachments and branding assets; lifecycle and retention | FR-060 to FR-066, FR-115 |
| Identity provider(s) for four audiences, open client registration, social login, MFA, per-tenant branded sign-in | FR-020 to FR-033, FR-010 |
| Blazor render modes for client portal, staff workspace, admin panels | NFR-016, NFR-080, FR-010 |
| White-label theming approach | FR-010 to FR-016, NFR-026 |
| Background processing and messaging (reliable, idempotent) | FR-075, NFR-003, NFR-017 |
| Email delivery service and sender domain | FR-070 to FR-074 |
| Secrets and configuration management | NFR-023 |
| Observability stack and telemetry retention | NFR-050 to NFR-054, NFR-042 |
| Search for agent queries (database queries vs dedicated search) | FR-050, FR-052, NFR-012 |
| Rate limiting, abuse control and edge protection | NFR-015, NFR-025, NFR-028, NFR-014 |
| Environments, on-demand provisioning and cost controls | C-06, C-07, NFR-060 to NFR-063 |
| CI/CD pipeline design and promotion with approvals | NFR-071, NFR-002, NFR-074 |
| Backup within RPO 15 min / RTO 4 h; zone redundancy vs cost; Poland Central has no paired region, so pair-based geo-redundant options are unavailable and any cross-region copy needs an explicit EU target (C-05, A-12) | NFR-004 to NFR-006, NFR-040 |
| Audit log storage (append-only) | FR-100 to FR-104 |

## 10. Questions

### Resolved (2026-10-01)

| ID | Question | Answer |
|---|---|---|
| Q-01 | Ticket scope | Both customer-service requests and claims handling, incl. FNOL (§1, §4.11). No adjudication or core-system integration (A-01). |
| Q-02 | Do end clients raise tickets themselves? | Yes (A-02). |
| Q-03 | Tenant types | Insurers, brokers and agencies (FR-001, A-03). |
| Q-04 | Can non-policyholders raise tickets? | Yes: prospects and third-party claimants (FR-020, FR-032, FR-120); abuse controls NFR-028; verification Later (FR-033). |
| Q-05 | Workload assumptions in §3 | Accepted (§3, A-06). |
| Q-06 | RPO, RTO, backups; DR region | RPO 15 min, RTO 4 h, 30-day backups accepted (NFR-004 to NFR-006). Backup/DR location deferred as not important now (A-12). |
| Q-07 | Retention | Tickets and attachments 6 years; audit events 2 years (FR-102, FR-115). |
| Q-08 | Tenant approval for support access? | No: time-limited, audited self-grant (FR-029). |
| Q-09 | UI languages | English only (NFR-082, A-10). |
| Q-10 | Statutory complaint deadline | Not a requirement; FR-057 removed; per-tenant due dates via FR-056 (Later). |
| Q-11 | Prod downtime outside lab windows | Accepted (A-11); refined in v2.1: prod is created on demand and deleted between labs. |
| Q-12 | Product name | InPolsure; repository stays `AzurePet`. |
| Q-13 | Architecture-review decisions affecting requirements | Environments lifecycle (C-07, A-11, NFR-001), no domain yet (A-05), budget-alert latency (NFR-052). Details are in `ARCHITECTURE.md` §10.2 and §10.3 (both resolved). v2.2: reduced load-test dataset with documented extrapolation (NFR-013, NFR-012; Q-B3), test on demand confirmed (C-07; Q-B2). |

### Still open

None. Remaining **(A)** details (e.g. session timeouts, file types,
reopen window, abuse-limit defaults) are architect defaults, approved
with this document; the user can still change them through a normal
requirements change.
