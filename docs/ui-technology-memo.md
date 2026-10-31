# C-30 UI technology memo

**Date:** 2026-10-31

**Scope:** throwaway Windows/Android spikes and a technology comparison only. Nothing under `spikes/` is the real UI or part of `Kandoo.sln`. No legal, tax, production-security, device-performance, or visual-accessibility claim is made.

## Evidence and candidate selection

.NET MAUI and Avalonia were selected for the two spikes because the existing D6 direction is a shared .NET MAUI application layer (`docs/tech-decision.md`, §3), and both candidates can reuse the repository's C# application contracts. Repository evidence is 98 `.cs` files and eight `.csproj` files under `src`/`tests`, with no Dart, JavaScript, or TypeScript production file under `src`; this is code-reuse evidence, not a claim about any individual's expertise. Flutter and a Hub-served local web/PWA remain memo-only alternatives and were not measured.

Each spike has one Persian RTL screen displaying **فاکتور دیجیتال کندو**, accessibility names, and a button that calls the same probe source as its CI harness. The harness starts the real `Kandoo.Hub`, waits for `/health`, requests generated `/openapi/v1.json`, checks its OpenAPI structure, and records path count and elapsed milliseconds. This proves a real HTTP contract fetch, not generated-client correctness, GUI automation, an Android emulator run, or device accessibility.

## Comparison

| Candidate | Persian RTL | Windows + Android CI | Generated Hub OpenAPI consumption | Accessibility | Team/repository fit |
| --- | --- | --- | --- | --- | --- |
| **.NET MAUI** | `FlowDirection="RightToLeft"` is present on the spike page/layout; mixed-direction endpoint text is explicitly LTR. Persian shaping and font behavior still need device review. | One multi-target project with Windows and Android hosts; measured below. Workload/SDK setup is a material CI cost. | Generated C# clients can consume the Hub document; the spike verifies the real document fetch only, not Kiota/NSwag output. | `SemanticProperties.Description` labels are present. Screen-reader order, focus, touch targets, and keyboard behavior are unmeasured. | Strongest fit with the existing D6 direction and C# code reuse; native platform adapters remain available. |
| **Avalonia** | Shared `MainView` sets RTL and isolates the LTR endpoint. Persian shaping/font behavior still needs device review. | Shared net8 UI with separate Desktop and Android hosts; measured below. Android still requires a .NET workload. | Same C# client-generation options and real-document probe boundary as MAUI. | `AutomationProperties.Name` labels are present. No screen-reader, focus, keyboard, or touch audit was run. | Strong C# reuse and a viable shared-view fallback; it would change the existing MAUI direction and requires its own native-capability validation. |
| **Flutter** | Supports explicit RTL directionality and locale-aware widgets in principle; no Kandoo spike or visual evidence was produced. | Not measured. Would require a new Flutter/Dart toolchain and separate CI setup. | Dart clients can be generated from OpenAPI, but no generator, dependency, or compatibility test was run here. | Flutter semantics would require a new audit; no Kandoo evidence exists. | No Dart production evidence in this repository; adopting it would add a language/toolchain and reduce direct C# reuse. This is not a claim that the team cannot learn Dart. |
| **Hub-served local web/PWA** | HTML `dir="rtl"` and CSS logical properties are a direct fit; no screen was implemented in C-30. | Not measured. Browser coverage could avoid native UI builds, but Windows/Android browser matrices and any native wrapper still need CI. | The browser can fetch the same document or a generated TypeScript client; neither was implemented or measured. | Semantic HTML can help, but RTL reading order, keyboard/focus behavior, zoom, and browser screen readers remain unverified. | Reuses the Hub HTTP host but introduces web UI tooling with no JS/TS production evidence in `src`. Offline storage, printing, device integration, and local-browser lifecycle need separate proof. |

## Measured CI results

All durations below must come from GitHub Actions step timestamps for run [41167595870](https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/41167595870), not local execution. “Build” includes restore/compilation performed by that step; it is not startup time, UI responsiveness, package-install time, or device performance.

| Technology | Workload install | Windows build | Android build | Real Hub OpenAPI probe | Job total |
| --- | ---: | ---: | ---: | ---: | ---: |
| MAUI | 203 s | 67 s | 167 s | 15 s | 467 s (7m47s) |
| Avalonia | 99 s | 23 s | 20 s | 17 s | 180 s (3m00s) |

These are single hosted-runner samples, not benchmarks. The probe step also publishes the Hub, builds the probe where needed, waits for readiness, fetches the document, and stops the Hub. Both evidence artifacts uploaded successfully (`c30-maui-spike-evidence`, 2,424 bytes; `c30-avalonia-spike-evidence`, 2,307 bytes); artifact size is not application package size.

Earlier candidate runs are retained rather than hidden: [41166806917](https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/41166806917) failed both Android targets with `NETSDK1202` after SDK 10 workload resolution; [41167253138](https://github.com/majid-ghanbari-dost/Kandoo/actions/runs/41167253138) pinned the spike scope to SDK 8, passed Avalonia and MAUI Windows, and failed MAUI Android with `XA1018` because its manifest was absent. Code changes followed; these are not unexplained reruns.

## Decision boundary and next gates

Both candidates passed the requested compile/probe gate. Keep **MAUI as the provisional existing D6 direction**, not as a newly proven final product choice, and retain **Avalonia as a measured fallback**. The single timing sample favors neither as a production decision: MAUI's workload and Android steps were slower here, while its single-project structure matches the existing direction. Real-device accessibility, Persian rendering, offline workflows, printing, and generated-client compilation remain decisive unresolved gates.

The next product-UI gate must test real Windows and Android devices for Persian text shaping, RTL focus/reading order, screen readers, keyboard/touch accessibility, offline Hub-unavailable behavior, printing/device adapters, startup/package cost, and compilation of an actual generated Hub client. Failure of a required workflow reopens the direction; these spikes must not be promoted into the real UI.
