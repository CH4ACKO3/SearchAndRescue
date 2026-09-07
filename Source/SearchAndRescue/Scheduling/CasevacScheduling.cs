using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace SearchAndRescue
{
    public sealed partial class SearchAndRescueCoordinator
    {
        internal bool HasUnservedCasevacTarget(Pawn worker)
        {
            int now = Find.TickManager.TicksGame;
            return map.mapPawns.AllPawnsSpawned.Any(patient =>
                !activeByTarget.ContainsKey(patient) &&
                !pendingByWorker.Values.Any(p => p.Target == patient && p.Stage != SearchAndRescueStage.Supply) &&
                TargetReadyForStage(patient, SearchAndRescueStage.Rescue, now) &&
                worker.CanReserveAndReach(patient, PathEndMode.Touch, Danger.Deadly) &&
                TryFindRescueDestination(worker, patient, out _, out _));
        }

        internal Job TryJoinOrUpgradeCasevac(Pawn worker)
        {
            if (!Compatibility.CanUseCasevac(worker) || !IsFieldResponder(worker) ||
                !WorkerReadyForStage(worker, SearchAndRescueStage.Rescue) ||
                pendingByWorker.ContainsKey(worker) || HasUnservedCasevacTarget(worker)) return null;

            // Native jobs discover their team from the shared patient and destination.
            // Helpers are external transport owners, not additional primary SAR claims.
            foreach (Pawn leader in map.mapPawns.AllPawnsSpawned
                         .OrderBy(p => p.Position.DistanceToSquared(worker.Position)))
            {
                if (Compatibility.CanJoinCasevac(worker, leader))
                    return Compatibility.MakeCasevacJob(leader.CurJob.targetA.Pawn,
                        leader.CurJob.targetB.Thing as Building_Bed);
            }

            foreach (ActiveAssignment assignment in activeByTarget.Values.ToList()
                         .Where(a => a.Worker != null)
                         .OrderBy(a => a.Worker.Position.DistanceToSquared(worker.Position)))
            {
                Pawn leader = assignment.Worker;
                Pawn patient = assignment.Target;
                Job oldJob = leader?.CurJob;
                if (assignment.Stage != SearchAndRescueStage.Rescue || leader == worker ||
                    !AssignmentJobStillRunning(assignment) || oldJob.playerForced || leader.Drafted ||
                    Compatibility.IsCasevac(oldJob) ||
                    (oldJob.def != JobDefOf.Rescue && oldJob.def != JobDefOf.Capture) ||
                    oldJob.targetB.Thing is not Building_Bed bed ||
                    !Compatibility.CasevacBedUsable(worker, patient, bed) ||
                    leader.carryTracker?.CarriedThing != patient ||
                    worker.TicksPerMoveCardinal > leader.TicksPerMoveCardinal ||
                    // Joining near the carrier must leave enough route to benefit from it.
                    worker.Position.DistanceToSquared(leader.Position) > 64 ||
                    leader.Position.DistanceToSquared(bed.Position) < 144 ||
                    !worker.CanReach(leader, PathEndMode.Touch, Danger.Deadly)) continue;

                Job upgraded = Compatibility.MakeCasevacJob(patient, bed);
                if (upgraded == null) continue;
                // A bounded handoff keeps CASEVAC-disabled carriers out of that work
                // type. Drop before ending the old job, then publish the new primary.
                if (!leader.carryTracker.TryDropCarriedThing(leader.Position, ThingPlaceMode.Near, out _)) continue;
                activeClaims.ReleasePrimary(patient);
                leader.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                activeClaims.Register(new ActiveAssignment(worker, patient, upgraded,
                    SearchAndRescueStage.Rescue, assignment.Destination, Find.TickManager.TicksGame,
                    CountUntendedHediffs(patient), patient.health.hediffSet.BleedRateTotal,
                    Compatibility.FieldEmergencySeverity(patient), assignment.Origin,
                    GetBloodLossSeverity(patient), GetHediffSeverity(patient, "Hemodilution")));
                RequestScheduleRebuild(maintenance: true);
                return upgraded;
            }
            return null;
        }
    }
}
