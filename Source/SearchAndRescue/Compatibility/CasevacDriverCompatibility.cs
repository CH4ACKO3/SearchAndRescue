using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SearchAndRescue
{
    internal static class CasevacDriverCompatibility
    {
        internal static Type DriverType => AccessTools.TypeByName("Casevac.JobDriver_CasevacRescue");
        [ThreadStatic] internal static JobDriver InitializingDriver;

        internal static List<Pawn> CurrentTeam(JobDriver driver)
        {
            Job job = driver.job;
            return driver.pawn.Map?.mapPawns.AllPawnsSpawned.Where(p =>
                Compatibility.IsCasevac(p.CurJob) && p.CurJob.targetA == job.targetA &&
                p.CurJob.targetB == job.targetB).ToList() ?? new List<Pawn>();
        }

        internal static bool InBedReleaseScope(ReservationManager manager, LocalTargetInfo target)
        {
            JobDriver driver = InitializingDriver;
            return driver?.pawn?.Map?.reservationManager == manager &&
                Compatibility.IsCasevac(driver?.job) && target == driver.job.targetB;
        }
    }

    [HarmonyPatch]
    internal static class CasevacFinishReservationPatch
    {
        private static bool Prepare() => CasevacDriverCompatibility.DriverType != null;
        private static MethodBase TargetMethod() => AccessTools.Method(CasevacDriverCompatibility.DriverType, "ReleaseAndMakeOtherReserveBed");
        private static bool Prefix(JobDriver __instance)
        {
            Pawn worker = __instance.pawn;
            Job endingJob = __instance.job;
            Map map = worker?.Map;
            if (map == null || endingJob?.targetB.Thing is not Building_Bed bed) return false;
            // JobTracker clears reservations BEFORE running finish actions, but keeps
            // CurJob until afterwards. Never re-reserve for this ending job: it will
            // be pooled and its def cleared while the reservation still refers to it.
            while (map.reservationManager.ReservedBy(bed, worker, endingJob))
                map.reservationManager.Release(bed, worker, endingJob);
            Pawn patient = endingJob.targetA.Thing as Pawn;
            if (patient == null || patient.Dead || patient.CurrentBed() == bed ||
                !Compatibility.IsSafeRescueBed(bed, patient)) return false;
            List<Pawn> remaining = CasevacDriverCompatibility.CurrentTeam(__instance)
                .Where(p => p != worker && p.jobs.curDriver != null && !p.jobs.curDriver.ended).ToList();
            if (remaining.Any(p => map.reservationManager.ReservedBy(bed, p, p.CurJob))) return false;
            Pawn carrier = (patient.ParentHolder as Pawn_CarryTracker)?.pawn;
            Pawn next = remaining.OrderByDescending(p => p == carrier)
                .FirstOrDefault(p => p.CanReserve(bed, bed.SleepingSlotsCount, 0));
            next?.Reserve(bed, next.CurJob, bed.SleepingSlotsCount, 0, errorOnFailed: false);
            return false;
        }
    }

    [HarmonyPatch]
    internal static class CasevacCurrentTeamPatch
    {
        private static bool Prepare() => CasevacDriverCompatibility.DriverType != null;
        private static MethodBase TargetMethod() => AccessTools.PropertyGetter(CasevacDriverCompatibility.DriverType, "Rescuers");
        private static bool Prefix(JobDriver __instance, ref List<Pawn> __result)
        {
            // Native cache eviction uses && between job/target mismatches. A pawn
            // switched to another CASEVAC patient can remain in the old team forever,
            // receive speed bonuses and have its unrelated job stopped at delivery.
            __result = CasevacDriverCompatibility.CurrentTeam(__instance);
            return false;
        }
    }

    [HarmonyPatch]
    internal static class CasevacReservationScopePatch
    {
        private static bool Prepare() => CasevacDriverCompatibility.DriverType != null;
        private static MethodBase TargetMethod() => AccessTools.Method(CasevacDriverCompatibility.DriverType, "MakeNewToils");
        private static void Postfix(JobDriver __instance, ref IEnumerable<Toil> __result) => __result = Wrap(__instance, __result);

        private static IEnumerable<Toil> Wrap(JobDriver driver, IEnumerable<Toil> toils)
        {
            foreach (Toil toil in toils)
            {
                Action original = toil.initAction;
                if (original != null)
                    toil.initAction = () =>
                    {
                        // A doctor may claim the patient while this team walks over.
                        // Native CASEVAC reserves only the bed before starting work.
                        if (toil.debugName == "StartCarryThing" &&
                            !driver.pawn.CanReserve(driver.job.targetA))
                        {
                            driver.EndJobWith(JobCondition.Incompletable);
                            return;
                        }
                        JobDriver previous = CasevacDriverCompatibility.InitializingDriver;
                        CasevacDriverCompatibility.InitializingDriver = driver;
                        try { original(); }
                        finally { CasevacDriverCompatibility.InitializingDriver = previous; }
                    };
                yield return toil;
            }
        }
    }

    [HarmonyPatch(typeof(ReservationManager), nameof(ReservationManager.Release))]
    internal static class CasevacReleaseOwnedBedPatch
    {
        private static bool Prefix(ReservationManager __instance, LocalTargetInfo target, Pawn claimant, Job job)
        {
            if (!CasevacDriverCompatibility.InBedReleaseScope(__instance, target) ||
                !Compatibility.IsCasevac(job) || job.targetB != target) return true;
            // Native delivery releases every team reservation then executes another
            // Release toil. Make that repeated release idempotent, scoped to CASEVAC.
            return __instance.ReservedBy(target, claimant, job);
        }
    }

    [HarmonyPatch(typeof(ReservationManager), nameof(ReservationManager.ReleaseAllForTarget))]
    internal static class CasevacReleaseTeamBedPatch
    {
        private static bool Prefix(ReservationManager __instance, Thing t)
        {
            if (!CasevacDriverCompatibility.InBedReleaseScope(__instance, t)) return true;
            // A double bed may also be reserved by another patient or ordinary job.
            // CASEVAC delivery owns only this team's reservations, not the whole bed.
            foreach (Pawn worker in CasevacDriverCompatibility.CurrentTeam(CasevacDriverCompatibility.InitializingDriver))
                if (__instance.ReservedBy(t, worker, worker.CurJob))
                    __instance.Release(t, worker, worker.CurJob);
            return false;
        }
    }
}
