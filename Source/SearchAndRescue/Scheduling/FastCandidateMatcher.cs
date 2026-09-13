#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace SearchAndRescue
{
    // Patient-first matching trades global score optimality for fewer expensive edge
    // evaluations. Exhaust an unsuccessful shortlist before expanding to all workers.
    internal static class FastCandidateMatcher
    {
        internal static List<Match<W, P>> Match<W, P>(IReadOnlyList<W> workers,
            IReadOnlyList<P> patients, Func<P, double> urgency, Func<W, P, double> distance,
            Func<W, P, double> score, int shortlist = 4, Func<W, P, bool> preferred = null,
            Action<Match<W, P>> onSelected = null)
        {
            var available = new HashSet<W>(workers);
            var result = new List<Match<W, P>>();
            foreach (P patient in patients.OrderByDescending(urgency))
            {
                if (available.Count == 0) break;
                var ordered = workers.Where(available.Contains)
                    .OrderByDescending(w => preferred?.Invoke(w, patient) == true)
                    .ThenBy(w => distance(w, patient));
                W best = default(W);
                double bestScore = 0;
                int examined = 0;
                foreach (W worker in ordered)
                {
                    if (examined >= shortlist && bestScore > 0) break;
                    double value = score(worker, patient);
                    examined++;
                    if (!double.IsNaN(value) && !double.IsInfinity(value) && value > bestScore)
                    { best = worker; bestScore = value; }
                }
                if (bestScore <= 0) continue;
                available.Remove(best);
                var match = new Match<W, P>(best, patient, bestScore);
                result.Add(match);
                onSelected?.Invoke(match);
            }
            return result;
        }
    }
}
