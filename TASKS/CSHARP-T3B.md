# [AGENT-TASK CSHARP-T3B] C# Track B (screens) — M-010/M-011 sprint chain

**Lane:** C# Track B (dedicated parallel session) — **Posted by:** exec-manager (2026-11-25, Asia/Tehran)
**Source orders:** M-010 + M-011 (`docs/messages/` on `main` of this repo). Manager answer: R-010 in `docs/messages/MANAGER-TO-PM.md` on `arena/order-15-status`.
**Reporting (GitHub only):** update the `## Status` line in THIS file + report comment on the PR you open. One short line per green stage (branch, tag, run URL). Never merge/close — PM merges (PO merges workflow-touching PRs).

## Branching (parallel-track discipline)
- Branch off the integration tip `e6cecc3` on `arena/4bb6cd51-kandoo`; branch name `arena/trackB-<task>`.
- CI runs on push; a stage is green ONLY per `docs/OPERATING-RULES-v2.md` (CI green + artifact + smoke).
- Create-only PR with base `main` only AFTER PR #21 merges (orphan-`main` trap — R-010 item 6).
- FILE OWNERSHIP (no overlap with Track A): Track B owns C-96 verification screenshots, per-group reports and screen fixes for groups 1–4 EXCEPT Settings/Devices (Track A owns those two screens).

## Stage 1 — C-96 group 1 verify (T0–T+8h)
First-run Setup (`docs/design/Setup.dc.html`, 3 steps) + role selection (`docs/design/Role-Select-v2.dc.html`) vs boards. Read `docs/design/README.md` FIRST, then behaviour from `docs/product-ux-spec.md`. Screenshot artifact per screen next to its board + report line per screen (matches / differs and why). Fixes go on your own branch. Tag `o17-C-96-G1V` when CI green.

## Stage 2 — Group 2 verify/fix
Review queue (`Review.dc.html`), Invoice issue (`Invoice.dc.html`), Invoices list/detail (`Invoices.dc.html`). Same acceptance. Tag `o17-C-96-G2V`.

## Stage 3 — Group 3 verify/fix
Product create/edit/list (`Product.dc.html`, `Product-Edit.dc.html`, `Products.dc.html`; plus/minus rows, 3 mandatory fields name/price/stock, auto numeric tag) + Customers (`Customers.dc.html`). Tag `o17-C-96-G3V`.

## Stage 4 — Group 4 verify/fix
Return (`Return.dc.html`), Empty/Error States (`States.dc.html`). Tag `o17-C-96-G4V`.

## Stage 5 (pre-approved; NOT DoD-A scope)
C-97 phone screens v2 (`Phone-*.dc.html` boards) and C-92 customer app on top.

## Discipline
- Hotfix cycles from the PO test reports outrank new work (M-010).
- Screenshots = CI artifacts; no success claim without an opened CI run URL.
- `[؟]` items are NOT built; file `docs/questions/Q-<id>.md` with options + default.

---
**Status:** DISPATCHED (T0 2026-11-25) — start stage 1 immediately (M-011: agents never idle).
