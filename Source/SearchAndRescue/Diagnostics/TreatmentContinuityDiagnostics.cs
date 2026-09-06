using System;
using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using Verse;
using Verse.AI;

namespace SearchAndRescue
{
    internal static class TreatmentContinuityDiagnostics
    {
        private static readonly List<Pawn> patients = new List<Pawn>();
        private static readonly Dictionary<Pawn, List<string>> rounds = new Dictionary<Pawn, List<string>>();
        private static readonly Dictionary<Pawn, HashSet<int>> jobs = new Dictionary<Pawn, HashSet<int>>();
        private static MedicalCoordinationMode originalMode;
        private static bool observing;
        private static Pawn urgentPatient;
        private static readonly HashSet<string> priorDoctors = new HashSet<string>();

        [DebugAction("Search and Rescue", "Start all-tending continuity fixture", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void StartAll() => Start(MedicalCoordinationMode.AllTending);

        [DebugAction("Search and Rescue", "Start emergency continuity fixture", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void StartEmergency() => Start(MedicalCoordinationMode.EmergencyAuto);

        private static void Start(MedicalCoordinationMode mode)
        {
            if (observing) throw new InvalidOperationException("Finish the current continuity fixture first.");
            Map map = Find.CurrentMap;
            var coordinator = map.GetComponent<SearchAndRescueCoordinator>();
            originalMode = SearchAndRescueMod.Settings.MedicalCoordinationMode;
            SearchAndRescueMod.Settings.MedicalCoordinationMode = mode;
            patients.Clear(); rounds.Clear(); jobs.Clear(); urgentPatient = null; priorDoctors.Clear();
            // Use only on an independent debug colony. Existing residents keep their
            // work settings in their own save; this map isolates the three candidate rows.
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned.ToList())
                if (coordinator.IsFieldResponder(pawn)) coordinator.SetFieldResponder(pawn, false);
            observing = true;
            for (int i = 0; i < 3; i++)
            {
                Pawn doctor = Generate();
                doctor.Name = new NameTriple("SAR", "Continuity Doctor " + i, "Test");
                GenSpawn.Spawn(doctor, CellFinder.RandomClosewalkCellNear(map.Center, map, 4), map);
                foreach (WorkTypeDef work in DefDatabase<WorkTypeDef>.AllDefsListForReading)
                    Compatibility.SetWorkPriorityForMigration(doctor, work, 0);
                doctor.skills.GetSkill(SkillDefOf.Medicine).Level = 12;
                if (mode == MedicalCoordinationMode.EmergencyAuto)
                {
                    Thing medicine = ThingMaker.MakeThing(ThingDefOf.MedicineIndustrial);
                    medicine.stackCount = 12;
                    doctor.inventory.innerContainer.TryAdd(medicine);
                }
                Compatibility.SetWorkPriorityForMigration(doctor, WorkTypeDefOf.Doctor, 1);
                coordinator.SetFieldResponder(doctor, true);
                doctor.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait_Wander, 1), JobCondition.InterruptForced);

                Pawn patient = Generate();
                patient.Name = new NameTriple("SAR", "Continuity Patient " + i, "Test");
                GenSpawn.Spawn(patient, CellFinder.RandomClosewalkCellNear(map.Center, map, 7), map);
                coordinator.SetFieldResponder(patient, false);
                patient.playerSettings.medCare = mode == MedicalCoordinationMode.AllTending
                    ? MedicalCareCategory.NoMeds : MedicalCareCategory.Best;
                foreach (Hediff hediff in patient.health.hediffSet.hediffs.ToList())
                    if (hediff is Hediff_Injury) patient.health.RemoveHediff(hediff);
                patient.health.AddHediff(HediffDefOf.Anesthetic);
                for (int wound = 0; wound < 8; wound++)
                {
                    Hediff injury = HediffMaker.MakeHediff(mode == MedicalCoordinationMode.AllTending
                        ? DefDatabase<HediffDef>.GetNamed("Bruise") : HediffDefOf.Cut, patient, patient.RaceProps.body.corePart);
                    injury.Severity = 4f;
                    patient.health.AddHediff(injury);
                }
                patients.Add(patient);
                rounds[patient] = new List<string>(); jobs[patient] = new HashSet<int>();
                coordinator.NotifyWorkerUndrafting(doctor);
            }
            Log.Message("[SAR continuity live] START mode=" + mode + "; 3 doctors, 3 downed patients, 8 wounds each; no manual treatment marks");
        }

        [DebugAction("Search and Rescue", "Inject urgent continuity patient", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void InjectUrgent()
        {
            if (!observing || urgentPatient != null) throw new InvalidOperationException("Start a fresh fixture first.");
            foreach (string doctor in rounds.Values.SelectMany(history => history)) priorDoctors.Add(doctor);
            if (priorDoctors.Count != 3) throw new InvalidOperationException("Wait for all three original doctors to commit a round first.");
            Map map = Find.CurrentMap;
            urgentPatient = Generate();
            urgentPatient.Name = new NameTriple("SAR", "Continuity Critical Arrival", "Test");
            GenSpawn.Spawn(urgentPatient, CellFinder.RandomClosewalkCellNear(map.Center, map, 3), map);
            var coordinator = map.GetComponent<SearchAndRescueCoordinator>();
            coordinator.SetFieldResponder(urgentPatient, false);
            urgentPatient.playerSettings.medCare = MedicalCareCategory.NoMeds;
            urgentPatient.health.AddHediff(HediffDefOf.Anesthetic);
            for (int i = 0; i < 4; i++)
            {
                Hediff cut = HediffMaker.MakeHediff(HediffDefOf.Cut, urgentPatient, urgentPatient.RaceProps.body.corePart);
                cut.Severity = 5f; urgentPatient.health.AddHediff(cut);
            }
            urgentPatient.health.AddHediff(HediffDefOf.BloodLoss).Severity = 0.55f;
            rounds[urgentPatient] = new List<string>(); jobs[urgentPatient] = new HashSet<int>();
            Log.Message("[SAR continuity live] INJECT critical patient=" + urgentPatient.ThingID +
                "; prior treating doctors=" + string.Join(",", priorDoctors));
        }

        internal static void Observe(Pawn doctor, Pawn patient)
        {
            if (!observing || patient == null || !rounds.TryGetValue(patient, out List<string> history)) return;
            history.Add(doctor.ThingID);
            jobs[patient].Add(doctor.CurJob.loadID);
            Log.Message("[SAR continuity live] ROUND patient=" + patient.ThingID + " doctor=" + doctor.ThingID +
                " job=" + doctor.CurJob.loadID + " round=" + history.Count);
        }

        [DebugAction("Search and Rescue", "Finish continuity fixture", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Finish()
        {
            if (!observing) return;
            foreach (Pawn patient in patients)
            {
                List<string> history = rounds[patient];
                bool continued = history.Count >= 3 && history.Take(3).Distinct().Count() == 1 && jobs[patient].Count >= 3;
                Log.Message("[SAR continuity live] " + (continued ? "PASS" : "REVIEW") + " patient=" + patient.ThingID +
                    " rounds=" + history.Count + " jobs=" + jobs[patient].Count + " doctors=" + string.Join(",", history));
            }
            if (urgentPatient != null)
                Log.Message("[SAR continuity live] " +
                    (rounds[urgentPatient].Any(priorDoctors.Contains) ? "PASS" : "REVIEW") +
                    " critical arrival treated by previous incumbent; doctors=" + string.Join(",", rounds[urgentPatient]));
            Log.Message(Find.CurrentMap.GetComponent<SearchAndRescueCoordinator>().DebugDescribeScheduler());
            SearchAndRescueMod.Settings.MedicalCoordinationMode = originalMode;
            observing = false;
        }

        private static Pawn Generate()
        {
            for (int attempt = 0; attempt < 40; attempt++)
            {
                Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,
                    Faction.OfPlayer, forceGenerateNewPawn: true, canGeneratePawnRelations: false));
                if (!pawn.WorkTypeIsDisabled(WorkTypeDefOf.Doctor) &&
                    !pawn.WorkTypeIsDisabled(SearchAndRescueDefOf.SAR_FieldRescue)) return pawn;
                pawn.Destroy(DestroyMode.Vanish);
            }
            throw new InvalidOperationException("No qualified fixture pawn generated.");
        }
    }
}

