# Kandoo technical decision record

**Status:** P-03 documentation-only technical baseline
**Date:** 2026-10-24
**Scope:** Decisions and verification plans only. No UI, storage, Hub, Cloud, or device prototype was run for this document.

## Evidence boundary

The measured evidence for the implementation is the GitHub Actions result: restore, compile, and test jobs must be green on the declared runner matrix. **CI green is measured evidence; local performance numbers are NOT measured yet.** No prototype ran for these decisions, so this document makes no claims about UI responsiveness, storage throughput, battery use, memory consumption, startup time, device compatibility, or production availability.

## 1. CI-based verification

**Decision**

Use GitHub Actions as the authoritative .NET verification environment. The solution is restored, built, and tested on `ubuntu-latest` and `windows-latest`; tests emit TRX files and upload them as artifacts. Each task is reviewed against a commit/tag and its associated run.

**Rationale**

The repository must be verifiable without assuming that an agent or contributor has a local .NET SDK, Windows installation, or Android device. A matrix catches platform-specific compiler, path, globalization, and test behavior. TRX artifacts preserve test-level evidence instead of relying on a screenshot or a local claim.

**Alternatives considered**

- **Local-only verification:** lower initial setup, but unavailable SDKs and OS-specific behavior make the result non-reproducible.
- **Linux-only CI:** faster and simpler, but would not exercise the Windows target or Windows-specific path behavior.
- **A self-hosted runner:** could provide specialized hardware later, but adds ownership, security, availability, and credential responsibilities before they are needed.

**Risks**

Hosted runners do not represent every store PC or Android handset. A green build can conceal poor performance, UI defects, unavailable emulator coverage, or a workflow that omits a project. Artifact retention and runner image changes also affect reproducibility.

**The test that would prove it wrong**

Add a deliberately failing platform-specific test or remove a test project from the solution. The relevant matrix job must turn red and still upload available TRX diagnostics. Separately, a project-coverage check must fail if a production project is not built by CI.

## 2. Project layout: shared core plus hosts

**Decision**

Keep `Kandoo.Core` as a pure .NET 8 class library containing domain rules, canonical JSON, hashing, and validation. Put the ASP.NET Core Cloud host in `Kandoo.Cloud`; add a Hub/host and Windows and Android clients as separate projects that reference the Core project. Keep test projects beside their corresponding production projects and include every project in `Kandoo.sln`.

**Rationale**

A project boundary makes the no-file/no-network/no-database Core rule reviewable and allows the Hub and Cloud to use the same hash and validation behavior. Host-specific concerns—HTTP, storage, UI, credentials, and device APIs—remain outside the Core. A solution-level CI build reduces the chance of an unverified project.

**Alternatives considered**

- **One monolithic application:** fewer projects initially, but domain rules become coupled to transport and UI and are easier to bypass.
- **Copy shared source into each host:** apparently independent deployment, but invites divergent numbering, quantity, and hash implementations.
- **A separately published Core NuGet package:** useful for independent consumers later, but adds versioning and publication overhead before there is an external consumer.

**Risks**

An overly large Core can become a hidden service locator or acquire infrastructure dependencies. Project references can also make accidental breaking changes affect every host. A project boundary alone does not prove thread safety or correct persistence transactions.

**The test that would prove it wrong**

CI dependency and API checks must reject `System.IO`, network, database, UI, and Cloud references from `Kandoo.Core`. The same P-02 vectors and domain tests must pass through every host-facing Core consumer; a host-specific hash or numbering divergence must fail the build.

## 3. UI approach

**Decision**

Use a shared .NET MAUI application layer for the first Windows/Android client direction, with small platform-specific adapters for capabilities such as print capture, secure storage, and device integration. Keep workflows and domain calls in shared view models/services rather than duplicating business rules in views. This is a direction for a future client task, not a claim that a MAUI prototype exists.

**Rationale**

D6 requires one application architecture across Windows and Android. MAUI offers a single C# project structure and access to native APIs while allowing the Core and application contracts to remain UI-independent. Platform adapters preserve an escape hatch for Windows print-spooler behavior and Android-specific lifecycle or storage APIs.

**Alternatives considered**

- **WinUI/WPF plus a native Android app:** potentially tighter platform control, but two UI implementations increase duplicated workflow and validation effort.
- **Blazor Hybrid or a web UI wrapped for devices:** shared web UI skills and markup, but adds webview/runtime constraints and still needs native print and offline adapters.
- **Separate native Windows and Android implementations without a shared application layer:** maximizes platform customization, but has the highest long-term consistency cost.

**Risks**

MAUI framework support, control behavior, accessibility, startup cost, and device-specific limitations may differ between Windows and Android. A shared UI can make platform-specific print/capture workflows awkward. No performance or usability result is measured here, and the choice may need revision after a real device prototype.

**The test that would prove it wrong**

A future CI/device matrix must build and run shared workflow tests on Windows and Android, including offline Hub-unavailable behavior, accessibility smoke tests, and print-adapter contract tests. If a required Windows/Android workflow cannot be implemented without forking business rules or missing a hard acceptance test, the UI decision must be reopened.

## 4. Local storage

**Decision**

Use SQLite for the authoritative Store Hub local database and its transactional outbox. Keep SQLite and migrations in the Hub infrastructure project, not in `Kandoo.Core`. Use PostgreSQL for Cloud persistence per D2/D5, with the Core remaining storage-agnostic. Device-local caches, when needed, are subordinate to Hub authority and never become a peer ledger.

**Rationale**

SQLite fits a single-Hub, offline-first store installation and supports transactions for operation records, domain effects, numbering, locks, and outbox append. It avoids requiring a separate database service on a store PC. PostgreSQL matches the Cloud hosting and portability decisions. Separating storage from Core lets pure tests prove domain behavior without a database.

**Alternatives considered**

- **JSON or flat files:** easy to inspect, but difficult to make concurrent, transactional, migration-safe, and crash-resilient for the operation ledger.
- **An embedded document database:** convenient object storage, but adds a second persistence model and requires equally strong transaction and migration evidence.
- **A local PostgreSQL server on every Hub:** operational consistency with Cloud, but increases installation and maintenance complexity for a single-store Hub.

**Risks**

SQLite locking, filesystem behavior, backup handling, encryption/key management, schema migrations, and disk-full conditions can still cause outages or data loss. The Core does not solve durable transaction boundaries by itself. No throughput, recovery-time, or storage-size measurements exist yet.

**The test that would prove it wrong**

A future Hub integration suite must kill and restart the Hub during concurrent issue/outbox commits, then prove atomic recovery, contiguous numbering, idempotent replay, and no unacknowledged-outbox loss on the supported filesystems. A migration and restore test must pass on a fresh local Docker Compose environment using synthetic data. If those guarantees cannot be met, the storage decision must be revisited.

## 5. Sharing Core between Hub and Cloud

**Decision**

Have both Hub and Cloud reference `Kandoo.Core` directly and call the same canonical JSON, SHA-256, quantity, operation-identity, and validation APIs. The Cloud must verify supplied canonical bytes and hashes rather than re-execute a Hub command or invent a different quantity/display-number representation. Transport DTOs and persistence mappings stay in the host projects.

**Rationale**

A single implementation removes a common cross-language/cross-host source of drift and makes the P-02 golden vectors meaningful at both ends of replication. It also lets C-03's ingest endpoint use exactly the hashing behavior tested by C-02 without copying code.

**Alternatives considered**

- **Independent Hub and Cloud implementations:** permits separate optimization, but requires a much larger compatibility suite and risks subtle canonicalization differences.
- **A shared source package or generated code:** can work across unlike runtimes, but adds build-generation and debugging complexity not needed for two .NET hosts.
- **Cloud-only validation:** simplifies the Hub, but permits invalid or divergent data to reach the replication boundary.

**Risks**

A future host with a different runtime cannot directly reference the .NET assembly. Serializer defaults outside the Core can still change DTOs, and an incorrect host transaction can undermine an otherwise correct hash. Versioning must be explicit if the Core is later consumed independently.

**The test that would prove it wrong**

Run all 34 P-02 vectors through Core and Cloud ingest in CI, including the intentional `CLOUD-007` mismatch, and compare canonical bytes and hashes rather than only logical deserialized values. A host that alters a quantity, key order, entry hash, or issued-only numbering result must fail the compatibility suite.

## 6. What remains unmeasured

This document does not measure local or device performance, MAUI usability, SQLite throughput/recovery, Cloud latency, Android compatibility, Windows print-spooler behavior, production security, or deployment portability. Those require future prototypes and task-specific integration tests. Until then, the honest evidence boundary is the green GitHub Actions build/test result and its TRX artifacts, not an inferred performance or production claim.
