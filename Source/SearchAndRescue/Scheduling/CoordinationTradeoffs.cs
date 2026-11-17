using System.Collections.Generic;
using System.Linq;
using Verse;

namespace SearchAndRescue
{
    public sealed partial class SearchAndRescueCoordinator
    {
        // A shortlist and its full score often compare the same stair route in one tick.
        private Dictionary<(int worker, IntVec3 from, IntVec3 to), double> bandDistances;
        private int bandDistanceTick;

        internal double BandDistanceSquared(Pawn worker, IntVec3 from, IntVec3 to)
        {
            int now = Find.TickManager.TicksGame;
            bandDistances ??= new Dictionary<(int, IntVec3, IntVec3), double>();
            if (bandDistanceTick != now)
            {
                bandDistances.Clear();
                bandDistanceTick = now;
            }
            var key = (worker.thingIDNumber, from, to);
            if (!bandDistances.TryGetValue(key, out double distance))
                bandDistances[key] = distance = MultiLevelRescue.EstimateBandDistanceSquared(worker, from, to);
            return distance;
        }

        private List<Match<Pawn, Pawn>> MatchRescuePatients(IReadOnlyList<Pawn> workers,
            IReadOnlyList<Pawn> patients, System.Func<Pawn, Pawn, double> score,
            IReadOnlyDictionary<Pawn, PendingAssignment> previous)
        {
            return SearchAndRescueMod.Settings?.UseFastRescueAllocation == true
                ? FastCandidateMatcher.Match(workers, patients, p => ScoringPatientUrgency(p),
                    (w, p) => MultiLevelRescue.DistanceSquared(w, w.Position, p.Position), score, preferred:
                    (w, p) => previous.TryGetValue(w, out PendingAssignment old) && old.Target == p)
                : MatchPatients(workers, patients, score);
        }

        private List<Match<Pawn, TransportTask>> MatchTransportTasks(IReadOnlyList<Pawn> workers,
            IReadOnlyList<Pawn> patients, Dictionary<Pawn, List<TransportTask>> options,
            System.Func<Pawn, TransportTask, double> score, bool fast,
            IReadOnlyDictionary<Pawn, PendingAssignment> previous,
            IReadOnlyDictionary<Pawn, ActiveStandby> standbys)
        {
            if (!fast) return MatchPatientOptions(workers, patients, p => options[p], score);
            var selected = new Dictionary<WorkerTargetPair, TransportTask>();
            var selectedSupplies = new HashSet<Thing>();
            var matches = FastCandidateMatcher.Match(workers, patients, p => ScoringPatientUrgency(p),
                (w, p) => MultiLevelRescue.DistanceSquared(w, w.Position, p.Position), (w, p) =>
                {
                    double best = 0;
                    foreach (TransportTask task in options[p])
                    {
                        if (task.IsSupply && selectedSupplies.Contains(task.SupplyResource)) continue;
                        double value = score(w, task);
                        if (value > best) { best = value; selected[new WorkerTargetPair(w, p)] = task; }
                    }
                    return best;
                }, preferred: (w, p) => previous.TryGetValue(w, out PendingAssignment old) && old.Target == p ||
                    standbys.TryGetValue(w, out ActiveStandby standby) && standby.Target == p,
                onSelected: match =>
                {
                    TransportTask task = selected[new WorkerTargetPair(match.Worker, match.Target)];
                    if (task.IsSupply) selectedSupplies.Add(task.SupplyResource);
                });
            return matches.Select(m => new Match<Pawn, TransportTask>(m.Worker,
                selected[new WorkerTargetPair(m.Worker, m.Target)], m.Weight)).ToList();
        }
    }
}
