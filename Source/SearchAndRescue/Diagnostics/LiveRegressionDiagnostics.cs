using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using Verse;
using Verse.AI;

namespace SearchAndRescue
{
    // Explicit developer actions for disposable maps. These exercise production methods
    // and actual JobDrivers rather than duplicating their rules in a simulation.
    internal static class LiveRegressionDiagnostics
    {
        private static readonly List<Pawn> supplyPawns = new List<Pawn>();
        private static Pawn supplier;
        private static Pawn donor;
        private static Pawn recipient;
        private static Thing source;
        private static bool sourceMoved;
        private static int startedAt;
        private static int initialGroundCount;
        private static Map supplyMap;

        [DebugAction("Search and Rescue", "Run external transport regressions",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ExternalTransport()
        {
            Map map = Find.CurrentMap;
            Pawn carrier = Spawn(map, -6);
            Pawn patient = Spawn(map, 0);
            var mark = new Designation(patient, SearchAndRescueDefOf.SAR_Rescue);
            map.designationManager.AddDesignation(mark);
            Job previous = carrier.CurJob;
            try
            {
                HealthUtility.TryAnesthetize(patient);
                Job kidnap = JobMaker.MakeJob(JobDefOf.Kidnap, patient, Cell(map, 16));
                // Isolate the actual maintenance method, without running the exit-map toil.
                carrier.jobs.curJob = kidnap;
                patient.DeSpawn();
                carrier.carryTracker.TryStartCarry(patient);
                Check(CompatibilityRegistry.PatientFor(carrier, kidnap, PatientJobRole.Transport) == patient,
                    "NOLB Kidnap is recognized as patient transport");
                var cleanup = AccessTools.Method(typeof(SearchAndRescueCoordinator), "CleanupOrphanedManagedCarries");
                cleanup.Invoke(map.GetComponent<SearchAndRescueCoordinator>(), null);
                Check(carrier.carryTracker.CarriedThing == patient,
                    "maintenance preserves externally carried marked patient");
                carrier.jobs.curJob = JobMaker.MakeJob(JobDefOf.Wait);
                cleanup.Invoke(map.GetComponent<SearchAndRescueCoordinator>(), null);
                Check(carrier.carryTracker.CarriedThing == null && patient.Spawned,
                    "maintenance still drops orphaned carry without transport job");

                var giver = new WorkGiver_RescueDowned();
                Check(giver.JobOnThing(carrier, patient) == null,
                    "marked guest rescue job construction is blocked");
                Check(giver.JobOnThing(carrier, patient, true) != null,
                    "forced rescue job construction remains available");
                // Hospitality can supply true from a prefix while skipping vanilla.
                // Exercise our final scanner boundary with exactly that result.
                var gate = AccessTools.Method(typeof(WorkGiverRescueDowned_SearchAndRescueOrderPatch), "Postfix");
                object[] args = { patient, false, true };
                gate.Invoke(null, args);
                Check(!(bool)args[2], "third-party positive scanner result respects SAR mark");
                args = new object[] { patient, true, true };
                gate.Invoke(null, args);
                Check((bool)args[2], "forced scanner result remains available");
                map.designationManager.RemoveDesignation(mark);
                mark = null;
                args = new object[] { patient, false, true };
                gate.Invoke(null, args);
                Check((bool)args[2] && giver.JobOnThing(carrier, patient) != null,
                    "unmarked rescue remains available");
            }
            finally
            {
                carrier.jobs.curJob = previous;
                if (mark != null) map.designationManager.RemoveDesignation(mark);
                if (carrier.carryTracker.CarriedThing == patient)
                    carrier.carryTracker.TryDropCarriedThing(carrier.Position, ThingPlaceMode.Near, out _);
                if (!patient.Destroyed) patient.Destroy();
                if (!carrier.Destroyed) carrier.Destroy();
            }
        }

        [DebugAction("Search and Rescue", "Run rescue destination regressions",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void RescueDestinations()
        {
            Map map = Find.CurrentMap;
            var hiddenBeds = map.listerThings.ThingsInGroup(ThingRequestGroup.Bed)
                .OfType<Building_Bed>().Select(b => new { Bed = b, Cell = b.Position, Rotation = b.Rotation }).ToList();
            var points = map.designationManager.SpawnedDesignationsOfDef(SearchAndRescueDefOf.SAR_RescuePoint).ToList();
            Pawn worker = null;
            Pawn patient = null;
            Building_Bed bed = null;
            Designation testPoint = null;
            try
            {
                // No ticks elapse while the native destination search is isolated.
                foreach (var old in hiddenBeds) old.Bed.DeSpawn();
                foreach (Designation point in points) map.designationManager.RemoveDesignation(point);
                worker = Spawn(map, -6);
                patient = Spawn(map, 0);
                HealthUtility.TryAnesthetize(patient);
                bed = (Building_Bed)ThingMaker.MakeThing(ThingDefOf.SleepingSpot);
                bed.SetFaction(Faction.OfPlayer);
                GenSpawn.Spawn(bed, Cell(map, 10), map);
                bed.Medical = false;
                Check(Compatibility.FindBestRescueBed(patient, worker) == bed,
                    "ordinary bed is selected by production search");
                Check(Compatibility.IsSafeRescueBed(bed, patient),
                    "ordinary bed satisfies production delivery contract");
                bed.Medical = true;
                Check(Compatibility.IsSafeRescueBed(bed, patient), "medical bed remains valid");
                bed.DeSpawn();

                testPoint = new Designation(patient.Position, SearchAndRescueDefOf.SAR_RescuePoint);
                map.designationManager.AddDesignation(testPoint);
                Check(!HasRoute(map, worker, patient), "no repeated route at existing rescue point");
                map.designationManager.RemoveDesignation(testPoint);
                testPoint = new Designation(Cell(map, 16), SearchAndRescueDefOf.SAR_RescuePoint);
                map.designationManager.AddDesignation(testPoint);
                Check(HasRoute(map, worker, patient), "changed rescue point enables transport");
                map.designationManager.RemoveDesignation(testPoint);
                testPoint = new Designation(patient.Position, SearchAndRescueDefOf.SAR_RescuePoint);
                map.designationManager.AddDesignation(testPoint);
                GenSpawn.Spawn(bed, Cell(map, 10), map);
                Check(HasRoute(map, worker, patient), "new bed enables onward transport from rescue point");
            }
            finally
            {
                if (testPoint != null) map.designationManager.RemoveDesignation(testPoint);
                if (bed != null && !bed.Destroyed) bed.Destroy();
                if (worker != null && !worker.Destroyed) worker.Destroy();
                if (patient != null && !patient.Destroyed) patient.Destroy();
                foreach (var old in hiddenBeds)
                    if (!old.Bed.Spawned && !old.Bed.Destroyed)
                        GenSpawn.Spawn(old.Bed, old.Cell, map, old.Rotation);
                foreach (Designation point in points) map.designationManager.AddDesignation(point);
                map.GetComponent<SearchAndRescueCoordinator>().NotifyRescuePointChanged();
            }
        }

        private static bool HasRoute(Map map, Pawn worker, Pawn patient)
        {
            object[] args = { worker, patient, null, IntVec3.Invalid };
            return (bool)AccessTools.Method(typeof(SearchAndRescueCoordinator), "TryFindRescueDestination")
                .Invoke(map.GetComponent<SearchAndRescueCoordinator>(), args);
        }

        [DebugAction("Search and Rescue", "Prepare held-supply regression",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void PrepareHeldSupply() => PrepareSupply(false);

        [DebugAction("Search and Rescue", "Prepare changed-owner supply regression",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void PrepareChangedOwner() => PrepareSupply(true);

        private static void PrepareSupply(bool moveSource)
        {
            CleanupSupply();
            Map map = Find.CurrentMap;
            supplyMap = map;
            supplier = Spawn(map, -12);
            donor = Spawn(map, -4);
            recipient = Spawn(map, 14);
            supplyPawns.AddRange(new[] { supplier, donor, recipient });
            HealthUtility.TryAnesthetize(recipient);
            initialGroundCount = GroundMedicineCount();
            Job wait = JobMaker.MakeJob(JobDefOf.Wait);
            wait.expiryInterval = 5000;
            wait.playerForced = true;
            donor.jobs.StartJob(wait, JobCondition.InterruptForced);
            source = ThingMaker.MakeThing(ThingDefOf.MedicineIndustrial);
            source.stackCount = 4;
            donor.inventory.innerContainer.TryAdd(source);
            sourceMoved = moveSource;
            startedAt = Find.TickManager.TicksGame;
            Job delivery = JobMaker.MakeJob(SearchAndRescueDefOf.SAR_DeliverMedicalSupply, source, recipient);
            delivery.count = 2;
            delivery.playerForced = true;
            supplier.jobs.StartJob(delivery, JobCondition.InterruptForced);
            if (moveSource)
            {
                donor.inventory.innerContainer.Remove(source);
                GenSpawn.Spawn(source, donor.Position, map);
            }
            Log.Message("[SAR live regression] prepared " + (moveSource ? "changed-owner" : "held-supply") +
                        "; advance 1200 ticks then Check supply regression. source=" + source.ThingID);
        }

        [DebugAction("Search and Rescue", "Check supply regression",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void CheckSupply()
        {
            if (recipient?.Spawned != true || recipient.Map != Find.CurrentMap)
            {
                Log.Message("[SAR live regression] NOT RUN: prepare the supply fixture on this map first.");
                return;
            }
            int delivered = GroundMedicineCount() - initialGroundCount;
            bool stillDelivering = supplier.CurJobDef == SearchAndRescueDefOf.SAR_DeliverMedicalSupply;
            Check(Find.TickManager.TicksGame - startedAt >= 1200, "supply observation covers 1200 ticks");
            Check(!stillDelivering, "supply driver terminates");
            Check(sourceMoved ? delivered == 0 && source.stackCount == 4 : delivered == 2 && source.stackCount == 2,
                sourceMoved ? "changed-owner pickup rejected without moving inventory" : "held inventory split delivered exactly two units");
            Log.Message("[SAR live regression] delivery detail: ground=" + delivered +
                        " source=" + source.stackCount + " job=" + supplier.CurJobDef?.defName);
        }

        [DebugAction("Search and Rescue", "Cleanup supply regression",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void CleanupSupply()
        {
            // Static debug state survives loading another save. Those old game objects
            // must not be destroyed through the newly loaded game's global caches.
            if (supplyMap != null && Find.Maps.Contains(supplyMap))
            {
                foreach (Pawn pawn in supplyPawns)
                    if (pawn != null && !pawn.Destroyed) pawn.Destroy();
                if (source != null && !source.Destroyed) source.Destroy();
            }
            supplyPawns.Clear();
            supplier = donor = recipient = null;
            source = null;
            supplyMap = null;
        }

        private static int GroundMedicineCount() =>
            GenRadial.RadialDistinctThingsAround(recipient.Position, recipient.Map, 2f, true)
                .Where(t => t.def == ThingDefOf.MedicineIndustrial && t != source).Sum(t => t.stackCount);

        private static Pawn Spawn(Map map, int offset)
        {
            Pawn pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
            GenSpawn.Spawn(pawn, Cell(map, offset), map);
            pawn.workSettings.EnableAndInitialize();
            foreach (WorkTypeDef work in DefDatabase<WorkTypeDef>.AllDefsListForReading)
                if (!pawn.WorkTypeIsDisabled(work)) pawn.workSettings.SetPriority(work, 0);
            return pawn;
        }

        private static IntVec3 Cell(Map map, int offset)
        {
            return GenRadial.RadialCellsAround(map.Center + new IntVec3(offset, 0, 0), 12f, true)
                .First(c => c.InBounds(map) && c.Standable(map) && c.GetFirstPawn(map) == null &&
                            c.GetEdifice(map) == null);
        }

        private static void Check(bool passed, string label)
        {
            // A failing regression is a result, not an engine exception; report every check.
            Log.Message("[SAR live regression] " + (passed ? "PASS: " : "FAIL: ") + label);
        }
    }
}
