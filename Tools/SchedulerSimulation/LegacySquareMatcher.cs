using System;
using System.Collections.Generic;
using System.Linq;

namespace SearchAndRescue.MatcherReference
{
    internal static class LegacySquareMatcher
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

    }
}
