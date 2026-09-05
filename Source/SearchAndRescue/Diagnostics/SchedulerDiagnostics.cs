using LudeonTK;
using Verse;

namespace SearchAndRescue
{
    internal static class SchedulerDiagnostics
    {
        [DebugAction("Search and Rescue", "Dump scheduler state",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DumpSchedulerState()
        {
            SearchAndRescueCoordinator coordinator = Find.CurrentMap?
                .GetComponent<SearchAndRescueCoordinator>();
            Log.Message(coordinator?.DebugDescribeScheduler() ??
                        "[Search and Rescue] No active map coordinator.");
        }
    }
}
