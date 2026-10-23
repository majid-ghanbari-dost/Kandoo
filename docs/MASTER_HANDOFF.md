# KANDOO — MASTER HANDOFF (Parent Platform Repository)

**Document status:** Authoritative R-0 Baseline  
**Date:** `2026-10-22`  
**Repository:** `majid-ghanbari-dost/Kandoo` (Parent Platform Repository)  
**Task:** `R-0 — Parent repo truth`

---

## 1. Project Overview

**Kandoo** is a store digitalization and digital-invoice platform structured around the domain hierarchy `Business → Store → Users / Devices`, designed local-first for retail stores operating under intermittent connectivity.

### Two-Repository Architecture

The Kandoo system is strictly partitioned across two separate repositories with a hard contract boundary between them:

1. **`majid-ghanbari-dost/Kandoo` (Parent Platform Repository — THIS REPOSITORY)**  
   Owns the complete Kandoo product platform: C# (`ASP.NET Core` / `.NET`) domain services, local Store Hub (`SQLite` + Outbox), Cloud API (`PostgreSQL`), unified Windows and Android client applications, customer invoice delivery/viewing pages, operational security controls, deployment configuration, and platform governance documentation.
2. **`majid-ghanbari-dost/Kandoo-Digital-Invoice` (Python Extraction Module Repository)**  
   Owns the standalone local Python image/print-capture extraction pipeline (`E-00` through `E-09`): print-spooler image layout detection, Glyph thermal-font numeric classification, fail-closed arithmetic gating, Canonical Invoice v0 schema validation, and the local CLI entrypoint (`image_extraction_adapter.cli`).

### Core Product Split (`D1` & `D2`)

- **`D1` — Two Operating Modes:**
  - **Mode `A` (Native Store Software):** Kandoo **is** the store's primary point-of-sale and inventory software (`Sale → Invoice → Digital Invoice`). All line items, prices, customer records, and ledger inventory events originate natively inside Kandoo.
  - **Mode `B` (Sidecar / Companion Software):** Kandoo runs **beside** an existing third-party store/POS system (such as Holoo) without accessing its database (`D3`). Printed invoice output is captured locally through the print spooler, extracted locally in Python, passed through the fail-closed Canonicalization Gate (`AUTO` / `REVIEW` / `REJECT`), and handed to the Kandoo platform for operator review, customer phone binding, and digital invoice issuance.
- **`D2` — Language-Independent Canonical Contract & Stack Split:**
  - Extraction and optical/layout analysis are implemented in **Python** (`Kandoo-Digital-Invoice`).
  - Store Hub, Cloud API, domain lifecycle, concurrency, and client apps are implemented in **C# (`ASP.NET Core` / `.NET`)** (`Kandoo`).
  - The two sides share **zero implementation code** and communicate exclusively through the language-independent **Canonical Invoice v0** JSON contract (`schema_version: "0"`) and the local **E-09 CLI** process boundary.

---

## 2. Repo Topology

```text
majid-ghanbari-dost/Kandoo  (Parent Platform Repo — C# / ASP.NET Core / Windows / Android / Cloud)
├── AGENT_HANDOFF.md        # Operational handoff state & exact cross-repo Git SHAs
├── README.md               # Repository entrypoint pointing to AGENT_HANDOFF.md
└── docs/
    ├── MASTER_HANDOFF.md   # Architecture, repo topology, D1-D14 summary, state & roadmap
    ├── DECISIONS.md        # Authoritative D1-D14 register, v4.1 + v4.2 amendments, Assumptions, Open items
    ├── domain-model.md     # P-01 logical entities, relationships, rules & concrete test scenarios
    ├── sync-protocol.md    # P-02 LAN, replication, restore, failures & vector index
    ├── sync-protocol-test-vectors.json # P-02 synthetic P-05/P-07 protocol vectors
    ├── openapi.yaml        # P-04 normative OpenAPI 3.1 Cloud contract
    └── cloud-api-contract.md # P-04 endpoint/auth/error/idempotency/rate-limit narrative
    [Future P-01..P-17: C# solution, Store Hub, Windows/Android app, Cloud API, Docker kit]
            │
            │  STRICT BOUNDARY (No Python source copied into Kandoo)
            │  1. Data Contract: Canonical Invoice v0 JSON (schema_version = "0")
            │  2. Process Seam : ProcessStartInfo -> python -m image_extraction_adapter.cli extract
            ▼
majid-ghanbari-dost/Kandoo-Digital-Invoice  (Mode B Extraction Repo — Python)
├── schemas/canonical_invoice_v0.json       # Frozen Canonical Invoice v0 + E-01b JSON Schema
├── src/image_extraction_adapter/
│   ├── canonical_invoice.py                # Structural, provenance, lifecycle & SHA-256 validator (E-01/E-01b)
│   ├── pipeline_v0.py                      # Local layout + Glyph numeric OCR + math gate (E-02)
│   └── cli.py                              # Parent integration CLI: exit codes 0 / 10 / 20 / 30 (E-09)
└── docs/                                   # Canonical Invoice v0 & CLI contract specifications
```

### Repository Responsibilities & Integration Contract

| Concern | `Kandoo` (Parent — C#) | `Kandoo-Digital-Invoice` (Module — Python) |
| --- | --- | --- |
| Primary languages | C# (`.NET` / `ASP.NET Core`), SQL (`SQLite`, `PostgreSQL`) | Python 3 + Pillow + `jsonschema` (offline local only) |
| Operating modes served | Mode `A` (native) and Mode `B` (host/orchestrator) | Mode `B` capture extraction only |
| Platform identifiers (`invoice_id`, `display_number`, `operation_id`, `device_id`, `public_token`, `store_id`) | **Owner / Producer** — allocates and persists all IDs | **Consumer only** — receives IDs via `--context` JSON; never invents IDs |
| Customer phone & `phone_status` (`claimed` / `verified`) | **Owner** — operator capture UI, OTP verification, D11 per-phone draft lock | Never infers phone or customer identity from printed receipt |
| Extraction & OCR | Invokes Python CLI locally via `ProcessStartInfo.ArgumentList` | Runs projection layout + Glyph classifier + arithmetic gate locally |
| Cross-repo communication | Reads UTF-8 Canonical Invoice v0 JSON from `stdout` + exit code (`0`=`AUTO`, `10`=`REVIEW`, `20`=`REJECT`, `30`=`ERROR`) | Validates output against `canonical_invoice_v0.json` before emitting to `stdout` |
| Lifecycle authority (`VALIDATED`, `ISSUED`, `REVOKED`, `SUPERSEDED`) | **Exclusive owner** of validation, issuance, delivery, sync, and storage | Never advances state beyond `EXTRACTED`; never auto-issues |
| Source code sharing | **PROHIBITED** — never copy Python modules here | **PROHIBITED** — no parent UI, hub, cloud, or SMS logic |

---

## 3. Decisions `D1`–`D14` (Board v4 + Amendments v4.1 & v4.2)

Full details and executable test criteria are maintained in [`docs/DECISIONS.md`](DECISIONS.md).

| ID | Summary | Owner | Reason | Consequence | Status |
| --- | --- | --- | --- | --- | --- |
| **D1** | **Two operating modes:** Mode `A` (Kandoo is store software) and Mode `B` (Kandoo runs beside existing POS software). | Product Owner | Stores vary between greenfield shops needing POS/inventory and established shops locked into legacy POS tools. | Architecture must explicitly separate Mode `A` native sales from Mode `B` external capture without coercing one into the other. | **ACTIVE** (Board v4) |
| **D2** | **Canonical contract & language split:** Language-independent Canonical Invoice contract; extraction in Python; platform in C# (`ASP.NET Core`). | Product Owner / Architect | Python excels at local image/glyph processing while C# (`ASP.NET Core` / `.NET`) provides strongly typed multi-platform app, hub, and API infrastructure. | Integration strictly uses Canonical Invoice v0 JSON + E-09 CLI (`Process.Start`); cross-language serialization and SHA-256 Golden Vectors must match byte-for-byte. | **ACTIVE** (Board v4) |
| **D3** | **No Holoo DB access:** Print spooler path is the product; operator supplies customer phone/name. | Product Owner | Holoo provides no cooperative DB access and its web service is prohibitively expensive for stores; printed receipts lack real customer phone/name. | Direct production Holoo DB integration is permanently out of scope; Mode `B` captures print output and prompts the operator for customer phone. | **CLOSED — FINAL** (`2026-10-17`, Board v4) |
| **D4** | **Customer delivery & verification:** SMS + QR + share button; `>= 128-bit` unguessable token; single-invoice view without account; full history via OTP (`phone_status = claimed \| verified`). *v4.1:* SMS provider unchosen; stub used until `P-10` (real provider selected before `P-08`). | Product Owner | Frictionless single-invoice viewing maximizes adoption while OTP protects multi-invoice history across stores; avoids blocking MVP engineering on telco procurement. | Public links require `>= 22`-char base64url random tokens; `ISmsSender` abstraction uses deterministic stub through `P-10`; real provider is a gate before `P-08`. | **AMENDED (v4.1) — OPEN** (provider choice pending owner) |
| **D5** | **Hosting & environment:** *Board v4:* `< 1h` migration portability. *v4.1:* No Vercel; small VPS with Docker; no customer data on free tiers. *v4.2:* Testing uses **local Docker Compose** with synthetic data; **no free cloud**; small domestic/non-US VPS with Docker only when a public address is needed; `P-10` portability kit kept. | Product Owner | Free cloud tiers sleep, expire DBs, and face sanctions/access barriers in Iran; `ASP.NET Core` + `PostgreSQL` maps cleanly to Docker Compose. | All dev/integration testing runs locally via Docker Compose on synthetic data; zero free-tier cloud dependencies; `< 1 hour` Docker portability preserved (`P-10`). | **AMENDED (v4.2) — OPEN** (final production VPS host pending owner) |
| **D6** | **Unified application & accounts:** One Kandoo app architecture targeting Windows and Android; one user account can hold memberships in multiple stores/businesses. | Product Owner | Operators, managers, and owners work across desktop counters and mobile devices and may belong to multiple stores. | Identity model separates global `User` authentication from scoped `Business` / `Store` role memberships from day one. | **ACTIVE** (Board v4) |
| **D7** | **Single Store Hub on LAN:** One hub per store; companion devices connect over LAN only (no Bluetooth); if hub is offline, companion devices cannot start work; hub operates offline without internet and replicates to cloud asynchronously. | Product Owner / Architect | Eliminates split-brain state inside a store while preserving full offline continuity during internet outages. | Store Hub owns local state ordering, locks, and numbering; LAN reachability check gates companion device workflows; cloud sync uses durable Outbox. | **ACTIVE** (Board v4) |
| **D8** | **Backup & restore:** Full cloud replica + local store backup; Store Hub disk is a documented single point of failure for unsynced local-only window. | Product Owner | Balances hardware failure recovery against realistic single-PC store hardware constraints. | Hub maintains cloud replication plus scheduled/exportable local backups; restore flows must also enforce `SEC-1` and **Restore Second Factor** rules (`v4.1`). | **ACTIVE** (Board v4 + v4.1 2FA rule) |
| **D9** | **Product identity & deduplication:** Unique store-scoped product ID + normalized product name; near-duplicate names trigger an operator warning. | Product Owner | Prevents catalog fragmentation and accidental duplicate SKUs without blocking legitimate similar items. | Product catalog stores normalized name index per store and surfaces non-silent similarity warnings before creation/confirmation. | **ACTIVE** (Board v4) |
| **D10** | **Concurrency & immutability:** Event-ledger inventory; `base_version` OCC on price/spec edits; conflicts surfaced to operator (never silent overwrite); `ISSUED` invoices immutable; every mutation carries idempotent `operation_id` + `device_id`. | Product Owner / Architect | Multi-device retail counters experience retries, race conditions, and offline queues where silent last-write-wins corrupts financial/stock records. | All commands require `operation_id` + `device_id`; stock changes append ledger events; metadata updates check `base_version`; post-`ISSUED` edits are rejected (`REVOKED`/`SUPERSEDED` only). | **ACTIVE** (Board v4) |
| **D11** | **Per-phone `DRAFT` lock:** Active `DRAFT` invoice locked per customer phone on one device; second device receives a hard error; released on issue, cancel, disconnect, or configurable timeout (**default `5 minutes` / 300 seconds** in `v4.2`, replacing `15 minutes` in `v4.1`); same `operation_id` uses idempotency; sequential purchases allowed. **P-0104r:** only newly Hub-accepted draft item/quantity/customer changes restart inactivity; passive screens/heartbeats do not, and no API endpoint extends a lock. | Product Owner | Prevents two cashiers/devices from simultaneously building conflicting drafts for the same customer phone without blocking repeat shopping trips. | Store Hub maintains an active per-phone `DRAFT` lease tied to `device_id` with configurable TTL defaulting to 300 seconds; only accepted draft changes renew it, and replay of identical `operation_id` succeeds idempotently without renewal. | **AMENDED (v4.2) — OWNER-CONFIRMED (`P-0104r`)** |
| **D12** | **Dual invoice numbering:** Internal globally unique `invoice_id` plus sequential human-readable `display_number` allocated by the Store Hub only at successful `ISSUE` (distinct from external POS printed number). Pre-issuance and retired drafts have `display_number=null`; issued numbers are gapless and never reused. | Product Owner | Internal keys need collision-free opacity across stores/offline nodes, while staff and customers need contiguous store-level numbers without draft-time gaps. | Store Hub transactionally allocates the next monotonic `display_number` per store only with successful issue and preserves the issued-only high watermark across restart/restore; external printed POS numbers stay in untrusted `header.invoice_number`. | **AMENDED — OWNER-APPROVED (`P-0104r`)** |
| **D13** | **Staged delivery, multi-device from day one:** Deliver features incrementally, but multi-device hub/LAN architecture and concurrency primitives exist from the first platform increment. | Product Owner | Retrofitting multi-device concurrency, `device_id`, locks, and hub routing onto a single-user local app requires a destructive rewrite. | `P-01`–`P-04` include `device_id`, `operation_id`, hub coordination, and multi-client boundaries from inception rather than deferring multi-device support. | **ACTIVE** (Board v4) |
| **D14** | **Positioning & lawyer-owned legal copy:** *v4.1 replacement:* Product wording is **"Kandoo digital invoice"** (never described as a "receipt"); install-time signed store agreement covers custody & encryption; **all legal wording is drafted/approved by a lawyer**; developers write zero legal claims. | Product Owner + Lawyer | Legal/tax classification carries regulatory liability; informal developer disclaimers or calling invoices "receipts" misstates the product and creates legal risk. | UI/templates use placeholder gating until exact lawyer-approved text is supplied; release gate blocks public customer pages (`P-09`) until approved wording is verified by test. | **AMENDED (v4.1/v4.2) — OPEN** (lawyer text pending owner) |

### Additional Cross-Cutting Governance Items (v4.1 & v4.2)

- **`SEC-1` — Data Protection Architecture (Added v4.1, Reframed v4.2 — ACTIVE):** Server holds as little readable private store data as possible. MVP defaults to **server-side encryption + restricted, audited/logged access**, designed with clean key boundaries so the next stage can move private store data to **end-to-end encryption with a recovery key**.
- **Rule — Restore Second Factor (Added v4.1 — ACTIVE):** Store restore after reinstall requires **SMS OTP plus a second factor** (either an install-time **recovery key** OR approval from a **previously paired device**) so a SIM-swap attack can never expose an entire store (`P-02`, `P-04`, `P-12`).
- **Rule — Agreement Truthfulness (Added v4.1, Reinforced v4.2 — ACTIVE):** Store/customer agreements must **never** claim technical guarantees that the codebase does not implement and prove via automated tests.
- **`E-01b` — Optional Accounting Placeholders (Added v4.2 — DONE in `Kandoo-Digital-Invoice`):** `summary.tax_lines`, `summary.discount_lines`, and `summary.rounding` (`null` allowed) added as optional structural fields in Canonical Invoice v0 without MVP calculation or accounting validation.
- **`ACC-0` — Store Accounting/Tax Module (Added v4.2 — PARKED):** Excluded from MVP; gated on tax/accounting expert review, market/competitor study, and legal liability review.

---

## 4. Current State

### `majid-ghanbari-dost/Kandoo-Digital-Invoice` (Extraction Repository)

- **Branch:** `arena/9dc17500-kandoo-digital-invoice`
- **Exact `HEAD`:** `27d6d14ef02302423a903e09adb60bfe0164c8f7` (`feat(cli): E-09 CLI for parent integration (extract command, exit codes, Canonical JSON output)`, committed `2026-10-22`)
- **Completed scope (`E-00` through `E-09`):**
  - `E-00` / `E-00b`: Synced Board v4 decisions `D1`–`D14` plus Amendments `v4.1` and `v4.2` (`SEC-1`, Restore 2FA, Agreement Truthfulness, `ACC-0` parked).
  - `E-01` / `E-01b`: Implemented `schemas/canonical_invoice_v0.json`, `canonical_invoice.py` validator (integer Rials, field-level provenance, lifecycle graph, UTF-16 key-sorted compact JSON `issued_content_sha256` digest, and reserved optional `tax_lines` / `discount_lines` / `rounding` fields), documentation, and unit tests.
  - `E-02`: Implemented offline local `pipeline_v0.py` (projection layout detection, Glyph numeric cell classification, strict arithmetic gate, dHash visual clustering, fail-closed `AUTO_CANDIDATE` / `REVIEW` / `REJECT` routing, operator correction re-gating).
  - `E-04`: Completed read-only issuance gap analysis (`docs/issuance-gap-analysis.md`) against Board v4.
  - `E-09`: Implemented `python -m image_extraction_adapter.cli extract` CLI integration boundary with exit codes `0` / `10` / `20` / `30` and validated Canonical Invoice v0 `stdout` output (`docs/cli-contract.md`).

### `majid-ghanbari-dost/Kandoo` (Parent Platform Repository — THIS REPO)

- **Pre-R-0 baseline (`main`):** `192fb470bcbebb34fcda220abb9e1a63c7f12d0b`
  - Contained **3 initial bootstrap documentation commits** (`80d967c` `Initial commit`, `729b10a` `docs: add mandatory parent project agent handoff`, `192fb470` `docs: point parent README to agent handoff`) with **only 2 markdown files** (`README.md` and `AGENT_HANDOFF.md`) and **zero application, server, client, schema, or test files**.
- **R-0 baseline (this task):**
  - **Branch:** `arena/456e429d-kandoo` *(Arena session branch for logical task branch `arena/r-0-parent-truth`)*
  - **`R-0` primary commit:** `6e17959afb8033f3dae1a09a4db28e2cbc4059ce` *(followed by SHA-recording commit tagged `stable-r-0`)*
  - Establishes authoritative architecture handoff (`docs/MASTER_HANDOFF.md`), complete decision register (`docs/DECISIONS.md`), updated cross-repo SHA handoff (`AGENT_HANDOFF.md`), and annotated tag `stable-r-0`.
  - Contains **documentation foundation only** — zero C# code exists yet.
- **`P-01` domain-model specification:**
  - Branch `arena/91ef8c4b-kandoo`, based on merged `main` HEAD `6409de18074f3ca71673d017ef671b51cc70c05c`.
  - Adds `docs/domain-model.md` with all 15 logical entities, an ASCII entity-relationship diagram, `D9`–`D13`/store-isolation rules, and 67 concrete acceptance-test scenarios.
  - Is explicitly **documentation-only**. The PM prompt superseded the provisional `P-01` C# bootstrap wording in the inherited `R-0` roadmap; implementation remains deferred to a later explicit PM task (`ASM-06`).
- **`P-02` sync/replication protocol specification:**
  - Cumulative Arena branch starts at `e8a935105bdfad7ab323b4a83ec83b8a4c44ecc2` (`stable-p-01`); PR #2 remains open and unmerged.
  - Adds `docs/sync-protocol.md` and 34 synthetic JSON vectors for P-05/P-07 covering LAN pairing/sessions/operations/locks, ordered Cloud replication, two-factor restore with replayed-state hashing, and all seven required failure cases.
  - Is explicitly **documentation-only**. The current PM prompt supersedes the provisional `P-02` Store Hub implementation wording in the inherited `R-0` roadmap; implementation remains deferred to a later explicit PM task (`ASM-13`).
- **`P-0104r` unified documentation revision (current PM task):**
  - Cumulative Arena branch starts at `ee1e17165924798339075e705a2aa45ecc60f899` (`stable-p-04` tip); PR #2 remains open and unmerged.
  - Exact six-document content commit is `1f9da7c1b6227c7a1332d581266f52fdce856614`; the follow-up handoff-recording commit completes the commit group tagged `stable-p-0104r`.
  - Revises `docs/domain-model.md`, `docs/sync-protocol.md`, `docs/sync-protocol-test-vectors.json`, `docs/openapi.yaml`, `docs/cloud-api-contract.md`, and `docs/DECISIONS.md`: display numbers are allocated only at successful `ISSUE`; cancelled/timed-out drafts consume none; only newly Hub-accepted draft item/quantity/customer changes restart the 300-second inactivity window; `ASM-22` standardizes signed integer thousandths (`quantity_millis`, `quantity_delta_millis`, `stock_millis`).
  - Changes 15 domain rule rows, 21 domain test rows, and 21 vectors (`LAN-SESSION-002`, `LAN-LOCK-001`–`006`, `LAN-HUB-001`, `CLOUD-001`–`005`, `RESTORE-001`–`004`, `FAIL-001`, `FAIL-003`–`005`); all 34 vectors, 11 wire batches/18 entries, 22 operations, 15 POST mutations, 51 error codes, and 15/52/67 domain counts are preserved.
  - Independent canonical SHA-256 validation passed 34 request occurrences, 21 entry occurrences, and two replayed-state projections in both Python and Node.js. Required Redocly command `npx --yes @redocly/cli lint docs/openapi.yaml` passed with CLI `2.69.0`, exit 0, zero errors/warnings. Intentional `CLOUD-007` corruption remains a negative fixture.
  - `ASM-09`/`ASM-10` record the owner-approved D11/D12 revisions and new `ASM-22` records quantity encoding/rationale; former Open items 3 and 6 are closed. No P-03 or implementation work was started.
- **`P-04` Cloud API contract:**
  - Cumulative Arena branch starts at `2b08caa16a794311c8f50cafb40c19a30919b69f` (`stable-p-02`); PR #2 remains open and unmerged.
  - Adds `docs/openapi.yaml` (OpenAPI 3.1) and `docs/cloud-api-contract.md`: 22 operations in six groups, 15 idempotent mutations, 51 registered error codes, per-device/restore credentials, path versioning, cursor pagination, and default rate limits.
  - Preserves Hub-only domain writes, P-02-compatible entries/batches/locks, two-factor restore with replay/hash checks, D4 SMS-stub/public-token behavior, and D14 onboarding/public-rendering gates. Covers the Cloud surfaces needed by P-05 through P-09 without implementing them.
  - Is explicitly **documentation and YAML only**. The current PM prompt supersedes the provisional `P-04` implementation wording in the inherited `R-0` roadmap; implementation remains deferred to later explicit PM tasks (`ASM-18`).

---

## 5. What Does NOT Exist

To prevent false assumptions by future agents or contributors, the following artifacts **DO NOT EXIST** in this repository or anywhere in the owner's repositories as of `R-0`:

1. **No Windows source code:** No WPF, WinUI, MAUI, Avalonia, WinForms, print-spooler agent UI, or Windows client project exists yet.
2. **No Android source code:** No MAUI, Kotlin, Java, Flutter, React Native, or Android client project exists yet.
3. **No C# / ASP.NET Core / Hub / Cloud API source code:** No `.sln`, `.csproj`, `.cs`, SQLite store, PostgreSQL migration, or Docker Compose implementation exists yet (`P-01` through `P-17` build these from scratch).
4. **No uploaded demo files in the product:** Earlier external demo/chat files — specifically `auth.py` containing a hardcoded/fixed password, ad-hoc inspection scripts, tool-state JSON snapshots, and any legacy Build/Bootstrap scripts — are **completely unrelated** to Kandoo and are **strictly excluded** from this repository.
5. **No copied `Kandoo-Digital-Invoice` source code:** Python extraction code lives exclusively in `majid-ghanbari-dost/Kandoo-Digital-Invoice` and must never be copied into `Kandoo`.
6. **No Holoo database integration:** Permanently out of scope per `D3` (`CLOSED`).
7. **No MVP accounting/tax engine:** Parked per `ACC-0` (`PARKED`).

---

## 6. Next Tasks (`P-01`, `P-02`, `P-03`, `P-04`)

Platform work proceeds strictly in one-task / one-commit-group increments (`P-01` to `P-17`) after PM review of `R-0`. **Current PM clarifications (`2026-10-23`):** `P-01` is the documentation-only domain-model specification, `P-02` is the documentation-only sync/replication protocol specification, `P-04` is the documentation-plus-YAML Cloud API contract below, and owner-approved `P-0104r` is the completed unified revision recorded above. The previously listed C# solution bootstrap, Canonical Invoice conformance, Store Hub, and Cloud implementation work are deferred to later explicit PM tasks and are not delivered by these documentation tasks (`ASM-06`, `ASM-13`, `ASM-18`).

### `P-01` — Domain Model Specification (Documentation Only)
- Define all 15 logical entities (`Business` through `Lock`), key fields, relationships, constraints, and an ASCII entity-relationship diagram in `docs/domain-model.md`.
- Express `D9`–`D12`, store isolation, and multi-device `D13` as numbered testable statements, each linked to a concrete Given/When/Then acceptance scenario.
- Specify 67 scenarios in total without writing C#, creating a physical database schema, copying extraction code, or claiming unimplemented concurrency/security behavior.
- Record conservative modeling choices and new open product questions in `docs/DECISIONS.md` under `ASM-06`–`ASM-12` and Open items 5–6.

### `P-02` — Sync / Replication Protocol Specification (Documentation Only)
- Specify owner-created QR pairing, device sessions/revocation, idempotent operation envelopes with `base_version`, D11 locks, and D7 Hub-off behavior over LAN.
- Specify an append-only per-store operation log with writer fencing, ordered atomic ingest, idempotent duplicates, gap detection, durable retries, and backpressure.
- Specify OTP plus second-factor restore, deterministic replay, canonical replayed-state hashing, new-writer activation, and explicit unsynced-disk-loss behavior.
- Provide a seven-case failure table and 34 syntactically valid, hash-checked synthetic JSON vectors in `docs/sync-protocol-test-vectors.json` for P-05/P-07.
- Record conservative choices under `ASM-13`–`ASM-17` and Open items 7–10; write no C#, physical schema, extraction implementation, secrets, or demos.

### `P-03` — Mode `B` Python CLI Adapter & Review Gate Integration
- Implement C# process adapter invoking `python -m image_extraction_adapter.cli extract --image <path> --context <path>` via `ProcessStartInfo.ArgumentList` with explicit UTF-8 `stdout`/`stderr` capture (`D1`, `D2`, `E-09`).
- Manage temporary producer-owned `--context` JSON lifecycle without leaking sensitive data or persisting invoice images inappropriately.
- Map exit codes deterministically (`0` → `AUTO` candidate held pre-issuance, `10` → operator `REVIEW` queue preserving `attention_cells`, `20` → `REJECT`, `30` → operational failure), enforcing fail-closed behavior with zero auto-repair or silent issuance.

### `P-04` — Cloud API Contract (Documentation + YAML Only)
- Define `docs/openapi.yaml` in OpenAPI 3.1 and `docs/cloud-api-contract.md` for P-05/P-07 implementers, covering Store Registration, Device Pairing, Operation Ingest, Locks, Restore, and Invoice Delivery (22 operations total).
- Specify per-device bearer credentials and rotation, scoped restore credentials, uniform `{code, message, retry_hint}` errors (51 codes), `/v1` path versioning, idempotency on all 15 mutations, rate-limit headers/defaults, and cursor pagination.
- Preserve P-01/P-02 domain and protocol shapes, Hub-only writes, 300-second configurable locks, OTP plus SEC-1 restore, explicit unsynced-loss reporting, SMS stub/public-token rules, and D14 legal gates. Cover the Cloud surfaces required by P-05 through P-09 without implementing them.
- Validate with a real OpenAPI linter at zero errors; record the exact command/result and conservative choices under `ASM-18`–`ASM-21` and Open items 11–13. Write no C#, physical schema, extraction implementation, secrets, production credentials, or demos.

### Downstream Milestone Gate Note (`P-05`–`P-17`)
- Later increments remain bound by v4.1/v4.2 amendments: `P-08` (SMS delivery — uses stub until `P-10`, requires owner provider decision before real SMS), `P-09` (customer web view — blocked on lawyer-approved `"Kandoo digital invoice"` wording per `D14`), `P-10` (local Docker Compose + portable VPS Docker kit per `D5`), `P-12` (device pairing & restore 2FA UX), and `P-16` (`SEC-1` encryption hardening). Each task executes only under its explicit PM task prompt.
