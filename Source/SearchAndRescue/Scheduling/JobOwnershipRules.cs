namespace SearchAndRescue
{
    internal static class JobOwnershipRules
    {
        internal static bool ExternalCarryBlocksScheduling(bool hasCurrentJob, bool activeSarJob)
        {
            // Unknown external JobDefs are still protected at the planning boundary.
            return hasCurrentJob && !activeSarJob;
        }

        internal static bool PreserveManagedCarry(bool activeRescue, bool registeredTransport)
        {
            // Cleanup requires a live rescue or a registered transport targeting this patient.
            // Keep this distinct from the conservative planning rule for unknown JobDefs.
            return activeRescue || registeredTransport;
        }
    }
}
