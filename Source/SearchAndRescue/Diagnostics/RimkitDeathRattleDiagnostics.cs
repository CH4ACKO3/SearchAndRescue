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
    [HarmonyPatch(typeof(Root), nameof(Root.Update))]
    internal static class RimkitDeathRattleDiagnostics
    {
        private static bool ran;
        private static readonly List<string> results = new List<string>();
        private static void Check(bool ok, string label) => results.Add((ok ? "PASS: " : "FAIL: ") + label);
        private static void Postfix()
        {
            if (ran || !GenCommandLine.CommandLineArgPassed("sar-rimkit-deathrattle-probe") ||
                !GenCommandLine.CommandLineArgPassed("quicktest") || LongEventHandler.AnyEventNowOrWaiting ||
                Find.CurrentMap == null || Current.ProgramState != ProgramState.Playing ||
                Find.GameInitData != null || Find.TickManager.TicksGame < 60) return;
            ran = true;
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            try { Run(); }
            catch (Exception exception) { results.Add("FAIL: " + exception); }
            File.WriteAllLines(Path.Combine(GenFilePaths.SaveDataFolderPath, "rimkit-deathrattle-results.txt"), results);
            foreach (string result in results) Log.Message("[SAR Rimkit/DR] " + result);
            Application.Quit();
        }

        private static Pawn Spawn(Map map, IntVec3 near)
        {
            Pawn pawn;
            do { pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer); }
            while (pawn.WorkTagIsDisabled(WorkTags.Caring) || pawn.WorkTagIsDisabled(WorkTags.ManualDumb));
            GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(near, map, 4), map);
            pawn.workSettings.EnableAndInitializeIfNotAlreadyInitialized();
            pawn.workSettings.SetPriority(SearchAndRescueDefOf.SAR_FieldRescue, 1);
            pawn.workSettings.SetPriority(WorkTypeDefOf.Doctor, 1);
            pawn.workSettings.SetPriority(WorkTypeDefOf.Hauling, 1);
            return pawn;
        }

        private static void Advance(int count)
        {
            int errors = 0;
            void OnLog(string message, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                { errors++; results.Add("RUNTIME ERROR: " + message); }
            }
            Application.logMessageReceived += OnLog;
            try { for (int i = 0; i < count; i++) Find.TickManager.DoSingleTick(); }
            finally { Application.logMessageReceived -= OnLog; }
            Check(errors == 0, count + " ticks without runtime errors");
        }

        private static void Run()
        {
            results.Add("Mods: " + string.Join(",", ModsConfig.ActiveModsInLoadOrder.Select(mod => mod.PackageId)));
            Map map = Find.CurrentMap;
            foreach (Pawn original in map.mapPawns.FreeColonistsSpawned.ToList()) original.drafter.Drafted = true;
            Pawn doctor = Spawn(map, map.Center);
            doctor.skills.GetSkill(SkillDefOf.Medicine).Level = 15;
            Pawn patient = Spawn(map, doctor.Position);
            patient.health.AddHediff(HediffDefOf.Anesthetic);
            Hediff cut = HediffMaker.MakeHediff(HediffDefOf.Cut, patient, patient.RaceProps.body.corePart);
            cut.Severity = 3f;
            patient.health.AddHediff(cut);
            Hediff bruise = HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed("Bruise"), patient, patient.RaceProps.body.corePart);
            bruise.Severity = 3f;
            patient.health.AddHediff(bruise);
            patient.playerSettings.medCare = MedicalCareCategory.Best;
            var apparel = (Apparel)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("MedicalKit"));
            doctor.apparel.Wear(apparel);
            var kit = apparel.AllComps.OfType<CompApparelReloadable>().Single();
            var auto = AccessTools.Field(kit.GetType(), "UseKitForTendJob");
            Check(RimkitCompatibility.Ready, "native Rimkit adapter ready");
            Check(!RimkitCompatibility.Option(doctor, patient).IsValid, "native automatic-use toggle defaults off");
            auto.SetValue(kit, true);
            var option = RimkitCompatibility.Option(doctor, patient);
            Check(option.IsValid && option.Equipment == apparel, "enabled charged worn kit becomes a bound candidate");
            Job managed = RimkitCompatibility.MakeJob(doctor, patient, option);
            Job manual = JobMaker.MakeJob(RimkitCompatibility.BandageJob, patient);
            Check(((IEnumerable<Toil>)RimkitCompatibility.DriverToils.Invoke(managed.GetCachedDriver(doctor), null)).Count() == 4,
                "bound native job has one-round toils even before transient claims exist");
            Check(((IEnumerable<Toil>)RimkitCompatibility.DriverToils.Invoke(manual.GetCachedDriver(doctor), null)).Count() == 5,
                "manual native Rimkit job retains its original looping toils");
            patient.playerSettings.medCare = MedicalCareCategory.HerbalOrWorse;
            Check(!RimkitCompatibility.Option(doctor, patient).IsValid, "industrial kit respects herbal-only policy");
            Check(RimkitCompatibility.MakeJob(doctor, patient, option) == null, "stale kit candidate cannot bypass changed policy");
            patient.playerSettings.medCare = MedicalCareCategory.Best;
            auto.SetValue(kit, false);
            Check(RimkitCompatibility.MakeJob(doctor, patient, option) == null, "stale candidate respects kit toggle change");
            auto.SetValue(kit, true);
            int charges = kit.RemainingCharges;
            map.designationManager.AddDesignation(new Designation(patient, SearchAndRescueDefOf.SAR_Treat));
            Advance(2400);
            Check(cut.IsTended(), "SAR automatically performs native kit bandaging");
            Check(!bruise.IsTended(), "minor wound remains untended in the field");
            Check(kit.RemainingCharges == charges - 1, "one completed round consumes exactly one kit charge");
            Check(doctor.CurJobDef != RimkitCompatibility.BandageJob, "native looping job ends after the managed round");
            AccessTools.Field(typeof(CompApparelVerbOwner_Charged), "remainingCharges").SetValue(kit, 0);
            Check(!RimkitCompatibility.Option(doctor, patient).IsValid, "empty kit cannot create a candidate");
            patient.Destroy();
            doctor.jobs.EndCurrentJob(JobCondition.InterruptForced);

            Pawn organPatient = Spawn(map, doctor.Position);
            BodyPartRecord heart = organPatient.health.hediffSet.GetNotMissingParts().First(p => p.def == BodyPartDefOf.Heart);
            organPatient.health.AddHediff(HediffDefOf.MissingBodyPart, heart);
            Advance(240);
            Hediff noPulse = organPatient.health.hediffSet.GetFirstHediffOfDef(
                DefDatabase<HediffDef>.GetNamed("ClinicalDeathNoHeartbeat"));
            Check(!organPatient.Dead && noPulse != null, "native Death Rattle keeps heartless casualty alive with no-pulse state");
            Check(DeathRattleCompatibility.TransportPriority(organPatient) >= 2d, "no pulse adds urgent hospital transport pressure");
            Check(noPulse != null && !FieldTreatmentBoundary.IsEmergency(noPulse), "no pulse does not become a futile tend target");
            Check(!RimkitCompatibility.Option(doctor, organPatient).IsValid, "kit cannot repair missing vital organs");
            var bed = (Building_Bed)ThingMaker.MakeThing(ThingDefOf.Bed, ThingDefOf.WoodLog);
            bed.SetFaction(Faction.OfPlayer);
            GenSpawn.Spawn(bed, CellFinder.RandomClosewalkCellNear(doctor.Position, map, 8), map);
            bed.Medical = true;
            Check(Compatibility.FindBestRescueBed(organPatient, doctor) == bed, "hospital bed is a valid player rescue destination");
            map.designationManager.AddDesignation(new Designation(organPatient, SearchAndRescueDefOf.SAR_Rescue));
            Advance(2400);
            results.Add("EVAC: dead=" + organPatient.Dead + " downed=" + organPatient.Downed +
                " position=" + organPatient.PositionHeld + " bed=" + organPatient.CurrentBed()?.ThingID +
                " expected=" + bed.ThingID + " worker=" + doctor.CurJobDef?.defName +
                " workerPosition=" + doctor.Position);
            results.Add(map.GetComponent<SearchAndRescueCoordinator>().DebugDescribeScheduler());
            Check(!organPatient.Dead && organPatient.CurrentBed() == bed, "SAR evacuates native no-pulse casualty to hospital alive");
            organPatient.health.RestorePart(heart);
            Check(DeathRattleCompatibility.TransportPriority(organPatient) == 0d, "restored capacity immediately removes stale transport pressure");
            BodyPartRecord liver = organPatient.health.hediffSet.GetNotMissingParts().First(p => p.def.defName == "Liver");
            organPatient.health.AddHediff(HediffDefOf.MissingBodyPart, liver);
            Advance(240);
            Hediff failure = organPatient.health.hediffSet.GetFirstHediffOfDef(DefDatabase<HediffDef>.GetNamed("LiverFailure"));
            Check(failure != null && DeathRattleCompatibility.TransportPriority(organPatient) >= 1d &&
                DeathRattleCompatibility.TransportPriority(organPatient) < 2d, "native liver failure has bounded hospital urgency below no-pulse urgency");
            Check(failure != null && !FieldTreatmentBoundary.IsEmergency(failure), "liver failure does not create futile bandaging");
            organPatient.health.RestorePart(liver);
            Check(DeathRattleCompatibility.TransportPriority(organPatient) == 0d, "restored liver clears its transport urgency");
        }
    }
}
