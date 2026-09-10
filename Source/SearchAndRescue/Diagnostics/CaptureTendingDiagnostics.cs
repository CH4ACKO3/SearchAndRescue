using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SearchAndRescue
{
    // A no-graphics process has a zero-width colonist bar. GrimWorks queries its
    // UI layout during startup; provide this disposable map's roster without layout.
    [HarmonyPatch(typeof(ColonistBar), nameof(ColonistBar.GetColonistsInOrder))]
    internal static class CaptureProbeHeadlessRoster
    {
        private static bool Prefix(ref List<Pawn> __result)
        {
            if (!(GenCommandLine.CommandLineArgPassed("sar-capture-probe") ||
                  GenCommandLine.CommandLineArgPassed("sar-aur-probe") ||
                  GenCommandLine.CommandLineArgPassed("sar-combination-probe")) ||
                !GenCommandLine.CommandLineArgPassed("quicktest") ||
                !GenCommandLine.CommandLineArgPassed("nographics")) return true;
            __result = Find.CurrentMap?.mapPawns.FreeColonistsSpawned.ToList() ?? new List<Pawn>();
            return false;
        }
    }

    // Runs only in an explicitly requested disposable quicktest process.
    [HarmonyPatch(typeof(Root), nameof(Root.Update))]
    internal static class CaptureTendingDiagnostics
    {
        private static bool ran;
        private static float nextStatus;
        private static readonly List<string> results = new List<string>();
        private static void Check(bool ok, string label) => results.Add((ok ? "PASS: " : "FAIL: ") + label);
        private static void Postfix()
        {
            if (!ran && GenCommandLine.CommandLineArgPassed("sar-capture-probe") && Time.realtimeSinceStartup > nextStatus)
            {
                nextStatus = Time.realtimeSinceStartup + 15;
                Log.Message("[SAR capture startup] state=" + Current.ProgramState + " longEvent=" +
                    LongEventHandler.AnyEventNowOrWaiting + " map=" + (Find.CurrentMap != null) +
                    " init=" + (Find.GameInitData != null) + " ticks=" + Find.TickManager?.TicksGame);
            }
            if (ran || !GenCommandLine.CommandLineArgPassed("sar-capture-probe") ||
                !GenCommandLine.CommandLineArgPassed("quicktest") || LongEventHandler.AnyEventNowOrWaiting ||
                Find.CurrentMap == null || Current.ProgramState != ProgramState.Playing ||
                Find.GameInitData != null || Find.TickManager.TicksGame < 60) return;
            ran = true;
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            try { Run(); } catch (Exception ex) { results.Add("FAIL: " + ex); }
            File.WriteAllLines(Path.Combine(GenFilePaths.SaveDataFolderPath, "capture-results.txt"), results);
            foreach (string line in results) Log.Message("[SAR capture] " + line);
            Application.Quit();
        }

        private static void Run()
        {
            Log.Message("[SAR capture] Creating disposable fixture");
            Map map = Find.CurrentMap;
            IntVec3 near = map.mapPawns.FreeColonistsSpawned.First().Position;
            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned.ToList()) pawn.drafter.Drafted = true;
            Pawn doctor;
            do { doctor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer); }
            while (doctor.WorkTagIsDisabled(WorkTags.Caring) || doctor.WorkTagIsDisabled(WorkTags.ManualDumb));
            GenSpawn.Spawn(doctor, CellFinder.RandomClosewalkCellNear(near, map, 3), map);
            doctor.workSettings.EnableAndInitializeIfNotAlreadyInitialized();
            doctor.workSettings.SetPriority(WorkTypeDefOf.Doctor, 1);
            doctor.workSettings.SetPriority(WorkTypeDefOf.Hauling, 1);
            doctor.workSettings.SetPriority(SearchAndRescueDefOf.SAR_FieldRescue, 1);
            WorkTypeDef initialCasevac = DefDatabase<WorkTypeDef>.GetNamedSilentFail("CP_CasevacRescue");
            if (initialCasevac != null) doctor.workSettings.SetPriority(initialCasevac, 0);
            WorkGiverDef nativeTend = DefDatabase<WorkGiverDef>.GetNamed("DoctorTendToHumanlikes");
            doctor.workSettings.SetPriority(nativeTend.workType, 1);
            if (GrimWorksCompatibility.Available)
            {
                doctor.workSettings.SetPriority(DefDatabase<WorkTypeDef>.GetNamed("GW_WorkManager_Rescue"), 1);
                doctor.workSettings.SetPriority(WorkTypeDefOf.Hauling, 0);
            }
            doctor.skills.GetSkill(SkillDefOf.Medicine).Level = 15;
            Pawn patient = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
            GenSpawn.Spawn(patient, CellFinder.RandomClosewalkCellNear(near, map, 4), map);
            patient.SetFaction(Find.FactionManager.FirstFactionOfDef(FactionDefOf.Pirate));
            patient.health.AddHediff(HediffDefOf.Anesthetic);
            patient.playerSettings.medCare = MedicalCareCategory.Best;
            Hediff cut = AddWound(patient, HediffDefOf.Cut);
            Hediff bruise = AddWound(patient, DefDatabase<HediffDef>.GetNamed("Bruise"));
            IntVec3 room = GenRadial.RadialCellsAround(near, 30, true).First(cell =>
                CellRect.CenteredOn(cell, 3).All(c => c.InBounds(map) && c.Standable(map) && c.GetEdifice(map) == null) &&
                doctor.CanReach(cell, PathEndMode.OnCell, Danger.Deadly) && cell.DistanceToSquared(patient.Position) > 64);
            foreach (IntVec3 cell in CellRect.CenteredOn(room, 3).EdgeCells)
            {
                Thing wall = ThingMaker.MakeThing(cell == room + new IntVec3(0, 0, -3)
                    ? ThingDefOf.Door : ThingDefOf.Wall, ThingDefOf.WoodLog);
                GenSpawn.Spawn(wall, cell, map);
                wall.SetFaction(Faction.OfPlayer);
            }
            var bed = (Building_Bed)ThingMaker.MakeThing(ThingDefOf.Bed, ThingDefOf.WoodLog);
            GenSpawn.Spawn(bed, room, map);
            bed.SetFaction(Faction.OfPlayer);
            bed.Medical = true;
            bed.ForPrisoners = true;
            map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
            bed.GetRoom().Notify_RoomShapeChanged();
            Check(bed.Position.IsInPrisonCell(map), "fixture has an enclosed prison cell");
            Thing medicine = ThingMaker.MakeThing(ThingDefOf.MedicineIndustrial);
            medicine.stackCount = 25;
            GenSpawn.Spawn(medicine, doctor.Position, map);
            foreach (DesignationDef def in new[] { SearchAndRescueDefOf.SAR_Capture,
                SearchAndRescueDefOf.SAR_Treat, SearchAndRescueDefOf.SAR_Rescue })
                map.designationManager.AddDesignation(new Designation(patient, def));
            var coordinator = map.GetComponent<SearchAndRescueCoordinator>();
            Log.Message("[SAR capture] Starting clinical ticks");
            coordinator.NotifyWorkerUndrafting(doctor);
            bool fieldTended = false, delivered = false, followup = false;
            int errors = 0;
            void OnLog(string message, string stack, LogType level)
            {
                if (level == LogType.Error || level == LogType.Exception || level == LogType.Assert)
                { errors++; results.Add("ERROR: " + message); }
            }
            Application.logMessageReceived += OnLog;
            try
            {
                string previous = null;
                for (int i = 0; i < 9000 && !patient.Dead; i++)
                {
                    Find.TickManager.DoSingleTick();
                    bool inBed = RescueDestinationPlanner.IsInSafePatientBed(patient);
                    if (!inBed && cut.IsTended() && !bruise.IsTended()) fieldTended = true;
                    delivered |= inBed;
                    followup |= SearchAndRescueJobContext.IsActive(doctor, doctor.CurJob, SearchAndRescueStage.FollowupTreat);
                    string state = doctor.CurJobDef?.defName + "/" + doctor.jobs.curDriver?.CurToilIndex +
                        " prisoner=" + patient.IsPrisonerOfColony + " bed=" + inBed +
                        " cut=" + cut.IsTended() + " bruise=" + bruise.IsTended();
                    if (state != previous) { results.Add(i + ": " + state); Log.Message("[SAR capture progress] " + i + ": " + state); previous = state; }
                    if (delivered && bruise.IsTended()) break;
                }
                Check(patient.IsPrisonerOfColony, "marked hostile was captured by actual job");
                Check(fieldTended, "bleeding treated in field while minor wound was deferred");
                Check(delivered, "actual rescue delivered prisoner into safe bed");
                Check(followup, "scheduler assigned followup treatment");
                Check(bruise.IsTended(), "bed followup actually treated minor wound without cancelling marks");
                if (delivered) CheckBedStages(doctor, patient, coordinator);
                CheckGrimWorks(doctor, patient);
                CheckHaulerSupplies(doctor, patient, coordinator);
                Check(errors == 0, "clinical execution logged no errors");
            }
            finally { Application.logMessageReceived -= OnLog; }
        }

        private static void CheckBedStages(Pawn doctor, Pawn patient, SearchAndRescueCoordinator coordinator)
        {
            doctor.jobs.EndCurrentJob(JobCondition.InterruptForced, false);
            var claims = (ActiveJobClaims)AccessTools.Field(typeof(SearchAndRescueCoordinator), "activeClaims").GetValue(coordinator);
            Hediff minor = AddWound(patient, DefDatabase<HediffDef>.GetNamed("Bruise"));
            var medicine = (Medicine)ThingMaker.MakeThing(ThingDefOf.MedicineIndustrial);
            medicine.stackCount = 2;
            Job job = JobMaker.MakeJob(JobDefOf.TendPatient, patient);
            doctor.jobs.curJob = job;
            try
            {
                foreach (SearchAndRescueStage stage in new[] { SearchAndRescueStage.Treat, SearchAndRescueStage.FollowupTreat })
                {
                    claims.Register(new ActiveAssignment(doctor, patient, job, stage, patient.Position,
                        Find.TickManager.TicksGame, 1, 1f, 1f, CareOrigin.ManualTreatment, 0f, 0f));
                    Check(FieldTreatmentBoundary.RestrictRound(doctor, patient) == (stage == SearchAndRescueStage.Treat),
                        stage + " keeps its intended emergency/routine boundary in bed");
                    TendUtility.DoTend(doctor, patient, medicine);
                    Check(minor.IsTended() == (stage == SearchAndRescueStage.FollowupTreat),
                        stage + " actual treatment respects bed stage permission");
                    Check(medicine.stackCount == (stage == SearchAndRescueStage.Treat ? 2 : 1),
                        stage + " medicine consumed only for an effective round");
                    Check(FieldTreatmentBoundary.EmergencyPatient == null, stage + " restores treatment scope");
                }
            }
            finally { doctor.jobs.curJob = null; claims.ReleasePrimary(patient); }
        }

        private static void CheckHaulerSupplies(Pawn doctor, Pawn patient, SearchAndRescueCoordinator coordinator)
        {
            Type type = AccessTools.TypeByName("HaulersDream.InventorySurplus");
            if (type == null) return;
            var method = AccessTools.Method(type, "SurplusOf", new[] { typeof(Pawn), typeof(Thing) });
            Thing medicine = ThingMaker.MakeThing(ThingDefOf.MedicineIndustrial);
            medicine.stackCount = 10;
            doctor.inventory.innerContainer.TryAdd(medicine);
            int Surplus() => (int)method.Invoke(null, new object[] { doctor, medicine });
            int before = Surplus();
            Check(before > 0, "Haulers Dream considers unclaimed inventory medicine surplus");
            var ledger = (MedicalResourceLedger)AccessTools.Field(typeof(SearchAndRescueCoordinator), "medicalResources").GetValue(coordinator);
            Check(ledger.TryClaim(medicine, doctor, patient, 1, false, Find.TickManager.TicksGame + 500,
                MedicalResourceAccess.Treatment), "fixture reserves inventory medicine for SAR");
            Check(Surplus() == 0, "Haulers Dream does not unload SAR-claimed medicine");
            ledger.ReleaseWorker(doctor);
            Check(Surplus() == before, "Haulers Dream resumes normal unloading after claim release");
        }

        private static void CheckGrimWorks(Pawn doctor, Pawn patient)
        {
            Type registry = AccessTools.TypeByName("GW_WorkManager.WorkGiverPriorityRegistry");
            if (registry == null) return;
            var setter = AccessTools.Method(registry, "SetPriority", new[] { typeof(Pawn), typeof(WorkGiverDef), typeof(int) });
            void Set(WorkGiverDef def, int priority) => setter.Invoke(null, new object[] { doctor, def, priority });
            doctor.workSettings.SetPriority(WorkTypeDefOf.Doctor, 1);
            doctor.workSettings.SetPriority(SearchAndRescueDefOf.SAR_FieldRescue, 1);
            Set(SearchAndRescueDefOf.SAR_EmergencyMedicalCare, 0);
            Check(!Compatibility.CanPerformTreatmentWork(doctor), "GrimWorks disabled emergency child blocks SAR emergency work");
            Set(SearchAndRescueDefOf.SAR_EmergencyMedicalCare, 7);
            Check(Compatibility.CanPerformTreatmentWork(doctor), "GrimWorks priority 7 emergency child is enabled");
            Set(SearchAndRescueDefOf.SAR_TreatMarked, 0);
            Check(!Compatibility.CanPerformTreatmentWork(doctor), "GrimWorks disabled SAR treatment child blocks assignment");
            Set(SearchAndRescueDefOf.SAR_TreatMarked, 1);
            WorkGiverDef tend = DefDatabase<WorkGiverDef>.GetNamed("DoctorTendToHumanlikes");
            Set(tend, 0);
            Check(!Compatibility.RoutinePatientWorkAllowed(doctor, patient), "GrimWorks emergency-only doctor cannot perform routine human tending");
            Set(tend, 8);
            Check(Compatibility.RoutinePatientWorkAllowed(doctor, patient), "GrimWorks priority 8 routine tending is enabled");
            if (Compatibility.UsesWorkTab)
            {
                var wtSetter = AccessTools.Method(AccessTools.TypeByName("WorkTab.Pawn_Extensions"), "SetPriority",
                    new[] { typeof(Pawn), typeof(WorkGiverDef), typeof(int), typeof(List<int>) });
                wtSetter.Invoke(null, new object[] { doctor, tend, 0, Enumerable.Range(0, 24).ToList() });
                Check(!Compatibility.RoutinePatientWorkAllowed(doctor, patient),
                    "GrimWorks cannot revive routine child disabled in Work Tab");
                wtSetter.Invoke(null, new object[] { doctor, tend, 1, Enumerable.Range(0, 24).ToList() });
                Set(tend, 0);
                Check(!Compatibility.RoutinePatientWorkAllowed(doctor, patient),
                    "Work Tab cannot revive routine child disabled in GrimWorks");
                Set(tend, 8);
                var wtTypeSetter = AccessTools.Method(AccessTools.TypeByName("WorkTab.Pawn_Extensions"), "SetPriority",
                    new[] { typeof(Pawn), typeof(WorkTypeDef), typeof(int), typeof(List<int>) });
                wtTypeSetter.Invoke(null, new object[] { doctor, WorkTypeDefOf.Hauling, 0, Enumerable.Range(0, 24).ToList() });
                Check(!Compatibility.CanPerformSupplyWork(doctor),
                    "disabled hauling parent blocks SAR supply with both work managers");
            }
            WorkGiverDef surgery = DefDatabase<WorkGiverDef>.GetNamed("DoBillsMedicalHumanOperation");
            Check(surgery.workType.defName == "GW_WorkManager_Surgeon", "GrimWorks surgery remains in its separate work type");
            WorkGiverDef rescue = DefDatabase<WorkGiverDef>.GetNamed("DoctorRescue");
            Check(Compatibility.RescueProviderFor(doctor) == RescueWorkProvider.GrimWorks,
                "GrimWorks Rescue supplies SAR transport with Hauling disabled");
            Set(rescue, 0);
            Check(!Compatibility.CanPerformRescueWork(doctor), "disabled GrimWorks rescue child revokes transport");
            Set(rescue, 6);
            Check(Compatibility.RescueWorkPriority(doctor) == 6, "GrimWorks rescue priority 6 is preserved");
            RescueWorkMode previousMode = SearchAndRescueMod.Settings.RescueWorkMode;
            WorkTypeDef nursing = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Nursing");
            try
            {
                if (nursing != null) doctor.workSettings.SetPriority(nursing, 0);
                SearchAndRescueMod.Settings.RescueWorkMode = RescueWorkMode.NursingOnly;
                Check(!Compatibility.CanPerformRescueWork(doctor), "GrimWorks does not bypass NursingOnly mode");
                if (nursing != null)
                {
                    doctor.workSettings.SetPriority(nursing, 1);
                    Check(Compatibility.RescueProviderFor(doctor) == RescueWorkProvider.Nursing,
                        "Nurse Job remains the provider in NursingOnly mode");
                    SearchAndRescueMod.Settings.RescueWorkMode = RescueWorkMode.NursingPreferred;
                    Check(Compatibility.RescueProviderFor(doctor) == RescueWorkProvider.Nursing,
                        "NursingPreferred takes precedence over GrimWorks rescue");
                    doctor.workSettings.SetPriority(nursing, 0);
                }
            }
            finally { SearchAndRescueMod.Settings.RescueWorkMode = previousMode; }
            WorkTypeDef casevac = DefDatabase<WorkTypeDef>.GetNamedSilentFail("CP_CasevacRescue");
            if (casevac != null)
            {
                doctor.workSettings.SetPriority(casevac, 1);
                Check(Compatibility.RescueProviderFor(doctor) == RescueWorkProvider.Casevac,
                    "CASEVAC remains preferred over ordinary GrimWorks rescue");
                Set(DefDatabase<WorkGiverDef>.GetNamed("CP_CasevacRescue"), 0);
                Check(!Compatibility.CanUseCasevac(doctor), "GrimWorks disabled CASEVAC child blocks CASEVAC");
                doctor.workSettings.SetPriority(casevac, 0);
            }
            doctor.workSettings.SetPriority(WorkTypeDefOf.Doctor, 0);
            doctor.workSettings.SetPriority(DefDatabase<WorkTypeDef>.GetNamed("GW_WorkManager_Nurse"), 1);
            Check(!Compatibility.CanPerformTreatmentWork(doctor), "GrimWorks Nurse alone does not grant medical tending");
        }

        private static Hediff AddWound(Pawn patient, HediffDef def)
        {
            Hediff h = HediffMaker.MakeHediff(def, patient, patient.RaceProps.body.corePart);
            h.Severity = 3f;
            patient.health.AddHediff(h);
            return h;
        }
    }
}
