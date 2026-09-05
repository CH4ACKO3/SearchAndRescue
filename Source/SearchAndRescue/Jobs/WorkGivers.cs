using RimWorld;
using Verse;
using Verse.AI;

namespace SearchAndRescue
{
    internal enum SearchAndRescueStage
    {
        Capture,
        Treat,
        FollowupTreat,
        Restock,
        Supply,
        Rescue
    }

    internal enum RescueWorkProvider
    {
        None,
        Hauling,
        Nursing,
        Paramedic,
        Animal
    }

    public sealed class WorkGiver_SearchAndRescueCapture : WorkGiver
    {
        public override Job NonScanJob(Pawn pawn)
        {
            return pawn.Map?.GetComponent<SearchAndRescueCoordinator>()
                ?.TryIssueJob(pawn, SearchAndRescueStage.Capture, RescueWorkProvider.None);
        }
    }

    public sealed class WorkGiver_SearchAndRescueTreat : WorkGiver
    {
        public override Job NonScanJob(Pawn pawn)
        {
            return pawn.Map?.GetComponent<SearchAndRescueCoordinator>()
                ?.TryIssueJob(pawn, SearchAndRescueStage.Treat, RescueWorkProvider.None);
        }
    }

    public sealed class WorkGiver_SearchAndRescueSupportiveCareNursing : WorkGiver
    {
        public override Job NonScanJob(Pawn pawn)
        {
            return pawn.Map?.GetComponent<SearchAndRescueCoordinator>()
                ?.TryIssueJob(pawn, SearchAndRescueStage.Treat, RescueWorkProvider.None);
        }
    }

    public sealed class WorkGiver_SearchAndRescueFollowupTreat : WorkGiver
    {
        public override Job NonScanJob(Pawn pawn)
        {
            return pawn.Map?.GetComponent<SearchAndRescueCoordinator>()
                ?.TryIssueJob(pawn, SearchAndRescueStage.FollowupTreat, RescueWorkProvider.None);
        }
    }

    /// <summary>
    /// Higher-priority materialization point used only by the AllTending admission mode.
    /// It consumes the same graph claim as the ordinary follow-up WorkGiver; keeping the
    /// filter here lets automatic routine care beat vanilla routine tending without also
    /// raising explicitly marked, non-emergency battlefield follow-up above vanilla care.
    /// </summary>
    public sealed class WorkGiver_SearchAndRescueAutomaticRoutineTreat : WorkGiver
    {
        public override Job NonScanJob(Pawn pawn)
        {
            return pawn.Map?.GetComponent<SearchAndRescueCoordinator>()
                ?.TryIssueAutomaticRoutineTreatment(pawn);
        }
    }

    public sealed class WorkGiver_SearchAndRescueRescueHauling : WorkGiver
    {
        public override Job NonScanJob(Pawn pawn)
        {
            SearchAndRescueCoordinator coordinator = pawn.Map?.GetComponent<SearchAndRescueCoordinator>();
            return coordinator?.TryIssueJob(pawn, SearchAndRescueStage.Supply, RescueWorkProvider.Hauling)
                   ?? coordinator?.TryIssueJob(pawn, SearchAndRescueStage.Rescue, RescueWorkProvider.Hauling);
        }
    }

    public sealed class WorkGiver_SearchAndRescueRescueNursing : WorkGiver
    {
        public override Job NonScanJob(Pawn pawn)
        {
            return pawn.Map?.GetComponent<SearchAndRescueCoordinator>()
                ?.TryIssueJob(pawn, SearchAndRescueStage.Rescue, RescueWorkProvider.Nursing);
        }
    }

    public sealed class WorkGiver_SearchAndRescueRescueParamedic : WorkGiver
    {
        public override Job NonScanJob(Pawn pawn)
        {
            return pawn.Map?.GetComponent<SearchAndRescueCoordinator>()
                ?.TryIssueJob(pawn, SearchAndRescueStage.Rescue, RescueWorkProvider.Paramedic);
        }
    }
}
