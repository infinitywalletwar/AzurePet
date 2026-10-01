# Project Instructions

## Project

A production-oriented educational project: designing and implementing a
scalable application on Microsoft Azure using .NET / ASP.NET Core and a
Blazor UI. The business domain is defined in `docs/REQUIREMENTS.md`.

The project is built incrementally as a sequence of architecture phases
and implementation labs.

## Current phase

**Incremental implementation — Lab 01**
(`docs/labs/lab-01-walking-skeleton.md`). `docs/ROADMAP.md`,
`docs/DELIVERY.md` and the Lab 01 spec approved (2026-10-01). Next:
wave 0 (T-01.1) by a single `dotnet-azure-developer`, per `docs/DELIVERY.md`.

Create code, infrastructure, and solution files only within an approved
lab task. Never create Azure resources; those are human steps
(`docs/DELIVERY.md` §2.2).
Update this section when a lab or wave completes.

## Workflow

Follow this order and do not skip phases:

Plan (`docs/PLAN.md`) → Requirements → Architecture → Architecture review
→ Architecture decisions → Lab roadmap → Incremental implementation

Design phases (plan through lab roadmap) are done with
`solution-architect`, which writes only to `docs/`. Lab specs split work
into tasks and parallel waves. The delivery process (branching, CI/CD,
environments, releases, wave workflow) is also designed by
`solution-architect`, in `docs/DELIVERY.md`.

During implementation:

1. `dotnet-azure-developer` implements an approved lab task.
2. Review before merging:
   - `architecture-compliance-reviewer` — phase, lab scope, ADRs
   - `dotnet-code-reviewer` — C# code and tests
   - `azure-infra-reviewer` — Bicep, Docker, pipelines (if changed)
3. Fix Blockers, then re-run the reviewers that flagged them.

## Architecture rules

- Prefer the simplest architecture that satisfies the requirements.
- Avoid premature microservices and unnecessary distributed-system
  complexity.
- Do not introduce technologies just because they are popular.
- Every significant infrastructure or architectural choice must explain:
  the problem it solves, why it is needed, alternatives considered,
  trade-offs, and scalability, operational, and (where relevant)
  approximate cost implications.
- Significant architecture changes must not be made silently: create or
  update an ADR that explains the reason for the change.

## Technology direction

- Application: .NET 10+ / ASP.NET Core
- UI: Blazor (render modes decided per ADR)
- Cloud: Microsoft Azure, with DevOps practices (CI/CD)
- Infrastructure as code: Bicep

Do not assume specific Azure services until the matching architecture
decisions have been made.

## Documentation

`docs/` is the source of truth for project design:

- `docs/REQUIREMENTS.md` — system requirements and constraints
- `docs/ARCHITECTURE.md` — current system architecture
- `docs/ROADMAP.md` — project phases and labs
- `docs/DELIVERY.md` — development workflow, CI/CD, environments, releases
- `docs/architecture/decisions/` — Architecture Decision Records
- `docs/labs/` — lab specifications

Keep this file concise. Put detailed domain and architecture information
in `docs/`.
