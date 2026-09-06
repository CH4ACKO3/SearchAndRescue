namespace SearchAndRescue
{
    /// <summary>
    /// Stable public Def names used by Fortified Features Framework and Dead Man's Switch.
    /// The underlying jobs repair a pawn and must participate in the shared treatment lease.
    /// </summary>
    internal static class DmsCompatibility
    {
        internal static readonly string[] FrameworkRepairJobs =
        {
            "FFF_RepairMech_Overseer"
        };

        internal static readonly string[] JointOperationsRepairJobs =
        {
            "Tinker_RepairAutomatroid"
        };

        internal const string JointOperationsRepairWorkGiver =
            "PRT_Mod.WorkGivers.WorkGiver_RepairAutomatroid_Smithing";
    }
}
