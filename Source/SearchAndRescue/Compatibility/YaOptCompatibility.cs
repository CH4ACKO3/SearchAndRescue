using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SearchAndRescue
{
    [HarmonyPatch(typeof(JobGiver_Work), nameof(JobGiver_Work.TryIssueJobPackage))]
    internal static class YaOptCompatibility
    {
        private static readonly FieldInfo Running = AccessTools.Field(
            AccessTools.TypeByName("YaOpt.Helpers.ParallelWorkGiver"), "Running");

        internal static bool Available => Running?.IsStatic == true && Running.FieldType == typeof(bool);
        private static bool Prepare() => Available;

        private static void Prefix(JobGiver_Work __instance, Pawn pawn, out bool __state)
        {
            __state = false;
            if (__instance.emergency || pawn?.workSettings == null || (bool)Running.GetValue(null)) return;

            foreach (WorkGiver giver in pawn.workSettings.WorkGiversInOrderNormal)
            {
                if (giver.def.giverClass.Assembly != typeof(YaOptCompatibility).Assembly) continue;
                if ((giver is WorkGiver_MechFieldRescueHauling || giver is WorkGiver_MechFieldRescueDoctor) &&
                    !Compatibility.IsColonyWorkMech(pawn)) continue;
                // YaOpt already uses this guard for nested serial scans. Keep the entire batch
                // serial: SAR commits ownership when issuing a job, before speculative results
                // would be selected and discarded by the parallel scanner.
                Running.SetValue(null, true);
                __state = true;
                return;
            }
        }

        private static void Finalizer(bool __state)
        {
            if (__state) Running.SetValue(null, false);
        }
    }
}
