using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;

// Probe-only instrumentation. Iterator bodies are measured at MoveNext, not creation.
static class HotspotTiming
{
    sealed class Metric { public string Name; public long Calls, Elapsed, Max, Success, Failure; }
    static readonly Dictionary<MethodBase, Metric> metrics = new Dictionary<MethodBase, Metric>();
    static readonly Dictionary<string, long> dirty = new Dictionary<string, long>();
    internal static bool Active;
    static bool cleanup;
    static int invalidSamples;
    static MethodInfo reasonMethod, pickupMethod, availableMethod;
    static FieldInfo ledgerField;
    internal static void Install()
    {
        var harmony = new Harmony("sar.coordination.hotspots");
        if (File.Exists(Path.Combine(GenFilePaths.SaveDataFolderPath, "reject-rescue-supply.txt")))
            harmony.Patch(AccessTools.Method(AccessTools.TypeByName("SearchAndRescue.SearchAndRescueCoordinator"), "TransportTaskEdgeWeight"),
                prefix: new HarmonyMethod(typeof(HotspotTiming), nameof(RejectRescueSupply)));
        if (File.Exists(Path.Combine(GenFilePaths.SaveDataFolderPath, "trace-dirty.txt")))
        {
            harmony.Patch(AccessTools.Method(AccessTools.TypeByName("SearchAndRescue.SearchAndRescueCoordinator"), "RequestScheduleRebuild"),
                prefix: new HarmonyMethod(typeof(HotspotTiming), nameof(DirtyRequest)));
            Type coordinator = AccessTools.TypeByName("SearchAndRescue.SearchAndRescueCoordinator");
            reasonMethod = AccessTools.Method(coordinator, "DebugPendingInvalidReason");
            pickupMethod = AccessTools.Method(coordinator, "PickupReservationAvailable");
            ledgerField = AccessTools.Field(coordinator, "medicalResources");
            availableMethod = AccessTools.Method(ledgerField.FieldType, "AvailableForRelocation", new[] { typeof(Thing), typeof(Pawn) });
            harmony.Patch(AccessTools.Method(coordinator, "CleanupInvalidPendingWorkers"),
                prefix: new HarmonyMethod(typeof(HotspotTiming), nameof(CleanupBegin)),
                postfix: new HarmonyMethod(typeof(HotspotTiming), nameof(CleanupEnd)));
            harmony.Patch(AccessTools.Method(coordinator, "PendingAssignmentValid"),
                postfix: new HarmonyMethod(typeof(HotspotTiming), nameof(InvalidPending)));
        }
        foreach (string name in new[] { "RebuildPendingAssignmentsCore", "ScheduleTransportAndStandbyAssignments",
            "TransportTaskEdgeWeight", "SelectTreatmentOption", "BuildMissionKit", "EdgeWeight",
            "BuildSupplyTasks", "SupplyAlternatives", "SupplyMedicineAlternatives", "SupplyReachableByAvailableHauler",
            "RebalanceNearbyFieldSupplyReferences", "TryFindRescueDestinationCached", "PendingAssignmentValid",
            "MatchTransportTasks", "MatchRescuePatients", "BestStageChoiceCore", "TryFindRescueDestination" })
        {
            MethodInfo method = AccessTools.Method(AccessTools.TypeByName("SearchAndRescue.SearchAndRescueCoordinator"), name);
            if (method == null) throw new MissingMethodException(name);
            var iterator = method.GetCustomAttribute<IteratorStateMachineAttribute>();
            if (iterator != null) method = AccessTools.Method(iterator.StateMachineType, "MoveNext");
            metrics.Add(method, new Metric { Name = name });
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(HotspotTiming), nameof(Begin)),
                postfix: new HarmonyMethod(typeof(HotspotTiming), nameof(End)));
            if (name == "TryFindRescueDestination" || name == "SupplyReachableByAvailableHauler")
                harmony.Patch(method, postfix: new HarmonyMethod(typeof(HotspotTiming), nameof(BoolResult)));
        }
        foreach (string name in new[] { "FindBestRescueBed", "FindNonTemporaryRescueBed" })
        {
            MethodInfo method = AccessTools.Method(AccessTools.TypeByName("SearchAndRescue.Compatibility"), name);
            if (method == null) throw new MissingMethodException(name);
            metrics.Add(method, new Metric { Name = name });
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(HotspotTiming), nameof(Begin)),
                postfix: new HarmonyMethod(typeof(HotspotTiming), nameof(End)));
            harmony.Patch(method, postfix: new HarmonyMethod(typeof(HotspotTiming), nameof(BedResult)));
        }
    }
    static void Begin(out long __state) { __state = Active ? Stopwatch.GetTimestamp() : 0; }
    // Causal experiment only: reject candidates already rejected by pending validation.
    static bool RejectRescueSupply(object __instance, object __1, ref double __result)
    {
        if (AccessTools.Field(__1.GetType(), "SupplyResource").GetValue(__1) == null) return true;
        Pawn target = (Pawn)AccessTools.Field(__1.GetType(), "Target").GetValue(__1);
        var active = (System.Collections.IDictionary)AccessTools.Property(__instance.GetType(), "activeByTarget").GetValue(__instance, null);
        object owner = active.Contains(target) ? active[target] : null;
        if (owner == null || AccessTools.Field(owner.GetType(), "Stage").GetValue(owner).ToString() != "Rescue") return true;
        __result = 0;
        return false;
    }
    static void CleanupBegin() { cleanup = true; }
    static void CleanupEnd() { cleanup = false; }
    static void InvalidPending(object __instance, Pawn __0, object __1, int __2, bool __result)
    {
        if (!Active || !cleanup || __result || invalidSamples++ >= 500) return;
        Type type = __1.GetType();
        object stage = AccessTools.Field(type, "Stage").GetValue(__1);
        Pawn patient = (Pawn)AccessTools.Field(type, "Target").GetValue(__1);
        Thing supply = (Thing)AccessTools.Field(type, "SupplyResource").GetValue(__1);
        int count = (int)AccessTools.Field(type, "SupplyCount").GetValue(__1);
        string reason = (string)reasonMethod.Invoke(__instance, new[] { (object)__0, __1, __2 });
        string extra = supply == null ? "" : "available=" + availableMethod.Invoke(ledgerField.GetValue(__instance), new object[] { supply, __0 }) +
            "/" + count + ";pickup=" + pickupMethod.Invoke(__instance, new object[] { __0, supply, count, patient });
        var active = (System.Collections.IDictionary)AccessTools.Property(__instance.GetType(), "activeByTarget").GetValue(__instance, null);
        object owner = active.Contains(patient) ? active[patient] : null;
        string ownerStage = owner == null ? "none" : AccessTools.Field(owner.GetType(), "Stage").GetValue(owner).ToString();
        extra += ";active=" + ownerStage + ";emergency=" + AccessTools.Method(__instance.GetType(), "NeedsFieldStabilization").Invoke(null, new object[] { patient });
        File.AppendAllText(Path.Combine(GenFilePaths.SaveDataFolderPath, "invalid-pending.tsv"),
            $"{__2}\t{__0.ThingID}\t{patient.ThingID}\t{stage}\t{reason}\t{supply?.ThingID}\t{extra}\n");
    }
    static void End(MethodBase __originalMethod, long __state)
    {
        if (__state == 0) return;
        long elapsed = Stopwatch.GetTimestamp() - __state;
        Metric m = metrics[__originalMethod]; m.Calls++; m.Elapsed += elapsed; m.Max = Math.Max(m.Max, elapsed);
    }
    static void BoolResult(MethodBase __originalMethod, bool __result)
    { if (Active) { Metric m = metrics[__originalMethod]; if (__result) m.Success++; else m.Failure++; } }
    static void BedResult(MethodBase __originalMethod, RimWorld.Building_Bed __result)
    { if (Active) { Metric m = metrics[__originalMethod]; if (__result != null) m.Success++; else m.Failure++; } }
    static void DirtyRequest(bool maintenance, int delayTicks)
    {
        if (!Active) return;
        string stack = string.Join(" > ", new StackTrace(false).GetFrames().Skip(2).Take(5)
            .Select(f => f.GetMethod().DeclaringType?.Name + "." + f.GetMethod().Name + ":" + f.GetILOffset()));
        string key = maintenance + "," + delayTicks + "," + stack;
        dirty.TryGetValue(key, out long count); dirty[key] = count + 1;
    }
    internal static void Reset() { foreach (Metric m in metrics.Values) m.Calls = m.Elapsed = m.Max = m.Success = m.Failure = 0; dirty.Clear(); invalidSamples = 0; }
    internal static void Dump(string run, int ticks)
    {
        File.AppendAllLines(Path.Combine(GenFilePaths.SaveDataFolderPath, "hotspots.csv"), metrics.Values.Select(m =>
            $"{run},{ticks},{m.Name},{m.Calls},{m.Elapsed * 1000d / Stopwatch.Frequency:F4},{m.Max * 1000d / Stopwatch.Frequency:F4},{m.Success},{m.Failure}"));
        if (dirty.Count > 0) File.AppendAllLines(Path.Combine(GenFilePaths.SaveDataFolderPath, "dirty.csv"),
            dirty.Select(pair => $"{run},{ticks},{pair.Value},{pair.Key}"));
        if (dirty.Count > 0)
        {
            Map map = Find.CurrentMap;
            var beds = map.listerBuildings.allBuildingsColonist.OfType<RimWorld.Building_Bed>().ToArray();
            int occupied = beds.Sum(b => Enumerable.Range(0, b.SleepingSlotsCount).Count(i => b.GetCurOccupant(i) != null));
            int reserved = map.reservationManager.ReservationsReadOnly.Select(r => r.Target.Thing).OfType<RimWorld.Building_Bed>().Distinct().Count();
            string jobs = string.Join("|", map.mapPawns.AllPawnsSpawned.GroupBy(p => p.CurJobDef?.defName ?? "none").OrderByDescending(g => g.Count()).Select(g => g.Key + ":" + g.Count()));
            File.AppendAllText(Path.Combine(GenFilePaths.SaveDataFolderPath, "state.csv"),
                $"{run},{ticks},{beds.Length},{beds.Sum(b => b.SleepingSlotsCount)},{occupied},{reserved},{jobs}\n");
        }
    }
}
