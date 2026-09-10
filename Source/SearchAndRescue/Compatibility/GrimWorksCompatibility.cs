using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SearchAndRescue
{
    internal static class GrimWorksCompatibility
    {
        private static readonly Func<Pawn, WorkGiverDef, int> Priority = BindPriority();
        internal static bool Available => Priority != null;

        internal static int RescuePriority(Pawn worker)
        {
            if (!Available || worker?.workSettings == null || HardworkingCompatibility.IsWorker(worker)) return 0;
            WorkGiverDef native = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoctorRescue");
            if (native?.workType?.defName != "GW_WorkManager_Rescue") return 0;
            WorkGiverDef field = DefDatabase<WorkGiverDef>.GetNamedSilentFail("SAR_RescueMarkedGrimWorks");
            if (field == null) return 0;
            int a = Compatibility.DetailedWorkPriority(worker, native, native.workType);
            int b = Compatibility.DetailedWorkPriority(worker, field, field.workType);
            return a > 0 && b > 0 ? Math.Max(a, b) : 0;
        }

        private static Func<Pawn, WorkGiverDef, int> BindPriority()
        {
            Type type = AccessTools.TypeByName("GW_WorkManager.WorkGiverPriorityRegistry");
            var method = type == null ? null : AccessTools.Method(type, "GetEffectivePriority",
                new[] { typeof(Pawn), typeof(WorkGiverDef) });
            return method == null ? null : (Func<Pawn, WorkGiverDef, int>)Delegate.CreateDelegate(
                typeof(Func<Pawn, WorkGiverDef, int>), method);
        }

        internal static int GetPriority(Pawn worker, WorkGiverDef workGiver)
        {
            // GrimWorks filters disabled parents before applying its child overrides.
            if (worker?.workSettings == null || workGiver?.workType == null ||
                worker.WorkTypeIsDisabled(workGiver.workType) ||
                worker.workSettings.GetPriority(workGiver.workType) <= 0) return 0;
            try { return Priority(worker, workGiver); }
            catch (Exception exception)
            {
                Log.WarningOnce("[Search and Rescue] GrimWorks priority lookup failed; skipping this work: " +
                    exception.GetBaseException().Message, 196320751);
                return 0;
            }
        }
    }
}
