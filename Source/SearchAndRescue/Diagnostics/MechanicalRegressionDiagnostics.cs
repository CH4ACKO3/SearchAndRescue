using System;
using LudeonTK;
using RimWorld;
using Verse;
using Verse.AI;

namespace SearchAndRescue
{
    internal static class MechanicalRegressionDiagnostics
    {
        [DebugAction("Search and Rescue", "Run mechanical care regressions",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Run()
        {
            if (!ModsConfig.BiotechActive) { Log.Message("[SAR mechanical regression] SKIP: Biotech required"); return; }
            Pawn mech = null, worker = null;
            int passed = 0;
            Action<bool, string> check = (condition, name) =>
            {
                if (!condition) throw new InvalidOperationException(name);
                passed++;
                Log.Message("[SAR mechanical regression] PASS: " + name);
            };
            try
            {
                Map map = Find.CurrentMap;
                mech = PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("Mech_Lifter"), Faction.OfPlayer);
                worker = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                GenSpawn.Spawn(mech, CellFinder.RandomClosewalkCellNear(map.Center, map, 8), map);
                GenSpawn.Spawn(worker, CellFinder.RandomClosewalkCellNear(mech.Position, map, 2), map);
                mech.TryGetComp<CompMechRepairable>().autoRepair = true;
                check(TargetEligibility.CanReceiveFieldCare(mech), "colony mech accepted as patient");
                check(!TargetEligibility.CanBeCaptured(mech), "mech excluded from prisoner capture");
                check(!MechanicalCare.NeedsRepair(mech), "intact mech needs no repair");
                Hediff injury = HediffMaker.MakeHediff(HediffDefOf.Cut, mech, mech.RaceProps.body.corePart);
                injury.Severity = 5f;
                mech.health.AddHediff(injury);
                check(MechanicalCare.NeedsRepair(mech), "damage activates repair");
                MedicalCarePlan plan = MedicalCarePlan.Build(mech, Find.TickManager.TicksGame);
                check(plan.Demands.Count == 0 && plan.EssentialMedicineRounds == 0, "repair reserves no medicine or blood");
                worker.workSettings.EnableAndInitializeIfNotAlreadyInitialized();
                worker.workSettings.SetPriority(SearchAndRescueDefOf.SAR_FieldRescue, 3);
                worker.workSettings.SetPriority(WorkTypeDefOf.Smithing, 3);
                check(!MechanicalCare.CanRepairWork(worker), "ordinary doctor cannot replace mechanitor");
                worker.health.AddHediff(HediffDefOf.MechlinkImplant, worker.health.hediffSet.GetBrain());
                worker.mechanitor = worker.mechanitor ?? new Pawn_MechanitorTracker(worker);
                check(MechanicalCare.CanRepair(worker, mech), "mechanitor uses native repair provider");
                worker.workSettings.SetPriority(WorkTypeDefOf.Smithing, 0);
                check(!MechanicalCare.CanRepairWork(worker), "smithing disabled blocks repair");
                worker.workSettings.SetPriority(WorkTypeDefOf.Smithing, 3);
                worker.workSettings.SetPriority(SearchAndRescueDefOf.SAR_FieldRescue, 0);
                check(!MechanicalCare.CanRepairWork(worker), "field rescue disabled blocks repair");
                worker.workSettings.SetPriority(SearchAndRescueDefOf.SAR_FieldRescue, 3);
                mech.TryGetComp<CompMechRepairable>().autoRepair = false;
                check(!MechanicalCare.NeedsRepair(mech), "auto repair toggle respected");
                mech.TryGetComp<CompMechRepairable>().autoRepair = true;
                var options = Compatibility.FindTreatmentOptions(worker, mech, plan, new MedicalResourceLedger(map));
                check(options.Count == 1 && options[0].Intervention == MedicalIntervention.MechRepair,
                    "mechanical patient only receives repair intervention");
                Job job = Compatibility.MakeTreatmentRoundJob(worker, mech, options[0]);
                check(job?.def == JobDefOf.RepairMech && !job.playerForced, "native automatic repair job constructed");
                check(CompatibilityRegistry.PatientFor(worker, job, PatientJobRole.Treatment) == mech,
                    "repair target participates in treatment ownership");
                float damage = MechanicalCare.Damage(mech);
                MechRepairUtility.RepairTick(mech);
                check(MechanicalCare.Damage(mech) < damage, "native repair progress observable by scheduler");
                mech.SetFaction(Faction.OfMechanoids);
                check(!TargetEligibility.CanReceiveFieldCare(mech) && !MechanicalCare.CanRepair(worker, mech),
                    "enemy mech excluded from automatic repair");
                Log.Message("[SAR mechanical regression] COMPLETE: " + passed + " passed");
            }
            catch (Exception error) { Log.Error("[SAR mechanical regression] FAIL after " + passed + ": " + error); }
            finally
            {
                worker?.Destroy(DestroyMode.Vanish);
                mech?.Destroy(DestroyMode.Vanish);
            }
        }
    }
}
