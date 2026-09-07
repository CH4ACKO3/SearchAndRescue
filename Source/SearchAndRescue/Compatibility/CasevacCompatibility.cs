using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SearchAndRescue
{
    internal static partial class Compatibility
    {
        private static readonly System.Func<Pawn, bool, float> CasevacBaseMovement =
            AccessTools.MethodDelegate<System.Func<Pawn, bool, float>>(
                AccessTools.Method(typeof(Pawn), "TicksPerMove", new[] { typeof(bool) }));

        internal static float CasevacBaseTicksPerMove(Pawn pawn) => CasevacBaseMovement(pawn, false);
        internal static bool FieldRescueEnabled(Pawn worker) => worker != null && FieldRescueWorkPriority(worker) > 0;
        internal static bool IsCasevac(Job job) => job?.def?.defName == "CP_CasevacRescue" ||
            job?.def?.defName == "CP_CasevacCapture";

        internal static int CasevacPriority(Pawn worker)
        {
            WorkTypeDef type = DefDatabase<WorkTypeDef>.GetNamedSilentFail("CP_CasevacRescue");
            WorkGiverDef giver = DefDatabase<WorkGiverDef>.GetNamedSilentFail("SAR_AutomaticCasevac");
            if (type == null || giver == null || worker?.RaceProps?.Humanlike != true ||
                worker.WorkTypeIsDisabled(type) || worker.WorkTagIsDisabled(WorkTags.Caring) ||
                !FieldRescueEnabled(worker)) return 0;
            int priority = DetailedWorkPriority(worker, giver, type);
            int native = DetailedWorkPriority(worker,
                DefDatabase<WorkGiverDef>.GetNamedSilentFail("CP_CasevacRescue"), type);
            return priority > 0 && native > 0 ? System.Math.Max(priority, native) : 0;
        }

        internal static bool CanUseCasevac(Pawn worker) => CasevacPriority(worker) > 0 &&
            DefDatabase<JobDef>.GetNamedSilentFail("CP_CasevacRescue") != null;

        internal static Job MakeCasevacJob(Pawn patient, Building_Bed bed)
        {
            if (patient == null || bed == null || bed.Destroyed || !bed.Spawned) return null;
            JobDef def = DefDatabase<JobDef>.GetNamedSilentFail(patient.IsPrisonerOfColony
                ? "CP_CasevacCapture" : "CP_CasevacRescue");
            if (def == null || bed == null) return null;
            Job job = JobMaker.MakeJob(def, patient, bed);
            job.count = 1;
            return job;
        }

        internal static bool CasevacBedUsable(Pawn worker, Pawn patient, Building_Bed bed) =>
            worker?.Spawned == true && bed?.Map == worker.Map &&
            IsSafeRescueBed(bed, patient) && !bed.IsForbidden(worker) &&
            worker.CanReach(bed, PathEndMode.Touch, Danger.Deadly);

        internal static bool CanJoinCasevac(Pawn helper, Pawn leader)
        {
            Job job = leader?.CurJob;
            Pawn patient = job?.targetA.Pawn;
            if (!IsCasevac(job) || patient == null || patient.Dead || patient.CurrentBed() != null ||
                leader == helper || leader.Map != helper.Map ||
                !CasevacBedUsable(helper, patient, job.targetB.Thing as Building_Bed) ||
                !helper.CanReach(leader, PathEndMode.Touch, Danger.Deadly)) return false;
            List<Pawn> team = helper.Map.mapPawns.AllPawnsSpawned.Where(p => IsCasevac(p.CurJob) &&
                p.CurJob.targetA == job.targetA && p.CurJob.targetB == job.targetB).ToList();
            Pawn carrier = (patient.ParentHolder as Pawn_CarryTracker)?.pawn ?? leader;
            // Native admission compares unboosted movement, not the public getters
            // patched by CASEVAC once helpers reach the carrier.
            return team.Count < 4 && CasevacBaseTicksPerMove(helper) <= CasevacBaseTicksPerMove(carrier);
        }
    }

    // Let native joining continue for non-SAR workers. SAR responders must service
    // reachable unassigned casualties before spending their capacity on an existing team.
    [HarmonyPatch]
    internal static class CasevacNativeJoinPriorityPatch
    {
        private static bool Prepare() => AccessTools.TypeByName("Casevac.WorkGiver_Casevac") != null;
        private static MethodBase TargetMethod() => AccessTools.Method(
            AccessTools.TypeByName("Casevac.WorkGiver_Casevac"), "HasJobOnThing");
        private static void Postfix(Pawn pawn, Thing t, bool forced, ref bool __result)
        {
            if (forced || !__result || !Compatibility.FieldRescueEnabled(pawn)) return;
            var coordinator = pawn.Map?.GetComponent<SearchAndRescueCoordinator>();
            __result = Compatibility.CanUseCasevac(pawn) &&
                coordinator?.HasUnservedCasevacTarget(pawn) == false &&
                Compatibility.CanJoinCasevac(pawn, t as Pawn);
        }
    }
}
