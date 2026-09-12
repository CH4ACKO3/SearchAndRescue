using System;
using System.Collections.Generic;
using System.Linq;

namespace SearchAndRescue
{
    internal static class WeightedBipartiteMatcher
    {
        private static double ValidWeight(double value) =>
            double.IsNaN(value) || double.IsInfinity(value) || value <= 0d ? 0d : value;

        public static List<Match<TWorker, TTarget>> MaximumWeight<TWorker, TTarget>(
            IReadOnlyList<TWorker> workers,
            IReadOnlyList<TTarget> targets,
            Func<TWorker, TTarget, double> weightSelector)
        {
            var result = new List<Match<TWorker, TTarget>>();
            if (workers.Count == 0 || targets.Count == 0) return result;

            // Put the smaller partition on the augmenting side. Zero-weight invalid edges
            // complete the rectangular assignment, then disappear from the returned matching.
            // Every partial positive matching can be completed this way without dummy rows.
            bool transpose = workers.Count > targets.Count;
            int rows = Math.Min(workers.Count, targets.Count);
            int columns = Math.Max(workers.Count, targets.Count);
            var weights = new double[rows + 1, columns + 1];
            // Preserve worker-major callback order: callers may collect per-edge plans.
            for (int w = 0; w < workers.Count; w++)
                for (int t = 0; t < targets.Count; t++)
                    weights[transpose ? t + 1 : w + 1, transpose ? w + 1 : t + 1] =
                        ValidWeight(weightSelector(workers[w], targets[t]));

            var rowPotential = new double[rows + 1];
            var columnPotential = new double[columns + 1];
            var columnRow = new int[columns + 1];
            var previousColumn = new int[columns + 1];
            var minimum = new double[columns + 1];
            var used = new bool[columns + 1];
            for (int row = 1; row <= rows; row++)
            {
                columnRow[0] = row;
                int column0 = 0;
                Array.Clear(used, 0, used.Length);
                for (int c = 1; c <= columns; c++) minimum[c] = double.PositiveInfinity;
                do
                {
                    used[column0] = true;
                    int row0 = columnRow[column0];
                    double delta = double.PositiveInfinity;
                    int column1 = 0;
                    for (int c = 1; c <= columns; c++)
                    {
                        if (used[c]) continue;
                        double cost = -weights[row0, c] - rowPotential[row0] - columnPotential[c];
                        if (cost < minimum[c])
                        {
                            minimum[c] = cost;
                            previousColumn[c] = column0;
                        }
                        if (minimum[c] < delta)
                        {
                            delta = minimum[c];
                            column1 = c;
                        }
                    }
                    for (int c = 0; c <= columns; c++)
                    {
                        if (used[c])
                        {
                            rowPotential[columnRow[c]] += delta;
                            columnPotential[c] -= delta;
                        }
                        else if (c > 0) minimum[c] -= delta;
                    }
                    column0 = column1;
                } while (columnRow[column0] != 0);
                do
                {
                    int column1 = previousColumn[column0];
                    columnRow[column0] = columnRow[column1];
                    column0 = column1;
                } while (column0 != 0);
            }

            // Return target-major order, including when the matrix was transposed.
            var workerForTarget = new int[targets.Count];
            for (int t = 0; t < targets.Count; t++) workerForTarget[t] = -1;
            for (int c = 1; c <= columns; c++)
            {
                int r = columnRow[c];
                if (r == 0 || weights[r, c] <= 0d) continue;
                workerForTarget[transpose ? r - 1 : c - 1] = transpose ? c - 1 : r - 1;
            }
            for (int t = 0; t < targets.Count; t++)
            {
                int w = workerForTarget[t];
                if (w >= 0) result.Add(new Match<TWorker, TTarget>(workers[w], targets[t],
                    weights[transpose ? t + 1 : w + 1, transpose ? w + 1 : t + 1]));
            }
            return result;
        }

        private readonly struct Edge
        {
            internal readonly int Worker, Target;
            internal readonly double Weight;
            internal Edge(int worker, int target, double weight)
            { Worker = worker; Target = target; Weight = weight; }
        }

        public static List<Match<TWorker, TTarget>> ApproximateWeight<TWorker, TTarget>(
            IReadOnlyList<TWorker> workers,
            IReadOnlyList<TTarget> targets,
            Func<TWorker, TTarget, double> weightSelector)
        {
            var result = new List<Match<TWorker, TTarget>>();
            if (workers.Count == 0 || targets.Count == 0) return result;
            var weights = new double[workers.Count, targets.Count];
            for (int w = 0; w < workers.Count; w++)
                for (int t = 0; t < targets.Count; t++)
                    weights[w, t] = ValidWeight(weightSelector(workers[w], targets[t]));

            // Grow disjoint paths, always taking the best edge to a remaining vertex.
            // Each vertex scans its opposite partition once: O(W*P) after scoring.
            // For every optimal edge, the first removed endpoint picked an edge at
            // least as heavy. Those charges are distinct, so total path weight >= OPT.
            // Optimal matching on each path retains at least half its edge weight.
            int vertices = workers.Count + targets.Count;
            var removed = new bool[vertices];
            var path = new List<Edge>(Math.Min(workers.Count, targets.Count) * 2);
            var best = new double[vertices + 1];
            var chosen = new Edge[targets.Count];
            for (int seed = 0; seed < vertices; seed++)
            {
                if (removed[seed]) continue;
                path.Clear();
                int current = seed;
                while (true)
                {
                    removed[current] = true;
                    int next = -1;
                    double weight = 0d;
                    if (current < workers.Count)
                    {
                        for (int t = 0; t < targets.Count; t++)
                            if (!removed[workers.Count + t] && weights[current, t] > weight)
                            { next = workers.Count + t; weight = weights[current, t]; }
                    }
                    else
                    {
                        int t = current - workers.Count;
                        for (int w = 0; w < workers.Count; w++)
                            if (!removed[w] && weights[w, t] > weight)
                            { next = w; weight = weights[w, t]; }
                    }
                    if (next < 0) break;
                    path.Add(current < workers.Count
                        ? new Edge(current, next - workers.Count, weight)
                        : new Edge(next, current - workers.Count, weight));
                    current = next;
                }
                // Dynamic programming selects the best non-adjacent path edges.
                best[0] = 0d;
                for (int i = 1; i <= path.Count; i++)
                    best[i] = Math.Max(best[i - 1], path[i - 1].Weight + (i > 1 ? best[i - 2] : 0d));
                for (int i = path.Count; i > 0;)
                {
                    if (best[i] > best[i - 1])
                    {
                        Edge edge = path[i - 1];
                        chosen[edge.Target] = edge;
                        i -= 2;
                    }
                    else i--;
                }
            }
            // Complete any directly available pairs the path solution left open.
            // This preserves the score bound and makes the result maximal, still O(W*P).
            var usedWorkers = new bool[workers.Count];
            for (int t = 0; t < targets.Count; t++)
                if (chosen[t].Weight > 0d) usedWorkers[chosen[t].Worker] = true;
            for (int w = 0; w < workers.Count; w++)
            {
                if (usedWorkers[w]) continue;
                int target = -1;
                double weight = 0d;
                for (int t = 0; t < targets.Count; t++)
                    if (chosen[t].Weight == 0d && weights[w, t] > weight)
                    { target = t; weight = weights[w, t]; }
                if (target >= 0) chosen[target] = new Edge(w, target, weight);
            }
            for (int t = 0; t < targets.Count; t++)
            {
                Edge edge = chosen[t];
                if (edge.Weight > 0d)
                    result.Add(new Match<TWorker, TTarget>(workers[edge.Worker], targets[t], edge.Weight));
            }
            return result;
        }

        /// <summary>
        /// Matches workers to logical targets while allowing several mutually exclusive
        /// options for each target.  The best option is selected independently for every
        /// worker/target edge before the Hungarian solve, so a target can consume at most one
        /// worker without discarding duplicate matches after the solve.
        /// </summary>
        public static List<Match<TWorker, TOption>> MaximumWeightGrouped<TWorker, TTarget, TOption>(
            IReadOnlyList<TWorker> workers,
            IReadOnlyList<TTarget> targets,
            Func<TTarget, IEnumerable<TOption>> optionsForTarget,
            Func<TWorker, TOption, double> weightSelector)
        {
            return MatchGrouped(workers, targets, optionsForTarget, weightSelector, false);
        }

        public static List<Match<TWorker, TOption>> ApproximateWeightGrouped<TWorker, TTarget, TOption>(
            IReadOnlyList<TWorker> workers, IReadOnlyList<TTarget> targets,
            Func<TTarget, IEnumerable<TOption>> optionsForTarget,
            Func<TWorker, TOption, double> weightSelector)
        {
            return MatchGrouped(workers, targets, optionsForTarget, weightSelector, true);
        }

        private static List<Match<TWorker, TOption>> MatchGrouped<TWorker, TTarget, TOption>(
            IReadOnlyList<TWorker> workers, IReadOnlyList<TTarget> targets,
            Func<TTarget, IEnumerable<TOption>> optionsForTarget,
            Func<TWorker, TOption, double> weightSelector, bool approximate)
        {
            TOption[,] bestOptions = new TOption[workers.Count, targets.Count];
            double[,] bestWeights = new double[workers.Count, targets.Count];
            for (int workerIndex = 0; workerIndex < workers.Count; workerIndex++)
            {
                for (int targetIndex = 0; targetIndex < targets.Count; targetIndex++)
                {
                    IEnumerable<TOption> options = optionsForTarget(targets[targetIndex]);
                    if (options == null)
                    {
                        continue;
                    }

                    foreach (TOption option in options)
                    {
                        double weight = ValidWeight(weightSelector(workers[workerIndex], option));
                        if (!double.IsNaN(weight) && weight > bestWeights[workerIndex, targetIndex])
                        {
                            bestWeights[workerIndex, targetIndex] = weight;
                            bestOptions[workerIndex, targetIndex] = option;
                        }
                    }
                }
            }

            List<int> workerIndices = new List<int>(workers.Count);
            List<int> targetIndices = new List<int>(targets.Count);
            for (int index = 0; index < workers.Count; index++)
            {
                workerIndices.Add(index);
            }
            for (int index = 0; index < targets.Count; index++)
            {
                targetIndices.Add(index);
            }

            List<Match<int, int>> grouped = approximate
                ? ApproximateWeight(workerIndices, targetIndices, (w, t) => bestWeights[w, t])
                : MaximumWeight(workerIndices, targetIndices, (w, t) => bestWeights[w, t]);
            List<Match<TWorker, TOption>> result = new List<Match<TWorker, TOption>>(grouped.Count);
            foreach (Match<int, int> match in grouped)
            {
                result.Add(new Match<TWorker, TOption>(
                    workers[match.Worker],
                    bestOptions[match.Worker, match.Target],
                    match.Weight));
            }
            return result;
        }

        /// <summary>
        /// Repairs grouped matches whose selected options contend for the same exclusive
        /// interaction target.  The worker/logical-target pairing remains intact, but each
        /// worker is moved to its best unused option when one exists.  Sharing is retained as
        /// a fallback so a single large stack can still serve several jobs sequentially.
        /// </summary>
        public static List<Match<TWorker, TOption>> DiversifyExclusiveOptions<
            TWorker, TTarget, TOption, TKey>(
            IEnumerable<Match<TWorker, TOption>> matches,
            Func<TOption, TTarget> targetSelector,
            Func<TTarget, IEnumerable<TOption>> optionsForTarget,
            Func<TWorker, TOption, double> weightSelector,
            Func<TOption, bool> consumesExclusiveKey,
            Func<TOption, TKey> exclusiveKeySelector,
            Func<TWorker, TKey, bool> keyAvailable)
        {
            List<Match<TWorker, TOption>> result = new List<Match<TWorker, TOption>>();
            HashSet<TKey> usedKeys = new HashSet<TKey>();
            foreach (Match<TWorker, TOption> match in matches.OrderByDescending(item => item.Weight))
            {
                if (!consumesExclusiveKey(match.Target))
                {
                    result.Add(match);
                    continue;
                }

                TTarget target = targetSelector(match.Target);
                Match<TWorker, TOption>? alternative = optionsForTarget(target)?
                    .Where(consumesExclusiveKey)
                    .Select(option => new Match<TWorker, TOption>(
                        match.Worker,
                        option,
                        weightSelector(match.Worker, option)))
                    .Where(candidate => candidate.Weight > 0d && !double.IsNaN(candidate.Weight))
                    .Where(candidate =>
                    {
                        TKey key = exclusiveKeySelector(candidate.Target);
                        return !usedKeys.Contains(key) && keyAvailable(match.Worker, key);
                    })
                    .OrderByDescending(candidate => candidate.Weight)
                    .Cast<Match<TWorker, TOption>?>()
                    .FirstOrDefault();

                Match<TWorker, TOption> chosen = alternative ?? match;
                result.Add(chosen);
                usedKeys.Add(exclusiveKeySelector(chosen.Target));
            }
            return result;
        }
    }

    internal readonly struct Match<TWorker, TTarget>
    {
        public readonly TWorker Worker;
        public readonly TTarget Target;
        public readonly double Weight;

        public Match(TWorker worker, TTarget target, double weight)
        {
            Worker = worker;
            Target = target;
            Weight = weight;
        }
    }
}
