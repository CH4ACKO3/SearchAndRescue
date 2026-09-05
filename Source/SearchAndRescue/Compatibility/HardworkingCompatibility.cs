using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SearchAndRescue
{
    // Optional dependency: never load or distribute the third-party assembly ourselves.
    internal static class HardworkingCompatibility
    {
        private static readonly Type CompType = AccessTools.TypeByName("Kz.CompHardworking");
        private static readonly Type SettingsType = AccessTools.TypeByName("Kz.HardworkingSettings");
        private static readonly MethodInfo WorkAtNight = AccessTools.Method(
            "Kz.HardworkingUtility:IsWorkTimeAtNight", new[] { typeof(Pawn) });
        private static readonly ThinkNode EmergencyWork = CreateWorkNode();

        internal static bool Ready => CompType != null && SettingsType != null &&
                                      WorkAtNight != null && EmergencyWork != null;

        internal static bool IsWorker(Pawn pawn) => pawn?.Faction == Faction.OfPlayer &&
            CompType != null && pawn.AllComps.Any(comp => CompType.IsInstanceOfType(comp));

        private static ThinkNode CreateWorkNode()
        {
            try
            {
                Type type = AccessTools.TypeByName("Kz.JobGiver_HardworkingWork");
                if (type == null) return null;
                var node = (ThinkNode)Activator.CreateInstance(type);
                // This branch of the native priority query has no random-work timer side effects.
                type.GetField("emergency").SetValue(node, true);
                return node;
            }
            catch (Exception exception)
            {
                Warn(exception);
                return null;
            }
        }

        internal static bool CanWorkNow(Pawn pawn)
        {
            if (!IsWorker(pawn)) return true;
            if (!Ready) return false;
            try
            {
                object comp = pawn.AllComps.First(CompType.IsInstanceOfType);
                // Native checks include global/personal stop, following a drafted master,
                // interaction cooldown and EverWork. Do not bypass these with animal training.
                if (EmergencyWork.GetPriority(pawn) <= 0f) return false;
                // Chance mode belongs to the native ThinkTree. Polling its random query from
                // the matching graph would consume its timer and change ordinary work behavior.
                if (Setting<bool>("enableGlobalChanceWorkMode") || Field<bool>(comp, "curWorkHasChance"))
                    return false;
                if (Field<bool>(comp, "curWorkAtNight") &&
                    Setting<bool>("enableGlobalWorkAtNightMust") &&
                    !(bool)WorkAtNight.Invoke(null, new object[] { pawn })) return false;
                return pawn.needs?.rest == null ||
                       pawn.needs.rest.CurLevelPercentage >= Setting<float>("setGlobalWorkLimiterMinRest");
            }
            catch (Exception exception)
            {
                Warn(exception);
                return false;
            }
        }

        private static T Setting<T>(string name) => (T)SettingsType.GetField(name).GetValue(null);
        private static T Field<T>(object instance, string name) =>
            (T)CompType.GetField(name).GetValue(instance);

        private static void Warn(Exception exception) => Log.WarningOnce(
            "[Search and Rescue] Hardworking adapter unavailable; automatic dispatch disabled for its workers. " +
            exception.GetBaseException().Message, 196320790);
    }
}
