using System;
using System.Collections.Generic;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using Verse;

namespace SearchAndRescue
{
    internal static class HardworkingRegressionDiagnostics
    {
        [DebugAction("Search and Rescue", "Run Hardworking permission regressions",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Run()
        {
            if (!HardworkingCompatibility.Ready)
            {
                Log.Message("[SAR Hardworking regression] SKIP: framework not loaded");
                return;
            }
            // A disposable vanilla animal with the real framework comp isolates permission
            // checks from species art/Defs. No ticks run and no save is written.
            Pawn pawn = PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("Husky"), Faction.OfPlayer);
            Type setup = AccessTools.TypeByName("Kz.HardworkingSetup");
            var races = (HashSet<ThingDef>)setup.GetField("AllHardWorkerRaceDefs").GetValue(null);
            bool added = races.Add(pawn.def);
            Type settings = AccessTools.TypeByName("Kz.HardworkingSettings");
            var changed = new Dictionary<string, object>();
            Action<string, object> setting = (name, value) =>
            {
                var field = settings.GetField(name);
                if (!changed.ContainsKey(name)) changed.Add(name, field.GetValue(null));
                field.SetValue(null, value);
            };
            try
            {
                var comp = (ThingComp)Activator.CreateInstance(AccessTools.TypeByName("Kz.CompHardworking"));
                comp.parent = pawn;
                var props = (CompProperties)Activator.CreateInstance(AccessTools.TypeByName("Kz.CompProperties_Hardworking"));
                comp.Initialize(props);
                pawn.AllComps.Add(comp);
                // Explicitly grant the tested work types, without teaching vanilla Rescue.
                props.GetType().GetField("firstlyWorkTypes").SetValue(props,
                    new List<WorkTypeDef> { SearchAndRescueDefOf.SAR_FieldRescue, WorkTypeDefOf.Doctor, WorkTypeDefOf.Hauling });
                pawn.workSettings = new Pawn_WorkSettings(pawn);
                pawn.workSettings.EnableAndInitializeIfNotAlreadyInitialized();
                pawn.workSettings.SetPriority(SearchAndRescueDefOf.SAR_FieldRescue, 3);
                pawn.workSettings.SetPriority(WorkTypeDefOf.Doctor, 3);
                pawn.workSettings.SetPriority(WorkTypeDefOf.Hauling, 3);
                pawn.needs.rest.CurLevelPercentage = 1f;
                GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(Find.CurrentMap.Center, Find.CurrentMap, 8), Find.CurrentMap);
                setting("enableGlobalHardWorkerCanWork", true);
                setting("enableGlobalChanceWorkMode", false);
                setting("setGlobalWorkLimiterMinRest", 0.35f);
                Action<string, object> state = (name, value) => comp.GetType().GetField(name).SetValue(comp, value);
                var coordinator = Find.CurrentMap.GetComponent<SearchAndRescueCoordinator>();
                Check(!Compatibility.IsTrainedRescueAnimal(pawn), "fixture has no Rescue training");
                Check(coordinator.CanToggleFieldResponder(pawn) && coordinator.IsFieldResponder(pawn), "animal joins through work settings");
                Check(HardworkingCompatibility.CanWorkNow(pawn), "native work permission admitted");
                Check(Compatibility.CanPerformTreatmentWork(pawn), "doctor lane admitted");
                Check(Compatibility.RescueProviderFor(pawn) == RescueWorkProvider.Hauling, "uses hauling provider");
                state("curStopWork", true);
                Check(!HardworkingCompatibility.CanWorkNow(pawn), "personal stop respected");
                state("curStopWork", false);
                setting("enableGlobalHardWorkerCanWork", false);
                Check(!HardworkingCompatibility.CanWorkNow(pawn), "global stop respected");
                setting("enableGlobalHardWorkerCanWork", true);
                state("curInteractMarkInt", Find.TickManager.TicksGame + 100);
                Check(!HardworkingCompatibility.CanWorkNow(pawn), "interaction cooldown respected");
                state("curInteractMarkInt", 0);
                state("curWorkHasChance", true);
                Check(!HardworkingCompatibility.CanWorkNow(pawn), "chance work left to native AI");
                state("curWorkHasChance", false);
                setting("enableGlobalChanceWorkMode", true);
                Check(!HardworkingCompatibility.CanWorkNow(pawn), "global chance mode respected");
                setting("enableGlobalChanceWorkMode", false);
                state("curWorkAtNight", true);
                setting("enableGlobalWorkAtNightMust", true);
                bool night = (bool)AccessTools.Method("Kz.HardworkingUtility:IsWorkTimeAtNight")
                    .Invoke(null, new object[] { pawn });
                Check(HardworkingCompatibility.CanWorkNow(pawn) == night, "strict night schedule respected");
                state("curWorkAtNight", false);
                pawn.needs.rest.CurLevelPercentage = 0.1f;
                Check(!HardworkingCompatibility.CanWorkNow(pawn), "rest floor respected");
                pawn.needs.rest.CurLevelPercentage = 1f;
                pawn.workSettings.SetPriority(WorkTypeDefOf.Doctor, 0);
                Check(!Compatibility.CanPerformTreatmentWork(pawn), "disabled doctor work respected");
                pawn.workSettings.SetPriority(WorkTypeDefOf.Hauling, 0);
                Check(!Compatibility.CanPerformRescueWork(pawn), "disabled hauling respected");
                var rescue = DefDatabase<TrainableDef>.GetNamed("Rescue");
                for (int i = 0; i < rescue.steps; i++) pawn.training.Train(rescue, null, true);
                Check(Compatibility.IsTrainedRescueAnimal(pawn), "fixture learned Rescue");
                Check(!Compatibility.CanPerformRescueWork(pawn), "Rescue training cannot bypass disabled hauling");
                pawn.workSettings.SetPriority(WorkTypeDefOf.Doctor, 3);
                props.GetType().GetField("disableWorkTypes").SetValue(props,
                    new List<WorkTypeDef> { WorkTypeDefOf.Doctor });
                pawn.Notify_DisabledWorkTypesChanged();
                Check(!Compatibility.CanPerformTreatmentWork(pawn), "framework work-type prohibition respected");
                coordinator.SetFieldResponder(pawn, false);
                Check(!coordinator.IsFieldResponder(pawn), "field work opt-out respected");
            }
            finally
            {
                foreach (var pair in changed) settings.GetField(pair.Key).SetValue(null, pair.Value);
                pawn.Destroy();
                if (added) races.Remove(pawn.def);
                AccessTools.Method("Kz.HardworkingData:ClearCache").Invoke(null, null);
            }
        }

        private static void Check(bool pass, string label) =>
            Log.Message("[SAR Hardworking regression] " + (pass ? "PASS: " : "FAIL: ") + label);
    }
}
