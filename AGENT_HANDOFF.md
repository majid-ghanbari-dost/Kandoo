# KANDOO — PARENT PROJECT AGENT HANDOFF

This repository is the parent Kandoo project.

## Current Repository State (`P-02` Snapshot)

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
2. [`docs/domain-model.md`](docs/domain-model.md) — `P-01` logical domain specification: 15 entities, relationships and constraints, ASCII diagram, numbered `D9`–`D13`/store-isolation rules, and 67 concrete acceptance-test scenarios.
3. [`docs/sync-protocol.md`](docs/sync-protocol.md) — `P-02` LAN, replication, restore, writer-fencing, and failure protocol with D7/D11/SEC-1 traceability.
4. [`docs/sync-protocol-test-vectors.json`](docs/sync-protocol-test-vectors.json) — 34 synthetic P-05/P-07 vectors with canonical SHA-256 fixtures.
5. [`docs/DECISIONS.md`](docs/DECISIONS.md) — Authoritative decision register (`D1`–`D14`), Amendments `v4.1` and `v4.2` (`SEC-1`, Restore Second Factor, Agreement Truthfulness, `E-01b`, `ACC-0` parked), `Assumptions`, and `Open` owner items.

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
- Per-phone `DRAFT` lock per device with configurable `5-minute` default timeout (`D11` v4.2)
- Store Hub sequential `display_number` separate from internal `invoice_id` and external POS numbers (`D12`)
- Event-ledger inventory and store-unique normalized products with near-duplicate warnings (`D9`, `D10`)
- Canonical Invoice source-independent model (`ISSUED` immutable; `REVOKED` / `SUPERSEDED` terminal states)
- External capture strictly through Canonicalization Gate (`AUTO` / `REVIEW` / `REJECT`), with fail-closed `REVIEW` on any doubt and zero silent creation of `Sale` / `Inventory` / `KPI`
- Server-side encryption + restricted logged access for MVP (`SEC-1`), second factor required on store restore, lawyer-owned `"Kandoo digital invoice"` copy (`D14`), and truthfulness between agreements and tested code

## Repository Truth at `R-0`

- Prior to `R-0`, this repository contained only 3 bootstrap documentation commits (`80d967c`, `729b10a`, `192fb470`) with `README.md` and `AGENT_HANDOFF.md`.
- **Zero C#, Windows, or Android source code** exists yet in this repository or any owner repository. The current `P-01` task is documentation-only; implementation requires a later explicit PM task (`ASM-06`).
- Unrelated external demo artifacts (`auth.py` with fixed password, demo inspection scripts, tool-state JSON files, legacy bootstrap scripts) and secrets are **strictly excluded** from this repository.
- Do not invent missing files or history.
