# KANDOO — AUTHORITATIVE DECISIONS REGISTER

**Repository:** `majid-ghanbari-dost/Kandoo` (Parent Platform Repository)  
**Baseline:** Board v4 + Amendment v4.1 + Amendment v4.2 (`R-0`)  
**Last updated:** `2026-10-23` (`P-0104r`)

> **Governance Rule:** Frozen decisions are not reopened without direct contradictory evidence or explicit Product Owner revision. Where Amendments `v4.1` or `v4.2` modify an earlier `Board v4` clause, the latest amendment governs. Whenever a detail is unspecified and the PM is unavailable, take the conservative fail-closed option (`REVIEW` / feature gate) and record it under [Assumptions](#assumptions).

---

## Core Architecture Decisions (`D1`–`D14`)

### `D1` — Two Operating Modes (`A` and `B`)
- **Owner:** Product Owner
- **Reason:** Target stores split into two distinct markets: shops ready to run Kandoo natively as their daily POS/inventory system and shops committed to existing legacy POS software that still want Kandoo digital invoices.
- **Consequence:** Platform supports two explicit modes — **Mode `A`** (*Kandoo is the store software*: native sales, catalog, and ledger inventory) and **Mode `B`** (*Kandoo runs beside other store software*: print-spooler capture → Python extraction → Canonicalization Gate → Kandoo issuance). Mode `B` captures never silently fabricate native `Sale`, `Inventory`, or `KPI` domain events.
- **Status:** **ACTIVE — FROZEN** (Board v4)
- **Test Criterion:** Automated integration tests execute both Mode `A` (`mode == "A"`, native provenance) and Mode `B` (`mode == "B"`, external capture provenance through Canonicalization Gate), verifying that Mode `B` invoice ingestion creates zero implicit native `Sale` or `Inventory` ledger mutations without explicit operator confirmation.

---

### `D2` — Language-Independent Canonical Contract & Technology Split
- **Owner:** Product Owner / Technical Architect
- **Reason:** Local image processing, projection layout analysis, and glyph classification fit Python cleanly, whereas multi-device store hubs, Windows/Android clients, and high-reliability APIs fit C# (`ASP.NET Core` / `.NET`). Decoupling via a strict JSON contract prevents cross-language coupling.
- **Consequence:** Extraction remains in Python (`majid-ghanbari-dost/Kandoo-Digital-Invoice`); platform services and client applications are built in C# (`ASP.NET Core` / `.NET`) in `majid-ghanbari-dost/Kandoo`. Cross-repo integration uses **only** the Canonical Invoice v0 JSON contract (`schema_version: "0"`) and the local `image_extraction_adapter.cli` process interface (`Process.Start`). Python code is never copied into `Kandoo`.
- **Status:** **ACTIVE — FROZEN** (Board v4; extended structurally by `E-01b` in v4.2)
- **Test Criterion:** C# and Python test suites validate identical positive/negative Golden Vector fixtures against `schemas/canonical_invoice_v0.json` and produce identical 64-hex `issued_content_sha256` digests; CI boundary check confirms zero Python extraction modules exist in `Kandoo`.

---

### `D3` — No Holoo Database Access (Print Path Is the Product)
- **Owner:** Product Owner
- **Reason:** Direct Holoo database access has no vendor cooperation and Holoo's web service is prohibitively expensive for retail stores. Furthermore, printed POS invoices do not reliably carry real customer phone numbers or verified customer names.
  > **Verbatim Owner Record (`2026-10-17`):**  
  > «هلو همکاری و دسترسی به داده ندارد و وب‌سرویسش برای فروشگاه‌ها پرهزینه است؛ مسیر چاپ محصول است. تلفن و نام واقعی مشتری از چاپ نمی‌آید و باید از اپراتور گرفته شود.»
- **Consequence:** Production Holoo database connectors are permanently out of scope. Mode `B` captures print output via the spooler and collects customer phone/name directly from the operator in the Kandoo UI.
- **Status:** **CLOSED — FINAL** (`2026-10-17`, Board v4)
- **Test Criterion:** Static dependency and runtime boundary checks confirm zero Holoo SQL/ODBC/web-service client dependencies in `Kandoo`, and Mode `B` invoice creation requires explicit operator-supplied customer phone context rather than OCR phone inference.

---

### `D4` — Customer Invoice Delivery, Token Security & Phone Verification
- **Owner:** Product Owner
- **Reason:** Customers must be able to view an individual invoice effortlessly immediately after purchase without creating an account, while viewing full cross-purchase history requires proof of phone ownership. Delivery should remain resilient across channels while avoiding premature lock-in to an unchosen SMS gateway.
- **Consequence:**
  - Customer delivery channels: **SMS + QR code + OS/Web share button**.
  - Public invoice URL carries a cryptographically random unguessable `public_token` with **`>= 128` bits of entropy** (`>= 22` unpadded base64url characters).
  - Opening a single invoice via `public_token` requires **no login/account**.
  - Viewing all invoices associated with a phone number requires **registration + SMS OTP verification**.
  - Phone state explicitly tracks `customer.phone_status = "claimed" | "verified"`.
  - *Amended in v4.1:* **SMS provider is NOT chosen yet**; use an `ISmsSender` **stub provider** through `P-10`, with a real provider selected by the Owner before `P-08` real delivery goes live.
- **Status:** **AMENDED (v4.1) — OPEN ON PROVIDER SELECTION**
- **Test Criterion:** Automated tests verify: (1) generated `public_token` values use CSPRNG `>= 16` raw bytes (`>= 22` base64url chars), (2) `GET` by valid `public_token` renders only that single invoice anonymously while invalid/enumerated tokens return `404`, (3) listing invoices by phone fails with `401/403` for `claimed` phones until OTP completes (`verified`), and (4) `StubSmsSender` captures outbound messages deterministically in test/dev runs without external network calls.

---

### `D5` — Environment, Hosting & `< 1 Hour` Portability
- **Owner:** Product Owner
- **Reason:** Free cloud tiers introduce container sleep, expiring databases, customer data exposure risks, and Iran sanctions/sign-up failures; `ASP.NET Core` + `PostgreSQL` requires standard Linux Docker hosting that can be relocated quickly if network or vendor conditions change.
- **Consequence:**
  - *Board v4:* Any host must support full migration in **under 1 hour**.
  - *Amended in v4.1:* **Vercel is NOT recommended**; free tiers must **never** hold customer data.
  - *Amended in v4.2 (governing):* All development and integration testing runs on **local Docker Compose** using **synthetic data only**. **No free cloud service** is used. A small domestic or non-US **VPS running Docker** is provisioned **only when a public address is required**. The `< 1 hour` Docker portability kit (`P-10`) remains mandatory.
- **Status:** **AMENDED (v4.2) — OPEN ON FINAL VPS HOST**
- **Test Criterion:** Fresh-machine end-to-end check runs `docker compose up` locally with synthetic fixtures, executes backup export + clean teardown + restore into a fresh Docker Compose stack with full health/data verification completing in `< 60 minutes` and zero external cloud calls.

---

### `D6` — Single Application Architecture (Windows & Android) & Multi-Store Memberships
- **Owner:** Product Owner
- **Reason:** Store staff operate across Windows counter PCs and Android handhelds, and business owners, accountants, or managers frequently work across multiple stores or businesses under a single identity.
- **Consequence:** Client experience is built as **one Kandoo application architecture** targeting **Windows and Android**, backed by **one global user account** capable of holding **multiple `Business` / `Store` role memberships** (`Business → Store → Users / Devices`).
- **Status:** **ACTIVE — FROZEN** (Board v4)
- **Test Criterion:** Domain and API tests authenticate one user account holding distinct role memberships across Store `S1` and Store `S2`, verifying strict tenant isolation (requests scoped to `S1` never leak `S2` records) and shared C# view-model/client contract compatibility across Windows and Android targets.

---

### `D7` — Single Store Hub on LAN & Offline-First Operation
- **Owner:** Product Owner / Technical Architect
- **Reason:** Retail stores experience frequent internet disruptions, yet multiple counter devices inside one store need a single local authority for numbering, locks, and stock consistency without peer-to-peer split-brain complexity.
- **Consequence:** Each store runs **exactly one Store Hub**. Companion devices connect to the Hub **over LAN only** (**no Bluetooth**). If the Store Hub is powered off or unreachable on LAN, companion devices **cannot start new store operations**. The Hub itself continues operating fully **without internet** (`SQLite` + local Outbox) and replicates asynchronously to the Cloud API when connectivity returns.
- **Status:** **ACTIVE — FROZEN** (Board v4)
- **Test Criterion:** Integration tests verify: (1) Hub completes full invoice creation/issuance and Outbox queuing with WAN internet severed, (2) companion client receives an explicit hard block when Hub LAN endpoint is down, and (3) queued Outbox operations sync cleanly without duplication when WAN connectivity resumes.

---

### `D8` — Backup, Cloud Replication & Local Restore Model
- **Owner:** Product Owner
- **Reason:** Protecting store records requires both continuous cloud replication and local backup capability, while honestly documenting single-machine hardware limits prior to cloud sync.
- **Consequence:** Store state is protected by **full cloud copy replication + local backup snapshots**. Unsynced transactions residing only on a failed **Hub disk remain a documented single point of failure (SPOF)**. Per `v4.1`, restoring a store onto a reinstalled device must also satisfy **Restore Second Factor** and `SEC-1`.
- **Status:** **ACTIVE** (Board v4 + `v4.1` Restore Second Factor rule)
- **Test Criterion:** Automated restore test creates store records, runs cloud sync + encrypted local snapshot, wipes local Hub storage, rejects restore when given SMS OTP alone, and restores exact state when given OTP + valid recovery key (or paired-device approval).

---

### `D9` — Store Product Identity & Near-Duplicate Name Warning
- **Owner:** Product Owner
- **Reason:** Uncontrolled manual or captured item entry creates duplicate catalog entries with minor spelling, spacing, or Persian/Arabic character variations (`ی`/`ي`, `ک`/`ك`, half-spaces).
- **Consequence:** Every catalog item has an immutable **store-unique `product_id`** and a canonical **normalized name**. Creating or linking a product whose normalized name closely matches an existing active store product raises an **operator warning** rather than silently creating duplicates or silently merging distinct SKUs.
- **Status:** **ACTIVE — FROZEN** (Board v4)
- **Test Criterion:** Unit and service tests verify deterministic Persian/Latin normalization (`ی`/`ک`/ZWNJ/whitespace folding), enforce unique `product_id` per store, and assert that submitting a near-duplicate name returns a structured warning without silent merge or overwrite.

---

### `D10` — Concurrency Control, Ledger Inventory, Operation Idempotency & Invoice Immutability
- **Owner:** Product Owner / Technical Architect
- **Reason:** Concurrent devices, retries, and offline synchronization corrupt stock counts and prices if mutable counters or silent last-write-wins updates are used, and issued financial documents cannot mutate retroactively.
- **Consequence:**
  - **Inventory** is stored as an **append-only event ledger** (negative stock allowed per domain rules; stock shortage alone does not block a sale).
  - **Price and product specification edits** require OCC `base_version`; version mismatches surface explicit **operator conflicts** and **never overwrite silently**.
  - **Issued invoices (`ISSUED`) are immutable** — corrections happen only via explicit terminal transitions (`REVOKED` or `SUPERSEDED` + replacement invoice).
  - **Every mutating operation** carries a unique client-generated `operation_id` (idempotency key) and `device_id`.
- **Status:** **ACTIVE — FROZEN** (Board v4)
- **Test Criterion:** Concurrent test suite proves: (1) replaying an identical `(store_id, operation_id)` returns the original result with zero duplicate side effects, (2) concurrent price edits sharing the same `base_version` commit the first and fail the second with a structured conflict payload, (3) concurrent inventory adjustments sum accurately via ledger events, and (4) any mutation attempt against an `ISSUED` invoice fails closed while preserving `issued_content_sha256`.

---

### `D11` — Per-Phone `DRAFT` Invoice Lock
- **Owner:** Product Owner
- **Reason:** Two operators/devices starting drafts simultaneously for the same customer phone number can double-bill, mix items, or confuse customer delivery, while legitimate repeat purchases one after another must work smoothly.
- **Consequence:**
  - *Board v4 + Clarified in v4.1:* Acquiring a `DRAFT` invoice in progress locks `(store_id, customer_phone)` to that single `device_id`. A second `device_id` attempting to open a concurrent `DRAFT` for the same phone receives an immediate **hard error**.
  - Resending the **same `operation_id`** from the owning device follows standard idempotency (`D10`) rather than failing the lock.
  - Lock releases immediately upon **issue**, **cancel**, **client disconnect**, or **inactivity timeout**.
  - *Amended in v4.2 and confirmed by Owner in `P-0104r` (`2026-10-23`):* Default abandoned-draft lock timeout is **5 minutes / 300 seconds (configurable)**. Only a newly Hub-accepted draft item, `quantity_millis`, or customer change restarts the inactivity window; an open screen, passive heartbeat, rejected operation, or no-effect replay does not.
  - **Sequential purchases** with the same phone number immediately after issue/cancel remain fully allowed.
- **Status:** **AMENDED (v4.2 + P-0104r) — ACTIVE / OWNER CONFIRMED**
- **Test Criterion:** Concurrency tests verify: (1) Device `D1` opens `DRAFT` for phone `P`, and Device `D2` receives hard conflict error for `P`; (2) `D1` retrying same `operation_id` succeeds idempotently without renewing inactivity state; (3) a Hub-accepted draft change restarts the 300-second window while an open screen/passive heartbeat does not; (4) after `issue`, `cancel`, disconnect signal, or simulated clock advance past `LockTimeout` (`Default = 5m`), `D2` immediately acquires a new `DRAFT` for `P`.

---

### `D12` — Dual Invoice Numbering (`invoice_id` + Hub Sequential `display_number`)
- **Owner:** Product Owner
- **Reason:** Distributed/offline systems need opaque collision-free primary keys, whereas store operators and customers expect clean, sequential store invoice numbers distinct from third-party POS receipt numbers.
- **Consequence:** Every invoice has an opaque internal unique `invoice_id`. Every **successfully issued** invoice also has a **Store Hub-allocated, gapless, store-sequential `display_number`**; every pre-issuance or retired draft has `display_number=null`. The Owner-approved `P-0104r` revision allocates the number only in the successful `ISSUE` transaction, so draft creation, validation, cancellation, timeout, failed issue, and replay consume no number. Issued numbers are never reused, and the issued-only high watermark survives restart/restore. External printed receipt numbers captured in Mode `B` stay strictly inside `header.invoice_number` with `untrusted` provenance.
- **Status:** **ACTIVE — FROZEN WITH OWNER-APPROVED `P-0104r` REVISION** (Board v4; revised `2026-10-23`)
- **Test Criterion:** Multi-device tests create null-numbered drafts without advancing the high watermark, then submit parallel `ISSUE` commands through the Hub and assert strictly unique, gapless, monotonic `display_number` allocation distinct from both `invoice_id` and `header.invoice_number`. Cancelled/timed-out drafts and failed issues consume nothing; exact issue replay adds nothing; the sequence is preserved across Hub restart/restore.

---

### `D13` — Staged Feature Delivery with Multi-Device Architecture from Day One
- **Owner:** Product Owner
- **Reason:** Shipping every module at once delays MVP feedback, but building single-device local shortcuts first forces a costly architectural rewrite when a second counter device is added.
- **Consequence:** Product scope is delivered in incremental stages (`P-01` through `P-17`), **with multi-device Hub/LAN boundaries, `device_id`, `operation_id`, and concurrency controls built in from Day 1 (`P-01`/`P-02`)**.
- **Status:** **ACTIVE — FROZEN** (Board v4)
- **Test Criterion:** Beginning in `P-02`, every Hub service integration test exercises at least two distinct simulated devices (`device-01`, `device-02`) concurrently against one Store Hub instance.

---

### `D14` — Product Terminology (`"Kandoo digital invoice"`) & Lawyer-Owned Legal Copy
- **Owner:** Product Owner (drafted/reviewed by Owner's Lawyer)
- **Reason:** Informal tax/legal claims or calling the artifact a "receipt" mischaracterizes the product and exposes the platform and stores to legal liability.
- **Consequence:**
  - *Replaced in v4.1 & reinforced in v4.2:* Customer- and store-facing terminology is **"Kandoo digital invoice"** (never described as a `"receipt"`).
  - The store onboarding agreement (accepted at installation with a **digital signature**) states exact data custody and encryption practices.
  - **All legal wording is drafted and reviewed by a lawyer.**
  - **Developers write NO legal claims or ad-hoc disclaimers.** Customer-facing public pages (`P-09`) remain gated until Owner/lawyer-approved copy is provided.
- **Status:** **AMENDED (v4.1 / v4.2) — OPEN ON LAWYER WORDING (RELEASE GATE)**
- **Test Criterion:** Release-gate test asserts: (1) zero occurrences of unapproved `"receipt"` legal disclaimer copy in customer views, (2) rendering customer invoice page or store install agreement fails closed unless signed/configured lawyer-approved text resource is present, and (3) every technical claim in the agreement maps to a passing verification test (`Agreement Truthfulness Rule`).

---

## Amendments `v4.1` Record

1. **`D4` (SMS Provider Stub):** SMS gateway is undecided; implement and use a deterministic stub provider until `P-10`. Real SMS provider must be chosen by Owner before `P-08` production SMS work.
2. **`D5` (No Vercel / Docker VPS):** Vercel is rejected (`ASP.NET Core` + `PostgreSQL` container mismatch, Iran connectivity issues, sanctions risk). Free tiers must never hold real customer data. *(Further tightened in `v4.2` below.)*
3. **`D11` (Draft Lock Semantics):** Clarified that per-phone lock scopes strictly to an active `DRAFT` per phone per device; releases on issue, cancel, disconnect, or configurable timeout (15m in v4.1; updated to **5m** in `v4.2`); idempotent retries on the same `operation_id` remain valid; sequential purchases for the same phone remain allowed.
4. **`D14` (Terminology & Lawyer Ownership):** Replaced Board v4 "digital receipt" placeholder with **"Kandoo digital invoice"**; added digitally signed store install agreement covering data custody and encryption; mandated that a lawyer authors/approves all legal text and developers author none.
5. **`SEC-1` — Data Protection Model (New Decision):** Evaluated three models: (1) server-side encryption with restricted audited access, (2) E2EE for private store data with recovery key, (3) hybrid. Selected **Option 1 (server-side encryption + restricted, logged access)** as MVP default, requiring key envelopes/interfaces that do not block migrating private store data to E2EE later.
   - **Test Criterion:** Persistence/security tests verify encryption at rest/envelope configuration, RBAC rejection for unauthorized internal access, and immutable audit log entries on every privileged read.
6. **Rule — Restore Second Factor (New Mandatory Rule):** Restoring a store after app/OS reinstall requires **SMS OTP plus a second factor**: either (a) the **recovery key** generated at store installation OR (b) explicit approval from a **previously paired device**. A SIM-swap attack alone must never expose store history. *(Impacts `P-02`, `P-04`, `P-12`.)*
   - **Test Criterion:** Security test proves restore endpoint returns `403 Forbidden` when presented only with a valid SMS OTP token, and succeeds only with valid recovery key proof or signed paired-device approval.
7. **Rule — Agreement Truthfulness (New Mandatory Rule):** Store and user agreements must **never** assert any property the running software does not enforce (e.g., claiming *"Kandoo servers cannot read private data"* is illegal until E2EE is implemented and proven by test). Every claim in the agreement requires a linked automated test.
   - **Test Criterion:** Compliance test checklist maps each numbered technical statement in the active agreement template to an automated test in CI that must pass on every build.

---

## Amendments `v4.2` Record

1. **`D5` Amended (Replaces `v4.1` Hosting Clause — ACTIVE):**
   - Development and automated testing use **local Docker Compose** with **synthetic data only**.
   - **No free cloud service** is recommended or used anywhere in the workflow.
   - A small domestic or non-US **VPS with Docker** is used **only when a public address is strictly needed**.
   - Portability kit (`P-10`, `< 1 hour` migration) remains mandatory.
2. **`D11` Amended (Replaces `v4.1` Timeout Default — ACTIVE; OWNER-CONFIRMED BY `P-0104r`):**
   - Abandoned-`DRAFT` per-phone lock timeout default reduced from `15` minutes to **`5` minutes (300 seconds, configurable)**.
   - Only a newly Hub-accepted draft item, quantity, or customer change restarts the inactivity window; opening/viewing, passive heartbeats, polling, rejected operations, and no-effect replays do not. No API endpoint extends a lock.
   - Sequential purchases with the same phone number remain allowed.
3. **`SEC-1` Reframed (ACTIVE):**
   - Core security objective: **the server holds as little readable private store data as possible**.
   - **MVP stage:** Server-side encryption + restricted, logged access.
   - **Next stage:** End-to-end encryption (E2EE) for private store data backed by an owner recovery key.
   - Reaffirms **Agreement Truthfulness** (never claim what code does not enforce) and strict lawyer ownership of all legal text.
4. **`E-01` Amended (`E-01b` — COMPLETED IN `Kandoo-Digital-Invoice`):**
   - Canonical Invoice v0 reserves three **optional** structural fields under `summary`: `tax_lines` (array of `{label, amount, provenance}`), `discount_lines` (array of `{label, amount, provenance}`), and `rounding` (`integer | null`).
   - Neither computed nor business-validated in MVP; added solely so `ACC-0` requires no breaking schema change later.
5. **`ACC-0` — Accounting / Tax Store Module (`PARKED`):**
   - Optional store-side accounting/tax module is **explicitly PARKED** and **excluded from MVP**.
   - **Hard entry gates before unparking:**
     1. Tax & accounting domain expert review
     2. Market & competitor analysis
     3. Legal review of platform responsibility/liability shift
   - **Test Criterion:** MVP C# Canonical Invoice validator accepts omitted or structurally valid `tax_lines` / `discount_lines` / `rounding` fields without computing taxes, applying rounding, or enforcing accounting formulas.

---

## Assumptions

Per Global Rule 5 (*Missing decision: take conservative option, record in `docs/DECISIONS.md` under "Assumptions"*), the following conservative baselines govern until superseded by an explicit Owner/PM decision:

1. **`ASM-01` — Arena Session Branch Binding (`R-0`):** In Arena Agent Mode, the working session is cryptographically bound to branch `arena/456e429d-kandoo` (creation or pushing of alternate branch names is disallowed by the sandbox environment, matching `arena/9dc17500-kandoo-digital-invoice` in the sibling repo). All `R-0` deliverables (`arena/r-0-parent-truth` task scope) and tag `stable-r-0` are committed on `arena/456e429d-kandoo` and opened as a non-merged PR targeting `main`.
2. **`ASM-02` — Preservation of Pre-R-0 Bootstrap History:** Inspection of `majid-ghanbari-dost/Kandoo` prior to `R-0` found 3 initial documentation commits on `main` (`80d967c`, `729b10a`, `192fb470`) containing `README.md` and `AGENT_HANDOFF.md`, and zero application code. Existing principles in `AGENT_HANDOFF.md` are preserved intact alongside the new `R-0` handoff metadata rather than overwritten destructively.
3. **`ASM-03` — Exact Post-Commit SHA Recording:** Because a Git commit SHA-1 cannot self-reference its own file contents, `R-0` records the primary content commit SHA in an immediate follow-up `docs(r-0)` metadata commit within the same `R-0` PR commit group, with annotated tag `stable-r-0` pointing to the branch tip.
4. **`ASM-04` — Conservative Scope of `P-01`–`P-04` Descriptions:** High-level descriptions of `P-01`–`P-04` in `docs/MASTER_HANDOFF.md` reflect only established Board v4 / v4.1 / v4.2 and E-01 / E-09 architectural requirements. Specific C# package choices, UI frameworks, database schemas, and endpoints are deferred until each task's authoritative PM specification is issued.
5. **`ASM-05` — Fail-Closed Handling of Open Owner Decisions:** Until items in [Open](#open) are resolved by the Owner, all future platform code must keep SMS on the local stub (`D4`), run exclusively in local Docker Compose with synthetic data (`D5`), expose the `5-minute` draft lock timeout as a configuration setting (`D11`), and block live public customer pages if signed lawyer-approved copy is absent (`D14`).
6. **`ASM-06` — Authoritative Documentation-Only Scope of This `P-01` (`2026-10-22`):** The current PM task defines `P-01` as the domain-model specification and expressly excludes C# implementation. It supersedes the provisional `P-01` C# bootstrap description inherited from the `R-0` roadmap for this task only. The earlier bootstrap/contract implementation scope is deferred to a later explicit PM task; no implementation is claimed by `docs/domain-model.md`.
7. **`ASM-07` — Conservative Product Duplicate Handling (`P-01`):** Until a broader matching algorithm is approved, active products in one store reject an exact `normalized_name` duplicate. Equal conservative `similarity_key` values with different normalized names return a structured warning and create nothing until the operator explicitly acknowledges the identified candidate in a new operation. No duplicate path silently merges, overwrites, or reuses a product ID; matching never crosses store boundaries.
8. **`ASM-08` — Strict Idempotency Replay Matching (`P-01`):** An idempotent replay must match `(store_id, operation_id, device_id, operation_type, request_hash)`. Reusing the store-scoped operation ID with any different component returns `OPERATION_ID_CONFLICT` and has no domain effects. The operation record and its effects/response snapshot commit atomically.
9. **`ASM-09` — Pre-Issuance Retirement and Active-Lock Ownership (`P-01`; activity clause revised by `P-0104r`):** Cancellation and timeout retire a pre-issuance draft through platform `retired_at` / `retirement_reason` metadata rather than adding a Canonical Invoice lifecycle status. Only the device holding an active, unexpired per-phone lock may mutate, cancel, or issue that draft. Timeout never leaves residual write rights for the old device. The Owner confirmed the 300-second configurable default and that only a newly Hub-accepted draft item, `quantity_millis`, or customer change restarts inactivity; an open screen, passive heartbeat, rejected operation, or no-effect replay does not. Former Open item 3 is closed.
10. **`ASM-10` — Display-Number Allocation and Gaps (`P-01`; original posture SUPERSEDED by Owner-approved `P-0104r`):** The former first-durable-persistence rule and cancelled/timed-out-draft gap posture are withdrawn. The Store Hub allocates `display_number` only at successful `ISSUE`; all pre-issuance/retired drafts remain null-numbered and consume nothing. Successful issued numbers are gapless, strictly increasing, and never reused, and only successful issue advances the restart/restore high watermark. Former Open item 6 is closed.
11. **`ASM-11` — Explicit Tenant Scope and Customer-Only Global History (`P-01`):** Store reads require explicit store scope and active membership; device-originated mutations additionally require an active device paired to that store. Shared `Business` ownership grants no sibling-store access. Global `CustomerProfile` links never authorize store operators to read cross-store history: only the verified customer self through the registered global user and OTP-backed session may do so. Public tokens authorize only one issued invoice view.
12. **`ASM-12` — Verified-Phone Customer/Profile Linking (`P-01`):** Customer phones are normalized to E.164 syntax before matching and `(store_id, phone)` identifies one store-level Customer. A `CLAIMED` Customer has no global profile link. OTP verification creates or reuses the globally unique verified-phone `CustomerProfile`, and `VERIFIED` requires an exact normalized-phone match between Customer and profile. No customer identity or global profile is inferred from OCR/printed content.
13. **`ASM-13` — Documentation-Only Scope and Canonical Hash Profile of This `P-02` (`2026-10-22`):** The current PM task defines `P-02` as the sync/replication protocol specification and expressly excludes C# implementation. It supersedes the provisional `P-02` Store Hub implementation wording inherited from the `R-0` roadmap for this task only. `kandoo.sync.v0` and its test-vector hashes use UTF-8 canonical JSON with Unicode-code-point-sorted object keys, preserved array order (after specified state-collection sorting), compact separators, JSON integers, and lowercase SHA-256. This does not change Canonical Invoice v0 or claim a protocol implementation.
14. **`ASM-14` — Conservative Pairing and Session Baseline (`P-02`):** Owner-created pairing QR grants are single-use, store/Hub bound, `COMPANION`-only, and expire after `300` seconds by default. They cannot activate a second Hub. Sessions are Hub-issued with explicit expiry and bound to the paired device credential; renewal does not change operation identity. Revocation invalidates sessions and releases owned locks as `DISCONNECT`. Sessions are runtime state and are never restored onto a replacement Hub.
15. **`ASM-15` — Ordered Atomic Replication, Durable Retry, and Backpressure (`P-02`):** `operation_seq` is contiguous per store and continues across authorized writer epochs. Cloud ingest batches are atomic, validate ascending sequences and hashes, acknowledge exact duplicates without effects, and reject gaps/out-of-order/conflicting entries without partial projection. One ordered upload stream per store retries unchanged entries with a configurable `1s` doubling to `60s` ceiling plus jitter and obeys advertised throttle/batch limits. Unacknowledged entries are never discarded; before local durable capacity is exhausted, new mutations fail explicitly rather than overwriting the outbox.
16. **`ASM-16` — Restore Activation and Writer Fencing (`P-02`):** OTP plus recovery-key proof or previously paired-device approval is required before restore export. A replacement writer activates only after contiguous log verification and exact replayed-state hash match; activation increments `writer_epoch`, preserves idempotency/numbering state, fences the old writer to read-only, and never restores old sessions. Divergent suffixes from fenced writers are quarantined for reviewed recovery rather than auto-merged. Disk-only unsynced entries are reported as unrecovered, never as a lossless restore.
17. **`ASM-17` — Time Authority and Partial-Delivery Handling (`P-02`):** Client timestamps are informational; Hub time governs LAN ordering and D11 expiry, while operation sequence and hash chain govern replication. Excessive Hub/Cloud clock skew requires operational review without rewriting accepted history. Incomplete/truncated batches apply no partial projection; a committed batch with a lost response is retried unchanged and acknowledged idempotently.
18. **`ASM-18` — Documentation-Only Scope and HTTP Path Versioning (`P-04`, `2026-10-22`):** The current PM task defines `P-04` as the Cloud API contract and expressly excludes implementation. It supersedes the provisional Cloud API implementation wording inherited from the `R-0` roadmap for this task only. `docs/openapi.yaml` is the normative OpenAPI 3.1 contract and uses `/v1` path versioning so routing, client base URLs, access policy, and parallel migration do not depend on a custom version header. HTTP `/v1`, `kandoo.sync.v0`, and Canonical Invoice `schema_version: "0"` remain independent versions.
19. **`ASM-19` — Conservative Cloud Credential, Pairing, and Revocation Baseline (`P-04`):** Cloud uses opaque, scoped per-device bearer credentials rather than prescribing JWT; servers must store only token hashes. Registration is the sole pre-Hub bootstrap. The Hub generates raw pairing QR/nonces locally and Cloud retains only SHA-256 fingerprints and replicated Hub outcomes, so offline LAN pairing and late replication remain valid. Pairing and revocation are companion-only; Hub replacement requires authorized restore. Companion Cloud tokens cannot perform domain writes. Rotation and successful restore activation each permit only identical-request/same-key replay for 60 seconds to recover a lost response.
20. **`ASM-20` — HTTP Idempotency, Cursor, Empty-Batch, and Rate Baseline (`P-04`):** Every POST requires a 1–128-character safe `Idempotency-Key`. Administrative/bootstrap idempotency records are retained for at least 24 hours with conflict-safe tombstones, while domain operation/log deduplication is permanent. Batch keys equal `batch_id`; single-entry mutation keys and acknowledgments equal the contained `operation_id`. Empty wire batches remain representable so writer fencing occurs first: stale writers receive `WRITER_FENCED`, while active-writer empty batches receive `INCOMPLETE_BATCH` with no effects. Lists use opaque store/filter-bound cursors with default 50/maximum 100; batches default to at most 100 entries/1 MiB and the group-specific documented limits until production tuning is approved.
21. **`ASM-21` — Conservative Restore, Delivery, and Legal-Gate Boundary (`P-04`):** Restore v1 exports the full log from genesis only after OTP plus SEC-1 second factor. OTP-only preflight returns binding data without export and does not consume the OTP verification token; successful two-factor authorization consumes it. Activation reports Cloud-high-watermark-only recovery and exposes its 60-second replay deadline. SMS remains a deterministic stub with no external calls. Store registration requires digital acceptance of the configured lawyer-approved onboarding agreement, and public rendering requires approved copy; SMS-stub/QR/share-link creation itself is not a public-rendering legal gate. Every malformed, invalid, or unknown public token has the same 404 response.
22. **`ASM-22` — Unified Scaled-Integer Quantity Encoding (`P-0104r`, `2026-10-23`):** All domain, sync, hash-bearing payload, and applicable API quantity values use signed integer thousandths with explicit field names `quantity_millis` for positive invoice-item quantities, `quantity_delta_millis` for non-zero signed inventory movements, and `stock_millis` for derived stock projections (`1000 = 1.000`; `-2000 = -2.000`). Stock and line arithmetic use exact signed-integer arithmetic with explicit overflow/range validation at implementation boundaries; binary floating point, locale-sensitive parsing, and decimal-string quantity encodings are forbidden. This preserves the integer-only canonical JSON hash profile, avoids binary rounding and locale ambiguity, supports exact thousandths and signed ledger corrections, and gives P-05–P-09 one simple validation rule. Existing integer Rial fields such as `unit_price_rials`, `line_total_rials`, and `total_rials` remain unscaled and unchanged.
23. **`ASM-23` — Conservative E-11 Consumer Boundary (`C-07`, `2026-10-24`):** The E-11 schema and seven JSON goldens/manifest are test fixtures only; no Python implementation is copied. The C# consumer rejects duplicate keys, non-canonical integer number tokens, invalid required structure, premature display numbers, negative core monetary amounts, and issued-content hash mismatches. Every accepted source state, including `ISSUED` and `REVOKED`, maps only to a `REVIEW` queue entity and never invokes native issuance or inventory effects. Optional ACC-0 tax/discount/rounding fields remain structural-only and are not given new accounting semantics.
24. **`ASM-24` — Cloud PostgreSQL and Portable CI Baseline (`C-05`, `2026-10-24`):** Production Cloud ingest has no in-memory store fallback; store state, log entries, and batch idempotency fingerprints persist in PostgreSQL transactions with per-store row locking. Linux CI uses a synthetic PostgreSQL 16 service container. Windows CI uses a real local PostgreSQL 15.3.0 embedded test process with a free port and no service container, so the same CLOUD vectors execute rather than being skipped or routed to an in-memory substitute.
25. **`ASM-25` — Single-Invoice Public Preview and D14 Gate (`C-06`, `2026-10-24`):** `/i/{token}` is an anonymous capability read of exactly one persisted issued/terminal invoice projection; tokens are generated from 16 CSPRNG bytes, malformed and unknown tokens share one 404, and no history/list or cross-store fallback exists. Development/Testing previews render the explicitly marked `needs lawyer approval` placeholder only. Production continues to fail closed with `LEGAL_COPY_UNAVAILABLE`; C-06 does not claim an approved-copy production path, and the placeholder is not legal copy or an approval claim.
26. **`ASM-26` — Hub API Credential and LAN Execution Boundary (`C-08`, `2026-10-25`):** The Hub API uses opaque bearer session tokens and device credentials returned once and persisted only as SHA-256 hashes; QR proofs are single-use, 128-bit minimum, hashed at rest, and never included in audit events. Owner bootstrap requires explicit configuration and is unavailable otherwise. QR pairing can create only `COMPANION` devices. `LAN-HUB-001` and strict Hub-off/D7 client behavior remain outside the Hub API test harness; the Hub fail-closed responsibility is to reject unpaired, revoked, expired, wrong-store, and wrong-device mutation attempts. Authenticated OCC/lock rejections use durable operation responses and outbox entries, while transport/authentication/shape failures create none.

---

## Open

The following decisions require explicit action from the **Product Owner** (and Lawyer where noted) and must **not** be invented by developers. Owner-approved `P-0104r` (`2026-10-23`) closed former **Open item 3** (300-second default plus accepted-change-only inactivity) and former **Open item 6** (issue-time gapless display numbering). Original item numbers are retained below, so items 3 and 6 are intentionally absent.

| Item | Related Decision | Current Conservative Posture | Blocking Gate | Action Needed from Owner |
| --- | --- | --- | --- | --- |
| **1. SMS Provider** | `D4` (v4.1) | Use local deterministic `StubSmsSender`; no external gateway credentials in repo. | Before `P-08` (real SMS delivery) | Select domestic/international SMS provider, pricing tier, and sender line policy. |
| **2. Host** | `D5` (v4.1 / v4.2) | Local Docker Compose with synthetic data only; zero free cloud services. | Before any public pilot / `P-08`–`P-10` external exposure | Choose domestic or non-US Docker-capable VPS vendor for public endpoints. |
| **4. Legal Wording (`D14`, by Lawyer)** | `D14` + `SEC-1` + Truthfulness Rule | Product named `"Kandoo digital invoice"`; zero legal claims written by developers; customer pages gated. | Hard release gate before `P-09` public customer views & store onboarding release | Provide lawyer-drafted/approved customer invoice notice and digitally signed store installation custody/encryption agreement matching tested MVP capabilities. |
| **5. Product Near-Duplicate Matcher (`P-01`)** | `D9` + `ASM-07` | Exact normalized duplicates reject; equal conservative similarity keys warn and require candidate-specific acknowledgement; no automatic merge. | Before adding broader fuzzy matching to product-catalog implementation | Confirm whether spacing/hyphen equivalence is sufficient for the first increment or approve a deterministic additional algorithm, threshold, and Persian/Latin test fixtures. |
| **7. Pairing and Session Lifetimes (`P-02`)** | `D7` + `ASM-14` | Single-use companion QR expires after `300s`; sessions have explicit Hub-issued configurable expiry and are bound to the device. | Before production pairing/session UX is finalized | Confirm QR validity and session renewal/absolute-lifetime requirements for store operations. |
| **8. Replication and Outbox Tuning (`P-02`)** | `D7` + `ASM-15` | Retry `1s` doubling to `60s` with jitter; one ordered stream; obey Cloud limits; never discard unacknowledged entries. | Before production rollout on target Hub hardware | Approve batch byte/entry limits, retry ceilings, soft sync-warning threshold, and hard durability-capacity behavior. |
| **9. Clock-Skew Incident Threshold (`P-02`)** | `D7` + `D11` + `ASM-17` | Client time never controls expiry/order; sequence/hash governs replication; excessive Hub/Cloud skew is review. | Before production monitoring/runbook completion | Define acceptable Hub/Cloud skew, detection method, alerting, and operator remediation thresholds. |
| **10. Fenced-Writer Divergent Suffix Recovery (`P-02`)** | `D8` + `ASM-16` | Old writer stays read-only; divergent unsynced entries are quarantined and never auto-merged. | Before a production two-Hub/disk-recovery incident runbook is approved | Specify owner-facing review/export/reconciliation procedure without reusing sequences or overwriting the active writer. |
| **11. Cloud Credential and Replay Parameters (`P-04`)** | `D7` + `D8` + `SEC-1` + `ASM-19`/`ASM-21` | Opaque scoped tokens stored as hashes; rotation and activation exact-replay grace is 60 seconds; no JWT or private-key transport is prescribed. | Before production pairing, restore, or token rollout | Approve token TTLs, opaque-token hash/storage profile, renewal policy, revocation propagation, and replay-cache durability. |
| **12. Production HTTP Retention, Cursor, and Rate-Limit Tuning (`P-04`)** | `D10` + `ASM-20` | Administrative idempotency is at least 24 hours with conflict-safe tombstones; domain dedup is permanent; cursors are opaque and bound; conservative documented limits apply. | Before multi-replica or production deployment | Approve retention/compaction, cursor encoding/signing and expiry, distributed rate-limit storage, and measured endpoint/batch limits. |
| **13. Cloud Implementation and Cryptographic Profiles (`P-04`)** | `D2` + `D8` + `SEC-1` + `ASM-18`/`ASM-21` | OpenAPI YAML is source; Redocly 2.69.0 validates it; no generator, canonical-JSON library, key-envelope algorithm, or approval-signature algorithm is selected. | Before Cloud implementation and security rollout | Select ASP.NET OpenAPI/validation/CI tooling, a .NET canonical-JSON implementation proven against all P-02 vectors, SEC-1 envelope algorithms, paired-approval signature profile, and OTP implementation. |
