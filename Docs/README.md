# Project guide

- Compatibility: [English](compatibility/README.md) / [中文](compatibility/README.zh-CN.md)
- [Live testing](development/Testing.zh-CN.md) and [compatibility test matrix](compatibility/TestMatrix.zh-CN.md)
- [Random scenarios and weight search](../Tools/SchedulerOptimizer/README.zh-CN.md): reproducible offline screening, fixed scoring, held-out evaluation and replay
- [Engine workers and TPE](../Tools/SchedulerOptimizer/ENGINE.zh-CN.md): headless workers, survival-first overload scenarios and completion-based routine regression
- [Headless and overload validation](reviews/2026-09-06-engine-overload.zh-CN.md): six-worker vanilla tests, graphical controls and CE smoke tests
- [Mid-save enemy designation investigation](reviews/2026-09-06-mid-save-designation.zh-CN.md): real before/after/reload comparison and remaining report-specific unknowns
- [Release checklist](development/ReleaseChecklist.zh-CN.md) and [tag-driven CI/CD](development/WorkshopAutomation.zh-CN.md)
- [Workshop descriptions and text exports](workshop/)
  Workshop copy follows the user's 2026-09-06 revision: retain its existing sections and concise per-mod entries; update those entries in place instead of adding sections. Keep implementation and test detail in the development/review records.
- [CE loadout, DMS and robot playtest](reviews/2026-09-06-ce-dms-runtime.zh-CN.md): persistent saves and verified runtime scope
- [Code review](reviews/2026-09-05.zh-CN.md) and [regression evidence](validation/2026-09-05-gabs.json)
- [Workshop compatibility audit](reviews/2026-09-06-compatibility.zh-CN.md): static findings and runtime verification gaps

Player-facing copy describes capabilities, purpose and actionable setup conditions in positive terms. Prefer “sets overall rescue priority; enable Doctor/Hauling for individual tasks” over “separate from Doctor work.” State compatibility limits as supported conditions and validation status, and keep the English, Simplified Chinese and Traditional Chinese messages aligned.

## Source layout

Under `Source/SearchAndRescue/`; all types retain the `SearchAndRescue` namespace.

| Folder | Responsibility |
| --- | --- |
| Core | Startup, settings, definitions and basic eligibility |
| Scheduling | Care admission, worker eligibility, patient ownership, rescue destinations, assignment lifecycle and weighted matching |
| Medical | Care plans, medical resources and supply ledger |
| Jobs | WorkGivers and JobDrivers |
| Commands | Designators and pawn commands |
| Compatibility | Mod adapters, ownership registry and Harmony patches |
| Diagnostics | Developer fixtures, regressions and performance reports |

Ownership changes should enter through `ActiveJobClaims`; its primary, logistics and standby views are read-only. Patient-wide cancellation detaches every active lane before calling JobTracker. The coordinator retains resource release, pending plans and stage completion effects.

Use `JobIdentity` (object, definition and native `loadID`) for live job ownership, and `JobEndSnapshot` across `EndCurrentJob` prefix/postfix. A pooled Job may already be cleared or reused in the postfix. Preserve the difference between an issued claim and a currently running job; WorkGivers register before the driver starts.

`AssignmentStageRules` defines role aliases; `WorkerReadinessRules` handles occupancy exclusions; `WorkerEligibility` queries live worker/provider permission. New compatibility adapters should register patient job roles in `CompatibilityRegistry`, rather than add independent ownership checks. The simulation links these pure production rules directly; game-bound claim and callback checks live in `LiveRegressionDiagnostics`.

RimWorld's runtime folders (`About`, `Assemblies`, `Defs`, `Languages`, `Patches`, `Textures`) remain at the repository root. `SourceAssets` holds editable artwork; its local `References` subfolder is ignored by Git. Builds use ignored `bin`/`obj` folders, and release packages go to `artifacts/releases`.

Build instructions are in the [README](../README.md). Run build, simulation and packaging commands from the repository root.
