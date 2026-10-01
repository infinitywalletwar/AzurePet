# Project Instructions

## Project

A production-oriented educational project: designing and implementing a
scalable video streaming and video analytics platform on Microsoft Azure
using .NET.

The project is built incrementally as a sequence of architecture phases
and implementation labs.

## Current phase

**Architecture and requirements.**

Do not create application code, infrastructure, Azure resources, or
solution files unless explicitly requested.
Update this section when the project moves to implementation.

## Workflow

Follow this order and do not skip phases:

Requirements → Architecture → Architecture review → Architecture decisions
→ Lab roadmap → Incremental implementation

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
- Cloud: Microsoft Azure, with DevOps practices (CI/CD)
- Infrastructure as code: Bicep

Do not assume specific Azure services until the matching architecture
decisions have been made.

## Documentation

`docs/` is the source of truth for project design:

- `docs/REQUIREMENTS.md` — system requirements and constraints
- `docs/ARCHITECTURE.md` — current system architecture
- `docs/ROADMAP.md` — project phases and labs
- `docs/architecture/decisions/` — Architecture Decision Records
- `docs/labs/` — lab specifications

Keep this file concise. Put detailed domain and architecture information
in `docs/`.
