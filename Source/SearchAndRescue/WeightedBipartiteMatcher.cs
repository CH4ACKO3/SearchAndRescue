using System;
using System.Collections.Generic;
using System.Linq;

namespace SearchAndRescue
{
    internal static class WeightedBipartiteMatcher
    {
        public static List<Match<TWorker, TTarget>> MaximumWeight<TWorker, TTarget>(
            IReadOnlyList<TWorker> workers,
            IReadOnlyList<TTarget> targets,
            Func<TWorker, TTarget, double> weightSelector)
        {
            int size = Math.Max(workers.Count, targets.Count);
            List<Match<TWorker, TTarget>> result = new List<Match<TWorker, TTarget>>();
            if (size == 0)
            {
                return result;
            }

            double[,] weights = new double[size + 1, size + 1];
            for (int workerIndex = 1; workerIndex <= workers.Count; workerIndex++)
            {
                for (int targetIndex = 1; targetIndex <= targets.Count; targetIndex++)
                {
                    double weight = weightSelector(workers[workerIndex - 1], targets[targetIndex - 1]);
                    weights[workerIndex, targetIndex] = double.IsNaN(weight) || weight <= 0d ? 0d : weight;
                }
            }

            // Hungarian algorithm over a square matrix. Dummy rows/columns have zero weight,
            // allowing unreachable pairs to remain unmatched.
            double[] rowPotential = new double[size + 1];
            double[] columnPotential = new double[size + 1];
            int[] columnWorker = new int[size + 1];
            int[] previousColumn = new int[size + 1];

            for (int worker = 1; worker <= size; worker++)
            {
                columnWorker[0] = worker;
                int column0 = 0;
                double[] minimum = new double[size + 1];
                bool[] used = new bool[size + 1];
                for (int column = 1; column <= size; column++)
                {
                    minimum[column] = double.PositiveInfinity;
                }

                do
                {
                    used[column0] = true;
                    int worker0 = columnWorker[column0];
                    double delta = double.PositiveInfinity;
                    int column1 = 0;

                    for (int column = 1; column <= size; column++)
                    {
                        if (used[column])
                        {
                            continue;
                        }

                        double reducedCost = -weights[worker0, column] - rowPotential[worker0] - columnPotential[column];
                        if (reducedCost < minimum[column])
                        {
                            minimum[column] = reducedCost;
                            previousColumn[column] = column0;
                        }

                        if (minimum[column] < delta)
                        {
                            delta = minimum[column];
                            column1 = column;
                        }
                    }

                    for (int column = 0; column <= size; column++)
                    {
                        if (used[column])
                        {
                            rowPotential[columnWorker[column]] += delta;
                            columnPotential[column] -= delta;
                        }
                        else if (column > 0)
                        {
                            minimum[column] -= delta;
                        }
                    }

                    column0 = column1;
                }
                while (columnWorker[column0] != 0);

                do
                {
                    int column1 = previousColumn[column0];
                    columnWorker[column0] = columnWorker[column1];
                    column0 = column1;
                }
                while (column0 != 0);
            }

            for (int column = 1; column <= targets.Count; column++)
            {
                int worker = columnWorker[column];
                if (worker > 0 && worker <= workers.Count && weights[worker, column] > 0d)
                {
                    result.Add(new Match<TWorker, TTarget>(workers[worker - 1], targets[column - 1], weights[worker, column]));
                }
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
                        double weight = weightSelector(workers[workerIndex], option);
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

            List<Match<int, int>> grouped = MaximumWeight(
                workerIndices,
                targetIndices,
                (workerIndex, targetIndex) => bestWeights[workerIndex, targetIndex]);
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
