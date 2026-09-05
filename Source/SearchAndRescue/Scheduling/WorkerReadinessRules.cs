namespace SearchAndRescue
{
    internal enum WorkerReadiness
    {
        Ready,
        NotOperational,
        NotFieldResponder,
        PlayerOrder,
        ActiveAssignment,
        ActiveLogistics,
        ActiveStandby,
        BedsideCare
    }

    internal static class WorkerReadinessRules
    {
        // Assignment occupancy is separate from work-provider permission. Allowing standby
        // during a transport rebuild must not bypass player orders or another active job.
        internal static WorkerReadiness Evaluate(
            bool operational, bool fieldResponder, bool playerForced,
            bool activeAssignment, bool activeLogistics, bool activeStandby,
            bool allowStandby, bool bedsideCare)
        {
            if (!operational) return WorkerReadiness.NotOperational;
            if (!fieldResponder) return WorkerReadiness.NotFieldResponder;
            if (playerForced) return WorkerReadiness.PlayerOrder;
            if (activeAssignment) return WorkerReadiness.ActiveAssignment;
            if (activeLogistics) return WorkerReadiness.ActiveLogistics;
            if (activeStandby && !allowStandby) return WorkerReadiness.ActiveStandby;
            if (bedsideCare) return WorkerReadiness.BedsideCare;
            return WorkerReadiness.Ready;
        }
    }
}
