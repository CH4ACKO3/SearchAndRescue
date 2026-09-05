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

    internal static class AssignmentStageRules
    {
        internal static bool Matches(SearchAndRescueStage actual, SearchAndRescueStage requested)
        {
            return actual == requested || requested == SearchAndRescueStage.Treat &&
                   (actual == SearchAndRescueStage.Restock || actual == SearchAndRescueStage.FollowupTreat) ||
                   requested == SearchAndRescueStage.Rescue && actual == SearchAndRescueStage.Supply;
        }

        internal static bool IsTreatment(SearchAndRescueStage stage)
        {
            return stage == SearchAndRescueStage.Treat || stage == SearchAndRescueStage.FollowupTreat;
        }
    }
}
