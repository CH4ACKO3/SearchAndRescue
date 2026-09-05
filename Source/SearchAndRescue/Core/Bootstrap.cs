using HarmonyLib;
using Verse;

namespace SearchAndRescue
{
    [StaticConstructorOnStartup]
    internal static class Bootstrap
    {
        static Bootstrap()
        {
            CompatibilityRegistry.Initialize();
            Harmony harmony = new Harmony("CH4AcKO3.SearchAndRescue");
            harmony.PatchAll();
            Compatibility.RegisterPriorityTreatmentJobs();
            PriorityTreatmentCompatibility.Install(harmony);
            Log.Message("[Search and Rescue] Loaded for RimWorld 1.6. Active compatibility: " +
                Compatibility.ActiveCompatibilitySummary() + ".");
        }
    }
}
