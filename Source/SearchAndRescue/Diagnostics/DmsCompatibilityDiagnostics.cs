using System;
using System.Collections.Generic;
using LudeonTK;
using RimWorld;
using Verse;
using Verse.AI;

namespace SearchAndRescue
{
    /// <summary>
    /// Persistent, saveable fixture for interactive DMS compatibility checks. It deliberately
    /// leaves every successfully generated pawn on the map so a tester can save, reload and
    /// exercise the normal UI and scheduler after this action returns.
    /// </summary>
    internal static class DmsCompatibilityDiagnostics
    {
        private const string Prefix = "[SAR DMS fixture] ";
        private static Pawn repairWorker;
        private static Pawn repairPatient;
        private static float initialRepairDamage;
        private static Pawn responderDoctor;
        private static Pawn responderPatient;
        private static Hediff responderBruise;

        [DebugAction("Search and Rescue", "Build persistent DMS compatibility fixture",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void BuildPersistentFixture()
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                Log.Error(Prefix + "FAIL map: no current map");
                return;
            }

            Pawn mechanitor = TrySpawn(map, "human mechanitor", () =>
                GenerateQualifiedMechanitor(map));

            MechanitorControlGroup group = mechanitor?.mechanitor?.controlGroups.Count > 0
                ? mechanitor.mechanitor.controlGroups[0]
                : null;
            group?.SetWorkMode(MechWorkModeDefOf.Work);

            var mechs = new List<Pawn>();
            TryAdd(mechs, SpawnMech(map, mechanitor, group, "DMS core Lady", "DMS_Mech_Lady"));
            TryAdd(mechs, SpawnMech(map, mechanitor, group, "Synthetic Maiden", "DMS_Mech_Maiden"));
            TryAdd(mechs, SpawnMech(map, mechanitor, group, "Joint Operations Tinker", "DMS_Mech_Tinker"));

            Pawn casualty = TrySpawn(map, "human casualty", () =>
            {
                Pawn pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                GenSpawn.Spawn(pawn, FixtureCell(map, map.Center, 10), map);
                pawn.Name = new NameTriple("SAR", "SAR DMS Human Casualty", "Fixture");
                AddBruise(pawn, 6f);
                HealthUtility.TryAnesthetize(pawn);
                return pawn;
            });

            foreach (Pawn mech in mechs)
                DiagnoseMech(mech, map);
            if (casualty != null)
                Log.Message(Prefix + "human casualty=" + casualty.ThingID +
                    "; downed=" + casualty.Downed + "; mechanicalPatient=" +
                    MechanicalCare.IsPatient(casualty));

            DiagnoseJobOwnership("RepairMech");
            DiagnoseJobOwnership("FFF_RepairMech_Overseer");
            DiagnoseJobOwnership("Tinker_RepairAutomatroid");

            StartManagedLadyRepair(map, mechanitor, mechs.Find(pawn =>
                pawn?.def?.defName == "DMS_Mech_Lady"));
            DiagnoseHostileMechCaptureBoundary(map);

            Log.Message(Prefix + "COMPLETE: generated pawns were retained; save/reload and " +
                "interactive job checks may proceed. successfulMechs=" + mechs.Count +
                "; mechanitor=" + (mechanitor != null) + "; humanCasualty=" + (casualty != null));
        }

        [DebugAction("Search and Rescue", "Finish persistent DMS repair check",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void FinishPersistentRepairCheck()
        {
            try
            {
                if (repairWorker == null || repairPatient == null || repairPatient.Destroyed)
                    throw new InvalidOperationException("run the persistent DMS fixture first in this game session");

                float remaining = MechanicalCare.Damage(repairPatient);
                if (repairPatient.Dead || remaining >= initialRepairDamage)
                    throw new InvalidOperationException("no RepairMech progress; initial=" +
                        initialRepairDamage + "; remaining=" + remaining +
                        "; currentJob=" + repairWorker.CurJobDef?.defName);

                Log.Message(Prefix + "PASS managed RepairMech reduced Lady damage " +
                    initialRepairDamage + " -> " + remaining +
                    "; pawns and designations retained");
            }
            catch (Exception error)
            {
                Log.Error(Prefix + "FAIL finish managed Lady repair: " + error);
            }
        }

        [DebugAction("Search and Rescue", "Retry DMS managed repair",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void RetryManagedRepair()
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                Log.Error(Prefix + "FAIL retry: no current map");
                return;
            }

            try
            {
                Pawn lady = FindFixturePawn(map, "DMS_Mech_Lady");
                if (lady == null)
                    throw new InvalidOperationException("no retained DMS_Mech_Lady; run Build once");
                CompMechRepairable repairable = lady.TryGetComp<CompMechRepairable>();
                if (repairable != null)
                    repairable.autoRepair = true;
                if (!MechanicalCare.NeedsRepair(lady))
                    AddInjury(lady, 10f);

                Pawn mechanitor = map.mapPawns.FreeColonistsSpawned.Find(pawn =>
                    pawn.Name?.ToStringShort == "SAR DMS Mechanitor" &&
                    MechanicalCare.CanRepairWork(pawn));
                if (mechanitor == null)
                {
                    mechanitor = GenerateQualifiedMechanitor(map);
                    Log.Message(Prefix + "retry replaced ineligible mechanitor with " +
                        mechanitor.ThingID + "; repairPriority=" + MechanicalCare.WorkPriority(mechanitor));
                }

                RebindFixtureMechs(map, mechanitor, lady);
                StartManagedLadyRepair(map, mechanitor, lady);
            }
            catch (Exception error)
            {
                Log.Error(Prefix + "FAIL retry managed repair: " + error);
            }
        }

        [DebugAction("Search and Rescue", "Start DMS responder treatment",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void StartResponderTreatment()
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                Log.Error(Prefix + "FAIL responder start: no current map");
                return;
            }

            try
            {
                Pawn maiden = FindFixturePawn(map, "DMS_Mech_Maiden");
                Pawn lady = FindFixturePawn(map, "DMS_Mech_Lady");
                Pawn patient = map.mapPawns.FreeColonistsSpawned.Find(pawn =>
                    pawn.Name?.ToStringShort == "SAR DMS Human Casualty");
                if (maiden == null || lady == null || patient == null)
                    throw new InvalidOperationException("retained Lady, Maiden or human casualty missing; run Build once");

                SearchAndRescueCoordinator coordinator = map.GetComponent<SearchAndRescueCoordinator>();
                var candidates = new List<Pawn> { maiden, lady };
                foreach (Pawn candidate in candidates)
                {
                    Pawn mechanitor = candidate.GetOverseer();
                    if (mechanitor?.mechanitor == null ||
                        !mechanitor.mechanitor.ControlledPawns.Contains(candidate) ||
                        candidate.GetMechWorkMode() != MechWorkModeDefOf.Work ||
                        !MechWorkerCompatibility.SupportsNativeWorkType(candidate, WorkTypeDefOf.Doctor))
                        continue;

                    candidate.workSettings = candidate.workSettings ?? new Pawn_WorkSettings(candidate);
                    candidate.workSettings.EnableAndInitializeIfNotAlreadyInitialized();
                    candidate.workSettings.SetPriority(WorkTypeDefOf.Doctor, 3);
                    coordinator.SetFieldResponder(candidate, true);
                    Thing medicine = ThingMaker.MakeThing(ThingDefOf.MedicineIndustrial);
                    medicine.stackCount = 2;
                    if (!candidate.inventory.innerContainer.TryAdd(medicine))
                        throw new InvalidOperationException("could not give medicine to " + candidate.def.defName);
                }

                foreach (Hediff hediff in patient.health.hediffSet.hediffs
                             .FindAll(candidate => candidate.TendableNow()))
                    patient.health.RemoveHediff(hediff);
                Hediff bruise = AddBruise(patient, 6f);
                if (!bruise.TendableNow())
                    throw new InvalidOperationException("stable Bruise is not tendable");
                if (patient.playerSettings != null)
                    patient.playerSettings.medCare = MedicalCareCategory.Best;

                Designation existing = map.designationManager.DesignationOn(
                    patient,
                    SearchAndRescueDefOf.SAR_Treat);
                if (existing == null)
                    map.designationManager.AddDesignation(
                        new Designation(patient, SearchAndRescueDefOf.SAR_Treat));
                coordinator.NotifyStageDesignationAdded(patient, SearchAndRescueStage.Treat);

                Pawn winner = null;
                Job job = null;
                foreach (Pawn candidate in candidates)
                {
                    if (!Compatibility.CanPerformTreatmentWork(candidate) ||
                        !MechWorkerCompatibility.CanRunSchedulerNow(candidate))
                        continue;
                    candidate.jobs.StartJob(
                        JobMaker.MakeJob(JobDefOf.Wait_Wander, 30),
                        JobCondition.InterruptForced);
                    coordinator.NotifyWorkerUndrafting(candidate);
                    Job issued = coordinator.TryIssueJob(
                        candidate,
                        SearchAndRescueStage.FollowupTreat,
                        Compatibility.RescueProviderFor(candidate));
                    if (issued?.def == JobDefOf.TendPatient && issued.targetA.Pawn == patient)
                    {
                        winner = candidate;
                        job = issued;
                        break;
                    }
                }
                if (job?.def != JobDefOf.TendPatient || job.targetA.Pawn != patient)
                    throw new InvalidOperationException("neither valid DMS doctor won FollowupTreat; scheduler=" +
                        coordinator.DebugDescribeScheduler());

                responderDoctor = winner;
                responderPatient = patient;
                responderBruise = bruise;
                winner.jobs.StartJob(job, JobCondition.InterruptForced);
                Log.Message(Prefix + "START DMS responder TendPatient; winner=" + winner.def.defName +
                    "; doctor=" + winner.ThingID +
                    "; patient=" + patient.ThingID + "; medicine=" +
                    job.targetB.Thing?.LabelCap + "; run 'Finish DMS responder treatment' after completion");
            }
            catch (Exception error)
            {
                Log.Error(Prefix + "FAIL start DMS responder treatment: " + error);
            }
        }

        [DebugAction("Search and Rescue", "Finish DMS responder treatment",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void FinishResponderTreatment()
        {
            try
            {
                if (responderDoctor == null || responderPatient == null || responderBruise == null)
                    throw new InvalidOperationException("run Start DMS responder treatment first");
                bool removed = !responderPatient.health.hediffSet.hediffs.Contains(responderBruise);
                bool tended = !removed && responderBruise.IsTended();
                if ((!tended && !removed) || responderDoctor.CurJobDef == JobDefOf.TendPatient)
                    throw new InvalidOperationException("Bruise not finished; tended=" + tended +
                        "; removed=" + removed + "; currentJob=" + responderDoctor.CurJobDef?.defName);

                Log.Message(Prefix + "PASS DMS responder completed native TendPatient; winner=" +
                    responderDoctor.def.defName + "; wound=" +
                    (tended ? "tended" : "resolved") + "; pawns retained");
            }
            catch (Exception error)
            {
                Log.Error(Prefix + "FAIL finish DMS responder treatment: " + error);
            }
        }

        private static Pawn SpawnMech(
            Map map,
            Pawn mechanitor,
            MechanitorControlGroup group,
            string label,
            string pawnKindDefName)
        {
            return TrySpawn(map, label, () =>
            {
                PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(pawnKindDefName);
                if (kind == null)
                    throw new InvalidOperationException("missing PawnKindDef " + pawnKindDefName);

                Pawn pawn = PawnGenerator.GeneratePawn(kind, Faction.OfPlayer);
                GenSpawn.Spawn(pawn, FixtureCell(map, map.Center, 10), map);
                pawn.Name = new NameSingle("SAR " + label);
                AddInjury(pawn, 10f);

                CompMechRepairable repairable = pawn.TryGetComp<CompMechRepairable>();
                if (repairable != null)
                    repairable.autoRepair = true;

                if (mechanitor != null && group != null)
                {
                    TryBindWithinBandwidth(mechanitor, group, pawn);
                }

                pawn.workSettings = pawn.workSettings ?? new Pawn_WorkSettings(pawn);
                pawn.workSettings.EnableAndInitializeIfNotAlreadyInitialized();
                if (MechWorkerCompatibility.SupportsNativeWorkType(pawn, WorkTypeDefOf.Doctor))
                    pawn.workSettings.SetPriority(WorkTypeDefOf.Doctor, 3);
                if (MechWorkerCompatibility.SupportsNativeWorkType(pawn, WorkTypeDefOf.Hauling))
                    pawn.workSettings.SetPriority(WorkTypeDefOf.Hauling, 3);

                map.GetComponent<SearchAndRescueCoordinator>()?.SetFieldResponder(pawn, true);
                return pawn;
            });
        }

        private static void DiagnoseMech(Pawn pawn, Map map)
        {
            try
            {
                bool doctor = MechWorkerCompatibility.SupportsNativeWorkType(pawn, WorkTypeDefOf.Doctor);
                bool hauling = MechWorkerCompatibility.SupportsNativeWorkType(pawn, WorkTypeDefOf.Hauling);
                bool captureAccepted = new Designator_Capture().CanDesignateThing(pawn).Accepted;
                SearchAndRescueCoordinator coordinator = map.GetComponent<SearchAndRescueCoordinator>();
                CompMechRepairable repairable = pawn.TryGetComp<CompMechRepairable>();
                Log.Message(Prefix + pawn.KindLabel + "=" + pawn.ThingID +
                    "; race=" + pawn.def.defName +
                    "; flesh=" + pawn.RaceProps.FleshType?.defName +
                    "; isMechanoid=" + pawn.RaceProps.IsMechanoid +
                    "; repairComp=" + (repairable != null) +
                    "; autoRepair=" + (repairable?.autoRepair == true) +
                    "; mechanicalPatient=" + MechanicalCare.IsPatient(pawn) +
                    "; needsRepair=" + MechanicalCare.NeedsRepair(pawn) +
                    "; captureAccepted=" + captureAccepted +
                    "; doctor=" + doctor +
                    "; hauling=" + hauling +
                    "; fieldResponder=" + (coordinator?.IsFieldResponder(pawn) == true) +
                    "; workMode=" + pawn.GetMechWorkMode()?.defName +
                    "; schedulerOperational=" + MechWorkerCompatibility.CanRunSchedulerNow(pawn));
            }
            catch (Exception error)
            {
                Log.Error(Prefix + "FAIL diagnostics for " + pawn?.ThingID + ": " + error);
            }
        }

        private static void DiagnoseJobOwnership(string defName)
        {
            try
            {
                JobDef job = DefDatabase<JobDef>.GetNamedSilentFail(defName);
                if (job == null)
                {
                    Log.Error(Prefix + "FAIL ownership: missing JobDef " + defName);
                    return;
                }

                PatientJobRole roles = CompatibilityRegistry.RolesFor(job);
                bool treatment = (roles & PatientJobRole.Treatment) != 0;
                Log.Message(Prefix + "ownership " + defName + "=" + roles +
                    "; treatmentRegistered=" + treatment);
                if (!treatment)
                    Log.Error(Prefix + "FAIL ownership: " + defName + " lacks Treatment role");
            }
            catch (Exception error)
            {
                Log.Error(Prefix + "FAIL ownership " + defName + ": " + error);
            }
        }

        private static void StartManagedLadyRepair(Map map, Pawn mechanitor, Pawn lady)
        {
            try
            {
                if (mechanitor == null || lady == null)
                    throw new InvalidOperationException("mechanitor or DMS_Mech_Lady was not generated");
                if (lady.GetOverseer() != mechanitor || lady.GetMechControlGroup() == null ||
                    !mechanitor.mechanitor.ControlledPawns.Contains(lady))
                    throw new InvalidOperationException("Lady is not controlled; overseer=" +
                        lady.GetOverseer()?.ThingID + "; group=" + lady.GetMechControlGroup()?.Index +
                        "; controlled=" + mechanitor.mechanitor.ControlledPawns.Contains(lady));

                SearchAndRescueCoordinator coordinator = map.GetComponent<SearchAndRescueCoordinator>();
                if (coordinator == null)
                    throw new InvalidOperationException("missing SAR coordinator");

                Designation existing = map.designationManager.DesignationOn(
                    lady,
                    SearchAndRescueDefOf.SAR_Treat);
                if (existing == null)
                    map.designationManager.AddDesignation(
                        new Designation(lady, SearchAndRescueDefOf.SAR_Treat));
                coordinator.NotifyStageDesignationAdded(lady, SearchAndRescueStage.Treat);

                mechanitor.jobs.StartJob(
                    JobMaker.MakeJob(JobDefOf.Wait_Wander, 30),
                    JobCondition.InterruptForced);
                coordinator.NotifyWorkerUndrafting(mechanitor);
                Job repair = coordinator.TryIssueJob(
                    mechanitor,
                    SearchAndRescueStage.Treat,
                    RescueWorkProvider.None);
                if (repair?.def != JobDefOf.RepairMech || repair.targetA.Pawn != lady)
                    throw new InvalidOperationException("coordinator did not issue RepairMech for Lady; got=" +
                        repair?.def?.defName + "; native=" + MechanicalCare.CanRepair(mechanitor, lady) +
                        "; scheduler=" + coordinator.DebugDescribeScheduler());

                repairWorker = mechanitor;
                repairPatient = lady;
                initialRepairDamage = MechanicalCare.Damage(lady);
                mechanitor.jobs.StartJob(repair, JobCondition.InterruptForced);
                Log.Message(Prefix + "START managed RepairMech; worker=" + mechanitor.ThingID +
                    "; patient=" + lady.ThingID + "; initialDamage=" + initialRepairDamage +
                    "; SAR_Treat=true; run 'Finish persistent DMS repair check' after several ticks");
            }
            catch (Exception error)
            {
                Log.Error(Prefix + "FAIL start managed Lady repair: " + error);
            }
        }

        private static void DiagnoseHostileMechCaptureBoundary(Map map)
        {
            Pawn hostile = null;
            try
            {
                PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("DMS_Mech_Lady");
                Faction faction = Find.FactionManager.FirstFactionOfDef(FactionDefOf.Mechanoid);
                if (kind == null || faction == null)
                    throw new InvalidOperationException("missing Lady PawnKindDef or mechanoid faction");

                hostile = PawnGenerator.GeneratePawn(kind, faction);
                GenSpawn.Spawn(hostile, FixtureCell(map, map.Center, 12), map);
                hostile.health.AddHediff(HediffDefOf.Anesthetic);
                if (!hostile.Downed || hostile.Dead || !hostile.HostileTo(Faction.OfPlayer))
                    throw new InvalidOperationException("hostile fixture state invalid; downed=" +
                        hostile.Downed + "; dead=" + hostile.Dead + "; faction=" + hostile.Faction?.Name);

                bool eligibility = TargetEligibility.CanBeCaptured(hostile);
                bool designator = new Designator_Capture().CanDesignateThing(hostile).Accepted;
                if (eligibility || designator)
                    throw new InvalidOperationException("hostile downed mech entered prisoner Capture; " +
                        "eligibility=" + eligibility + "; designator=" + designator);

                Log.Message(Prefix + "PASS hostile downed DMS mech excluded from prisoner Capture; " +
                    "race=" + hostile.def.defName + "; flesh=" + hostile.RaceProps.FleshType?.defName);
            }
            catch (Exception error)
            {
                Log.Error(Prefix + "FAIL hostile mech capture boundary: " + error);
            }
            finally
            {
                if (hostile != null && !hostile.Destroyed)
                    hostile.Destroy(DestroyMode.Vanish);
            }
        }

        private static Pawn TrySpawn(Map map, string label, Func<Pawn> spawn)
        {
            try
            {
                Pawn pawn = spawn();
                Log.Message(Prefix + "spawned " + label + ": " + pawn.ThingID);
                return pawn;
            }
            catch (Exception error)
            {
                Log.Error(Prefix + "FAIL spawn " + label + ": " + error);
                return null;
            }
        }

        private static Pawn GenerateQualifiedMechanitor(Map map)
        {
            const int maxAttempts = 40;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                Pawn pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                pawn.health.AddHediff(
                    HediffDefOf.MechlinkImplant,
                    pawn.health.hediffSet.GetBrain());
                pawn.mechanitor = pawn.mechanitor ?? new Pawn_MechanitorTracker(pawn);
                if (pawn.mechanitor.controlGroups.Count == 0)
                    pawn.mechanitor.controlGroups.Add(new MechanitorControlGroup(pawn.mechanitor));
                SetPriority(pawn, SearchAndRescueDefOf.SAR_FieldRescue, 3);
                SetPriority(pawn, WorkTypeDefOf.Smithing, 3);

                if (MechanicalCare.CanRepairWork(pawn))
                {
                    GenSpawn.Spawn(pawn, FixtureCell(map, map.Center, 8), map);
                    pawn.Name = new NameTriple("SAR", "SAR DMS Mechanitor", "Fixture");
                    Log.Message(Prefix + "qualified mechanitor after attempts=" + attempt +
                        "; repairPriority=" + MechanicalCare.WorkPriority(pawn));
                    return pawn;
                }

                pawn.Discard(silentlyRemoveReferences: true);
            }

            throw new InvalidOperationException("could not generate a mechanitor with enabled " +
                "Smithing and Field Rescue after " + maxAttempts + " attempts");
        }

        private static Pawn FindFixturePawn(Map map, string raceDefName)
        {
            Pawn preferred = null;
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn?.def?.defName != raceDefName || pawn.Faction != Faction.OfPlayer || pawn.Dead)
                    continue;
                if (MechanicalCare.NeedsRepair(pawn))
                    return pawn;
                preferred = preferred ?? pawn;
            }
            return preferred;
        }

        private static void RebindFixtureMechs(Map map, Pawn mechanitor, Pawn lady)
        {
            MechanitorControlGroup group = mechanitor.mechanitor.controlGroups[0];
            group.SetWorkMode(MechWorkModeDefOf.Work);
            var fixtureMechs = new List<Pawn>();
            foreach (Pawn mech in map.mapPawns.AllPawnsSpawned)
            {
                if (mech?.Faction != Faction.OfPlayer || mech.Dead ||
                    (mech.def.defName != "DMS_Mech_Lady" &&
                     mech.def.defName != "DMS_Mech_Maiden" &&
                     mech.def.defName != "DMS_Mech_Tinker"))
                    continue;

                fixtureMechs.Add(mech);
                Pawn oldOverseer = mech.GetOverseer();
                if (oldOverseer != null)
                    oldOverseer.relations.TryRemoveDirectRelation(PawnRelationDefOf.Overseer, mech);
            }

            foreach (MechanitorControlGroup existingGroup in mechanitor.mechanitor.controlGroups)
                foreach (Pawn mech in fixtureMechs)
                    existingGroup.TryUnassign(mech);

            // Bind the repair patient first, then add only fixtures that fit. This prevents
            // the high-bandwidth Tinker from pushing Lady into RequiresBandwidth.
            TryBindWithinBandwidth(mechanitor, group, lady);
            foreach (Pawn mech in fixtureMechs)
                if (mech != lady)
                    TryBindWithinBandwidth(mechanitor, group, mech);
            group.SetWorkMode(MechWorkModeDefOf.Work);
            mechanitor.mechanitor.Notify_BandwidthChanged();
            Log.Message(Prefix + "retry rebound fixture mechs; LadyOverseer=" +
                lady.GetOverseer()?.ThingID + "; LadyGroup=" + lady.GetMechControlGroup()?.Index +
                "; bandwidth=" + mechanitor.mechanitor.UsedBandwidth + "/" +
                mechanitor.mechanitor.TotalBandwidth);
        }

        private static bool TryBindWithinBandwidth(
            Pawn mechanitor,
            MechanitorControlGroup group,
            Pawn mech)
        {
            float cost = mech.GetStatValue(StatDefOf.BandwidthCost);
            if (mechanitor.mechanitor.UsedBandwidth + cost > mechanitor.mechanitor.TotalBandwidth)
            {
                Log.Warning(Prefix + "left " + mech.def.defName +
                    " uncontrolled to preserve bandwidth; cost=" + cost +
                    "; used/total=" + mechanitor.mechanitor.UsedBandwidth + "/" +
                    mechanitor.mechanitor.TotalBandwidth);
                return false;
            }

            mechanitor.relations.AddDirectRelation(PawnRelationDefOf.Overseer, mech);
            group.Assign(mech);
            group.SetWorkMode(MechWorkModeDefOf.Work);
            mechanitor.mechanitor.Notify_BandwidthChanged();
            return true;
        }

        private static void TryAdd(List<Pawn> pawns, Pawn pawn)
        {
            if (pawn != null)
                pawns.Add(pawn);
        }

        private static void AddInjury(Pawn pawn, float severity)
        {
            Hediff injury = HediffMaker.MakeHediff(
                HediffDefOf.Cut,
                pawn,
                pawn.RaceProps.body.corePart);
            injury.Severity = severity;
            pawn.health.AddHediff(injury);
        }

        private static Hediff AddBruise(Pawn pawn, float severity)
        {
            Hediff bruise = HediffMaker.MakeHediff(
                DefDatabase<HediffDef>.GetNamed("Bruise"),
                pawn,
                pawn.RaceProps.body.corePart);
            bruise.Severity = severity;
            pawn.health.AddHediff(bruise);
            return bruise;
        }

        private static void SetPriority(Pawn pawn, WorkTypeDef workType, int priority)
        {
            pawn.workSettings = pawn.workSettings ?? new Pawn_WorkSettings(pawn);
            pawn.workSettings.EnableAndInitializeIfNotAlreadyInitialized();
            if (!pawn.WorkTypeIsDisabled(workType))
                pawn.workSettings.SetPriority(workType, priority);
        }

        private static IntVec3 FixtureCell(Map map, IntVec3 center, int radius)
        {
            return CellFinder.RandomClosewalkCellNear(center, map, radius);
        }
    }
}
