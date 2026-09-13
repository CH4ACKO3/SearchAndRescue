# Coordination option comparisons

Run through `../Run-CoordinationProbe.ps1`. This developer harness loads prepared benchmark saves; it does not generate equivalent scenarios from pawn counts alone. The runner creates a separate runtime mod and save profile and starts a hidden game process. Never run multiple comparison games concurrently.

Required baseline saves: `d5p34h4r10_Initial.rws`, `d20p136h16r8_Initial.rws`, `d40p272h32r9_Initial.rws`, under `BaselineProfile/Saves`. `BasePackageId` must match the SAR id stored in these saves.

Use `-ClinicalSave <path>` for the validated 5-doctor/34-patient/4-hauler clinical save. This runs the seven modes at 24000 ticks with EmergencyAuto after a warmup. Use `-PerformanceOnly` for the 20/136/16 and 40/272/32 pressure cases at 6000 ticks with AllTending. Do not combine these switches. The small scaling save is for warmup and is not an adequate clinical fixture.

Modes reload their initial save and change only the named setting: full, fast allocation, simplified logistics, fewer medicine sources, no mission kits, no standby, and combined. Combined enables all three simplifications and disables both mission kits and standby. Exact matching remains selected for stages using complete graphs. The probe sets options before loading and before starting measurement.

`progress.txt` records completed cases; `error.txt` indicates a harness failure. Summarize a completed run using `python Tools/CoordinationProbe/analyze.py <output-directory> <profile> [<profile> ...]`. Retain the assembly hash, timings, clinical XML, and profiler log with any claims. Profile stages are nested and must not be summed as independent costs.

The harness fixes per-tick random seeds, but game trajectories can still differ between runs. It is not a deterministic outcome test. Compare clinical outcomes separately from CPU time, repeat material comparisons, and report observation limits. First-treatment timing refers to the first completed tend; uncompleted patients receive the full observation horizon.

The four new defaults and settings serialization are covered by `StandingTreatmentProbe`; its four actual tending cases enable the three simplifications and disable kits. `Run-StandingProbe.ps1 -Preview` renders the advanced settings page after those cases finish.

## Large-scene hotspot tracing

Pass `-HotspotPlan @('full,00011','combined,11100')` and optionally `-HotspotTicks 900`. Each bit controls, in order: fast rescue, simplified logistics, fewer medicine sources, mission kits, standby. A short small-scene warmup precedes the specified large-scene cases. Do not combine this with ClinicalSave or PerformanceOnly. Defaults outside these five flags match the pressure probe.

The probe dumps cumulative method timings and the built-in phase profile every 300 ticks. `hotspots.csv` columns are run, elapsed ticks, method, calls, total ms, max ms, successes, failures. Outcome counters apply to destination, bed and supply-reachability queries. Iterator bodies are measured at MoveNext; methods returning lazy LINQ queries still measure query construction, with actual enumeration included in their consuming BuildSupplyTasks timer. Nested timings must not be summed.

`-TraceDirty` adds rebuild-request call stacks to dirty.csv and bed/occupation/reservation/job snapshots to state.csv. This is a separate instrumented diagnostic run; its extra overhead must not be presented as a production speed benchmark. `summarize-hotspots.py <output> <profile> ...` retains every window, including the initial peak, in hotspot-summary.json. A truncated case remains a partial observation; use progress.txt to confirm completion.

HotspotTicks must be 600–6000 and divisible by 30. TraceDirty also records up to 500 invalid-pending samples per case in invalid-pending.tsv: game tick, worker, patient, stage, diagnostic reason, supply id, resource availability/pickup/active-owner/emergency details. The separate `-RejectRescueSupply` switch applies a probe-only causal filter to supply edges for patients already owned by Rescue; never use its measurements as unchanged-production observations. The production DLL remains untouched.
