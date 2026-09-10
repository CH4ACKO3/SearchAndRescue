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
    // Explicit disposable quicktest only; never touches a player's normal session.
    [HarmonyPatch(typeof(Root), nameof(Root.Update))]
    internal static class AncientUrbanRuinsDiagnostics
    {
        private static bool ran;
        private static readonly List<string> results = new List<string>();
        private static void Check(bool ok, string label) => results.Add((ok ? "PASS: " : "FAIL: ") + label);
        private static void Postfix()
        {
            if (ran || !GenCommandLine.CommandLineArgPassed("sar-aur-probe") ||
                !GenCommandLine.CommandLineArgPassed("quicktest") || LongEventHandler.AnyEventNowOrWaiting ||
                Find.CurrentMap == null || Current.ProgramState != ProgramState.Playing ||
                Find.GameInitData != null || Find.TickManager.TicksGame < 60) return;
            ran = true;
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            try { Run(); } catch (Exception ex) { results.Add("FAIL: " + ex); }
            File.WriteAllLines(Path.Combine(GenFilePaths.SaveDataFolderPath, "aur-results.txt"), results);
            foreach (string line in results) Log.Message("[SAR AUR] " + line);
            Application.Quit();
        }

        private static Pawn Spawn(Map map, IntVec3 near)
        {
            Pawn pawn;
            do { pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer); }
            while (pawn.WorkTagIsDisabled(WorkTags.Caring) || pawn.WorkTagIsDisabled(WorkTags.ManualDumb));
            GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(near, map, 3), map);
            pawn.workSettings.EnableAndInitializeIfNotAlreadyInitialized();
            pawn.workSettings.SetPriority(WorkTypeDefOf.Doctor, 1);
            pawn.workSettings.SetPriority(SearchAndRescueDefOf.SAR_FieldRescue, 1);
            pawn.skills.GetSkill(SkillDefOf.Medicine).Level = 15;
            return pawn;
        }

        private static void Run()
        {
            results.Add("Mods: " + string.Join(",", ModsConfig.ActiveModsInLoadOrder.Select(m => m.PackageId)));
            Map map = Find.CurrentMap;
            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned.ToList()) pawn.drafter.Drafted = true;
            foreach (string name in new[] { "AM_AI2A", "AM_HemostaticAgent", "AM_FirstAidKit", "AM_Salewa", "AM_Grizzly", "AM_AncientGrizzly" })
            foreach (bool managed in new[] { false, true })
            {
                Pawn doctor = Spawn(map, map.mapPawns.FreeColonistsSpawned.First().Position);
                Pawn patient = Spawn(map, doctor.Position);
                patient.health.AddHediff(HediffDefOf.Anesthetic);
                patient.playerSettings.medCare = MedicalCareCategory.Best;
                Hediff cut = HediffMaker.MakeHediff(HediffDefOf.Cut, patient, patient.RaceProps.body.corePart);
                cut.Severity = 3f; patient.health.AddHediff(cut);
                Hediff bruise = HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed("Bruise"), patient, patient.RaceProps.body.corePart);
                bruise.Severity = 3f; patient.health.AddHediff(bruise);
                var kit = (Medicine)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(name));
                GenSpawn.Spawn(kit, CellFinder.RandomClosewalkCellNear(doctor.Position, map, 1), map);
                Check(doctor.CanReach(patient, PathEndMode.Touch, Danger.Deadly) &&
                    doctor.CanReach(kit, PathEndMode.Touch, Danger.Deadly), name + " fixture reachable");
                ThingComp comp = kit.AllComps.First(c => c.GetType().FullName == "AncientMarket_Libraray.CompUseableCount");
                var counter = AccessTools.Field(comp.GetType(), "count");
                if (GenCommandLine.CommandLineArgPassed("sar-aur-initialize"))
                    AccessTools.Property(comp.GetType(), "Count").GetValue(comp, null);
                bool lastUse = GenCommandLine.CommandLineArgPassed("sar-aur-last-use");
                if (lastUse) counter.SetValue(comp, (int?)1);
                string label = name + (managed ? " SAR" : " ordinary");
                Job job = JobMaker.MakeJob(JobDefOf.TendPatient, patient, kit);
                if (managed)
                {
                    var option = Compatibility.FindTreatmentOptions(doctor, patient,
                        MedicalCarePlan.Build(patient, Find.TickManager.TicksGame), new MedicalResourceLedger(map))
                        .FirstOrDefault(o => o.Resource == kit);
                    job = option == null ? null : Compatibility.MakeTreatmentRoundJob(doctor, patient, option);
                    Check(job != null, label + " selected production medicine/job");
                    if (job == null) throw new InvalidOperationException(label + " no production job");
                }
                job.draftedTend = true;
                job.endAfterTendedOnce = true;
                var claims = (ActiveJobClaims)AccessTools.Field(typeof(SearchAndRescueCoordinator), "activeClaims")
                    .GetValue(map.GetComponent<SearchAndRescueCoordinator>());
                if (managed) claims.Register(new ActiveAssignment(doctor, patient, job, SearchAndRescueStage.Treat,
                    patient.Position, Find.TickManager.TicksGame, 1, 1f, 1f, CareOrigin.ManualTreatment, 0f, 0f));
                int errors = 0;
                void OnLog(string message, string stack, LogType level)
                {
                    if (level == LogType.Error || level == LogType.Exception || level == LogType.Assert)
                    { errors++; results.Add("ERROR " + label + ": " + message); }
                }
                Application.logMessageReceived += OnLog;
                try
                {
                    results.Add(label + " initial count=" + (counter.GetValue(comp) ?? "null") + " selectedJob=" + job.def.defName);
                    bool Stabilized() => (cut as HediffWithComps)?.comps.Any(c => c.GetType().FullName == "CombatExtended.HediffComp_Stabilize"
                        && (bool)AccessTools.Property(c.GetType(), "Stabilized").GetValue(c, null)) == true;
                    doctor.jobs.StartJob(job, JobCondition.InterruptForced);
                    int lastToil = -999;
                    int ticks = 0;
                    for (; ticks < 1200 && !cut.IsTended() && !Stabilized() && !patient.Dead; ticks++)
                    {
                        int toil = doctor.jobs.curDriver?.CurToilIndex ?? -1;
                        if (toil != lastToil)
                        {
                            results.Add(label + " tick=" + ticks + " job=" + doctor.CurJobDef?.defName + " toil=" + toil
                                + " patient=" + patient.Position + " doctor=" + doctor.Position + " kit=" + kit.Position);
                            lastToil = toil;
                        }
                        Find.TickManager.DoSingleTick();
                    }
                    for (int i = 0; i < 2; i++) Find.TickManager.DoSingleTick();
                    results.Add(label + " ticks=" + ticks + " cut=" + cut.IsTended() + " bruise=" + bruise.IsTended()
                        + " kitDestroyed=" + kit.Destroyed + " count=" + (counter.GetValue(comp) ?? "null")
                        + " job=" + doctor.CurJobDef?.defName);
                    Check((cut.IsTended() || Stabilized()) && !patient.Dead, label + " actual wound treated/stabilized");
                    Check(!managed || !bruise.IsTended(), label + " field boundary preserved");
                    if (lastUse) Check(kit.Destroyed, label + " final use consumes kit");
                    else if (managed || GenCommandLine.CommandLineArgPassed("sar-aur-initialize"))
                    {
                        object props = AccessTools.Property(comp.GetType(), "Props").GetValue(comp, null);
                        int capacity = (int)AccessTools.Field(props.GetType(), "useableCount").GetValue(props);
                        Check(!kit.Destroyed && Equals(counter.GetValue(comp), capacity - 1), label + " exactly one use consumed; kit retained");
                    }
                    else results.Add("BASELINE: " + label + " native uninitialized counter kitDestroyed=" + kit.Destroyed);
                    Check(errors == 0, label + " no runtime errors");
                }
                finally
                {
                    Application.logMessageReceived -= OnLog;
                    claims.ReleasePrimary(patient);
                    doctor.Destroy(); patient.Destroy();
                    if (!kit.Destroyed) kit.Destroy();
                }
            }
        }
    }
}
