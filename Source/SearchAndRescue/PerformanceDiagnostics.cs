using System;
using System.Diagnostics;
using System.Text;
using LudeonTK;
using Verse;

namespace SearchAndRescue
{
    internal enum SarPerformancePhase
    {
        MapTick,
        PendingAndWake,
        TreatmentMonitoring,
        ActiveAssignmentMaintenance,
        PeriodicCleanup,
        ScheduleRebuild,
        CarePlanning,
        UnifiedMatching,
        UnifiedEdgeScoring,
        PickupReachability,
        TransportMatching,
        WakePendingWorkers,
        Count
    }

    /// <summary>
    /// Opt-in, allocation-free timing on the game thread. It is deliberately disabled by
    /// default: normal play pays only a few predictable branches in the coordinator, while a
    /// development run can expose average cost, spikes and graph sizes without requiring DPA.
    /// </summary>
    internal static class SearchAndRescuePerformanceDiagnostics
    {
        private static readonly long[] Calls = new long[(int)SarPerformancePhase.Count];
        private static readonly long[] TotalTimestampTicks = new long[(int)SarPerformancePhase.Count];
        private static readonly long[] MaximumTimestampTicks = new long[(int)SarPerformancePhase.Count];

        private static int startedAtGameTick;
        private static long globalRebuilds;
        private static long requestScopedRebuilds;
        private static long nestedRebuilds;
        private static int rebuildDepth;
        private static int maximumRebuildDepth;
        private static long dirtyRequests;
        private static long immediateDirtyRequests;
        private static long maintenanceDirtyRequests;
        private static long pickupReachabilityCacheHits;
        private static long pickupReachabilityCacheMisses;
        private static long transportNoCapableWorkerSkips;
        private static int lastUnifiedWorkers;
        private static int lastUnifiedTargets;
        private static int maximumUnifiedEdges;
        private static int lastTransportWorkers;
        private static int lastTransportTasks;
        private static int maximumTransportEdges;
        private static Map benchmarkMap;
        private static string benchmarkScenario;

        internal static bool Enabled { get; private set; }

        internal static void SetBenchmarkScenario(Map map, string scenario)
        {
            benchmarkMap = map;
            benchmarkScenario = scenario;
        }

        internal static long Begin(SarPerformancePhase phase)
        {
            return Enabled ? Stopwatch.GetTimestamp() : 0L;
        }

        internal static void End(SarPerformancePhase phase, long started)
        {
            if (!Enabled || started == 0L)
            {
                return;
            }

            long elapsed = Math.Max(0L, Stopwatch.GetTimestamp() - started);
            int index = (int)phase;
            Calls[index]++;
            TotalTimestampTicks[index] += elapsed;
            if (elapsed > MaximumTimestampTicks[index])
            {
                MaximumTimestampTicks[index] = elapsed;
            }
        }

        internal static void EnterRebuild(bool requestScoped)
        {
            if (!Enabled)
            {
                return;
            }

            if (requestScoped)
            {
                requestScopedRebuilds++;
            }
            else
            {
                globalRebuilds++;
            }

            rebuildDepth++;
            if (rebuildDepth > 1)
            {
                nestedRebuilds++;
            }
            maximumRebuildDepth = Math.Max(maximumRebuildDepth, rebuildDepth);
        }

        internal static void ExitRebuild()
        {
            if (Enabled && rebuildDepth > 0)
            {
                rebuildDepth--;
            }
        }

        internal static void RecordDirtyRequest(bool maintenance, int delayTicks)
        {
            if (!Enabled)
            {
                return;
            }

            dirtyRequests++;
            if (delayTicks <= 0)
            {
                immediateDirtyRequests++;
            }
            if (maintenance)
            {
                maintenanceDirtyRequests++;
            }
        }

        internal static void RecordPickupReachabilityCache(bool hit)
        {
            if (!Enabled)
            {
                return;
            }

            if (hit)
            {
                pickupReachabilityCacheHits++;
            }
            else
            {
                pickupReachabilityCacheMisses++;
            }
        }

        internal static void RecordTransportNoCapableWorkerSkip()
        {
            if (Enabled)
            {
                transportNoCapableWorkerSkips++;
            }
        }

        internal static void RecordGraph(bool transport, int workers, int targets)
        {
            if (!Enabled)
            {
                return;
            }

            int edges = SaturatingProduct(workers, targets);
            if (transport)
            {
                lastTransportWorkers = workers;
                lastTransportTasks = targets;
                maximumTransportEdges = Math.Max(maximumTransportEdges, edges);
            }
            else
            {
                lastUnifiedWorkers = workers;
                lastUnifiedTargets = targets;
                maximumUnifiedEdges = Math.Max(maximumUnifiedEdges, edges);
            }
        }

        private static int SaturatingProduct(int left, int right)
        {
            long product = Math.Max(0, left) * (long)Math.Max(0, right);
            return product >= int.MaxValue ? int.MaxValue : (int)product;
        }

        private static void Reset()
        {
            Array.Clear(Calls, 0, Calls.Length);
            Array.Clear(TotalTimestampTicks, 0, TotalTimestampTicks.Length);
            Array.Clear(MaximumTimestampTicks, 0, MaximumTimestampTicks.Length);
            startedAtGameTick = Find.TickManager?.TicksGame ?? 0;
            globalRebuilds = 0;
            requestScopedRebuilds = 0;
            nestedRebuilds = 0;
            rebuildDepth = 0;
            maximumRebuildDepth = 0;
            dirtyRequests = 0;
            immediateDirtyRequests = 0;
            maintenanceDirtyRequests = 0;
            pickupReachabilityCacheHits = 0;
            pickupReachabilityCacheMisses = 0;
            transportNoCapableWorkerSkips = 0;
            lastUnifiedWorkers = 0;
            lastUnifiedTargets = 0;
            maximumUnifiedEdges = 0;
            lastTransportWorkers = 0;
            lastTransportTasks = 0;
            maximumTransportEdges = 0;
        }

        private static string BuildReport()
        {
            int now = Find.TickManager?.TicksGame ?? startedAtGameTick;
            int observedGameTicks = Math.Max(0, now - startedAtGameTick);
            StringBuilder report = new StringBuilder();
            report.Append("[Search and Rescue] Performance profile enabled=")
                .Append(Enabled)
                .Append(" benchmark=")
                .Append(ReferenceEquals(Find.CurrentMap, benchmarkMap) &&
                        !string.IsNullOrEmpty(benchmarkScenario)
                    ? benchmarkScenario
                    : "unconfigured")
                .Append(" observedGameTicks=").Append(observedGameTicks)
                .Append(" rebuilds(global/request)=")
                .Append(globalRebuilds).Append('/').Append(requestScopedRebuilds)
                .Append(" nested/maxDepth=").Append(nestedRebuilds).Append('/').Append(maximumRebuildDepth)
                .Append(" dirty(total/immediate/maintenance)=")
                .Append(dirtyRequests).Append('/').Append(immediateDirtyRequests).Append('/')
                .Append(maintenanceDirtyRequests)
                .Append(" pickupReachCache(hit/miss)=")
                .Append(pickupReachabilityCacheHits).Append('/').Append(pickupReachabilityCacheMisses)
                .Append(" transportNoWorkerSkips=").Append(transportNoCapableWorkerSkips)
                .Append("\n graphs unified(last/maxEdges)=")
                .Append(lastUnifiedWorkers).Append('x').Append(lastUnifiedTargets)
                .Append('/').Append(maximumUnifiedEdges)
                .Append(" transport(last/maxEdges)=")
                .Append(lastTransportWorkers).Append('x').Append(lastTransportTasks)
                .Append('/').Append(maximumTransportEdges);

            double timestampToMilliseconds = 1000d / Stopwatch.Frequency;
            for (int index = 0; index < (int)SarPerformancePhase.Count; index++)
            {
                long calls = Calls[index];
                if (calls <= 0)
                {
                    continue;
                }

                double totalMs = TotalTimestampTicks[index] * timestampToMilliseconds;
                double averageMicroseconds = totalMs * 1000d / calls;
                double maximumMs = MaximumTimestampTicks[index] * timestampToMilliseconds;
                double microsecondsPerGameTick = observedGameTicks <= 0
                    ? 0d
                    : totalMs * 1000d / observedGameTicks;
                report.Append("\n ").Append((SarPerformancePhase)index)
                    .Append(" calls=").Append(calls)
                    .Append(" avgUs=").Append(averageMicroseconds.ToString("F2"))
                    .Append(" maxMs=").Append(maximumMs.ToString("F3"))
                    .Append(" totalMs=").Append(totalMs.ToString("F2"))
                    .Append(" usPerGameTick=").Append(microsecondsPerGameTick.ToString("F2"));
            }

            return report.ToString();
        }

        [DebugAction("Search and Rescue", "Start/reset performance profile",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void StartProfile()
        {
            Reset();
            Enabled = true;
            Log.Message("[Search and Rescue] Performance profiling started. " +
                        "Use 'Dump performance profile' for a snapshot or 'Stop performance profile' to finish.");
        }

        [DebugAction("Search and Rescue", "Dump performance profile",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DumpProfile()
        {
            Log.Message(BuildReport());
        }

        [DebugAction("Search and Rescue", "Stop performance profile",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void StopProfile()
        {
            string report = BuildReport();
            Enabled = false;
            Log.Message(report + "\n[Search and Rescue] Performance profiling stopped.");
        }
    }
}
