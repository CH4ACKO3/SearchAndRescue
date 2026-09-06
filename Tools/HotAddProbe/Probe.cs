using System;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SarHotAddProbe
{
    // No compile-time SAR reference: the exact same probe runs before and after adding SAR.
    public sealed class ProbeMod : Mod
    {
        public ProbeMod(ModContentPack content) : base(content)
        {
            if (GenCommandLine.TryGetCommandLineArg("sar-hotadd-phase", out string unused))
                new Harmony("sar.hotadd.probe").PatchAll();
        }
    }
    [HarmonyPatch(typeof(Root), nameof(Root.Update))]
    internal static class Probe
    {
        private static bool requested, finished;
        private static void Postfix()
        {
            if (finished || LongEventHandler.AnyEventNowOrWaiting ||
                !GenCommandLine.TryGetCommandLineArg("sar-hotadd-phase", out string phase)) return;
            try
            {
                if (phase.StartsWith("anomaly-", StringComparison.Ordinal))
                {
                    if (phase != "anomaly-before" && !requested)
                    {
                        requested = true;
                        GameDataSaveLoader.LoadGame("SAR_Anomaly_Progressed");
                        return;
                    }
                    if (Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null) return;
                    finished = true;
                    Write(phase, NativeAnomalyProbe.Run(phase != "anomaly-before"));
                    Application.Quit();
                    return;
                }
                if (phase.StartsWith("capturegate", StringComparison.Ordinal))
                {
                    if (Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null) return;
                    finished = true;
                    Write(phase, CaptureGateProbe.Run(phase.EndsWith("after", StringComparison.Ordinal)));
                    Application.Quit();
                    return;
                }
                if (!requested && phase != "before" && phase != "new")
                {
                    requested = true;
                    GameDataSaveLoader.LoadGame(phase == "reload" ? "SAR_HotAdd_Marked" : "SAR_HotAdd_Before");
                    return;
                }
                if (Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null) return;
                finished = true;
                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                Map map = Find.CurrentMap;
                var report = new StringBuilder();
                Type sar = AccessTools.TypeByName("SearchAndRescue.Designator_SearchAndRescue");
                report.AppendLine("phase=" + phase + " sarLoaded=" + (sar != null));
                if (phase == "before" && sar != null) throw new Exception("SAR must be disabled for the before-save.");
                Pawn enemy;
                if (phase == "before" || phase == "new")
                {
                    enemy = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,
                        Faction.OfAncientsHostile, forceGenerateNewPawn: true, canGeneratePawnRelations: false));
                    enemy.Name = new NameTriple("SAR", "HotAdd Enemy", "Probe");
                    GenSpawn.Spawn(enemy, CellFinder.RandomClosewalkCellNear(map.Center, map, 4), map);
                    HealthUtility.TryAnesthetize(enemy);
                    Hediff injury = HediffMaker.MakeHediff(HediffDefOf.Cut, enemy, enemy.RaceProps.body.corePart);
                    injury.Severity = 5; enemy.health.AddHediff(injury);
                }
                else enemy = map.mapPawns.AllPawnsSpawned.Single(p => p.LabelShort.Contains("HotAdd Enemy"));
                if (!enemy.HostileTo(Faction.OfPlayer) || !enemy.Downed)
                    throw new Exception("Invalid fixture: expected a hostile downed pawn.");
                map.fogGrid.Unfog(enemy.Position);
                Find.Selector.Select(enemy);
                report.AppendLine("enemy=" + enemy.ThingID + " downed=" + enemy.Downed +
                    " hostile=" + enemy.HostileTo(Faction.OfPlayer) + " selected=" + Find.Selector.IsSelected(enemy) +
                    " medCare=" + enemy.playerSettings?.medCare);
                report.AppendLine("coordinator=" + map.components.Any(c => c.GetType().FullName == "SearchAndRescue.SearchAndRescueCoordinator"));
                if (phase == "before") GameDataSaveLoader.SaveGame("SAR_HotAdd_Before");
                else
                {
                    report.AppendLine("existingMarks=" + Marks(map, enemy));
                    foreach (Designation d in map.designationManager.AllDesignations.Where(d =>
                        d.target.Thing == enemy && d.def.defName.StartsWith("SAR_")).ToList())
                        map.designationManager.RemoveDesignation(d);
                    foreach (string name in new[] { "Designator_Capture", "Designator_Treat", "Designator_Rescue", "Designator_SearchAndRescue" })
                    {
                        var d = (Designator)Activator.CreateInstance(AccessTools.TypeByName("SearchAndRescue." + name));
                        AcceptanceReport accepted = d.CanDesignateThing(enemy);
                        report.AppendLine(name + "=" + accepted.Accepted + " reason=" + accepted.Reason +
                            " cell=" + d.CanDesignateCell(enemy.Position).Accepted);
                    }
                    report.AppendLine("pawnCommand=" + enemy.GetGizmos().Any(g => g.GetType().FullName == "SearchAndRescue.Command_SearchAndRescuePawn"));
                    var combined = (Designator)Activator.CreateInstance(sar);
                    if (!combined.CanDesignateThing(enemy).Accepted) throw new Exception("Combined command rejected hostile downed human.");
                    combined.DesignateThing(enemy);
                    report.AppendLine("appliedMarks=" + Marks(map, enemy));
                    if (!map.designationManager.AllDesignations.Any(d => d.target.Thing == enemy && d.def.defName == "SAR_Capture"))
                        throw new Exception("Capture designation missing.");
                    GameDataSaveLoader.SaveGame("SAR_HotAdd_Marked");
                }
                Write(phase, report.ToString());
            }
            catch (Exception e) { finished = true; Write(phase, "FAIL " + e); }
            if (finished) Application.Quit();
        }
        private static string Marks(Map map, Pawn pawn) => string.Join(",", map.designationManager.AllDesignations
            .Where(d => d.target.Thing == pawn && d.def.defName.StartsWith("SAR_")).Select(d => d.def.defName));
        private static void Write(string phase, string text)
        {
            Directory.CreateDirectory(Path.Combine(GenFilePaths.SaveDataFolderPath, "Probe"));
            File.WriteAllText(Path.Combine(GenFilePaths.SaveDataFolderPath, "Probe", phase + ".txt"), text);
            Log.Message("[SAR hot-add] " + text);
        }
    }
}
