# KANDOO — PARENT PROJECT AGENT HANDOFF

This repository is the parent Kandoo project.

## Repository & Cross-Repo State (`R-0` Snapshot)

- **Date:** `2026-10-22`
- **Task:** `R-0 — Parent repo truth`
- **Parent repository (`majid-ghanbari-dost/Kandoo` — THIS REPO):**
  - **Active branch:** `arena/456e429d-kandoo` *(Arena session-bound branch corresponding to task branch `arena/r-0-parent-truth`)*
  - **Pre-`R-0` `main` baseline `HEAD`:** `192fb470bcbebb34fcda220abb9e1a63c7f12d0b`
  - **Exact `R-0` content commit `HEAD`:** `<PENDING_R0_SHA>`
  - **Stable tag:** `stable-r-0` *(points to tip of this `R-0` commit group)*
- **Digital Invoice extraction repository (`majid-ghanbari-dost/Kandoo-Digital-Invoice`):**
  - **Branch:** `arena/9dc17500-kandoo-digital-invoice`
  - **Exact `HEAD`:** `27d6d14ef02302423a903e09adb60bfe0164c8f7` *(E-00 through E-09 complete)*

## Authoritative Documentation Index

1. [`docs/MASTER_HANDOFF.md`](docs/MASTER_HANDOFF.md) — Project overview, two-repository topology, `D1`–`D14` summary, verified repository state, non-existence baseline, and next platform tasks (`P-01`–`P-04`).
2. [`docs/DECISIONS.md`](docs/DECISIONS.md) — Authoritative decision register (`D1`–`D14`), Amendments `v4.1` and `v4.2` (`SEC-1`, Restore Second Factor, Agreement Truthfulness, `E-01b`, `ACC-0` parked), `Assumptions`, and `Open` owner items.

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
- **Zero C#, Windows, or Android source code** exists yet in this repository or any owner repository (platform build starts in `P-01`).
- Unrelated external demo artifacts (`auth.py` with fixed password, demo inspection scripts, tool-state JSON files, legacy bootstrap scripts) and secrets are **strictly excluded** from this repository.
- Do not invent missing files or history.
