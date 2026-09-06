using System;

namespace SearchAndRescue
{
    internal static class InfectionPriorityRules
    {
        internal static double Urgency(double severity, double lethalSeverity, double immunity, bool tendable)
        {
            if (immunity >= 1d || severity <= 0d) return 0d;
            double progression = Math.Max(0d, Math.Min(1d, severity / Math.Max(0.001d, lethalSeverity)));
            double immuneDeficit = Math.Max(0d, progression - Math.Max(0d, immunity));
            double urgency = 0.8d + progression * 1.8d + immuneDeficit * 1.2d;
            // Treatment cooldown must not generate repeat tend jobs. Keep a smaller risk
            // contribution for evacuation while the next useful treatment is unavailable.
            return tendable ? urgency : urgency * 0.35d;
        }
    }
}
