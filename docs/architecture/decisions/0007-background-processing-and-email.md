# ADR-0007: Background processing with a SQL outbox and in-process worker; email via Brevo

- **Status:** Accepted (2026-10-01)
- **Date:** 2026-10-01 (amended 2026-10-01 after the architecture review: Brevo decided, email per environment before a domain exists, restartable jobs and prod min replicas, sender domain deferred; amended again after the compliance re-review: Q-B4 branding decision, delivery verification from `maildrop`)
- **Deciders:** the user (approver), solution-architect
- **Requirements:** FR-070–FR-077, FR-001, FR-020, FR-082, FR-102, FR-112, NFR-003, NFR-017, NFR-040, NFR-043, NFR-050, NFR-052, NFR-062, NFR-075, C-06, A-05, A-08

## Context

User actions (registration, ticket creation, replies, status changes,
assignment) trigger emails (FR-070–FR-072). Emails are sent
**asynchronously**, retried, and **not duplicated** per event and
recipient (FR-075). No accepted action may be lost when the email
provider is down (NFR-003); p95 send time ≤ 2 min (NFR-017). Design
peak: about 300 emails/min, 25M/year. Other background work: GDPR
exports (FR-112, ≤ 24 h, NFR-043), usage snapshots (FR-082), cleanup of
staging blobs and the outbox, and the audit retention purge (FR-102).

Hosting facts: dev scales to zero; test and prod exist only on demand,
and prod keeps at least one replica while it exists (ADR-0004,
ADR-0014). No platform domain exists yet (A-05, ADR-0016), so there is
no authenticated sender domain.

**ACS Email is retiring** (no new customers from 23 October 2026,
retirement 30 September 2028,
[Microsoft Learn](https://learn.microsoft.com/azure/communication-services/acs-retirement-and-breaking-changes-guide));
M365 High Volume Email supports internal recipients only.

## Decision

### Background processing

1. **Transactional outbox in Azure SQL.** Modules write integration
   events (`TicketCreated`, `MessageAdded`, `StatusChanged`,
   `TicketAssigned`, `AccountRegistered`, `TenantCreated`,
   `TenantHostnameReady`, …) to
   `platform.OutboxMessages` **in the same transaction** as the state
   change. Columns: `Id` (event ID), `TenantId` (the scope the handler
   runs in, ADR-0002), `Type`, `Payload` (IDs only, no ticket text),
   `TraceContext`, `OccurredAt`, `Attempts`, `NextAttemptAt`,
   `ProcessedAt`, `LeaseUntil`. The table is platform-scoped (ADR-0002).
2. **Dispatcher as an `IHostedService` in the web host** (role `Worker`,
   on by default, ADR-0001):
   - Woken in-process when a request commits outbox rows; otherwise
     polls every 30 s **only while the host runs**, so dev can scale to
     zero and the database can pause.
   - Claims batches with `UPDATE TOP (n) … WITH (UPDLOCK, READPAST) …
     OUTPUT` and a 5-minute lease, so several replicas can run it.
   - Handles each message in its **tenant scope** (`ITenantScopeFactory`)
     under the originating trace context (NFR-050).
   - Retries with exponential backoff and jitter (30 s, 2 min, 10 min,
     1 h, then every 6 h for up to 3 days); then **failed** and alerted
     (NFR-052).
3. **Idempotency:** deliveries recorded in
   `notifications.NotificationDeliveries` with unique key
   `(EventId, RecipientId, Template)`; existing row → skip (FR-075). A
   crash between provider acceptance and the delivery row can still
   produce one duplicate (accepted, documented). The event ID is also
   sent as a custom header.
4. **Restartable work and scale-to-zero (NFR-017, NFR-043).** The
   simplest rule that works:
   - **Every handler is idempotent and restartable.** A message whose
     lease expires (replica stopped, deploy, crash) is claimed again.
     Long jobs (GDPR export) record progress per step in a job table and
     resume from the last completed step.
   - **Graceful shutdown:** on `SIGTERM` the dispatcher stops claiming,
     finishes or abandons in-flight messages, and clears their leases
     within the 60 s grace period (ADR-0004).
   - **Prod keeps min 1 replica whenever it exists** (ADR-0004), so the
     NFR-017 and NFR-043 targets do not depend on traffic. In dev and
     test (no targets), pending work resumes on the next wake-up.
   - No KEDA SQL scaler and no separate worker app at MVP.
5. **Scheduled maintenance** in a hosted service, one replica at a time
   via `sp_getapplock`: outbox cleanup, staging-blob orphan cleanup,
   usage snapshots per tenant (FR-082), audit purge per tenant via
   `audit.usp_PurgeExpired` (ADR-0005).

### Email

6. **Provider-agnostic port** `IEmailSender` in Notifications.
   Templates are rendered in-process with tenant branding (FR-073) and
   contain only ticket number, subject and link (FR-074). Two adapters:
   - **`SmtpEmailSender`** (MailKit): talks to Mailpit locally and to
     Brevo's SMTP relay in prod. The code path is identical; only host
     and credential differ.
   - **`BlobDropEmailSender`**: writes the rendered `.eml` to the
     `maildrop` container (7-day lifecycle, ADR-0006). Lets Azure
     dev/test prove the whole outbox → render → "send" path without a
     provider account or a domain.
7. **Where email goes, per environment:**

   | Environment | Adapter | Real delivery? |
   |---|---|---|
   | Local, CI | `SmtpEmailSender` → Mailpit container | No (captured) |
   | Azure dev, Azure test | `BlobDropEmailSender` | No |
   | Prod (requires a domain, ADR-0016) | `SmtpEmailSender` → **Brevo** | Yes |

   This keeps the free quota for prod, never sends test mail to real
   inboxes, and needs no provider before the domain gate. **Delivery
   is verified from `maildrop`** in Azure dev and test: the `.eml`
   carries the event ID header (item 3), and the pipeline's smoke, E2E
   and fault-injection tests read the container with the
   `deploy-dev`/`deploy-test` identity (*Storage Blob Data Reader* on
   `maildrop` only, ADR-0012 §1; test procedure in ADR-0009 item 6).
   Locally and in CI, tests use Mailpit's API instead.
8. **Provider: Brevo** (decided by the user, 2026-10-01). French company,
   EU hosting, free plan of 300 emails/day, SMTP relay and API.
   **Mailjet** (EU data centres, free 6,000/month, 200/day) is the
   alternative behind the same port. Brevo is a GDPR sub-processor; a
   DPA is assumed (A-08). **To verify before the first prod send:**
   (a) EU storage of transactional email data and logs, and the DPA
   terms; (b) whether the free plan adds Brevo branding to transactional
   emails (third-party reviews report branding on the free tier).
   **User decision Q-B4 (2026-10-01): Brevo branding on free-plan emails
   is acceptable during the learning phase** (synthetic data, no real
   tenant). It must be revisited **before a real tenant uses prod**;
   then white-label emails (FR-073) require the cheapest paid tier or
   Mailjet; (c) sender-authentication requirements.
9. **Sender domain: deferred** until a platform domain exists. Then:
   `notifications@<platform-domain>` with SPF, DKIM and DMARC in Azure
   DNS, managed in Bicep. Sending from a free-mail address without own
   DNS would fail DMARC alignment at major mailbox providers, which is
   why real delivery waits for the domain gate. Per-tenant sender
   domains are Later.

## Alternatives considered

### Processing

| Option | NFR-003 (no lost events) | Cost at run load | Ops | Verdict |
|---|---|---|---|---|
| **SQL outbox + in-process hosted dispatcher (chosen)** | Yes: committed with the state change | 0 USD extra | One codebase, one deploy | **Chosen** |
| Fire-and-forget email in the request | No | 0 | Simple | Rejected: fails FR-075, NFR-003 |
| Azure Service Bus queue + worker | Only with an outbox (dual-write otherwise) | Cheap per operation, but another resource per env | Dead-letter handling | Not needed. **Trigger:** several independent consumers, worker extracted, or polling becomes measurable DB load |
| Azure Storage Queues + worker | Same dual-write issue | ~0 | Another moving part | Rejected |
| Separate worker Container App, or KEDA SQL scaler on dev | Same guarantees | +5–10 USD with min 1, or scaler setup | Two apps per env | Deferred. **Trigger:** worker load hurts web p95 (ARCHITECTURE §3.2) |
| Azure Functions | Needs an outbox or queue | ~0 | Separate deployable | Rejected for MVP |

### Email provider

| Option | EU residency | Free tier / run cost | Status | Verdict |
|---|---|---|---|---|
| Azure Communication Services Email | Europe selectable | 0.00025 USD/email + 0.00012 USD/MB ([Learn](https://learn.microsoft.com/azure/communication-services/concepts/service-limits)) | **Retiring; no new customers from 23 Oct 2026** | Rejected |
| M365 High Volume Email | EU | M365 licensing | Internal recipients only | Rejected |
| **Brevo (decided)** | EU company, EU hosting (verify item 8a) | Free 300/day; paid volume tiers | Active | **Chosen** |
| Mailjet | EU data centres | Free 6,000/month (200/day) | Active | Alternative |
| Infobip / Telesign | EU options | Contract pricing | Active | Rejected for MVP |
| SendGrid, Mailgun, Postmark, Amazon SES | Varies | Varies | Active | Valid, not preferred (EU residency/free tier less straightforward) |
| Self-hosted SMTP | Yes | Compute | We own deliverability | Rejected |

## Consequences

**Trade-offs**

- (+) Exactly-once intent with at-least-once delivery and deduplication;
  email outages never fail user actions (FR-075, NFR-003).
- (+) No extra Azure service; the outbox is a core learning goal.
- (+) Provider changes are adapter/configuration changes.
- (+) No real email leaves dev/test; no provider or domain needed until
  prod.
- (−) The Brevo path is first used in prod. Mitigation: same SMTP adapter
  as Mailpit; a prod smoke test sends one email to the owner.
- (−) A third-party sub-processor outside Azure with an SMTP credential
  in Key Vault (ADR-0012).
- (−) The in-process worker shares CPU with the web host (split trigger
  above).

**Scalability**

- The outbox handles 300 emails/min (5/s) with batch claiming; SQL load
  is a few small indexed updates per second.
- The provider quota is the real limit: the free tier covers learning
  load only; design load (~2.1M/month) needs a paid tier and a 2–4 week
  domain warm-up.

**Operations**

- Alerts: failed outbox messages > 0, oldest pending > 10 min (custom
  metric), provider bounce rate (Later, webhooks).
- Runbook: requeue failed messages after fixing the cause.
- DNS records for sender authentication in Bicep once the domain exists.

**Cost (approximate)**

- Run load: 0 USD (free tier; in-process worker; blob drop costs cents).
- Design load: ~2.1M emails/month on a paid tier, about 200–600 USD/month
  depending on provider and contract; a separate worker app adds about
  10–60 USD/month.
