using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SearchAndRescue
{
    public sealed class SearchAndRescueCoordinator : MapComponent
    {
        private const int JobStartGraceTicks = 30;
        private const int MaintenanceInterval = 60;
        private const int TravelPreemptionInterval = 60;
        private const int FullScheduleInterval = 120;
        private const int LargeBattleScheduleInterval = 180;
        private const int LargeBattleTargetThreshold = 30;
        private const int DirtyDebounceTicks = 15;
        private const int RetryDelay = 90;
        private const int MaximumRetryDelay = 300;
        private const int TreatmentReevaluationDelay = 30;
        // A carrier may arrive this much before the expected end of field treatment. This
        // avoids making them idle beside a long procedure while still putting them in place
        // for an immediate hand-off.
        private static int StandbyLeadTicks => SearchAndRescueMod.Settings?.StandbyLeadTicks ?? 180;
        private const int SoftClaimLeaseTicks = 300;
        private const int ActiveResourceLeaseTicks = 5000;
        private static int MaxMissionKitPatients =>
            SearchAndRescueMod.Settings?.MissionKitPatientCount ?? 3;
        private static int MaxMissionKitConsumables =>
            SearchAndRescueMod.Settings?.MissionKitConsumableCount ?? 6;
        private static int SupplyNearbyRadiusSquared =>
            SearchAndRescueMod.Settings?.FieldSupplyRadiusSquared ?? 64;
        private const int PendingContinuityDecayTicks = 300;
        private const int PostTreatmentContinuityTicks = TreatmentContinuityRules.DurationTicks;
        private const int PostCaptureTreatmentContinuityTicks = 1200;
        // Four in-game hours. Completed manual orders remain dormant for this window and
        // are restored if the same pawn recovers, then becomes downed again.
        private static int RecentMarkerMemoryTicks =>
            SearchAndRescueMod.Settings?.RecentMarkerMemoryTicks ?? 10000;
        private const int TravelInitialCommitmentDecayTicks = 180;
        private const int TravelRecentSwitchDecayTicks = 600;
        private static int SafeBloodLossHorizonTicks =>
            SearchAndRescueMod.Settings?.BloodLossWarningTicks ?? 45000;
        private static float MajorUntendedBleedRate =>
            SearchAndRescueMod.Settings?.MajorBleedThreshold ?? 0.08f;
        private static float SignificantTotalBleedRate =>
            SearchAndRescueMod.Settings?.TotalBleedThreshold ?? 0.12f;
        private const double AssignmentBaseWeight = 1000000d;
        private const double PendingContinuityBaseWeight = 20000d;
        private const double PendingFreshPairWeight = 40000d;
        private const double TravelContinuityBaseWeight = 25000d;
        private const double TravelInitialCommitmentWeight = 90000d;
        private const double TravelNearPatientWeight = 80000d;
        private const double TravelRecentSwitchWeight = 110000d;
        private static double TreatmentBeforeTransportWeight =>
            SearchAndRescueMod.Settings?.TreatmentBeforeTransportWeight ?? 180000d;
        private const double RescueInterceptionWeight = 70000d;
        private const double ResumeTransportWeight = 90000d;
        private static double UrgentSurgeryTransportWeight =>
            SearchAndRescueMod.Settings?.SurgeryTransportWeight ?? 90000d;
        private static double InJobTreatmentSwitchMargin =>
            SearchAndRescueMod.Settings?.TreatmentSwitchMargin ?? 60000d;
        private const double ManualTreatmentAffinityWeight = 30000d;
        private const double CaptureBeforeTreatmentWeight = 650000d;
        private const double CaptureTreatmentBundleWeight = 180000d;
        private const double PostCaptureTreatmentContinuityWeight = 500000d;
        private static double EmergencyTreatmentRouteCost =>
            SearchAndRescueMod.Settings?.EmergencyMedicineRouteCost ?? 325d;
        private static double FollowupTreatmentRouteCost =>
            SearchAndRescueMod.Settings?.FollowupMedicineRouteCost ?? 900d;
        private static double SupplyRouteCost =>
            SearchAndRescueMod.Settings?.SupplyRouteCost ?? 1000d;
        private const double DoctorEmergencySupplyOpportunityCost = 60000d;
        private const float TravelNearPatientRadius = 12f;
        private static readonly SearchAndRescueStage[] UnifiedMatchingStages =
        {
            SearchAndRescueStage.Capture,
            SearchAndRescueStage.Treat,
            SearchAndRescueStage.FollowupTreat,
            SearchAndRescueStage.Rescue
        };

        private readonly Dictionary<Pawn, PendingAssignment> pendingByWorker = new Dictionary<Pawn, PendingAssignment>();
        private readonly ActiveJobClaims activeClaims = new ActiveJobClaims();
        private IReadOnlyDictionary<Pawn, ActiveAssignment> activeByTarget => activeClaims.Primary;
        private IReadOnlyDictionary<Pawn, ActiveStandby> standbyByTarget => activeClaims.Standby;
        private readonly Dictionary<StageRetryKey, StageRetryState> retryByStage =
            new Dictionary<StageRetryKey, StageRetryState>();
        private readonly Dictionary<Pawn, int> lastTravelSwitchAt = new Dictionary<Pawn, int>();
        private readonly Dictionary<Pawn, Pawn> preferredRescuerByTarget = new Dictionary<Pawn, Pawn>();
        private readonly Dictionary<Pawn, MedicalCarePlan> carePlans = new Dictionary<Pawn, MedicalCarePlan>();
        private readonly Dictionary<Pawn, CareAdmission> careAdmissions =
            new Dictionary<Pawn, CareAdmission>();
        private List<RecentMarkerMemory> recentMarkerMemories = new List<RecentMarkerMemory>();
        private IReadOnlyDictionary<Pawn, ActiveAssignment> activeLogisticsByWorker => activeClaims.Logistics;
        private readonly Dictionary<Pawn, SoftCareClaim> careAffinityClaims =
            new Dictionary<Pawn, SoftCareClaim>();
        // Legacy per-map roster. Humanlike entries are migrated once to SAR_FieldRescue;
        // animals and work mechs remain here because they do not use the ordinary work tab.
        private List<Pawn> fieldResponders = new List<Pawn>();
        private bool fieldResponderRosterInitialized;
        private bool fieldResponderWorkTypeMigrated;
        private readonly HashSet<Pawn> deferredWakeWorkers = new HashSet<Pawn>();
        // Delivery never interrupts a partially filled tend bar. This one-shot event is
        // consumed by NotifyTreatmentCommitted at the next safe wound boundary.
        private readonly HashSet<Pawn> deliveredSupplyReevaluation = new HashSet<Pawn>();
        private readonly Dictionary<Pawn, string> lastSchedulerDecision =
            new Dictionary<Pawn, string>();
        private double treatmentDetourBacklogPressure;
        private readonly Dictionary<StageRetryKey, bool> schedulingWorkerReadiness =
            new Dictionary<StageRetryKey, bool>();
        private readonly Dictionary<StageRetryKey, bool> schedulingTargetReadiness =
            new Dictionary<StageRetryKey, bool>();
        private readonly Dictionary<WorkerTargetPair, RescueDestinationPlan> schedulingRescueDestinations =
            new Dictionary<WorkerTargetPair, RescueDestinationPlan>();
        private readonly Dictionary<Pawn, bool> schedulingExternalOwnership =
            new Dictionary<Pawn, bool>();
        private readonly List<Pawn> pendingWorkerScratch = new List<Pawn>();
        private readonly List<KeyValuePair<Pawn, ActiveAssignment>> activeAssignmentScratch =
            new List<KeyValuePair<Pawn, ActiveAssignment>>();
        private readonly List<KeyValuePair<Pawn, ActiveAssignment>> activeLogisticsScratch =
            new List<KeyValuePair<Pawn, ActiveAssignment>>();
        private readonly List<KeyValuePair<Pawn, ActiveStandby>> activeStandbyScratch =
            new List<KeyValuePair<Pawn, ActiveStandby>>();
        private readonly List<ActiveAssignment> activeTreatmentScratch = new List<ActiveAssignment>();
        private readonly MedicalResourceLedger medicalResources;

        private int lastScheduleTick = -1;
        private int scheduleNotBeforeTick;
        private int lastKnownCareTargetCount;
        private bool maintenanceDirty = true;
        private bool scheduleDirty = true;
        private bool postLoadRecoveryPending;
        private bool schedulingSnapshotActive;
        private int internalDesignationRemovalDepth;

        public SearchAndRescueCoordinator(Map map) : base(map)
        {
            medicalResources = new MedicalResourceLedger(map);
        }

        internal static void NotifyGlobalSettingsChanged()
        {
            if (Current.ProgramState != ProgramState.Playing || Find.Maps == null)
            {
                return;
            }

            foreach (Map activeMap in Find.Maps)
            {
                activeMap?.GetComponent<SearchAndRescueCoordinator>()?.NotifyCareScopeChanged();
            }
        }

        private void NotifyCareScopeChanged()
        {
            careAdmissions.Clear();
            foreach (KeyValuePair<Pawn, ActiveAssignment> pair in activeByTarget.ToList())
            {
                if (ActiveAssignmentAuthorized(pair.Key, pair.Value))
                {
                    continue;
                }

                activeClaims.ReleasePrimary(pair.Key);
                medicalResources.ReleaseWorker(pair.Value.Worker);
                InterruptAssignmentWorker(pair.Value);
            }
            foreach (KeyValuePair<Pawn, ActiveAssignment> pair in activeLogisticsByWorker.ToList())
            {
                if (ActiveAssignmentAuthorized(pair.Value.Target, pair.Value))
                {
                    continue;
                }

                activeClaims.ReleaseLogistics(pair.Key);
                medicalResources.ReleaseWorker(pair.Key);
                InterruptAssignmentWorker(pair.Value);
            }
            foreach (Pawn patient in carePlans.Keys.ToList())
            {
                bool activeCare = activeByTarget.ContainsKey(patient) ||
                                  activeLogisticsByWorker.Values.Any(active => active.Target == patient);
                if (!activeCare)
                {
                    medicalResources.ReleasePatient(patient);
                }
            }
            carePlans.Clear();
            foreach (Pawn worker in pendingByWorker.Keys.ToList())
            {
                medicalResources.ReleaseWorker(worker);
            }
            pendingByWorker.Clear();
            careAffinityClaims.Clear();
            deferredWakeWorkers.Clear();
            lastKnownCareTargetCount = 0;
            lastScheduleTick = -1;
            RequestScheduleRebuild(maintenance: true, delayTicks: 1);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            medicalResources.ExposeData();
            Scribe_Values.Look(
                ref fieldResponderRosterInitialized,
                "fieldResponderRosterInitialized",
                false);
            Scribe_Values.Look(
                ref fieldResponderWorkTypeMigrated,
                "fieldResponderWorkTypeMigrated",
                false);
            Scribe_Collections.Look(
                ref fieldResponders,
                "fieldResponders",
                LookMode.Reference);
            Scribe_Collections.Look(
                ref recentMarkerMemories,
                "recentMarkerMemories",
                LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                fieldResponders ??= new List<Pawn>();
                fieldResponders = fieldResponders
                    .Where(pawn => pawn != null && CanToggleFieldResponder(pawn))
                    .Distinct()
                    .ToList();
                recentMarkerMemories = recentMarkerMemories?
                    .Where(memory => memory?.Target != null)
                    .GroupBy(memory => memory.Target)
                    .Select(group => group.OrderByDescending(memory => memory.ExpiresAt).First())
                    .ToList() ?? new List<RecentMarkerMemory>();
                // Transient graph edges contain live Job references and are deliberately not
                // serialized. Reconcile any persisted SAR jobs on the first playable tick,
                // then rebuild the graph from the durable designations.
                postLoadRecoveryPending = true;
                lastScheduleTick = -1;
                scheduleDirty = true;
                maintenanceDirty = true;
                scheduleNotBeforeTick = 0;
            }
        }

        internal bool IsFieldResponder(Pawn pawn)
        {
            if (pawn == null || pawn.Drafted)
            {
                return false;
            }

            EnsureFieldResponderWorkTypeMigrated();
            if (pawn.workSettings != null && (pawn.RaceProps?.Humanlike == true || HardworkingCompatibility.IsWorker(pawn)))
            {
                return Compatibility.FieldRescueWorkPriority(pawn) > 0;
            }

            return fieldResponders.Contains(pawn);
        }

        internal void NotifyWorkerUndrafting(Pawn worker)
        {
            if (worker == null || worker.Destroyed || worker.MapHeld != map)
            {
                return;
            }

            EnsureFieldResponderWorkTypeMigrated();
            bool hasFieldRescueWork = worker.workSettings != null && worker.RaceProps?.Humanlike == true
                ? Compatibility.FieldRescueWorkPriority(worker) > 0
                : fieldResponders.Contains(worker);
            if (!hasFieldRescueWork)
            {
                return;
            }

            // Pawn_DraftController immediately asks the pawn for a new job while changing from
            // drafted to undrafted. Dirty the graph before that scan, and permit a request-scoped
            // rebuild even when several pawns are undrafted in the same game tick.
            int now = Find.TickManager?.TicksGame ?? 0;
            if (lastScheduleTick >= now)
            {
                lastScheduleTick = now - 1;
            }
            RequestScheduleRebuild(maintenance: true, delayTicks: 0);
        }

        internal bool CanToggleFieldResponder(Pawn pawn)
        {
            return pawn != null && !pawn.Destroyed && pawn.MapHeld == map &&
                   pawn.Faction == Faction.OfPlayer &&
                   (pawn.RaceProps?.Humanlike == true && !pawn.IsPrisoner ||
                    Compatibility.IsColonyWorkMech(pawn) || HardworkingCompatibility.IsWorker(pawn) ||
                    Compatibility.IsTrainedRescueAnimal(pawn));
        }

        internal void SetFieldResponder(Pawn pawn, bool enabled)
        {
            EnsureFieldResponderWorkTypeMigrated();
            if (!CanToggleFieldResponder(pawn))
            {
                return;
            }

            if (pawn.workSettings != null && (pawn.RaceProps?.Humanlike == true || HardworkingCompatibility.IsWorker(pawn)))
            {
                Compatibility.SetWorkPriorityForMigration(
                    pawn,
                    SearchAndRescueDefOf.SAR_FieldRescue,
                    enabled ? 3 : 0);
                if (!enabled)
                {
                    ReleaseWorkerAssignments(pawn);
                }
                RequestScheduleRebuild(maintenance: true, delayTicks: 1);
                return;
            }

            if (enabled)
            {
                if (!fieldResponders.Contains(pawn))
                {
                    fieldResponders.Add(pawn);
                }
            }
            else
            {
                fieldResponders.Remove(pawn);
                ReleaseWorkerAssignments(pawn);
            }

            RequestScheduleRebuild(maintenance: true, delayTicks: 1);
        }

        private void EnsureFieldResponderRosterInitialized()
        {
            fieldResponders ??= new List<Pawn>();
            if (fieldResponderRosterInitialized)
            {
                return;
            }

            // Existing saves and new colonies begin with their current workforce enabled,
            // preserving the mod's previous behavior. Pawns joining later are opt-in.
            fieldResponders.AddRange(map.mapPawns.AllPawnsSpawned
                .Where(CanToggleFieldResponder)
                .Where(pawn => !fieldResponders.Contains(pawn)));
            fieldResponderRosterInitialized = true;
        }

        private void EnsureFieldResponderWorkTypeMigrated()
        {
            if (fieldResponderWorkTypeMigrated || SearchAndRescueDefOf.SAR_FieldRescue == null)
            {
                return;
            }

            EnsureFieldResponderRosterInitialized();
            foreach (Pawn pawn in fieldResponders.ToList())
            {
                if (pawn?.workSettings == null || pawn.RaceProps?.Humanlike != true ||
                    pawn.WorkTypeIsDisabled(SearchAndRescueDefOf.SAR_FieldRescue))
                {
                    continue;
                }

                if (Compatibility.FieldRescueWorkPriority(pawn) <= 0)
                {
                    Compatibility.SetWorkPriorityForMigration(
                        pawn,
                        SearchAndRescueDefOf.SAR_FieldRescue,
                        LegacyResponderPriority(pawn));
                }
            }

            // Keep only pawns that cannot express the role through the ordinary work tab.
            fieldResponders = fieldResponders
                .Where(pawn => pawn != null &&
                               (pawn.workSettings == null || pawn.RaceProps?.Humanlike != true))
                .Distinct()
                .ToList();
            fieldResponderWorkTypeMigrated = true;
        }

        private static int LegacyResponderPriority(Pawn pawn)
        {
            int[] priorities =
            {
                pawn.workSettings.GetPriority(WorkTypeDefOf.Doctor),
                pawn.workSettings.GetPriority(WorkTypeDefOf.Hauling),
                pawn.workSettings.GetPriority(WorkTypeDefOf.Warden)
            };
            int configured = priorities.Where(priority => priority > 0).DefaultIfEmpty(3).Min();
            return Math.Max(1, Math.Min(4, configured));
        }

        private void ReleaseWorkerAssignments(Pawn pawn)
        {
            pendingByWorker.Remove(pawn);
            deferredWakeWorkers.Remove(pawn);
            medicalResources.ReleaseWorker(pawn);
            foreach (KeyValuePair<Pawn, ActiveAssignment> pair in activeByTarget
                         .Where(pair => pair.Value.Worker == pawn).ToList())
            {
                activeClaims.ReleasePrimary(pair.Key);
                InterruptAssignmentWorker(pair.Value);
            }
            if (activeLogisticsByWorker.TryGetValue(pawn, out ActiveAssignment logistics))
            {
                activeClaims.ReleaseLogistics(pawn);
                InterruptAssignmentWorker(logistics);
            }
            foreach (Pawn patient in standbyByTarget
                         .Where(pair => pair.Value.Worker == pawn)
                         .Select(pair => pair.Key).ToList())
            {
                StopStandby(patient);
            }
        }

        private bool ScheduleRebuildDue(int now)
        {
            if (lastScheduleTick < 0)
            {
                return true;
            }
            if (scheduleDirty && now >= scheduleNotBeforeTick)
            {
                return true;
            }
            if (lastKnownCareTargetCount <= 0 && CoordinationMode == MedicalCoordinationMode.MarkedOnly)
            {
                return false;
            }

            int interval = lastKnownCareTargetCount >= LargeBattleTargetThreshold
                ? LargeBattleScheduleInterval
                : FullScheduleInterval;
            return now - lastScheduleTick >= interval;
        }

        private bool IsRetryBlocked(Pawn target, SearchAndRescueStage stage, int now)
        {
            return target != null &&
                   retryByStage.TryGetValue(new StageRetryKey(target, stage), out StageRetryState retry) &&
                   now < retry.RetryAfter;
        }

        private void SetStageRetry(
            Pawn target,
            SearchAndRescueStage stage,
            int now,
            bool progressive = true,
            int fixedDelay = RetryDelay)
        {
            if (target == null)
            {
                return;
            }

            StageRetryKey key = new StageRetryKey(target, stage);
            retryByStage.TryGetValue(key, out StageRetryState previous);
            int failureCount = progressive ? Math.Min((previous?.FailureCount ?? 0) + 1, 8) : 0;
            int delay = fixedDelay;
            if (progressive)
            {
                int exponent = Math.Min(Math.Max(0, failureCount - 1), 4);
                delay = Math.Min(MaximumRetryDelay, RetryDelay << exponent);
            }

            int retryTick = now + Math.Max(1, delay);
            retryByStage[key] = new StageRetryState(retryTick, failureCount);
            RequestScheduleRebuild(delayTicks: retryTick - now);
        }

        private void ClearStageRetry(Pawn target, SearchAndRescueStage stage)
        {
            if (target != null)
            {
                retryByStage.Remove(new StageRetryKey(target, stage));
            }
        }

        private void ClearTargetRetries(Pawn target)
        {
            if (target == null)
            {
                return;
            }

            foreach (StageRetryKey key in retryByStage.Keys.Where(key => key.Target == target).ToList())
            {
                retryByStage.Remove(key);
            }
        }

        private void ClearDesignationRetries(Pawn target, SearchAndRescueStage stage)
        {
            if (stage == SearchAndRescueStage.Treat || stage == SearchAndRescueStage.FollowupTreat ||
                stage == SearchAndRescueStage.Restock || stage == SearchAndRescueStage.Supply)
            {
                ClearStageRetry(target, SearchAndRescueStage.Treat);
                ClearStageRetry(target, SearchAndRescueStage.FollowupTreat);
                ClearStageRetry(target, SearchAndRescueStage.Restock);
                ClearStageRetry(target, SearchAndRescueStage.Supply);
                return;
            }

            ClearStageRetry(target, stage);
        }

        private static bool FailureWarrantsBackoff(JobCondition condition)
        {
            return condition == JobCondition.Incompletable ||
                   condition == JobCondition.QueuedNoLongerValid ||
                   condition == JobCondition.Errored ||
                   condition == JobCondition.ErroredPather;
        }

        private static bool JobEndWasInterrupted(JobCondition condition)
        {
            return condition == JobCondition.InterruptForced ||
                   condition == JobCondition.InterruptOptional;
        }

        private bool WorkerOperational(Pawn worker)
        {
            return WorkerEligibility.WorkerOperational(worker, map);
        }

        private void RecoverTransientStateAfterLoad()
        {
            postLoadRecoveryPending = false;
            pendingByWorker.Clear();
            activeClaims.Clear();
            retryByStage.Clear();
            careAffinityClaims.Clear();
            deferredWakeWorkers.Clear();
            deliveredSupplyReevaluation.Clear();
            preferredRescuerByTarget.Clear();
            carePlans.Clear();
            careAdmissions.Clear();
            lastTravelSwitchAt.Clear();
            medicalResources.ClearTransientClaims();

            foreach (Pawn worker in map.mapPawns.AllPawnsSpawned.ToList())
            {
                Job job = worker?.CurJob;
                if (!IsPersistedManagedJob(worker, job))
                {
                    continue;
                }

                Pawn carriedPatient = worker.carryTracker?.CarriedThing as Pawn;
                bool carryingManagedPatient = carriedPatient != null &&
                                              (job.targetA.Pawn == carriedPatient ||
                                               job.targetB.Pawn == carriedPatient);

                // Ending rather than partially reconstructing a path/toil is deterministic:
                // reservations are released and the durable marks remain for the next graph
                // pass. Some modded JobDefs retain carried things after interruption, so make
                // the patient-drop invariant explicit rather than relying on JobDef metadata.
                worker.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                if (carryingManagedPatient && worker.carryTracker?.CarriedThing == carriedPatient)
                {
                    worker.carryTracker.TryDropCarriedThing(worker.Position, ThingPlaceMode.Near, out _);
                }
            }

            lastScheduleTick = -1;
            RequestScheduleRebuild(maintenance: true, delayTicks: 1);
        }

        private bool IsPersistedManagedJob(Pawn worker, Job job)
        {
            if (job == null)
            {
                return false;
            }

            if (job.def == SearchAndRescueDefOf.SAR_EvacuateToPoint ||
                job.def == SearchAndRescueDefOf.SAR_CaptureInPlace ||
                job.def == SearchAndRescueDefOf.SAR_WaitForFieldTreatment ||
                job.def == SearchAndRescueDefOf.SAR_RestockMedicalKit ||
                job.def == SearchAndRescueDefOf.SAR_DeliverMedicalSupply)
            {
                return true;
            }

            if (job.workGiverDef?.defName?.StartsWith("SAR_", StringComparison.Ordinal) == true)
            {
                return true;
            }

            // Non-scan WorkGivers do not populate Job.workGiverDef. Only infer ownership from
            // a durable marker that overlaps the registered role. In particular, a rescue-only
            // patient must not turn an unrelated persisted Tend job into a managed SAR job.
            Pawn patient = CompatibilityRegistry.PatientFor(worker, job);
            if (job.playerForced || patient == null || patient.MapHeld != map)
            {
                return false;
            }

            PatientJobRole roles = CompatibilityRegistry.RolesFor(job.def);
            return (roles & PatientJobRole.Treatment) != 0 &&
                       HasDesignation(patient, SearchAndRescueDefOf.SAR_Treat) ||
                   (roles & PatientJobRole.Transport) != 0 &&
                       (HasDesignation(patient, SearchAndRescueDefOf.SAR_Rescue) ||
                        HasDesignation(patient, SearchAndRescueDefOf.SAR_Capture)) ||
                   (roles & PatientJobRole.Capture) != 0 &&
                       HasDesignation(patient, SearchAndRescueDefOf.SAR_Capture);
        }

        private void RequestScheduleRebuild(bool maintenance = false, int delayTicks = DirtyDebounceTicks)
        {
            SearchAndRescuePerformanceDiagnostics.RecordDirtyRequest(maintenance, delayTicks);
            int now = Find.TickManager.TicksGame;
            int requestedTick = now + Math.Max(0, delayTicks);
            if (!scheduleDirty)
            {
                scheduleNotBeforeTick = requestedTick;
            }
            else
            {
                scheduleNotBeforeTick = Math.Min(scheduleNotBeforeTick, requestedTick);
            }
            scheduleDirty = true;
            maintenanceDirty |= maintenance;
        }

        private void CleanupInvalidPendingWorkers()
        {
            if (pendingByWorker.Count == 0)
            {
                return;
            }

            bool removedAny = false;
            pendingWorkerScratch.Clear();
            foreach (Pawn worker in pendingByWorker.Keys)
            {
                if (!WorkerOperational(worker) || worker.CurJob?.playerForced == true)
                {
                    pendingWorkerScratch.Add(worker);
                }
            }
            foreach (Pawn worker in pendingWorkerScratch)
            {
                pendingByWorker.Remove(worker);
                deferredWakeWorkers.Remove(worker);
                medicalResources.ReleaseWorker(worker);
                removedAny = true;
            }
            pendingWorkerScratch.Clear();

            deferredWakeWorkers.RemoveWhere(worker =>
                !WorkerOperational(worker) || worker.CurJob?.playerForced == true);
            if (removedAny)
            {
                RequestScheduleRebuild(maintenance: true, delayTicks: 0);
            }
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            EngineBenchmarkDiagnostics.Tick(map);
            bool profile = SearchAndRescuePerformanceDiagnostics.Enabled;
            long mapTickStart = profile
                ? SearchAndRescuePerformanceDiagnostics.Begin(SarPerformancePhase.MapTick)
                : 0L;
            try
            {
                EnsureFieldResponderWorkTypeMigrated();
                if (postLoadRecoveryPending)
                {
                    RecoverTransientStateAfterLoad();
                }

                int now = Find.TickManager.TicksGame;
                UpdateRecentMarkerMemories(now);

                long phaseStart = profile
                    ? SearchAndRescuePerformanceDiagnostics.Begin(SarPerformancePhase.PendingAndWake)
                    : 0L;
                CleanupInvalidPendingWorkers();
                WakeDeferredPendingWorkers();
                if (profile)
                {
                    SearchAndRescuePerformanceDiagnostics.End(SarPerformancePhase.PendingAndWake, phaseStart);
                }

                phaseStart = profile
                    ? SearchAndRescuePerformanceDiagnostics.Begin(SarPerformancePhase.TreatmentMonitoring)
                    : 0L;
                MonitorActiveTreatmentRounds();
                MonitorTreatmentTravelPreemption();
                if (profile)
                {
                    SearchAndRescuePerformanceDiagnostics.End(SarPerformancePhase.TreatmentMonitoring, phaseStart);
                }

                phaseStart = profile
                    ? SearchAndRescuePerformanceDiagnostics.Begin(SarPerformancePhase.ActiveAssignmentMaintenance)
                    : 0L;
                // Active assignments are a very small set. Polling only this set each tick lets
                // a naturally completed tend round hand off on the following tick regardless
                // of third-party EndCurrentJob Harmony ordering.
                UpdateActiveAssignments(now);
                UpdateActiveLogistics(now);
                // Standby validity depends on the exact live doctor/job pair. Check the very small
                // active set every tick so an interrupted doctor cannot leave a carrier waiting on
                // a stale forecast until the maintenance interval.
                UpdateActiveStandbys(now);
                if (profile)
                {
                    SearchAndRescuePerformanceDiagnostics.End(
                        SarPerformancePhase.ActiveAssignmentMaintenance,
                        phaseStart);
                }

                if (maintenanceDirty || map.IsHashIntervalTick(MaintenanceInterval))
                {
                    phaseStart = profile
                        ? SearchAndRescuePerformanceDiagnostics.Begin(SarPerformancePhase.PeriodicCleanup)
                        : 0L;
                    maintenanceDirty = false;
                    medicalResources.Cleanup(now);
                    CleanupSoftCareClaims(now);
                    CleanupOrphanedManagedCarries();
                    CleanupDesignations();
                    CleanupRetiredWorkerState();
                    if (profile)
                    {
                        SearchAndRescuePerformanceDiagnostics.End(
                            SarPerformancePhase.PeriodicCleanup,
                            phaseStart);
                    }
                }

                if (ScheduleRebuildDue(now))
                {
                    RebuildPendingAssignments(now, null);
                }
            }
            finally
            {
                if (profile)
                {
                    SearchAndRescuePerformanceDiagnostics.End(SarPerformancePhase.MapTick, mapTickStart);
                }
            }
        }

        internal Job TryIssueJob(Pawn worker, SearchAndRescueStage stage, RescueWorkProvider rescueProvider)
        {
            if (worker == null || worker.Map != map)
            {
                return null;
            }

            int now = Find.TickManager.TicksGame;
            if (!pendingByWorker.TryGetValue(worker, out PendingAssignment pending) ||
                !PendingMatchesRequest(worker, pending, stage) ||
                !PendingAssignmentValid(worker, pending, now))
            {
                if (lastScheduleTick != now && ScheduleRebuildDue(now))
                {
                    RebuildPendingAssignments(now, worker);
                }

                if (!pendingByWorker.TryGetValue(worker, out pending) ||
                    !PendingMatchesRequest(worker, pending, stage) ||
                    !PendingAssignmentValid(worker, pending, now))
                {
                    if (!lastSchedulerDecision.TryGetValue(worker, out string priorDecision) ||
                        priorDecision.StartsWith("candidate in rebuild", StringComparison.Ordinal) ||
                        priorDecision.StartsWith("no winning graph edge", StringComparison.Ordinal) ||
                        priorDecision.StartsWith("pending ", StringComparison.Ordinal))
                    {
                        lastSchedulerDecision[worker] =
                            $"issue {stage}: rejected " +
                            (pending == null
                                ? "no pending assignment"
                                : $"{pending.Stage}:{pending.Target?.ThingID} " +
                                  $"valid={PendingAssignmentValid(worker, pending, now)} " +
                                  $"reason={DebugPendingInvalidReason(worker, pending, now)}") +
                            $" at {now}";
                    }
                    bool staleTreatmentAdmission = pending != null &&
                        IsTreatmentStage(pending.Stage) &&
                        !TreatmentAdmitted(pending.Target, pending.Stage);
                    if (pending != null &&
                        (PendingMatchesRequest(worker, pending, stage) || staleTreatmentAdmission))
                    {
                        pendingByWorker.Remove(worker);
                        medicalResources.ReleaseWorker(worker);
                        RequestScheduleRebuild(maintenance: true);
                    }
                    return null;
                }
            }

            if (pending.Stage == SearchAndRescueStage.Rescue &&
                Compatibility.RescueProviderFor(worker) != rescueProvider ||
                pending.Stage == SearchAndRescueStage.Supply &&
                (rescueProvider != RescueWorkProvider.Hauling || !Compatibility.CanPerformSupplyWork(worker)))
            {
                return null;
            }

            Job job;
            IntVec3 destination = IntVec3.Invalid;
            if (pending.WaitForTreatment)
            {
                job = JobMaker.MakeJob(SearchAndRescueDefOf.SAR_WaitForFieldTreatment, pending.Target);
            }
            else
            {
                job = MakeJob(worker, pending, out destination);
            }
            if (job == null)
            {
                lastSchedulerDecision[worker] =
                    $"issue {stage}: MakeJob returned null for {pending.Stage}:{pending.Target?.ThingID} at {now}";
                pendingByWorker.Remove(worker);
                medicalResources.ReleaseWorker(worker);
                SetStageRetry(pending.Target, pending.Stage, now);
                return null;
            }

            NormalizePawnCarryCount(job);

            Pawn releasedRescuer = null;
            if (pending.Stage == SearchAndRescueStage.Treat &&
                activeByTarget.TryGetValue(pending.Target, out ActiveAssignment activeRescue) &&
                activeRescue.Stage == SearchAndRescueStage.Rescue &&
                !TryInterruptRescueForTreatment(pending.Target, activeRescue, out releasedRescuer))
            {
                // The doctor had a valid graph edge, but the carrier could not safely hand
                // the patient over (most importantly, TryDrop failed). Keep the original
                // Rescue running and discard only this soft treatment claim.
                pendingByWorker.Remove(worker);
                medicalResources.ReleaseWorker(worker);
                RequestScheduleRebuild(maintenance: true);
                return null;
            }

            // Treatment construction may deliberately set playerForced for an explicitly
            // marked neutral patient that vanilla automatic care would reject (notably a
            // wild, non-hostile animal). Preserve that narrowly scoped flag; every other
            // managed job constructor leaves it false.
            pendingByWorker.Remove(worker);
            medicalResources.ReleaseWorker(worker);
            ClaimPendingResources(worker, pending, now + ActiveResourceLeaseTicks);
            foreach (Pawn otherWorker in pendingByWorker
                         .Where(pair => pair.Value.Target == pending.Target &&
                                        pair.Value.WaitForTreatment == pending.WaitForTreatment &&
                                        pair.Value.Stage == pending.Stage)
                         .Select(pair => pair.Key)
                         .ToList())
            {
                pendingByWorker.Remove(otherWorker);
                medicalResources.ReleaseWorker(otherWorker);
            }

            if (pending.WaitForTreatment)
            {
                if (!TryGetActiveTreatmentEta(
                        pending.Target,
                        now,
                        out Pawn doctor,
                        out Job treatmentJob,
                        out int treatmentTicks))
                {
                    RequestScheduleRebuild(maintenance: true, delayTicks: 0);
                    return null;
                }

                activeClaims.Register(new ActiveStandby(
                    worker,
                    pending.Target,
                    job,
                    doctor,
                    treatmentJob,
                    now + treatmentTicks));
                preferredRescuerByTarget[pending.Target] = worker;
                return job;
            }

            TryGetCareAdmission(pending.Target, out CareAdmission activeAdmission);
            ActiveAssignment active = new ActiveAssignment(
                worker,
                pending.Target,
                job,
                pending.Stage,
                destination,
                now,
                CountUntendedHediffs(pending.Target),
                pending.Target.health.hediffSet.BleedRateTotal,
                Compatibility.FieldEmergencySeverity(pending.Target),
                activeAdmission.Origin,
                GetBloodLossSeverity(pending.Target),
                GetHediffSeverity(pending.Target, "Hemodilution"));
            activeClaims.Register(active);
            if (pending.Stage == SearchAndRescueStage.Treat &&
                careAffinityClaims.TryGetValue(pending.Target, out SoftCareClaim affinity) &&
                affinity.Worker == worker && affinity.ConsumeOnTreatmentStart)
            {
                careAffinityClaims.Remove(pending.Target);
            }
            lastSchedulerDecision[worker] =
                $"issued {job.def?.defName}:{pending.Stage}:{pending.Target?.ThingID} at {now}";
            if (releasedRescuer != null)
            {
                RequestScheduleRebuild(maintenance: true, delayTicks: 0);
            }
            return job;
        }

        internal Job TryIssuePriorityTreatmentOverride(Pawn worker, out Pawn target)
        {
            target = null;
            if (worker == null || worker.Map != map || worker.jobs == null)
            {
                return null;
            }

            int now = Find.TickManager.TicksGame;
            if (!pendingByWorker.TryGetValue(worker, out PendingAssignment pending) ||
                !PendingMatchesPriorityTreatment(pending) ||
                !PendingAssignmentValid(worker, pending, now))
            {
                // Priority Treatment is allowed to wake a doctor who is resting or taking
                // recreation. Include that doctor in a fresh graph, but still let the graph
                // decide which casualty (if any) belongs to them.
                RebuildPendingAssignments(now, worker);
                if (!pendingByWorker.TryGetValue(worker, out pending) ||
                    !PendingMatchesPriorityTreatment(pending) ||
                    !PendingAssignmentValid(worker, pending, now))
                {
                    if (pending != null && PendingMatchesPriorityTreatment(pending))
                    {
                        pendingByWorker.Remove(worker);
                        medicalResources.ReleaseWorker(worker);
                        RequestScheduleRebuild(maintenance: true);
                    }
                    return null;
                }
            }

            target = pending.Target;
            Job job = TryIssueJob(worker, SearchAndRescueStage.Treat, RescueWorkProvider.None);
            if (job == null)
            {
                target = null;
            }
            return job;
        }

        internal Job TryIssueAutomaticRoutineTreatment(Pawn worker)
        {
            if (worker == null || worker.Map != map || CoordinationMode != MedicalCoordinationMode.AllTending)
            {
                return null;
            }

            int now = Find.TickManager.TicksGame;
            if (!pendingByWorker.TryGetValue(worker, out PendingAssignment pending) ||
                pending.Stage != SearchAndRescueStage.FollowupTreat ||
                !PendingAssignmentValid(worker, pending, now))
            {
                if (lastScheduleTick != now && ScheduleRebuildDue(now))
                {
                    RebuildPendingAssignments(now, worker);
                }

                if (!pendingByWorker.TryGetValue(worker, out pending) ||
                    pending.Stage != SearchAndRescueStage.FollowupTreat ||
                    !PendingAssignmentValid(worker, pending, now))
                {
                    if (pending != null && pending.Stage == SearchAndRescueStage.FollowupTreat &&
                        !TreatmentAdmitted(pending.Target, SearchAndRescueStage.FollowupTreat))
                    {
                        pendingByWorker.Remove(worker);
                        medicalResources.ReleaseWorker(worker);
                        RequestScheduleRebuild(maintenance: true, delayTicks: 0);
                    }
                    return null;
                }
            }

            if (!UsesAutomaticRoutineLane(pending.Target))
            {
                return null;
            }

            return TryIssueJob(worker, SearchAndRescueStage.FollowupTreat, RescueWorkProvider.None);
        }

        private static void NormalizePawnCarryCount(Job job)
        {
            if (job != null && job.count < 1 &&
                (job.def == JobDefOf.Rescue || job.def == JobDefOf.Capture ||
                 job.def == SearchAndRescueDefOf.SAR_EvacuateToPoint))
            {
                // JobMaker leaves count at -1. Vanilla work givers normally fill this in,
                // but our scheduler constructs the carrying jobs directly.
                job.count = 1;
            }
        }

        private bool PendingMatchesRequest(
            Pawn worker,
            PendingAssignment pending,
            SearchAndRescueStage requested)
        {
            return pending.Stage == requested ||
                   requested == SearchAndRescueStage.Treat && pending.Stage == SearchAndRescueStage.Restock ||
                   requested == SearchAndRescueStage.Treat && pending.Stage == SearchAndRescueStage.Capture &&
                   CaptureIsTreatmentPrerequisite(worker, pending.Target) ||
                   requested == SearchAndRescueStage.Rescue && pending.Stage == SearchAndRescueStage.Supply;
        }

        private static bool PendingMatchesPriorityTreatment(PendingAssignment pending)
        {
            return pending != null &&
                   (pending.Stage == SearchAndRescueStage.Treat ||
                    pending.Stage == SearchAndRescueStage.Restock);
        }

        internal bool IsActiveJob(Pawn worker, Job job, SearchAndRescueStage? stage = null)
        {
            return activeClaims.Owns(worker, ActiveJobClaims.IdentityOf(job), stage);
        }

        private static bool StageMatchesRequest(SearchAndRescueStage actual, SearchAndRescueStage requested)
        {
            return AssignmentStageRules.Matches(actual, requested);
        }

        private static bool IsTreatmentStage(SearchAndRescueStage stage)
        {
            return AssignmentStageRules.IsTreatment(stage);
        }

        internal void NotifyPlayerOrderedPatientJob(Pawn orderedWorker, Job job)
        {
            Pawn target = CompatibilityRegistry.PatientFor(orderedWorker, job);
            if (orderedWorker == null || target == null || target.MapHeld != map ||
                !HasCareInterestOrOwnership(target) || !IsPatientHandlingJob(job.def))
            {
                return;
            }

            activeByTarget.TryGetValue(target, out ActiveAssignment assignment);
            if (assignment != null && Compatibility.IsMoreInjuriesTreatmentJob(job.def) &&
                assignment.Worker == orderedWorker && AssignmentJobStillRunning(assignment) &&
                assignment.JobDef.defName == "ProvideFirstAid")
            {
                // More Injuries' aggregate first-aid driver dispatches its child jobs through
                // TryTakeOrderedJob. That is an internal hand-off, not a player override.
                return;
            }

            // Detach every active lane before EndCurrentJob can re-enter a WorkGiver.
            // The mark and persistent field supplies still belong to the patient.
            activeClaims.DetachPatient(target, out assignment,
                out List<ActiveAssignment> detachedDeliveries, out ActiveStandby detachedStandby);
            RequestScheduleRebuild(maintenance: true);

            foreach (Pawn worker in pendingByWorker
                         .Where(pair => pair.Value.Target == target)
                         .Select(pair => pair.Key)
                         .ToList())
            {
                pendingByWorker.Remove(worker);
                medicalResources.ReleaseWorker(worker);
            }

            ClearTargetRetries(target);
            // A manual patient order supersedes our transient assignments, but the field
            // pile must remain protected until the mark/need itself becomes invalid.
            medicalResources.ReleasePatientClaims(target);
            careAffinityClaims.Remove(target);
            foreach (Pawn claimedPatient in careAffinityClaims
                         .Where(pair => pair.Value.Worker == orderedWorker)
                         .Select(pair => pair.Key).ToList())
            {
                careAffinityClaims.Remove(claimedPatient);
            }
            foreach (ActiveAssignment logistics in detachedDeliveries)
            {
                if (AssignmentJobStillRunning(logistics))
                {
                    // The incoming ordered job has not reserved its target yet.
                    logistics.Worker.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                }
            }
            if (StandbyJobStillRunning(detachedStandby))
            {
                detachedStandby.Worker.jobs.EndCurrentJob(JobCondition.Succeeded, startNewJob: false);
            }
            if (assignment == null)
            {
                return;
            }

            if (assignment.Stage == SearchAndRescueStage.Rescue && assignment.Worker != null)
            {
                preferredRescuerByTarget[target] = assignment.Worker;
            }
            // TryTakeOrderedJob reserves before it starts. Release our reservation in its
            // prefix so the explicit player order can acquire the target, and make the
            // patient-drop invariant explicit for a transport assignment.
            InterruptAssignmentWorker(assignment, startNewJob: false);
        }

        private static bool IsPatientHandlingJob(JobDef jobDef)
        {
            return CompatibilityRegistry.HasRole(jobDef, PatientJobRole.Any);
        }

        internal JobEndSnapshot CaptureJobEnd(Pawn worker)
        {
            Job job = worker?.CurJob;
            return new JobEndSnapshot(
                ActiveJobClaims.IdentityOf(job),
                IsActiveJob(worker, job),
                CompatibilityRegistry.PatientFor(worker, job),
                job != null && !job.playerForced && IsRoutineBoundaryJob(job));
        }

        internal void NotifyExternalPatientJobEnded(JobEndSnapshot ended)
        {
            if (ended.WasManaged) return;
            Pawn patient = ended.Patient;
            if (patient?.MapHeld == map && HasAnyCareInterest(patient))
            {
                ClearTargetRetries(patient);
                RequestScheduleRebuild(maintenance: true, delayTicks: 0);
            }
        }

        internal void NotifyPlayerPatientQueueReleased(Pawn patient)
        {
            if (patient?.MapHeld != map || !HasCareInterestOrOwnership(patient))
            {
                return;
            }

            // A player-forced queued patient job is an ownership lease. When the queue is
            // cleared without ever starting that job there is no EndCurrentJob callback, so
            // wake the graph explicitly instead of leaving the casualty unassigned until the
            // next periodic full pass.
            ClearTargetRetries(patient);
            RequestScheduleRebuild(maintenance: true, delayTicks: 0);
        }

        internal void NotifyStageDesignationAdded(Pawn target, SearchAndRescueStage addedStage)
        {
            if (target == null)
            {
                return;
            }

            ForgetRecentMarker(target, addedStage);

            careAdmissions.Remove(target);

            foreach (Pawn worker in pendingByWorker
                         .Where(pair => pair.Value.Target == target)
                         .Select(pair => pair.Key)
                         .ToList())
            {
                pendingByWorker.Remove(worker);
                medicalResources.ReleaseWorker(worker);
            }

            ClearTargetRetries(target);
            StopStandby(target);
            foreach (Pawn rescuer in map.mapPawns.AllPawnsSpawned
                         .Where(pawn => SearchAndRescueJobContext.IsPatientTakeToBedJob(pawn.CurJob) &&
                                        CompatibilityRegistry.PatientFor(
                                            pawn,
                                            pawn.CurJob,
                                            PatientJobRole.Transport) == target &&
                                        !pawn.CurJob.playerForced)
                         .ToList())
            {
                if (activeByTarget.TryGetValue(target, out ActiveAssignment assignment) &&
                    assignment.Worker == rescuer && AssignmentJobStillRunning(assignment))
                {
                    preferredRescuerByTarget[target] = rescuer;

                    // Keep a tracked SAR transport intact until a matched doctor's actual
                    // job request has succeeded. Rebuild may prepare the intercept now, but
                    // TryIssueJob performs the destructive hand-off only after it has a job.
                    if (addedStage == SearchAndRescueStage.Treat &&
                        assignment.Stage == SearchAndRescueStage.Rescue)
                    {
                        continue;
                    }

                    activeClaims.ReleasePrimary(target);
                    InterruptAssignmentWorker(assignment);
                }
            }
            // Existing external treatment/transport is deliberately not interrupted. Its
            // current job owns the patient until the next natural boundary, at which point
            // NotifyExternalPatientJobEnded wakes the graph immediately.
            RequestScheduleRebuild(maintenance: true);
        }

        internal void NotifyStageDesignationRemoved(Pawn target, SearchAndRescueStage removedStage)
        {
            if (target?.MapHeld == map)
            {
                // Designations removed outside the coordinator are explicit player/mod
                // cancellations. Do not let an earlier dormant order resurrect them.
                if (internalDesignationRemovalDepth == 0)
                {
                    ForgetRecentMarker(target, removedStage);
                }
                careAdmissions.Remove(target);
                RequestScheduleRebuild(maintenance: true);
            }
        }

        internal bool HasRecentMarkerMemories => recentMarkerMemories?.Count > 0;

        internal void ClearRecentMarkerMemories()
        {
            recentMarkerMemories?.Clear();
        }

        /// <summary>
        /// Clears durable orders and every transient owner derived from them before a debug
        /// benchmark preset is applied. Going through RemoveAllStages is important here: simply
        /// deleting designations would leave active jobs, supply claims and standby ownership
        /// from the previous fixture alive until their normal maintenance boundary.
        /// </summary>
        internal void ClearBenchmarkFixture()
        {
            foreach (Pawn target in AllMarkedPawns())
            {
                RemoveAllStages(target);
            }

            RequestScheduleRebuild(maintenance: true);
        }

        internal void NotifyManagedJobEnding(Pawn worker, Job job, JobCondition condition)
        {
            if (!IsActiveJob(worker, job))
            {
                return;
            }

            ActiveAssignment assignment = activeClaims.FindAssignment(worker, ActiveJobClaims.IdentityOf(job));
            Pawn target = assignment?.Target;
            if (target == null)
            {
                target = activeClaims.FindStandby(worker, ActiveJobClaims.IdentityOf(job))?.Target;
            }

            if (assignment != null)
            {
                assignment.EndCondition = condition;
            }

            if (condition == JobCondition.Succeeded && target != null)
            {
                if (assignment != null && activeByTarget.TryGetValue(target, out ActiveAssignment active) &&
                    active == assignment)
                {
                    if (IsTreatmentStage(assignment.Stage) &&
                        (assignment.JobDef != JobDefOf.TendPatient && assignment.JobDef?.defName != "UseTourniquet" ||
                         assignment.CommittedTreatmentRounds > 0))
                    {
                        // Vanilla TendPatient can also succeed empty when its target becomes
                        // untendable between matching and driver startup. Only TendUtility.DoTend's
                        // committed-round callback proves that vanilla actually treated a wound;
                        // aggregate third-party treatment jobs still use their successful result.
                        assignment.RoundEffectSeen = true;
                    }
                    // This callback is a prefix on EndCurrentJob, so the worker still appears
                    // to be running the old job. Leave the assignment intact; the next
                    // MapComponentTick observes the completed job and retires it safely.
                    return;
                }
                NotifyTargetStageAdvanced(target);
                return;
            }

            // Path/resource failures are retired by the stage-specific backoff path; ordinary
            // interruptions rebuild immediately so another valid worker can take over.
            RequestScheduleRebuild(maintenance: true);
        }

        internal void NotifyManagedJobEnded(Pawn worker, JobEndSnapshot ended, JobCondition condition)
        {
            if (condition != JobCondition.Succeeded || worker == null || !ended.WasManaged)
            {
                return;
            }

            ActiveAssignment assignment = activeClaims.FindPrimary(worker, ended.Identity);
            if (assignment == null || assignment.Target == null ||
                !activeByTarget.TryGetValue(assignment.Target, out ActiveAssignment current) ||
                current != assignment)
            {
                return;
            }

            // EndCurrentJob's postfix runs after vanilla has installed its short transition
            // wait. Settle against the actual ending job here, before cleanup or another
            // WorkGiver can mistake a finished round for an abandoned assignment.
            activeClaims.ReleasePrimary(assignment.Target);
            medicalResources.ReleaseWorker(worker);
            if (IsTreatmentStage(assignment.Stage) &&
                (assignment.JobDef != JobDefOf.TendPatient && assignment.JobDef?.defName != "UseTourniquet" ||
                 assignment.CommittedTreatmentRounds > 0))
            {
                assignment.RoundEffectSeen = true;
            }

            int now = Find.TickManager.TicksGame;
            CompleteActiveAssignment(assignment.Target, assignment, now);

            // This is a Harmony postfix but still runs inside EndCurrentJob's call stack.
            // Re-entering CheckForJobOverride here lets an immediately completed job recurse
            // through EndCurrentJob again, producing ten or more Tend/Wander jobs in one tick.
            // Settle the event now, then rebuild from the next MapComponentTick after the job
            // tracker has fully unwound. Empty vanilla rounds also keep their RetryDelay from
            // FinishTreatmentRound.
            RequestScheduleRebuild(maintenance: true, delayTicks: 1);
        }

        internal void NotifyRoutineWorkBoundary(Pawn worker, JobEndSnapshot ended, JobCondition condition)
        {
            if (condition != JobCondition.Succeeded || worker == null ||
                !ended.WasAutomaticRoutineWork || !WorkerOperational(worker) ||
                !IsFieldResponder(worker) ||
                activeClaims.HasPrimaryWorker(worker) ||
                activeLogisticsByWorker.ContainsKey(worker))
            {
                return;
            }

            // The graph is authoritative. Avoid repeating its target scan here merely to
            // decide whether to schedule the scan; the last snapshot is a sufficient hint,
            // while new designations and automatic-care admissions dirty the graph directly.
            if (lastKnownCareTargetCount <= 0)
            {
                return;
            }

            RequestScheduleRebuild(maintenance: true, delayTicks: 1);
        }

        internal void NotifyTreatmentCommitted(Pawn doctor, Pawn patient)
        {
            EngineBenchmarkDiagnostics.Observe(doctor, patient);
            Job job = doctor?.CurJob;
            if (doctor == null || patient == null || job?.def != JobDefOf.TendPatient)
            {
                return;
            }

            TreatmentContinuityDiagnostics.Observe(doctor, patient);
            if (!activeByTarget.TryGetValue(patient, out ActiveAssignment assignment) ||
                !ActiveJobClaims.Matches(assignment, doctor, ActiveJobClaims.IdentityOf(job)) ||
                !IsTreatmentStage(assignment.Stage))
            {
                YieldExternalTendAtSafeBoundary(doctor, patient, job);
                return;
            }

            assignment.RoundEffectSeen = true;
            assignment.CommittedTreatmentRounds++;
            int now = Find.TickManager.TicksGame;

            if (assignment.Stage == SearchAndRescueStage.FollowupTreat)
            {
                // This lane intentionally yields after every committed wound. A fresh work
                // scan gives vanilla tending and surgery (which sort above the follow-up
                // WorkGiver) a chance before another stable battlefield wound is selected.
                job.endAfterTendedOnce = true;
                return;
            }

            // Toils_Tend checks this flag immediately after TendUtility.DoTend returns.
            // Keeping the current driver alive preserves its carried medicine and avoids
            // Smart Medicine's intentional inventory drop/pickup shim at every wound boundary.
            // A materially more urgent unclaimed patient still forces a normal job boundary,
            // after which the full joint graph makes the authoritative assignment.
            bool restartWithDeliveredSupply = ShouldRestartDryTreatmentWithDeliveredSupply(
                doctor,
                patient,
                assignment,
                now);
            job.endAfterTendedOnce = restartWithDeliveredSupply ||
                                     !ShouldContinueCurrentTendJob(doctor, patient, now);
        }

        private void YieldExternalTendAtSafeBoundary(Pawn doctor, Pawn currentPatient, Job job)
        {
            if (job.playerForced || job.endAfterTendedOnce || !WorkerOperational(doctor) ||
                !doctor.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) ||
                !Compatibility.CanPerformTreatmentWork(doctor) ||
                activeClaims.HasPrimaryWorker(doctor) ||
                activeLogisticsByWorker.ContainsKey(doctor))
            {
                return;
            }

            int now = Find.TickManager.TicksGame;
            if (Compatibility.HasMoreInjuriesTransfusionNeed(currentPatient))
            {
                // Vanilla TendPatient can loop through every wound without returning to the
                // work graph. Once one wound is committed, yield so More Injuries' higher
                // priority blood/saline giver can treat the same patient's volume deficit.
                // Player-forced tending was excluded above and therefore remains authoritative.
                job.endAfterTendedOnce = true;
                lastSchedulerDecision[doctor] =
                    $"yielded external TendPatient after wound for urgent transfusion:" +
                    $"{currentPatient.ThingID} at {now}";
                return;
            }

            if (TryGetCareAdmission(currentPatient, out CareAdmission currentAdmission) &&
                currentAdmission.HasAutomaticTreatment)
            {
                // An ordinary native TendPatient may have started before the automatic
                // patient index admitted this pawn. Yield only after the current wound is
                // committed, then let the same global graph compare every doctor/patient edge.
                RememberCompletedTreatment(doctor, currentPatient, now);
                job.endAfterTendedOnce = true;
                lastSchedulerDecision[doctor] =
                    $"yielded external TendPatient after wound for automatic coordination:" +
                    $"{currentPatient.ThingID} at {now}";
                RequestScheduleRebuild(maintenance: true, delayTicks: 1);
                return;
            }

            Pawn replacement = null;
            if (pendingByWorker.TryGetValue(doctor, out PendingAssignment pending) &&
                (pending.Stage == SearchAndRescueStage.Treat ||
                 pending.Stage == SearchAndRescueStage.Restock) &&
                PendingAssignmentValid(doctor, pending, now))
            {
                replacement = pending.Target;
            }

            if (replacement == null)
            {
                replacement = AllCareCandidates()
                    .Where(candidate => candidate != currentPatient &&
                                        TargetReadyForStage(candidate, SearchAndRescueStage.Treat, now) &&
                                        !pendingByWorker.Values.Any(other => other.Target == candidate &&
                                            other != pending &&
                                            (other.Stage == SearchAndRescueStage.Treat ||
                                             other.Stage == SearchAndRescueStage.Restock)) &&
                                        EdgeWeight(doctor, candidate, SearchAndRescueStage.Treat) > 0d)
                    .OrderByDescending(candidate => ImmediateTreatmentSwitchScore(doctor, candidate))
                    .FirstOrDefault();
            }

            if (replacement == null)
            {
                return;
            }

            double currentScore = ImmediateTreatmentSwitchScore(doctor, currentPatient);
            double replacementScore = ImmediateTreatmentSwitchScore(doctor, replacement);
            if (replacementScore <= currentScore + InJobTreatmentSwitchMargin)
            {
                return;
            }

            // TendUtility.DoTend has just committed one wound. Toils_Tend reads this flag
            // immediately afterwards, so this ends the vanilla continuous-treatment job at
            // a safe wound boundary without discarding a partial tend bar or carried medicine.
            job.endAfterTendedOnce = true;
            lastSchedulerDecision[doctor] =
                $"yielded external TendPatient after wound for Treat:{replacement.ThingID} " +
                $"gain={replacementScore - currentScore:0} at {now}";
            RequestScheduleRebuild(maintenance: true, delayTicks: 1);
        }

        private bool ShouldRestartDryTreatmentWithDeliveredSupply(
            Pawn doctor,
            Pawn patient,
            ActiveAssignment assignment,
            int now)
        {
            if (assignment?.Job?.targetB.Thing != null ||
                !deliveredSupplyReevaluation.Contains(patient))
            {
                return false;
            }

            if (!NeedsFieldStabilization(patient) || !Compatibility.NeedsAnyFieldTreatment(patient))
            {
                deliveredSupplyReevaluation.Remove(patient);
                return false;
            }

            MedicalCarePlan plan = MedicalCarePlan.Build(patient, now);
            carePlans[patient] = plan;
            List<MedicalTreatmentOption> options = Compatibility
                .FindTreatmentOptions(doctor, patient, plan, medicalResources)
                .ToList();
            MedicalTreatmentOption delivered = options
                .Where(option => option.Resource != null &&
                                 medicalResources.IsFieldSupplyFor(option.Resource, patient))
                .OrderByDescending(option => TreatmentEdgeWeight(doctor, patient, option))
                .FirstOrDefault();
            if (delivered == null)
            {
                // The delivery claim can overlap this callback for a fraction of a tick. Keep
                // the event armed so the next completed wound can retry after logistics cleanup.
                return false;
            }

            deliveredSupplyReevaluation.Remove(patient);
            double deliveredWeight = TreatmentEdgeWeight(doctor, patient, delivered);
            double dryWeight = options
                .Where(option => option.Resource == null)
                .Select(option => TreatmentEdgeWeight(doctor, patient, option))
                .DefaultIfEmpty(double.MinValue)
                .Max();
            return deliveredWeight > dryWeight;
        }

        private bool ShouldContinueCurrentTendJob(Pawn doctor, Pawn patient, int now)
        {
            ActiveAssignment assignment = activeByTarget.TryGetValue(patient, out ActiveAssignment currentAssignment)
                ? currentAssignment
                : null;
            if (assignment == null || assignment.CommittedTreatmentRounds >= assignment.TreatmentRoundBudget)
            {
                return false;
            }

            if (!NeedsFieldStabilization(patient) || !Compatibility.NeedsAnyFieldTreatment(patient))
            {
                return false;
            }

            double currentScore = ImmediateTreatmentSwitchScore(doctor, patient);
            foreach (Pawn alternative in AllCareCandidates())
            {
                if (alternative == patient || !TargetReadyForStage(alternative, SearchAndRescueStage.Treat, now) ||
                    activeByTarget.TryGetValue(alternative, out ActiveAssignment active) &&
                    active.Stage != SearchAndRescueStage.Rescue ||
                    pendingByWorker.Values.Any(pending => pending.Target == alternative &&
                        (pending.Stage == SearchAndRescueStage.Treat || pending.Stage == SearchAndRescueStage.Restock)))
                {
                    continue;
                }

                if (ImmediateTreatmentSwitchScore(doctor, alternative) >
                    currentScore + InJobTreatmentSwitchMargin)
                {
                    return false;
                }
            }

            return true;
        }

        private static double ImmediateTreatmentSwitchScore(Pawn doctor, Pawn patient)
        {
            double distance = doctor.Spawned && patient.Spawned
                ? Math.Sqrt(doctor.Position.DistanceToSquared(patient.Position))
                : 0d;
            return PatientUrgency(patient) * 120000d - distance * 250d;
        }

        internal void NotifyFieldSupplyDelivered(Pawn supplier, Thing supply, Pawn patient, int count)
        {
            medicalResources.RegisterFieldSupply(supply, patient, count);
            // The relocation claim has fulfilled its purpose once the stack is on the ground.
            // Releasing it here makes the delivery visible to a treatment boundary occurring
            // in the same tick; normal logistics cleanup remains idempotent.
            medicalResources.ReleaseWorker(supplier);
            if (activeByTarget.TryGetValue(patient, out ActiveAssignment active) &&
                IsTreatmentStage(active.Stage) &&
                active.JobDef == JobDefOf.TendPatient &&
                AssignmentJobStillRunning(active) &&
                active.Job.targetB.Thing == null)
            {
                deliveredSupplyReevaluation.Add(patient);
            }
            CancelAutomaticHaulsTargeting(supply);
            NotifyTargetStageAdvanced(patient);
        }

        private void NotifyTargetStageAdvanced(Pawn target)
        {
            if (target == null)
            {
                return;
            }

            // Stage completion changes every edge touching this target. Clear a stale
            // backoff immediately, then let the next coordinator pass retire old active and
            // pending claims before rebuilding the global graph once. We deliberately do not
            // hand the next stage straight to the previous claimant: a newly available
            // doctor/hauler may now be the better weighted match.
            ClearTargetRetries(target);
            RequestScheduleRebuild(maintenance: true, delayTicks: 0);
        }

        private void CancelAutomaticHaulsTargeting(Thing supply)
        {
            if (supply == null)
            {
                return;
            }

            foreach (Pawn worker in map.mapPawns.AllPawnsSpawned.ToList())
            {
                worker.jobs.jobQueue.RemoveAll(worker, queuedJob =>
                    !queuedJob.playerForced && JobTargetsSupply(queuedJob, supply) &&
                    IsAutomaticHaulJob(queuedJob, null));

                Job job = worker.CurJob;
                if (job == null || job.playerForced || !JobTargetsSupply(job, supply))
                {
                    continue;
                }

                if (IsAutomaticHaulJob(job, worker.jobs.curDriver))
                {
                    worker.jobs.EndCurrentJob(JobCondition.InterruptForced);
                }
            }
        }

        private static bool JobTargetsSupply(Job job, Thing supply)
        {
            return job != null && (job.targetA.Thing == supply ||
                                   job.targetQueueA?.Any(target => target.Thing == supply) == true);
        }

        private static bool IsAutomaticHaulJob(Job job, JobDriver driver)
        {
            string driverName = driver?.GetType().Name ?? job?.def?.driverClass?.Name ?? string.Empty;
            return job?.def == JobDefOf.HaulToCell || job?.def == JobDefOf.HaulToContainer ||
                   driverName.IndexOf("Haul", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal bool IsProtectedFieldSupply(Thing supply)
        {
            return medicalResources.IsProtectedFieldSupply(supply);
        }

        internal bool IsClaimedMedicalSupply(Thing supply)
        {
            return medicalResources.IsClaimedMedicalSupply(supply);
        }

        internal void NotifyRescuePointChanged()
        {
            foreach (StageRetryKey key in retryByStage.Keys
                         .Where(key => key.Stage == SearchAndRescueStage.Rescue).ToList())
            {
                retryByStage.Remove(key);
            }

            foreach (KeyValuePair<Pawn, ActiveAssignment> pair in activeByTarget
                         .Where(pair => pair.Value.Stage == SearchAndRescueStage.Rescue &&
                                        !pair.Value.DestinationIsBed)
                         .ToList())
            {
                Pawn patient = pair.Key;
                ActiveAssignment assignment = pair.Value;
                activeClaims.ReleasePrimary(patient);
                medicalResources.ReleaseWorker(assignment.Worker);
                if (assignment.Worker != null)
                {
                    preferredRescuerByTarget[patient] = assignment.Worker;
                }
                InterruptAssignmentWorker(assignment);
            }

            RequestScheduleRebuild(maintenance: true, delayTicks: 0);
        }

        private void UpdateActiveAssignments(int now)
        {
            if (activeByTarget.Count == 0)
            {
                return;
            }

            activeAssignmentScratch.Clear();
            activeAssignmentScratch.AddRange(activeByTarget);
            foreach (KeyValuePair<Pawn, ActiveAssignment> pair in activeAssignmentScratch)
            {
                Pawn target = pair.Key;
                ActiveAssignment assignment = pair.Value;

                if (target == null || target.Destroyed || target.Dead ||
                    target.Spawned && target.Map != map)
                {
                    activeClaims.ReleasePrimary(target);
                    medicalResources.ReleaseWorker(assignment.Worker);
                    InterruptAssignmentWorker(assignment);
                    continue;
                }

                if (!WorkerOperational(assignment.Worker))
                {
                    activeClaims.ReleasePrimary(target);
                    medicalResources.ReleaseWorker(assignment.Worker);
                    ClearStageRetry(target, assignment.Stage);
                    InterruptAssignmentWorker(assignment);
                    RequestScheduleRebuild(maintenance: true, delayTicks: 0);
                    continue;
                }

                if (!ActiveAssignmentAuthorized(target, assignment))
                {
                    activeClaims.ReleasePrimary(target);
                    medicalResources.ReleaseWorker(assignment.Worker);
                    ClearDesignationRetries(target, assignment.Stage);
                    InterruptAssignmentWorker(assignment);
                    continue;
                }

                if (!ActiveTargetControlValid(target, assignment.Stage, assignment))
                {
                    activeClaims.ReleasePrimary(target);
                    medicalResources.ReleaseWorker(assignment.Worker);
                    ClearStageRetry(target, assignment.Stage);
                    InterruptAssignmentWorker(assignment);
                    RequestScheduleRebuild(maintenance: true, delayTicks: 0);
                    continue;
                }

                if (!target.Spawned)
                {
                    if (IsCarriedByActiveRescuer(target, assignment))
                    {
                        continue;
                    }

                    activeClaims.ReleasePrimary(target);
                    medicalResources.ReleaseWorker(assignment.Worker);
                    InterruptAssignmentWorker(assignment);
                    continue;
                }

                if (AssignmentJobStillRunning(assignment))
                {
                    if (IsTreatmentStage(assignment.Stage) && !assignment.ActualStartObserved)
                    {
                        assignment.ActualStartObserved = true;
                        // A pending doctor claim has no reliable ETA. Rebuild once its treatment
                        // job is genuinely running so the transport graph can time standby.
                        RequestScheduleRebuild(delayTicks: 0);
                    }
                    continue;
                }

                if (now - assignment.StartedAt < JobStartGraceTicks)
                {
                    continue;
                }

                activeClaims.ReleasePrimary(target);
                medicalResources.ReleaseWorker(assignment.Worker);
                CompleteActiveAssignment(target, assignment, now);
            }
            activeAssignmentScratch.Clear();
        }

        private void CompleteActiveAssignment(Pawn target, ActiveAssignment assignment, int now)
        {
            switch (assignment.Stage)
            {
                case SearchAndRescueStage.Capture:
                    if (target.IsPrisonerOfColony)
                    {
                        RemoveDesignation(target, SearchAndRescueStage.Capture);
                        if (CaptureTreatmentCanContinue(assignment.Worker, target))
                        {
                            SetCareAffinity(target, new SoftCareClaim(
                                assignment.Worker,
                                now + PostCaptureTreatmentContinuityTicks,
                                PostCaptureTreatmentContinuityWeight,
                                consumeOnTreatmentStart: true), now);
                        }
                    }
                    else
                    {
                        if (JobEndWasInterrupted(assignment.EndCondition))
                        {
                            ClearStageRetry(target, assignment.Stage);
                        }
                        else
                        {
                            SetStageRetry(target, assignment.Stage, now,
                                progressive: FailureWarrantsBackoff(assignment.EndCondition) ||
                                             assignment.EndCondition == JobCondition.None);
                        }
                    }
                    break;

                case SearchAndRescueStage.Treat:
                case SearchAndRescueStage.FollowupTreat:
                    FinishTreatmentRound(target, assignment, now);
                    break;

                case SearchAndRescueStage.Restock:
                    // On success the kit is in the doctor's inventory; return to matching so
                    // it can choose the best current patient. A failed pickup backs off only
                    // Restock, allowing direct or medicine-less treatment to continue.
                    if (assignment.EndCondition == JobCondition.Succeeded ||
                        JobEndWasInterrupted(assignment.EndCondition))
                    {
                        ClearStageRetry(target, SearchAndRescueStage.Restock);
                    }
                    else
                    {
                        SetStageRetry(target, SearchAndRescueStage.Restock, now,
                            progressive: FailureWarrantsBackoff(assignment.EndCondition) ||
                                         assignment.EndCondition == JobCondition.None);
                    }
                    break;

                case SearchAndRescueStage.Rescue:
                    if (RescueCompleted(target, assignment.Destination, assignment.DestinationBed))
                    {
                        RemoveDesignation(target, SearchAndRescueStage.Rescue);
                        // Safe delivery completes transport, not medical stabilization. Keep
                        // the treatment watch while accumulated blood loss, shock, or another
                        // supported condition still needs care; FinishTreatmentRound retires it
                        // after the final required intervention.
                        if (!Compatibility.NeedsAnyFieldTreatment(target))
                        {
                            RemoveDesignation(target, SearchAndRescueStage.Treat);
                        }
                    }
                    else
                    {
                        if (JobEndWasInterrupted(assignment.EndCondition))
                        {
                            ClearStageRetry(target, SearchAndRescueStage.Rescue);
                        }
                        else
                        {
                            SetStageRetry(target, SearchAndRescueStage.Rescue, now,
                                progressive: FailureWarrantsBackoff(assignment.EndCondition) ||
                                             assignment.EndCondition == JobCondition.None);
                        }
                    }
                    break;
            }
        }

        private static void InterruptAssignmentWorker(
            ActiveAssignment assignment,
            bool startNewJob = true)
        {
            Pawn worker = assignment?.Worker;
            if (AssignmentJobStillRunning(assignment) && worker.Spawned && worker.jobs != null)
            {
                worker.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob);
            }

            if (worker?.Spawned != true || worker.carryTracker?.CarriedThing != assignment?.Target)
            {
                return;
            }

            Job current = worker.CurJob;
            bool newerTransportOwnsPatient = current != null && !AssignmentJobStillRunning(assignment) &&
                CompatibilityRegistry.PatientFor(worker, current, PatientJobRole.Transport) == assignment.Target;
            if (!newerTransportOwnsPatient)
            {
                bool dropped = worker.carryTracker.TryDropCarriedThing(
                    worker.Position,
                    ThingPlaceMode.Near,
                    out _);
                if (!dropped && worker.carryTracker.CarriedThing == assignment.Target)
                {
                    dropped = worker.carryTracker.TryDropCarriedThing(
                        worker.Position,
                        ThingPlaceMode.Direct,
                        out _);
                }
                if (!dropped && worker.carryTracker.CarriedThing == assignment.Target)
                {
                    Log.WarningOnce(
                        $"[Search and Rescue] Could not safely drop {assignment.Target} from {worker} " +
                        "after its managed transport ended. The next lifecycle pass will retry.",
                        196320760 ^ worker.thingIDNumber ^ assignment.Target.thingIDNumber);
                }
            }
        }

        private void UpdateActiveLogistics(int now)
        {
            if (activeLogisticsByWorker.Count == 0)
            {
                return;
            }

            activeLogisticsScratch.Clear();
            activeLogisticsScratch.AddRange(activeLogisticsByWorker);
            foreach (KeyValuePair<Pawn, ActiveAssignment> pair in activeLogisticsScratch)
            {
                Pawn worker = pair.Key;
                ActiveAssignment assignment = pair.Value;
                Pawn patient = assignment.Target;
                bool invalidWorker = !WorkerOperational(worker);
                bool invalidPatient = patient == null || patient.Dead || patient.Destroyed ||
                                      !patient.Spawned || patient.Map != map ||
                                      !ActiveTargetControlValid(patient, assignment.Stage, assignment);
                bool jobEnded = !AssignmentJobStillRunning(assignment) &&
                                now - assignment.StartedAt >= JobStartGraceTicks;
                if (invalidWorker || invalidPatient || jobEnded)
                {
                    activeClaims.ReleaseLogistics(worker);
                    medicalResources.ReleaseWorker(worker);
                    if ((invalidWorker || invalidPatient) && AssignmentJobStillRunning(assignment) &&
                        worker.Spawned && worker.jobs != null)
                    {
                        worker.jobs.EndCurrentJob(JobCondition.InterruptForced);
                    }
                    if (patient != null)
                    {
                        if (invalidWorker || invalidPatient || assignment.EndCondition == JobCondition.Succeeded ||
                            JobEndWasInterrupted(assignment.EndCondition))
                        {
                            ClearStageRetry(patient, assignment.Stage);
                        }
                        else
                        {
                            SetStageRetry(patient, assignment.Stage, now,
                                progressive: FailureWarrantsBackoff(assignment.EndCondition) ||
                                             assignment.EndCondition == JobCondition.None);
                        }
                    }
                    RequestScheduleRebuild(maintenance: true, delayTicks: 0);
                }
            }
            activeLogisticsScratch.Clear();
        }

        private void UpdateActiveStandbys(int now)
        {
            if (standbyByTarget.Count == 0)
            {
                return;
            }

            activeStandbyScratch.Clear();
            activeStandbyScratch.AddRange(standbyByTarget);
            foreach (KeyValuePair<Pawn, ActiveStandby> pair in activeStandbyScratch)
            {
                Pawn target = pair.Key;
                ActiveStandby standby = pair.Value;
                if (StandbyJobStillRunning(standby) &&
                    ActiveStandbyStillValid(standby, now))
                {
                    continue;
                }

                activeClaims.ReleaseStandby(target);
                if (StandbyJobStillRunning(standby))
                {
                    standby.Worker.jobs.EndCurrentJob(JobCondition.Succeeded);
                }
                RequestScheduleRebuild(maintenance: true, delayTicks: 0);
            }
            activeStandbyScratch.Clear();
        }

        internal bool ShouldContinueStandby(Pawn worker, Pawn patient, Job standbyJob)
        {
            int now = Find.TickManager.TicksGame;
            return patient != null && standbyByTarget.TryGetValue(patient, out ActiveStandby standby) &&
                   ActiveJobClaims.Matches(standby, worker, ActiveJobClaims.IdentityOf(standbyJob)) &&
                   ActiveStandbyStillValid(standby, now);
        }

        private void StopStandby(Pawn target, bool startNewJob = true)
        {
            if (!standbyByTarget.TryGetValue(target, out ActiveStandby standby))
            {
                return;
            }

            activeClaims.ReleaseStandby(target);
            if (StandbyJobStillRunning(standby))
            {
                standby.Worker.jobs.EndCurrentJob(JobCondition.Succeeded, startNewJob);
            }
            RequestScheduleRebuild(maintenance: true, delayTicks: 0);
        }

        private void FinishTreatmentRound(Pawn patient, ActiveAssignment assignment, int now)
        {
            if (!TreatmentProgressMade(patient, assignment))
            {
                if (JobEndWasInterrupted(assignment.EndCondition))
                {
                    ClearStageRetry(patient, assignment.Stage);
                }
                else
                {
                    SetStageRetry(patient, assignment.Stage, now,
                        progressive: FailureWarrantsBackoff(assignment.EndCondition) ||
                                     assignment.EndCondition == JobCondition.None);
                }
                return;
            }

            if (!Compatibility.NeedsAnyFieldTreatment(patient))
            {
                if (!HasDesignation(patient, SearchAndRescueDefOf.SAR_Rescue))
                {
                    RemoveDesignation(patient, SearchAndRescueStage.Treat);
                }
                ClearDesignationRetries(patient, SearchAndRescueStage.Treat);
                // When rescue is still marked, SAR_Treat remains as a dormant deterioration
                // watch until safe delivery retires the combined order.
                RequestScheduleRebuild(maintenance: true, delayTicks: 0);
                return;
            }

            // The casualty still has tendable wounds. If stabilization just crossed its
            // threshold, the next graph pass changes it to the low-priority follow-up lane;
            // if it deteriorated, the same pass promotes it back to emergency treatment.
            // Either way the next graph pass preserves the priority boundary.
            if (assignment.Worker != null && !assignment.Worker.Dead && !assignment.Worker.Downed)
            {
                // Keep a short, soft affinity after a wound boundary. The global matcher may
                // still move the doctor to a substantially more urgent casualty, but small
                // score changes no longer send them across the map and back between wounds.
                RememberCompletedTreatment(assignment.Worker, patient, now);
            }
            ClearDesignationRetries(patient, SearchAndRescueStage.Treat);
            RequestScheduleRebuild(maintenance: true, delayTicks: 0);
        }

        private void RememberCompletedTreatment(Pawn doctor, Pawn patient, int now)
        {
            if (!IsFieldResponder(doctor) || !WorkerOperational(doctor) ||
                !Compatibility.NeedsAnyFieldTreatment(patient)) return;
            SetCareAffinity(patient, new SoftCareClaim(doctor,
                now + PostTreatmentContinuityTicks, 0d, completedTreatment: true), now);
        }

        private void MonitorActiveTreatmentRounds()
        {
            if (activeByTarget.Count == 0)
            {
                return;
            }

            activeTreatmentScratch.Clear();
            activeTreatmentScratch.AddRange(activeByTarget.Values);
            foreach (ActiveAssignment assignment in activeTreatmentScratch)
            {
                if (!IsTreatmentStage(assignment.Stage) || !AssignmentJobStillRunning(assignment))
                {
                    continue;
                }

                // Vanilla/Smart Medicine TendPatient rounds end naturally via
                // endAfterTendedOnce, and More Injuries device jobs are already one-shot.
                // Only aggregate third-party drivers need an effect-based loop breaker.
                if (!Compatibility.RequiresTreatmentEffectMonitor(assignment.Job))
                {
                    continue;
                }

                // RH2 and More Injuries aggregate first-aid drivers normally loop over every wound.
                // Their effects are committed atomically by the driver, so ending after the
                // first observed effect is a completed round rather than a partial vanilla
                // tend progress bar.
                if (TreatmentProgressMade(assignment.Target, assignment))
                {
                    assignment.RoundEffectSeen = true;
                    assignment.Worker.jobs.EndCurrentJob(JobCondition.Succeeded);
                }
            }
            activeTreatmentScratch.Clear();
        }

        private void MonitorTreatmentTravelPreemption()
        {
            int interval = lastKnownCareTargetCount >= LargeBattleTargetThreshold
                ? FullScheduleInterval
                : TravelPreemptionInterval;
            if (!map.IsHashIntervalTick(interval))
            {
                return;
            }

            int now = Find.TickManager.TicksGame;
            List<Pawn> preemptionCandidates = AllCareCandidates();
            foreach (ActiveAssignment assignment in activeByTarget.Values
                         .Where(active => IsTreatmentStage(active.Stage))
                         .ToList())
            {
                Pawn doctor = assignment.Worker;
                Pawn currentPatient = assignment.Target;
                if (!AssignmentJobStillRunning(assignment) || !doctor.pather.Moving || assignment.RoundEffectSeen)
                {
                    continue;
                }

                Pawn replacement = FindTravelPreemptionTarget(
                    doctor,
                    currentPatient,
                    now,
                    preemptionCandidates);
                if (replacement == null)
                {
                    continue;
                }

                double replacementWeight = EdgeWeight(doctor, replacement, SearchAndRescueStage.Treat);
                if (assignment.Stage == SearchAndRescueStage.Treat)
                {
                    double currentWeight = EdgeWeight(doctor, currentPatient, SearchAndRescueStage.Treat);
                    double stayWeight = currentWeight +
                                        TravelContinuityWeight(doctor, currentPatient, assignment, now);
                    if (replacementWeight <= stayWeight)
                    {
                        continue;
                    }
                }

                // A follow-up trip has no commitment advantage over a newly available
                // emergency. During an actual tend bar we still wait for the wound boundary;
                // this branch only preempts doctors who are travelling.

                activeClaims.ReleasePrimary(currentPatient);
                medicalResources.ReleaseWorker(doctor);
                SetStageRetry(currentPatient, assignment.Stage, now,
                    progressive: false, fixedDelay: TreatmentReevaluationDelay);
                lastTravelSwitchAt[doctor] = now;
                MedicalCarePlan plan = carePlans.TryGetValue(replacement, out MedicalCarePlan knownPlan)
                    ? knownPlan
                    : MedicalCarePlan.Build(replacement, now);
                carePlans[replacement] = plan;
                MedicalTreatmentOption option = BestTreatmentOption(doctor, replacement, plan);
                pendingByWorker[doctor] = new PendingAssignment(
                    replacement,
                    SearchAndRescueStage.Treat,
                    replacementWeight,
                    now,
                    false,
                    now + SoftClaimLeaseTicks,
                    option);
                if (option?.Resource != null)
                {
                    medicalResources.TryClaim(
                        option.Resource,
                        doctor,
                        replacement,
                        Math.Max(1, option.Count),
                        option.Reusable,
                        now + SoftClaimLeaseTicks,
                        MedicalResourceAccess.Treatment);
                }
                doctor.jobs.EndCurrentJob(JobCondition.InterruptForced);
            }
        }

        private Pawn FindTravelPreemptionTarget(
            Pawn doctor,
            Pawn currentPatient,
            int now,
            IEnumerable<Pawn> careCandidates)
        {
            HashSet<Pawn> unavailable = new HashSet<Pawn>(activeByTarget.Keys);
            foreach (PendingAssignment pending in pendingByWorker.Values)
            {
                unavailable.Add(pending.Target);
            }

            return careCandidates
                .Where(patient => patient != currentPatient && !unavailable.Contains(patient) &&
                                  TargetReadyForStage(patient, SearchAndRescueStage.Treat, now))
                .Select(patient => new
                {
                    Patient = patient,
                    Weight = EdgeWeight(doctor, patient, SearchAndRescueStage.Treat)
                })
                .Where(candidate => candidate.Weight > 0d)
                .OrderByDescending(candidate => candidate.Weight)
                .Select(candidate => candidate.Patient)
                .FirstOrDefault();
        }

        private double TravelContinuityWeight(Pawn doctor, Pawn currentPatient, ActiveAssignment assignment, int now)
        {
            float initialCommitment = 1f - Mathf.Clamp01(
                (now - assignment.StartedAt) / (float)TravelInitialCommitmentDecayTicks);
            float distance = Mathf.Sqrt(doctor.Position.DistanceToSquared(currentPatient.Position));
            float proximity = 1f - Mathf.Clamp01(distance / TravelNearPatientRadius);
            float recentSwitch = 0f;
            if (lastTravelSwitchAt.TryGetValue(doctor, out int switchedAt))
            {
                recentSwitch = 1f - Mathf.Clamp01(
                    (now - switchedAt) / (float)TravelRecentSwitchDecayTicks);
            }

            return TravelContinuityBaseWeight +
                   initialCommitment * TravelInitialCommitmentWeight +
                   proximity * TravelNearPatientWeight +
                   recentSwitch * TravelRecentSwitchWeight;
        }

        private void CleanupDesignations()
        {
            HashSet<Pawn> marked = new HashSet<Pawn>(AllMarkedPawns());
            if (CoordinationMode == MedicalCoordinationMode.MarkedOnly)
            {
                lastKnownCareTargetCount = marked.Count;
            }
            foreach (Pawn patient in marked)
            {
                if (patient.Dead)
                {
                    RemoveAllStages(patient);
                    continue;
                }

                if (!patient.Spawned)
                {
                    if (activeByTarget.TryGetValue(patient, out ActiveAssignment carriedAssignment) &&
                        IsCarriedByActiveRescuer(patient, carriedAssignment) ||
                        IsCarriedByAnyPawn(patient))
                    {
                        continue;
                    }

                    RemoveAllStages(patient);
                    continue;
                }

                if (patient.Map != map)
                {
                    RemoveAllStages(patient);
                    continue;
                }

                if (patient.InMentalState)
                {
                    // Mental states are temporary control leases. Active work is interrupted
                    // immediately by ActiveTargetControlValid, but the player's durable marks
                    // must survive berserk/manhunter hostility and resume when control returns.
                    continue;
                }

                if (HasDesignation(patient, SearchAndRescueDefOf.SAR_Capture) &&
                    (!TargetEligibility.CanBeCaptured(patient) || patient.IsPrisonerOfColony ||
                     !patient.Downed || !patient.HostileTo(Faction.OfPlayer)))
                {
                    RemoveDesignation(patient, SearchAndRescueStage.Capture);
                }

                bool hasTreatmentWatch = HasDesignation(patient, SearchAndRescueDefOf.SAR_Treat);
                bool watchUntilRescued = hasTreatmentWatch &&
                                         HasDesignation(patient, SearchAndRescueDefOf.SAR_Rescue) &&
                                         patient.Downed && !IsInSafePatientBed(patient);
                if (hasTreatmentWatch &&
                    (!TargetEligibility.CanReceiveFieldCare(patient) ||
                     (!patient.IsPrisonerOfColony && patient.HostileTo(Faction.OfPlayer) &&
                      !HasDesignation(patient, SearchAndRescueDefOf.SAR_Capture)) ||
                     !watchUntilRescued &&
                     !Compatibility.NeedsAnyFieldTreatment(patient)))
                {
                    RemoveDesignation(patient, SearchAndRescueStage.Treat);
                }

                if (HasDesignation(patient, SearchAndRescueDefOf.SAR_Rescue) &&
                    (!TargetEligibility.CanReceiveFieldCare(patient) || !patient.Downed ||
                     (!patient.IsPrisonerOfColony && patient.HostileTo(Faction.OfPlayer) &&
                      !HasDesignation(patient, SearchAndRescueDefOf.SAR_Capture)) ||
                     IsInSafePatientBed(patient)))
                {
                    bool deliveredSafely = IsInSafePatientBed(patient);
                    bool noLongerCareEligible = !TargetEligibility.CanReceiveFieldCare(patient);
                    RemoveDesignation(patient, SearchAndRescueStage.Rescue);
                    if (noLongerCareEligible ||
                        deliveredSafely && !Compatibility.NeedsAnyFieldTreatment(patient))
                    {
                        RemoveDesignation(patient, SearchAndRescueStage.Treat);
                    }
                }
            }

            foreach (StageRetryKey key in retryByStage.Keys
                         .Where(key => !HasAnyCareInterest(key.Target)).ToList())
            {
                retryByStage.Remove(key);
            }

            foreach (Pawn patient in preferredRescuerByTarget.Keys
                         .Where(patient => !HasDesignation(patient, SearchAndRescueDefOf.SAR_Rescue))
                         .ToList())
            {
                preferredRescuerByTarget.Remove(patient);
            }
        }

        private void CleanupOrphanedManagedCarries()
        {
            foreach (Pawn carrier in map.mapPawns.AllPawnsSpawned.ToList())
            {
                Pawn patient = carrier?.carryTracker?.CarriedThing as Pawn;
                if (patient == null || !HasAnyStageDesignation(patient))
                {
                    continue;
                }

                bool activeRescueOwnsCarry = activeByTarget.TryGetValue(patient, out ActiveAssignment active) &&
                                             IsCarriedByActiveRescuer(patient, active);
                Job current = carrier.CurJob;
                bool anotherTransportOwnsCarry = current != null &&
                    CompatibilityRegistry.PatientFor(carrier, current, PatientJobRole.Transport) == patient;
                if (JobOwnershipRules.PreserveManagedCarry(activeRescueOwnsCarry, anotherTransportOwnsCarry))
                {
                    continue;
                }

                bool dropped = carrier.carryTracker.TryDropCarriedThing(
                    carrier.Position,
                    ThingPlaceMode.Near,
                    out _);
                if (!dropped && carrier.carryTracker.CarriedThing == patient)
                {
                    carrier.carryTracker.TryDropCarriedThing(
                        carrier.Position,
                        ThingPlaceMode.Direct,
                        out _);
                }
            }
        }

        private void CleanupRetiredWorkerState()
        {
            foreach (Pawn worker in lastTravelSwitchAt.Keys
                         .Where(worker => worker == null || worker.Destroyed || worker.MapHeld != map)
                         .ToList())
            {
                lastTravelSwitchAt.Remove(worker);
            }

            foreach (Pawn worker in lastSchedulerDecision.Keys
                         .Where(worker => worker == null || worker.Destroyed || worker.MapHeld != map)
                         .ToList())
            {
                lastSchedulerDecision.Remove(worker);
            }
        }

        private void RebuildPendingAssignments(int now, Pawn requestingWorker)
        {
            bool profile = SearchAndRescuePerformanceDiagnostics.Enabled;
            long rebuildStart = profile
                ? SearchAndRescuePerformanceDiagnostics.Begin(SarPerformancePhase.ScheduleRebuild)
                : 0L;
            SearchAndRescuePerformanceDiagnostics.EnterRebuild(requestingWorker != null);
            try
            {
                BeginSchedulingSnapshot();
                try
                {
                    RebuildPendingAssignmentsCore(now, requestingWorker);
                }
                finally
                {
                    EndSchedulingSnapshot();
                }

                // Starting jobs may re-enter work scanning through third-party patches. Keep the
                // per-rebuild cache scoped strictly to graph construction and claim materialization.
                long wakeStart = profile
                    ? SearchAndRescuePerformanceDiagnostics.Begin(SarPerformancePhase.WakePendingWorkers)
                    : 0L;
                try
                {
                    WakePendingWorkers(requestingWorker);
                }
                finally
                {
                    if (profile)
                    {
                        SearchAndRescuePerformanceDiagnostics.End(
                            SarPerformancePhase.WakePendingWorkers,
                            wakeStart);
                    }
                }
            }
            finally
            {
                if (profile)
                {
                    SearchAndRescuePerformanceDiagnostics.End(
                        SarPerformancePhase.ScheduleRebuild,
                        rebuildStart);
                }
                SearchAndRescuePerformanceDiagnostics.ExitRebuild();
            }
        }

        private void RebuildPendingAssignmentsCore(int now, Pawn requestingWorker)
        {
            bool profile = SearchAndRescuePerformanceDiagnostics.Enabled;
            long carePlanningStart = profile
                ? SearchAndRescuePerformanceDiagnostics.Begin(SarPerformancePhase.CarePlanning)
                : 0L;
            List<Pawn> allCareTargets = AllCareCandidates();
            lastKnownCareTargetCount = allCareTargets.Count;
            RefreshCarePlans(now, allCareTargets);
            if (profile)
            {
                SearchAndRescuePerformanceDiagnostics.End(SarPerformancePhase.CarePlanning, carePlanningStart);
            }
            Dictionary<Pawn, PendingAssignment> previous = pendingByWorker
                .Where(pair => PendingAssignmentValid(pair.Key, pair.Value, now))
                .ToDictionary(pair => pair.Key, pair => pair.Value);

            foreach (Pawn pendingWorker in pendingByWorker.Keys.ToList())
            {
                medicalResources.ReleaseWorker(pendingWorker);
            }
            pendingByWorker.Clear();
            HashSet<Pawn> usedWorkers = new HashSet<Pawn>(activeByTarget.Values.Select(active => active.Worker));
            usedWorkers.UnionWith(standbyByTarget.Values.Select(standby => standby.Worker));
            usedWorkers.UnionWith(activeLogisticsByWorker.Keys);

            List<Pawn> targets = allCareTargets
                .Where(TargetAvailableForUnifiedMatching)
                .OrderBy(patient => patient.thingIDNumber)
                .ToList();

            List<Pawn> workers = WorkerCandidates()
                .Where(worker => !usedWorkers.Contains(worker) && WorkerReadyForAnyStage(worker) &&
                                 (worker == requestingWorker ||
                                  WorkerAvailableForMatching(worker) ||
                                  targets.Any(patient =>
                                      CanPreemptRoutineForStage(
                                          worker, patient, SearchAndRescueStage.Capture, now) ||
                                      CanPreemptRoutineForStage(
                                          worker, patient, SearchAndRescueStage.Treat, now))))
                .OrderBy(worker => worker.thingIDNumber)
                .ToList();
            int readyTreatmentTargets = targets.Count(patient =>
                TargetReadyForStage(patient, SearchAndRescueStage.Treat, now));
            int availableTreatmentWorkers = workers.Count(worker =>
                WorkerReadyForStage(worker, SearchAndRescueStage.Treat));
            if (availableTreatmentWorkers > 0)
            {
                treatmentDetourBacklogPressure = Math.Min(
                    540d,
                    Math.Max(0, readyTreatmentTargets - availableTreatmentWorkers) *
                    360d / availableTreatmentWorkers);
            }
            else if (readyTreatmentTargets == 0)
            {
                treatmentDetourBacklogPressure = 0d;
            }
            foreach (Pawn worker in workers)
            {
                lastSchedulerDecision[worker] = $"candidate in rebuild at {now}";
            }
            Dictionary<WorkerTargetPair, StageChoice> choices = new Dictionary<WorkerTargetPair, StageChoice>();
            SearchAndRescuePerformanceDiagnostics.RecordGraph(false, workers.Count, targets.Count);
            long matchingStart = profile
                ? SearchAndRescuePerformanceDiagnostics.Begin(SarPerformancePhase.UnifiedMatching)
                : 0L;
            List<Match<Pawn, Pawn>> matches = WeightedBipartiteMatcher.MaximumWeight(
                workers,
                targets,
                (worker, patient) =>
                {
                    StageChoice choice = BestStageChoice(worker, patient, now, previous);
                    choices[new WorkerTargetPair(worker, patient)] = choice;
                    return choice.Weight;
                });
            if (profile)
            {
                SearchAndRescuePerformanceDiagnostics.End(SarPerformancePhase.UnifiedMatching, matchingStart);
            }

            HashSet<Pawn> treatmentTargets = new HashSet<Pawn>();
            foreach (Match<Pawn, Pawn> match in matches.OrderByDescending(match => match.Weight))
            {
                if (!choices.TryGetValue(new WorkerTargetPair(match.Worker, match.Target), out StageChoice choice) ||
                    !choice.IsValid)
                {
                    lastSchedulerDecision[match.Worker] =
                        $"matched invalid edge to {match.Target?.ThingID} at {now}";
                    continue;
                }

                // Rescue choices participate in the joint graph, but are finalized below in
                // a transport-only graph together with standby choices. This lets a hauler
                // compare "move another casualty" against "wait for this treatment".
                if (choice.Stage == SearchAndRescueStage.Rescue)
                {
                    continue;
                }

                if (requestingWorker != null && match.Worker != requestingWorker &&
                    choice.Stage == SearchAndRescueStage.Treat &&
                    activeByTarget.TryGetValue(match.Target, out ActiveAssignment plannedRescue) &&
                    plannedRescue.Stage == SearchAndRescueStage.Rescue)
                {
                    // A request-scoped rebuild (notably a Priority Treatment pulse) can
                    // calculate useful edges for every doctor, but only the requesting
                    // doctor is guaranteed to claim a job immediately after this method.
                    // Interrupting a carrier for another doctor's merely pending edge drops
                    // the patient, then lets the carrier claim Rescue again on the next scan.
                    // Leave that destructive transition to a global rebuild, or to the
                    // matching doctor's own request-scoped rebuild.
                    continue;
                }

                if (!TryClaimStageChoice(match.Worker, match.Target, choice, now))
                {
                    // Another winning edge may have consumed the same stack/device. Re-price
                    // this edge against the updated ledger so it can fall back to CPR,
                    // medicine-less tending, or another supply instead of losing the worker.
                    choice = BestStageChoice(match.Worker, match.Target, now, previous);
                    if (!choice.IsValid || !TryClaimStageChoice(match.Worker, match.Target, choice, now))
                    {
                        lastSchedulerDecision[match.Worker] =
                            $"resource claim failed twice for {choice.Stage}:{match.Target?.ThingID} at {now}";
                        continue;
                    }
                }
                if (!usedWorkers.Add(match.Worker))
                {
                    medicalResources.ReleaseWorker(match.Worker);
                    continue;
                }

                if (activeByTarget.TryGetValue(match.Target, out ActiveAssignment activeRescue))
                {
                    if (activeRescue.Stage != SearchAndRescueStage.Rescue || choice.Stage != SearchAndRescueStage.Treat)
                    {
                        usedWorkers.Remove(match.Worker);
                        medicalResources.ReleaseWorker(match.Worker);
                        continue;
                    }

                    // This is a two-phase hand-off. Matching records the doctor's soft
                    // claim while the carrier continues Rescue. TryIssueJob first builds a
                    // concrete treatment job, then drops/intercepts the patient. If job
                    // construction fails, no destructive carrier transition has happened.
                }

                int createdAt = previous.TryGetValue(match.Worker, out PendingAssignment old) &&
                                !old.WaitForTreatment && old.Stage == choice.Stage && old.Target == match.Target
                    ? old.CreatedAt
                    : now;
                pendingByWorker[match.Worker] = new PendingAssignment(
                    match.Target,
                    choice.Stage,
                    choice.Weight,
                    createdAt,
                    false,
                    now + SoftClaimLeaseTicks,
                    choice.Treatment,
                    choice.Kit);
                lastSchedulerDecision[match.Worker] =
                    $"pending {choice.Stage}:{match.Target?.ThingID} weight={choice.Weight:0} at {now}";
                if ((choice.Stage == SearchAndRescueStage.Treat || choice.Stage == SearchAndRescueStage.Restock) &&
                    HasDesignation(match.Target, SearchAndRescueDefOf.SAR_Rescue))
                {
                    treatmentTargets.Add(match.Target);
                }
            }

            foreach (KeyValuePair<Pawn, ActiveAssignment> pair in activeByTarget)
            {
                if ((pair.Value.Stage == SearchAndRescueStage.Treat || pair.Value.Stage == SearchAndRescueStage.Restock) &&
                    HasDesignation(pair.Key, SearchAndRescueDefOf.SAR_Rescue))
                {
                    treatmentTargets.Add(pair.Key);
                }
            }

            foreach (Pawn patient in allCareTargets.Where(patient =>
                         TreatmentAdmitted(patient, SearchAndRescueStage.Treat) &&
                         HasDesignation(patient, SearchAndRescueDefOf.SAR_Rescue) &&
                         FindCurrentTreatingDoctor(patient) != null))
            {
                treatmentTargets.Add(patient);
            }

            long transportStart = profile
                ? SearchAndRescuePerformanceDiagnostics.Begin(SarPerformancePhase.TransportMatching)
                : 0L;
            ScheduleTransportAndStandbyAssignments(
                now,
                requestingWorker,
                previous,
                usedWorkers,
                treatmentTargets,
                allCareTargets);
            if (profile)
            {
                SearchAndRescuePerformanceDiagnostics.End(SarPerformancePhase.TransportMatching, transportStart);
            }
            foreach (Pawn worker in workers.Where(worker =>
                         !pendingByWorker.ContainsKey(worker) &&
                         !activeClaims.HasPrimaryWorker(worker) &&
                         lastSchedulerDecision.TryGetValue(worker, out string decision) &&
                         decision == $"candidate in rebuild at {now}"))
            {
                lastSchedulerDecision[worker] = $"no winning graph edge at {now}";
            }
            lastScheduleTick = now;
            int nextRetryTick = retryByStage.Values
                .Select(retry => retry.RetryAfter)
                .Where(tick => tick > now)
                .DefaultIfEmpty(int.MaxValue)
                .Min();
            scheduleDirty = nextRetryTick != int.MaxValue;
            scheduleNotBeforeTick = scheduleDirty ? nextRetryTick : now + DirtyDebounceTicks;
        }

        private void WakePendingWorkers(Pawn requestingWorker)
        {
            if (requestingWorker != null)
            {
                // Rebuilds requested from inside a WorkGiver are already running in this
                // pawn's DetermineNextJob call. Re-entering the ThinkTree here would recurse.
                // The claim may belong to a WorkGiver that was already scanned, however, so
                // wake it safely from the next map tick instead of waiting 120 ticks.
                if (pendingByWorker.ContainsKey(requestingWorker))
                {
                    deferredWakeWorkers.Add(requestingWorker);
                }
                return;
            }

            foreach (Pawn worker in pendingByWorker.Keys.ToList())
            {
                TryWakePendingWorker(worker);
            }
        }

        private bool TryWakePendingWorker(Pawn worker)
        {
            if (!WorkerOperational(worker) || !IsFieldResponder(worker) ||
                !pendingByWorker.TryGetValue(worker, out PendingAssignment pending))
            {
                return true;
            }

            if (IsSoftStageTransitionJob(worker.CurJob))
            {
                // At idle and completed-work boundaries the graph can materialize its result
                // directly, avoiding competition between the role-specific WorkGiver scans.
                Job responderJob = TryIssuePendingResponderJob(worker, pending);
                if (responderJob != null)
                {
                    worker.jobs.StartJob(responderJob, JobCondition.InterruptOptional);
                }
                return true;
            }

            if (!worker.mindState.IsIdle &&
                !CanPreemptRoutineForStage(worker, pending.Target, pending.Stage, Find.TickManager.TicksGame))
            {
                return false;
            }

            // Non-transition idle jobs and the deliberately interruptible emergency jobs still
            // enter through the ThinkTree so normal work priority arbitration remains intact.
            worker.jobs.CheckForJobOverride();
            if (pendingByWorker.ContainsKey(worker))
            {
                lastSchedulerDecision[worker] =
                    $"wake left pending assignment; current job={worker.CurJobDef?.defName ?? "none"} at {Find.TickManager.TicksGame}";
            }
            return true;
        }

        private Job TryIssuePendingResponderJob(Pawn worker, PendingAssignment pending)
        {
            switch (pending.Stage)
            {
                case SearchAndRescueStage.Capture:
                    return TryIssueJob(
                        worker,
                        Compatibility.CanPerformCaptureWork(worker)
                            ? SearchAndRescueStage.Capture
                            : SearchAndRescueStage.Treat,
                        RescueWorkProvider.None);
                case SearchAndRescueStage.Treat:
                case SearchAndRescueStage.Restock:
                    return TryIssueJob(worker, SearchAndRescueStage.Treat, RescueWorkProvider.None);
                case SearchAndRescueStage.FollowupTreat:
                    return TryIssueJob(worker, SearchAndRescueStage.FollowupTreat, RescueWorkProvider.None);
                case SearchAndRescueStage.Rescue:
                    return TryIssueJob(worker, SearchAndRescueStage.Rescue,
                        Compatibility.RescueProviderFor(worker));
                case SearchAndRescueStage.Supply:
                    return TryIssueJob(worker, SearchAndRescueStage.Supply, RescueWorkProvider.Hauling);
                default:
                    return null;
            }
        }

        private static bool IsSoftStageTransitionJob(Job job)
        {
            if (job == null)
            {
                return true;
            }

            string defName = job.def?.defName;
            return defName == "Wait_MaintainPosture" ||
                   defName == "Wait_Wander" ||
                   defName == "GotoWander";
        }

        private static bool IsInterruptibleRoutineJob(Job job)
        {
            if (job == null || job.playerForced)
            {
                return false;
            }

            string defName = job.def?.defName;
            return defName == "FeedPatient" || defName == "Clean";
        }

        private static bool IsRoutineBoundaryJob(Job job)
        {
            string defName = job?.def?.defName;
            bool ordinaryWorkGiverJob = job?.workGiverDef != null &&
                                        job.workGiverDef.defName?.StartsWith(
                                            "SAR_",
                                            StringComparison.Ordinal) != true;
            return ordinaryWorkGiverJob ||
                   defName == "BuildRoof" || defName == "FinishFrame" ||
                   defName == "Repair" || defName == "Deconstruct" ||
                   defName == "Clean" || defName == "HaulToCell" ||
                   defName == "HaulToContainer" || defName == "TakeInventory";
        }

        private bool CanPreemptRoutineForStage(
            Pawn worker,
            Pawn patient,
            SearchAndRescueStage stage,
            int now)
        {
            if (!IsInterruptibleRoutineJob(worker?.CurJob))
            {
                return false;
            }

            if (SearchAndRescueMod.Settings?.PreemptRoutineWorkForEmergencies == false)
            {
                return false;
            }

            SearchAndRescueStage emergencyStage = stage == SearchAndRescueStage.Restock
                ? SearchAndRescueStage.Treat
                : stage;
            return (emergencyStage == SearchAndRescueStage.Capture ||
                    emergencyStage == SearchAndRescueStage.Treat) &&
                   WorkerReadyForTargetStage(worker, patient, emergencyStage) &&
                   TargetReadyForStage(patient, emergencyStage, now);
        }

        private void WakeDeferredPendingWorkers()
        {
            foreach (Pawn worker in deferredWakeWorkers.ToList())
            {
                if (TryWakePendingWorker(worker))
                {
                    deferredWakeWorkers.Remove(worker);
                }
            }
        }

        private void RefreshCarePlans(int now, IEnumerable<Pawn> careTargets)
        {
            HashSet<Pawn> treatmentTargets = new HashSet<Pawn>(careTargets.Where(patient =>
                HasTreatmentInterest(patient) && patient.health != null && !patient.Dead &&
                Compatibility.NeedsAnyFieldTreatment(patient)));
            foreach (Pawn patient in treatmentTargets)
            {
                carePlans[patient] = MedicalCarePlan.Build(patient, now);
            }
            foreach (Pawn stale in carePlans.Keys.Where(patient => !treatmentTargets.Contains(patient)).ToList())
            {
                carePlans.Remove(stale);
                medicalResources.ReleasePatient(stale);
            }
        }

        private void CleanupSoftCareClaims(int now)
        {
            foreach (Pawn patient in careAffinityClaims.Keys.Where(patient =>
                         patient == null || patient.Dead || !patient.Spawned || patient.Map != map ||
                         !HasTreatmentInterest(patient) ||
                         careAffinityClaims[patient].ExpiresAt <= now ||
                         !WorkerOperational(careAffinityClaims[patient].Worker) ||
                         !IsFieldResponder(careAffinityClaims[patient].Worker))
                     .ToList())
            {
                careAffinityClaims.Remove(patient);
            }
        }

        private void SetCareAffinity(Pawn patient, SoftCareClaim claim, int now)
        {
            if (patient == null || claim == null) return;
            careAffinityClaims.TryGetValue(patient, out SoftCareClaim existing);
            if (TreatmentContinuityRules.ShouldReplace(existing?.ExpiresAt > now,
                    existing?.CompletedTreatment == true, claim.CompletedTreatment,
                    existing?.WeightAt(now) ?? 0d, claim.WeightAt(now)))
                careAffinityClaims[patient] = claim;
        }

        private bool TryClaimStageChoice(Pawn worker, Pawn patient, StageChoice choice, int now)
        {
            if (choice.Kit != null && !choice.Kit.IsEmpty)
            {
                foreach (ThingCount item in choice.Kit.Items)
                {
                    if (!medicalResources.TryClaim(
                            item.Thing,
                            worker,
                            patient,
                            item.Count,
                            item.Thing.def.stackLimit == 1,
                            now + SoftClaimLeaseTicks,
                            MedicalResourceAccess.Relocation))
                    {
                        medicalResources.ReleaseWorker(worker);
                        return false;
                    }
                }
                foreach (Pawn plannedPatient in choice.Kit.PlannedPatients.Take(MaxMissionKitPatients))
                {
                    SetCareAffinity(plannedPatient, new SoftCareClaim(
                        worker,
                        now + SoftClaimLeaseTicks * 3,
                        PendingContinuityBaseWeight,
                        50000d,
                        SoftClaimLeaseTicks * 3), now);
                }
                return true;
            }

            MedicalTreatmentOption option = choice.Treatment;
            return option?.Resource == null || medicalResources.TryClaim(
                option.Resource,
                worker,
                patient,
                Math.Max(1, option.Count),
                option.Reusable,
                now + SoftClaimLeaseTicks,
                MedicalResourceAccess.Treatment);
        }

        private void ClaimPendingResources(Pawn worker, PendingAssignment pending, int expiresAt)
        {
            if (pending.Kit != null && !pending.Kit.IsEmpty)
            {
                foreach (ThingCount item in pending.Kit.Items)
                {
                    medicalResources.TryClaim(
                        item.Thing,
                        worker,
                        pending.Target,
                        item.Count,
                        item.Thing.def.stackLimit == 1,
                        expiresAt,
                        MedicalResourceAccess.Relocation);
                }
                return;
            }

            MedicalTreatmentOption option = pending.Treatment;
            if (option?.Resource != null)
            {
                medicalResources.TryClaim(
                    option.Resource,
                    worker,
                    pending.Target,
                    Math.Max(1, option.Count),
                    option.Reusable,
                    expiresAt,
                    MedicalResourceAccess.Treatment);
            }
            else if (pending.SupplyResource != null)
            {
                medicalResources.TryClaim(
                    pending.SupplyResource,
                    worker,
                    pending.Target,
                    Math.Max(1, pending.SupplyCount),
                    pending.SupplyResource.def.stackLimit == 1,
                    expiresAt,
                    MedicalResourceAccess.Relocation);
            }
        }

        private IEnumerable<Pawn> WorkerCandidates()
        {
            EnsureFieldResponderWorkTypeMigrated();
            return map.mapPawns.AllPawnsSpawned.Where(pawn =>
                pawn != null && pawn.Map == map && IsFieldResponder(pawn));
        }

        private bool TargetAvailableForUnifiedMatching(Pawn patient)
        {
            if (!activeByTarget.TryGetValue(patient, out ActiveAssignment active))
            {
                return true;
            }

            // An evacuation remains a candidate solely for a doctor-interception edge.
            return active.Stage == SearchAndRescueStage.Rescue &&
                   TreatmentAdmitted(patient, SearchAndRescueStage.Treat);
        }

        private StageChoice BestStageChoice(
            Pawn worker,
            Pawn patient,
            int now,
            IReadOnlyDictionary<Pawn, PendingAssignment> previous)
        {
            bool profile = SearchAndRescuePerformanceDiagnostics.Enabled;
            long started = profile
                ? SearchAndRescuePerformanceDiagnostics.Begin(SarPerformancePhase.UnifiedEdgeScoring)
                : 0L;
            try
            {
                return BestStageChoiceCore(worker, patient, now, previous);
            }
            finally
            {
                if (profile)
                {
                    SearchAndRescuePerformanceDiagnostics.End(SarPerformancePhase.UnifiedEdgeScoring, started);
                }
            }
        }

        private StageChoice BestStageChoiceCore(
            Pawn worker,
            Pawn patient,
            int now,
            IReadOnlyDictionary<Pawn, PendingAssignment> previous)
        {
            StageChoice best = StageChoice.Invalid;
            foreach (SearchAndRescueStage stage in UnifiedMatchingStages)
            {
                if (IsInterruptibleRoutineJob(worker?.CurJob) && worker.mindState?.IsIdle != true &&
                    !CanPreemptRoutineForStage(worker, patient, stage, now))
                {
                    // Cleaning and feeding may be interrupted for marked life-saving work,
                    // never for stable follow-up care or ordinary transport.
                    continue;
                }

                if (!WorkerReadyForTargetStage(worker, patient, stage) ||
                    stage == SearchAndRescueStage.FollowupTreat &&
                    !WorkerReadyForFollowupLane(worker, patient) ||
                    !TargetReadyForStage(patient, stage, now))
                {
                    continue;
                }

                if (stage == SearchAndRescueStage.FollowupTreat &&
                    AutomaticRoutineRequiresNativePosture(patient) &&
                    !WorkGiver_Tend.GoodLayingStatusForTend(patient, worker))
                {
                    // All-tending coordinates ordinary bedside care; it must not turn a
                    // minor wound or illness into an implicit battlefield tend-in-place.
                    // The native patient AI can lie down first. Explicit marks and genuine
                    // emergencies retain the field-care behavior.
                    continue;
                }

                bool rescueInterception = stage == SearchAndRescueStage.Treat &&
                                          activeByTarget.TryGetValue(patient, out ActiveAssignment reservedActive) &&
                                          reservedActive.Stage == SearchAndRescueStage.Rescue;
                if (!rescueInterception && !worker.CanReserve(patient, 1, -1))
                {
                    // The coordinator's claims are intentionally soft, but the actual pawn
                    // reservation remains authoritative. This also catches vanilla doctors
                    // and managed jobs restored from a save before we construct a duplicate.
                    continue;
                }

                SearchAndRescueStage selectedStage = stage;
                MedicalTreatmentOption treatment = null;
                MedicalKitBundle kit = null;
                double weight;
                if (IsTreatmentStage(stage))
                {
                    if (!carePlans.TryGetValue(patient, out MedicalCarePlan plan))
                    {
                        plan = MedicalCarePlan.Build(patient, now);
                        carePlans[patient] = plan;
                    }
                    treatment = BestTreatmentOption(
                        worker,
                        patient,
                        plan,
                        allowExternalInventory: stage != SearchAndRescueStage.FollowupTreat,
                        stage: stage);
                    if (!treatment.IsValid)
                    {
                        continue;
                    }

                    weight = TreatmentEdgeWeight(worker, patient, treatment, stage);
                    bool interceptingRescue = stage == SearchAndRescueStage.Treat &&
                                               activeByTarget.TryGetValue(patient, out ActiveAssignment active) &&
                                               active.Stage == SearchAndRescueStage.Rescue;
                    if (stage == SearchAndRescueStage.Treat && !interceptingRescue)
                    {
                        kit = BuildMissionKit(worker, patient, treatment);
                        if (kit != null && !kit.IsEmpty)
                        {
                            if (IsRetryBlocked(patient, SearchAndRescueStage.Restock, now))
                            {
                                // A failed kit route must not suppress dry/direct treatment.
                                kit = null;
                            }
                            else
                            {
                                selectedStage = SearchAndRescueStage.Restock;
                            }
                        }
                    }
                }
                else
                {
                    weight = EdgeWeight(worker, patient, stage);
                }
                if (weight <= 0d)
                {
                    continue;
                }

                if (selectedStage == SearchAndRescueStage.Treat || selectedStage == SearchAndRescueStage.Restock)
                {
                    weight += TreatmentBeforeTransportWeight;
                    if (activeByTarget.TryGetValue(patient, out ActiveAssignment active) &&
                        active.Stage == SearchAndRescueStage.Rescue)
                    {
                        weight += RescueInterceptionWeight;
                    }
                }

                if (previous.TryGetValue(worker, out PendingAssignment old) && !old.WaitForTreatment &&
                    StageMatchesRequest(old.Stage, stage) && old.Target == patient)
                {
                    float freshness = 1f - Mathf.Clamp01(
                        (now - old.CreatedAt) / (float)PendingContinuityDecayTicks);
                    weight += PendingContinuityBaseWeight + freshness * PendingFreshPairWeight;
                }
                if (IsTreatmentStage(stage) &&
                    careAffinityClaims.TryGetValue(patient, out SoftCareClaim careClaim) &&
                    careClaim.Worker == worker && careClaim.ExpiresAt > now)
                {
                    weight += careClaim.WeightAt(now);
                }

                if (weight > best.Weight)
                {
                    best = new StageChoice(selectedStage, weight, treatment, kit);
                }
            }

            return best;
        }

        private double TreatmentEdgeWeight(
            Pawn worker,
            Pawn patient,
            MedicalTreatmentOption option,
            SearchAndRescueStage stage = SearchAndRescueStage.Treat)
        {
            double predictedQuality = Compatibility.PredictTreatmentQuality(
                worker,
                patient,
                option.Intervention);
            double urgency = PatientUrgency(patient);
            double scarcity = medicalResources.ScarcityPrice(option.Resource, option.Reusable);
            double routeCost = stage == SearchAndRescueStage.FollowupTreat
                ? FollowupTreatmentRouteCost
                : EmergencyTreatmentRouteCost;
            double manualAffinity = TryGetCareAdmission(patient, out CareAdmission admission) &&
                                    admission.HasManualTreatment
                ? ManualTreatmentAffinityWeight
                : 0d;
            int workPriority = TreatmentPriorityFor(worker, option.Intervention, stage);
            double workPriorityWeight = workPriority > 0
                ? (5 - Math.Min(4, Math.Max(1, workPriority))) * 6000d
                : 0d;
            return AssignmentBaseWeight + urgency * predictedQuality * 120000d +
                   urgency * option.Benefit * 30000d + predictedQuality * 3000d -
                   option.RouteDistance * routeCost - scarcity -
                   TreatmentDetourPenalty(worker, patient, option) +
                   Compatibility.TreatmentRoleFitBonus(worker, option.Intervention) +
                   Compatibility.TransfusionUrgencyBonus(patient, option.Intervention) +
                   manualAffinity + workPriorityWeight +
                   TreatmentDeadlineWeight(
                       worker,
                       patient,
                       option.RouteDistance,
                       TreatmentBaseDuration(option.Intervention));
        }

        private static int TreatmentPriorityFor(
            Pawn worker,
            MedicalIntervention intervention,
            SearchAndRescueStage stage)
        {
            if (intervention == MedicalIntervention.MechRepair) return MechanicalCare.WorkPriority(worker);
            if (Compatibility.IsSupportiveIntervention(intervention))
            {
                return Compatibility.SupportiveTreatmentWorkPriority(worker);
            }

            if (stage != SearchAndRescueStage.FollowupTreat)
            {
                return Compatibility.TreatmentWorkPriority(worker);
            }

            int marked = Compatibility.FollowupTreatmentWorkPriority(worker);
            int automatic = Compatibility.AutomaticRoutineTreatmentWorkPriority(worker);
            if (marked <= 0)
            {
                return automatic;
            }
            return automatic <= 0 ? marked : Math.Min(marked, automatic);
        }

        private MedicalTreatmentOption BestTreatmentOption(
            Pawn worker,
            Pawn patient,
            MedicalCarePlan plan,
            bool allowExternalInventory = true,
            SearchAndRescueStage stage = SearchAndRescueStage.Treat)
        {
            return Compatibility.FindTreatmentOptions(worker, patient, plan, medicalResources)
                       .Where(option => allowExternalInventory || option.Resource == null ||
                                        MedicalResourceLedger.InventoryHolder(option.Resource) == null ||
                                        MedicalResourceLedger.InventoryHolder(option.Resource) == worker ||
                                        MedicalResourceLedger.InventoryHolder(option.Resource) == patient)
                       .OrderByDescending(option => TreatmentEdgeWeight(worker, patient, option, stage))
                       .FirstOrDefault() ?? MedicalTreatmentOption.Invalid;
        }

        private double TreatmentDetourPenalty(
            Pawn doctor,
            Pawn patient,
            MedicalTreatmentOption option)
        {
            if (doctor == null || patient == null || option == null || option.FromInventory ||
                option.Resource == null)
            {
                return 0d;
            }

            double directDistance = Math.Sqrt(doctor.Position.DistanceToSquared(patient.Position));
            double detourDistance = Math.Max(0d, option.RouteDistance - directDistance);
            float moveSpeed = Math.Max(0.1f, doctor.GetStatValue(StatDefOf.MoveSpeed));
            double detourTicks = detourDistance * 60d / moveSpeed;

            int deadline = HealthUtility.TicksUntilDeathDueToBloodLoss(patient);
            double deadlinePressure = deadline == int.MaxValue
                ? 0d
                : 1d - Math.Max(0d, Math.Min(1d, deadline / 12000d));
            double untreatedInjuryPressure = Math.Min(4d, PatientUrgency(patient)) * 30d;

            // Time has a baseline opportunity cost even for a stable casualty. As the
            // number/severity of untreated injuries rises or the blood-loss horizon closes,
            // each tick spent walking away from the patient is priced much more heavily. The
            // intervention's Benefit remains on the positive side of TreatmentEdgeWeight, so
            // high-value emergency devices can still justify a substantial detour.
            return detourTicks * (30d + untreatedInjuryPressure + deadlinePressure * 90d +
                                  treatmentDetourBacklogPressure);
        }

        private MedicalKitBundle BuildMissionKit(
            Pawn doctor,
            Pawn primaryPatient,
            MedicalTreatmentOption option)
        {
            // These finders consume map resources through their own native Tend driver.
            if (RobotMedicalProfile.OwnsMedicineSelection(primaryPatient)) return null;
            if (option?.Resource == null || option.Resource.Destroyed || option.FromInventory)
            {
                return null;
            }

            if (medicalResources.IsProtectedFieldSupply(option.Resource))
            {
                // A referenced field stack is available for this casualty's immediate
                // one-round treatment, but must never be vacuumed into a multi-patient kit.
                return null;
            }

            List<MedicalCarePlan> routePlans = carePlans.Values
                .Where(plan => plan.Patient != null && plan.Patient.Spawned && !plan.Patient.Dead &&
                               TryGetCareAdmission(plan.Patient, out CareAdmission admission) &&
                               admission.AllowsLogistics)
                .OrderBy(plan => plan.Patient == primaryPatient ? 0 : 1)
                .ThenBy(plan => plan.Patient.Position.DistanceToSquared(primaryPatient.Position))
                .Take(MaxMissionKitPatients)
                .ToList();
            ThingDef primaryDef = option.Resource.def;
            int desired = Math.Max(1, option.Count);
            if (primaryDef.IsMedicine)
            {
                desired = Math.Max(desired,
                    routePlans.Where(plan =>
                            Compatibility.AllowsMedicine(plan.Patient, primaryDef))
                        .Sum(plan => plan.EssentialMedicineRounds));
            }
            desired = Mathf.Clamp(desired, 1, option.Reusable ? 1 : MaxMissionKitConsumables);
            if (Compatibility.UsesCombatExtended)
            {
                int capacity = Compatibility.CombatExtendedInventoryCapacity(doctor, option.Resource);
                if (capacity <= 0)
                {
                    return null;
                }
                desired = Math.Min(desired, capacity);
            }
            int take = Math.Min(desired,
                medicalResources.AvailableForRelocation(option.Resource, doctor));
            List<ThingCount> items = take > 0
                ? new List<ThingCount> { new ThingCount(option.Resource, take) }
                : new List<ThingCount>();

            // A mission kit may amortize the already-approved pickup from this same stack,
            // but it must never introduce extra pickup stops whose detours were not scored.
            bool worthRestocking = MedicalResourceLedger.InventoryHolder(option.Resource) != null || take > 1;
            return worthRestocking
                ? new MedicalKitBundle(items, routePlans.Select(plan => plan.Patient))
                : null;
        }

        private sealed class SoftCareClaim
        {
            public readonly Pawn Worker;
            public readonly int ExpiresAt;
            private readonly double baseWeight;
            private readonly double freshnessWeight;
            private readonly int freshnessTicks;
            public readonly bool ConsumeOnTreatmentStart;
            public readonly bool CompletedTreatment;

            public SoftCareClaim(
                Pawn worker,
                int expiresAt,
                double baseWeight,
                double freshnessWeight = 0d,
                int freshnessTicks = 1,
                bool consumeOnTreatmentStart = false,
                bool completedTreatment = false)
            {
                Worker = worker;
                ExpiresAt = expiresAt;
                this.baseWeight = baseWeight;
                this.freshnessWeight = freshnessWeight;
                this.freshnessTicks = Math.Max(1, freshnessTicks);
                ConsumeOnTreatmentStart = consumeOnTreatmentStart;
                CompletedTreatment = completedTreatment;
            }

            public double WeightAt(int now)
            {
                if (CompletedTreatment) return TreatmentContinuityRules.Weight(ExpiresAt - now);
                float freshness = Mathf.Clamp01((ExpiresAt - now) / (float)freshnessTicks);
                return baseWeight + freshness * freshnessWeight;
            }
        }

        private void ScheduleTransportAndStandbyAssignments(
            int now,
            Pawn requestingWorker,
            IReadOnlyDictionary<Pawn, PendingAssignment> previous,
            ISet<Pawn> usedWorkers,
            ISet<Pawn> treatmentTargets,
            IReadOnlyCollection<Pawn> allMarked)
        {
            Dictionary<Pawn, ActiveStandby> existingStandbys = standbyByTarget.Values
                .Where(standby => standby.Worker != null)
                .GroupBy(standby => standby.Worker)
                .ToDictionary(group => group.Key, group => group.First());
            foreach (Pawn standbyWorker in existingStandbys.Keys)
            {
                // Active standbys are intentionally reconsidered by this graph. They remain
                // unavailable to the capture/treatment graph above.
                usedWorkers.Remove(standbyWorker);
            }

            List<Pawn> transportCandidates = WorkerCandidates()
                .Concat(existingStandbys.Keys)
                .Distinct()
                .ToList();
            List<Pawn> workers = transportCandidates
                .Where(worker => !usedWorkers.Contains(worker) &&
                                  (WorkerReadyForStage(worker, SearchAndRescueStage.Rescue, true) ||
                                   WorkerReadyForStage(worker, SearchAndRescueStage.Supply, true)) &&
                                  (worker == requestingWorker || WorkerAvailableForMatching(worker) ||
                                   existingStandbys.ContainsKey(worker)))
                .OrderBy(worker => worker.thingIDNumber)
                .ToList();

            // Rebalance nearby quotas before the no-worker short circuit. References are a
            // treatment resource allocation, not a hauling side effect: a newly critical
            // patient must be able to displace a stale stable-patient quota even when every
            // hauler is currently busy.
            RebalanceNearbyFieldSupplyReferences();
            if (workers.Count == 0)
            {
                // Do not enumerate rescue destinations, treatment ETAs or new resource deficits
                // when the resulting graph has no worker row. If a worker becomes available,
                // the job-boundary dirty notification rebuilds immediately and performs the
                // full nearby-supply reconciliation before that worker is woken.
                SearchAndRescuePerformanceDiagnostics.RecordGraph(true, 0, 0);
                SearchAndRescuePerformanceDiagnostics.RecordTransportNoCapableWorkerSkip();
                foreach (ActiveStandby standby in existingStandbys.Values)
                {
                    StopStandby(standby.Target);
                }
                return;
            }

            List<TransportTask> tasks = allMarked
                .Where(patient => !treatmentTargets.Contains(patient) &&
                                  TargetReadyForStage(patient, SearchAndRescueStage.Rescue, now))
                .Select(patient => new TransportTask(patient, false))
                .Concat(treatmentTargets
                    .Where(patient => StandbyTargetValid(patient, now) &&
                                      TryGetActiveTreatmentEta(patient, now, out _, out _, out _))
                    .Select(patient => new TransportTask(patient, true)))
                .Concat(BuildSupplyTasks(now))
                .OrderBy(task => task.Target.thingIDNumber)
                .ToList();
            if (tasks.Count == 0)
            {
                foreach (ActiveStandby standby in existingStandbys.Values)
                {
                    StopStandby(standby.Target);
                }
                return;
            }

            Dictionary<Pawn, List<TransportTask>> tasksByPatient = tasks
                .GroupBy(task => task.Target)
                .ToDictionary(group => group.Key, group => group.ToList());
            List<Pawn> transportTargets = tasksByPatient.Keys
                .OrderBy(patient => patient.thingIDNumber)
                .ToList();
            SearchAndRescuePerformanceDiagnostics.RecordGraph(true, workers.Count, transportTargets.Count);
            List<Match<Pawn, TransportTask>> selectedMatches =
                WeightedBipartiteMatcher.MaximumWeightGrouped(
                    workers,
                    transportTargets,
                    patient => tasksByPatient[patient],
                    (worker, task) => TransportTaskEdgeWeight(
                        worker,
                        task,
                        now,
                        previous,
                        existingStandbys));
            selectedMatches = WeightedBipartiteMatcher.DiversifyExclusiveOptions(
                selectedMatches,
                task => task.Target,
                patient => tasksByPatient[patient],
                (worker, task) => TransportTaskEdgeWeight(
                    worker,
                    task,
                    now,
                    previous,
                    existingStandbys),
                task => task.IsSupply,
                task => task.SupplyResource,
                (worker, supply) => !medicalResources.IsClaimedByOtherWorker(supply, worker));
            Dictionary<Pawn, Match<Pawn, TransportTask>> matchByWorker = selectedMatches
                .ToDictionary(match => match.Worker, match => match);
            foreach (KeyValuePair<Pawn, ActiveStandby> pair in existingStandbys)
            {
                bool continueWaiting = matchByWorker.TryGetValue(pair.Key, out Match<Pawn, TransportTask> match) &&
                                       match.Target.WaitForTreatment &&
                                       match.Target.Target == pair.Value.Target;
                if (continueWaiting)
                {
                    usedWorkers.Add(pair.Key);
                }
                else
                {
                    StopStandby(pair.Value.Target);
                }
            }

            foreach (Match<Pawn, TransportTask> match in selectedMatches)
            {
                if (existingStandbys.TryGetValue(match.Worker, out ActiveStandby existing) &&
                    match.Target.WaitForTreatment && match.Target.Target == existing.Target)
                {
                    // The existing JobDriver can keep waiting; do not restart its path/job.
                    continue;
                }

                if (!usedWorkers.Add(match.Worker))
                {
                    continue;
                }

                TransportTask task = match.Target;
                if (task.IsSupply && !medicalResources.TryClaim(
                        task.SupplyResource,
                        match.Worker,
                        task.Target,
                        task.SupplyCount,
                        task.SupplyResource.def.stackLimit == 1,
                        now + SoftClaimLeaseTicks,
                        MedicalResourceAccess.Relocation))
                {
                    usedWorkers.Remove(match.Worker);
                    continue;
                }
                int createdAt = previous.TryGetValue(match.Worker, out PendingAssignment old) &&
                                old.WaitForTreatment == task.WaitForTreatment && old.Target == task.Target
                    ? old.CreatedAt
                    : now;
                pendingByWorker[match.Worker] = new PendingAssignment(
                    task.Target,
                    task.IsSupply ? SearchAndRescueStage.Supply : SearchAndRescueStage.Rescue,
                    match.Weight,
                    createdAt,
                    task.WaitForTreatment,
                    now + SoftClaimLeaseTicks,
                    null,
                    null,
                    task.SupplyResource,
                    task.SupplyCount);
            }
        }

        private double TransportTaskEdgeWeight(
            Pawn worker,
            TransportTask task,
            int now,
            IReadOnlyDictionary<Pawn, PendingAssignment> previous,
            IReadOnlyDictionary<Pawn, ActiveStandby> existingStandbys)
        {
            Pawn patient = task.Target;
            double weight;
            if (task.IsSupply)
            {
                if (!Compatibility.CanPerformSupplyWork(worker) || task.SupplyResource == null ||
                    medicalResources.AvailableForRelocation(task.SupplyResource, worker) < task.SupplyCount ||
                    !PickupReservationAvailable(worker, task.SupplyResource, task.SupplyCount, patient) ||
                    !worker.CanReach(patient, PathEndMode.Touch, Danger.Deadly))
                {
                    return 0d;
                }

                IntVec3 sourcePosition = task.SupplyResource.PositionHeld;
                double route = Math.Sqrt(worker.Position.DistanceToSquared(sourcePosition)) +
                               Math.Sqrt(sourcePosition.DistanceToSquared(patient.Position));
                int priority = Compatibility.SupplyWorkPriority(worker);
                bool workerCanTreat = Compatibility.CanPerformTreatmentWork(worker);
                if (workerCanTreat && !NeedsFieldStabilization(patient))
                {
                    return 0d;
                }

                double doctorOpportunityCost = workerCanTreat
                    ? DoctorEmergencySupplyOpportunityCost +
                      worker.GetStatValue(StatDefOf.MedicalTendQuality) * 30000d +
                      (5 - Compatibility.TreatmentWorkPriority(worker)) * 7000d
                    : 0d;
                double netUtility = PatientUrgency(patient) * task.SupplyBenefit * 65000d +
                                    (5 - priority) * 4000d - route * SupplyRouteCost -
                                    medicalResources.ScarcityPrice(
                                        task.SupplyResource,
                                        task.SupplyResource.def.stackLimit == 1) -
                                    doctorOpportunityCost;
                if (netUtility <= 0d)
                {
                    return 0d;
                }
                weight = AssignmentBaseWeight + netUtility;
            }
            else if (task.WaitForTreatment)
            {
                if (!worker.CanReach(patient, PathEndMode.Touch, Danger.Deadly) ||
                    !DestinationAllowedForAnimal(worker, patient.Position) ||
                    !ShouldMatchStandby(worker, patient, now, existingStandbys))
                {
                    return 0d;
                }

                double distance = Math.Sqrt(worker.Position.DistanceToSquared(patient.Position));
                int expectedWait = existingStandbys.TryGetValue(worker, out ActiveStandby committed) &&
                                   committed.Target == patient
                    ? Math.Max(0, committed.ExpectedTreatmentEndTick - now)
                    : ExpectedRemainingTreatmentTicks(patient);
                weight = AssignmentBaseWeight + PatientUrgency(patient) * 20000d -
                         expectedWait * 200d - distance * 300d +
                         RescueMedicalPriorityWeight(patient);
                if (preferredRescuerByTarget.TryGetValue(patient, out Pawn preferred) && preferred == worker)
                {
                    weight += ResumeTransportWeight;
                }
            }
            else
            {
                weight = EdgeWeight(worker, patient, SearchAndRescueStage.Rescue);
            }

            if (weight <= 0d)
            {
                return 0d;
            }

            if (previous.TryGetValue(worker, out PendingAssignment old) &&
                old.WaitForTreatment == task.WaitForTreatment && old.Target == patient &&
                (task.IsSupply ? old.Stage == SearchAndRescueStage.Supply :
                    old.Stage == SearchAndRescueStage.Rescue))
            {
                weight += PendingContinuityBaseWeight;
            }
            if (existingStandbys.TryGetValue(worker, out ActiveStandby existing) &&
                task.WaitForTreatment && existing.Target == patient)
            {
                weight += PendingContinuityBaseWeight + PendingFreshPairWeight;
            }

            return weight;
        }

        private IEnumerable<TransportTask> BuildSupplyTasks(int now)
        {
            foreach (MedicalCarePlan plan in carePlans.Values
                         .Where(plan => plan.Patient != null && plan.Patient.Spawned && !plan.Patient.Dead)
                         .OrderByDescending(plan => PatientUrgency(plan.Patient)))
            {
                Pawn patient = plan.Patient;
                if (IsRetryBlocked(patient, SearchAndRescueStage.Supply, now))
                {
                    continue;
                }
                activeByTarget.TryGetValue(patient, out ActiveAssignment active);
                bool activeDryTreatment = active?.Stage == SearchAndRescueStage.Treat &&
                                          active.Job?.targetB.Thing == null;
                bool treatmentNeedReady = activeDryTreatment ||
                                          TargetReadyForStage(patient, SearchAndRescueStage.Treat, now);
                bool activeResourceRun = active != null &&
                                         (active.Stage == SearchAndRescueStage.Restock ||
                                          active.Stage == SearchAndRescueStage.Treat &&
                                          active.Job?.targetB.Thing != null);
                bool pendingResourceRun = pendingByWorker.Values.Any(pending => pending.Target == patient &&
                    (pending.Stage == SearchAndRescueStage.Restock || pending.Stage == SearchAndRescueStage.Supply ||
                     pending.Stage == SearchAndRescueStage.Treat && pending.Treatment?.Resource != null));
                if (!treatmentNeedReady ||
                    activeLogisticsByWorker.Values.Any(logistics => logistics.Target == patient) ||
                    activeResourceRun || pendingResourceRun)
                {
                    continue;
                }

                List<TransportTask> alternatives = new List<TransportTask>();
                foreach (MedicalResourceDemand candidate in plan.Demands
                             .Where(candidate => candidate.ResourceDef != null)
                             .OrderByDescending(candidate => candidate.Essential)
                             .ThenByDescending(candidate => candidate.Benefit))
                {
                    int required = candidate.Reusable ? 1 : Math.Max(1, candidate.Count);
                    int alreadyNear = ResourceCountNearPatient(patient, candidate.ResourceDef, required);
                    if (alreadyNear >= required)
                    {
                        continue;
                    }
                    int remainingDemand = Math.Max(1, required - alreadyNear);
                    alternatives.AddRange(SupplyAlternatives(candidate.ResourceDef, patient)
                        .Take(6)
                        .Select(resource => new TransportTask(
                            patient,
                            resource,
                            candidate.Reusable
                                ? 1
                                : Math.Min(
                                    medicalResources.AvailableForRelocation(resource),
                                    remainingDemand),
                            candidate.Benefit))
                        .Where(task => task.SupplyCount > 0));
                }
                if (plan.EssentialMedicineRounds > 0)
                {
                    int medicineAlreadyNear = MedicineCountNearPatient(
                        patient,
                        plan.EssentialMedicineRounds);
                    if (medicineAlreadyNear < plan.EssentialMedicineRounds)
                    {
                        int remainingDemand = Math.Max(
                            1,
                            plan.EssentialMedicineRounds - medicineAlreadyNear);
                        alternatives.AddRange(SupplyMedicineAlternatives(patient)
                            .Take(6)
                            .Select(resource => new TransportTask(
                                patient,
                                resource,
                                Math.Min(
                                    medicalResources.AvailableForRelocation(resource),
                                    remainingDemand),
                                1d))
                            .Where(task => task.SupplyCount > 0));
                    }
                }
                if (alternatives.Count == 0)
                {
                    continue;
                }

                // Different interventions can name the same physical stack (ordinary
                // medicine is the common overlap). Keep its highest-value interpretation,
                // while retaining several different resources so a contended pickup can be
                // replaced by useful work for the same casualty.
                foreach (TransportTask alternative in alternatives
                             .GroupBy(task => task.SupplyResource)
                             .Select(group => group.OrderByDescending(task => task.SupplyBenefit).First())
                             .OrderByDescending(task => task.SupplyBenefit)
                             .ThenBy(task => task.SupplyResource.thingIDNumber)
                             .Take(12))
                {
                    yield return alternative;
                }
            }
        }

        private void RebalanceNearbyFieldSupplyReferences()
        {
            List<MedicalCarePlan> orderedPlans = carePlans.Values
                .Where(plan => plan.Patient != null && plan.Patient.Spawned && !plan.Patient.Dead)
                .OrderByDescending(plan => PatientUrgency(plan.Patient))
                .ThenBy(plan => plan.Patient.thingIDNumber)
                .ToList();
            HashSet<Thing> previouslyProtected = medicalResources.ProtectedFieldSupplies().ToHashSet();
            foreach (MedicalCarePlan plan in orderedPlans)
            {
                medicalResources.ReleasePatientFieldSupplyReferences(plan.Patient);
            }

            foreach (MedicalCarePlan plan in orderedPlans)
            {
                foreach (IGrouping<ThingDef, MedicalResourceDemand> demandGroup in plan.Demands
                             .Where(demand => demand.ResourceDef != null)
                             .GroupBy(demand => demand.ResourceDef))
                {
                    int required = demandGroup.Sum(demand => demand.Reusable ? 1 : Math.Max(1, demand.Count));
                    ReconcileResourceCountNearPatient(plan.Patient, demandGroup.Key, required);
                }
                if (plan.EssentialMedicineRounds > 0)
                {
                    ReconcileMedicineCountNearPatient(plan.Patient, plan.EssentialMedicineRounds);
                }
            }

            foreach (Thing supply in medicalResources.ProtectedFieldSupplies()
                         .Where(supply => !previouslyProtected.Contains(supply))
                         .ToList())
            {
                CancelAutomaticHaulsTargeting(supply);
            }
        }

        private int ResourceCountNearPatient(Pawn patient, ThingDef def, int required)
        {
            return medicalResources.ReferencedCountForPatient(patient, thing => thing.def == def);
        }

        private int ReconcileResourceCountNearPatient(Pawn patient, ThingDef def, int required)
        {
            List<Thing> nearby = map.listerThings.ThingsOfDef(def)
                .Where(thing => thing.Spawned && !thing.Destroyed &&
                                !thing.IsForbidden(Faction.OfPlayer) &&
                                thing.Position.DistanceToSquared(patient.Position) <= SupplyNearbyRadiusSquared)
                .OrderByDescending(thing => medicalResources.IsFieldSupplyFor(thing, patient))
                .ThenBy(thing => thing.Position.DistanceToSquared(patient.Position))
                .ToList();
            int referenced = medicalResources.ReconcileNearbyFieldSupplyReferences(
                patient,
                nearby,
                required,
                thing => thing.def == def);
            return referenced;
        }

        private int MedicineCountNearPatient(Pawn patient, int required)
        {
            return medicalResources.ReferencedCountForPatient(patient, thing =>
                thing.def.IsMedicine && Compatibility.AllowsMedicine(patient, thing));
        }

        private int ReconcileMedicineCountNearPatient(Pawn patient, int required)
        {
            if (patient == null || required <= 0)
            {
                return 0;
            }

            List<Thing> nearby = map.listerThings.ThingsInGroup(ThingRequestGroup.Medicine)
                .Where(thing => thing.Spawned && !thing.Destroyed &&
                                !thing.IsForbidden(Faction.OfPlayer) &&
                                thing.Position.DistanceToSquared(patient.Position) <= SupplyNearbyRadiusSquared &&
                                Compatibility.AllowsMedicine(patient, thing))
                .OrderByDescending(thing => medicalResources.IsFieldSupplyFor(thing, patient))
                .ThenByDescending(thing => Compatibility.MedicinePreference(patient, thing))
                .ThenBy(thing => thing.Position.DistanceToSquared(patient.Position))
                .ToList();
            int referenced = medicalResources.ReconcileNearbyFieldSupplyReferences(
                patient,
                nearby,
                required,
                thing => thing.def.IsMedicine);
            return referenced;
        }

        internal void NotifyFieldSupplyForbiddenChanged(Thing supply)
        {
            if (supply == null || supply.MapHeld != map)
            {
                return;
            }

            bool referenceReleased = false;
            if (supply.IsForbidden(Faction.OfPlayer))
            {
                // A player forbid is an explicit opt-out from automated field use. Drop all
                // patient quotas immediately; vanilla's own forbidden state keeps the stack
                // in place without needing SAR haul protection.
                referenceReleased = medicalResources.ReleaseFieldSupplyReferences(supply);
            }

            bool medicallyRelevant = referenceReleased || supply.def.IsMedicine ||
                                     carePlans.Values.Any(plan => plan.Demands.Any(demand =>
                                         demand.ResourceDef == supply.def));
            if (!medicallyRelevant)
            {
                return;
            }

            // Unforbidding is equally significant: a nearby stack may now close a deficit.
            // Coalesce repeated drag-forbid changes, but do not wait for the heartbeat.
            RequestScheduleRebuild(maintenance: true, delayTicks: 1);
        }

        internal void NotifyMedicalSupplyDestroyed(Thing supply)
        {
            if (supply == null)
            {
                return;
            }

            medicalResources.ForgetDestroyedSupply(supply);
            // Destroy can run inside a tend/haul toil or explosion damage stack. Never
            // re-enter the ThinkTree from that call stack; rebuild on the following tick.
            RequestScheduleRebuild(maintenance: true, delayTicks: 1);
        }

        internal void NotifyPatientBedDestroyed(Building_Bed bed)
        {
            if (bed == null)
            {
                return;
            }

            bool activeDestination = activeByTarget.Values.Any(assignment =>
                assignment.Stage == SearchAndRescueStage.Rescue && assignment.Job?.targetB.Thing == bed);
            bool pendingTransport = pendingByWorker.Values.Any(assignment =>
                assignment.Stage == SearchAndRescueStage.Rescue);
            bool markedOccupant = AllMarkedPawns().Any(patient =>
                patient?.CurJob?.targetA.Thing == bed || patient?.CurJob?.targetB.Thing == bed);
            if (!activeDestination && !pendingTransport && !markedOccupant)
            {
                return;
            }

            // Bed destruction can happen inside designator/deconstruction and LayDown cleanup
            // call stacks. Retire the stale route on the next tick; active vanilla drivers get
            // to unwind first and carried pawns remain protected by their normal cleanup.
            RequestScheduleRebuild(maintenance: true, delayTicks: 1);
        }

        private IEnumerable<Thing> SupplyAlternatives(ThingDef def, Pawn patient)
        {
            IEnumerable<Thing> mapSupplies = map.listerThings.ThingsOfDef(def)
                .Where(thing => thing.Spawned && !thing.Destroyed &&
                                medicalResources.AvailableForRelocation(thing) >= 1 &&
                                SupplyReachableByAvailableHauler(thing, patient));
            return mapSupplies
                .Concat(VehicleCargoSupplies(def, patient))
                .Distinct()
                .OrderBy(thing => medicalResources.IsClaimedMedicalSupply(thing) ? 1 : 0)
                .ThenBy(thing => thing.PositionHeld.DistanceToSquared(patient.Position))
                .ThenBy(thing => thing.thingIDNumber);
        }

        private IEnumerable<Thing> SupplyMedicineAlternatives(Pawn patient)
        {
            if (patient == null || !patient.health.HasHediffsNeedingTend())
            {
                return Enumerable.Empty<Thing>();
            }

            IEnumerable<Thing> mapMedicines = map.listerThings.ThingsInGroup(ThingRequestGroup.Medicine)
                .Where(thing => thing.Spawned && !thing.Destroyed &&
                                Compatibility.AllowsMedicine(patient, thing) &&
                                medicalResources.AvailableForRelocation(thing) > 0 &&
                                SupplyReachableByAvailableHauler(thing, patient));
            return mapMedicines
                .Concat(VehicleCargoSupplies(null, patient)
                    .Where(thing => thing.def.IsMedicine && Compatibility.AllowsMedicine(patient, thing)))
                .Distinct()
                .OrderBy(thing => medicalResources.IsClaimedMedicalSupply(thing) ? 1 : 0)
                .ThenByDescending(thing => Compatibility.MedicinePreference(patient, thing))
                .ThenBy(thing => thing.PositionHeld.DistanceToSquared(patient.Position))
                .ThenBy(thing => thing.thingIDNumber);
        }

        private IEnumerable<Thing> VehicleCargoSupplies(ThingDef def, Pawn patient)
        {
            if (!Compatibility.UsesVehiclesFramework || patient == null)
            {
                return Enumerable.Empty<Thing>();
            }

            return map.mapPawns.AllPawnsSpawned
                .Where(vehicle => Compatibility.VehicleCargoSourceAvailable(vehicle))
                .SelectMany(vehicle => vehicle.inventory.innerContainer)
                .Where(thing => thing != null && !thing.Destroyed &&
                                (def == null || thing.def == def) &&
                                medicalResources.AvailableForRelocation(thing) > 0 &&
                                SupplyReachableByAvailableHauler(thing, patient))
                .Distinct();
        }

        private bool SupplyReachableByAvailableHauler(Thing resource, Pawn patient)
        {
            if (resource == null || patient == null)
            {
                return false;
            }

            return WorkerCandidates().Any(worker =>
                WorkerReadyForStage(worker, SearchAndRescueStage.Supply, true) &&
                WorkerAvailableForMatching(worker) &&
                PickupReservationAvailable(worker, resource, 1, patient) &&
                worker.CanReach(patient, PathEndMode.Touch, Danger.Deadly));
        }

        private int ExpectedRemainingTreatmentTicks(Pawn patient)
        {
            int now = Find.TickManager.TicksGame;
            if (!TryGetActiveTreatmentEta(patient, now, out _, out _, out int treatmentTicks))
            {
                return SafeBloodLossHorizonTicks;
            }

            return treatmentTicks;
        }

        private int EstimateTreatmentTicks(Pawn patient, Pawn doctor)
        {
            if (patient == null || doctor == null)
            {
                return SafeBloodLossHorizonTicks;
            }

            if (MechanicalCare.IsPatient(patient))
                return Mathf.CeilToInt(Math.Max(1f, MechanicalCare.Damage(patient)) * 120f /
                    Math.Max(0.05f, doctor.GetStatValue(StatDefOf.MechRepairSpeed))) +
                    ExpectedTravelTicks(doctor, patient);

            float tendSpeed = Math.Max(0.05f, doctor.GetStatValue(StatDefOf.MedicalTendSpeed));
            int baseDuration = doctor.CurJobDef?.defName == "UseBloodBag"
                ? 720
                : doctor.CurJobDef?.defName == "UseSalineBag"
                    ? 320
                    : doctor.CurJobDef?.defName == "Stabilize"
                        ? 60
                        : 600;
            int roundTicks = Mathf.CeilToInt(baseDuration / tendSpeed);
            int bleedingRounds = patient.health.hediffSet.hediffs.Count(hediff =>
                hediff.TendableNow() && hediff.Bleeding && hediff.BleedRate >= MajorUntendedBleedRate);
            int emergencyRounds = patient.health.hediffSet.hediffs.Count(hediff =>
                hediff.def.defName == "ChokingOnBlood" || hediff.def.defName == "CardiacArrest" ||
                hediff.def.defName == "HeartAttack");
            // More Injuries device jobs are dispatched as one scheduler intervention. The
            // next bag/device produces a fresh job and a fresh forecast after notification.
            int remainingRounds = Compatibility.IsMoreInjuriesTreatmentJob(doctor.CurJobDef)
                ? 1
                : doctor.CurJobDef?.defName == "Stabilize"
                    ? Math.Max(1, Compatibility.CombatExtendedStabilizableWoundCount(patient))
                    : Math.Max(1, bleedingRounds + emergencyRounds);
            int treatmentTicks = remainingRounds * roundTicks;

            if (CompatibilityRegistry.PatientFor(doctor, doctor.CurJob, PatientJobRole.Treatment) == patient &&
                doctor.jobs.curDriver != null &&
                doctor.jobs.curDriver.ticksLeftThisToil > 0 &&
                doctor.jobs.curDriver.ticksLeftThisToil <= roundTicks)
            {
                treatmentTicks = doctor.jobs.curDriver.ticksLeftThisToil +
                                 Math.Max(0, remainingRounds - 1) * roundTicks;
            }
            else if (doctor.Spawned && patient.Spawned)
            {
                treatmentTicks += ExpectedTravelTicks(doctor, patient);
            }

            return Math.Max(0, treatmentTicks);
        }

        private bool TryGetActiveTreatmentEta(
            Pawn patient,
            int now,
            out Pawn doctor,
            out Job treatmentJob,
            out int remainingTicks)
        {
            doctor = null;
            treatmentJob = null;
            remainingTicks = 0;
            if (patient == null || patient.Dead || !patient.Spawned || patient.Map != map)
            {
                return false;
            }

            if (activeByTarget.TryGetValue(patient, out ActiveAssignment active) &&
                active.Stage == SearchAndRescueStage.Treat && AssignmentJobStillRunning(active))
            {
                doctor = active.Worker;
                treatmentJob = active.Job;
            }
            else
            {
                doctor = FindCurrentTreatingDoctor(patient);
                treatmentJob = doctor?.CurJob;
            }

            if (!WorkerOperational(doctor) || treatmentJob == null ||
                CompatibilityRegistry.PatientFor(
                    doctor,
                    treatmentJob,
                    PatientJobRole.Treatment) != patient ||
                !Compatibility.IsTreatmentJob(treatmentJob.def))
            {
                doctor = null;
                treatmentJob = null;
                return false;
            }

            remainingTicks = EstimateTreatmentTicks(patient, doctor);
            return true;
        }

        private static int ExpectedTravelTicks(Pawn worker, Pawn patient)
        {
            if (worker == null || patient == null || !worker.Spawned || !patient.Spawned)
            {
                return SafeBloodLossHorizonTicks;
            }

            float moveSpeed = Math.Max(0.1f, worker.GetStatValue(StatDefOf.MoveSpeed));
            float distance = Mathf.Sqrt(worker.Position.DistanceToSquared(patient.Position));
            return Mathf.CeilToInt(distance * 60f / moveSpeed);
        }

        private bool ShouldMatchStandby(
            Pawn worker,
            Pawn patient,
            int now,
            IReadOnlyDictionary<Pawn, ActiveStandby> existingStandbys)
        {
            if (SearchAndRescueMod.Settings?.EnableRescuerStandby == false)
            {
                return false;
            }

            // Waiting is useful only when the carrier can actually evacuate the casualty
            // after treatment. Smart Medicine's temporary tending spot is deliberately
            // rejected by TryFindRescueDestination through FindBestRescueBed.
            if (!TryFindRescueDestination(worker, patient, out _, out _))
            {
                return false;
            }

            if (existingStandbys != null &&
                existingStandbys.TryGetValue(worker, out ActiveStandby existing) &&
                existing.Target == patient)
            {
                // Once travel has begun, do not reapply the entry inequality: carrier ETA
                // continuously shrinks and would otherwise make the same pair oscillate.
                return ActiveStandbyStillValid(existing, now);
            }

            return TryGetActiveTreatmentEta(patient, now, out _, out _, out int treatmentTicks) &&
                   treatmentTicks <= ExpectedTravelTicks(worker, patient) + StandbyLeadTicks;
        }

        private bool ActiveStandbyStillValid(ActiveStandby standby, int now)
        {
            if (SearchAndRescueMod.Settings?.EnableRescuerStandby == false ||
                standby == null || !StandbyTargetValid(standby.Target, now) ||
                !WorkerOperational(standby.Worker) || !WorkerOperational(standby.Doctor))
            {
                return false;
            }

            // A new treatment job, even for the same doctor and patient, is a new forecast.
            // Releasing here lets the graph calculate a fresh ETA after an interruption or a
            // modded multi-stage procedure boundary.
            return TryGetActiveTreatmentEta(
                       standby.Target,
                       now,
                       out Pawn doctor,
                       out Job treatmentJob,
                       out _) &&
                   doctor == standby.Doctor && standby.TreatmentIdentity.Matches(ActiveJobClaims.IdentityOf(treatmentJob));
        }

        private bool TryInterruptRescueForTreatment(
            Pawn patient,
            ActiveAssignment rescue,
            out Pawn releasedRescuer)
        {
            releasedRescuer = null;
            Pawn rescuer = rescue.Worker;
            if (rescuer == null)
            {
                activeClaims.ReleasePrimary(patient);
                ClearStageRetry(patient, SearchAndRescueStage.Rescue);
                return patient != null && patient.Spawned;
            }

            if (!AssignmentJobStillRunning(rescue))
            {
                // The snapshot went stale between matching and job pickup. In particular,
                // never pull a carried patient out of a newer/player job here.
                return false;
            }

            bool carryingPatient = rescuer.carryTracker?.CarriedThing == patient;
            if (patient == null || !patient.Spawned && !carryingPatient)
            {
                // There is no reversible hand-off path: ending the carrier cannot expose a
                // target for the doctor's reservations. Preserve Rescue unchanged.
                return false;
            }

            if (carryingPatient &&
                !rescuer.carryTracker.TryDropCarriedThing(
                    rescuer.Position,
                    ThingPlaceMode.Near,
                    out _))
            {
                // Keep the original job and carry state intact when the map has no legal
                // placement. Ending Rescue first would strand an unspawned patient and make
                // the newly constructed treatment job fail its reservations as well.
                return false;
            }

            preferredRescuerByTarget[patient] = rescuer;
            activeClaims.ReleasePrimary(patient);
            ClearStageRetry(patient, SearchAndRescueStage.Rescue);
            rescuer.jobs.EndCurrentJob(JobCondition.InterruptForced);

            if (patient == null || !patient.Spawned)
            {
                return false;
            }

            if (rescuer.Map == map && !rescuer.Dead && !rescuer.Downed)
            {
                releasedRescuer = rescuer;
            }
            return true;
        }

        private bool StandbyTargetValid(Pawn patient, int now)
        {
            return patient != null && patient.Spawned && patient.Map == map && !patient.Dead && patient.Downed &&
                   TreatmentAdmitted(patient, SearchAndRescueStage.Treat) &&
                   HasDesignation(patient, SearchAndRescueDefOf.SAR_Rescue) &&
                   !IsInSafePatientBed(patient) &&
                   !IsRetryBlocked(patient, SearchAndRescueStage.Rescue, now);
        }

        private bool PendingAssignmentValid(Pawn worker, PendingAssignment pending, int now)
        {
            if (pending.ExpiresAt > 0 && now >= pending.ExpiresAt)
            {
                return false;
            }

            if (IsRetryBlocked(pending.Target, pending.Stage, now))
            {
                return false;
            }

            if (pending.WaitForTreatment)
            {
                return worker != null && worker.Map == map &&
                       WorkerReadyForStage(worker, SearchAndRescueStage.Rescue) &&
                       StandbyTargetValid(pending.Target, now) &&
                       ShouldMatchStandby(
                           worker,
                           pending.Target,
                           now,
                           null);
            }

            SearchAndRescueStage readinessStage = pending.Stage == SearchAndRescueStage.Restock ||
                                                pending.Stage == SearchAndRescueStage.Supply
                ? SearchAndRescueStage.Treat
                : pending.Stage;
            bool resourceValid = pending.SupplyResource == null ||
                                 medicalResources.AvailableForRelocation(pending.SupplyResource, worker) >=
                                 Math.Max(1, pending.SupplyCount) &&
                                 PickupReservationAvailable(
                                     worker,
                                     pending.SupplyResource,
                                     Math.Max(1, pending.SupplyCount),
                                     pending.Target);
            if (resourceValid && pending.Treatment?.Resource != null)
            {
                resourceValid = medicalResources.AvailableForTreatment(
                                    pending.Treatment.Resource,
                                    worker,
                                    pending.Target) >=
                                Math.Max(1, pending.Treatment.Count) &&
                                PickupReservationAvailable(
                                    worker,
                                    pending.Treatment.Resource,
                                    Math.Max(1, pending.Treatment.Count),
                                    pending.Target);
            }
            if (resourceValid && pending.Kit != null)
            {
                resourceValid = pending.Kit.Items.All(item =>
                    medicalResources.AvailableForRelocation(item.Thing, worker) >= item.Count &&
                    PickupReservationAvailable(worker, item.Thing, item.Count, pending.Target));
            }
            bool concurrentDrySupply = pending.Stage == SearchAndRescueStage.Supply &&
                                       activeByTarget.TryGetValue(pending.Target, out ActiveAssignment active) &&
                                       active.Stage == SearchAndRescueStage.Treat &&
                                       active.Job?.targetB.Thing == null;
            bool pendingRescueInterception = pending.Stage == SearchAndRescueStage.Treat &&
                                             activeByTarget.TryGetValue(
                                                 pending.Target,
                                                 out ActiveAssignment rescue) &&
                                             rescue.Stage == SearchAndRescueStage.Rescue;
            bool targetReady = concurrentDrySupply
                ? pending.Target != null && pending.Target.Spawned && !pending.Target.Dead &&
                  TreatmentAdmitted(pending.Target, SearchAndRescueStage.Supply) &&
                  Compatibility.NeedsAnyFieldTreatment(pending.Target) &&
                  NeedsFieldStabilization(pending.Target)
                : (pendingRescueInterception || !activeByTarget.ContainsKey(pending.Target)) &&
                  TargetReadyForStage(pending.Target, readinessStage, now);
            bool targetReservationValid = pending.WaitForTreatment ||
                                          pending.Stage == SearchAndRescueStage.Restock ||
                                          pending.Stage == SearchAndRescueStage.Supply ||
                                          pendingRescueInterception ||
                                          worker != null && worker.CanReserve(pending.Target, 1, -1);
            bool workerReady = WorkerReadyForTargetStage(worker, pending.Target, pending.Stage) &&
                               (pending.Stage != SearchAndRescueStage.FollowupTreat ||
                                WorkerReadyForFollowupLane(worker, pending.Target));
            return worker != null && worker.Map == map && targetReady && targetReservationValid &&
                   workerReady && resourceValid;
        }

        private string DebugPendingInvalidReason(Pawn worker, PendingAssignment pending, int now)
        {
            if (pending == null)
            {
                return "none";
            }
            if (pending.ExpiresAt > 0 && now >= pending.ExpiresAt)
            {
                return "expired";
            }
            if (IsRetryBlocked(pending.Target, pending.Stage, now))
            {
                return "retry-blocked";
            }

            List<string> reasons = new List<string>();
            if (worker == null || worker.Map != map)
            {
                reasons.Add("worker-map");
            }
            if (!WorkerReadyForTargetStage(worker, pending.Target, pending.Stage))
            {
                reasons.Add("worker-stage");
            }
            else if (pending.Stage == SearchAndRescueStage.FollowupTreat &&
                     !WorkerReadyForFollowupLane(worker, pending.Target))
            {
                reasons.Add("worker-followup-lane");
            }
            if (pending.Treatment?.Resource != null)
            {
                int count = Math.Max(1, pending.Treatment.Count);
                int available = medicalResources.AvailableForTreatment(
                    pending.Treatment.Resource, worker, pending.Target);
                if (available < count)
                {
                    reasons.Add($"treatment-resource({available}/{count})");
                }
                if (!PickupReservationAvailable(worker, pending.Treatment.Resource, count, pending.Target))
                {
                    reasons.Add("treatment-pickup");
                }
            }
            if (pending.Kit != null)
            {
                foreach (ThingCount item in pending.Kit.Items)
                {
                    int available = medicalResources.AvailableForRelocation(item.Thing, worker);
                    if (available < item.Count)
                    {
                        reasons.Add($"kit-resource({available}/{item.Count})");
                    }
                    if (!PickupReservationAvailable(worker, item.Thing, item.Count, pending.Target))
                    {
                        reasons.Add("kit-pickup");
                    }
                }
            }

            SearchAndRescueStage readinessStage = pending.Stage == SearchAndRescueStage.Restock ||
                                                pending.Stage == SearchAndRescueStage.Supply
                ? SearchAndRescueStage.Treat
                : pending.Stage;
            if (!TargetReadyForStage(pending.Target, readinessStage, now))
            {
                reasons.Add($"target-stage(active={activeByTarget.ContainsKey(pending.Target)}," +
                            $"external={CompatibilityRegistry.HasExternalOwner(pending.Target)})");
            }
            if (pending.Stage != SearchAndRescueStage.Restock &&
                pending.Stage != SearchAndRescueStage.Supply &&
                !(pending.Stage == SearchAndRescueStage.Treat &&
                  activeByTarget.TryGetValue(pending.Target, out ActiveAssignment active) &&
                  active.Stage == SearchAndRescueStage.Rescue) &&
                worker != null && !worker.CanReserve(pending.Target, 1, -1))
            {
                reasons.Add("target-reservation");
            }
            return reasons.Count == 0 ? "unknown" : string.Join(",", reasons);
        }

        private bool PickupReservationAvailable(
            Pawn worker,
            Thing resource,
            int count,
            Pawn patient = null)
        {
            if (worker == null || resource == null || resource.Destroyed ||
                resource.stackCount < Math.Max(1, count))
            {
                return false;
            }

            // Medicine already carried by this doctor needs neither a map reservation nor
            // an inventory-to-inventory transfer. Treating it as an external holder calls
            // CanTakeFromInventoryHolder(worker, worker, ...), which correctly rejects a
            // self-transfer but used to invalidate the freshly matched treatment claim.
            if (worker.inventory?.innerContainer.Contains(resource) == true ||
                worker.carryTracker?.CarriedThing == resource)
            {
                return true;
            }

            Pawn holder = MedicalResourceLedger.InventoryHolder(resource);
            return holder != null
                ? MedicalResourceLedger.CanTakeFromInventoryHolder(
                    worker,
                    holder,
                    resource,
                    count,
                    patient)
                : !resource.IsForbidden(worker) &&
                  medicalResources.CanReserveAndReachForPickupCached(worker, resource, count);
        }

        private bool TargetReadyForStage(Pawn patient, SearchAndRescueStage stage, int now)
        {
            StageRetryKey cacheKey = new StageRetryKey(patient, stage);
            if (schedulingSnapshotActive && schedulingTargetReadiness.TryGetValue(cacheKey, out bool cached))
            {
                return cached;
            }

            bool ready = TargetReadyForStageCore(patient, stage, now);
            if (schedulingSnapshotActive)
            {
                schedulingTargetReadiness[cacheKey] = ready;
            }
            return ready;
        }

        private bool TargetReadyForStageCore(Pawn patient, SearchAndRescueStage stage, int now)
        {
            if (patient?.InMentalState == true)
            {
                // A mental state is temporary. Preserve the player's designation, but do not
                // chase or operate on a pawn that native and third-party drivers may consider
                // hostile or otherwise uncontrolled. The periodic graph pass resumes it.
                return false;
            }

            ActiveAssignment active = null;
            bool interceptingRescue = patient != null &&
                                      activeByTarget.TryGetValue(patient, out active) &&
                                       active.Stage == SearchAndRescueStage.Rescue &&
                                       stage == SearchAndRescueStage.Treat;
            bool logisticsInProgress = patient != null && activeLogisticsByWorker.Values.Any(logistics =>
                logistics.Target == patient);
            if (patient == null || patient.Dead ||
                (patient.Spawned && patient.Map != map) ||
                (!patient.Spawned && !interceptingRescue) ||
                (active != null && !interceptingRescue) ||
                logisticsInProgress && (stage == SearchAndRescueStage.Rescue || stage == SearchAndRescueStage.Supply) ||
                IsRetryBlocked(patient, stage, now))
            {
                return false;
            }

            if (HasExternalOwnerForScheduling(patient))
            {
                // Active third-party jobs own the patient until their job boundary. This
                // covers vanilla surgery, allied/enemy rescue, BCD CASEVAC/First Aid,
                // Move the Patient and future registered providers without pairwise patches.
                return false;
            }

            switch (stage)
            {
                case SearchAndRescueStage.Capture:
                    return TargetEligibility.CanBeCaptured(patient) &&
                           HasDesignation(patient, SearchAndRescueDefOf.SAR_Capture) &&
                           patient.Downed && !patient.IsPrisonerOfColony && patient.HostileTo(Faction.OfPlayer);

                case SearchAndRescueStage.Treat:
                case SearchAndRescueStage.Restock:
                    return (interceptingRescue
                               ? TargetEligibility.CanReceiveFieldCareAfterDrop(patient)
                               : TargetEligibility.CanReceiveFieldCare(patient)) &&
                           TreatmentAdmitted(patient, stage) &&
                           Compatibility.NeedsAnyFieldTreatment(patient) &&
                           NeedsFieldStabilization(patient) &&
                           (!HasDesignation(patient, SearchAndRescueDefOf.SAR_Capture) || patient.IsPrisonerOfColony) &&
                           (!patient.HostileTo(Faction.OfPlayer) || patient.IsPrisonerOfColony);

                case SearchAndRescueStage.FollowupTreat:
                    return TargetEligibility.CanReceiveFieldCare(patient) &&
                           TreatmentAdmitted(patient, stage) &&
                           Compatibility.NeedsAnyFieldTreatment(patient) &&
                           !NeedsFieldStabilization(patient) &&
                           (!HasDesignation(patient, SearchAndRescueDefOf.SAR_Capture) || patient.IsPrisonerOfColony) &&
                           (!patient.HostileTo(Faction.OfPlayer) || patient.IsPrisonerOfColony);

                case SearchAndRescueStage.Rescue:
                    return TargetEligibility.CanReceiveFieldCare(patient) &&
                           TryGetCareAdmission(patient, out CareAdmission rescueAdmission) &&
                           rescueAdmission.HasRescue &&
                           patient.Downed &&
                           !HasDesignation(patient, SearchAndRescueDefOf.SAR_Capture) &&
                           (!patient.HostileTo(Faction.OfPlayer) || patient.IsPrisonerOfColony) &&
                           !IsInSafePatientBed(patient);

                case SearchAndRescueStage.Supply:
                    return TargetReadyForStage(patient, SearchAndRescueStage.Treat, now);

                default:
                    return false;
            }
        }

        private bool ActiveTargetControlValid(
            Pawn patient,
            SearchAndRescueStage stage,
            ActiveAssignment assignment)
        {
            if (assignment?.JobDef == JobDefOf.RepairMech &&
                !MechanicalCare.CanRepair(assignment.Worker, patient)) return false;
            if (patient == null || patient.Destroyed || patient.Dead || patient.InMentalState)
            {
                return false;
            }

            bool carriedByThisRescuer = stage == SearchAndRescueStage.Rescue && assignment != null &&
                                        assignment.Worker?.carryTracker?.CarriedThing == patient;
            bool canReceiveCare = carriedByThisRescuer
                ? TargetEligibility.CanReceiveFieldCareAfterDrop(patient)
                : TargetEligibility.CanReceiveFieldCare(patient);

            switch (stage)
            {
                case SearchAndRescueStage.Capture:
                    return TargetEligibility.CanBeCaptured(patient) &&
                           HasDesignation(patient, SearchAndRescueDefOf.SAR_Capture) &&
                           patient.Downed && !patient.IsPrisonerOfColony &&
                           patient.HostileTo(Faction.OfPlayer);

                case SearchAndRescueStage.Treat:
                case SearchAndRescueStage.FollowupTreat:
                case SearchAndRescueStage.Restock:
                case SearchAndRescueStage.Supply:
                    bool manuallyAuthorized = assignment != null &&
                                              (assignment.Origin & CareOrigin.ManualTreatment) != 0 &&
                                              HasDesignation(patient, SearchAndRescueDefOf.SAR_Treat);
                    bool automaticallyAuthorized = assignment != null &&
                                                   AutomaticOriginAllowedByMode(
                                                       patient,
                                                       assignment.Origin);
                    return canReceiveCare && (manuallyAuthorized || automaticallyAuthorized) &&
                           (!HasDesignation(patient, SearchAndRescueDefOf.SAR_Capture) ||
                            patient.IsPrisonerOfColony) &&
                           (!patient.HostileTo(Faction.OfPlayer) || patient.IsPrisonerOfColony);

                case SearchAndRescueStage.Rescue:
                    bool manualRescue = assignment != null &&
                                        (assignment.Origin & CareOrigin.ManualRescue) != 0 &&
                                        HasDesignation(patient, SearchAndRescueDefOf.SAR_Rescue);
                    bool automaticRescue = assignment != null &&
                                           (assignment.Origin & CareOrigin.AutomaticRescue) != 0 &&
                                           CoordinationMode != MedicalCoordinationMode.MarkedOnly &&
                                           AutomaticCareRelationshipEligible(
                                               patient,
                                               carriedByThisRescuer);
                    return canReceiveCare && (manualRescue || automaticRescue) &&
                           patient.Downed && !HasDesignation(patient, SearchAndRescueDefOf.SAR_Capture) &&
                           (!patient.HostileTo(Faction.OfPlayer) || patient.IsPrisonerOfColony);

                default:
                    return false;
            }
        }

        private bool ActiveAssignmentAuthorized(Pawn patient, ActiveAssignment assignment)
        {
            if (patient == null || assignment == null || !IsFieldResponder(assignment.Worker))
            {
                return false;
            }

            switch (assignment.Stage)
            {
                case SearchAndRescueStage.Capture:
                    return HasDesignation(patient, SearchAndRescueDefOf.SAR_Capture);
                case SearchAndRescueStage.Rescue:
                    bool manualRescue = (assignment.Origin & CareOrigin.ManualRescue) != 0 &&
                                        HasDesignation(patient, SearchAndRescueDefOf.SAR_Rescue);
                    bool carriedByThisRescuer = IsCarriedByActiveRescuer(patient, assignment);
                    bool automaticRescue = (assignment.Origin & CareOrigin.AutomaticRescue) != 0 &&
                                           CoordinationMode != MedicalCoordinationMode.MarkedOnly &&
                                           AutomaticCareRelationshipEligible(
                                               patient,
                                               carriedByThisRescuer);
                    return manualRescue || automaticRescue;
                case SearchAndRescueStage.Treat:
                case SearchAndRescueStage.FollowupTreat:
                case SearchAndRescueStage.Restock:
                case SearchAndRescueStage.Supply:
                    bool manual = (assignment.Origin & CareOrigin.ManualTreatment) != 0 &&
                                  HasDesignation(patient, SearchAndRescueDefOf.SAR_Treat);
                    bool automatic = AutomaticOriginAllowedByMode(patient, assignment.Origin);
                    return manual || automatic;
                default:
                    return false;
            }
        }

        private bool WorkerReadyForStage(
            Pawn worker,
            SearchAndRescueStage stage,
            bool allowActiveStandby = false)
        {
            // Active-standby eligibility is intentionally evaluated live: the transport graph
            // may stop a standby while it is being rebuilt. Ordinary readiness is stable for
            // the duration of a graph build and otherwise repeats Work Tab reflection for
            // every worker-target edge.
            if (!allowActiveStandby && schedulingSnapshotActive)
            {
                StageRetryKey cacheKey = new StageRetryKey(worker, stage);
                if (schedulingWorkerReadiness.TryGetValue(cacheKey, out bool cached))
                {
                    return cached;
                }

                bool ready = WorkerReadyForStageCore(worker, stage, false);
                schedulingWorkerReadiness[cacheKey] = ready;
                return ready;
            }

            return WorkerReadyForStageCore(worker, stage, allowActiveStandby);
        }

        private bool WorkerReadyForStageCore(
            Pawn worker,
            SearchAndRescueStage stage,
            bool allowActiveStandby)
        {
            // Preserve short-circuiting of live provider queries for ineligible workers.
            bool operational = WorkerOperational(worker);
            bool responder = operational && IsFieldResponder(worker);
            WorkerReadiness readiness = WorkerReadinessRules.Evaluate(
                operational, responder, worker?.CurJob?.playerForced == true,
                activeClaims.HasPrimaryWorker(worker),
                worker != null && activeLogisticsByWorker.ContainsKey(worker),
                activeClaims.HasStandbyWorker(worker), allowActiveStandby,
                responder && WorkerEligibility.IsProvidingBedsideCare(worker));
            if (readiness != WorkerReadiness.Ready) return false;

            return WorkerEligibility.CanPerformStage(worker, stage);
        }

        private bool WorkerReadyForTargetStage(
            Pawn worker,
            Pawn patient,
            SearchAndRescueStage stage)
        {
            return WorkerReadyForStage(worker, stage) ||
                   stage == SearchAndRescueStage.Capture &&
                   CaptureIsTreatmentPrerequisite(worker, patient);
        }

        private bool CaptureIsTreatmentPrerequisite(Pawn worker, Pawn patient)
        {
            return WorkerReadyForStage(worker, SearchAndRescueStage.Treat) &&
                   Compatibility.CanPerformTreatmentWork(worker) &&
                   patient?.MapHeld == map && patient.Downed && !patient.IsPrisonerOfColony &&
                   patient.HostileTo(Faction.OfPlayer) &&
                   HasDesignation(patient, SearchAndRescueDefOf.SAR_Capture) &&
                   HasDesignation(patient, SearchAndRescueDefOf.SAR_Treat);
        }

        private bool CaptureTreatmentCanContinue(Pawn worker, Pawn patient)
        {
            return WorkerOperational(worker) && IsFieldResponder(worker) &&
                   Compatibility.CanPerformTreatmentWork(worker) &&
                   patient?.MapHeld == map && patient.IsPrisonerOfColony &&
                   HasDesignation(patient, SearchAndRescueDefOf.SAR_Treat) &&
                   Compatibility.NeedsAnyFieldTreatment(patient);
        }

        private bool WorkerReadyForAnyStage(Pawn worker)
        {
            return WorkerReadyForStage(worker, SearchAndRescueStage.Capture) ||
                   WorkerReadyForStage(worker, SearchAndRescueStage.Treat) ||
                   WorkerReadyForStage(worker, SearchAndRescueStage.FollowupTreat) ||
                   WorkerReadyForStage(worker, SearchAndRescueStage.Supply) ||
                   WorkerReadyForStage(worker, SearchAndRescueStage.Rescue);
        }

        private void BeginSchedulingSnapshot()
        {
            schedulingWorkerReadiness.Clear();
            schedulingTargetReadiness.Clear();
            schedulingExternalOwnership.Clear();
            schedulingRescueDestinations.Clear();
            medicalResources.BeginSchedulingSnapshot();
            schedulingSnapshotActive = true;
        }

        private void EndSchedulingSnapshot()
        {
            schedulingSnapshotActive = false;
            schedulingWorkerReadiness.Clear();
            schedulingTargetReadiness.Clear();
            schedulingExternalOwnership.Clear();
            schedulingRescueDestinations.Clear();
            medicalResources.EndSchedulingSnapshot();
        }

        private bool HasExternalOwnerForScheduling(Pawn patient)
        {
            if (!schedulingSnapshotActive)
            {
                return CompatibilityRegistry.HasExternalOwner(patient);
            }

            if (!schedulingExternalOwnership.TryGetValue(patient, out bool owned))
            {
                owned = CompatibilityRegistry.HasExternalOwner(patient);
                schedulingExternalOwnership[patient] = owned;
            }
            return owned;
        }

        private static bool WorkerAvailableForMatching(Pawn worker)
        {
            return worker.CurJob == null || worker.mindState.IsIdle || IsSoftStageTransitionJob(worker.CurJob);
        }

        internal string DebugDescribeScheduler()
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            EnsureFieldResponderWorkTypeMigrated();
            StringBuilder report = new StringBuilder();
            report.AppendLine($"[Search and Rescue] Scheduler snapshot map={map.uniqueID} tick={now} " +
                              $"mode={CoordinationMode} " +
                              $"pending={pendingByWorker.Count} active={activeByTarget.Count} " +
                              $"logistics={activeLogisticsByWorker.Count} standby={standbyByTarget.Count} " +
                              $"detourBacklog={treatmentDetourBacklogPressure:0.0}");
            report.AppendLine(" responders=" + string.Join(",", WorkerCandidates()
                .OrderBy(pawn => pawn.thingIDNumber)
                .Select(pawn => pawn.LabelShortCap + "/" + pawn.ThingID)));

            List<Pawn> doctors = WorkerCandidates()
                .Where(worker => worker != null && Compatibility.CanPerformAnyTreatmentWork(worker))
                .OrderBy(worker => worker.thingIDNumber)
                .ToList();
            List<Pawn> targets = AllCareCandidates()
                .OrderBy(patient => patient.thingIDNumber)
                .ToList();
            Pawn diagnosticDoctor = doctors.FirstOrDefault(worker => WorkerOperational(worker));
            ThingDef bloodDevice = Compatibility.MoreInjuriesBloodBag;
            if (bloodDevice != null)
            {
                int bloodStacks = map.listerThings.ThingsOfDef(bloodDevice)
                    .Count(thing => thing != null && thing.Spawned);
                int bloodUnits = map.listerThings.ThingsOfDef(bloodDevice)
                    .Where(thing => thing != null && thing.Spawned)
                    .Sum(thing => thing.stackCount);
                report.AppendLine($" resources bloodDef={bloodDevice.defName} stacks={bloodStacks} units={bloodUnits} " +
                                  $"diagnosticDoctor={diagnosticDoctor?.LabelShortCap ?? "none"}");
            }
            Dictionary<Pawn, PendingAssignment> validPrevious = pendingByWorker
                .Where(pair => PendingAssignmentValid(pair.Key, pair.Value, now))
                .ToDictionary(pair => pair.Key, pair => pair.Value);

            foreach (Pawn doctor in doctors)
            {
                pendingByWorker.TryGetValue(doctor, out PendingAssignment pending);
                ActiveAssignment active = activeByTarget.Values.FirstOrDefault(value => value.Worker == doctor);
                Pawn currentJobPatient = CompatibilityRegistry.PatientFor(
                    doctor,
                    doctor.CurJob,
                    PatientJobRole.Treatment);
                report.AppendLine(
                    $" worker={doctor.LabelShortCap}/{doctor.ThingID} job={doctor.CurJobDef?.defName ?? "none"} " +
                    $"jobPatient={currentJobPatient?.ThingID ?? "none"} forced={doctor.CurJob?.playerForced == true} " +
                    $"idle={doctor.mindState?.IsIdle == true} operational={WorkerOperational(doctor)} " +
                    $"available={WorkerAvailableForMatching(doctor)} treatReady={WorkerReadyForStage(doctor, SearchAndRescueStage.Treat)} " +
                    $"priority={Compatibility.TreatmentWorkPriority(doctor)} " +
                    $"pending={(pending == null ? "none" : pending.Stage + ":" + pending.Target?.ThingID + ":valid=" + PendingAssignmentValid(doctor, pending, now))} " +
                    $"active={(active == null ? "none" : active.Stage + ":" + active.Target?.ThingID)} " +
                    $"decision={(lastSchedulerDecision.TryGetValue(doctor, out string decision) ? decision : "none")}");
            }

            foreach (Pawn patient in targets)
            {
                TryGetCareAdmission(patient, out CareAdmission admission);
                if (careAffinityClaims.TryGetValue(patient, out SoftCareClaim affinity))
                    report.AppendLine($" continuity patient={patient.ThingID} worker={affinity.Worker?.ThingID}" +
                        $" completed={affinity.CompletedTreatment} weight={affinity.WeightAt(now):0}" +
                        $" remainingTicks={affinity.ExpiresAt - now}");
                bool needs = Compatibility.NeedsAnyFieldTreatment(patient);
                bool stabilize = NeedsFieldStabilization(patient);
                int deathTicks = HealthUtility.TicksUntilDeathDueToBloodLoss(patient);
                float bloodLoss = patient.health?.hediffSet
                    ?.GetFirstHediffOfDef(HediffDefOf.BloodLoss)?.Severity ?? 0f;
                int bloodStabilizeCount = Compatibility.MoreInjuriesRequiredTransfusions(
                    patient,
                    MedicalIntervention.Blood);
                int bloodFullCount = Compatibility.MoreInjuriesRequiredTransfusions(
                    patient,
                    MedicalIntervention.Blood,
                    fullyHeal: true);
                MedicalCarePlan diagnosticPlan = MedicalCarePlan.Build(patient, now);
                report.AppendLine(
                    $" target={patient.LabelShortCap}/{patient.ThingID} origin={admission.Origin} " +
                    $"spawned={patient.Spawned} downed={patient.Downed} " +
                    $"bleed={patient.health?.hediffSet?.BleedRateTotal ?? 0f:0.####} bloodLoss={bloodLoss:0.###} " +
                    $"bloodNeed={bloodStabilizeCount}/{bloodFullCount} deathTicks={deathTicks} " +
                    $"needs={needs} stabilize={stabilize} urgentSurgery={Compatibility.RequiresUrgentSurgery(patient)} " +
                    $"externalOwner={CompatibilityRegistry.HasExternalOwner(patient)} " +
                    $"treatReady={TargetReadyForStage(patient, SearchAndRescueStage.Treat, now)} " +
                    $"followupReady={TargetReadyForStage(patient, SearchAndRescueStage.FollowupTreat, now)} " +
                    $"rescueReady={TargetReadyForStage(patient, SearchAndRescueStage.Rescue, now)}");
                report.AppendLine("  demands=" + (diagnosticPlan.Demands.Count == 0
                    ? "none"
                    : string.Join(",", diagnosticPlan.Demands.Select(demand =>
                        $"{demand.Intervention}:{demand.ResourceDef?.defName ?? "none"}x{demand.Count}@{demand.Benefit:0.##}"))));
                if (diagnosticDoctor != null)
                {
                    IReadOnlyList<MedicalTreatmentOption> diagnosticOptions = Compatibility
                        .FindTreatmentOptions(diagnosticDoctor, patient, diagnosticPlan, medicalResources);
                    report.AppendLine("  options=" + (diagnosticOptions.Count == 0
                        ? "none"
                        : string.Join(",", diagnosticOptions.Select(option =>
                            $"{option.Intervention}:{option.Resource?.ThingID ?? "none"}x{option.Count}" +
                            $"/route={option.RouteDistance:0.0}/weight={TreatmentEdgeWeight(diagnosticDoctor, patient, option):0}"))));
                }

                foreach (Pawn doctor in doctors.Where(worker =>
                             WorkerOperational(worker) && WorkerAvailableForMatching(worker)))
                {
                    StageChoice choice = BestStageChoice(doctor, patient, now, validPrevious);
                    MedicalTreatmentOption option = choice.Treatment;
                    report.AppendLine(
                        $"  edge doctor={doctor.LabelShortCap}/{doctor.ThingID} reserve={doctor.CanReserve(patient, 1, -1)} " +
                        $"choice={(choice.IsValid ? choice.Stage.ToString() : "invalid")} weight={choice.Weight:0} " +
                        $"intervention={(option?.IsValid == true ? option.Intervention.ToString() : "none")} " +
                        $"resource={option?.Resource?.ThingID ?? "none"} count={option?.Count ?? 0} " +
                        $"kit={choice.Kit?.TotalCount ?? 0}");
                }
            }

            return report.ToString();
        }

        private double EdgeWeight(Pawn worker, Pawn patient, SearchAndRescueStage stage)
        {
            IntVec3 interactionPosition = patient.Position;
            ActiveAssignment active = null;
            bool interceptingRescue = stage == SearchAndRescueStage.Treat &&
                                      activeByTarget.TryGetValue(patient, out active) &&
                                      active.Stage == SearchAndRescueStage.Rescue;
            bool reachable;
            if (interceptingRescue)
            {
                Pawn rescuer = active.Worker;
                if (rescuer == null || rescuer.Map != map)
                {
                    return 0d;
                }

                interactionPosition = rescuer.Position;
                reachable = worker.CanReach(rescuer, PathEndMode.ClosestTouch, Danger.Deadly);
            }
            else if (stage == SearchAndRescueStage.Rescue &&
                     Compatibility.IsTemporaryFieldTendBed(patient.CurrentBed()))
            {
                // A pawn lying on Smart Medicine's temporary tending spot participates in
                // that bed's reservation. CanReserveAndReach therefore rejects the rescue
                // edge before JobDriver_TakeToBed gets its normal chance to clear the
                // takee's reservations. Path reachability is the correct preflight here;
                // the rescue driver performs the authoritative reservations when it starts.
                reachable = worker.CanReach(patient, PathEndMode.ClosestTouch, Danger.Deadly);
            }
            else
            {
                reachable = worker.CanReserveAndReach(patient, PathEndMode.ClosestTouch, Danger.Deadly);
            }

            if (!reachable || !DestinationAllowedForAnimal(worker, interactionPosition))
            {
                return 0d;
            }

            double distance = Math.Sqrt(worker.Position.DistanceToSquared(interactionPosition));
            double urgency = PatientUrgency(patient);

            switch (stage)
            {
                case SearchAndRescueStage.Capture:
                    {
                        int priority = Compatibility.CaptureWorkPriority(worker);
                        bool treatmentPrerequisite = CaptureIsTreatmentPrerequisite(worker, patient);
                        if (priority <= 0 && treatmentPrerequisite)
                        {
                            priority = Compatibility.TreatmentWorkPriority(worker);
                        }
                        double bundle = treatmentPrerequisite
                            ? CaptureTreatmentBundleWeight +
                              TreatmentDeadlineWeight(worker, patient, distance, 720)
                            : 0d;
                        return AssignmentBaseWeight + CaptureBeforeTreatmentWeight + urgency * 35000d +
                               (5 - Math.Max(1, priority)) * 4000d - distance * 350d + bundle;
                    }
                case SearchAndRescueStage.Treat:
                case SearchAndRescueStage.FollowupTreat:
                    {
                        int now = Find.TickManager.TicksGame;
                        if (!carePlans.TryGetValue(patient, out MedicalCarePlan plan))
                        {
                            plan = MedicalCarePlan.Build(patient, now);
                            carePlans[patient] = plan;
                        }
                        MedicalTreatmentOption option = BestTreatmentOption(
                            worker,
                            patient,
                            plan,
                            allowExternalInventory: stage != SearchAndRescueStage.FollowupTreat);
                        return option.IsValid ? TreatmentEdgeWeight(worker, patient, option) : 0d;
                    }
                case SearchAndRescueStage.Rescue:
                    {
                        if (!Compatibility.CanCarryRescueTarget(worker, patient) ||
                            !TryFindRescueDestinationCached(worker, patient, out _, out _))
                        {
                            return 0d;
                        }

                        int rescueWorkPriority = Compatibility.RescueWorkPriority(worker);
                        double doctorOpportunityCost = 0d;
                        if (worker.workSettings != null && Compatibility.CanPerformTreatmentWork(worker))
                        {
                            double tendQuality = worker.GetStatValue(StatDefOf.MedicalTendQuality);
                            int doctorPriority = Compatibility.TreatmentWorkPriority(worker);
                            doctorOpportunityCost = tendQuality * 18000d + (5 - doctorPriority) * 5000d;
                        }

                        double movement = worker.GetStatValue(StatDefOf.MoveSpeed);
                        double resumeBonus = preferredRescuerByTarget.TryGetValue(patient, out Pawn preferred) &&
                                             preferred == worker
                            ? ResumeTransportWeight
                            : 0d;
                        return AssignmentBaseWeight + Compatibility.RescueWorkPreferenceBonus(worker) + resumeBonus +
                               urgency * 18000d + (5 - rescueWorkPriority) * 5000d +
                               movement * 1200d - distance * 400d - doctorOpportunityCost +
                               RescueMedicalPriorityWeight(patient);
                    }
                default:
                    return 0d;
            }
        }

        private static double PatientUrgency(Pawn patient)
        {
            float healthLoss = 1f - patient.health.summaryHealth.SummaryHealthPercent;
            float bleedRate = patient.health.hediffSet.BleedRateTotal;
            int ticksToDeath = HealthUtility.TicksUntilDeathDueToBloodLoss(patient);
            float deathPressure = ticksToDeath == int.MaxValue
                ? 0f
                : 1f - Mathf.Clamp01(ticksToDeath / 45000f);

            double urgency = 0.15d + healthLoss * 1.2d + Mathf.Clamp01(bleedRate * 2f) * 1.4d +
                             deathPressure * 2.2d + Compatibility.MedicalEmergencyUrgency(patient);
            if (patient.Downed)
            {
                urgency += 0.3d;
            }

            if (IsInSafePatientBed(patient))
            {
                urgency *= 0.55d;
            }
            else
            {
                urgency += 0.4d;
            }

            return urgency;
        }

        private static double RescueMedicalPriorityWeight(Pawn patient)
        {
            return Compatibility.RequiresUrgentSurgery(patient)
                ? UrgentSurgeryTransportWeight
                : 0d;
        }

        private static double TreatmentDeadlineWeight(Pawn doctor, Pawn patient, IntVec3 interactionPosition)
        {
            double distance = Math.Sqrt(doctor.Position.DistanceToSquared(interactionPosition));
            return TreatmentDeadlineWeight(doctor, patient, distance, 600);
        }

        private static int TreatmentBaseDuration(MedicalIntervention intervention)
        {
            switch (intervention)
            {
                case MedicalIntervention.HemostaticAgent:
                    return 60;
                case MedicalIntervention.Tourniquet:
                    return 90;
                case MedicalIntervention.RemoveTourniquet:
                    return 480;
                case MedicalIntervention.Bandage:
                case MedicalIntervention.Defibrillate:
                    return 180;
                case MedicalIntervention.Saline:
                    return 320;
                case MedicalIntervention.Cpr:
                    return 360;
                case MedicalIntervention.Blood:
                    return 720;
                default:
                    return 600;
            }
        }

        private static double TreatmentDeadlineWeight(
            Pawn doctor,
            Pawn patient,
            double routeDistance,
            int baseDuration)
        {
            int deadline = HealthUtility.TicksUntilDeathDueToBloodLoss(patient);
            if (deadline == int.MaxValue)
            {
                return 0d;
            }

            float moveSpeed = Math.Max(0.1f, doctor.GetStatValue(StatDefOf.MoveSpeed));
            float tendSpeed = Math.Max(0.05f, doctor.GetStatValue(StatDefOf.MedicalTendSpeed));
            double estimatedTicks = routeDistance * 60d / moveSpeed + baseDuration / tendSpeed;
            double slack = deadline - estimatedTicks;
            double survivalChance = 1d / (1d + Math.Exp(-Math.Max(-8d, Math.Min(8d, slack / 1200d))));
            double weight = survivalChance * 180000d;
            if (slack < 0d)
            {
                // A very severe but unreachable-in-time patient must not consume the only
                // doctor while several nearby savable casualties die. It remains matchable
                // when no better edge exists because the base assignment weight is retained.
                weight -= Math.Min(240000d, -slack * 80d);
            }

            return weight;
        }

        private Job MakeJob(Pawn worker, PendingAssignment pending, out IntVec3 destination)
        {
            Pawn patient = pending.Target;
            destination = IntVec3.Invalid;
            switch (pending.Stage)
            {
                case SearchAndRescueStage.Capture:
                    return Compatibility.MakeCaptureJob(patient);
                case SearchAndRescueStage.Treat:
                case SearchAndRescueStage.FollowupTreat:
                    return Compatibility.MakeTreatmentRoundJob(worker, patient, pending.Treatment);
                case SearchAndRescueStage.Restock:
                    {
                        if (pending.Kit == null || pending.Kit.IsEmpty)
                        {
                            return null;
                        }
                        Thing first = pending.Kit.Items[0].Thing;
                        Job restock = JobMaker.MakeJob(SearchAndRescueDefOf.SAR_RestockMedicalKit, first, patient);
                        restock.targetQueueA = pending.Kit.Items.Select(item => new LocalTargetInfo(item.Thing)).ToList();
                        restock.countQueue = pending.Kit.Items.Select(item => item.Count).ToList();
                        return restock;
                    }
                case SearchAndRescueStage.Supply:
                    if (pending.SupplyResource == null)
                    {
                        return null;
                    }
                    Job supply = JobMaker.MakeJob(
                        SearchAndRescueDefOf.SAR_DeliverMedicalSupply,
                        pending.SupplyResource,
                        patient);
                    supply.count = Math.Max(1, pending.SupplyCount);
                    return supply;
                case SearchAndRescueStage.Rescue:
                    return TryMakeRescueJob(worker, patient, out Job rescueJob, out destination)
                        ? rescueJob
                        : null;
                default:
                    return null;
            }
        }

        private bool TryMakeRescueJob(Pawn rescuer, Pawn patient, out Job job, out IntVec3 destination)
        {
            job = null;
            destination = IntVec3.Invalid;

            if (Compatibility.IsTrainedRescueAnimal(rescuer) && !patient.Downed)
            {
                return false;
            }

            if (!TryFindRescueDestination(rescuer, patient, out Building_Bed bed, out destination))
            {
                return false;
            }

            if (bed != null)
            {
                job = JobMaker.MakeJob(patient.IsPrisonerOfColony ? JobDefOf.Capture : JobDefOf.Rescue, patient, bed);
                return true;
            }

            job = JobMaker.MakeJob(SearchAndRescueDefOf.SAR_EvacuateToPoint, patient, destination);
            return true;
        }

        private bool TryFindRescueDestination(
            Pawn rescuer,
            Pawn patient,
            out Building_Bed bed,
            out IntVec3 destination)
        {
            return RescueDestinationPlanner.TryFind(map, rescuer, patient, out bed, out destination);
        }

        private bool TryFindRescueDestinationCached(
            Pawn rescuer,
            Pawn patient,
            out Building_Bed bed,
            out IntVec3 destination)
        {
            WorkerTargetPair key = new WorkerTargetPair(rescuer, patient);
            if (schedulingSnapshotActive &&
                schedulingRescueDestinations.TryGetValue(key, out RescueDestinationPlan cached))
            {
                bed = cached.Bed;
                destination = cached.Destination;
                return cached.Valid;
            }

            bool valid = TryFindRescueDestination(rescuer, patient, out bed, out destination);
            if (schedulingSnapshotActive)
            {
                schedulingRescueDestinations[key] = new RescueDestinationPlan(valid, bed, destination);
            }
            return valid;
        }

        private static bool DestinationAllowedForAnimal(Pawn worker, IntVec3 destination)
        {
            return RescueDestinationPlanner.DestinationAllowedForAnimal(worker, destination);
        }

        private static bool NeedsFieldStabilization(Pawn patient)
        {
            if (MechanicalCare.IsPatient(patient)) return MechanicalCare.NeedsRepair(patient);
            if (InfectionPriority.NeedsUrgentTend(patient) ||
                Compatibility.HasFieldTreatableEmergency(patient) ||
                Compatibility.HasMoreInjuriesTransfusionNeed(patient) ||
                Compatibility.HasHemogenTransfusionNeed(patient))
            {
                return true;
            }

            if (!patient.health.HasHediffsNeedingTend())
            {
                return false;
            }

            int ticksToDeath = HealthUtility.TicksUntilDeathDueToBloodLoss(patient);
            if (ticksToDeath != int.MaxValue && ticksToDeath <= SafeBloodLossHorizonTicks)
            {
                return true;
            }

            float untendedBleedRate = 0f;
            float largestUntendedBleed = 0f;
            foreach (Hediff hediff in patient.health.hediffSet.hediffs)
            {
                if (!hediff.TendableNow() || !hediff.Bleeding)
                {
                    continue;
                }

                float bleedRate = hediff.BleedRate;
                untendedBleedRate += bleedRate;
                largestUntendedBleed = Math.Max(largestUntendedBleed, bleedRate);
            }

            return largestUntendedBleed >= MajorUntendedBleedRate ||
                   untendedBleedRate >= SignificantTotalBleedRate;
        }

        private static int CountUntendedHediffs(Pawn patient)
        {
            return patient.health.hediffSet.hediffs.Count(hediff => hediff.TendableNow());
        }

        private static bool TreatmentProgressMade(Pawn patient, ActiveAssignment assignment)
        {
            // More Injuries reports Succeeded for an already-treated limb too.
            // Count the actual device effect before granting continuity or clearing retries.
            if (assignment.JobDef?.defName == "UseTourniquet")
                return patient.health.hediffSet.hediffs.Count(hediff =>
                    hediff.def.defName == "TourniquetApplied") > assignment.InitialTourniquetCount;

            bool epinephrineApplied = assignment.JobDef?.defName == "UseEpinephrine" &&
                                      patient.health.hediffSet.hediffs.Any(hediff =>
                                          hediff.def.defName == "AdrenalineRush" && hediff.Severity >= 0.25f);
            return assignment.RoundEffectSeen ||
                   epinephrineApplied ||
                   CountUntendedHediffs(patient) < assignment.InitialUntendedHediffs ||
                   patient.health.hediffSet.BleedRateTotal < assignment.InitialBleedRate - 0.0001f ||
                   GetBloodLossSeverity(patient) < assignment.InitialBloodLossSeverity - 0.0001f ||
                   GetHediffSeverity(patient, "Hemodilution") <
                       assignment.InitialHemodilutionSeverity - 0.0001f ||
                   Compatibility.FieldEmergencySeverity(patient) < assignment.InitialEmergencySeverity - 0.0001f ||
                   !Compatibility.NeedsAnyFieldTreatment(patient);
        }

        private static float GetBloodLossSeverity(Pawn patient)
        {
            return patient?.health?.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss)?.Severity ?? 0f;
        }

        private static float GetHediffSeverity(Pawn patient, string defName)
        {
            return patient?.health?.hediffSet.hediffs
                .FirstOrDefault(hediff => hediff.def.defName == defName)?.Severity ?? 0f;
        }

        private static bool RescueCompleted(Pawn patient, IntVec3 destination, Building_Bed destinationBed)
        {
            return RescueDestinationPlanner.RescueCompleted(patient, destination, destinationBed);
        }

        private static bool IsCarriedByActiveRescuer(Pawn patient, ActiveAssignment assignment)
        {
            return assignment.Stage == SearchAndRescueStage.Rescue &&
                   AssignmentJobStillRunning(assignment) &&
                   assignment.Worker.carryTracker?.CarriedThing == patient;
        }

        private static bool AssignmentJobStillRunning(ActiveAssignment assignment)
        {
            Job current = assignment?.Worker?.CurJob;
            return assignment != null && assignment.Identity.Matches(ActiveJobClaims.IdentityOf(current));
        }

        private static bool StandbyJobStillRunning(ActiveStandby standby)
        {
            Job current = standby?.Worker?.CurJob;
            return standby != null && standby.Identity.Matches(ActiveJobClaims.IdentityOf(current));
        }

        private bool IsCarriedByAnyPawn(Pawn patient)
        {
            return map.mapPawns.AllPawnsSpawned.Any(carrier =>
                carrier.carryTracker?.CarriedThing == patient);
        }

        private Pawn FindCurrentTreatingDoctor(Pawn patient)
        {
            return map.mapPawns.AllPawnsSpawned.FirstOrDefault(worker =>
            {
                Job job = worker.CurJob;
                return job != null &&
                       CompatibilityRegistry.PatientFor(worker, job, PatientJobRole.Treatment) == patient &&
                       Compatibility.IsTreatmentJob(job.def);
            });
        }

        private static bool IsInSafePatientBed(Pawn pawn)
        {
            return RescueDestinationPlanner.IsInSafePatientBed(pawn);
        }

        private List<Pawn> AllMarkedPawns()
        {
            return map.designationManager.AllDesignations
                .Where(designation => designation.target.HasThing && IsStageDesignation(designation.def))
                .Select(designation => designation.target.Thing as Pawn)
                .Where(pawn => pawn != null)
                .Distinct()
                .ToList();
        }

        private static MedicalCoordinationMode CoordinationMode =>
            SearchAndRescueMod.Settings?.MedicalCoordinationMode ?? MedicalCoordinationMode.EmergencyAuto;

        private bool AutomaticOriginAllowedByMode(Pawn patient, CareOrigin origin)
        {
            if (!AutomaticCareRelationshipEligible(patient))
            {
                return false;
            }

            MedicalCoordinationMode mode = CoordinationMode;
            bool emergencyAllowed = mode != MedicalCoordinationMode.MarkedOnly &&
                                    (origin & CareOrigin.AutomaticEmergency) != 0;
            bool routineAllowed = mode == MedicalCoordinationMode.AllTending &&
                                  (origin & CareOrigin.AutomaticRoutine) != 0;
            return emergencyAllowed || routineAllowed;
        }

        private List<Pawn> AllCareCandidates()
        {
            careAdmissions.Clear();
            HashSet<Pawn> candidates = new HashSet<Pawn>(AllMarkedPawns());
            if (CoordinationMode != MedicalCoordinationMode.MarkedOnly)
            {
                foreach (Pawn patient in map.mapPawns.AllPawnsSpawned)
                {
                    if (AutomaticCareRelationshipEligible(patient))
                    {
                        candidates.Add(patient);
                    }
                }
            }

            foreach (Pawn patient in candidates.ToList())
            {
                if (TryBuildCareAdmission(patient, out CareAdmission admission))
                {
                    careAdmissions[patient] = admission;
                }
                else
                {
                    candidates.Remove(patient);
                }
            }
            return candidates.ToList();
        }

        private bool TryGetCareAdmission(Pawn patient, out CareAdmission admission)
        {
            if (patient != null && careAdmissions.TryGetValue(patient, out admission))
            {
                return true;
            }

            if (!TryBuildCareAdmission(patient, out admission))
            {
                return false;
            }

            careAdmissions[patient] = admission;
            return true;
        }

        private bool TryBuildCareAdmission(Pawn patient, out CareAdmission admission)
        {
            CareOrigin origin = CareOrigin.None;
            if (HasDesignation(patient, SearchAndRescueDefOf.SAR_Treat))
            {
                origin |= CareOrigin.ManualTreatment;
            }
            if (HasDesignation(patient, SearchAndRescueDefOf.SAR_Rescue))
            {
                origin |= CareOrigin.ManualRescue;
            }
            if (HasDesignation(patient, SearchAndRescueDefOf.SAR_Capture))
            {
                origin |= CareOrigin.ManualCapture;
            }

            MedicalCoordinationMode mode = CoordinationMode;
            bool carriedByManagedRescuer = patient != null && !patient.Spawned &&
                                           activeByTarget.TryGetValue(patient, out ActiveAssignment activeRescue) &&
                                           activeRescue.Stage == SearchAndRescueStage.Rescue &&
                                           IsCarriedByActiveRescuer(patient, activeRescue);
            if (mode != MedicalCoordinationMode.MarkedOnly &&
                AutomaticCareRelationshipEligible(patient, carriedByManagedRescuer))
            {
                if (Compatibility.NeedsAnyFieldTreatment(patient))
                {
                    if (NeedsFieldStabilization(patient))
                    {
                        origin |= CareOrigin.AutomaticEmergency;
                    }
                    else if (mode == MedicalCoordinationMode.AllTending)
                    {
                        origin |= CareOrigin.AutomaticRoutine;
                    }
                }

                // Vanilla assigns bed rescue to Doctor work. Admit ordinary downed colony
                // patients to the unified transport lane as well, so a designated field
                // responder with Hauling/Nursing can evacuate them without a manual mark.
                if (patient.Spawned && patient.Downed && !IsInSafePatientBed(patient))
                {
                    origin |= CareOrigin.AutomaticRescue;
                }
            }

            admission = new CareAdmission(origin);
            return admission.IsValid;
        }

        private bool AutomaticCareRelationshipEligible(Pawn patient, bool allowManagedRescueCarry = false)
        {
            bool presentOnMap = patient?.Spawned == true && patient.Map == map;
            bool carriedOnMap = allowManagedRescueCarry && patient?.Spawned == false &&
                                patient.MapHeld == map;
            bool careEligible = presentOnMap
                ? TargetEligibility.CanReceiveFieldCare(patient)
                : carriedOnMap && TargetEligibility.CanReceiveFieldCareAfterDrop(patient);
            if (!careEligible || patient.InMentalState ||
                (!MechanicalCare.IsPatient(patient) && !HealthAIUtility.ShouldEverReceiveMedicalCareFromPlayer(patient)) ||
                patient.HostileTo(Faction.OfPlayer) && !patient.IsPrisonerOfColony)
            {
                return false;
            }

            // Automatic scope follows colony responsibility and never turns nearby wildlife
            // or neutral visitors into implicit battlefield orders. Explicit marks retain the
            // broader relationship policy used by the designator.
            return patient.Faction == Faction.OfPlayer || patient.IsPrisonerOfColony ||
                   patient.HostFaction == Faction.OfPlayer;
        }

        private bool TreatmentAdmitted(Pawn patient, SearchAndRescueStage stage)
        {
            return TryGetCareAdmission(patient, out CareAdmission admission) &&
                   admission.AllowsStage(stage);
        }

        private bool AutomaticRoutineRequiresNativePosture(Pawn patient)
        {
            return UsesAutomaticRoutineLane(patient);
        }

        private bool UsesAutomaticRoutineLane(Pawn patient)
        {
            return TryGetCareAdmission(patient, out CareAdmission admission) &&
                   (admission.Origin & CareOrigin.AutomaticRoutine) != 0 &&
                   !admission.HasManualTreatment;
        }

        private bool WorkerReadyForFollowupLane(Pawn worker, Pawn patient)
        {
            return UsesAutomaticRoutineLane(patient)
                ? Compatibility.CanPerformAutomaticRoutineTreatmentWork(worker)
                : Compatibility.CanPerformMarkedFollowupTreatmentWork(worker);
        }

        internal bool OwnsAutonomousTreatment(Pawn patient)
        {
            if (patient == null)
            {
                return false;
            }

            if (HasDesignation(patient, SearchAndRescueDefOf.SAR_Treat))
            {
                return true;
            }

            if (!TryGetCareAdmission(patient, out CareAdmission admission) ||
                !admission.HasAutomaticTreatment)
            {
                return false;
            }

            // Automatic admission alone never suppresses vanilla. Ownership begins only
            // after the unified graph has a viable soft or active claim; a failed materialized
            // job releases that claim and restores the vanilla fallback immediately.
            bool activeTreatment = activeByTarget.TryGetValue(patient, out ActiveAssignment active) &&
                                   (IsTreatmentStage(active.Stage) ||
                                    active.Stage == SearchAndRescueStage.Restock);
            bool pendingTreatment = pendingByWorker.Values.Any(pending => pending.Target == patient &&
                (IsTreatmentStage(pending.Stage) || pending.Stage == SearchAndRescueStage.Restock) &&
                admission.AllowsStage(pending.Stage));
            return activeTreatment || pendingTreatment;
        }

        internal bool OwnsAutonomousTransport(Pawn patient)
        {
            if (patient == null || !TryGetCareAdmission(patient, out CareAdmission admission) ||
                (admission.Origin & CareOrigin.AutomaticRescue) == 0)
            {
                return false;
            }

            // As with treatment, admission alone does not suppress vanilla rescue. Ownership
            // starts only when the graph has a viable claim and ends as soon as that claim is
            // released, preserving native and third-party fallback when SAR cannot act.
            bool activeRescue = activeByTarget.TryGetValue(patient, out ActiveAssignment active) &&
                                active.Stage == SearchAndRescueStage.Rescue;
            bool pendingRescue = pendingByWorker.Values.Any(pending => pending.Target == patient &&
                pending.Stage == SearchAndRescueStage.Rescue && admission.AllowsStage(pending.Stage));
            return activeRescue || pendingRescue;
        }

        private bool HasTreatmentInterest(Pawn patient)
        {
            return TryGetCareAdmission(patient, out CareAdmission admission) && admission.HasTreatment;
        }

        internal bool RetainsFieldSupplyReference(Pawn patient)
        {
            return patient?.Map == map && HasTreatmentInterest(patient) &&
                   Compatibility.NeedsAnyFieldTreatment(patient);
        }

        private bool HasAnyCareInterest(Pawn patient)
        {
            return TryGetCareAdmission(patient, out _);
        }

        private bool HasCareInterestOrOwnership(Pawn patient)
        {
            return HasAnyCareInterest(patient) || activeByTarget.ContainsKey(patient) ||
                   activeLogisticsByWorker.Values.Any(active => active.Target == patient) ||
                   pendingByWorker.Values.Any(pending => pending.Target == patient) ||
                   standbyByTarget.ContainsKey(patient);
        }

        private bool HasDesignation(Pawn pawn, DesignationDef def)
        {
            return map.designationManager.DesignationOn(pawn, def) != null;
        }

        private bool HasAnyStageDesignation(Pawn pawn)
        {
            return HasDesignation(pawn, SearchAndRescueDefOf.SAR_Treat) ||
                   HasDesignation(pawn, SearchAndRescueDefOf.SAR_Capture) ||
                   HasDesignation(pawn, SearchAndRescueDefOf.SAR_Rescue);
        }

        private void RememberRemovedMarker(Pawn pawn, SearchAndRescueStage stage)
        {
            CareOrigin origin = ManualOriginForStage(stage);
            if (origin == CareOrigin.None || pawn == null || pawn.Destroyed || pawn.Dead ||
                pawn.MapHeld != map || RecentMarkerMemoryTicks <= 0)
            {
                return;
            }

            recentMarkerMemories ??= new List<RecentMarkerMemory>();
            RecentMarkerMemory memory = recentMarkerMemories.FirstOrDefault(item => item.Target == pawn);
            int now = Find.TickManager.TicksGame;
            if (memory == null)
            {
                memory = new RecentMarkerMemory(pawn);
                recentMarkerMemories.Add(memory);
            }

            memory.ManualOrigins |= origin;
            memory.ExpiresAt = now + RecentMarkerMemoryTicks;
            // Automatic completion commonly happens while the casualty is still downed in
            // bed. Require a real recovery before this memory may fire.
            memory.Armed = !pawn.Downed;
        }

        private void ForgetRecentMarker(Pawn pawn, SearchAndRescueStage stage)
        {
            CareOrigin origin = ManualOriginForStage(stage);
            if (pawn == null || origin == CareOrigin.None || recentMarkerMemories == null)
            {
                return;
            }

            RecentMarkerMemory memory = recentMarkerMemories.FirstOrDefault(item => item.Target == pawn);
            if (memory == null)
            {
                return;
            }

            memory.ManualOrigins &= ~origin;
            if (memory.ManualOrigins == CareOrigin.None)
            {
                recentMarkerMemories.Remove(memory);
            }
        }

        private void UpdateRecentMarkerMemories(int now)
        {
            if (recentMarkerMemories == null || recentMarkerMemories.Count == 0)
            {
                return;
            }

            if (RecentMarkerMemoryTicks <= 0)
            {
                recentMarkerMemories.Clear();
                return;
            }

            for (int index = recentMarkerMemories.Count - 1; index >= 0; index--)
            {
                RecentMarkerMemory memory = recentMarkerMemories[index];
                Pawn target = memory?.Target;
                if (target == null || target.Destroyed || target.Dead || target.MapHeld != map ||
                    memory.ManualOrigins == CareOrigin.None || now >= memory.ExpiresAt)
                {
                    recentMarkerMemories.RemoveAt(index);
                    continue;
                }

                if (!target.Downed)
                {
                    memory.Armed = true;
                    continue;
                }

                if (!memory.Armed || !target.Spawned || target.Map != map)
                {
                    continue;
                }

                // Consume before adding designations: NotifyStageDesignationAdded is
                // synchronous and deliberately forgets any dormant copy of that stage.
                CareOrigin origins = memory.ManualOrigins;
                recentMarkerMemories.RemoveAt(index);
                RestoreRecentMarkers(target, origins);
            }
        }

        private void RestoreRecentMarkers(Pawn target, CareOrigin origins)
        {
            if ((origins & CareOrigin.ManualCapture) != 0 &&
                TargetEligibility.CanBeCaptured(target) && !target.IsPrisonerOfColony &&
                target.HostileTo(Faction.OfPlayer))
            {
                AddRestoredDesignation(target, SearchAndRescueStage.Capture);
            }

            bool permittedPatient = TargetEligibility.CanReceiveFieldCare(target) &&
                                    (!target.HostileTo(Faction.OfPlayer) || target.IsPrisonerOfColony ||
                                     HasDesignation(target, SearchAndRescueDefOf.SAR_Capture));
            if (permittedPatient && (origins & CareOrigin.ManualTreatment) != 0)
            {
                AddRestoredDesignation(target, SearchAndRescueStage.Treat);
            }

            if (permittedPatient && (origins & CareOrigin.ManualRescue) != 0 &&
                !IsInSafePatientBed(target))
            {
                AddRestoredDesignation(target, SearchAndRescueStage.Rescue);
            }
        }

        private void AddRestoredDesignation(Pawn target, SearchAndRescueStage stage)
        {
            DesignationDef def = DesignationForStage(stage);
            if (map.designationManager.DesignationOn(target, def) != null)
            {
                return;
            }

            map.designationManager.AddDesignation(new Designation(target, def));
            NotifyStageDesignationAdded(target, stage);
        }

        private static CareOrigin ManualOriginForStage(SearchAndRescueStage stage)
        {
            switch (stage)
            {
                case SearchAndRescueStage.Capture:
                    return CareOrigin.ManualCapture;
                case SearchAndRescueStage.Treat:
                case SearchAndRescueStage.FollowupTreat:
                case SearchAndRescueStage.Restock:
                case SearchAndRescueStage.Supply:
                    return CareOrigin.ManualTreatment;
                case SearchAndRescueStage.Rescue:
                    return CareOrigin.ManualRescue;
                default:
                    return CareOrigin.None;
            }
        }

        private void RemoveDesignation(Pawn pawn, SearchAndRescueStage stage)
        {
            DesignationDef def = DesignationForStage(stage);
            Designation designation = map.designationManager.DesignationOn(pawn, def);
            if (designation != null)
            {
                RememberRemovedMarker(pawn, stage);
                internalDesignationRemovalDepth++;
                try
                {
                    map.designationManager.RemoveDesignation(designation);
                }
                finally
                {
                    internalDesignationRemovalDepth--;
                }
            }

            careAdmissions.Remove(pawn);

            foreach (Pawn worker in pendingByWorker
                         .Where(pair => pair.Value.Target == pawn &&
                                         (pair.Value.Stage == stage ||
                                          stage == SearchAndRescueStage.Treat &&
                                         (pair.Value.Stage == SearchAndRescueStage.FollowupTreat ||
                                          pair.Value.Stage == SearchAndRescueStage.Restock ||
                                           pair.Value.Stage == SearchAndRescueStage.Supply) ||
                                          pair.Value.WaitForTreatment &&
                                         (stage == SearchAndRescueStage.Treat || stage == SearchAndRescueStage.Rescue)))
                         .Select(pair => pair.Key)
                         .ToList())
            {
                pendingByWorker.Remove(worker);
                medicalResources.ReleaseWorker(worker);
            }

            ClearDesignationRetries(pawn, stage);
            if (stage == SearchAndRescueStage.Treat)
            {
                carePlans.Remove(pawn);
                deliveredSupplyReevaluation.Remove(pawn);
                medicalResources.ReleasePatient(pawn);
                careAffinityClaims.Remove(pawn);
                foreach (KeyValuePair<Pawn, ActiveAssignment> logistics in activeLogisticsByWorker
                             .Where(pair => pair.Value.Target == pawn).ToList())
                {
                    activeClaims.ReleaseLogistics(logistics.Key);
                    if (AssignmentJobStillRunning(logistics.Value))
                    {
                        logistics.Key.jobs.EndCurrentJob(JobCondition.InterruptForced);
                    }
                }
            }
            if (stage == SearchAndRescueStage.Treat || stage == SearchAndRescueStage.Rescue)
            {
                StopStandby(pawn);
            }
            if (stage == SearchAndRescueStage.Rescue)
            {
                preferredRescuerByTarget.Remove(pawn);
            }
        }

        private void RemoveAllStages(Pawn pawn)
        {
            recentMarkerMemories?.RemoveAll(memory => memory?.Target == pawn);
            activeClaims.DetachPatient(pawn, out ActiveAssignment assignment,
                out List<ActiveAssignment> deliveries, out ActiveStandby standby);
            // Removing marks can invoke patches, and ending a Job can invoke its ThinkTree.
            // Retire all ownership first and let the next map tick choose replacement work.
            if (assignment != null) InterruptAssignmentWorker(assignment, startNewJob: false);
            if (StandbyJobStillRunning(standby))
                standby.Worker.jobs.EndCurrentJob(JobCondition.Succeeded, startNewJob: false);

            foreach (DesignationDef def in new[]
                     {
                         SearchAndRescueDefOf.SAR_Treat,
                         SearchAndRescueDefOf.SAR_Capture,
                         SearchAndRescueDefOf.SAR_Rescue
                     })
            {
                Designation designation = map.designationManager.DesignationOn(pawn, def);
                if (designation != null)
                {
                    map.designationManager.RemoveDesignation(designation);
                }
            }

            pendingByWorker.RemoveAll(pair => pair.Value.Target == pawn);
            foreach (ActiveAssignment logistics in deliveries)
            {
                medicalResources.ReleaseWorker(logistics.Worker);
                if (AssignmentJobStillRunning(logistics))
                    logistics.Worker.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            }
            ClearTargetRetries(pawn);
            carePlans.Remove(pawn);
            deliveredSupplyReevaluation.Remove(pawn);
            medicalResources.ReleasePatient(pawn);
            careAffinityClaims.Remove(pawn);
            preferredRescuerByTarget.Remove(pawn);
            RequestScheduleRebuild(maintenance: true);
        }

        private static DesignationDef DesignationForStage(SearchAndRescueStage stage)
        {
            return stage == SearchAndRescueStage.Treat || stage == SearchAndRescueStage.FollowupTreat ||
                   stage == SearchAndRescueStage.Restock ||
                   stage == SearchAndRescueStage.Supply
                ? SearchAndRescueDefOf.SAR_Treat
                : stage == SearchAndRescueStage.Capture
                    ? SearchAndRescueDefOf.SAR_Capture
                    : SearchAndRescueDefOf.SAR_Rescue;
        }

        private static bool IsStageDesignation(DesignationDef def)
        {
            return def == SearchAndRescueDefOf.SAR_Treat ||
                   def == SearchAndRescueDefOf.SAR_Capture ||
                   def == SearchAndRescueDefOf.SAR_Rescue;
        }

        private sealed class RecentMarkerMemory : IExposable
        {
            public Pawn Target;
            public CareOrigin ManualOrigins;
            public int ExpiresAt;
            public bool Armed;

            public RecentMarkerMemory()
            {
            }

            public RecentMarkerMemory(Pawn target)
            {
                Target = target;
            }

            public void ExposeData()
            {
                Scribe_References.Look(ref Target, "target");
                Scribe_Values.Look(ref ManualOrigins, "manualOrigins", CareOrigin.None);
                Scribe_Values.Look(ref ExpiresAt, "expiresAt");
                Scribe_Values.Look(ref Armed, "armed");
            }
        }

        private sealed class PendingAssignment
        {
            public readonly Pawn Target;
            public readonly SearchAndRescueStage Stage;
            public readonly double Weight;
            public readonly int CreatedAt;
            public readonly bool WaitForTreatment;
            public readonly int ExpiresAt;
            public readonly MedicalTreatmentOption Treatment;
            public readonly MedicalKitBundle Kit;
            public readonly Thing SupplyResource;
            public readonly int SupplyCount;

            public PendingAssignment(
                Pawn target,
                SearchAndRescueStage stage,
                double weight,
                int createdAt,
                bool waitForTreatment = false,
                int expiresAt = 0,
                MedicalTreatmentOption treatment = null,
                MedicalKitBundle kit = null,
                Thing supplyResource = null,
                int supplyCount = 0)
            {
                Target = target;
                Stage = stage;
                Weight = weight;
                CreatedAt = createdAt;
                WaitForTreatment = waitForTreatment;
                ExpiresAt = expiresAt;
                Treatment = treatment;
                Kit = kit;
                SupplyResource = supplyResource;
                SupplyCount = supplyCount;
            }
        }

        private sealed class TransportTask
        {
            public readonly Pawn Target;
            public readonly bool WaitForTreatment;
            public readonly Thing SupplyResource;
            public readonly int SupplyCount;
            public readonly double SupplyBenefit;
            public bool IsSupply => SupplyResource != null;

            public TransportTask(Pawn target, bool waitForTreatment)
            {
                Target = target;
                WaitForTreatment = waitForTreatment;
            }

            public TransportTask(Pawn target, Thing supplyResource, int supplyCount, double supplyBenefit)
            {
                Target = target;
                SupplyResource = supplyResource;
                SupplyCount = supplyCount;
                SupplyBenefit = supplyBenefit;
            }
        }

        private readonly struct StageChoice
        {
            public static readonly StageChoice Invalid = new StageChoice(
                SearchAndRescueStage.Rescue, 0d, null, null);

            public readonly SearchAndRescueStage Stage;
            public readonly double Weight;
            public readonly MedicalTreatmentOption Treatment;
            public readonly MedicalKitBundle Kit;
            public bool IsValid => Weight > 0d;

            public StageChoice(
                SearchAndRescueStage stage,
                double weight,
                MedicalTreatmentOption treatment,
                MedicalKitBundle kit)
            {
                Stage = stage;
                Weight = weight;
                Treatment = treatment;
                Kit = kit;
            }
        }

        private readonly struct WorkerTargetPair : IEquatable<WorkerTargetPair>
        {
            private readonly Pawn worker;
            private readonly Pawn target;

            public WorkerTargetPair(Pawn worker, Pawn target)
            {
                this.worker = worker;
                this.target = target;
            }

            public bool Equals(WorkerTargetPair other)
            {
                return worker == other.worker && target == other.target;
            }

            public override bool Equals(object obj)
            {
                return obj is WorkerTargetPair other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((worker?.thingIDNumber ?? 0) * 397) ^ (target?.thingIDNumber ?? 0);
                }
            }
        }

        private readonly struct RescueDestinationPlan
        {
            public readonly bool Valid;
            public readonly Building_Bed Bed;
            public readonly IntVec3 Destination;

            public RescueDestinationPlan(bool valid, Building_Bed bed, IntVec3 destination)
            {
                Valid = valid;
                Bed = bed;
                Destination = destination;
            }
        }

        private readonly struct StageRetryKey : IEquatable<StageRetryKey>
        {
            public readonly Pawn Target;
            public readonly SearchAndRescueStage Stage;

            public StageRetryKey(Pawn target, SearchAndRescueStage stage)
            {
                Target = target;
                Stage = stage;
            }

            public bool Equals(StageRetryKey other)
            {
                return Target == other.Target && Stage == other.Stage;
            }

            public override bool Equals(object obj)
            {
                return obj is StageRetryKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((Target?.thingIDNumber ?? 0) * 397) ^ (int)Stage;
                }
            }
        }

        private sealed class StageRetryState
        {
            public readonly int RetryAfter;
            public readonly int FailureCount;

            public StageRetryState(int retryAfter, int failureCount)
            {
                RetryAfter = retryAfter;
                FailureCount = failureCount;
            }
        }

    }
}
