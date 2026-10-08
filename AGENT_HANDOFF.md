# KANDOO — PARENT PROJECT AGENT HANDOFF

This repository is the parent Kandoo project.

## Repository
majid-ghanbari-dost/Kandoo

## Project relationship
The dedicated Digital Invoice development line is maintained separately in:
majid-ghanbari-dost/Kandoo-Digital-Invoice

The Digital Invoice repository is conceptually a development branch/line of this parent project, but it is kept separate during active development to avoid contaminating the parent repository with unrelated or unfinished changes.

## Mandatory Handoff Rule

At the end of every meaningful project conversation/change affecting the parent project:
- update this handoff;
- update relevant authoritative project files;
- commit the state;
- record the exact HEAD and significant changes.

## Scope

This repository is for the complete Kandoo platform and future integration of the Digital Invoice line.

Do not copy Digital Invoice implementation here merely for convenience. Integration must happen through an explicit, reviewed synchronization/merge decision.

## Current known project principles

Kandoo is a city-scale digitalization platform. Digital Invoice is an important entry/module line, not the whole product.

Core architecture includes:
- Business → Store → Users/Devices
- offline-first local SQLite + Outbox
- Cloud API + PostgreSQL
- OperationId idempotency
- Global Revision synchronization cursor
- AggregateVersion/OCC
- no silent overwrite
- ledger-based inventory
- Canonical Invoice source-independent model
- external capture through Canonicalization Gate
- no silent creation of Sale/Inventory/KPI from external captures

## Repository Bootstrap

This repository was newly created empty except for its initial README.
The actual parent-project source tree must be transferred from the authoritative working project before this repository is treated as a complete source mirror.

Do not invent missing files or history.
