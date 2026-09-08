using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SearchAndRescue
{
    // Tooltip order is explanatory. Never reorder the shared workGiversByPriority
    // list: both vanilla and Work Tab use it for actual work selection.
    [HarmonyPatch(typeof(PawnColumnWorker_WorkPriority), "SpecificWorkListString")]
    internal static class FieldRescueWorkList
    {
        private static bool Prefix(WorkTypeDef def, ref string __result)
        {
            if (def != SearchAndRescueDefOf.SAR_FieldRescue) return true;
            __result = string.Join(Environment.NewLine, def.workGiversByPriority
                .OrderBy(giver => DisplayOrder(giver.defName))
                .Select(giver => " - " + giver.LabelCap.ToString() +
                    (giver.emergency ? " (" + "EmergencyWorkMarker".Translate().ToString() + ")" : "")));
            return false;
        }

        private static int DisplayOrder(string name) => name switch
        {
            "SAR_TreatMarked" => 0,
            "SAR_SupportiveCareMarkedNursing" => 1,
            "SAR_RepairMarkedMech" => 2,
            "SAR_CaptureMarked" => 3,
            "SAR_RescueMarkedHauling" => 4,
            "SAR_RescueMarkedParamedic" => 4,
            "SAR_RescueMarkedNursing" => 4,
            "SAR_FollowupTreatMarked" => 5,
            "SAR_AutomaticRoutineTreat" => 6,
            _ => 7
        };
    }
}
