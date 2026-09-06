using RimWorld;
using Verse;
using Verse.AI;

namespace SearchAndRescue
{
    public sealed class WorkGiver_MechFieldRescueHauling : WorkGiver
    {
        public override Job NonScanJob(Pawn pawn)
        {
            if (!Compatibility.IsColonyWorkMech(pawn))
            {
                return null;
            }

            SearchAndRescueCoordinator coordinator = pawn.Map?.GetComponent<SearchAndRescueCoordinator>();
            if (coordinator?.IsFieldResponder(pawn) != true)
            {
                return null;
            }

            return coordinator.TryIssueJob(
                       pawn,
                       SearchAndRescueStage.Supply,
                       RescueWorkProvider.Hauling) ??
                   coordinator.TryIssueJob(
                       pawn,
                       SearchAndRescueStage.Rescue,
                       RescueWorkProvider.Hauling);
        }
    }

    public sealed class WorkGiver_MechFieldRescueDoctor : WorkGiver
    {
        public override Job NonScanJob(Pawn pawn)
        {
            if (!Compatibility.IsColonyWorkMech(pawn))
            {
                return null;
            }

            SearchAndRescueCoordinator coordinator = pawn.Map?.GetComponent<SearchAndRescueCoordinator>();
            if (coordinator?.IsFieldResponder(pawn) != true)
            {
                return null;
            }

            return coordinator.TryIssueJob(
                       pawn,
                       SearchAndRescueStage.Treat,
                       RescueWorkProvider.None) ??
                   coordinator.TryIssueJob(
                       pawn,
                       SearchAndRescueStage.FollowupTreat,
                       RescueWorkProvider.None) ??
                   coordinator.TryIssueJob(
                       pawn,
                       SearchAndRescueStage.Rescue,
                       RescueWorkProvider.Paramedic);
        }
    }
}
