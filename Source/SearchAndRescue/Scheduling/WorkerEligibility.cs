using RimWorld;
using Verse;
using Verse.AI;

namespace SearchAndRescue
{
    internal static class WorkerEligibility
    {
        internal static bool WorkerOperational(Pawn worker, Map map)
        {
            return worker != null && !worker.Destroyed && worker.Spawned && worker.Map == map &&
                   !worker.Dead && !worker.Downed && !worker.InMentalState && worker.jobs != null &&
                   worker.health?.capacities?.CapableOf(PawnCapacityDefOf.Manipulation) == true &&
                   WorkerControlledByScheduler(worker) && MechWorkerCompatibility.CanRunSchedulerNow(worker);
        }

        internal static bool WorkerControlledByScheduler(Pawn worker)
        {
            // Right-click prioritized work survives between individual jobs (for example,
            // consecutive tending jobs). During the transition CurJob may not be playerForced; let
            // vanilla finish or clear the sustained order before matching this worker.
            if (worker == null || worker.mindState?.duty != null ||
                worker.mindState?.priorityWork?.IsPrioritized == true ||
                !HardworkingCompatibility.CanWorkNow(worker))
            {
                return false;
            }

            bool playerControlled = worker.IsColonistPlayerControlled || HardworkingCompatibility.IsWorker(worker) ||
                                    Compatibility.IsColonyWorkMech(worker) ||
                                    Compatibility.IsTrainedRescueAnimal(worker);
            if (!playerControlled)
            {
                return false;
            }

            // Persistent Field Rescue belongs to the ordinary work graph. Drafted search-and-
            // rescue and threat-zone coordination are intentionally outside this alpha; no
            // optional integration may opt a drafted pawn into this scheduler implicitly.
            return !worker.Drafted;
        }

        internal static bool IsProvidingBedsideCare(Pawn worker)
        {
            Job job = worker.CurJob;
            if (job == null || !Compatibility.IsTreatmentJob(job.def))
            {
                return false;
            }

            Pawn patient = CompatibilityRegistry.PatientFor(worker, job, PatientJobRole.Treatment);
            return patient != null && RescueDestinationPlanner.IsInSafePatientBed(patient);
        }

        internal static bool CanPerformStage(Pawn worker, SearchAndRescueStage stage)
        {
            switch (stage)
            {
                case SearchAndRescueStage.Treat:
                case SearchAndRescueStage.Restock:
                    return Compatibility.CanPerformAnyTreatmentWork(worker) ||
                           MechanicalCare.CanRepairWork(worker);
                case SearchAndRescueStage.FollowupTreat:
                    return Compatibility.CanPerformFollowupTreatmentWork(worker);
                case SearchAndRescueStage.Capture:
                    return Compatibility.CanPerformCaptureWork(worker);
                case SearchAndRescueStage.Rescue:
                    return Compatibility.CanPerformRescueWork(worker);
                case SearchAndRescueStage.Supply:
                    return Compatibility.CanPerformSupplyWork(worker);
                default:
                    return false;
            }
        }
    }
}
