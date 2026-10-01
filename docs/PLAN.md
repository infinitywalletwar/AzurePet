# InPolsure: Project Plan

| | |
|---|---|
| **Product** | InPolsure. The repository is still named `AzurePet`. |
| **Status** | **Approved** v3.1 (confirmed by the user on 2026-10-01 together with the ADRs). v3.1 applies the architecture-review decisions on environments and the platform domain (§5, §8). |
| **Phase** | 0: Plan (approved) |
| **Owner** | solution-architect |
| **Last updated** | 2026-10-01 |
| **Next document** | [`REQUIREMENTS.md`](REQUIREMENTS.md) (approved v2.1), then [`ARCHITECTURE.md`](ARCHITECTURE.md) |

This document sets the direction for the project: why it exists, what it
should teach, what the domain is, and how the work is phased. It contains
no architecture or technology decisions. Those belong in
`ARCHITECTURE.md` and the ADRs. Detailed, numbered requirements live in
`REQUIREMENTS.md`. Anything marked *tentative* can change in a later
phase.

---

## 1. Vision

Build **InPolsure**, a realistic, production-oriented, multi-tenant SaaS
ticket management application for the insurance industry, on Microsoft
Azure with .NET 10+ / ASP.NET Core and a Blazor UI. Build it
in small labs that each add one meaningful capability. The application is
the teaching vehicle. The real product is the learner's ability to
design, justify, build, deploy, observe and scale a cloud application end
to end: requirements, architecture and ADRs, code, Bicep infrastructure
as code, and CI/CD. At every step the solution stays the simplest one
that meets the stated requirements.

## 2. Goals

### Learning goals (all are priorities, per the user)

- **Design discipline.** Turn vague ideas into measurable requirements.
  Write an architecture document and ADRs that state the problem, the
  alternatives, the trade-offs, and the scale and cost impact.
- **Backend.** ASP.NET Core APIs, EF Core and relational data modelling,
  validation, error handling, a modular monolith with clear module
  boundaries.
- **Multi-tenancy.** Tenant resolution, tenant data isolation, per-tenant
  configuration and branding, noisy-neighbour protection.
- **UI.** Blazor with render modes chosen on purpose per area (client
  portal, tenant admin panel, superadmin panel), white-label theming,
  auth in the UI, forms.
- **Identity.** Self-registering external users, staff accounts, admins,
  superadmin, social login (Google, Microsoft), MFA, role- and
  tenant-scoped authorization.
- **Azure PaaS.** Hosting, a managed database, file storage, secrets,
  managed identity, configuration.
- **Asynchronous work.** Background jobs, messaging, retries,
  idempotency, email notifications.
- **Operations.** Structured logging, metrics, tracing, health checks,
  alerts, SLOs, cost monitoring.
- **Delivery.** Bicep IaC, GitHub Actions CI/CD, dev → test → prod
  promotion, safe deployments.
- **Scaling and performance.** Load testing against a stated target
  (100+ tenants, 5M+ end clients), finding bottlenecks, scaling for a
  measured reason.
- **Privacy and compliance by design.** GDPR-aware handling of personal
  insurance data, data residency in Poland/EU, audit trails.

### Product goals

- A coherent multi-tenant ticket management product for the insurance
  industry, with real roles, workflows and per-tenant branding.
- Deployable to Azure from a pipeline, reproducible from code,
  observable in production.
- Good enough that it could plausibly serve a small real insurer.

### Non-goals

- A commercial launch, real billing or payment processing, or formal
  compliance certification (e.g. ISO 27001, SOC 2). Compliance controls
  are designed as if real but not certified.
- Full claims adjudication or a policy administration system (see §3).
- Microservices, Kubernetes, multi-region or event sourcing as goals in
  themselves. Each is considered only if a requirement or measurement
  triggers it.
- Native mobile apps (the UI must still be responsive).
- Pixel-perfect UX design.
- Covering every Azure service.

## 3. Domain (decided)

**Chosen by the user: a ticket manager for the insurance domain, sold as
a multi-tenant SaaS.** The three options from Plan v1 (A: pet care and
clinic booking, B: event ticketing, C: expense management) were
considered and not chosen.

**Interpretation (confirmed by the user, 2026-10-01):**

- The **platform owner** (one company, the superadmin) operates the SaaS
  and sells it to **tenants**: insurance companies, brokers and
  agencies. Tenant type is descriptive; all get the same features.
- Each tenant uses the product as its **customer service and claims
  intake desk**. End clients log in and raise tickets themselves:
  service requests (policy questions, document and change requests,
  complaints) and **claims, including first notice of loss (FNOL)**.
  Tenant staff (agents) handle them.
- End clients can be **policyholders, prospects or third-party
  claimants**. The relationship is self-declared at registration; the
  system does not verify it in the MVP.
- The product **records, tracks and communicates** about claims; it does
  **not** adjudicate, calculate or pay claims, and does not integrate
  with or replace the tenant's policy or claims core systems. Policy and
  claim numbers are stored as references.
- Each tenant has its own **admin panel** and a **branded (white-label)
  client portal**. The platform owner has a **superadmin panel**.
- The UI is **English only**.

Why it fits the learning goals: multi-tenancy, white-label UI, four
identity audiences, a state-machine workflow, sensitive attachments,
background notifications, audit and GDPR give every target skill a
natural reason to exist. The 5M-client target gives scaling labs a real
basis.

## 4. Users and roles

| Role | Scope | Description | Key needs |
|---|---|---|---|
| Superadmin (platform owner) | Platform | Operates the SaaS | Onboard and suspend tenants, tenant plans and limits, platform health and usage, support access by time-limited, audited self-grant |
| Tenant Admin | One tenant | Administrator of the insurer, broker or agency | Branding, staff accounts and roles, ticket categories and settings, reports |
| Tenant Agent (staff) | One tenant | Customer-service or claims staff | Work queue, register claims, reply to clients, internal notes, assign, change status |
| End Client | One tenant | Policyholder, prospect or third-party claimant; self-registers on the tenant's branded portal | Raise and track service requests and claims (FNOL), upload documents, get email updates |

Detailed personas and permissions are in `REQUIREMENTS.md`.

## 5. Scope (tentative; requirement-level detail in `REQUIREMENTS.md`)

### MVP

- Tenant onboarding by the superadmin; each tenant reachable on its own
  address with its own branding (logo, colours, name).
- Sign-up and sign-in for end clients (email and password, Google,
  Microsoft); staff accounts created by the tenant admin; MFA for staff
  and admins.
- Ticket lifecycle: create, categorise, assign, comment (public and
  internal), change status, resolve, close, reopen.
- Claims intake: FNOL with basic loss details; claim number recorded as
  a reference.
- Open client registration (non-policyholders included) with basic
  abuse limits.
- Attachments on tickets with secure upload and download.
- Email notifications on key ticket events (background processing).
- Tenant admin panel and superadmin panel (basic).
- Audit log of security-relevant and data-access events.
- GDPR basics: data residency, consent and privacy notice, data export
  and erasure on request.
- Deployed to Azure through GitHub Actions from Bicep, with telemetry,
  health checks and budget alerts: dev is permanent; test and prod are
  created on demand and deleted after use.

### Later scope (candidates)

- SLA timers, due dates and escalation per tenant; teams and queues.
- Manual client verification flag; tenant-configurable claim fields.
- Reports and dashboards, CSV export.
- Custom domains per tenant; branded email sender domains.
- Attachment malware scanning, thumbnails and previews.
- Tenant SSO (federation with the insurer's own identity provider).
- Email-to-ticket, other channels (SMS, push), notification preferences.
- Integration API for insurers' core systems.
- Tenant plans with usage limits and metering.
- Performance work driven by load tests (caching, scale-out, data
  partitioning).

Out of scope for now: payments and billing, claims adjudication, policy
administration, core-system integration, statutory complaint deadlines,
multi-language UI, live chat or phone integration, AI features, native
mobile.

## 6. Guiding principles

1. **Requirements first.** Every component traces to a requirement.
2. **Simplest architecture that works.** Start with a single deployable
   modular monolith. Split only for a stated, measured reason.
3. **Managed PaaS over self-managed infrastructure.**
4. **Every significant choice is an ADR** (problem, alternatives,
   trade-offs, scalability, operations, cost). ADRs start as *Proposed*.
5. **Design for evolution.** Document the trigger for each later step;
   do not build it early.
6. **Design for the target, run for the budget.** The architecture must
   be able to reach the scale and availability targets, but day-to-day
   environments run on the cheapest suitable tiers, scale to zero or are
   created on demand. Prod-grade capacity is provisioned only for
   specific labs (e.g. load tests) and torn down afterwards.
7. **Tenant isolation is a security property.** Cross-tenant data access
   must be impossible by construction and covered by automated tests.
8. **Privacy by design.** Personal data stays in Poland/EU, is minimised,
   encrypted, access-logged, exportable and erasable.
9. **Everything as code.** Bicep and GitHub Actions; no click-ops in
   shared environments.
10. **Secure and observable from day one.**
11. **Small increments.** Each lab is buildable, testable, reviewable and
    deployable on its own. The user's time budget is unknown, so labs
    stay small.

## 7. Phase plan and milestones

| # | Phase | Output documents | Exit criteria | Agent(s) |
|---|---|---|---|---|
| 0 | **Plan** | `docs/PLAN.md` | Domain chosen; goals, MVP and principles agreed; open questions answered or recorded as assumptions | solution-architect |
| 1 | **Requirements** | `docs/REQUIREMENTS.md` | Personas, functional requirements, measurable NFRs, constraints, assumptions and out-of-scope written down; user approves | solution-architect |
| 2 | **Architecture** | `docs/ARCHITECTURE.md` | Every component traces to a requirement; candidate Azure services listed with alternatives | solution-architect |
| 3 | **Architecture review** | Review notes | Gaps, risks and over-engineering addressed or explicitly accepted | solution-architect; the user |
| 4 | **Architecture decisions** | `docs/architecture/decisions/NNNN-*.md` | ADR per significant choice; user marks them Accepted | solution-architect |
| 5 | **Lab roadmap** | `docs/ROADMAP.md`, `docs/labs/NN-*.md` | Ordered labs with testable acceptance criteria; `AGENTS.md` "Current phase" updated | solution-architect |
| 6 | **Incremental implementation** | Code, Bicep, pipelines, tests | Per lab: acceptance criteria met; reviews pass; deployed | dotnet-azure-developer; reviewer agents |

Phase 6 repeats per lab. If a lab finds that a decision is wrong, the
work goes back to phase 4 and a new ADR supersedes the old one.

### ADRs expected in phase 4 (topics, not decisions)

Tenancy data-isolation model; tenant resolution (subdomain, path, custom
domain); hosting/compute; relational data store; file storage;
identity provider(s) for four audiences and social login; Blazor render
modes per panel; white-label theming approach; background processing and
messaging; email delivery; secrets and configuration; observability;
environments and cost controls (on-demand / scale-to-zero); GitHub
Actions CI/CD and promotion; backup/DR within the residency constraint.

### Likely lab themes (tentative, to be finalised in `ROADMAP.md`)

1. **Walking skeleton:** solution structure, Blazor and ASP.NET Core
   host, health check, first test.
2. **CI:** GitHub Actions build, test and quality gates.
3. **IaC and first deployment:** Bicep for dev, CD from GitHub Actions,
   budget alerts.
4. **Data:** domain model, EF Core, migrations, managed relational DB.
5. **Multi-tenancy core:** tenant resolution, tenant context, isolation
   enforcement and cross-tenant tests.
6. **Identity I:** staff and admin sign-in, roles, MFA, superadmin.
7. **Identity II:** client self-registration, Google and Microsoft login.
8. **Tickets:** lifecycle, comments, assignment, concurrency, claims
   intake (FNOL).
9. **Attachments:** secure upload and download, limits, retention.
10. **Background processing:** email notifications, retries, idempotency.
11. **White-label:** per-tenant branding of portal and emails.
12. **Admin panels:** tenant admin and superadmin features.
13. **Audit and GDPR:** audit log, data export, erasure.
14. **Observability:** logs, traces, dashboards, SLO alerts.
15. **Environments:** dev → test → prod promotion, zero-downtime
    deployment.
16. **Scaling:** synthetic data for 100 tenants / 5M clients, load tests,
    measured optimisation.
17. **Hardening:** security review, backup and restore drill, cost
    review.

## 8. Constraints and assumptions

**Confirmed by the user:**

- Stack: .NET 10+ / ASP.NET Core, Blazor, Azure, Bicep.
- CI/CD: **GitHub Actions** (Azure DevOps possibly later; not now).
- Environments: **dev, test, prod**.
- Region: **Poland Central**; data residency in Poland/EU.
- Availability target: **99.9%** for prod.
- Budget: **free tier or about 50–100 USD/month** in total; minimise cost.
- Notifications: **email** for the start.
- Time budget: unknown; keep labs small.
- Workload model, RPO 15 min / RTO 4 h / 30-day backups, retention
  (6 years tickets, 2 years audit): as in `REQUIREMENTS.md`.
- Prod is **created on demand** for the labs that need it and deleted
  afterwards; time without prod is excluded from the 99.9% SLO. Dev is
  permanent (scale-to-zero); test is also on demand.
- UI language: **English only**.
- **No platform domain** is bought now, and there is no commitment to
  buy one. Until then a "pre-domain mode" applies (local
  `<slug>.localhost`; Azure dev/test on the default Azure hostname with
  one demo tenant). Buying a domain is a gate before prod, multi-tenant
  use in Azure, or real email delivery.
- GitHub repository: **public**.

**Deferred:** backup/DR location is not a current requirement; default
platform backups (EU regions allowed) until real data needs more.
Poland Central has no paired region, which the backup ADR must reflect.

**Assumptions (to confirm):**

- Solo learner, part-time.
- Data is fictional (synthetic), but handled as if it were real
  personal and potentially sensitive insurance data.
- One Azure region; multi-region is only an evolution step.
- Each lab can be built and tested locally without Azure.
- Scale and workload numbers not given by the user are assumed in
  `REQUIREMENTS.md` and clearly marked.

**Budget tension (recorded, resolved in ADRs):** 99.9% availability,
three environments and a 5M-client design target normally cost far more
than 50–100 USD/month. Free and lowest tiers often carry no SLA. The plan
therefore separates *design targets* (what the architecture must be able
to reach, proven by load tests in short-lived environments) from *run
targets* (what is kept running day to day within budget). The 99.9%
target applies to prod while it exists; prod is created for a lab and
deleted afterwards, and time without prod is excluded from the SLO.

## 9. Risks

| Risk | Impact | Mitigation |
|---|---|---|
| Domain scope creep (claims processing, policy systems) | Labs stall on business logic | Product tracks and communicates, does not adjudicate (§3); strict MVP |
| Multi-tenancy bugs leak data across tenants | Severe privacy breach (in a real system) | Isolation as a requirement with automated tests; isolation model chosen in an ADR |
| Budget vs 99.9% and 3 environments | Overspend, or targets not credible | Design vs run targets (§8); on-demand environments; budgets and alerts from the first deployment |
| Budget vs 5M-client load tests | Load tests cost money | Short, scripted, torn-down load-test runs with a per-run cost cap |
| GDPR and insurance regulation complexity (special-category data, DORA, supervisor guidance) | Time sink or false sense of compliance | Implement core GDPR controls; record regulatory items as constraints and out-of-scope certification |
| Identity complexity (four audiences, social login, MFA) | Large time sink | Split into two identity labs; simplest working flow first |
| Open registration (prospects, third-party claimants) invites spam, fake accounts and storage abuse | Agent noise, storage cost, malware exposure | Email verification, per-client ticket and upload limits, account deactivation (NFR-028); bot protection and malware scanning before real data |
| White-label scope (custom domains, per-tenant CSS, email domains) | UI and ops complexity | MVP: subdomain, logo, colours, name; custom domains later |
| Over-engineering | Cost, fragility | Principles 1–5; every addition needs an ADR and a requirement |
| Fast-moving platform (.NET, Azure SKUs, region feature availability in Poland Central) | Outdated or unavailable options | Verify against current Microsoft Learn docs and regional availability when writing ADRs |
| Solo reviewer blind spots | Design flaws unnoticed | Reviewer agents plus the architecture-review phase |

## 10. Questions

### Resolved (2026-10-01)

| # | Question | Answer |
|---|---|---|
| 1 | Domain | Ticket manager, insurance domain (interpretation in §3, confirmed) |
| 2 | Tenancy | Multi-tenant SaaS: platform owner (superadmin) sells to tenant companies; per-tenant admin panel and branded UI |
| 3 | Scale | 100+ tenants, each 50,000+ clients (5M+ end clients) |
| 4 | Budget | Free tier or ~50–100 USD/month; minimise cost |
| 5 | Auth | Self-registering clients, staff, tenant admins, superadmin, Google and Microsoft login |
| 6 | Notifications | Email for the start |
| 7 | CI/CD | GitHub Actions; Azure DevOps maybe later |
| 8 | Environments | dev, test, prod |
| 9 | Availability | 99.9% |
| 10 | Region | Poland Central; data in Poland/EU |
| 11 | Time budget | Unknown; keep labs small |
| 12 | Learning priorities | All |
| 13 | Ticket scope, tenant types, non-policyholders, workload, RPO/RTO, retention, support access, language, complaint deadline, prod downtime, product name | Answered; see `REQUIREMENTS.md` §10 (Q-01 to Q-12) |

### Still open

None. Backup/DR location is deferred (§8).
