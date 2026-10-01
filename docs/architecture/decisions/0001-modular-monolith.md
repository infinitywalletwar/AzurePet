# ADR-0001: Modular monolith in one ASP.NET Core host

- **Status:** Accepted (2026-10-01)
- **Date:** 2026-10-01 (amended 2026-10-01 after the architecture review: Reporting folded into Tickets; `Migrate` role)
- **Deciders:** the user (approver), solution-architect
- **Requirements:** C-01, C-06, C-08, NFR-002, NFR-018, NFR-062, NFR-073, NFR-074, NFR-075; PLAN principles 2 and 5

## Context

InPolsure has eight functional areas: tenancy, identity, tickets/claims,
attachments, notifications, audit, privacy and reporting. There are four
user audiences. The design target is 5M clients and 300 req/s
(REQUIREMENTS §3), but the run budget is 50–100 USD/month for all three
environments (C-06). The builder is one part-time learner (C-08). Every
lab must build and run locally (NFR-075). Most use cases touch several
areas in one transaction. For example, "create ticket" writes the
ticket, its attachments, the timeline, an outbox event and an audit
event.

## Decision

Build InPolsure as a **modular monolith**:

1. **One deployable**: one ASP.NET Core (.NET 10) host and one container
   image. It contains the Blazor UI, the minimal-API endpoints and the
   background hosted services.
2. **Modules (seven)**: Tenancy, Identity & Access, Tickets (incl.
   the Claims/FNOL and Reporting sub-areas), Attachments, Notifications,
   Audit, Privacy, plus a small shared kernel (ARCHITECTURE §4).
   **Reporting is not a module at MVP.** The only MVP report (FR-090
   tenant dashboard) is a set of read queries over tickets, so it lives
   in `Tickets/Reporting`. Trigger to split it out: a report that spans
   modules (e.g. FR-093 cross-tenant usage) or a separate read model.
3. **Boundaries are enforced in code**: one project per module, with a
   public contract (commands, queries, integration events) and
   `internal` implementation types. There is one EF Core `DbContext` and
   one SQL schema per module. Modules do not join across schemas.
   Architecture tests in CI check the dependency rules.
4. **Cross-module side effects** go through integration events stored in
   a transactional outbox (ADR-0007). They are not direct calls into
   another module's internals.
5. **Roles by configuration**: the host can start as `Web`, `Worker`, or
   both (the default). This allows a later split into two Container Apps
   from the same image without code changes. The same image also has
   one-shot commands (`migrate`, `bootstrap-superadmin`, `seed`) that run
   as a Container Apps Job with their own identity (ADR-0009, ADR-0012),
   so there is still one artefact per release.

## Alternatives considered

| Option | Pros | Cons | Verdict |
|---|---|---|---|
| **Modular monolith (chosen)** | One deploy, one pipeline, local transactions across modules, cheapest hosting (one app per env can scale to zero), easy local run, clear path to extraction | Discipline needed to keep boundaries; one runtime failure domain; all modules scale together | Meets all requirements at the lowest cost and effort |
| Traditional layered monolith (no module boundaries) | Simplest to start | Boundaries erode; hard to extract later; weak learning value for module design (PLAN learning goals) | Rejected |
| Microservices (e.g. tickets, identity, notifications as separate services) | Independent scaling and deployment | Distributed transactions (ticket + outbox + audit), service-to-service auth, several apps × 3 envs (each with min replicas or cold starts), more pipelines; PLAN lists microservices as a non-goal unless triggered | Rejected: no requirement needs independent deployability; cost and effort exceed budget |
| Separate Web (Blazor) and API apps from day one | Clear front/back split; API reusable | Two apps per env, network hop, auth between them (tokens or BFF), duplicated contracts; no external API consumer exists in MVP | Rejected for MVP; reconsider when an integration API (Later) or WebAssembly UI is introduced |
| Serverless functions per use case (Azure Functions) | Scale to zero per function | Blazor UI still needs a host; fragmented domain logic; cold starts; harder local transactions | Rejected |

## Consequences

**Trade-offs**

- (+) Lowest operational load: one image, one Container App per
  environment, one migration pipeline.
- (+) Use cases stay ACID within one database transaction.
- (−) A bug or memory leak in one module affects all of them. This is
  mitigated by health probes, restarts and tests.
- (−) Boundaries rely on project structure and architecture tests, not
  network isolation. Reviews (architecture-compliance-reviewer) must
  check them.

**Scalability**

- Scales horizontally: the web host is stateless except for Interactive
  Server circuits, which use sticky sessions (ADR-0008). Container Apps
  adds replicas. At design load the estimate is 4–6 replicas
  (ARCHITECTURE §9.7).
- Evolution triggers (ARCHITECTURE §3.2): split the worker when
  background load hurts web latency; extract a module into its own
  service only for a measured reason, such as a very different scaling
  profile, a separate team, or a compliance boundary. Because modules
  communicate through contracts and outbox events, extraction means
  replacing in-process calls with HTTP or messaging.

**Operations**

- One set of dashboards and alerts; one deployment per change.
- Database migrations are versioned per module (one migrations history
  table per `DbContext`) and applied together by the pipeline.

**Cost**

- Run load: one Container App per environment; dev scales to zero when
  idle and test/prod exist only on demand (ADR-0014). This saves roughly
  10–30 USD/month per extra always-on service compared with a split
  design.
- Design load: the whole host scales together. This can over-provision
  memory for modules that need little, but the cost difference at 4–6
  replicas is small compared with the database.
