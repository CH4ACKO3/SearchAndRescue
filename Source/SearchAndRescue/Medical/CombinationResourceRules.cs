using System;
using System.Collections.Generic;

namespace SearchAndRescue
{
    internal static class CombinationResourceRules
    {
        // Alternative providers consuming the same ThingDef describe one stock budget.
        // Independent procedures still add their requirements to that budget.
        internal static int SharedBudget<T>(IEnumerable<T> demands,
            Func<T, bool> alternative, Func<T, int> count)
        {
            int independent = 0;
            int largestAlternative = 0;
            foreach (T demand in demands)
            {
                int required = Math.Max(0, count(demand));
                if (alternative(demand)) largestAlternative = Math.Max(largestAlternative, required);
                else independent += required;
            }
            return independent + largestAlternative;
        }
    }
}
