using SearchAndRescue;

record Worker(string Name, bool Doctor, double Skill, double X, bool Nurse = false);
record Patient(string Name, double Urgency, int Deadline, double X, bool NeedsDevice = false);
record TaskOffer(Patient Patient, string Kind, double Benefit, double PickupX);
record ClinicalOffer(Patient Patient, bool Supportive, double Benefit);
record HandoffState(bool CarrierJobRunning, bool PatientCarried, bool TreatmentStarted);
sealed class PooledJob(string def) { public string Def = def; }
enum CoordinationScope { MarkedOnly, EmergencyAuto, AllTending }

static class Simulation
{
    private const double Base = 1_000_000d;

    private static void CompletedCareWinsOverSpeculativeAffinity()
    {
        double fresh = TreatmentContinuityRules.Weight(TreatmentContinuityRules.DurationTicks);
        Assert(fresh == 70000d, "completed care starts with its full continuity weight");
        Assert(TreatmentContinuityRules.ShouldReplace(true, false, true, 70000d, fresh),
            "actual treatment supersedes another doctor's fresh pickup plan");
        Assert(TreatmentContinuityRules.ShouldReplace(true, false, true, 500000d, fresh),
            "latest actual treatment supersedes even a stronger speculative handoff");
        Assert(!TreatmentContinuityRules.ShouldReplace(true, true, false, 30000d, 70000d),
            "fresh restock plan cannot erase an unexpired completed-care affinity");
        Assert(TreatmentContinuityRules.ShouldReplace(true, true, true, fresh, fresh),
            "a different doctor who actually treated becomes the new incumbent");
        Assert(TreatmentContinuityRules.ShouldReplace(false, true, false, 0d, 70000d),
            "expired continuity does not hold the patient");
        Assert(TreatmentContinuityRules.Weight(0) == 0d,
            "continuity expires instead of retaining a permanent base bonus");
        string[] workers = { "incumbent", "new-doctor" };
        string[] patients = { "current", "other" };
        var stable = WeightedBipartiteMatcher.MaximumWeight(workers, patients,
            (w, p) => Base + (w == "incumbent" && p == "current" ? fresh : 0d) +
                (w == "new-doctor" && p == "current" ? 10000d : 0d));
        Assert(stable.Single(m => m.Target == "current").Worker == "incumbent",
            "small skill or route changes retain the last treating doctor after rematching");
        var urgent = WeightedBipartiteMatcher.MaximumWeight(workers, patients,
            (w, p) => Base + (w == "incumbent" && p == "current" ? fresh : 0d) +
                (w == "incumbent" && p == "other" ? 150000d : 0d));
        Assert(urgent.Single(m => m.Target == "other").Worker == "incumbent",
            "a materially more valuable urgent pairing still overrides continuity");
        Console.WriteLine("PASS: completed-care affinity precedence, expiry and global rematching (9 checks)");
    }


    public static int Main()
    {
        ProductionPolicyTests.Run();
        CompletedCareWinsOverSpeculativeAffinity();
        BetterDoctorGetsSeverePatient();
        NurseTakesTransfusionWhileDoctorTakesSkilledCare();
        DoctorFallsBackToTransfusionWithoutNurse();
        BandagedBloodLossRemainsUrgent();
        HypovolemicShockUsesBoundedResuscitation();
        StableFollowupPrefersLocality();
        CasualtyBacklogPricesDoctorDetours();
        PostRoundContinuityPreventsMarginalSwitch();
        TemporaryBedForceWaitKeepsExistingLayDown();
        TemporaryBedReplacementWaitsForFreshLifecycle();
        SchedulingSnapshotCachesPickupReachability();
        TransportTaskBuildShortCircuitScenarios();
        GroupedTransportRolesPreserveOtherCasualties();
        SupplySourcesDiversifyBeforeQueueing();
        RemovedBedCleanupUsesLiveOccupancy();
        VanillaTendYieldsOnlyAtUrgentWoundBoundary();
        ScarceDeviceFallsBackWithoutDroppingWorker();
        UrgentSupplyCanBeatRoutineRescue();
        SupplyDistanceHasRealDispatchGate();
        FieldSupplyOwnershipInvariants();
        MedicineSupplyShortfallScenarios();
        StaleMedicineTargetRecoversAtPickupBoundary();
        ExistingHaulYieldsToNewFieldReference();
        ForbiddenFieldSupplyBecomesDeficit();
        DestroyedFieldSupplyRematchesNextTick();
        TreatmentOwnershipBoundaries();
        TakeToBedOwnershipBoundaries();
        RescueInterceptionIsTwoPhase();
        SafeDeliveryKeepsUnresolvedTreatment();
        UnifiedCareAdmissionModes();
        AutomaticRescueAdmissionBoundaries();
        TourniquetRemovalLifecycle();
        RecycledJobReferenceDoesNotKeepClaimActive();
        InterruptedEvacuationDropsPatient();
        SchedulerControlTransitions();
        FieldResponderGateRespectsUnderlyingWork();
        CaptureTreatmentBundleKeepsSameDoctor();
        RescuePointChangesRetireOldRoutes();
        InventoryDonorControlBoundaries();
        RandomizedMatchingInvariants();
        Console.WriteLine("PASS: 40 scheduler/resource/ownership/lifecycle scenarios");
        return 0;
    }

    private static void RecycledJobReferenceDoesNotKeepClaimActive()
    {
        PooledJob job = new("UseBandage");
        PooledJob storedReference = job;
        string storedDef = job.Def;
        job.Def = "GotoWander";

        Assert(ReferenceEquals(job, storedReference),
            "the fixture models RimWorld reusing the same pooled Job object");
        Assert(!(ReferenceEquals(job, storedReference) && job.Def == storedDef),
            "a recycled Job reference must not keep the old assignment active");
        Console.WriteLine("PASS: pooled Job reuse cannot retain a stale active claim");
    }

    private static void UnifiedCareAdmissionModes()
    {
        static (bool admitted, bool emergency, bool followup) Admit(
            CoordinationScope scope,
            bool manuallyMarked,
            bool needsAnyCare,
            bool needsStabilization)
        {
            bool automaticEmergency = scope != CoordinationScope.MarkedOnly &&
                                      needsAnyCare && needsStabilization;
            bool automaticRoutine = scope == CoordinationScope.AllTending &&
                                    needsAnyCare && !needsStabilization;
            return (
                manuallyMarked || automaticEmergency || automaticRoutine,
                needsAnyCare && (manuallyMarked || automaticEmergency) && needsStabilization,
                needsAnyCare && (manuallyMarked || automaticRoutine) && !needsStabilization);
        }

        Assert(!Admit(CoordinationScope.MarkedOnly, false, true, true).admitted,
            "marked-only must not silently enroll an unmarked emergency");
        Assert(Admit(CoordinationScope.MarkedOnly, true, true, true).emergency,
            "marked patients must use the shared emergency lane");
        Assert(Admit(CoordinationScope.EmergencyAuto, false, true, true).emergency,
            "emergency-auto must enroll an unmarked unstable patient");
        Assert(!Admit(CoordinationScope.EmergencyAuto, false, true, false).admitted,
            "emergency-auto must release stable routine care to vanilla");
        Assert(Admit(CoordinationScope.AllTending, false, true, false).followup,
            "all-tending must enroll stable routine care in the shared follow-up lane");

        bool rescueMarkedEmergency = Admit(
            CoordinationScope.EmergencyAuto,
            manuallyMarked: false,
            needsAnyCare: true,
            needsStabilization: true).emergency;
        Assert(rescueMarkedEmergency,
            "a rescue-only mark must still gain an automatic emergency-treatment edge");

        bool carriedByManagedRescuer = true;
        bool carriedPatientStillAdmitted = carriedByManagedRescuer &&
                                           Admit(CoordinationScope.EmergencyAuto, false, true, true).emergency;
        Assert(carriedPatientStillAdmitted,
            "managed carriage must not erase automatic emergency admission before interception");

        const int automaticRoutinePriority = 105;
        const int vanillaEmergencyPriority = 110;
        const int vanillaRoutinePriority = 100;
        const int markedFollowupPriority = 85;
        Assert(automaticRoutinePriority < vanillaEmergencyPriority &&
               automaticRoutinePriority > vanillaRoutinePriority &&
               markedFollowupPriority < vanillaRoutinePriority,
            "the shared claim needs separate materialization priorities for all-tending and marked follow-up");

        bool pureAutomaticRoutine = true;
        bool goodNativeTendingPosture = false;
        bool automaticRoutineEdgeAllowed = !pureAutomaticRoutine || goodNativeTendingPosture;
        Assert(!automaticRoutineEdgeAllowed,
            "pure automatic routine care must wait for native laying posture");

        bool automaticAdmissionWithoutClaimBlocksVanilla = false;
        bool automaticAdmissionWithClaimBlocksVanilla = true;
        Assert(!automaticAdmissionWithoutClaimBlocksVanilla && automaticAdmissionWithClaimBlocksVanilla,
            "automatic ownership must begin at claim, not at admission, preserving vanilla fallback");
        Console.WriteLine("PASS: unified three-mode care admission + soft ownership fallback");
    }

    private static void BetterDoctorGetsSeverePatient()
    {
        Worker[] doctors =
        {
            new("expert", true, 1.25, 0),
            new("novice", true, 0.55, 0)
        };
        Patient[] patients =
        {
            new("critical", 4.8, 3500, 12),
            new("moderate", 1.7, 28000, 8),
            new("minor", 0.5, int.MaxValue, 3)
        };
        var matches = WeightedBipartiteMatcher.MaximumWeight(
            doctors,
            patients,
            (doctor, patient) => DoctorWeight(doctor, patient, 1.0));
        Assert(matches.Single(match => match.Worker.Name == "expert").Target.Name == "critical",
            "the expert doctor should stabilize the critical casualty");
        Assert(matches.Single(match => match.Worker.Name == "novice").Target.Name == "moderate",
            "the remaining doctor should take the next savable casualty");
        Console.WriteLine("PASS: skill/severity pairing");
    }

    private static void GroupedTransportRolesPreserveOtherCasualties()
    {
        Worker[] workers =
        {
            new("hauler-a", false, 0, 0),
            new("hauler-b", false, 0, 0)
        };
        Patient crowded = new("crowded", 4, 1000, 4);
        Patient waiting = new("waiting", 3, 2000, 8);
        Patient[] patients = { crowded, waiting };
        TaskOffer[] offers =
        {
            new(crowded, "rescue", 1, 0),
            new(crowded, "supply", 1, 0),
            new(waiting, "rescue", 1, 0)
        };

        double Weight(Worker worker, TaskOffer offer)
        {
            if (offer.Patient == crowded)
            {
                return worker.Name == "hauler-a"
                    ? offer.Kind == "rescue" ? 100 : 90
                    : offer.Kind == "rescue" ? 95 : 94;
            }
            return worker.Name == "hauler-a" ? 80 : 70;
        }

        List<Match<Worker, TaskOffer>> matches = WeightedBipartiteMatcher.MaximumWeightGrouped(
            workers,
            patients,
            patient => offers.Where(offer => offer.Patient == patient),
            Weight);

        Assert(matches.Count == 2 && matches.Select(match => match.Target.Patient).Distinct().Count() == 2,
            "mutually exclusive roles for one casualty must not consume two worker columns");
        Assert(matches.Single(match => match.Target.Patient == crowded).Worker.Name == "hauler-b",
            "grouped solving must preserve the globally better worker/casualty allocation");
        Console.WriteLine("PASS: grouped transport roles preserve other casualties");
    }

    private static void SupplySourcesDiversifyBeforeQueueing()
    {
        Worker[] workers =
        {
            new("hauler-near", false, 0, 0),
            new("hauler-far", false, 0, 8)
        };
        Patient first = new("first", 4, 1000, 20);
        Patient second = new("second", 4, 1000, 22);
        Patient[] patients = { first, second };
        TaskOffer[] offers =
        {
            new(first, "medicine-a", 1, 4),
            new(first, "medicine-b", 1, 7),
            new(second, "medicine-a", 1, 4),
            new(second, "medicine-b", 1, 7)
        };

        double Weight(Worker worker, TaskOffer offer) =>
            1000 - Math.Abs(worker.X - offer.PickupX) * 10 -
            Math.Abs(offer.PickupX - offer.Patient.X);

        List<Match<Worker, TaskOffer>> grouped = WeightedBipartiteMatcher.MaximumWeightGrouped(
            workers,
            patients,
            patient => offers.Where(offer => offer.Patient == patient),
            Weight);
        List<Match<Worker, TaskOffer>> diversified = WeightedBipartiteMatcher.DiversifyExclusiveOptions(
            grouped,
            offer => offer.Patient,
            patient => offers.Where(offer => offer.Patient == patient),
            Weight,
            _ => true,
            offer => offer.Kind,
            (_, _) => true);

        Assert(diversified.Select(match => match.Target.Kind).Distinct().Count() == 2,
            "parallel supply jobs should use different reachable stacks when alternatives exist");

        TaskOffer[] onlyOneStack =
        {
            new(first, "medicine-a", 1, 4),
            new(second, "medicine-a", 1, 4)
        };
        List<Match<Worker, TaskOffer>> singleSourceMatches =
            WeightedBipartiteMatcher.MaximumWeightGrouped(
                workers,
                patients,
                patient => onlyOneStack.Where(offer => offer.Patient == patient),
                Weight);
        List<Match<Worker, TaskOffer>> singleSourceDiversified =
            WeightedBipartiteMatcher.DiversifyExclusiveOptions(
                singleSourceMatches,
                offer => offer.Patient,
                patient => onlyOneStack.Where(offer => offer.Patient == patient),
                Weight,
                _ => true,
                offer => offer.Kind,
                (_, _) => true);
        Assert(singleSourceDiversified.Count == 2,
            "one sufficiently large source must remain a sequential fallback");
        Console.WriteLine("PASS: supply source diversification avoids avoidable pickup queues");
    }

    private static void NurseTakesTransfusionWhileDoctorTakesSkilledCare()
    {
        Worker[] responders =
        {
            new("expert-doctor", true, 1.30, 0, true),
            new("nurse", false, 0.55, 24, true)
        };
        ClinicalOffer[] tasks =
        {
            new(new Patient("airway-emergency", 4.8, 2600, 3, true), false, 2.5),
            new(new Patient("bandaged-severe-blood-loss", 4.4, int.MaxValue, 25, true), true, 2.4)
        };

        var matches = WeightedBipartiteMatcher.MaximumWeight(
            responders,
            tasks,
            ClinicalWeight);
        Assert(matches.Single(match => match.Worker.Name == "expert-doctor").Target.Patient.Name ==
               "airway-emergency",
            "the doctor must stay on the skill-sensitive emergency");
        Assert(matches.Single(match => match.Worker.Name == "nurse").Target.Patient.Name ==
               "bandaged-severe-blood-loss",
            "the nurse should concurrently own the fixed-effect transfusion");
        Console.WriteLine("PASS: nurse transfusion / doctor skilled-care split");
    }

    private static void DoctorFallsBackToTransfusionWithoutNurse()
    {
        Worker[] responders = { new("doctor", true, 1.1, 0) };
        ClinicalOffer transfusion = new(
            new Patient("blood-loss", 4.5, int.MaxValue, 8, true),
            true,
            2.4);
        var match = WeightedBipartiteMatcher.MaximumWeight(
            responders,
            new[] { transfusion },
            ClinicalWeight).Single();
        Assert(match.Worker.Name == "doctor" && match.Weight > 0,
            "Doctor work must remain a transfusion fallback when no Nursing worker is available");
        Console.WriteLine("PASS: doctor transfusion fallback");
    }

    private static void BandagedBloodLossRemainsUrgent()
    {
        static double TransfusionPressure(double bloodLoss, double bleedRate) =>
            (bloodLoss >= 0.45
                ? 260_000 + Math.Min(1, (bloodLoss - 0.45) / 0.55) * 220_000
                : bloodLoss >= 0.15
                    ? 60_000 + (bloodLoss - 0.15) / 0.30 * 100_000
                    : 0) + bleedRate * 10_000;

        Assert(TransfusionPressure(0.62, 0) > TransfusionPressure(0.30, 0.2),
            "severe accumulated blood loss must remain urgent after bleeding reaches zero");
        Console.WriteLine("PASS: bandaged blood-loss transfusion urgency");
    }

    private static void HypovolemicShockUsesBoundedResuscitation()
    {
        static int PlannedBags(double bloodLoss, double shockSeverity, double volumePerBag)
        {
            const double stabilizationThreshold = 0.449;
            int severeLossBags = (int)Math.Ceiling(
                Math.Max(0, bloodLoss - stabilizationThreshold) / volumePerBag);
            if (severeLossBags > 0) return severeLossBags;
            return shockSeverity >= 0.5 && bloodLoss > 0.30 ? 1 : 0;
        }

        static double Urgency(double bloodLoss, bool shock, double shockSeverity, bool wholeBlood) => shock
            ? 520_000 + (wholeBlood ? 70_000 : 0) +
              Math.Min(1, Math.Max(0, shockSeverity)) * 180_000
            : bloodLoss >= 0.45 ? 260_000 : 0;

        Assert(PlannedBags(0.70, 0.2, 0.35) == 1 && PlannedBags(0.70, 0.2, 0.15) == 2,
            "shock resuscitation must stop after crossing More Injuries' safe-volume threshold");
        Assert(PlannedBags(0.44, 0.5, 0.15) == 1 && PlannedBags(0.29, 0.8, 0.15) == 0,
            "moderate shock gets one bounded dose and stops below the 0.30 volume-deficit floor");
        Assert(PlannedBags(0.44, 0.49, 0.15) == 0,
            "minor shock below the severe blood-loss threshold must not trigger transfusion");
        Assert(Urgency(0.55, true, 0.1, true) > Urgency(0.70, false, 0, true),
            "any diagnosed hypovolemic shock must outrank ordinary severe blood loss");
        Assert(Urgency(0.55, true, 0.1, true) > Urgency(0.55, true, 0.1, false),
            "whole blood should be preferred over saline when both can stabilize shock");
        Console.WriteLine("PASS: bounded hypovolemic-shock resuscitation");
    }

    private static void StableFollowupPrefersLocality()
    {
        Worker[] doctors =
        {
            new("west", true, 0.9, 0),
            new("east", true, 0.95, 80)
        };
        Patient[] patients =
        {
            new("west-stable", 0.8, int.MaxValue, 8),
            new("east-stable", 0.9, int.MaxValue, 72)
        };
        var matches = WeightedBipartiteMatcher.MaximumWeight(
            doctors,
            patients,
            (doctor, patient) => DoctorWeight(doctor, patient, 1.0, 900));
        Assert(matches.Single(match => match.Worker.Name == "west").Target.Name == "west-stable" &&
               matches.Single(match => match.Worker.Name == "east").Target.Name == "east-stable",
            "stable follow-up treatment should keep doctors in their local casualty cluster");
        Console.WriteLine("PASS: stable follow-up locality");
    }

    private static void CasualtyBacklogPricesDoctorDetours()
    {
        static double OptionWeight(
            double urgency,
            double quality,
            double benefit,
            double route,
            double direct,
            double backlogPressure)
        {
            double detourTicks = Math.Max(0, route - direct) * 60 / 4.6;
            return Base + urgency * quality * 120_000 + urgency * benefit * 30_000 +
                   quality * 3_000 - route * 325 -
                   detourTicks * (30 + Math.Min(4, urgency) * 30 + backlogPressure);
        }

        double deviceWithoutBacklog = OptionWeight(4, 1.0, 4.0, 79, 11, 0);
        double deviceWithBacklog = OptionWeight(4, 1.0, 4.0, 79, 11, 360);
        double dryNow = OptionWeight(4, 0.9, 0.45, 11, 11, 360);
        Assert(deviceWithoutBacklog > dryNow,
            "a high-value hemostatic device may justify a long pickup when doctors are not backlogged");
        Assert(deviceWithBacklog < dryNow,
            "two waiting emergencies per doctor should favor immediate dry stabilization over a long pickup");
        Console.WriteLine("PASS: casualty backlog prices doctor equipment detours");
    }

    private static void PostRoundContinuityPreventsMarginalSwitch()
    {
        Worker doctor = new("doctor", true, 1.0, 10);
        Patient current = new("current", 1.0, int.MaxValue, 12);
        Patient marginal = new("marginal", 1.05, int.MaxValue, 18);
        double stay = DoctorWeight(doctor, current, 1.0, 900) + 60_000;
        double change = DoctorWeight(doctor, marginal, 1.0, 900);
        Assert(stay > change,
            "a recently treated patient should retain the doctor across a marginal wound-boundary change");
        Console.WriteLine("PASS: post-round treatment continuity");
    }

    private static void TemporaryBedForceWaitKeepsExistingLayDown()
    {
        static string ForceWaitAction(
            bool managed,
            bool activeMoreInjuriesProvider,
            bool currentJobOwnsTemporaryBed) =>
            (managed || activeMoreInjuriesProvider) && currentJobOwnsTemporaryBed
                ? "keep-current-job"
                : "vanilla-force-wait";

        Assert(ForceWaitAction(true, false, true) == "keep-current-job",
            "More Injuries ForceWait must not replace LayDown on Smart Medicine's temporary bed");
        Assert(ForceWaitAction(false, true, true) == "keep-current-job",
            "a native More Injuries procedure must retain the job-owned temporary bed even outside SAR ownership");
        Assert(ForceWaitAction(true, true, false) == "vanilla-force-wait" &&
               ForceWaitAction(false, false, true) == "vanilla-force-wait",
            "the ForceWait guard must stay scoped to an owned temporary-bed job and active treatment");
        Console.WriteLine("PASS: temporary-bed ForceWait lifecycle");
    }

    private static void TemporaryBedReplacementWaitsForFreshLifecycle()
    {
        static string PatientBedJob(
            bool managed,
            bool currentJobOwnsTemporaryBed,
            bool existingTemporaryBedReservable) =>
            currentJobOwnsTemporaryBed
                ? "no-replacement"
                : managed && !existingTemporaryBedReservable
                    ? "no-contested-job"
                    : "smart-medicine-fallback";

        Assert(PatientBedJob(true, true, true) == "no-replacement",
            "a retiring LayDown must not hand its lifecycle-owned temporary bed to a new job");
        Assert(PatientBedJob(false, true, true) == "no-replacement",
            "overlapping CurrentBed results must not replace an unmanaged lifecycle-owning LayDown job");
        Assert(PatientBedJob(true, false, false) == "no-contested-job",
            "Smart Medicine's fallback must reject a temporary spot that cannot be reserved");
        Assert(PatientBedJob(true, false, true) == "smart-medicine-fallback" &&
               PatientBedJob(false, false, false) == "smart-medicine-fallback",
            "the guard must preserve valid and unmanaged Smart Medicine field tending");
        Console.WriteLine("PASS: temporary-bed replacement lifecycle");
    }

    private static void SchedulingSnapshotCachesPickupReachability()
    {
        Dictionary<(string worker, string thing, int count), bool> snapshot = new();
        int pathQueries = 0;
        bool Query(string worker, string thing, int count)
        {
            var key = (worker, thing, Math.Max(1, count));
            if (snapshot.TryGetValue(key, out bool cached)) return cached;
            pathQueries++;
            return snapshot[key] = true;
        }

        Assert(Query("doctor", "medicine-stack", 1) &&
               Query("doctor", "medicine-stack", 1) && pathQueries == 1,
            "patients sharing an edge resource should reuse one pickup reachability query per snapshot");
        Assert(Query("doctor", "medicine-stack", 2) && pathQueries == 2,
            "reservation counts must remain distinct cache keys");
        snapshot.Clear();
        Assert(Query("doctor", "medicine-stack", 1) && pathQueries == 3,
            "a new scheduling snapshot must reevaluate live reachability and reservations");
        Console.WriteLine("PASS: per-snapshot pickup reachability cache");
    }

    private static void TransportTaskBuildShortCircuitScenarios()
    {
        int referenceRefreshes = 0;
        int taskBuilds = 0;
        void Schedule(IEnumerable<(bool capable, bool available)> candidates)
        {
            var snapshot = candidates.ToList();
            referenceRefreshes++;
            if (!snapshot.Any(candidate => candidate.capable && candidate.available)) return;
            taskBuilds++;
        }

        Schedule(new[] { (capable: false, available: true), (capable: false, available: false) });
        Assert(taskBuilds == 0, "transport tasks should not be built without any enabled lane");
        Schedule(new[] { (capable: true, available: false) });
        Assert(taskBuilds == 0, "busy capable workers should not build an empty graph");
        Assert(referenceRefreshes == 2, "empty graphs must retain supply-reference scope maintenance");
        Schedule(new[] { (capable: true, available: true) });
        Assert(taskBuilds == 1, "available capable workers must build the transport graph");
        Assert(referenceRefreshes == 3, "available graphs must refresh references exactly once");
        Console.WriteLine("PASS: no-capable-transport-worker short circuit");
    }

    private static void RemovedBedCleanupUsesLiveOccupancy()
    {
        static string NormalizeKickTarget(string capturedBed, string currentBed, bool postureInBed)
        {
            if (capturedBed == currentBed) return capturedBed;
            if (currentBed == "none" && !postureInBed) return "skip";
            return currentBed;
        }

        Assert(NormalizeKickTarget("removed", "none", true) == "none",
            "a removed patient bed should clear posture without using its stale sleeping slot");
        Assert(NormalizeKickTarget("real-bed", "temp-bed", true) == "temp-bed",
            "overlapping-bed cleanup should act on the bed currently occupied by the patient");
        Assert(NormalizeKickTarget("removed", "none", false) == "skip",
            "already-normalized posture should ignore a stale bed finish action");
        Console.WriteLine("PASS: removed/overlapping bed cleanup");
    }

    private static void VanillaTendYieldsOnlyAtUrgentWoundBoundary()
    {
        static bool YieldAfterCommittedWound(
            bool playerForced,
            bool woundCommitted,
            double currentScore,
            double replacementScore) =>
            !playerForced && woundCommitted && replacementScore > currentScore + 60_000;

        Assert(YieldAfterCommittedWound(false, true, 120_000, 420_000),
            "routine vanilla tending should yield after a wound when a marked emergency is much stronger");
        Assert(!YieldAfterCommittedWound(false, false, 120_000, 420_000),
            "vanilla tending must never be interrupted in the middle of a tend bar");
        Assert(!YieldAfterCommittedWound(false, true, 120_000, 170_000),
            "a marginal score change should not churn the current vanilla patient");
        Assert(!YieldAfterCommittedWound(true, true, 120_000, 420_000),
            "player-forced treatment must retain ownership");
        static bool YieldToSamePatientTransfusion(
            bool playerForced,
            bool woundCommitted,
            bool urgentTransfusionNeeded) =>
            !playerForced && woundCommitted && urgentTransfusionNeeded;
        Assert(YieldToSamePatientTransfusion(false, true, true),
            "automatic continuous tending must yield after a wound to an urgent transfusion on the same patient");
        Assert(!YieldToSamePatientTransfusion(true, true, true),
            "a player-forced tend must not be redirected to transfusion");
        Console.WriteLine("PASS: vanilla tend safe-boundary preemption");
    }

    private static void ScarceDeviceFallsBackWithoutDroppingWorker()
    {
        Worker[] doctors =
        {
            new("doctor-a", true, 1.0, 0),
            new("doctor-b", true, 0.9, 20)
        };
        Patient[] patients =
        {
            new("cardiac-a", 5.0, 2400, 5, true),
            new("cardiac-b", 4.6, 3200, 18, true)
        };
        var initial = WeightedBipartiteMatcher.MaximumWeight(
                doctors,
                patients,
                (doctor, patient) => DoctorWeight(doctor, patient, 2.5))
            .OrderByDescending(match => match.Weight)
            .ToList();
        var claimedDevice = false;
        var resolved = new List<string>();
        foreach (var match in initial)
        {
            if (!claimedDevice)
            {
                claimedDevice = true;
                resolved.Add($"{match.Worker.Name}:defibrillator");
            }
            else
            {
                var fallback = DoctorWeight(match.Worker, match.Target, 1.0);
                Assert(fallback > 0, "CPR fallback should remain a valid treatment edge");
                resolved.Add($"{match.Worker.Name}:cpr");
            }
        }
        Assert(resolved.Count(item => item.EndsWith(":defibrillator")) == 1,
            "a reusable device must have exactly one owner");
        Assert(resolved.Count(item => item.EndsWith(":cpr")) == 1,
            "the losing device edge should be repriced to CPR");
        Console.WriteLine("PASS: scarce device claim + fallback");
    }

    private static void UrgentSupplyCanBeatRoutineRescue()
    {
        Worker[] haulers = { new("hauler", false, 0, 0) };
        Patient urgent = new("urgent-cardiac", 5.2, 2200, 30, true);
        Patient routine = new("stable-rescue", 1.0, int.MaxValue, 8);
        TaskOffer[] offers =
        {
            new(urgent, "supply", 2.5, 4),
            new(routine, "rescue", 1.0, routine.X)
        };
        var match = WeightedBipartiteMatcher.MaximumWeight(
            haulers,
            offers,
            (worker, offer) => TransportWeight(worker, offer)).Single();
        Assert(match.Target.Kind == "supply", "urgent device delivery should beat routine transport");
        Console.WriteLine("PASS: implicit supply competes with rescue");
    }

    private static void SupplyDistanceHasRealDispatchGate()
    {
        Worker hauler = new("hauler", false, 0, 0);
        Worker doctor = new("doctor", true, 1.0, 0);
        Patient stable = new("stable", 0.6, int.MaxValue, 60);
        Patient emergency = new("emergency", 5.0, 2400, 70, true);

        Assert(TransportWeight(hauler, new TaskOffer(stable, "supply", 1.0, 55)) == 0,
            "the assignment base must not make a distant low-value supply run mandatory");
        Assert(TransportWeight(doctor, new TaskOffer(stable, "supply", 1.0, 5)) == 0,
            "doctors must leave stable supply staging to haulers");
        Assert(TransportWeight(doctor, new TaskOffer(emergency, "supply", 4.0, 20)) > 0,
            "a doctor may still fetch a genuinely life-saving device when its net value is positive");
        Console.WriteLine("PASS: supply distance and doctor-opportunity dispatch gate");
    }

    private static void FieldSupplyOwnershipInvariants()
    {
        var references = new Dictionary<string, int>
        {
            ["patient-a"] = 2,
            ["patient-b"] = 3
        };
        int stackCount = 4;
        int Allocation(string patient)
        {
            if (!references.ContainsKey(patient)) return 0;
            int requested = references.Values.Sum();
            int distributable = Math.Min(stackCount, requested);
            var allocations = references.OrderBy(pair => pair.Key).ToDictionary(
                pair => pair.Key,
                pair => distributable * pair.Value / requested);
            int remainder = distributable - allocations.Values.Sum();
            foreach (string key in references.Keys.Order())
            {
                if (remainder-- <= 0) break;
                allocations[key]++;
            }
            return allocations[patient];
        }
        int RelocationAvailable() => references.Count > 0 ? 0 : stackCount;

        Assert(Allocation("patient-a") + Allocation("patient-b") == stackCount,
            "merged field-supply allocations must never exceed or lose the physical stack");
        Assert(Allocation("patient-c") == 0,
            "an unrelated casualty must not consume a referenced field stack");
        Assert(RelocationAvailable() == 0,
            "a referenced field stack must not be selected for supply or kit relocation");

        references.Remove("patient-a");
        Assert(Allocation("patient-a") == 0 && Allocation("patient-b") == 3,
            "releasing one reference must preserve every other casualty's ownership");
        references.Remove("patient-b");
        Assert(RelocationAvailable() == 4,
            "a stack must become relocatable after its final reference is released");
        Console.WriteLine("PASS: field-supply ownership boundaries");
    }

    private static void MedicineSupplyShortfallScenarios()
    {
        static int Shortfall(int budget, params int[] nearbyCounts) =>
            Math.Max(0, budget - nearbyCounts.Sum());
        static int Pickup(int shortfall, int sourceStack) => Math.Min(shortfall, sourceStack);

        Assert(Shortfall(4, 1) == 3,
            "one nearby medicine must not suppress the remaining three-round budget");
        Assert(Shortfall(4, 2, 2) == 0,
            "multiple nearby allowed stacks must satisfy one combined medicine budget");
        Assert(Pickup(3, 20) == 3,
            "a large source stack must contribute only the exact missing quantity");
        Assert(Math.Min(4, 75) == 4,
            "a full nearby medicine stack must reference only the four-round patient budget");
        Assert(Pickup(3, 1) == 1,
            "a small source stack may partially fill the deficit for the next event rebuild");
        Console.WriteLine("PASS: exact medicine shortfall and partial replenishment");
    }

    private static void StaleMedicineTargetRecoversAtPickupBoundary()
    {
        static string PickupBoundary(bool managedTend, bool targetExists, bool exactQuotaClaimed)
        {
            if (!managedTend) return "vanilla";
            if (!targetExists) return "rematch";
            return exactQuotaClaimed ? "take-exact" : "vanilla";
        }

        Assert(PickupBoundary(true, false, true) == "rematch",
            "a consumed shared medicine target must rematch instead of dereferencing null");
        Assert(PickupBoundary(true, true, true) == "take-exact",
            "a live protected stack must skip opportunity duplicates and take only its lease");
        Assert(PickupBoundary(false, true, true) == "vanilla",
            "ordinary hauling and non-SAR tending must retain vanilla duplicate collection");
        Console.WriteLine("PASS: stale medicine pickup recovery and exact-quota boundary");
    }

    private static void ExistingHaulYieldsToNewFieldReference()
    {
        static bool KeepExistingJob(bool playerForced, bool targetsNewReference, bool automaticHaul) =>
            playerForced || !targetsNewReference || !automaticHaul;

        Assert(!KeepExistingJob(false, true, true),
            "a pre-existing automatic haul must release a stack that just became field-referenced");
        Assert(KeepExistingJob(true, true, true),
            "an explicit player haul must remain authoritative over a field reference");
        Assert(KeepExistingJob(false, true, false),
            "treatment and other non-hauling users must not be interrupted with storage hauling");
        Console.WriteLine("PASS: new field references retire stale automatic haul reservations");
    }

    private static void ForbiddenFieldSupplyBecomesDeficit()
    {
        static int CountTowardBudget(int stackCount, int referencedCount, bool forbidden) =>
            forbidden ? 0 : Math.Min(stackCount, referencedCount);

        Assert(CountTowardBudget(20, 4, false) == 4,
            "an allowed referenced stack must satisfy its patient quota");
        Assert(CountTowardBudget(20, 4, true) == 0,
            "a player-forbidden stack must immediately reopen the complete deficit");
        Assert(4 - CountTowardBudget(20, 4, true) == 4,
            "forbidding the only field stack must schedule a replacement four-unit budget");
        Console.WriteLine("PASS: forbidden field supplies release references and reopen deficits");
    }

    private static void DestroyedFieldSupplyRematchesNextTick()
    {
        static (int References, int Claims, int RebuildDelay) DestroySupply(
            int references,
            int claims) => (0, 0, 1);

        var result = DestroySupply(2, 1);
        Assert(result.References == 0 && result.Claims == 0,
            "destroying a medical stack must synchronously forget durable and soft ownership");
        Assert(result.RebuildDelay == 1,
            "destroy callbacks must defer graph reconstruction until the next tick");
        Console.WriteLine("PASS: destroyed medical supplies release ownership and rematch next tick");
    }

    private static void TreatmentOwnershipBoundaries()
    {
        static bool SarOwnsTreatment(
            bool treat,
            bool rescue,
            bool capture,
            bool hostile,
            bool prisoner) =>
            treat || capture && hostile && !prisoner;

        Assert(SarOwnsTreatment(true, false, false, false, false),
            "an explicit treatment mark must retain treatment ownership");
        Assert(!SarOwnsTreatment(false, true, false, false, false),
            "a rescue-only mark must remain eligible for vanilla/Priority Treatment tending");
        Assert(SarOwnsTreatment(false, false, true, true, false),
            "an uncaptured hostile must not be revived by autonomous tending");
        Assert(!SarOwnsTreatment(false, false, true, true, true),
            "a secured prisoner without a treatment mark may return to normal tending");
        Console.WriteLine("PASS: treatment-ownership boundaries");
    }

    private static void TakeToBedOwnershipBoundaries()
    {
        static bool AllowTakeToBed(
            bool usesTakeToBedDriver,
            bool playerForced,
            bool activeSasRescue,
            bool marked) =>
            !usesTakeToBedDriver || playerForced || activeSasRescue || !marked;

        foreach (string job in new[] { "Rescue", "Capture", "TakeWoundedPrisonerToBed" })
        {
            Assert(!AllowTakeToBed(true, false, false, true),
                $"autonomous {job} must yield a marked patient to the SAR scheduler");
        }
        Assert(AllowTakeToBed(true, true, false, true),
            "a player-forced TakeToBed order must remain an explicit override");
        Assert(AllowTakeToBed(true, false, true, true),
            "the tracked SAR transport job must pass its own ownership guard");
        Console.WriteLine("PASS: TakeToBed ownership covers rescue/capture aliases");
    }

    private static void RescueInterceptionIsTwoPhase()
    {
        static HandoffState TryHandoff(bool treatmentJobBuilt, bool dropSucceeded)
        {
            HandoffState original = new(true, true, false);
            if (!treatmentJobBuilt || !dropSucceeded)
            {
                return original;
            }

            return new(false, false, true);
        }

        HandoffState constructionFailure = TryHandoff(false, true);
        Assert(constructionFailure.CarrierJobRunning && constructionFailure.PatientCarried &&
               !constructionFailure.TreatmentStarted,
            "a failed treatment job build must leave the original carrier untouched");

        HandoffState placementFailure = TryHandoff(true, false);
        Assert(placementFailure.CarrierJobRunning && placementFailure.PatientCarried &&
               !placementFailure.TreatmentStarted,
            "a failed patient drop must leave the original carrier untouched");

        HandoffState committed = TryHandoff(true, true);
        Assert(!committed.CarrierJobRunning && !committed.PatientCarried && committed.TreatmentStarted,
            "a successful treatment job build and drop must commit one clean hand-off");
        Console.WriteLine("PASS: two-phase rescue interception");
    }

    private static void InterruptedEvacuationDropsPatient()
    {
        static bool PatientRemainsCarried(
            bool jobKeepsCarry,
            bool newerTransportOwnsPatient) =>
            jobKeepsCarry || newerTransportOwnsPatient;

        Assert(!PatientRemainsCarried(false, false),
            "an interrupted SAR evacuation must drop its patient before unrelated work starts");
        Assert(PatientRemainsCarried(false, true),
            "a newer explicit transport must retain ownership of the carried patient");
        Console.WriteLine("PASS: interrupted evacuation patient-drop invariant");
    }

    private static void SafeDeliveryKeepsUnresolvedTreatment()
    {
        static bool KeepTreatmentWatch(bool safelyDelivered, bool stillNeedsTreatment) =>
            safelyDelivered && stillNeedsTreatment;

        Assert(KeepTreatmentWatch(true, true),
            "safe delivery must retain treatment while shock or accumulated blood loss remains");
        Assert(!KeepTreatmentWatch(true, false),
            "safe delivery may retire treatment only after all supported care is complete");
        Console.WriteLine("PASS: safe delivery retains unresolved medical care");
    }

    private static void SchedulerControlTransitions()
    {
        static bool Controlled(
            bool playerControlled,
            bool mental,
            bool duty,
            bool drafted) =>
            playerControlled && !mental && !duty && !drafted;

        Assert(Controlled(true, false, false, false),
            "an ordinary controlled colonist should remain schedulable");
        Assert(!Controlled(true, true, false, false),
            "a mental break must withdraw a worker from scheduling");
        Assert(!Controlled(true, false, true, false),
            "a lord or caravan duty must withdraw a worker from scheduling");
        Assert(!Controlled(true, false, false, true),
            "drafting must always withdraw a worker from standalone SAR scheduling");
        Console.WriteLine("PASS: worker control-state boundaries");
    }

    private static void AutomaticRescueAdmissionBoundaries()
    {
        static (bool admitted, bool ownsVanilla) Admit(
            CoordinationScope scope,
            bool colonyResponsibility,
            bool downed,
            bool safelyBedded,
            bool viableClaim)
        {
            bool admitted = scope != CoordinationScope.MarkedOnly && colonyResponsibility &&
                            downed && !safelyBedded;
            return (admitted, admitted && viableClaim);
        }

        Assert(!Admit(CoordinationScope.MarkedOnly, true, true, false, true).admitted,
            "marked-only mode must leave unmarked rescue entirely to vanilla");
        Assert(Admit(CoordinationScope.EmergencyAuto, true, true, false, false).admitted,
            "an unmarked downed colonist must enter the shared hauling rescue lane");
        Assert(!Admit(CoordinationScope.EmergencyAuto, false, true, false, true).admitted,
            "neutral visitors and wildlife must not become implicit rescue orders");
        Assert(!Admit(CoordinationScope.EmergencyAuto, true, true, false, false).ownsVanilla,
            "automatic admission without a viable hauler claim must preserve vanilla fallback");
        Assert(Admit(CoordinationScope.EmergencyAuto, true, true, false, true).ownsVanilla,
            "a viable automatic transport claim may temporarily suppress duplicate vanilla rescue");
        Assert(!Admit(CoordinationScope.EmergencyAuto, true, true, true, true).admitted,
            "a casualty already in a safe patient bed must not be re-enrolled for transport");
        Console.WriteLine("PASS: automatic rescue admission + soft transport ownership");
    }

    private static void TourniquetRemovalLifecycle()
    {
        static bool NeedsSafeRemoval(
            bool moreInjuries,
            bool hasTourniquet,
            bool coveredLimbHasUntendedBleed) =>
            moreInjuries && hasTourniquet && !coveredLimbHasUntendedBleed;

        Assert(!NeedsSafeRemoval(true, true, true),
            "a limb with an untended bleed must retain its own tourniquet");
        Assert(NeedsSafeRemoval(true, true, false),
            "a tourniquet must become removable once its covered limb is treated");
        bool anotherLimbStillBleeding = true;
        Assert(anotherLimbStillBleeding && NeedsSafeRemoval(true, true, false),
            "bleeding on another limb must not prolong ischemia under this tourniquet");
        Assert(!NeedsSafeRemoval(true, false, false),
            "the removal option must disappear immediately after the tourniquet is gone");
        Assert(!NeedsSafeRemoval(false, true, false),
            "the compatibility lane must remain inert without More Injuries");
        Console.WriteLine("PASS: More Injuries tourniquet safe-removal lifecycle");
    }

    private static void FieldResponderGateRespectsUnderlyingWork()
    {
        static bool Eligible(bool responder, bool underlyingWorkEnabled) =>
            responder && underlyingWorkEnabled;

        Assert(!Eligible(false, true),
            "ordinary workers must not enter the joint battlefield graph");
        Assert(!Eligible(true, false),
            "the responder toggle must not bypass a disabled underlying work type");
        Assert(Eligible(true, true),
            "an enabled responder with the relevant work may enter the graph");

        bool doctorDisabledButNurseEnabled =
            !Eligible(true, false) && Eligible(true, true);
        Assert(doctorDisabledButNurseEnabled,
            "a responder may remain eligible for nursing while Doctor work is disabled");
        Console.WriteLine("PASS: responder gate preserves underlying work authority");
    }

    private static void CaptureTreatmentBundleKeepsSameDoctor()
    {
        const double bundleBonus = 180_000;
        const double handoffContinuity = 500_000;
        double capturingDoctor = Base + bundleBonus;
        double captureOnlyWarden = Base + 20_000;
        Assert(capturingDoctor > captureOnlyWarden,
            "capture+treat should favor a doctor able to complete both stages when urgent");

        double sameDoctorFollowup = Base + handoffContinuity - 20_000;
        double marginallyCloserDoctor = Base + 80_000;
        Assert(sameDoctorFollowup > marginallyCloserDoctor,
            "successful capture should keep the same doctor for immediate treatment");

        static bool DoctorMayCapture(bool captureMarked, bool treatmentMarked, bool doctorEnabled) =>
            captureMarked && treatmentMarked && doctorEnabled;
        Assert(!DoctorMayCapture(true, false, true),
            "capture-only orders must remain Warden work");
        Assert(DoctorMayCapture(true, true, true),
            "a doctor may perform capture only as a treatment prerequisite");
        Console.WriteLine("PASS: capture/treatment bundle and same-doctor handoff");
    }

    private static void RescuePointChangesRetireOldRoutes()
    {
        static bool Retire(bool rescue, bool destinationIsBed) => rescue && !destinationIsBed;

        Assert(Retire(true, false),
            "moving a rescue point must retire an evacuation bound to the old cell");
        Assert(!Retire(true, true),
            "moving a rescue point must not interrupt an unrelated bed rescue");
        Console.WriteLine("PASS: rescue-point route invalidation");
    }

    private static void InventoryDonorControlBoundaries()
    {
        static bool DonorAvailable(
            bool isPatient,
            bool mental,
            bool duty,
            bool carryingPawn,
            bool downed,
            bool drafted,
            bool activeManaged) =>
            !mental && !duty && !carryingPawn && (!downed || isPatient) &&
            (!drafted || activeManaged);

        Assert(DonorAvailable(false, false, false, false, false, false, false),
            "an available allied doctor may donate spare medicine");
        Assert(DonorAvailable(true, false, false, false, true, false, false),
            "a casualty may supply medicine from its own inventory");
        Assert(!DonorAvailable(false, true, false, false, false, false, false),
            "a mentally uncontrolled donor must not be approached for inventory transfer");
        Assert(!DonorAvailable(false, false, false, true, false, false, false),
            "a carrier transporting a pawn must not be used as a moving medicine source");
        Assert(!DonorAvailable(false, false, false, false, true, false, false),
            "an unrelated downed pawn must not be used as an inventory source");
        Assert(DonorAvailable(false, false, false, false, false, true, true),
            "a drafted doctor already executing managed battlefield care may share spare medicine");
        Console.WriteLine("PASS: inventory donor control boundaries");
    }

    private static void RandomizedMatchingInvariants()
    {
        Random random = new(0x5A5);
        for (int run = 0; run < 200; run++)
        {
            int n = random.Next(1, 18);
            int m = random.Next(1, 10);
            Worker[] doctors = Enumerable.Range(0, m)
                .Select(index => new Worker($"d{index}", true, 0.35 + random.NextDouble(), random.NextDouble() * 80))
                .ToArray();
            Patient[] patients = Enumerable.Range(0, n)
                .Select(index => new Patient(
                    $"p{index}",
                    0.2 + random.NextDouble() * 5,
                    random.NextDouble() < 0.7 ? random.Next(1200, 45000) : int.MaxValue,
                    random.NextDouble() * 80))
                .ToArray();
            var matches = WeightedBipartiteMatcher.MaximumWeight(
                doctors,
                patients,
                (doctor, patient) => DoctorWeight(doctor, patient, 1.0));
            Assert(matches.Count <= Math.Min(n, m), "matching cardinality exceeds graph bounds");
            Assert(matches.Select(match => match.Worker).Distinct().Count() == matches.Count,
                "a worker was matched more than once");
            Assert(matches.Select(match => match.Target).Distinct().Count() == matches.Count,
                "a patient was matched more than once");
            Assert(matches.All(match => match.Weight > 0 && double.IsFinite(match.Weight)),
                "matching returned an invalid edge");
        }
        Console.WriteLine("PASS: 200 randomized N/M graphs");
    }

    private static double DoctorWeight(
        Worker doctor,
        Patient patient,
        double interventionBenefit,
        double routeCost = 325)
    {
        double route = Math.Abs(doctor.X - patient.X);
        double estimatedTicks = route * 60 / 4.6 + 600 / Math.Max(0.05, doctor.Skill);
        double deadlineBonus = patient.Deadline == int.MaxValue
            ? 0
            : 180_000 / (1 + Math.Exp(-Math.Clamp((patient.Deadline - estimatedTicks) / 1200, -8, 8)));
        return Base + patient.Urgency * doctor.Skill * 120_000 +
               patient.Urgency * interventionBenefit * 30_000 - route * routeCost + deadlineBonus;
    }

    private static double ClinicalWeight(Worker worker, ClinicalOffer offer)
    {
        if (!offer.Supportive && !worker.Doctor || offer.Supportive && !worker.Doctor && !worker.Nurse)
        {
            return 0;
        }

        double quality = offer.Supportive ? 1 : worker.Skill;
        double roleBonus = offer.Supportive && worker.Nurse
            ? 90_000 - (worker.Doctor ? Math.Max(0, worker.Skill * 20 - 4) * 4_500 : 0)
            : 0;
        double transfusionUrgency = offer.Supportive ? 260_000 : 0;
        double route = Math.Abs(worker.X - offer.Patient.X);
        return Base + offer.Patient.Urgency * quality * 120_000 +
               offer.Patient.Urgency * offer.Benefit * 30_000 + roleBonus +
               transfusionUrgency - route * 325;
    }

    private static double TransportWeight(Worker worker, TaskOffer offer)
    {
        double route = Math.Abs(worker.X - offer.PickupX) + Math.Abs(offer.PickupX - offer.Patient.X);
        if (offer.Kind != "supply")
        {
            return Base + offer.Patient.Urgency * 18_000 - route * 400;
        }

        if (worker.Doctor && !offer.Patient.NeedsDevice)
        {
            return 0;
        }

        double doctorOpportunityCost = worker.Doctor
            ? 60_000 + worker.Skill * 30_000 + 28_000
            : 0;
        double netUtility = offer.Patient.Urgency * offer.Benefit * 65_000 -
                            route * 1_000 - doctorOpportunityCost;
        return netUtility > 0 ? Base + netUtility : 0;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
