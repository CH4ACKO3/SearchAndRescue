using HarmonyLib;
using Verse;
using RimWorld.Planet;

namespace SearchAndRescue
{
    // Installed before Def.PostLoad. Only explicit no-graphics benchmark processes opt in.
    internal static class EngineBenchmarkHeadless
    {
        internal static void Install()
        {
            if (!GenCommandLine.CommandLineArgPassed("sar-bench-worker") ||
                !GenCommandLine.CommandLineArgPassed("nographics")) return;
            var harmony = new Harmony("CH4AcKO3.SearchAndRescue.HeadlessBenchmark");
            var skip = new HarmonyMethod(typeof(EngineBenchmarkHeadless), nameof(Skip));
            harmony.Patch(AccessTools.Method(typeof(GlobalTextureAtlasManager), "TryInsertStatic"), prefix: skip);
            harmony.Patch(AccessTools.Method(typeof(GlobalTextureAtlasManager), "BakeStaticAtlases"), prefix: skip);
            harmony.Patch(AccessTools.Method(typeof(Root), "OnGUI"), prefix: skip);
            harmony.Patch(AccessTools.Method(typeof(Widgets), "CroppedTerrainTextureRect"), prefix: skip);
            harmony.Patch(AccessTools.PropertyGetter(typeof(WorldRendererUtility), "DrawingMap"), prefix: skip);
        }
        private static bool Skip() => false;
    }
}
