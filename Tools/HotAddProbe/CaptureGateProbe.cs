using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SarHotAddProbe
{
    // Component-level reproduction using engine classes; does not claim full DLC coverage.
    internal static class CaptureGateProbe
    {
        private sealed class StudyFixture : CompStudiable
        {
            public override float AnomalyKnowledge => 1f;
        }

        internal static string Run(bool expectFixed)
        {
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,
                Faction.OfAncientsHostile, forceGenerateNewPawn: true, canGeneratePawnRelations: false));
            GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(Find.CurrentMap.Center, Find.CurrentMap, 4), Find.CurrentMap);
            HealthUtility.TryAnesthetize(pawn);
            var injury = HediffMaker.MakeHediff(HediffDefOf.Cut, pawn, pawn.RaceProps.body.corePart);
            injury.Severity = 5; pawn.health.AddHediff(injury);
            var holding = new CompHoldingPlatformTarget { parent = pawn, props = new CompProperties_HoldingPlatformTarget() };
            pawn.AllComps.Add(holding);
            var flag = AccessTools.Field(typeof(ModsConfig), "anomalyActive");
            bool original = ModsConfig.AnomalyActive;
            var anomaly = Find.Anomaly;
            var progress = AccessTools.Field(typeof(GameComponent_Anomaly), "highestLevelReached");
            int originalProgress = (int)progress.GetValue(anomaly);
            var difficulty = Find.Storyteller.difficulty;
            var originalPlaystyle = difficulty.AnomalyPlaystyleDef;
            var study = new StudyFixture { parent = pawn, props = new CompProperties_Studiable { minMonolithLevelForStudy = 1 } };
            var report = new StringBuilder("Component fixture; full Anomaly DLC is not installed.\n");
            try
            {
                flag.SetValue(null, true);
                var capture = (Designator)Activator.CreateInstance(AccessTools.TypeByName("SearchAndRescue.Designator_Capture"));
                var all = (Designator)Activator.CreateInstance(AccessTools.TypeByName("SearchAndRescue.Designator_SearchAndRescue"));
                bool accepted = capture.CanDesignateThing(pawn).Accepted;
                bool combined = all.CanDesignateThing(pawn).Accepted;
                bool cell = all.CanDesignateCell(pawn.Position).Accepted;
                bool command = ((IEnumerable<Gizmo>)AccessTools.Method(AccessTools.TypeByName("SearchAndRescue.Pawn_SearchAndRescueCommandPatch"), "AddCommand")
                    .Invoke(null, new object[] { Enumerable.Empty<Gizmo>(), pawn })).Any();
                report.AppendLine($"ordinary human: nativeCapturable={holding.CanBeCaptured} platformStudy={holding.StudiedAtHoldingPlatform} capture={accepted} combined={combined} cell={cell} command={command}");
                if (!holding.CanBeCaptured || holding.StudiedAtHoldingPlatform || accepted != expectFixed || combined != expectFixed || cell != expectFixed || command != expectFixed)
                    throw new Exception("Ordinary human eligibility expectation failed.");
                pawn.AllComps.Add(study);
                difficulty.AnomalyPlaystyleDef = new AnomalyPlaystyleDef { generateMonolith = true };
                progress.SetValue(anomaly, 0);
                bool newGameCapture = capture.CanDesignateThing(pawn).Accepted;
                progress.SetValue(anomaly, 1);
                bool progressedCapture = capture.CanDesignateThing(pawn).Accepted;
                report.AppendLine($"same human: monolithLevel0Capture={newGameCapture} monolithLevel1Capture={progressedCapture}");
                if (!newGameCapture || progressedCapture != expectFixed) throw new Exception("Monolith progress regression.");
                if (expectFixed)
                {
                    all.DesignateThing(pawn);
                    string marks = string.Join(",", Find.CurrentMap.designationManager.AllDesignations.Where(d => d.target.Thing == pawn).Select(d => d.def.defName));
                    report.AppendLine("marks=" + marks);
                    if (!new[] { "SAR_Capture", "SAR_Treat", "SAR_Rescue" }.All(m => marks.Contains(m))) throw new Exception("Missing stage marks.");
                    pawn.mutant = new Pawn_MutantTracker(pawn);
                    AccessTools.Field(typeof(Pawn_MutantTracker), "def").SetValue(pawn.mutant, new MutantDef { canBeCapturedToHoldingPlatform = true });
                    bool eligible = (bool)AccessTools.Method(AccessTools.TypeByName("SearchAndRescue.TargetEligibility"), "CanBeCaptured").Invoke(null, new object[] { pawn });
                    report.AppendLine($"containment mutant: platformStudy={holding.StudiedAtHoldingPlatform} prisonerCapture={eligible}");
                    if (!holding.StudiedAtHoldingPlatform || eligible) throw new Exception("Containment target entered prisoner capture.");
                    pawn.mutant = null; pawn.AllComps.Remove(study);
                }
                return report.AppendLine("PASS").ToString();
            }
            finally
            {
                pawn.mutant = null;
                pawn.AllComps.Remove(study);
                progress.SetValue(anomaly, originalProgress);
                difficulty.AnomalyPlaystyleDef = originalPlaystyle;
                flag.SetValue(null, original);
                pawn.AllComps.Remove(holding);
            }
        }
    }
}
