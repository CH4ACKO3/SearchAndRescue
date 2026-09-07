namespace SearchAndRescue
{
    internal static class PendingAssignmentRules
    {
        internal static bool IsLive(int expiresAt, int now)
        {
            return expiresAt <= 0 || now < expiresAt;
        }
    }
}
