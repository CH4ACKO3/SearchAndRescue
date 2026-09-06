using RimWorld;
using Verse;
using Verse.AI;

namespace SearchAndRescue
{
    public sealed class JobDriver_EvacuateToPoint : JobDriver_DeliverPawnToCell
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // Keep other owners' reservations intact when a competing job wins the race.
            return pawn.Reserve(job.GetTarget(TargetIndex.A), job, 1, -1, null, errorOnFailed);
        }
    }
}
