using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using Verse;
using Verse.AI;

namespace SearchAndRescue
{
    internal static partial class CasevacAndBoundaryDiagnostics
    {
        private static Pawn casualty;
        private static Building_Bed bed;
        private static List<Pawn> team;
        private static bool sawTeam;
        private static int coMovingSteps;
        private static IntVec3 lastCarrierPosition = IntVec3.Invalid;
        private static Pawn ordinaryCarrier;
        private static Pawn emergencyDoctor;
        private static Pawn emergencyPatient;
        private static Hediff emergencyWound;
        private static Hediff routineWound;

        [DebugAction("Search and Rescue", "Start emergency-only doctor fixture", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void StartEmergencyDoctor()
        {
            Map map = Find.CurrentMap;
            foreach (Pawn p in map.mapPawns.AllPawnsSpawned.ToList())
                if (p.workSettings != null) Compatibility.SetWorkPriorityForMigration(p, SearchAndRescueDefOf.SAR_FieldRescue, 0);
            emergencyDoctor = Spawn(map, "Emergency Only", map.Center);
            emergencyPatient = Spawn(map, "Emergency Patient", map.Center + new IntVec3(2, 0, 0));
            emergencyPatient.health.AddHediff(HediffDefOf.Anesthetic);
            Compatibility.SetWorkPriorityForMigration(emergencyDoctor, SearchAndRescueDefOf.SAR_FieldRescue, 1);
            var setter = AccessTools.Method(AccessTools.TypeByName("WorkTab.Pawn_Extensions"), "SetPriority",
                new[] { typeof(Pawn), typeof(WorkGiverDef), typeof(int), typeof(List<int>) });
            if (setter == null) throw new InvalidOperationException("This fixture requires Work Tab.");
            setter.Invoke(null, new object[] { emergencyDoctor, SearchAndRescueDefOf.SAR_EmergencyMedicalCare, 1, Enumerable.Range(0,24).ToList() });
            emergencyDoctor.skills.GetSkill(SkillDefOf.Medicine).Level = 15;
            emergencyWound = HediffMaker.MakeHediff(HediffDefOf.Cut, emergencyPatient, emergencyPatient.RaceProps.body.corePart);
            routineWound = HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed("Bruise"), emergencyPatient, emergencyPatient.RaceProps.body.corePart);
            emergencyWound.Severity = routineWound.Severity = 3;
            emergencyPatient.health.AddHediff(emergencyWound);
            emergencyPatient.health.AddHediff(routineWound);
            AllowFixtureTend(emergencyPatient, emergencyWound);
            AllowFixtureTend(emergencyPatient, routineWound);
            Thing medicine = ThingMaker.MakeThing(ThingDefOf.MedicineIndustrial);
            medicine.stackCount = 5;
            emergencyDoctor.inventory.innerContainer.TryAdd(medicine);
            map.designationManager.AddDesignation(new Designation(emergencyPatient, SearchAndRescueDefOf.SAR_Treat));
            map.GetComponent<SearchAndRescueCoordinator>().NotifyWorkerUndrafting(emergencyDoctor);
            Log.Message("[SAR next features] START emergency-only doctor fixture; advance 4000 ticks.");
        }

        [DebugAction("Search and Rescue", "Check emergency-only doctor fixture", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void CheckEmergencyDoctor()
        {
            Check(emergencyPatient?.Dead == false && emergencyWound.IsTended(), "Work Tab emergency-only doctor autonomously treats bleeding wound");
            Check(!routineWound.IsTended(), "autonomous doctor leaves minor wound for care location");
            Check(!Compatibility.CanPerformFollowupTreatmentWork(emergencyDoctor), "autonomous emergency doctor keeps routine work disabled");
        }

        [DebugAction("Search and Rescue", "Run emergency coverage checks", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Coverage()
        {
            foreach (string name in new[] { "Cpr", "EmergencyMedicine" })
            {
                ResearchProjectDef research = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(name);
                if (research != null) Find.ResearchManager.FinishProject(research, false, null, false);
            }
            // Test real loaded Defs, including inherited stages/comps and third-party patches.
            CoverageCase("WoundInfection", 0.1f, true);
            CoverageCase("Flu", 0.1f, true);
            CoverageCase("Malaria", 0.1f, true);
            CoverageCase("Crack", 2f, false);
            CoverageCase("Bruise", 2f, false);
            CoverageCase("Burn", 2f, false);
            CoverageCase("HypovolemicShock", 0.1f, true);
            CoverageCase("HemorrhagicStroke", 0.1f, true);
            CoverageCase("CardiacArrest", 0.1f, false);
            CoverageCase("ChokingOnBlood", 0.1f, false);
            CoverageCase("LungCollapse", 0.1f, false);
            CoverageCase("GangreneWet", 0.1f, false);
            CoverageCase("Acidosis", 0.7f, false);
            CoverageCase("Coagulopathy", 0.7f, false);
            CoverageCase("Fracture", 0.1f, false);
            CoverageCase("OrganHypoxia", 1f, false);
            CoverageCase("BrainDamage_Hypoxia", 1f, false);
            CoverageCase("SpontaneousBleeding", 2f, true);
            CoverageCase("SpallFragmentCut", 2f, true);
            CoverageCase("BoneFragmentLaceration", 2f, true);
            CoverageCase("ClinicalDeathNoHeartbeat", 0.1f, false);
            CoverageCase("ClinicalDeathAsphyxiation", 0.1f, false);
            CoverageCase("LiverFailure", 0.1f, false);
            CoverageCase("KidneyFailure", 0.1f, false);
            if (Compatibility.UsesMoreInjuries) CheckNeckTourniquet();

            Pawn patient = Spawn(Find.CurrentMap, "Boundary Location", Find.CurrentMap.Center);
            try
            {
                Hediff crack = HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed("Bruise"), patient, patient.RaceProps.body.corePart);
                crack.Severity = 2;
                patient.health.AddHediff(crack);
                AllowFixtureTend(patient, crack);
                Check(!FieldTreatmentBoundary.AtCareLocation(patient) && !FieldTreatmentBoundary.Tendable(patient).Any(),
                    "minor field injury has no routine treatment candidates");
                Find.CurrentMap.designationManager.AddDesignation(new Designation(patient.Position, SearchAndRescueDefOf.SAR_RescuePoint));
                Check(FieldTreatmentBoundary.AtCareLocation(patient) && FieldTreatmentBoundary.Tendable(patient).Contains(crack),
                    "designated rescue location reopens routine tending");
                Find.CurrentMap.designationManager.DesignationAt(patient.Position, SearchAndRescueDefOf.SAR_RescuePoint)?.Delete();
            }
            finally { patient.Destroy(); }
        }

        private static void CheckNeckTourniquet()
        {
            Pawn patient = Spawn(Find.CurrentMap, "Boundary Tourniquet", Find.CurrentMap.Center);
            try
            {
                BodyPartRecord neck = patient.health.hediffSet.GetNotMissingParts().First(p => p.def.defName == "Neck");
                Hediff cut = HediffMaker.MakeHediff(HediffDefOf.Cut, patient, neck);
                cut.Severity = 2;
                patient.health.AddHediff(cut);
                Hediff tourniquet = HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed("TourniquetApplied"), patient, neck);
                tourniquet.Severity = 0.1f;
                patient.health.AddHediff(tourniquet);
                patient.health.AddHediff(DefDatabase<HediffDef>.GetNamed("ChokingOnTourniquet"));
                Check(Compatibility.MoreInjuriesTourniquetForRemoval(patient) == tourniquet,
                    "choking neck tourniquet is removable even with an untended neck wound");
                Check(Compatibility.HasFieldTreatableEmergency(patient), "tourniquet choking admits urgent field care");
            }
            finally { patient.Destroy(); }
        }

        private static void AllowFixtureTend(Pawn patient, Hediff h)
        {
            // CYM defaults can explicitly forbid small wounds. Enable this one test
            // wound so that the boundary, rather than CYM NoCare, is what is tested.
            var setter = AccessTools.Method(AccessTools.TypeByName("ChooseYourMedicine.DrawButton"), "MakeNewHediffEntry");
            setter?.Invoke(null, new object[] { h, patient, MedicalCareCategory.Best, false });
            Check(h.TendableNow(), "fixture minor wound is permitted by native medical policy");
        }

        private static void CoverageCase(string name, float severity, bool expected)
        {
            HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(name);
            if (def == null) { Log.Message("[SAR next features] SKIP unloaded condition: " + name); return; }
            Pawn patient = Spawn(Find.CurrentMap, "Boundary " + name, Find.CurrentMap.Center);
            try
            {
                Hediff h = HediffMaker.MakeHediff(def, patient, name == "GangreneWet"
                    ? patient.health.hediffSet.GetNotMissingParts().First(part => part.def.defName == "Arm")
                    : def.injuryProps != null ? patient.RaceProps.body.corePart : null);
                h.Severity = severity;
                patient.health.AddHediff(h);
                if (Compatibility.UsesMoreInjuries && (name == "CardiacArrest" || name == "ChokingOnBlood"))
                    Check(Compatibility.HasFieldTreatableEmergency(patient), name + " admits specialized field intervention despite being untendable");
                if (name == "LungCollapse" || name == "GangreneWet")
                    Check(Compatibility.RequiresUrgentSurgery(patient), name + " prioritizes urgent surgical evacuation");
                if (name == "ClinicalDeathNoHeartbeat" || name == "ClinicalDeathAsphyxiation" || name == "LiverFailure" || name == "KidneyFailure")
                    Check(Compatibility.MedicalEmergencyUrgency(patient) > 0, name + " retains evacuation urgency");
                Check(FieldTreatmentBoundary.IsEmergency(h) == expected, name + " emergency-tend=" + expected +
                    " (tendable=" + h.TendableNow() + ", bleeding=" + h.BleedRate + ", danger=" + h.CurStage?.lifeThreatening + ")");
            }
            finally { patient.Destroy(); }
        }

        [DebugAction("Search and Rescue", "Start CASEVAC upgrade fixture", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void StartUpgrade()
        {
            Start();
            foreach (Pawn worker in team)
                Compatibility.SetWorkPriorityForMigration(worker, DefDatabase<WorkTypeDef>.GetNamed("CP_CasevacRescue"), 0);
            ordinaryCarrier = team.OrderByDescending(p => p.TicksPerMoveCardinal).First();
            ordinaryCarrier.Position = CellFinder.RandomClosewalkCellNear(casualty.Position, casualty.Map, 1);
            Compatibility.SetWorkPriorityForMigration(ordinaryCarrier, WorkTypeDefOf.Hauling, 1);
            Log.Message("[SAR next features] Upgrade fixture: advance 300 ticks, then run Execute CASEVAC upgrade checks.");
        }

        [DebugAction("Search and Rescue", "Execute CASEVAC upgrade checks", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Upgrade()
        {
            if (ordinaryCarrier.carryTracker.CarriedThing != casualty || ordinaryCarrier.CurJobDef != JobDefOf.Rescue)
            {
                Log.Message("[SAR next features] NOT READY: carrier has not picked up patient; advance more ticks.");
                return;
            }
            Check(ordinaryCarrier.carryTracker.CarriedThing == casualty && ordinaryCarrier.CurJobDef == JobDefOf.Rescue,
                "upgrade fixture uses a real ordinary rescue already carrying the patient");
            Pawn helper = team.Where(p => p != ordinaryCarrier).OrderBy(p => p.TicksPerMoveCardinal).First();
            helper.Position = CellFinder.RandomClosewalkCellNear(ordinaryCarrier.Position, ordinaryCarrier.Map, 1);
            Compatibility.SetWorkPriorityForMigration(helper, DefDatabase<WorkTypeDef>.GetNamed("CP_CasevacRescue"), 1);
            var coordinator = helper.Map.GetComponent<SearchAndRescueCoordinator>();
            Job original = ordinaryCarrier.CurJob;
            original.playerForced = true;
            Check(coordinator.TryJoinOrUpgradeCasevac(helper) == null && ordinaryCarrier.CurJob == original,
                "CASEVAC upgrade preserves manual transport");
            original.playerForced = false;
            Job upgraded = coordinator.TryJoinOrUpgradeCasevac(helper);
            Check(Compatibility.IsCasevac(upgraded) && upgraded.targetB.Thing == bed, "ordinary rescue upgrades to native CASEVAC with the same bed");
            Check(casualty.Spawned && !casualty.Dead && ordinaryCarrier.carryTracker.CarriedThing == null,
                "handoff places patient safely before retiring the old carrier");
            Check(!Compatibility.CanUseCasevac(ordinaryCarrier), "upgrade does not enable CASEVAC on the old carrier");
            if (upgraded != null) helper.jobs.StartJob(upgraded, JobCondition.InterruptForced);
            foreach (Pawn p in team.Where(p => p != ordinaryCarrier))
                Compatibility.SetWorkPriorityForMigration(p, DefDatabase<WorkTypeDef>.GetNamed("CP_CasevacRescue"), 1);
        }

        [DebugAction("Search and Rescue", "Run field treatment boundary checks", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Boundary()
        {
            Map map = Find.CurrentMap;
            Pawn doctor = Spawn(map, "Boundary Doctor", map.Center);
            Pawn patient = Spawn(map, "Boundary Patient", map.Center + new IntVec3(2, 0, 0));
            try
            {
                var coordinator = map.GetComponent<SearchAndRescueCoordinator>();
                Compatibility.SetWorkPriorityForMigration(doctor, WorkTypeDefOf.Doctor, 1);
                Compatibility.SetWorkPriorityForMigration(doctor, SearchAndRescueDefOf.SAR_FieldRescue, 1);
                Hediff crack = HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed("Bruise"), patient, patient.RaceProps.body.corePart);
                crack.Severity = 2;
                patient.health.AddHediff(crack);
                AllowFixtureTend(patient, crack);
                Check(!FieldTreatmentBoundary.IsEmergency(crack), "nonbleeding minor wound is not emergency care");
                Check(MedicalCarePlan.Build(patient, Find.TickManager.TicksGame).EssentialMedicineRounds == 0,
                    "field minor wound does not budget medicine");
                Hediff cut = HediffMaker.MakeHediff(HediffDefOf.Cut, patient, patient.RaceProps.body.corePart);
                cut.Severity = 2;
                patient.health.AddHediff(cut);
                Check(FieldTreatmentBoundary.IsEmergency(cut), "bleeding cut is emergency care");
                Job job = JobMaker.MakeJob(JobDefOf.TendPatient, patient);
                var claims = (ActiveJobClaims)AccessTools.Field(typeof(SearchAndRescueCoordinator), "activeClaims").GetValue(coordinator);
                claims.Register(new ActiveAssignment(doctor, patient, job, SearchAndRescueStage.Treat,
                    IntVec3.Invalid, Find.TickManager.TicksGame, 2, 1, 0, CareOrigin.ManualTreatment, 0, 0));
                doctor.jobs.curJob = job;
                var medicine = (Medicine)ThingMaker.MakeThing(ThingDefOf.MedicineIndustrial);
                medicine.stackCount = 2;
                TendUtility.DoTend(doctor, patient, medicine);
                Check(!cut.TendableNow() && crack.TendableNow(), "actual medicated emergency tend leaves minor wound untreated");
                Check(medicine.stackCount == 1, "emergency round consumes one medicine");
                TendUtility.DoTend(doctor, patient, medicine);
                Check(crack.TendableNow() && medicine.stackCount == 1, "stable repeat cannot treat minor wound or consume medicine");
                Check(FieldTreatmentBoundary.EmergencyPatient == null, "tending scope is restored");
                doctor.jobs.curJob = null;
                claims.ReleasePrimary(patient);
                // Confirm the ordinary native treatment path is not globally restricted.
                TendUtility.DoTend(doctor, patient, medicine);
                Check(!crack.TendableNow(), "ordinary doctor still treats non-emergency wounds");
                if (Compatibility.UsesWorkTab)
                {
                    var setter = AccessTools.Method(AccessTools.TypeByName("WorkTab.Pawn_Extensions"), "SetPriority",
                        new[] { typeof(Pawn), typeof(WorkGiverDef), typeof(int), typeof(List<int>) });
                    var getter = AccessTools.Method(AccessTools.TypeByName("WorkTab.Pawn_Extensions"), "GetPriority",
                        new[] { typeof(Pawn), typeof(WorkGiverDef), typeof(int) });
                    Compatibility.SetWorkPriorityForMigration(doctor, WorkTypeDefOf.Doctor, 0);
                    setter.Invoke(null, new object[] { doctor, SearchAndRescueDefOf.SAR_EmergencyMedicalCare, 1, Enumerable.Range(0,24).ToList() });
                    Check(Compatibility.CanPerformTreatmentWork(doctor), "Work Tab emergency-only doctor can perform SAR emergency care");
                    Check(!Compatibility.CanPerformFollowupTreatmentWork(doctor), "emergency-only doctor cannot perform routine SAR care");
                    Check((int)getter.Invoke(null, new object[] { doctor, DefDatabase<WorkGiverDef>.GetNamed("DoBillsMedicalHumanOperation"), -1 }) == 0,
                        "emergency-only assignment leaves surgery disabled");
                    setter.Invoke(null, new object[] { doctor, SearchAndRescueDefOf.SAR_EmergencyMedicalCare, 0, Enumerable.Range(0,24).ToList() });
                    setter.Invoke(null, new object[] { doctor, DefDatabase<WorkGiverDef>.GetNamed("DoctorTendToHumanlikes"), 1, Enumerable.Range(0,24).ToList() });
                    Check(!Compatibility.CanPerformTreatmentWork(doctor) && Compatibility.CanPerformFollowupTreatmentWork(doctor),
                        "routine-only doctor respects disabled emergency subtask");
                }
            }
            finally { doctor.jobs.curJob = null; doctor.Destroy(); patient.Destroy(); }
        }

        [DebugAction("Search and Rescue", "Start automatic CASEVAC fixture", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Start()
        {
            Map map = Find.CurrentMap;
            if (casualty?.MapHeld == map) casualty.Destroy();
            if (bed?.Map == map) bed.Destroy();
            foreach (Pawn p in map.mapPawns.AllPawnsSpawned.ToList())
                if (p.workSettings != null)
                {
                    foreach (WorkTypeDef work in DefDatabase<WorkTypeDef>.AllDefsListForReading)
                        Compatibility.SetWorkPriorityForMigration(p, work, 0);
                    p.jobs.StopAll();
                }
            team = new List<Pawn>();
            for (int i = 0; i < 3; i++)
            {
                Pawn worker = Spawn(map, "CASEVAC " + i, map.Center + new IntVec3(-5 - i, 0, 0));
                Compatibility.SetWorkPriorityForMigration(worker, SearchAndRescueDefOf.SAR_FieldRescue, 1);
                Compatibility.SetWorkPriorityForMigration(worker, DefDatabase<WorkTypeDef>.GetNamed("CP_CasevacRescue"), 1);
                team.Add(worker);
            }
            casualty = Spawn(map, "CASEVAC Patient", map.Center);
            casualty.health.AddHediff(HediffDefOf.Anesthetic);
            Hediff wound = HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed("Bruise"), casualty, casualty.RaceProps.body.corePart);
            wound.Severity = 2;
            casualty.health.AddHediff(wound);
            AllowFixtureTend(casualty, wound);
            casualty.playerSettings.medCare = MedicalCareCategory.Best;
            bed = (Building_Bed)ThingMaker.MakeThing(ThingDefOf.SleepingSpot);
            bed.SetFaction(Faction.OfPlayer);
            GenSpawn.Spawn(bed, CellFinder.RandomClosewalkCellNear(map.Center + new IntVec3(55, 0, 0), map, 3), map);
            bed.Medical = true;
            map.designationManager.AddDesignation(new Designation(casualty, SearchAndRescueDefOf.SAR_Rescue));
            var coordinator = map.GetComponent<SearchAndRescueCoordinator>();
            foreach (Pawn worker in team) coordinator.NotifyWorkerUndrafting(worker);
            sawTeam = false;
            coMovingSteps = 0;
            lastCarrierPosition = IntVec3.Invalid;
            Log.Message("[SAR next features] START CASEVAC: patient=" + casualty.ThingID + "; workers=" + string.Join(",", team.Select(p => p.ThingID)));
        }

        internal static void Observe(Map map)
        {
            if (casualty?.MapHeld != map || team == null) return;
            Pawn carrier = (casualty.ParentHolder as Pawn_CarryTracker)?.pawn;
            if (invalidateBedDuringCarry && carrier != null)
            {
                invalidateBedDuringCarry = false;
                bed.Destroy();
                Log.Message("[SAR next features] PASS: destination bed destroyed during active carrying");
            }
            if (carrier != null && Compatibility.IsCasevac(carrier.CurJob) && carrier.Position != lastCarrierPosition)
            {
                if (team.Any(p => p != carrier && Compatibility.IsCasevac(p.CurJob) &&
                    p.CurJob.targetA.Pawn == casualty && p.pather.MovingNow && p.Position.DistanceToSquared(carrier.Position) <= 2) &&
                    carrier.TicksPerMoveCardinal < Compatibility.CasevacBaseTicksPerMove(carrier) - 0.1f)
                    coMovingSteps++;
                lastCarrierPosition = carrier.Position;
            }
            int count = team.Count(p => Compatibility.IsCasevac(p.CurJob) && p.CurJob.targetA.Pawn == casualty);
            if (count >= 2 && !sawTeam)
            {
                sawTeam = true;
                Log.Message("[SAR next features] PASS: automatic CASEVAC team formed with " + count + " members");
            }
        }

        private static bool invalidateBedDuringCarry;
        private static IntVec3 interruptedRescuePoint;

        [DebugAction("Search and Rescue", "Start CASEVAC lost-bed fixture", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void StartLostBed()
        {
            Start();
            Map map = Find.CurrentMap;
            interruptedRescuePoint = CellFinder.RandomClosewalkCellNear(map.Center + new IntVec3(35, 0, 0), map, 2);
            map.designationManager.AddDesignation(new Designation(interruptedRescuePoint, SearchAndRescueDefOf.SAR_RescuePoint));
            map.GetComponent<SearchAndRescueCoordinator>().NotifyRescuePointChanged();
            invalidateBedDuringCarry = true;
        }

        [DebugAction("Search and Rescue", "Check CASEVAC lost-bed fixture", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void CheckLostBed()
        {
            Check(!invalidateBedDuringCarry && bed.Destroyed, "lost-bed fixture exercised destruction during carrying");
            Check(casualty?.Dead == false && casualty.Spawned &&
                RescueDestinationPlanner.RescueCompleted(casualty, interruptedRescuePoint, null),
                "patient reaches rescue point after CASEVAC destination disappears");
            Check(team.All(p => !Compatibility.IsCasevac(p.CurJob)), "lost-bed team exits native CASEVAC");
            Check(Find.CurrentMap.reservationManager.ReservationsReadOnly.All(r => r.Job?.def != null),
                "lost-bed cancellation leaves no pooled job reservations");
        }

        [DebugAction("Search and Rescue", "Check automatic CASEVAC fixture", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void CheckCasevac()
        {
            foreach (Pawn p in team)
            {
                Log.Message("[SAR next features] worker=" + p.LabelShort + " provider=" + Compatibility.RescueProviderFor(p) +
                    " priority=" + Compatibility.CasevacPriority(p) + " field=" + Compatibility.FieldRescueWorkPriority(p) +
                    " job=" + p.CurJobDef + " ready=" + WorkerEligibility.CanPerformStage(p, SearchAndRescueStage.Rescue));
                var coordinator = p.Map.GetComponent<SearchAndRescueCoordinator>();
                Log.Message("[SAR next features] operational=" + WorkerEligibility.WorkerOperational(p, p.Map) +
                    " reserve=" + p.CanReserve(casualty) + " reach=" + p.CanReach(casualty, PathEndMode.Touch, Danger.Deadly) +
                    " bed=" + Compatibility.FindBestRescueBed(casualty, p) +
                    " edge=" + AccessTools.Method(typeof(SearchAndRescueCoordinator), "EdgeWeight").Invoke(coordinator,
                        new object[] { p, casualty, SearchAndRescueStage.Rescue }) +
                    " targetReady=" + AccessTools.Method(typeof(SearchAndRescueCoordinator), "TargetReadyForStage").Invoke(coordinator,
                        new object[] { casualty, SearchAndRescueStage.Rescue, Find.TickManager.TicksGame }));
            }
            Check(sawTeam, "native helpers joined automatically");
            Check(coMovingSteps >= 3, "helpers physically follow and boost the carrier across cells (steps=" + coMovingSteps + ")");
            Check(casualty?.Dead == false && casualty.CurrentBed() == bed, "CASEVAC patient delivered alive to bed");
            Check(team.All(p => !Compatibility.IsCasevac(p.CurJob)), "team exits after delivery");
            Check(!Find.CurrentMap.reservationManager.ReservationsReadOnly.Any(r => team.Contains(r.Claimant) && r.Target == bed),
                "delivered patient bed has no remaining CASEVAC team reservation");
            Check(Find.CurrentMap.reservationManager.ReservationsReadOnly.All(r => r.Job?.def != null),
                "all live reservations still refer to valid jobs after delivery");
        }

        private static Pawn Spawn(Map map, string name, IntVec3 cell)
        {
            for (int i = 0; i < 100; i++)
            {
                Pawn pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                if (pawn.WorkTagIsDisabled(WorkTags.Caring) || pawn.Downed) { pawn.Destroy(); continue; }
                pawn.Name = new NameTriple("SAR", name, "Test");
                GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(cell, map, 2), map);
                foreach (WorkTypeDef work in DefDatabase<WorkTypeDef>.AllDefsListForReading)
                    Compatibility.SetWorkPriorityForMigration(pawn, work, 0);
                return pawn;
            }
            throw new InvalidOperationException("Could not generate a viable fixture pawn.");
        }
        private static void Check(bool value, string label) => Log.Message("[SAR next features] " + (value ? "PASS: " : "FAIL: ") + label);
    }
}
