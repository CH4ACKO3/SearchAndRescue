using System;
using System.Linq;
using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SarHotAddProbe
{
    internal static class NativeAnomalyProbe
    {
        internal static string Run(bool fixedDll)
        {
            if (!ModsConfig.AnomalyActive) throw new Exception("Full Anomaly must be enabled.");
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            var log = new StringBuilder("Full Anomaly DLC; native components and progression API.\n");
            Pawn enemy = fixedDll ? Find.CurrentMap.mapPawns.AllPawnsSpawned.Single(p => p.LabelShort.Contains("Anomaly Probe")) : Create();
            var holding = enemy.GetComp<CompHoldingPlatformTarget>();
            var study = enemy.GetComp<CompStudiable>();
            if (holding == null || study == null) throw new Exception("Native Human components missing.");
            log.AppendLine($"pawn={enemy.ThingID} studyType={study.GetType().FullName} minLevel={study.Props.minMonolithLevelForStudy}");
            if (!fixedDll)
            {
                if (Find.Anomaly.HighestLevelReached != 0 || !Find.Anomaly.GenerateMonolith) throw new Exception("Expected standard fresh Anomaly game.");
                Check(enemy, true, log);
                Find.Anomaly.SetLevel(DefDatabase<MonolithLevelDef>.AllDefs.First(d => d.level == 1), silent: true);
            }
            Check(enemy, fixedDll, log);
            if (fixedDll)
            {
                var combined = (Designator)Activator.CreateInstance(AccessTools.TypeByName("SearchAndRescue.Designator_SearchAndRescue"));
                combined.DesignateThing(enemy);
                var marks = Find.CurrentMap.designationManager.AllDesignations.Where(d => d.target.Thing == enemy).Select(d => d.def.defName).ToList();
                log.AppendLine("marks=" + string.Join(",", marks));
                if (!new[] { "SAR_Capture", "SAR_Treat", "SAR_Rescue" }.All(marks.Contains)) throw new Exception("Missing mark.");
                Pawn mutant = Create();
                MutantUtility.SetPawnAsMutantInstantly(mutant, DefDatabase<MutantDef>.GetNamed("Shambler"));
                bool platform = mutant.GetComp<CompHoldingPlatformTarget>().StudiedAtHoldingPlatform;
                bool capture = (bool)AccessTools.Method(AccessTools.TypeByName("SearchAndRescue.TargetEligibility"), "CanBeCaptured").Invoke(null, new object[] { mutant });
                log.AppendLine($"native Shambler: platformStudy={platform} prisonerCapture={capture}");
                if (!platform || capture) throw new Exception("Shambler classification failed.");
            }
            GameDataSaveLoader.SaveGame(fixedDll ? "SAR_Anomaly_Fixed" : "SAR_Anomaly_Progressed");
            return log.AppendLine("PASS").ToString();
        }
        private static Pawn Create()
        {
            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfAncientsHostile,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false));
            pawn.Name = new NameTriple("SAR", "Anomaly Probe", "Test");
            GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(Find.CurrentMap.Center, Find.CurrentMap, 4), Find.CurrentMap);
            Find.CurrentMap.fogGrid.Unfog(pawn.Position);
            HealthUtility.TryAnesthetize(pawn);
            var injury = HediffMaker.MakeHediff(HediffDefOf.Cut, pawn, pawn.RaceProps.body.corePart);
            injury.Severity = 5; pawn.health.AddHediff(injury);
            return pawn;
        }
        private static void Check(Pawn pawn, bool expected, StringBuilder log)
        {
            var all = (Designator)Activator.CreateInstance(AccessTools.TypeByName("SearchAndRescue.Designator_SearchAndRescue"));
            bool thing = all.CanDesignateThing(pawn).Accepted;
            bool cell = all.CanDesignateCell(pawn.Position).Accepted;
            bool command = pawn.GetGizmos().Any(g => g.GetType().FullName == "SearchAndRescue.Command_SearchAndRescuePawn");
            log.AppendLine($"level={Find.Anomaly.HighestLevelReached} thing={thing} cell={cell} nativeGizmosCommand={command}");
            if (thing != expected || cell != expected || command != expected) throw new Exception("Designation expectation failed.");
        }
    }
}
