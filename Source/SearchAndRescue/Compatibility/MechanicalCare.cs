using System;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace SearchAndRescue
{
    // Mechanical patients share scheduling and ownership, with a native repair provider.
    internal static class MechanicalCare
    {
        internal static bool IsPatient(Pawn pawn) => ModsConfig.BiotechActive &&
            pawn != null && !pawn.Dead && pawn.RaceProps.IsMechanoid &&
            pawn.Faction == Faction.OfPlayer && pawn.needs?.energy != null &&
            pawn.TryGetComp<CompMechRepairable>() != null;

        internal static bool NeedsRepair(Pawn pawn) => IsPatient(pawn) &&
            pawn.TryGetComp<CompMechRepairable>().autoRepair && MechRepairUtility.CanRepair(pawn);

        internal static int WorkPriority(Pawn worker)
        {
            WorkGiverDef provider = DefDatabase<WorkGiverDef>.GetNamedSilentFail("RepairMech");
            if (!ModsConfig.BiotechActive || worker?.workSettings == null || provider == null ||
                !MechanitorUtility.IsMechanitor(worker) || worker.WorkTypeIsDisabled(provider.workType) ||
                worker.WorkTypeIsDisabled(SearchAndRescueDefOf.SAR_FieldRescue) ||
                worker.WorkTagIsDisabled(provider.workTags) ||
                worker.health?.capacities?.CapableOf(PawnCapacityDefOf.Manipulation) != true)
                return 0;

            return Compatibility.MechanicalRepairWorkPriority(worker, provider);
        }

        internal static bool CanRepairWork(Pawn worker) => WorkPriority(worker) > 0;

        internal static bool CanRepair(Pawn worker, Pawn patient)
        {
            if (!CanRepairWork(worker) || !NeedsRepair(patient) || !patient.Spawned ||
                worker == patient || worker.Map != patient.Map || patient.IsForbidden(worker)) return false;
            var provider = DefDatabase<WorkGiverDef>.GetNamedSilentFail("RepairMech")?.Worker as WorkGiver_Scanner;
            // Probe the native provider past the SAR ownership gate. Reservations and the
            // auto-repair toggle are checked here; the resulting job remains automatic.
            return provider != null && !provider.ShouldSkip(worker) &&
                worker.CanReserve(patient) && provider.HasJobOnThing(worker, patient, forced: true) &&
                worker.CanReach(patient, PathEndMode.Touch, Danger.Deadly);
        }

        internal static Job MakeJob(Pawn worker, Pawn patient) =>
            CanRepair(worker, patient) ? JobMaker.MakeJob(JobDefOf.RepairMech, patient) : null;

        internal static float Damage(Pawn patient) => IsPatient(patient)
            ? patient.health.hediffSet.hediffs.Sum(hediff =>
                hediff is Hediff_Injury ? hediff.Severity : hediff is Hediff_MissingPart ? 20f : 0f)
            : 0f;
    }
}
