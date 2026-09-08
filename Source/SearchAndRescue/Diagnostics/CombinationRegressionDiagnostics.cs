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
    // Only runs in an explicitly requested disposable -quicktest process. Never acts
    // on an ordinary player session, changes saved settings, or saves the test map.
    [HarmonyPatch(typeof(Root), nameof(Root.Update))]
    internal static class CombinationRegressionDiagnostics
    {
        private static bool ran;
        private static readonly List<string> results = new List<string>();
        private static void Postfix()
        {
            if (ran || !GenCommandLine.CommandLineArgPassed("sar-combination-probe") ||
                !GenCommandLine.CommandLineArgPassed("quicktest") ||
                LongEventHandler.AnyEventNowOrWaiting || Find.CurrentMap == null ||
                Current.ProgramState != ProgramState.Playing) return;
            ran = true;
            try
            {
                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                Run();
            }
            catch (Exception exception) { results.Add("FAIL: " + exception); }
            File.WriteAllLines(Path.Combine(GenFilePaths.SaveDataFolderPath, "combination-results.txt"), results);
            foreach (string result in results) Log.Message("[SAR combinations] " + result);
            Application.Quit();
        }

        private static void Check(bool success, string label)
        {
            results.Add((success ? "PASS: " : "FAIL: ") + label);
        }

        private static void Run()
        {
            results.Add("Mods: " + string.Join(",", ModsConfig.ActiveModsInLoadOrder.Select(mod => mod.PackageId)));
            Map map = Find.CurrentMap;
            var miSettings = AccessTools.Property(AccessTools.TypeByName("MoreInjuries.MoreInjuriesMod"), "Settings")
                ?.GetValue(null, null);
            var biotechSetting = miSettings == null ? null : AccessTools.GetDeclaredFields(miSettings.GetType())
                .FirstOrDefault(field => field.FieldType == typeof(bool) &&
                    field.Name.StartsWith("_biotechEnableIntegration", StringComparison.OrdinalIgnoreCase));
            if (miSettings != null && biotechSetting == null)
                throw new InvalidOperationException("MI Biotech backing setting not found");
            biotechSetting?.SetValue(miSettings, true);
            Pawn patient = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
            Pawn doctor = map.mapPawns.FreeColonistsSpawned.First(pawn => !pawn.WorkTagIsDisabled(WorkTags.Caring));
            GenSpawn.Spawn(patient, CellFinder.RandomClosewalkCellNear(doctor.Position, map, 3), map);
            doctor.workSettings.SetPriority(SearchAndRescueDefOf.SAR_FieldRescue, 1);
            doctor.workSettings.SetPriority(WorkTypeDefOf.Doctor, 1);
            patient.playerSettings.medCare = MedicalCareCategory.HerbalOrWorse;
            Hediff cut = HediffMaker.MakeHediff(HediffDefOf.Cut, patient, patient.RaceProps.body.corePart);
            cut.Severity = 5f;
            patient.health.AddHediff(cut);
            Thing medicine = ThingMaker.MakeThing(ThingDefOf.MedicineUltratech);
            medicine.stackCount = 3;
            GenSpawn.Spawn(medicine, CellFinder.RandomClosewalkCellNear(doctor.Position, map, 2), map);
            var sm = AccessTools.TypeByName("SmartMedicine.PriorityCareSettingsComp");
            var overrides = sm == null ? null : AccessTools.Method(sm, "Get").Invoke(null, null)
                as IDictionary<Hediff, MedicalCareCategory>;
            var cym = AccessTools.Method(AccessTools.TypeByName("ChooseYourMedicine.DrawButton"), "MakeNewHediffEntry");
            if (cym != null) cym.Invoke(null, new object[] { cut, patient, MedicalCareCategory.Best, false });
            if (overrides != null) overrides[cut] = cym == null ? MedicalCareCategory.Best : MedicalCareCategory.NoMeds;
            if (cym != null || overrides != null)
            {
                Check(Compatibility.EffectiveMedicalCare(patient) == MedicalCareCategory.Best,
                    "native per-wound policy overrides baseline; CYM wins conflicting SM policy");
                Check(Compatibility.AllowsMedicine(patient, medicine), "dose permission matches per-wound budget");
                var plan = MedicalCarePlan.Build(patient, Find.TickManager.TicksGame);
                Check(plan.EssentialMedicineRounds > 0, "per-wound policy budgets medicine");
                var options = Compatibility.FindTreatmentOptions(doctor, patient, plan, new MedicalResourceLedger(map));
                var selected = options.FirstOrDefault(option => option.Resource == medicine);
                Check(selected != null, "actual production selector retains legal high-quality dose");
                if (cym != null) cym.Invoke(null, new object[] { cut, patient, MedicalCareCategory.NoMeds, false });
                if (overrides != null) overrides[cut] = MedicalCareCategory.NoMeds;
                Check(!Compatibility.AllowsMedicine(patient, medicine), "policy change revokes dose permission");
                if (selected != null) Check(Compatibility.MakeTreatmentRoundJob(doctor, patient, selected) == null,
                    "stale selected medicine rejected at Job boundary");
                if (cym != null) cym.Invoke(null, new object[] { cut, patient, MedicalCareCategory.Best, false });
                if (overrides != null) overrides[cut] = MedicalCareCategory.Best;
            }
            else results.Add("SKIP: SM/CYM policy providers absent");

            if (Compatibility.UsesMoreInjuries && Compatibility.UsesHemogenTransfusion)
            {
                foreach (var research in DefDatabase<ResearchProjectDef>.AllDefsListForReading)
                    Find.ResearchManager.FinishProject(research, false, null, false);
                Hediff bloodLoss = HediffMaker.MakeHediff(HediffDefOf.BloodLoss, patient);
                bloodLoss.Severity = 0.65f;
                patient.health.AddHediff(bloodLoss);
                var plan = MedicalCarePlan.Build(patient, Find.TickManager.TicksGame);
                var blood = plan.Demands.FirstOrDefault(demand => demand.Intervention == MedicalIntervention.Blood);
                Check(blood != null, "MI native transfusion demand available");
                if (blood?.ResourceDef == Compatibility.HemogenPack)
                {
                    var shared = plan.Demands.Where(demand => demand.ResourceDef == blood.ResourceDef).ToList();
                    Check(CombinationResourceRules.SharedBudget(shared, demand => demand.IsResuscitation,
                            demand => demand.Count) == shared.Max(demand => demand.Count),
                        "MI blood and direct hemogen share one HemogenPack budget");
                    Check(shared.Any(demand => demand.Intervention == MedicalIntervention.HemogenTransfusion),
                        "direct provider remains available for its distinct inventory access");
                }
                else Check(false, "MI Biotech integration must resolve HemogenPack");
                biotechSetting.SetValue(miSettings, false);
                Check(Compatibility.MoreInjuriesBloodBag != Compatibility.HemogenPack,
                    "MI runtime setting change refreshes blood resource");
                biotechSetting.SetValue(miSettings, true);
                Check(Compatibility.MoreInjuriesBloodBag == Compatibility.HemogenPack,
                    "MI runtime setting restored without stale resource cache");
            }
            else results.Add("SKIP: combined transfusion providers absent");

            // Exercise the real registry/gates with a temporary registered facility
            // type. This is an ownership boundary test, not a MedPod playthrough.
            map.designationManager.AddDesignation(new Designation(patient, SearchAndRescueDefOf.SAR_Treat));
            map.designationManager.AddDesignation(new Designation(patient, SearchAndRescueDefOf.SAR_Rescue));
            Check(!SearchAndRescueJobContext.HasManagedTreatmentOrder(patient), "unassigned explicit treatment permits external fallback");
            Check(!SearchAndRescueJobContext.HasManagedTransportOrder(patient), "unassigned explicit rescue permits external fallback");
            Check(!SearchAndRescueJobContext.HasManagedBattlefieldOrder(patient), "unassigned explicit marks permit warden fallback");
            var claims = (ActiveJobClaims)AccessTools.Field(typeof(SearchAndRescueCoordinator), "activeClaims")
                .GetValue(map.GetComponent<SearchAndRescueCoordinator>());
            claims.Register(new ActiveAssignment(doctor, patient, JobMaker.MakeJob(JobDefOf.TendPatient, patient),
                SearchAndRescueStage.Treat, patient.Position, Find.TickManager.TicksGame, 1, 1f, 1f,
                CareOrigin.ManualTreatment, 0f, 0f));
            Check(SearchAndRescueJobContext.HasManagedTreatmentOrder(patient), "committed treatment blocks competing tend");
            Check(SearchAndRescueJobContext.HasManagedTransportOrder(patient), "committed treatment blocks premature transport");
            var bed = (Building_Bed)ThingMaker.MakeThing(ThingDefOf.Bed, ThingDefOf.WoodLog);
            GenSpawn.Spawn(bed, CellFinder.RandomClosewalkCellNear(patient.Position, map, 4), map);
            patient.Position = bed.GetSleepingSlotPos(0);
            patient.jobs.StartJob(JobMaker.MakeJob(JobDefOf.LayDown, bed), JobCondition.InterruptForced);
            patient.jobs.posture = PawnPosture.LayingInBed;
            Check(patient.CurrentBed() == bed, "facility fixture has a real bed occupant");
            var facilities = (HashSet<Type>)AccessTools.Field(typeof(CompatibilityRegistry), "FacilityBedTypes").GetValue(null);
            bool added = facilities.Add(typeof(Building_Bed));
            try
            {
                Check(CompatibilityRegistry.HasExternalOwner(patient), "registered facility owns patient before tending Job");
                Check(!SearchAndRescueJobContext.HasManagedTreatmentOrder(patient), "facility provider is not blocked by SAR treatment gate");
                Check(!SearchAndRescueJobContext.HasManagedTransportOrder(patient), "facility provider is not blocked by SAR transport gate");
                Check(!SearchAndRescueJobContext.HasManagedBattlefieldOrder(patient), "warden gate also yields during facility handoff");
            }
            finally { if (added) facilities.Remove(typeof(Building_Bed)); }
            Check(SearchAndRescueJobContext.HasManagedTreatmentOrder(patient), "SAR treatment gate resumes after external lease ends");
            Check(SearchAndRescueJobContext.HasManagedTransportOrder(patient), "SAR transport gate resumes after external lease ends");
            claims.ReleasePrimary(patient);
            Check(!SearchAndRescueJobContext.HasManagedTreatmentOrder(patient), "released claim immediately restores external fallback");

            if (Compatibility.UsesWorkTab && Compatibility.UsesMoreInjuries)
            {
                var setter = AccessTools.Method(AccessTools.TypeByName("WorkTab.Pawn_Extensions"), "SetPriority",
                    new[] { typeof(Pawn), typeof(WorkGiverDef), typeof(int), typeof(int), typeof(bool) });
                if (setter == null) throw new InvalidOperationException("Work Tab setter missing");
                void Set(string name, int value) => setter.Invoke(null, new object[] { doctor,
                    DefDatabase<WorkGiverDef>.GetNamed(name), value, -1, true });
                Set("SAR_EmergencyMedicalCare", 1);
                foreach (var entry in new[] {
                    ("DoctorUseDefibrillator", MedicalIntervention.Defibrillate),
                    ("DoctorManageAirways", MedicalIntervention.Suction),
                    ("DoctorUseBloodBag", MedicalIntervention.Blood),
                    ("DoctorUseSalineBag", MedicalIntervention.Saline),
                    ("DoctorPerformCpr", MedicalIntervention.Cpr) })
                {
                    Set(entry.Item1, 0);
                    Check(!Compatibility.NativeInterventionWorkAllowed(doctor, entry.Item2, patient),
                        "disabled native child blocks SAR: " + entry.Item1);
                    var stale = new MedicalTreatmentOption(entry.Item2, null, 1, false, false, 1d, 0d);
                    Check(Compatibility.MakeTreatmentRoundJob(doctor, patient, stale) == null,
                        "disabled native child rejects stale Job: " + entry.Item1);
                    Set(entry.Item1, 1);
                    Check(Compatibility.NativeInterventionWorkAllowed(doctor, entry.Item2, patient),
                        "reenabled native child permits SAR: " + entry.Item1);
                }
                Hediff cardiac = HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed("CardiacArrest"), patient);
                patient.health.AddHediff(cardiac);
                Set("DoctorPerformCpr", 0);
                var cardiacPlan = MedicalCarePlan.Build(patient, Find.TickManager.TicksGame);
                Check(!Compatibility.FindTreatmentOptions(doctor, patient, cardiacPlan, new MedicalResourceLedger(map))
                    .Any(option => option.Intervention == MedicalIntervention.Cpr), "disabled CPR excluded from actual treatment candidates");
                Set("DoctorPerformCpr", 1);
                Check(Compatibility.FindTreatmentOptions(doctor, patient, cardiacPlan, new MedicalResourceLedger(map))
                    .Any(option => option.Intervention == MedicalIntervention.Cpr), "enabled CPR returns to actual treatment candidates");
                patient.health.RemoveHediff(cardiac);
            }
            int beforeTended = patient.health.hediffSet.hediffs.Count(h => h.IsTended());
            int errors = 0;
            void OnLog(string message, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                { errors++; results.Add("RUNTIME ERROR: " + message); }
            }
            Application.logMessageReceived += OnLog;
            try
            {
                for (int tick = 0; tick < 6000; tick++) Find.TickManager.DoSingleTick();
            }
            finally { Application.logMessageReceived -= OnLog; }
            Check(errors == 0, "6000 ticks after ownership handoff without runtime errors");
            Check(!patient.Dead && patient.health.hediffSet.hediffs.Count(h => h.IsTended()) > beforeTended,
                "marked patient receives actual tending after ownership handoff");
        }
    }
}
