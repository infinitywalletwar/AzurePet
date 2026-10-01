# ADR-0006: Attachments: Blob Storage with app-mediated upload and short-lived SAS download

- **Status:** Accepted (2026-10-01)
- **Date:** 2026-10-01 (amended 2026-10-01 after the architecture review: `maildrop` container, on-demand prod, link to ADR-0015)
- **Deciders:** the user (approver), solution-architect
- **Requirements:** FR-060–FR-066, FR-010, FR-100, FR-112, FR-113, FR-115, NFR-004, NFR-006, NFR-022, NFR-023, NFR-028, NFR-030, NFR-040, NFR-041, A-09

## Context

Tickets carry files: PDF, JPEG, PNG, DOCX and XLSX, at most 20 MB per
file and 10 per message, with types checked by content (FR-061). At
design load that is about 5 TB per year, kept 6 years. Files may hold
special-category data such as medical documents in claims (NFR-041).
Downloads must be authorised, unguessable and expire within 15 minutes
(FR-062). Internal-note files must never reach clients (FR-063).
Malware scanning is Later; the risk is accepted while data is synthetic
(A-09). Branding images (FR-010) and GDPR export packages (FR-112) also
need storage.

## Decision

1. **Azure Blob Storage** (StorageV2, hot tier). One storage account per
   environment, in Poland Central. **LRS** in dev and test, **ZRS** in
   prod.
2. **Security settings:** all containers private (anonymous access
   disabled), **shared key authorization disabled**, TLS 1.2 minimum,
   HTTPS only. Access only through Entra RBAC: the app's managed
   identity has *Storage Blob Data Contributor* on the account
   (*Storage Blob Delegator* is included in it).
3. **Containers and paths:**
   - `attachments/tenants/{tenantId}/tickets/{ticketId}/{attachmentId}`.
     The blob name is a GUID, never the user's file name. The original
     name is kept in the database.
   - `staging/tenants/{tenantId}/{uploadId}` for files uploaded before
     their ticket transaction commits. A lifecycle rule deletes staging
     blobs after 1 day.
   - `branding/tenants/{tenantId}/…` for logos and favicons, served
     through a cached app endpoint (ADR-0011).
   - `exports/tenants/{tenantId}/{jobId}.zip`. A lifecycle rule deletes
     them after 7 days.
   - `dataprotection/` for the ASP.NET Core Data Protection key ring
     (ADR-0012).
   - `maildrop/` (Azure dev and test only) for rendered emails written by
     the blob-drop email sender instead of real delivery (ADR-0007). A
     lifecycle rule deletes them after 7 days.
4. **Upload is app-mediated:** the browser posts multipart to the web
   host. The Attachments module checks authorization, count, size, the
   daily per-client volume (NFR-028) and the **file signature** (magic
   bytes) against the allow-list, then streams the file to Blob Storage
   without buffering it all in memory. The module records `ContentType`
   (sniffed), `SizeBytes`, `InternalOnly` and `Status`.
5. **Download is app-authorised and served by Storage:** the endpoint
   loads the attachment, which the tenant filter and RLS cover. It
   checks the ticket permission and `InternalOnly` against the user's
   role, writes an **audit event** (FR-100), then returns `302` to the
   blob URL with a **user-delegation SAS**: read only, this blob only,
   **valid 5 minutes**, HTTPS only, and
   `Content-Disposition: attachment` and `Content-Type` fixed by SAS
   parameters. The user-delegation key is cached for its validity
   period.
6. **Data protection:** **blob soft delete (30 days)** and **versioning**
   in prod. Lifecycle moves attachment blobs to **cool tier after 30
   days** without access (applied once real volumes exist). Retention
   deletion (FR-115, Later) is done by the application per tenant policy
   and ticket closure date, not by blanket lifecycle rules.
7. **Erasure (FR-113):** the Privacy module deletes the blobs, including
   versions. Soft-deleted copies expire within 30 days. This is stated
   in the privacy notice.
8. **Malware scanning (FR-064, Later):** the attachment model has a
   `Status` (`Available` now; later `PendingScan`, `Clean`,
   `Quarantined`). The planned implementation is **Microsoft Defender
   for Storage on-upload malware scanning**, which scans in the same
   region as the account. Results arrive through Event Grid and a worker
   handler. Before enabling it, confirm Poland Central support, since
   Microsoft says not all regions support malware scanning
   ([Learn](https://learn.microsoft.com/azure/defender-for-cloud/introduction-malware-scanning)).
   Fallback: a ClamAV container run as a Container Apps job.
   **Trigger:** before any real data (A-09).

## Alternatives considered

| Option | Pros | Cons | Verdict |
|---|---|---|---|
| **Blob Storage, app-mediated upload, SAS-redirect download (chosen)** | Server-side validation before the file is stored; downloads offloaded to Storage; per-blob 5-minute links; no account keys | Upload bandwidth and CPU pass through the web replicas (20 MB max; ~160 KB/s average at design load); SAS is a bearer link for 5 minutes | **Chosen** |
| Direct browser upload with user-delegation SAS (write) | Offloads upload bandwidth | Validation (type, size, quota) only after upload, which needs a quarantine flow; CORS on Storage; more moving parts | Rejected for MVP. **Trigger:** upload traffic measurably affects web latency at design load |
| Stream downloads through the app (no SAS) | Storage endpoint never exposed to browsers; simplest CSP | All download bytes through web replicas; longer requests | Not chosen. Acceptable fallback if SAS issuance is a problem |
| Files in Azure SQL (`varbinary(max)` / FILESTREAM-like) | Single store, transactional | 5 TB/year in the database: large backups, cost, slower restores; exceeds free-offer 32 GB immediately | Rejected |
| Azure Files | SMB/NFS semantics | Not needed; more expensive per GB than blob hot/cool; no user-delegation SAS per file in the same way | Rejected |
| Public container with unguessable names | Simple | Links never expire; fails FR-062 | Rejected |
| Account-key SAS | Simple to sign | Needs the account key (secret); fails NFR-023 | Rejected |

## Consequences

**Trade-offs**

- (+) Upload validation happens before data is accepted, which fits an
  open-registration abuse model (NFR-028).
- (+) No storage secrets: managed identity plus user-delegation SAS.
- (+) Per-tenant prefixes make tenant export and offboarding a prefix
  operation.
- (−) Prod storage is deleted with prod (ADR-0014); soft delete and
  versioning protect data only while prod exists. Accepted because data
  is synthetic (A-07).
- (−) The Storage public endpoint must be reachable from browsers for
  SAS downloads (ADR-0015). With private endpoints later,
  downloads must switch to streaming through the app, or the account
  keeps public access restricted to SAS requests. This decision is
  revisited in the hardening lab.
- (−) A SAS link copied within its 5-minute validity works for anyone
  who has it. Mitigations: short expiry, audited issuance, HTTPS only.
- (−) Until scanning exists, infected files can be stored and downloaded
  (accepted risk A-09). `Content-Disposition: attachment` and a fixed
  content type reduce browser-side risk.

**Scalability**

- Blob Storage scales far beyond 5 TB/year. Per-account limits are not a
  concern at design load.
- Web replicas handle uploads. If upload volume becomes a bottleneck,
  switch to direct-to-blob upload with post-upload validation (trigger
  above).

**Operations**

- Lifecycle rules, soft delete and versioning are defined in Bicep.
- Storage metrics and a daily-capped diagnostic log go to Log Analytics
  (ADR-0010).
- An orphan-cleanup job reconciles database rows and blobs weekly.

**Cost (approximate)**

- Run load: a few GB at about 0.02 USD/GB-month (hot LRS; ZRS about 25%
  more), so under 1 USD per environment. Operations cost cents.
- Design load: about 5 TB in year 1, roughly 50–150 USD/month with cool
  tiering after 30 days, growing each year until retention deletion
  starts (FR-115).
- Malware scanning (Later): Defender for Storage per-account and per-GB
  scanned charges (check the current pricing page when enabling); at
  design volume this is a notable line item and must be in the budget
  review then.
