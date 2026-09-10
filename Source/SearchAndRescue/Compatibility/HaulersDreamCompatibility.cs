using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using HarmonyLib;
using Verse;

namespace SearchAndRescue
{
    // Protect the same temporary medical claims that SAR protects from vanilla
    // and PUAH unloading. Both public and bulk-surplus paths use these overloads.
    [HarmonyPatch]
    internal static class HaulersDreamMedicalSupplyPatch
    {
        private static bool Prepare() => TargetMethods().Any();

        private static IEnumerable<MethodBase> TargetMethods()
        {
            Type type = AccessTools.TypeByName("HaulersDream.InventorySurplus");
            if (type == null) yield break;
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(type))
            {
                ParameterInfo[] args = method.GetParameters();
                if (method.Name == "SurplusOf" && method.ReturnType == typeof(int) && args.Length >= 2 &&
                    args[0].ParameterType == typeof(Pawn) && args[1].ParameterType == typeof(Thing))
                    yield return method;
            }
        }

        private static bool Prefix([HarmonyArgument(1)] Thing thing, ref int __result)
        {
            if (!SearchAndRescueJobContext.IsProtectedOrClaimedMedicalSupply(thing)) return true;
            __result = 0;
            return false;
        }
    }
}
