using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace SearchAndRescue
{
    // Transient claims only. Detach before invoking JobTracker callbacks: they may re-enter
    // scheduling. Medical resource release and stage completion remain coordinator effects.
    internal sealed class ActiveJobClaims
    {
        private readonly Dictionary<Pawn, ActiveAssignment> primary = new Dictionary<Pawn, ActiveAssignment>();
        private readonly Dictionary<Pawn, ActiveAssignment> logistics = new Dictionary<Pawn, ActiveAssignment>();
        private readonly Dictionary<Pawn, ActiveStandby> standby = new Dictionary<Pawn, ActiveStandby>();
        internal IReadOnlyDictionary<Pawn, ActiveAssignment> Primary => primary;
        internal IReadOnlyDictionary<Pawn, ActiveAssignment> Logistics => logistics;
        internal IReadOnlyDictionary<Pawn, ActiveStandby> Standby => standby;

        internal static JobIdentity IdentityOf(Job job) => new JobIdentity(job, job?.def, job?.loadID ?? -1);
        internal static bool Matches(ActiveAssignment claim, Pawn worker, JobIdentity identity) =>
            claim != null && claim.Worker == worker && claim.Identity.Matches(identity);
        internal static bool Matches(ActiveStandby claim, Pawn worker, JobIdentity identity) =>
            claim != null && claim.Worker == worker && claim.Identity.Matches(identity);

        internal ActiveAssignment FindPrimary(Pawn worker, JobIdentity identity) =>
            primary.Values.FirstOrDefault(claim => Matches(claim, worker, identity));
        internal ActiveAssignment FindAssignment(Pawn worker, JobIdentity identity) =>
            FindPrimary(worker, identity) ?? logistics.Values.FirstOrDefault(claim => Matches(claim, worker, identity));
        internal ActiveStandby FindStandby(Pawn worker, JobIdentity identity) =>
            standby.Values.FirstOrDefault(claim => Matches(claim, worker, identity));

        internal bool HasPrimaryWorker(Pawn worker) => worker != null && primary.Values.Any(claim => claim.Worker == worker);
        internal bool HasStandbyWorker(Pawn worker) => worker != null && standby.Values.Any(claim => claim.Worker == worker);

        internal bool Owns(Pawn worker, JobIdentity identity, SearchAndRescueStage? stage)
        {
            if (worker == null) return false;
            ActiveAssignment claim = FindAssignment(worker, identity);
            return claim != null && (!stage.HasValue || AssignmentStageRules.Matches(claim.Stage, stage.Value)) ||
                (!stage.HasValue || stage.Value == SearchAndRescueStage.Rescue) && FindStandby(worker, identity) != null;
        }

        internal void Register(ActiveAssignment claim)
        {
            if (claim.Stage == SearchAndRescueStage.Supply) logistics[claim.Worker] = claim;
            else primary[claim.Target] = claim;
        }
        internal void Register(ActiveStandby claim) => standby[claim.Target] = claim;
        internal bool ReleasePrimary(Pawn target) => primary.Remove(target);
        internal bool ReleaseLogistics(Pawn worker) => logistics.Remove(worker);
        internal bool ReleaseStandby(Pawn target) => standby.Remove(target);
        internal void DetachPatient(Pawn patient, out ActiveAssignment assignment,
            out List<ActiveAssignment> deliveries, out ActiveStandby waiting)
        {
            primary.TryGetValue(patient, out assignment);
            standby.TryGetValue(patient, out waiting);
            deliveries = logistics.Values.Where(claim => claim.Target == patient).ToList();
            primary.Remove(patient);
            standby.Remove(patient);
            foreach (ActiveAssignment delivery in deliveries) logistics.Remove(delivery.Worker);
        }

        internal void Clear()
        {
            primary.Clear();
            logistics.Clear();
            standby.Clear();
        }
    }
}
