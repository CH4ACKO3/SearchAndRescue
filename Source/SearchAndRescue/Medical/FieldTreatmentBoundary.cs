using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SearchAndRescue
{
    internal static class FieldTreatmentBoundary
    {
        internal static bool IsEmergency(Hediff hediff)
        {
            if (hediff == null || !hediff.TendableNow()) return false;
            if (hediff.BleedRate > 0f) return true;
            // Immunity wins over a stale lifeThreatening stage while disease recedes.
            HediffComp_Immunizable immunity = hediff.TryGetComp<HediffComp_Immunizable>();
            if (immunity != null && immunity.FullyImmune) return false;
            if (hediff.CurStage?.lifeThreatening == true || InfectionPriority.IsInfection(hediff)) return true;
            // Fatal immunity races (including mod diseases) need timely tending even
            // before the first stage marked life-threatening. Labels are not an API.
            if (immunity != null && hediff.def.lethalSeverity > 0f) return true;
            // MI shock can be arrested by CompTended before its 0.5 danger stage.
            // CPR/defibrillation, transfusion and surgery use their own eligibility;
            // marking their non-tendable conditions here would create empty tend jobs.
            return Compatibility.UsesMoreInjuries && hediff.def.defName == "HypovolemicShock";
        }

        internal static bool AtCareLocation(Pawn patient)
        {
            if (patient?.Spawned != true) return false;
            if (RescueDestinationPlanner.IsInSafePatientBed(patient)) return true;
            return patient.Map.designationManager.SpawnedDesignationsOfDef(SearchAndRescueDefOf.SAR_RescuePoint)
                .Any(d => RescueDestinationPlanner.RescueCompleted(patient, d.target.Cell, null));
        }

        internal static IEnumerable<Hediff> Tendable(Pawn patient)
        {
            bool atCareLocation = AtCareLocation(patient);
            return patient.health.hediffSet.hediffs.Where(h => h.TendableNow() && (atCareLocation || IsEmergency(h)));
        }

        internal static bool RestrictRound(Pawn doctor, Pawn patient) =>
            doctor?.CurJob?.targetA.Pawn == patient &&
            !MechanicalCare.IsPatient(patient) && !RobotMedicalProfile.OwnsMedicineSelection(patient) &&
            (SearchAndRescueJobContext.IsActive(doctor, doctor.CurJob, SearchAndRescueStage.Treat) ||
             (SearchAndRescueJobContext.IsActive(doctor, doctor.CurJob) ||
              RimkitCompatibility.IsManagedRound(doctor.CurJob)) && !AtCareLocation(patient));

        [ThreadStatic] internal static Pawn EmergencyPatient;
    }

    [HarmonyPatch(typeof(TendUtility), nameof(TendUtility.DoTend))]
    internal static class FieldTendScopePatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Pawn doctor, Pawn patient, out Pawn __state)
        {
            __state = FieldTreatmentBoundary.EmergencyPatient;
            if (!FieldTreatmentBoundary.RestrictRound(doctor, patient)) return true;
            FieldTreatmentBoundary.EmergencyPatient = patient;
            // No medicine or treatment statistics should be consumed after stabilization.
            return patient.health.hediffSet.hediffs.Any(FieldTreatmentBoundary.IsEmergency);
        }
        private static void Finalizer(Pawn __state) => FieldTreatmentBoundary.EmergencyPatient = __state;
    }

    [HarmonyPatch(typeof(TendUtility), nameof(TendUtility.GetOptimalHediffsToTendWithSingleTreatment))]
    internal static class FieldTendSelectionPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(Pawn patient, ref List<Hediff> tendableHediffsInTendPriorityOrder)
        {
            if (FieldTreatmentBoundary.EmergencyPatient != patient) return;
            tendableHediffsInTendPriorityOrder = (tendableHediffsInTendPriorityOrder ?? patient.health.hediffSet.hediffs)
                .Where(FieldTreatmentBoundary.IsEmergency).ToList();
            TendUtility.SortByTendPriority(tendableHediffsInTendPriorityOrder);
        }
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Pawn patient, List<Hediff> outHediffsToTend)
        {
            if (FieldTreatmentBoundary.EmergencyPatient == patient)
                outHediffsToTend.RemoveAll(h => !FieldTreatmentBoundary.IsEmergency(h));
        }
    }
}
