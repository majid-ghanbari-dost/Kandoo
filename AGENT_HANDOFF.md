# KANDOO — PARENT PROJECT AGENT HANDOFF

## Current Implementation Handoff (`C-09`, 2026-10-25)

C-08 is complete and authoritative at `776ef59d4f594410f6d72f3ec970eb539a66b83b` with annotated `stable-c-08-v4` (`4413d13c97a4a54ab1e6aa9af9205365171980ed`). Run `40170402686` is green on `ubuntu-latest` and `windows-latest`; earlier C-08 tags remain immutable audit history. C-09 starts from that verified commit. It adds `ISmsProvider`, a deterministic no-network/no-secret stub, Persian single-invoice-link templates, interface-backed subscription metadata, PostgreSQL operation-ID reservations, per-store quota/counters, and per-invoice/per-recipient abuse limits. Tests cover template content, durable retry without duplicates, quota exhaustion, rate-limit retry metadata/window recovery, recipient abuse, operation fingerprint conflicts, and cross-store link rejection. Target tag: `stable-c-09`. CI is pending at candidate publication; final pushed SHA, tag object, and per-OS run URL are reported in the dispatch result. Any red CI requires a fix and new tag.

## Previous Implementation Handoff (`C-08`, 2026-10-25)

The active branch remains `arena/1c5ae19c-kandoo`; C-08 starts from the independently verified CI-green commit `3af5c898743451ff4574c45374b5b7dd9a077832`. `main` remains at `82d40cf7d55ac170db7a464dad5985be75d6b71a`. The C-08 candidate adds the ASP.NET Core Hub API, persistent companion-only QR grants/device registry/hashed credentials and sessions, owner-only pairing/revocation, disconnect and D11 lock handling, durable rejected responses/outbox entries, and C-04-backed operation idempotency. The in-process `WebApplicationFactory` suite executes all 16 `LAN-PAIR`/`LAN-SESSION`/`LAN-OP`/`LAN-LOCK` vectors on temporary SQLite databases; client-only `LAN-HUB-001` is intentionally outside the Hub harness. The first candidate was tagged `stable-c-08` at `979749d85abf11467a1b80c85dbe17c27e0eb27a`; run `40169889363` was red on both OS legs because Minimal API DTO binding did not yet use the snake-case JSON profile. That tag remains immutable. The second candidate, `stable-c-08-v2` at `668403657b40f7b31961c8a21e921d262949e352`, configured ASP.NET HTTP JSON binding correctly but run `40170080773` was red on both OS legs because the test factory still shared the top-level default SQLite database across parallel vectors. That tag also remains immutable. The third candidate, `stable-c-08-v3` at `d83b4fa219f1d4dbcf886aa135b81373ad2f7cf5`, isolated each test database correctly; run `40170217533` remained red on both OS legs for two fixed-invoice-ID harness bindings and one real D11 read-expiry defect. That tag remains immutable. The v4 candidate targets `stable-c-08-v4`: follow-up commands bind to Hub-generated invoice IDs, and `DraftLockManager.Get` now evaluates exact-boundary expiry. CI is pending at v4 publication; final pushed SHA, tag object, and per-OS run URL are reported in the dispatch result. Never move a published tag; any further red CI requires another fix and new tag.

## Previous Implementation Handoff (`H-00`, 2026-10-24)

This section supersedes the older documentation-only snapshots below for the active implementation line. The active Arena branch is `arena/1c5ae19c-kandoo`; `main` remains at baseline `82d40cf7d55ac170db7a464dad5985be75d6b71a`. The C-03 starting tip for the next dispatch is `fca2262aeaa6dbfedd2c1f3d0107bd0ce147aa62`.

| Task | Content commit | Annotated tag (object SHA) | CI run ID |
| --- | --- | --- | --- |
| **C-01** | `5ff98a5b52819af8ba215942532cdf2a3c6f565c` | `stable-c-01` (`4394f7b19a476ff4769b468f1decb7f91dc41153`) | `40068393877` |
| **C-02** | `9a2581ed53d57575343bb1346575b11ab8c8fdfa` | **`stable-c-02-v4` (`9cca13b309fcf169e8aadc09a4cecc63fe9c42be`)** | `40069226292` |
| **P-03-docs** | `f071070c5a7a835a104f5c3eb31859a1ffdae121` | `stable-p-03-docs` (`48c58d6cc921021dc3d20d22951c3270b0da31a6`) | `40069315245` |
| **C-03** | `fca2262aeaa6dbfedd2c1f3d0107bd0ce147aa62` | `stable-c-03` (`a97767356019c17dcbfa00477ebfdb7414faa608`) | `40069741771` |

**C-02 tag authority:** `stable-c-02-v4` is the authoritative, corrected C-02 tag. The earlier `stable-c-02` tag points to pre-fix commit `2c8a5796970d4e5bc159f264d1f2bed218894413` and **must not be moved**. Earlier C-02 fix tags remain immutable audit history.

The current dispatch CI loop requires owner/verifier confirmation for each new task: **CI result needed from the owner**. Do not infer a new task's OS result from these historical run IDs.

This repository is the parent Kandoo project.

## Current Repository State (`P-0104r` Snapshot)

- **Date:** `2026-10-23`
- **Task:** `P-0104r — Unified documentation revision` (owner-approved; documentation and YAML only)
- **Parent repository (`majid-ghanbari-dost/Kandoo` — THIS REPOSITORY):**
  - **Active cumulative branch:** `arena/91ef8c4b-kandoo`; PR #2 remains open and unmerged.
  - **Starting `P-0104r` `HEAD`:** `ee1e17165924798339075e705a2aa45ecc60f899` (`stable-p-04` tip).
  - **Exact `P-0104r` content commit `HEAD`:** `1f9da7c1b6227c7a1332d581266f52fdce856614` *(six requested documents; immediate parent of this handoff-recording commit; final `P-0104r` commit-group tip is tagged `stable-p-0104r`)*.
  - **Stable tag:** `stable-p-0104r`.
  - **Delivered:** `docs/domain-model.md`, `docs/sync-protocol.md`, `docs/sync-protocol-test-vectors.json`, `docs/openapi.yaml`, `docs/cloud-api-contract.md`, and `docs/DECISIONS.md` now allocate `display_number` only at successful `ISSUE`, restart the 300-second lock inactivity window only on newly Hub-accepted draft item/quantity/customer changes, and use `ASM-22` signed integer thousandths (`quantity_millis`, `quantity_delta_millis`, `stock_millis`). D11 Open item 3 and D12 Open item 6 are closed; no endpoint or implementation was added.
  - **Changed domain rule IDs:** `D10-R1`, `D10-R3`, `D11-R1`, `D11-R6`, `D11-R8`, `D11-R9`, `D11-R10`, `D12-R2`–`D12-R6`, `MD-R5`, `MD-R8`, `MD-R9`.
  - **Changed domain test IDs:** `TC-E09`, `TC-E11`, `TC-D10-R1`, `TC-D10-R2`, `TC-D10-R3`, `TC-D10-R5`, `TC-D11-R1`, `TC-D11-R3`, `TC-D11-R5`, `TC-D11-R6`, `TC-D11-R8`, `TC-D11-R9`, `TC-D12-R2`–`TC-D12-R6`, `TC-SI-R6`, `TC-MD-R5`, `TC-MD-R8`, `TC-MD-R9`.
  - **Changed vector IDs (21):** `LAN-SESSION-002`, `LAN-LOCK-001`–`LAN-LOCK-006`, `LAN-HUB-001`, `CLOUD-001`–`CLOUD-005`, `RESTORE-001`–`RESTORE-004`, `FAIL-001`, `FAIL-003`, `FAIL-004`, `FAIL-005`. The other 13 vectors are unchanged; intentional `CLOUD-007` corruption is preserved.
  - **Changed P-04 contract fields/metadata:** `Invoice.display_number` is required but nullable (`integer`/`null`, positive when issued); `PublicInvoiceView.display_number` remains required positive issued-only data with an explicit description; `LogEntry` documents the ASM-22 generic payload/outcome/response-snapshot quantity names; `info.description` records the revision; `x-contract.revision=P-0104r` and `x-contract.quantity_encoding` were added. No quantity endpoint or lock-extension endpoint was added.
  - **Hash validation:** UTF-8 canonical JSON with recursively sorted object keys, compact separators, and no ASCII escaping; request hashes use exactly `store_id`, `device_id`, `operation_id`, `operation_type`, `base_version`, `payload`; entry hashes omit only `entry_hash`; state hashes use the canonical replayed projection. Independent Python and Node.js recomputation passed 34 request-hash occurrences, 21 entry-hash occurrences, and 2 state projections, with zero failures.
  - **Other validation:** `npx --yes @redocly/cli lint docs/openapi.yaml` (`2.69.0`) exited 0 with zero errors/warnings. Structural/P-02 compatibility passed 34 vectors, 11 wire batches, 18 contained entries, 22 operations, 15 POST mutations, exactly 51 matching narrative/YAML error codes, and frozen domain counts 15 entities/52 rules/67 scenarios. `python3 -m json.tool docs/sync-protocol-test-vectors.json` and `git diff --check` passed.
  - **Assumptions/decisions:** `ASM-09` and `ASM-10` contain the owner-approved D11/D12 revisions; new `ASM-22` records the quantity encoding and rationale. D7 single-Hub authority, D10 idempotency/OCC, D8/SEC-1 restore second factor, D4 SMS-stub/public-token rules, and strict store isolation are unchanged.
  - **Not delivered:** P-03, C#, physical schema, extraction implementation, secrets, production credentials, demos, live SMS, or legal copy. P-03 was not started.

## Repository State (`P-04` Snapshot)

- **Date:** `2026-10-22`
- **Task:** `P-04 — Cloud API contract` (documentation and YAML only)
- **Parent repository (`majid-ghanbari-dost/Kandoo` — THIS REPOSITORY):**
  - **Active cumulative branch:** `arena/91ef8c4b-kandoo`; PR #2 remains open and unmerged.
  - **Starting `P-04` `HEAD`:** `2b08caa16a794311c8f50cafb40c19a30919b69f` (`stable-p-02`).
  - **Exact `P-04` content commit `HEAD`:** `8473cacbec8ce57e06e91c03da14444ece9d7ad7` *(immediate parent of this handoff-recording commit; final `P-04` commit-group tip is tagged `stable-p-04`)*.
  - **Stable tag:** `stable-p-04`.
  - **Delivered:** `docs/openapi.yaml` (OpenAPI 3.1) and `docs/cloud-api-contract.md` define 22 operations across Store Registration, Device Pairing, Operation Ingest, Locks, Restore, and Invoice Delivery; 15 mutations require `Idempotency-Key`; 51 error codes share the `{code, message, retry_hint}` envelope. The contract specifies per-device/restore credentials and rotation, `/v1` path versioning, cursor pagination, default rate limits, P-02-compatible operation/log/lock shapes, OTP plus SEC-1 restore, and D4/D14 gates.
  - **Validation:** `npx --yes @redocly/cli@2.69.0 lint docs/openapi.yaml` exited 0 with zero errors and zero warnings. Structural checks validated 22 operations, the complete error matrix, all 11 wire-shaped P-02 batches and 18 contained entries; `CLOUD-006` harness metadata and intentional `CLOUD-007` corruption were handled explicitly.
  - **Assumptions:** `docs/DECISIONS.md` records `ASM-18`–`ASM-21` and Open items 11–13.
  - **Not delivered:** C#, physical schema, API implementation, extraction code, secrets, production credentials, demos, live SMS, or legal copy (`ASM-18`).

## Repository State (`P-02` Snapshot)

- **Date:** `2026-10-22`
- **Task:** `P-02 — Sync/replication protocol specification` (documentation only)
- **Parent repository (`majid-ghanbari-dost/Kandoo` — THIS REPOSITORY):**
  - **Active cumulative branch:** `arena/91ef8c4b-kandoo`; PR #2 remains open and unmerged.
  - **Starting `P-02` `HEAD`:** `e8a935105bdfad7ab323b4a83ec83b8a4c44ecc2` (`stable-p-01`).
  - **Exact `P-02` content commit `HEAD`:** `fc4df52ad5617ebd837a07022cc2d847436a8f58` *(immediate parent of this handoff-recording commit; final `P-02` commit-group tip is tagged `stable-p-02`)*.
  - **Stable tag:** `stable-p-02`.
  - **Delivered:** `docs/sync-protocol.md` specifies LAN pairing/sessions/operations/D11 locks, ordered idempotent Cloud replication, OTP+second-factor restore with replayed-state hashing, and a seven-case failure table. `docs/sync-protocol-test-vectors.json` contains 34 unique synthetic vectors for P-05/P-07 with independently checked request, entry, and state hashes. `docs/DECISIONS.md` records `ASM-13`–`ASM-17` and Open items 7–10.
  - **Not delivered:** C#, physical schema, extraction implementation, production credentials, secrets, demos, or implementation claims (`ASM-13`).

## Repository State (`P-01` Snapshot)

- **Date:** `2026-10-22`
- **Task:** `P-01 — Domain model specification` (documentation only)
- **Parent repository (`majid-ghanbari-dost/Kandoo` — THIS REPOSITORY):**
  - **Active branch:** `arena/91ef8c4b-kandoo`.
  - **Merged `main` baseline `HEAD`:** `6409de18074f3ca71673d017ef671b51cc70c05c`.
  - **Exact `P-01` content commit `HEAD`:** `9d6d6fa1d935ded429eaa2698706daae099d70b0` *(immediate parent of this handoff-recording commit; final `P-01` branch tip is tagged `stable-p-01`)*.
  - **Stable tag:** `stable-p-01`.
  - **Delivered:** `docs/domain-model.md` defines 15 entities, an ASCII relationship diagram, 52 numbered `D9`–`D13`/isolation rules, and 67 concrete test scenarios; `docs/DECISIONS.md` records `ASM-06`–`ASM-12` and Open items 5–6; `docs/MASTER_HANDOFF.md` records the PM's documentation-only `P-01` clarification.
  - **Not delivered:** C#, database schema, extraction code, secrets, demos, or implementation claims. The previously provisional `P-01` bootstrap work is deferred to a later explicit PM task (`ASM-06`).
- **Digital Invoice extraction repository:** unchanged and not accessed for implementation; its last verified `R-0` snapshot remains `27d6d14ef02302423a903e09adb60bfe0164c8f7`.

## Repository & Cross-Repo State (`R-0` Historical Snapshot)

- **Date:** `2026-10-22`
- **Task:** `R-0 — Parent repo truth`
- **Parent repository (`majid-ghanbari-dost/Kandoo` — THIS REPO):**
  - **Active branch:** `arena/456e429d-kandoo` *(Arena session-bound branch corresponding to task branch `arena/r-0-parent-truth`)*
  - **Pre-`R-0` `main` baseline `HEAD`:** `192fb470bcbebb34fcda220abb9e1a63c7f12d0b`
  - **Exact `R-0` content commit `HEAD`:** `6e17959afb8033f3dae1a09a4db28e2cbc4059ce` *(immediate parent of this SHA-recording commit; branch tip tagged `stable-r-0`)*
  - **Stable tag:** `stable-r-0` *(points to tip of this `R-0` commit group)*
- **Digital Invoice extraction repository (`majid-ghanbari-dost/Kandoo-Digital-Invoice`):**
  - **Branch:** `arena/9dc17500-kandoo-digital-invoice`
  - **Exact `HEAD`:** `27d6d14ef02302423a903e09adb60bfe0164c8f7` *(E-00 through E-09 complete)*

## Authoritative Documentation Index

1. [`docs/MASTER_HANDOFF.md`](docs/MASTER_HANDOFF.md) — Project overview, two-repository topology, `D1`–`D14` summary, verified repository state, non-existence baseline, and next platform tasks (`P-01`–`P-04`).
2. [`docs/domain-model.md`](docs/domain-model.md) — `P-01` logical domain specification, revised by `P-0104r`: 15 entities, relationships and constraints, ASCII diagram, numbered `D9`–`D13`/store-isolation rules, and 67 concrete acceptance-test scenarios.
3. [`docs/sync-protocol.md`](docs/sync-protocol.md) — `P-02` LAN, replication, restore, writer-fencing, and failure protocol with D7/D11/SEC-1 traceability and the `P-0104r` numbering/activity/`ASM-22` profile.
4. [`docs/sync-protocol-test-vectors.json`](docs/sync-protocol-test-vectors.json) — 34 synthetic P-05/P-07 vectors with canonical SHA-256 fixtures, including the `P-0104r` issue-only numbering and scaled-quantity revisions.
5. [`docs/openapi.yaml`](docs/openapi.yaml) — `P-04` normative OpenAPI 3.1 Cloud API contract with the `P-0104r` pre-implementation revision.
6. [`docs/cloud-api-contract.md`](docs/cloud-api-contract.md) — `P-04` narrative for implementers: six endpoint groups, authentication, complete error matrix, versioning, idempotency, rate limits, P-01/P-02 mapping, `P-0104r` compatibility, and open technology questions.
7. [`docs/DECISIONS.md`](docs/DECISIONS.md) — Authoritative decision register (`D1`–`D14`), Amendments `v4.1` and `v4.2` (`SEC-1`, Restore Second Factor, Agreement Truthfulness, `E-01b`, `ACC-0` parked), `Assumptions` including `ASM-22`, and `Open` owner items.

## Project Relationship

The dedicated Digital Invoice development line is maintained separately in:
`majid-ghanbari-dost/Kandoo-Digital-Invoice`

The Digital Invoice repository handles Mode `B` local Python image extraction (`E-00`–`E-09`) and communicates with this parent C# (`ASP.NET Core`) platform **exclusively** through:
1. **Canonical Invoice v0 JSON contract** (`schema_version = "0"`), and
2. **Local CLI invocation** (`python -m image_extraction_adapter.cli extract`).

Do **not** copy Python implementation code from `Kandoo-Digital-Invoice` into this repository.

## Mandatory Handoff Rule

At the end of every meaningful project conversation/change affecting the parent project:
- update this handoff;
- update relevant authoritative project files (`docs/MASTER_HANDOFF.md`, `docs/DECISIONS.md`);
- commit the state;
- record the exact `HEAD` and significant changes.

## Current Known Project Principles

Kandoo is a city-scale digitalization platform. Digital Invoice is an important entry/module line, not the whole product.

Core architecture includes:
- `Business → Store → Users / Devices`
- Two operating modes: Mode `A` (Kandoo is store software) and Mode `B` (Kandoo runs beside existing POS software via print capture)
- Offline-first local Store Hub (`SQLite` + Outbox) on LAN (`D7`)
- `ASP.NET Core` Cloud API + `PostgreSQL` (`D2`, `D5`)
- `operation_id` idempotency + `device_id` attribution on every mutation (`D10`)
- Global Revision synchronization cursor
- `AggregateVersion` / OCC (`base_version`) with zero silent overwrites (`D10`)
- Per-phone `DRAFT` lock per device with configurable `300-second` default timeout; only newly Hub-accepted draft item/quantity/customer changes restart inactivity, never passive screens/heartbeats (`D11` v4.2 + `P-0104r`)
- Store Hub sequential `display_number` separate from internal `invoice_id` and external POS numbers; allocation occurs only at successful `ISSUE`, drafts remain null-numbered, and issued numbers are gapless (`D12` + `P-0104r`)
- Event-ledger inventory and store-unique normalized products with near-duplicate warnings (`D9`, `D10`); quantities use signed integer thousandths (`quantity_millis`, `quantity_delta_millis`, `stock_millis`) under `ASM-22`
- Canonical Invoice source-independent model (`ISSUED` immutable; `REVOKED` / `SUPERSEDED` terminal states)
- External capture strictly through Canonicalization Gate (`AUTO` / `REVIEW` / `REJECT`), with fail-closed `REVIEW` on any doubt and zero silent creation of `Sale` / `Inventory` / `KPI`
- Server-side encryption + restricted logged access for MVP (`SEC-1`), second factor required on store restore, lawyer-owned `"Kandoo digital invoice"` copy (`D14`), and truthfulness between agreements and tested code

## Repository Truth at `R-0`

- Prior to `R-0`, this repository contained only 3 bootstrap documentation commits (`80d967c`, `729b10a`, `192fb470`) with `README.md` and `AGENT_HANDOFF.md`.
- **Zero C#, Windows, or Android source code** exists yet in this repository or any owner repository. The current `P-01` task is documentation-only; implementation requires a later explicit PM task (`ASM-06`).
- Unrelated external demo artifacts (`auth.py` with fixed password, demo inspection scripts, tool-state JSON files, legacy bootstrap scripts) and secrets are **strictly excluded** from this repository.
- Do not invent missing files or history.
