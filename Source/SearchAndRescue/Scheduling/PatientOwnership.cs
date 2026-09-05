using RimWorld;
using Verse;
using Verse.AI;

namespace SearchAndRescue
{
    internal static class PatientOwnership
    {
        internal static bool HasExternalOwner(Pawn patient, PatientJobRole roles = PatientJobRole.Any)
        {
            Map map = patient?.MapHeld;
            if (map == null)
            {
                return false;
            }

            if (CompatibilityRegistry.HasFacilityOrLordOwner(map, patient, roles))
            {
                return true;
            }

            if (patient.ParentHolder is Pawn_CarryTracker carryTracker &&
                carryTracker.pawn?.CurJob is Job carryJob &&
                JobOwnershipRules.ExternalCarryBlocksScheduling(
                    true, SearchAndRescueJobContext.IsActive(carryTracker.pawn, carryJob)))
            {
                // A patient physically held by a non-SAR carrier is externally owned even
                // when that mod uses an unregistered JobDef. This is the final safety net for
                // allied rescue, multi-carrier jobs, trained animals and future carry mods.
                return true;
            }

            foreach (Pawn worker in map.mapPawns.AllPawnsSpawned)
            {
                Job job = worker?.CurJob;
                if (job != null && (CompatibilityRegistry.RolesFor(job.def) & roles) != 0 &&
                    CompatibilityRegistry.PatientFor(worker, job, roles) == patient &&
                    !SearchAndRescueJobContext.IsActive(worker, job))
                {
                    return true;
                }

                // TryTakeOrderedJob reserves a Shift-queued order immediately, even though
                // it is not yet CurJob. Treat that explicit queue entry as a durable player
                // ownership lease as well. Otherwise the graph keeps matching another SAR
                // doctor/carrier to a patient that the player has already claimed, producing
                // failed reservations, standby churn, or an apparent attempt to steal control.
                // Only player-forced queue entries qualify: ordinary continuation/cleaning
                // jobs inserted by other mods must not suppress emergency care indefinitely.
                JobQueue queue = worker?.jobs?.jobQueue;
                if (queue == null)
                {
                    continue;
                }
                foreach (QueuedJob queuedJob in queue)
                {
                    Job queued = queuedJob?.job;
                    if (queued?.playerForced == true &&
                        (CompatibilityRegistry.RolesFor(queued.def) & roles) != 0 &&
                        CompatibilityRegistry.PatientFor(worker, queued, roles) == patient)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
