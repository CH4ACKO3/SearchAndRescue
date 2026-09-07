using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using Verse;
using Verse.AI;

namespace SearchAndRescue
{
    // Explicit actions for disposable debug colonies; no automatic execution.
    internal static class CombinationWorkflowDiagnostics
    {
        private static Pawn doctor, patient;
        private static Thing blood;
        private static object settings;
        private static FieldInfo integration;
        private static bool originalIntegration;
        private static int startedAt;

        [DebugAction("Search and Rescue", "Start combined native transfusion fixture",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Start()
        {
            if (settings != null) throw new InvalidOperationException("Finish the existing transfusion fixture first.");
            if (!Compatibility.UsesMoreInjuries || !Compatibility.UsesHemogenTransfusion)
                throw new InvalidOperationException("More Injuries and Hemogen emergency transfusion are required.");
            settings = AccessTools.Property(AccessTools.TypeByName("MoreInjuries.MoreInjuriesMod"), "Settings")
                .GetValue(null, null);
            integration = AccessTools.GetDeclaredFields(settings.GetType()).First(field =>
                field.FieldType == typeof(bool) && field.Name.StartsWith("_biotechEnableIntegration", StringComparison.OrdinalIgnoreCase));
            originalIntegration = (bool)integration.GetValue(settings);
            integration.SetValue(settings, true);
            try
            {
                Map map = Find.CurrentMap;
                // This fixture intentionally unlocks treatment research on the disposable map.
                foreach (ResearchProjectDef research in DefDatabase<ResearchProjectDef>.AllDefsListForReading)
                    Find.ResearchManager.FinishProject(research, false, null, false);
                doctor = Spawn(map, "Combination Doctor", -4, true);
                patient = Spawn(map, "Combination Patient", 0, false);
                doctor.skills.GetSkill(SkillDefOf.Medicine).Level = 15;
                patient.playerSettings.medCare = MedicalCareCategory.Best;
                patient.health.AddHediff(HediffDefOf.Anesthetic);
                Hediff wound = HediffMaker.MakeHediff(HediffDefOf.Cut, patient, patient.RaceProps.body.corePart);
                wound.Severity = 2f;
                patient.health.AddHediff(wound);
                AccessTools.Method(AccessTools.TypeByName("ChooseYourMedicine.DrawButton"), "MakeNewHediffEntry")
                    ?.Invoke(null, new object[] { wound, patient, MedicalCareCategory.Best, false });
                patient.health.AddHediff(HediffDefOf.BloodLoss).Severity = 0.65f;
                blood = ThingMaker.MakeThing(Compatibility.HemogenPack);
                blood.stackCount = 3;
                doctor.inventory.innerContainer.TryAdd(blood);
                var plan = MedicalCarePlan.Build(patient, Find.TickManager.TicksGame);
                var shared = plan.Demands.Where(d => d.ResourceDef == blood.def).ToList();
                Check(shared.Any(d => d.Intervention == MedicalIntervention.Blood) &&
                    shared.Any(d => d.Intervention == MedicalIntervention.HemogenTransfusion), "both native providers offer the shared blood resource");
                Check(shared.Count > 0 && CombinationResourceRules.SharedBudget(shared, d => d.IsResuscitation, d => d.Count) ==
                    shared.Max(d => d.Count), "shared transfusion budget uses the maximum rather than summing providers");
                var options = Compatibility.FindTreatmentOptions(doctor, patient, plan, new MedicalResourceLedger(map));
                var selected = options.FirstOrDefault(o => o.Intervention == MedicalIntervention.Blood && o.Resource == blood);
                Check(selected != null && !options.Any(o => o.Intervention == MedicalIntervention.HemogenTransfusion && o.Resource == blood),
                    "MI wins the same physical resource without a duplicate direct-transfusion option");
                Job job = selected == null ? null : Compatibility.MakeTreatmentRoundJob(doctor, patient, selected);
                Check(job?.def.defName == "UseBloodBag", "dispatcher constructs native MI blood job");
                if (job == null) throw new InvalidOperationException("No native transfusion job available.");
                startedAt = Find.TickManager.TicksGame;
                doctor.drafter.Drafted = true;
                doctor.jobs.StartJob(job, JobCondition.InterruptForced);
                Log.Message("[SAR combined workflow] START: " + doctor.ThingID + " -> " + patient.ThingID +
                    "; blood=" + blood.ThingID + "; count=3; loss=0.65. Advance 1200 ticks then finish.");
            }
            catch { Restore(); throw; }
        }

        [DebugAction("Search and Rescue", "Finish combined native transfusion fixture",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Finish()
        {
            if (settings == null) throw new InvalidOperationException("Start the transfusion fixture first.");
            try
            {
                int remaining = doctor.inventory.innerContainer.TotalStackCountOfDef(Compatibility.HemogenPack);
                float loss = patient.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss)?.Severity ?? 0f;
                Check(Find.TickManager.TicksGame - startedAt >= 1200, "native workflow observed for at least 1200 ticks");
                Check(!patient.Dead && loss < 0.45f, "native transfusion materially reduces blood loss");
                Check(remaining < 3 && remaining > 0, "native transfusion consumes a bounded part of the carried stack");
                Check(doctor.CurJobDef?.defName != "UseBloodBag", "native transfusion job terminates");
                Log.Message("[SAR combined workflow] END: remaining=" + remaining + "; loss=" + loss + "; job=" + doctor.CurJobDef?.defName);
            }
            finally { Restore(); }
        }

        private static void Restore()
        {
            integration?.SetValue(settings, originalIntegration);
            settings = null;
            integration = null;
        }

        private static Pawn Spawn(Map map, string name, int offset, bool caring)
        {
            Pawn pawn;
            do
            {
                pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                if (!caring || !pawn.WorkTagIsDisabled(WorkTags.Caring)) break;
                pawn.Destroy();
            } while (true);
            pawn.Name = new NameTriple("SAR", name, "Test");
            GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(map.Center + new IntVec3(offset, 0, 0), map, 2), map);
            foreach (WorkTypeDef work in DefDatabase<WorkTypeDef>.AllDefsListForReading)
                Compatibility.SetWorkPriorityForMigration(pawn, work, 0);
            if (caring)
            {
                Compatibility.SetWorkPriorityForMigration(pawn, WorkTypeDefOf.Doctor, 1);
                Compatibility.SetWorkPriorityForMigration(pawn, SearchAndRescueDefOf.SAR_FieldRescue, 1);
            }
            return pawn;
        }

        private static void Check(bool passed, string label) =>
            Log.Message("[SAR combined workflow] " + (passed ? "PASS: " : "FAIL: ") + label);
    }
}
