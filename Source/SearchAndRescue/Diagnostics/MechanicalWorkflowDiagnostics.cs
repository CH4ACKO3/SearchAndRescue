using System;
using LudeonTK;
using RimWorld;
using Verse;
using Verse.AI;

namespace SearchAndRescue
{
    internal static class MechanicalWorkflowDiagnostics
    {
        private static Pawn worker, patient;
        private static float initialDamage;
        private static Designation testPoint;
        private static IntVec3 destination;

        [DebugAction("Search and Rescue", "Start mechanical evacuation workflow", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void StartEvacuation()
        {
            Cleanup();
            try
            {
                Map map = Find.CurrentMap;
                worker = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                patient = PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("Mech_Lifter"), Faction.OfPlayer);
                GenSpawn.Spawn(patient, CellFinder.RandomClosewalkCellNear(map.Center, map, 8), map);
                GenSpawn.Spawn(worker, CellFinder.RandomClosewalkCellNear(patient.Position, map, 1), map);
                patient.health.AddHediff(HediffDefOf.Anesthetic);
                if (!patient.Downed) throw new InvalidOperationException("fixture is not downed");
                testPoint = new Designation(CellFinder.RandomClosewalkCellNear(patient.Position, map, 6),
                    SearchAndRescueDefOf.SAR_RescuePoint);
                map.designationManager.AddDesignation(testPoint);
                if (!new Designator_Rescue().CanDesignateThing(patient).Accepted ||
                    !RescueDestinationPlanner.TryFind(map, worker, patient, out Building_Bed bed, out destination) || bed != null)
                    throw new InvalidOperationException("mechanical rescue point admission failed");
                Job job = JobMaker.MakeJob(SearchAndRescueDefOf.SAR_EvacuateToPoint, patient, destination);
                job.count = 1;
                worker.jobs.StartJob(job, JobCondition.InterruptForced);
                Log.Message("[SAR mechanical evacuation] START: " + patient.ThingID + " -> " + destination);
            }
            catch (Exception error) { Cleanup(); Log.Error("[SAR mechanical evacuation] FAIL: " + error); }
        }

        [DebugAction("Search and Rescue", "Finish mechanical evacuation workflow", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void FinishEvacuation()
        {
            try
            {
                if (patient == null || !patient.Spawned || worker.carryTracker.CarriedThing != null ||
                    !RescueDestinationPlanner.RescueCompleted(patient, destination, null))
                    throw new InvalidOperationException("patient has not reached rescue point");
                Log.Message("[SAR mechanical evacuation] PASS: downed mech delivered and released at rescue point");
            }
            catch (Exception error) { Log.Error("[SAR mechanical evacuation] FAIL: " + error); }
            finally { Cleanup(); }
        }

        [DebugAction("Search and Rescue", "Start mechanical repair workflow", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Start()
        {
            Cleanup();
            try
            {
                Map map = Find.CurrentMap;
                worker = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                patient = PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("Mech_Lifter"), Faction.OfPlayer);
                GenSpawn.Spawn(patient, CellFinder.RandomClosewalkCellNear(map.Center, map, 8), map);
                GenSpawn.Spawn(worker, CellFinder.RandomClosewalkCellNear(patient.Position, map, 1), map);
                worker.health.AddHediff(HediffDefOf.MechlinkImplant, worker.health.hediffSet.GetBrain());
                worker.mechanitor = worker.mechanitor ?? new Pawn_MechanitorTracker(worker);
                foreach (WorkTypeDef type in DefDatabase<WorkTypeDef>.AllDefsListForReading)
                    Compatibility.SetWorkPriorityForMigration(worker, type, 0);
                Compatibility.SetWorkPriorityForMigration(worker, SearchAndRescueDefOf.SAR_FieldRescue, 1);
                Compatibility.SetWorkPriorityForMigration(worker, WorkTypeDefOf.Smithing, 1);
                patient.TryGetComp<CompMechRepairable>().autoRepair = true;
                Hediff injury = HediffMaker.MakeHediff(HediffDefOf.Cut, patient, patient.RaceProps.body.corePart);
                injury.Severity = 10f;
                patient.health.AddHediff(injury);
                initialDamage = MechanicalCare.Damage(patient);
                map.designationManager.AddDesignation(new Designation(patient, SearchAndRescueDefOf.SAR_Treat));
                var coordinator = map.GetComponent<SearchAndRescueCoordinator>();
                coordinator.NotifyStageDesignationAdded(patient, SearchAndRescueStage.Treat);
                worker.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait_Wander, 30), JobCondition.InterruptForced);
                coordinator.NotifyWorkerUndrafting(worker);
                Job repair = coordinator.TryIssueJob(worker, SearchAndRescueStage.Treat, RescueWorkProvider.None);
                if (repair?.def != JobDefOf.RepairMech)
                    throw new InvalidOperationException("scheduler did not issue RepairMech; native=" +
                        MechanicalCare.CanRepair(worker, patient) + "; " + coordinator.DebugDescribeScheduler());
                worker.jobs.StartJob(repair, JobCondition.InterruptForced);
                Log.Message("[SAR mechanical workflow] START: managed RepairMech; worker=" + worker.ThingID +
                    "; patient=" + patient.ThingID + "; damage=" + initialDamage);
            }
            catch (Exception error) { Cleanup(); Log.Error("[SAR mechanical workflow] FAIL: " + error); }
        }

        [DebugAction("Search and Rescue", "Finish mechanical repair workflow", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Finish()
        {
            try
            {
                if (patient == null || worker == null) throw new InvalidOperationException("start fixture first");
                float remaining = MechanicalCare.Damage(patient);
                if (patient.Dead || remaining >= initialDamage)
                    throw new InvalidOperationException("no native repair progress; currentJob=" + worker.CurJobDef?.defName);
                Log.Message("[SAR mechanical workflow] PASS: native job reduced damage " + initialDamage + " -> " + remaining);
                // A forced repair on the same target is an external ownership lease.
                var forced = JobMaker.MakeJob(JobDefOf.RepairMech, patient);
                forced.playerForced = true;
                worker.jobs.TryTakeOrderedJob(forced);
                if (!PatientOwnership.HasExternalOwner(patient, PatientJobRole.Treatment))
                    throw new InvalidOperationException("manual repair did not take ownership");
                Log.Message("[SAR mechanical workflow] PASS: manual repair takes patient ownership");
            }
            catch (Exception error) { Log.Error("[SAR mechanical workflow] FAIL: " + error); }
            finally { Cleanup(); }
        }

        private static void Cleanup()
        {
            if (testPoint != null) testPoint.designationManager?.RemoveDesignation(testPoint);
            testPoint = null;
            worker?.Destroy(DestroyMode.Vanish);
            patient?.Destroy(DestroyMode.Vanish);
            worker = patient = null;
        }
    }
}
