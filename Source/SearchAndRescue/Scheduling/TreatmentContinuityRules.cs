using System;

namespace SearchAndRescue
{
    internal static class TreatmentContinuityRules
    {
        internal const int DurationTicks = 600;

        internal static double Weight(int remainingTicks) => remainingTicks <= 0 ? 0d :
            20000d + 50000d * Math.Min(1d, remainingTicks / (double)DurationTicks);

        internal static bool ShouldReplace(bool existingLive, bool existingCompleted,
            bool incomingCompleted, double existingWeight, double incomingWeight)
        {
            if (!existingLive) return true;
            // Actual care identifies the latest treating doctor. A pickup plan must not
            // displace that evidence merely because its lease has a longer lifetime.
            if (incomingCompleted) return true;
            return !existingCompleted && incomingWeight >= existingWeight;
        }
    }
}
