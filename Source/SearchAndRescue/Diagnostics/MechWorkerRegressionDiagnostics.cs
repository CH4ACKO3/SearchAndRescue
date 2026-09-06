using System;
using LudeonTK;
using RimWorld;
using Verse;
using Verse.AI;

namespace SearchAndRescue
{
    internal static class MechWorkerRegressionDiagnostics
    {
        [DebugAction("Search and Rescue", "Run mech worker regressions",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Run()
        {
            if (!ModsConfig.BiotechActive)
            {
                Log.Message("[SAR mech worker regression] SKIP: Biotech required");
                return;
            }

            Pawn mechanitor = null;
            Pawn lifter = null;
            Pawn paramedic = null;
            int passed = 0;
            Action<bool, string> check = (condition, label) =>
            {
                if (!condition) throw new InvalidOperationException(label);
                passed++;
                Log.Message("[SAR mech worker regression] PASS: " + label);
            };

            try
            {
                Map map = Find.CurrentMap;
                SearchAndRescueCoordinator coordinator = map.GetComponent<SearchAndRescueCoordinator>();
                mechanitor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                lifter = PawnGenerator.GeneratePawn(
                    DefDatabase<PawnKindDef>.GetNamed("Mech_Lifter"),
                    Faction.OfPlayer);
                paramedic = PawnGenerator.GeneratePawn(
                    DefDatabase<PawnKindDef>.GetNamed("Mech_Paramedic"),
                    Faction.OfPlayer);

                GenSpawn.Spawn(mechanitor, NearbyCell(map, map.Center, 8), map);
                GenSpawn.Spawn(lifter, NearbyCell(map, mechanitor.Position, 3), map);
                GenSpawn.Spawn(paramedic, NearbyCell(map, mechanitor.Position, 3), map);

                mechanitor.health.AddHediff(
                    HediffDefOf.MechlinkImplant,
                    mechanitor.health.hediffSet.GetBrain());
                mechanitor.mechanitor = new Pawn_MechanitorTracker(mechanitor);
                MechanitorControlGroup group = new MechanitorControlGroup(mechanitor.mechanitor);
                mechanitor.mechanitor.controlGroups.Add(group);
                mechanitor.relations.AddDirectRelation(PawnRelationDefOf.Overseer, lifter);
                mechanitor.relations.AddDirectRelation(PawnRelationDefOf.Overseer, paramedic);

                lifter.workSettings = lifter.workSettings ?? new Pawn_WorkSettings(lifter);
                paramedic.workSettings = paramedic.workSettings ?? new Pawn_WorkSettings(paramedic);
                lifter.workSettings.EnableAndInitializeIfNotAlreadyInitialized();
                paramedic.workSettings.EnableAndInitializeIfNotAlreadyInitialized();
                lifter.workSettings.SetPriority(WorkTypeDefOf.Hauling, 3);
                paramedic.workSettings.SetPriority(WorkTypeDefOf.Doctor, 3);

                check(Compatibility.IsColonyWorkMech(lifter) &&
                      Compatibility.IsColonyWorkMech(paramedic),
                    "fixtures are controlled colony work mechs");
                check(!coordinator.IsFieldResponder(lifter), "lifter roster defaults off");
                coordinator.SetFieldResponder(lifter, true);
                check(coordinator.IsFieldResponder(lifter), "lifter roster opt-in is retained");
                check(Compatibility.RescueProviderFor(lifter) == RescueWorkProvider.Hauling,
                    "opted-in lifter uses native hauling provider");
                lifter.workSettings.SetPriority(WorkTypeDefOf.Hauling, 0);
                check(lifter.workSettings.GetPriority(WorkTypeDefOf.Hauling) == 0,
                    "fixture native hauling priority changed to zero");
                check(!Compatibility.CanPerformSupplyWork(lifter) &&
                      !Compatibility.CanPerformRescueWork(lifter),
                    "disabled hauling blocks supply and rescue despite roster opt-in");
                lifter.workSettings.SetPriority(WorkTypeDefOf.Hauling, 3);
                coordinator.SetFieldResponder(lifter, false);
                check(!Compatibility.CanPerformRescueWork(lifter),
                    "roster opt-out blocks enabled hauling provider");

                coordinator.SetFieldResponder(paramedic, true);
                check(Compatibility.CanPerformTreatmentWork(paramedic) &&
                      Compatibility.RescueProviderFor(paramedic) == RescueWorkProvider.Paramedic,
                    "opted-in paramedic uses native doctor provider");
                paramedic.workSettings.SetPriority(WorkTypeDefOf.Doctor, 0);
                check(paramedic.workSettings.GetPriority(WorkTypeDefOf.Doctor) == 0,
                    "fixture native doctor priority changed to zero");
                check(!Compatibility.CanPerformTreatmentWork(paramedic) &&
                      !Compatibility.CanPerformRescueWork(paramedic),
                    "disabled doctor blocks treatment and paramedic rescue");
                paramedic.workSettings.SetPriority(WorkTypeDefOf.Doctor, 3);

                group.SetWorkMode(MechWorkModeDefOf.Work);
                check(WorkerEligibility.WorkerOperational(paramedic, map),
                    "vanilla Work mode admits scheduler");
                group.SetWorkMode(MechWorkModeDefOf.Recharge);
                check(!WorkerEligibility.WorkerOperational(paramedic, map),
                    "Recharge mode blocks scheduler");
                group.SetWorkMode(MechWorkModeDefOf.SelfShutdown);
                check(!WorkerEligibility.WorkerOperational(paramedic, map),
                    "SelfShutdown mode blocks scheduler");
                group.SetWorkMode(MechWorkModeDefOf.Escort);
                check(!WorkerEligibility.WorkerOperational(paramedic, map),
                    "Escort mode blocks scheduler");
                group.SetWorkMode(MechWorkModeDefOf.Work);

                Job forced = JobMaker.MakeJob(JobDefOf.Wait, 60);
                forced.playerForced = true;
                paramedic.jobs.jobQueue.EnqueueLast(forced);
                check(!WorkerEligibility.WorkerOperational(paramedic, map),
                    "queued player command blocks scheduler");
                paramedic.jobs.ClearQueuedJobs();

                coordinator.SetFieldResponder(paramedic, false);
                Log.Message("[SAR mech worker regression] COMPLETE: " + passed + " passed");
            }
            catch (Exception error)
            {
                Log.Error("[SAR mech worker regression] FAIL after " + passed + ": " + error);
            }
            finally
            {
                paramedic?.Destroy(DestroyMode.Vanish);
                lifter?.Destroy(DestroyMode.Vanish);
                mechanitor?.Destroy(DestroyMode.Vanish);
            }
        }

        private static IntVec3 NearbyCell(Map map, IntVec3 center, int radius)
        {
            return CellFinder.RandomClosewalkCellNear(center, map, radius);
        }
    }
}
