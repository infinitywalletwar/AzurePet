# ADR-0012: Secrets and configuration: managed identity first, Key Vault for the rest

- **Status:** Accepted (2026-10-01)
- **Date:** 2026-10-01 (amended 2026-10-01 after the architecture review: identity inventory per environment, `migrator` identity on the migration job, secrets per environment before and after the domain gate, permanent base layer; amended again after the compliance re-review: `teardown` identity, `maildrop` read grant for E2E in dev/test, Container Apps log destination)
- **Deciders:** the user (approver), solution-architect
- **Requirements:** NFR-023, NFR-024, NFR-022, NFR-070, NFR-071, NFR-075, NFR-062, NFR-003, FR-020, FR-031, FR-102, C-06, C-07, A-05

**Why a separate ADR:** PLAN §7 lists secrets and configuration as an
ADR topic, and NFR-023 needs a clear, reviewable rule set that
developers and the azure-infra-reviewer can check against.

## Context

NFR-023 requires no secrets in source, pipelines or images, and
service-to-service access without stored credentials where the platform
allows. NFR-024 requires least privilege for people and workloads and no
standing human write access to prod data. The app talks to Azure SQL,
Blob Storage, Key Vault and ACR, which all support Entra
authentication. It also talks to third parties that need credentials:
Google OAuth, the Microsoft identity platform and Brevo (ADR-0007).
ASP.NET Core **Data Protection** keys protect auth cookies, antiforgery
tokens, login-request tokens and handoff payloads (ADR-0003). These keys
must be shared across replicas and survive restarts.

Since the review: the repository is public (ADR-0009), migrations run as
a Container Apps Job (ADR-0009), test and prod are created on demand
(ADR-0014), data-plane endpoints are public with Entra-only access
(ADR-0015), and no domain exists yet, so social login and real email
are not configured in Azure (ADR-0016). Dynamic feature flags are not
required.

## Decision

### 1. Identity inventory (single source for all ADRs)

Three kinds of identities per environment plus one shared `teardown`
identity, all **user-assigned managed identities** (they survive
teardown of the workload layer and keep role assignments stable,
ADR-0014). No service principals with secrets exist.

| Identity | Lives in | Attached to / used by | Roles and grants | Dev | Test | Prod |
|---|---|---|---|---|---|---|
| `id-inpolsure-app-{env}` | `rg-inpolsure-{env}` (base layer) | Web Container App (all roles: Web and Worker) | **SQL:** contained database user with `db_datareader`, `db_datawriter`, `GRANT EXECUTE` on `audit.usp_PurgeExpired`, `DENY UPDATE, DELETE` on `audit.AuditEvents` (ADR-0005). **Storage:** *Storage Blob Data Contributor* on the environment's account (includes user-delegation SAS). **Key Vault:** *Key Vault Secrets User* and *Key Vault Crypto Service Encryption User* (get, wrap, unwrap for Data Protection) on the environment's vault. **ACR:** *AcrPull* on the shared registry | yes | yes | yes |
| `id-inpolsure-migrator-{env}` | `rg-inpolsure-{env}` (base layer) | Container Apps Job `migrate` only (commands `migrate`, `seed`, `bootstrap-superadmin`) | **SQL:** the logical server's **Microsoft Entra admin** (DDL, creates the `app` user and its grants idempotently). **ACR:** *AcrPull*. No Storage or Key Vault roles | yes | yes | yes (`seed` is not allowed in prod; the job refuses it) |
| `id-inpolsure-deploy-{env}` | `rg-inpolsure-shared` | GitHub Actions jobs targeting GitHub environment `{env}` (federated credential subject `repo:<owner>/<repo>:environment:{env}`, ADR-0009) | **Azure control plane:** *Contributor* on `rg-inpolsure-{env}`; *Role Based Access Control Administrator* on `rg-inpolsure-{env}` **with a condition** that allows assigning only *Storage Blob Data Contributor*, *Key Vault Secrets User* and *Key Vault Crypto Service Encryption User* to the `app` identity, and (dev and test only) *Storage Blob Data Reader* to the `deploy-{env}` identity itself and the owner's account. `deploy-dev` also has *AcrPush* on the registry. **No SQL rights.** **Data plane (dev and test only):** *Storage Blob Data Reader* on the **`maildrop` container** of the environment's storage account, assigned by `workload.bicep`, so E2E tests can verify delivered email (FR-020, NFR-003; ADR-0007, ADR-0009). No other data-plane roles; none in prod | yes (+AcrPush, +maildrop read) | yes (+maildrop read) | yes |
| `id-inpolsure-teardown` (one, not per env) | `rg-inpolsure-shared` | GitHub Actions `teardown.yml` jobs targeting GitHub environment **`teardown`** (federated credential subject `repo:<owner>/<repo>:environment:teardown`; the environment has **no required reviewer** and allows deployments only from `main`, so the 6-hourly schedule runs unattended, ADR-0009, ADR-0014) | **Custom role `InPolsure Workload Teardown`** assigned on `rg-inpolsure-test` and `rg-inpolsure-prod` only. Actions: `*/read`, and `delete` on the workload resource types only (`Microsoft.App/containerApps`, `Microsoft.App/jobs`, `Microsoft.App/managedEnvironments`, `Microsoft.Sql/servers`, `Microsoft.Storage/storageAccounts`, `Microsoft.Insights/metricAlerts`, `Microsoft.Insights/scheduledQueryRules`, `Microsoft.Insights/webtests`). No `write`, no resource-group delete, no delete on identities, Key Vault, workspace, Application Insights or action group, no role-assignment rights. Plus *Monitoring Metrics Publisher* on prod's Application Insights resource (base layer) to write the window run record before deleting prod (ADR-0014 §4). No SQL or other data-plane rights | — | yes | yes |

Rules:

- **AcrPull** for `app-{env}` and `migrator-{env}` is assigned once in
  the base layer (cross-resource-group assignment on the shared
  registry), so the on-demand workload deployment never needs rights on
  the shared resource group.
- **`teardown` identity: custom role instead of *Contributor*.** The
  scheduled teardown must work without the owner's approval, so its
  token must not be able to do what the approval-gated `deploy-prod`
  token does. *Contributor* on the test and prod resource groups would
  be simpler (one built-in role, no maintenance) but would let an
  unattended job create or change prod and delete the base layer. The
  custom role (defined at subscription scope in the shared-layer Bicep,
  assignable only to the two resource groups) can only read and delete
  workload resource types; the base layer and the resource groups are
  protected by RBAC, not only by the script. Trade-offs: (−) the action
  list must be extended when a new workload resource type is added (the
  azure-infra-reviewer checks it against `workload.bicep`); (−) Azure
  RBAC conditions cannot test resource tags on control-plane deletes, so
  the **`expiresAt` check is enforced by the workflow**, not by RBAC. A
  compromised `main` workflow could therefore delete a live test or prod
  workload early; that is accepted because data is synthetic and the
  workload is recreated from Bicep (A-07, ADR-0014). Dev is out of its
  reach (no assignment on `rg-inpolsure-dev`).
- **`maildrop` read grant (dev and test only).** Azure dev and test send
  no real email; the rendered messages land in `maildrop` (ADR-0007).
  The release pipeline's E2E and fault-injection tests run with the
  `deploy-test` token and must read them (FR-020, NFR-003); smoke tests
  in dev use `deploy-dev`. The role is scoped to the container, not the
  account, so attachments, exports and the Data Protection key ring stay
  unreadable. The RBAC-administrator condition limits role and
  principal, not scope; the container scope is enforced in
  `workload.bicep` and checked by the azure-infra-reviewer. The prod
  deploy identity's condition does not include this role.
- **Humans:** the owner holds *Owner* on the subscription (needed for the
  bootstrap in ADR-0014) but has **no data-plane role assignments** in
  prod, only *Storage Blob Data Reader* on `maildrop` in dev and test
  (same grant as above, to inspect captured emails), and is not the SQL
  Entra admin. Reading or changing prod
  data requires a deliberate, temporary role assignment (break-glass),
  which the Activity Log records; it is removed afterwards (NFR-024).
  In dev the owner may add their own account as a temporary SQL user for
  debugging.
- The `app` identity cannot run DDL, cannot change or delete audit rows,
  and cannot read Key Vault keys' private material.

### 2. Key Vault and secrets

**Key Vault Standard, RBAC mode, soft delete + purge protection**, one
per environment in the **base layer** (it survives teardown of test and
prod, so secrets are not re-entered and purge protection does not block
re-creation). Contents:

| Item | Type | Dev | Test | Prod |
|---|---|---|---|---|
| Data Protection key-wrapping key | Key (RSA 2048) | yes | yes | yes |
| Brevo SMTP key | Secret | — (blob drop, ADR-0007) | — | yes, set by the owner once the domain gate is passed |
| Google OAuth client secret | Secret | — (social login local only, ADR-0016) | — | after the domain gate |
| Microsoft app credential | Certificate preferred over a secret | — | — | after the domain gate |

Local development keeps the local OAuth client credentials in
`dotnet user-secrets` only; they are never in the repository (public
repository, ADR-0009) and never in Azure.

### 3. Delivery of secrets to the app

Container Apps secrets that **reference Key Vault** through the `app`
identity, exposed as environment variables. Rotation needs only a new
Key Vault version and a revision restart. Bicep never outputs secrets
and never calls `listKeys()`.

**Container Apps environment logs (lab note, compliance re-review):** the
`log-analytics` destination of `appLogsConfiguration` needs the
workspace shared key, which would require `listKeys()`. The environment
therefore uses the **`azure-monitor` destination** and a **diagnostic
setting** on the Container Apps environment that sends console and
system logs to the base-layer workspace. No exception to the
`listKeys()` ban is needed.

### 4. Data Protection

Key ring persisted to Blob (`dataprotection/keys.xml` in the
environment's storage account) and wrapped with the Key Vault key. The
application name is fixed, so all replicas share keys. The storage
account is in the workload layer, so tearing down test or prod discards
the key ring; users of the next live window simply sign in again (data
is synthetic, A-07).

### 5. Configuration

`appsettings.json` (defaults), `appsettings.{Env}.json` (non-secret
per-environment values), and environment variables set by Bicep
(endpoints, resource names, hosting mode and platform domain
(ADR-0016), feature switches). Strongly typed options with validation at
startup (`ValidateOnStart`), including the ADR-0016 rule that refuses to
start prod in pre-domain mode. The Application Insights connection
string is configuration, not a secret.

### 6. Local development

`dotnet user-secrets` and local containers (SQL Server, Azurite,
Mailpit, Aspire dashboard). There are no cloud secrets on laptops;
`DefaultAzureCredential` falls back to the developer's own login only
when they target dev resources on purpose (NFR-075).

### 7. CI guardrails

GitHub secret scanning with push protection blocks commits with secrets
(ADR-0009). The azure-infra-reviewer checks Bicep for `listKeys()`,
connection strings with passwords, shared-key access, and secret
outputs.

## Alternatives considered

| Option | Pros | Cons | Verdict |
|---|---|---|---|
| **User-assigned identities + Key Vault references (chosen)** | No credentials for Azure services; identities and role assignments survive workload teardown; few secrets, centrally rotated; audited access | Key Vault per env (cents/month); RBAC setup in Bicep | **Chosen** |
| System-assigned identities on the Container App and Job | No separate resource | Recreated with every on-demand workload deployment: new principal IDs, new SQL users and role assignments each time; the `app` SQL user would be orphaned | Rejected (ADR-0014) |
| One identity for app and migrations | Fewer objects | The running app would hold DDL rights and could alter audit permissions; fails NFR-024 | Rejected |
| Connection strings with SQL passwords / storage keys in Container Apps secrets | Simple | Long-lived credentials; rotation burden; fails NFR-023 | Rejected |
| Azure App Configuration (with Key Vault references) | Central config, feature flags, dynamic refresh | Another resource; no requirement needs dynamic config or flags yet | Not now. **Trigger:** feature flags or runtime config changes across replicas become a requirement |
| GitHub secrets injected at deploy | Familiar | Secrets copied into app config; rotation needs redeploy; a public repository makes any mistake costlier | Rejected (GitHub holds no secrets at all thanks to OIDC) |
| Data Protection keys in the database | One store | Keys next to the data they protect | Not chosen |
| No shared Data Protection keys | Nothing to configure | Users signed out and antiforgery failures on scale-out or restart | Rejected |

## Consequences

**Trade-offs**

- (+) Nearly zero secrets. Most access is identity-based, auditable and
  revocable, and the same table is used by developers and reviewers.
- (+) Rotation is simple for the few remaining third-party secrets, and
  dev/test hold none until the domain gate.
- (−) The `migrator` identity is powerful (SQL admin). It is attached
  only to the job, and the job is started only by the pipeline.
- (−) The conditional RBAC-administrator assignment is more complex to
  write than plain *Owner*; it is what keeps a compromised deploy token
  from granting itself arbitrary roles.
- (−) The unattended `teardown` identity can delete live test or prod
  workloads (not create, change or delete the base layer); the
  `expiresAt` check lives in the workflow. Accepted while data is
  synthetic.
- (−) Local development cannot use managed identity. Local containers
  replace Azure services, so credentials differ but logic does not.
- (−) Key Vault availability matters at startup (secret resolution).
  Replicas cache resolved values for the process lifetime.

**Scalability**

- No impact. Key Vault is read at startup and on rotation, not per
  request.

**Operations**

- Rotation runbook per secret, with an expiry check (hardening lab).
- The identity table above is the checklist for the azure-infra-reviewer
  on every Bicep change.

**Cost**

- Key Vault Standard: per-operation pricing, well under 1 USD/month per
  environment at run load; an idle vault in the base layer costs
  nothing noticeable.
- Managed identities and role assignments: free.
- Design load: unchanged (Key Vault is not on the request path).
