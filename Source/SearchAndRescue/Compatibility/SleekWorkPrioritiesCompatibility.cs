using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SearchAndRescue
{
    internal static class SleekWorkPrioritiesCompatibility
    {
        private static readonly Type StoreType = AccessTools.TypeByName("SleekWorkPriorities.SleekWorkGiverStore");
        private static readonly Func<bool> Enabled = BindEnabled();
        private static readonly System.Reflection.MethodInfo GetStore = StoreType == null ? null :
            AccessTools.PropertyGetter(StoreType, "Get");
        private static readonly System.Reflection.MethodInfo GetOverride = StoreType == null ? null :
            AccessTools.Method(StoreType, "GetCommittedOverride", new[] { typeof(Pawn), typeof(WorkGiverDef) });

        internal static bool Available => Enabled != null && GetStore != null && GetOverride != null;

        private static Func<bool> BindEnabled()
        {
            Type type = AccessTools.TypeByName("SleekWorkPriorities.BoardActive");
            var method = type == null ? null : AccessTools.PropertyGetter(type, "PerJob");
            return method == null ? null : (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), method);
        }

        internal static int ApplyPriority(Pawn worker, WorkGiverDef giver, int priority)
        {
            if (priority <= 0 || !Available || HardworkingCompatibility.IsWorker(worker)) return priority;
            try
            {
                if (!Enabled()) return priority;
                object store = GetStore.Invoke(null, null);
                if (store == null) return priority;
                int child = (int)GetOverride.Invoke(store, new object[] { worker, giver });
                // Sleek's implicit rank keeps the parent priority. Explicit child ranks
                // may defer a task, while an Off child always removes its permission.
                return child < 0 ? priority : child == 0 ? 0 : Math.Max(priority, child);
            }
            catch (Exception exception)
            {
                Log.WarningOnce("[Search and Rescue] Sleek work priority lookup failed; skipping this work: " +
                    exception.GetBaseException().Message, 196320752);
                return 0;
            }
        }
    }
}
