# KANDOO — PARENT PROJECT AGENT HANDOFF

## Window-3 Final Handoff (`H-02 v3`, 2026-10-30)

This final documentation-only ledger starts from H-02 v2 at `f6d6ac22bef22e14e7295cde9ef13985d7124a9b` / `stable-h-02-v2` (`73b69702eb41c11c6d081b49a504ee61817321dc`). C-15 is certified GREEN by the executive's live read; the former inferred qualification is obsolete.

| Task / candidate | Full commit SHA | Annotated tag / tag object | PM-relayed CI verdict |
| --- | --- | --- | --- |
| C-14 v1 (retained, superseded) | `9aa6383ffad88e9544e493ca055fcce4e5308153` | `stable-c-14` / `c3a4a1f87742b2a0200aa907432277f82e4391ec` | [40838199654](https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/40838199654) — RED; retained superseded catalog-fixture candidate |
| **C-14 final v2** | `33ef8209f074f17fd1fe740517318889e1085bdc` | `stable-c-14-v2` / `6985f9b1a192d8c55ade7813fa945b856901a881` | [40838323344](https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/40838323344) — GREEN |
| **C-15** | `2bf8614d277366323dd689f43790816eb93223d2` | `stable-c-15` / `454c335c5964e654f537000e6ac3904c0c2ebbe1` | [40838490817](https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/40838490817) — **CERTIFIED GREEN** on both jobs (push, created 2026-10-29T07:54:55Z), read live by the executive; the inferred qualification is obsolete; both test-result artifacts and `kandoo-hub-c14-win-x64` at 47,944,324 B uploaded |
| **C-16** | `271730d1bef6f1ee4db174ee3bcdd6b073780bf0` | `stable-c-16` / `441c05dfd7b6e3c745f0ff777b2e903c809ced92` | [40838766492](https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/40838766492) — GREEN; `kandoo-hub-c14-win-x64` = 47,955,575 B |
| C-18 v1 (retained red) | `4da3cb651499f39b09a99501013d0af3e9906416` | `stable-c-18` / `d5492573ab9a389460d6f4ed63e7a96c17f1ca26` | [40838999159](https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/40838999159) — RED, Build on Ubuntu and Windows; unresolved `IClock` |
| **C-18 final v2** | `cbff8a5465e4e565641992aa67b050798305d830` | `stable-c-18-v2` / `85b966caf69e258fd48972bab48bd75985c04ce9` | [40858174391](https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/40858174391) — GREEN on both jobs (push, created 2026-10-29T10:31:02Z); the one-import fix; the FIRST fully-green full-suite run on this branch |
| **C-19** | `7544f9bbc2de33c9b13eebcf2f1068e8dbb4eb64` | `stable-c-19` / `accc30983f4aef0e55dffde058f727da16286219` | [40839617820](https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/40839617820) — RED with the same inherited C-18 compile error; no additional C-19 compile error was relayed |
| H-02 v1 (retained red) | `c089dc3bf5b792aabb9d3a37683dd67548480490` | `stable-h-02` / `58dcd04ac83ca66335d117baeebb80c3ab6a1aa0` | [40839664353](https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/40839664353) — RED with the same inherited compile error; documentation-only tip |
| **H-02 v2** | `f6d6ac22bef22e14e7295cde9ef13985d7124a9b` | `stable-h-02-v2` / `73b69702eb41c11c6d081b49a504ee61817321dc` | [40858274525](https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/40858274525) — GREEN (push, created 2026-10-29T10:31:47Z); [40862607886](https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/40862607886) — GREEN on the merge ref (pull_request #5, created 2026-10-29T11:05:03Z, 4/4 checks; PR #5 created by the PM via compare URL, create-only, never merged) |
| **H-02 v3 (this publication)** | commit SHA: published in the execution report (a commit cannot contain its own SHA) | `stable-h-02-v3` / tag object SHA: published in the execution report | PR #5 run on the new head: PENDING VERDICT (awaiting executive relay) |

C-18/C-19 tests HAVE executed green: run #63 was the first fully-green full-suite execution on this branch (both jobs, including `Kandoo.Capture.Tests`); #64/#65 confirmed it. The two real-SPL/SHD compatibility tests remain unchanged, skipped, and BLOCKED by absent owner-provided samples; no capture was fabricated. Nothing was pushed to `main`, no existing tag was moved, and no merge or UI work occurred. The H-02 v3 commit and tag object SHAs are published in the execution report because a commit cannot contain its own SHA.

## Window-3 Fix-Forward Handoff (`C-18 v2`, 2026-10-29)

PM relayed run `40838999159` as RED on both operating systems at Build for C-18 `4da3cb651499f39b09a99501013d0af3e9906416`; C-19 run `40839617820` and H-02 run `40839664353` inherited the same single unresolved-`IClock` error. Source verification against the clean published tree found that `Kandoo.Core.IClock` already exists, `SystemCloudClock` already implements it, and `Program.cs` already injects that implementation into `PublicInvoiceRateLimiter`. The attributable defect is only the missing `Kandoo.Core` import in `PublicTokenSecurity.cs`; v2 adds that import and does not duplicate the clock contract or touch any C-19 file. All existing tags remain immutable. Target tag: `stable-c-18-v2`; its CI run URL is `relayed after batch`, and no test execution is claimed before PM relay.

## Window-3 Implementation Handoff (`C-19`, 2026-10-29)

C-18 is published at `4da3cb651499f39b09a99501013d0af3e9906416` / `stable-c-18`. C-19 moves the supplied probe's raw spool capture, stable-scan/baseline behavior, OpenXPS Persian glyph reconstruction, and ESC/POS GS-v-0 raster/PNG reconstruction into a new `Kandoo.Capture` .NET 8 library. The Hub registers capture through standard `Hub:Capture` configuration and an opt-in hosted monitor; each stable SPL/SHD file is copied by content hash with an atomic REVIEW-only manifest. Raw captures do not create canonical invoices, customers, sales, or other native effects. Four active synthetic tests cover XPS reconstruction, raster pixels/PNG output, stable hashed spool handoff, and startup-baseline/change detection; a Hub test covers the REVIEW manifest and idempotent sink. Two tests are explicitly BLOCKED/skipped because owner-provided real SPL and SHD capture samples are absent; no captures are fabricated and no real-spool compatibility is claimed. Capture builds and tests run in both CI jobs. Target tag: `stable-c-19`; CI run URL is `relayed after batch`. Local .NET/PowerShell execution remains unavailable, so no local or CI success is claimed.

## Window-3 Implementation Handoff (`C-18`, 2026-10-29)

C-16 is published at `271730d1bef6f1ee4db174ee3bcdd6b073780bf0` / `stable-c-16`. C-18 hardens `/i/{token}` without changing its public-invoice contract: public lookups use a SHA-256 token-hash index and `CryptographicOperations.FixedTimeEquals` on full hashes; unknown, malformed, and unauthorized capabilities collapse to the same 404 body/headers; a fixed-window per-client-address limiter returns bounded retry metadata; and ASP.NET request/routing diagnostics that could disclose raw token paths are disabled. Existing raw tokens remain only in trusted server-side delivery/claim storage. Four tests cover fixed-time comparison behavior, identical unknown/unauthorized responses, per-address window recovery, and absence of valid/unknown tokens from captured server logs. Target tag: `stable-c-18`; CI run URL is `relayed after batch`.

## Window-3 Implementation Handoff (`C-16`, 2026-10-29)

C-15 is published at `2bf8614d277366323dd689f43790816eb93223d2` / `stable-c-15`. C-16 adds a SQLite-backed Mode-B REVIEW queue around the strict C-07 consumer. Acceptance has no native sale/customer effect; captured attention cells plus synthetic operator-phone/payable cells are listed; allowed value corrections and explicit phone entry update the preserved source provenance to `operator_confirmed` and append audit rows. Payable is recomputed with checked C# signed-integer arithmetic as line totals plus only declared `summary.extra_amounts`; ACC-0 tax/discount/rounding semantics are not inferred. Issue requires an empty captured-attention list, explicit normalized operator phone, and a matching sum, then uses the same draft/item/ISSUE operation service with deterministic retry IDs, external capture identity, D11 locking, and display-number allocation. Four golden-JSON tests cover clean issue, corrected value/provenance, failing-sum refusal before native operations, and missing-phone refusal. Target tag: `stable-c-16`; CI run URL is `relayed after batch`.

## Window-3 Implementation Handoff (`C-15`, 2026-10-29)

C-14 is complete at `33ef8209f074f17fd1fe740517318889e1085bdc` with annotated `stable-c-14-v2`; initial `stable-c-14` remains immutable. C-15 adds the R1 vertical slice through the real Hub HTTP API, durable SQLite operation/outbox log, real Cloud ingest/PostgreSQL, deterministic SMS stub and durable quota counters, and the public token page. Native Hub ISSUE responses now retain the immutable issued payload/hash, timestamp, external identity, and lines; item add validates an optional same-store product ID. Cloud projects only complete successful native ISSUE responses into the single-invoice public projection and never invents missing legacy details. Four serial PostgreSQL tests cover the full product → phone draft → item → issue-at-number-1 → four-entry log → replicate → one SMS charge → public invoice run, idempotent operation/Cloud/SMS retries, store-A/store-B denial, and second-device phone-lock conflict. Target tag: `stable-c-15`; CI run URL is `relayed after batch`.

## Window-3 Implementation Handoff (`C-14`, 2026-10-29)

C-14 starts from the executive-verified PR #4 merge `1dd53ff74c091f71e35606c3e1254049c1ffc16a` on `arena/23ca21c9-kandoo`. The existing ASP.NET Core Hub executable is now the runnable console host: SQLite remains on disk, standard appsettings/environment configuration is used, loopback-only pages expose the latest explicitly issued pairing grant, the same payload is printed as `KANDOO_PAIRING_GRANT=...`, authenticated product creation is available over HTTP, and WebApplication lifetime handlers provide graceful shutdown with SQLite disposal. A disabled-by-default loopback shutdown switch exists only for the smoke harness. Windows CI publishes a self-contained `win-x64` folder, starts the executable, pairs a device, creates a stocked product, creates a phone draft, adds an item, issues display number 1, stops the host gracefully, measures the uncompressed publish bytes/MiB into `c14-artifact-size.txt` and the step summary, and uploads the publish plus smoke result. Two in-process tests cover local pairing display and durable idempotent product creation. The initial candidate was published as `9aa6383ffad88e9544e493ca055fcce4e5308153` / `stable-c-14`. Before C-15, static integration review found that the in-process test factory replaced the operation store but not the newly registered catalog service; v2 replaces both so the product test uses its isolated SQLite database. The production host was already wired to one store. Target final tag: `stable-c-14-v2`; the v1 tag remains immutable. CI run URLs and the CI-measured artifact size are recorded in the SECTION C evidence block below, copied byte-for-byte from the executive relay. No real-capture samples, UI work, merges, or main pushes are included.

### C-14 v2 Windows smoke evidence

```text
SECTION C — C-14 v2 WINDOWS SMOKE EVIDENCE (record VERBATIM; never re-derive)
Source: Actions run #57, id 40838323344, head 33ef8209f074f17fd1fe740517318889e1085bdc
(stable-c-14-v2, "test(c-14): isolate the host catalog fixture"), event push, created
2026-10-29T07:53:23Z, conclusion SUCCESS. All figures below are verbatim from the CI
run/job pages, fetched 2026-10-30 by the executive. Nothing derived, rounded, or invented.

Jobs:
  Build and test (ubuntu-latest)  job 123328319284  success  duration 1m 14s
  Build and test (windows-latest) job 123328319640  success  duration 2m 43s
  Run page header: 2m 47s, Artifacts 3

Windows job steps (all conclusion=success; timestamps UTC):
  #10 "Publish self-contained win-x64 Hub host"
      started 2026-10-29T07:55:44Z, completed 2026-10-29T07:55:55Z
  #11 "Start Hub, run C-14 smoke, stop it, and measure the publish"   <-- SMOKE RESULT
      started 2026-10-29T07:55:55Z, completed 2026-10-29T07:55:58Z
  #12 "Upload C-14 self-contained Hub artifact"
      started 2026-10-29T07:55:58Z, completed 2026-10-29T07:56:03Z

Artifacts (run page table, verbatim Name | Size | Digest):
  kandoo-hub-c14-win-x64       45.7 MB  sha256:d5384482d1e41899f18e5e2e5df06431974c9d41b57cd930fe98795c29bc3772
  test-results-ubuntu-latest   23.1 KB  sha256:a6144525343480defcc262d7cfafb6799015852804eb9c5c7840c277a9a7ff6c
  test-results-windows-latest  23.1 KB  sha256:2feb0c1b8e7dfdc8de13bc6b8f15cf1d13af61bebbf736237f7e76e5206909a2

Not supplied (never invent): the byte-exact size_in_bytes of kandoo-hub-c14-win-x64 at
this run (API quota drained; logs require auth). "45.7 MB" is exactly as displayed on
the run page. If the executive later relays an exact byte figure, record it in the next
handoff update. Never compute, round, or infer it.

Annotations (run page, verbatim; non-blocking warnings):
  - Build and test (ubuntu-latest) Node.js 20 is deprecated. The following actions target
    Node.js 20 but are being forced to run on Node.js 24: actions/checkout@v4,
    actions/setup-dotnet@v4, actions/upload-artifact@v4. For more information see:
    https://github.blog/changelog/2025-09-19-deprecation-of-node-20-on-github-actions-runners/
  - Build and test (windows-latest) Node.js 20 is deprecated. (same action list and link)
  - Build and test (ubuntu-latest) "The ubuntu-latest label will migrate to Ubuntu 26
    beginning October 19, 2026. For more information, see
    https://github.com/actions/runner-images/issues/14748 "
```

## Final Implementation Handoff (`H-01`, 2026-10-26)

C-10 through C-13 are complete on the fixed review-only Arena branch. The authoritative implementation line ends at C-13 v2. Every candidate SHA, annotated tag/object, per-OS result, and run URL is recorded below; red tags remain immutable and were never moved.

| Task / candidate | Commit SHA | Annotated tag / tag object | Ubuntu | Windows | CI run |
| --- | --- | --- | --- | --- | --- |
| **C-10 final** | `dbbf2511ff78dd6d154cc1b12cf99bf821eeecac` | `stable-c-10` / `0d02b9634d2fc095141f605dcffb6c2026cf7eb4` | Green | Green | [40293083629](https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/40293083629) |
| C-11 v1 (retained red) | `58c1751ad4ed8e2c85d1a0ef1ca30cd6931f2619` | `stable-c-11` / `edada80faabd9baa88c763a8b9970855ca9e79ac` | Red — Build | Red — Build | [40293860657](https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/40293860657) |
| **C-11 final v2** | `582a2cfd4876b38b85619f2690402ade6dfe0e72` | `stable-c-11-v2` / `d07001ab10ef76ffb8b58b991ad28ffb3c30019c` | Green | Green | [40294075398](https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/40294075398) |
| C-12 v1 (retained red) | `45ce927df4786ab3f165df88ad2c3076d0c57f87` | `stable-c-12` / `f9a9a9a16d19483d5329d6adfddb2050349fec60` | Red — Build | Red — Build | [40295770215](https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/40295770215) |
| C-12 v2 (retained red) | `90a92ab4207c78bdf5b9cc111ea1fa7f1aedded4` | `stable-c-12-v2` / `d5107297b95b006a23d156eb4b46c43219983313` | Red — Cloud tests | Red — Cloud tests | [40296029468](https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/40296029468) |
| C-12 v3 (retained red) | `4c1ec4eb12e109e9a2de845a235fc0b95a78f74d` | `stable-c-12-v3` / `a78ae177afbf7b0caeeded4498904b46c67ca017` | Red — Cloud round trip | Red — Cloud round trip | [40296419280](https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/40296419280) |
| **C-12 final v4** | `b35ede07a93c6c68ff8980201971e0c57c85d71c` | `stable-c-12-v4` / `0552215c490f603632a37d9f3caa284b0150caf7` | Green | Green | [40296755316](https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/40296755316) |
| C-13 v1 (retained red) | `a136293199eff62f1c96524a24df60f1e526b3ca` | `stable-c-13` / `1859ca8557f2b414be29060ac67286d97fcd5ffb` | Red — Build | Red — Build | [40297875572](https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/40297875572) |
| **C-13 final v2** | `f90dbc672b9c0c17eddfe6ff06fd4b60ca49e0a9` | `stable-c-13-v2` / `744ed1bb8834a093b8e0afcd7d880db0d16c8e9a` | Green | Green | [40298111209](https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/40298111209) |

H-01 is documentation-only: it adds this final ledger and publication note, starts from verified C-13 v2, and targets new annotated tag `stable-h-01`. Its own pushed SHA, tag object, and per-OS CI URL are reported in the final dispatch result. No merge or PR closure is authorized; `main` remains unchanged.

## Previous Implementation Handoff (`C-13`, 2026-10-26)

C-12 is complete at `b35ede07a93c6c68ff8980201971e0c57c85d71c` with annotated `stable-c-12-v4` (`0552215c490f603632a37d9f3caa284b0150caf7`); run `40296755316` is green on Ubuntu and Windows. Its v1–v3 tags remain immutable with the documented build/fixture failures. C-13 starts from that verified commit and adds a no-UI durable product catalog creation path. Name, non-negative Rial price, and initial stock are required; structured category/color/size/material/brand/barcode/specification are optional and blank-normalized. Existing Persian/Arabic/half-space normalization and candidate-acknowledged near-duplicate behavior are preserved. Atomic idempotent creation allocates positive per-store numeric tags from a dedicated watermark, records initial stock through the append-only inventory ledger, and survives retirement/restart/restore without tag reuse. Products, tag assignments, and the tag watermark join the canonical restore projection. Canonical QR JSON carries only store ID/tag plus optional calculated Luhn digit, with strict round-trip validation and no authentication capability. Six focused tests cover required fields, metadata/stock/idempotency, duplicate warning/isolation, retirement/restart/restore continuation, and exact QR examples/error cases. `ASM-31` records the boundary. The first candidate was tagged `stable-c-13` at `a136293199eff62f1c96524a24df60f1e526b3ca`; run `40297875572` was red on Ubuntu and Windows at Build because a new tag-counter write command reused a nested local name, so no tests ran. That tag remains immutable. The v2 candidate targets `stable-c-13-v2` with only that local renamed and this handoff update. CI is pending at v2 publication; final pushed SHA, tag, and per-OS run URL are reported in the task result. Any further red CI requires another fix and new immutable tag. No merge is authorized.

## Previous Implementation Handoff (`C-12`, 2026-10-26)

C-11 is complete at `582a2cfd4876b38b85619f2690402ade6dfe0e72` with annotated `stable-c-11-v2`; run `40294075398` is green on Ubuntu and Windows. The earlier `stable-c-11` tag remains immutable with its documented red build. C-12 starts from the verified v2 commit and implements sync-protocol section 4 restore semantics. OTP plus an interface-backed recovery-key or bound paired-device approval creates a manifest-bound export grant; SMS alone or as a purported second factor cannot export or activate. PostgreSQL exports only a complete contiguous hash-valid log plus canonical replay state. Fresh SQLite staging restores pairing metadata, products, inventory, invoices/items, idempotent operations, locks, display counters, and acknowledged log history atomically, recomputes the hash before commit, excludes sessions/retry timestamps, and activates a fenced replacement writer only after an identical re-read hash. Unsynced suffix loss is listed and requires owner acknowledgement. Nine tests cover RESTORE-001–004, FAIL-001, SMS-only denial, loss acknowledgement, independent-manifest corruption detection, and a real Hub → PostgreSQL ingest/export → fresh Hub round trip that proves identical state hash, no restored session, idempotent replay, and writer-epoch `N+1` continuation. `ASM-30` records the boundary. The first candidate was tagged `stable-c-12` at `45ce927df4786ab3f165df88ad2c3076d0c57f87`; run `40295770215` was red on Ubuntu and Windows at Build because the new partial store called a nonexistent transaction-helper name, so no tests ran. That tag remains immutable. The second candidate was tagged `stable-c-12-v2` at `90a92ab4207c78bdf5b9cc111ea1fa7f1aedded4`; run `40296029468` built and passed Core on both OSes but was red in Cloud tests because the new vector loader assumed an output-copied file and the synthetic draft phone used local rather than international syntax. That tag also remains immutable. The third candidate was tagged `stable-c-12-v3` at `4c1ec4eb12e109e9a2de845a235fc0b95a78f74d`; run `40296419280` passed the vector/corruption/loss tests but was red on both OSes because the populated test supplied an idempotency key different from Cloud's required `batch_id`, correctly receiving 409. That tag remains immutable. The v4 candidate targets `stable-c-12-v4` with `cloudBatch.BatchId` as that test key and this handoff update. CI is pending at v4 publication; final pushed SHA, tag, and per-OS run URL are reported in the task result. Any further red CI requires another fix and new immutable tag. No merge is authorized.

## Previous Implementation Handoff (`C-11`, 2026-10-25)

C-10 is complete at `dbbf2511ff78dd6d154cc1b12cf99bf821eeecac` with annotated `stable-c-10`; run `40293083629` is green on Ubuntu and Windows. C-11 starts from that verified commit. It adds customer-specific OTP issue/verify endpoints using `ISmsProvider` and only the deterministic stub, durable normalized-phone rate limits, expiry and attempt lockout, hashed OTP/session secrets, verified accounts and opaque bearer sessions, public-invoice claim records, and verified-phone invoice listing across stores with explicit store identity. Claims do not rewrite Hub invoice projections, and anonymous `/i/{token}` single-invoice reads remain unchanged. Five in-process Cloud/PostgreSQL tests cover expiry, wrong-code limits, per-number rate limiting plus duplicate-free issue retry, verified-phone isolation/claim behavior across multiple stores, and anonymous public-token access without an account. The first candidate was tagged `stable-c-11` at `58c1751ad4ed8e2c85d1a0ef1ca30cd6931f2619`; run `40293860657` was red on both OSes because the test compiler required a non-null assertion for an explicitly seeded optional public-token property. That tag remains immutable. The v2 candidate targets `stable-c-11-v2` with only that test nullability correction and this handoff update. CI is pending at v2 publication; final pushed SHA, tag, and per-OS run URL are reported in the task result. Any further red CI requires another fix and new immutable tag. No merge is authorized.

## Previous Implementation Handoff (`C-10`, 2026-10-25)

U-01 is complete at `3c0033f595e47e1d0abea10eeeea27084909169a` with annotated `stable-u-01`; run `40218201455` is green on Ubuntu and Windows. The owner authorized and verified another mixed-reset recovery from stale R-0 metadata to that exact published U-01 tip, with a clean tree and no recovery-time remote changes. C-10 starts from the recovered U-01 commit and follows current sync-protocol section 3 plus CLOUD-001–008, ignoring obsolete section/target-task numbering as directed. It adds immutable hash-chained SQLite replication entries in the original operation transaction, ordered per-store HTTP batches, exact head/sequence acknowledgement, durable retry/outbox behavior, gap/review reporting, throttle limits, and persistent writer fencing that blocks later mutations. Nine tests execute all CLOUD vectors through the in-process Cloud server with real PostgreSQL and prove lost-response retry across actual Hub store recreation, canonical entry hashes, acknowledgement durability, and restart-persistent fencing. Target tag: `stable-c-10`. CI is pending at publication; final pushed SHA, tag, and per-OS run URL are reported in the task result. Any red CI requires a fix and new immutable tag. No merge is authorized.

## Previous Documentation Handoff (`U-01`, 2026-10-25)

P-01b is complete at `c1093e4670b383df0dff78a4837a53beac209a9f` with annotated `stable-p-01b` (`2cd2b7c4eab92c0dcb95c965bb5fcb5a27714c8c`); run `40171231477` is green on Ubuntu and Windows. Before U-01, the local clone again had stale R-0 HEAD/index metadata while its workspace was byte-identical to that published P-01b commit. The owner explicitly authorized `git reset --mixed FETCH_HEAD` at verified `c1093e4670b383df0dff78a4837a53beac209a9f`; recovery produced a clean tree with zero file changes and did not alter remote refs. U-01 starts from the recovered P-01b tip and is documentation-only. It adds `docs/product-ux-vision.md` with the owner’s two Persian screen concepts quoted verbatim, 39 structured requirements each tagged `decided by owner` or `needs discussion`, and exactly five intentionally unanswered open questions covering free-form fields/cross-store matching, phone/Windows presentation, rental/other-service MVP scope, customer-order destination/operator notification, and cascading options/product schema. No screens are designed and no UI or implementation code is changed. Existing normative documents remain unchanged; only the new UX vision document and this mandatory handoff are in scope. Target tag: `stable-u-01`. CI is pending at publication; final SHA and tag are reported in the task result. No merge is authorized.

## Previous Implementation Handoff (`P-01b`, 2026-10-25)

C-09 is complete at `98a5a45983dc25ebe3eb90523dec222935e63fda` with annotated `stable-c-09-v2` (`4f4cd9aeba68bf705c06c7dd08a72c9b6bd458b0`). Run `40170934415` is green on Ubuntu and Windows. Its first tag `stable-c-09` remains retained with Ubuntu green and the documented Windows embedded-PostgreSQL fixture failure. P-01b starts from the verified C-09-v2 commit and is documentation-only. It adds `docs/domain-model-basket.md` specifying Basket/BasketItem strictly as customer requests, the five-state machine, submit-price snapshots and accept recheck, store switch, never-reused store-scoped product tags and optional Luhn digit, printed QR labels, verified-phone guards, abuse limits, Hub/Cloud in-app live notifications without Google-push assumptions, strict isolation, 34 acceptance tests, and an explicit non-destructive P-01/P-02/P-04 required-change list. Existing domain/protocol/OpenAPI/decision documents are intentionally unchanged; only the new Basket document and this mandatory handoff are in scope. Target tag: `stable-p-01b`. CI is pending at publication; final SHA, tag object, and per-OS run URL are reported in the dispatch result. Any red CI requires a fix and new tag.

## Previous Implementation Handoff (`C-09`, 2026-10-25)

C-08 is complete and authoritative at `776ef59d4f594410f6d72f3ec970eb539a66b83b` with annotated `stable-c-08-v4` (`4413d13c97a4a54ab1e6aa9af9205365171980ed`). Run `40170402686` is green on `ubuntu-latest` and `windows-latest`; earlier C-08 tags remain immutable audit history. C-09 starts from that verified commit. It adds `ISmsProvider`, a deterministic no-network/no-secret stub, Persian single-invoice-link templates, interface-backed subscription metadata, PostgreSQL operation-ID reservations, per-store quota/counters, and per-invoice/per-recipient abuse limits. Tests cover template content, durable retry without duplicates, quota exhaustion, rate-limit retry metadata/window recovery, recipient abuse, operation fingerprint conflicts, and cross-store link rejection. The first candidate was tagged `stable-c-09` at `5e5bc8c3a49c0f45bdba17c1ee63b6edd82ba4be`. Run `40170774903` was green on Ubuntu but red on Windows because separate test classes started competing embedded PostgreSQL processes; that tag remains immutable. The v2 candidate targets `stable-c-09-v2` and places Cloud/SMS tests in one shared, serial xUnit PostgreSQL collection fixture while preserving real database execution on both OS legs. CI is pending at v2 publication; final pushed SHA, tag object, and per-OS run URL are reported in the dispatch result. Any further red CI requires a fix and new tag.

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
