using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SearchAndRescue
{
    public sealed class EngineBenchmarkRequest
    {
        public string RunId = "baseline";
        public int Seed = 601;
        public int Horizon = 24000;
        public string Scenario = "stress-v1";
        internal int DoctorCount => Scenario == "stress-v1" ? 4 + Math.Abs(Seed % 2) : 3;
        internal int PatientCount => Scenario == "stress-v1" ? 28 + Math.Abs(Seed % 3) * 6 : 6;
        internal int HaulerCount => Scenario == "stress-v1" ? Math.Abs(Seed % 5) : 0;
        internal string SaveName => "SAR_Engine_" + Scenario + "_" + Seed + "_Initial";
        public float MedicineDetourTolerance = 1;
        public float TreatmentSwitchReluctance = 1;
        public float TreatmentBeforeTransportPriority = 1;
    }

    public sealed class EngineBenchmarkResult
    {
        public int ScoringVersion = 3;
        public EngineBenchmarkRequest Request;
        public string Status;
        public int Elapsed, Patients, Deaths, Untended, Rounds, Switches, Errors, OwnershipConflicts;
        public int RemainingPatients, Survivors, Stabilized, DoctorCount, HaulerCount, CompletionTick = -1;
        public int MedicineConsumed;
        public double BloodBurden, FirstTreatmentDelay, WalkDistance, Score;
        public string[] Events;
    }

    internal static class EngineBenchmarkDiagnostics
    {
        internal static readonly string DirectoryPath = Path.Combine(GenFilePaths.SaveDataFolderPath, "SAR_EngineBench");
        private static Map activeMap;
        private static EngineBenchmarkRequest request;
        private static EngineBenchmarkRequest previous;
        private static MedicalCoordinationMode previousMode;
        private static int start, rounds, switches, errors, conflicts;
        private static int stableSince, completionTick, initialMedicine;
        private static readonly HashSet<Pawn> observedDeaths = new HashSet<Pawn>();
        private static double burden, distance;
        private static List<Pawn> patients, doctors;
        private static readonly Dictionary<Pawn, int> first = new Dictionary<Pawn, int>();
        private static readonly Dictionary<Pawn, Pawn> last = new Dictionary<Pawn, Pawn>();
        private static readonly Dictionary<Pawn, IntVec3> positions = new Dictionary<Pawn, IntVec3>();
        private static readonly List<string> events = new List<string>();
        internal static bool Active => activeMap != null && Find.CurrentMap == activeMap;
        internal static int TickSeed => unchecked(request.Seed * 397 ^ (Find.TickManager.TicksGame - start));
        private static string Prefix(int seed) => "SAR Bench " + seed + " ";
        internal static EngineBenchmarkRequest ReadRequest()
        {
            using (var reader = File.OpenRead(Path.Combine(DirectoryPath, "request.xml")))
            {
                var value = (EngineBenchmarkRequest)new XmlSerializer(typeof(EngineBenchmarkRequest)).Deserialize(reader);
                if (value.RunId == null || value.RunId.Length > 80 || value.RunId.Any(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_') ||
                    (value.Scenario != "stress-v1" && value.Scenario != "routine-v1") || value.Horizon < 600 || value.Horizon > 24000 || value.Horizon % 30 != 0 ||
                    !Valid(value.MedicineDetourTolerance, .25f, 2f) || !Valid(value.TreatmentSwitchReluctance, 0, 2) ||
                    !Valid(value.TreatmentBeforeTransportPriority, 0, 2)) throw new ArgumentException("Invalid engine benchmark request.");
                return value;
            }
        }
        private static bool Valid(float v, float min, float max) => !float.IsNaN(v) && v >= min && v <= max;

        [DebugAction("Search and Rescue", "Build engine benchmark", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        internal static void Build()
        {
            if (activeMap != null) throw new InvalidOperationException("Finish the active benchmark first.");
            EngineBenchmarkRequest config = ReadRequest();
            Map map = Find.CurrentMap;
            var coordinator = map.GetComponent<SearchAndRescueCoordinator>();
            if (map.mapPawns.AllPawnsSpawned.Any(p => p.LabelShort.Contains("SAR Bench")))
                throw new InvalidOperationException("Load a fresh independent base before building a scenario.");
            Rand.PushState(config.Seed);
            try
            {
                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned.ToList())
                {
                    coordinator.SetFieldResponder(pawn, false);
                    if (pawn.workSettings != null && pawn.Faction == Faction.OfPlayer)
                        foreach (WorkTypeDef work in DefDatabase<WorkTypeDef>.AllDefsListForReading)
                            Compatibility.SetWorkPriorityForMigration(pawn, work, 0);
                }
                for (int i = 0; i < config.DoctorCount; i++)
                {
                    Pawn doctor = Generate(map, config.Seed, "Doctor " + i, 8);
                    doctor.skills.GetSkill(SkillDefOf.Medicine).Level = 5 + i * 12 / Math.Max(1, config.DoctorCount - 1);
                    Compatibility.SetWorkPriorityForMigration(doctor, WorkTypeDefOf.Doctor, 1);
                    Compatibility.SetWorkPriorityForMigration(doctor, WorkTypeDefOf.Hauling, 2);
                    coordinator.SetFieldResponder(doctor, true);
                }
                for (int i = 0; i < config.HaulerCount; i++)
                {
                    Pawn hauler = Generate(map, config.Seed, "Hauler " + i, 12);
                    Compatibility.SetWorkPriorityForMigration(hauler, WorkTypeDefOf.Hauling, 1);
                    coordinator.SetFieldResponder(hauler, true);
                }
                for (int i = 0; i < config.PatientCount; i++)
                {
                    Pawn patient = Generate(map, config.Seed, "Patient " + i, config.Scenario == "stress-v1" ? 35 : 22);
                    patient.health.AddHediff(HediffDefOf.Anesthetic);
                    patient.playerSettings.medCare = MedicalCareCategory.Best;
                    bool stress = config.Scenario == "stress-v1";
                    var parts = patient.health.hediffSet.GetNotMissingParts().Where(p => p.def.defName == "Arm" || p.def.defName == "Leg").ToList();
                    int woundCount = stress ? Rand.RangeInclusive(5, 8) : Rand.RangeInclusive(4, 7);
                    for (int j = 0; j < woundCount; j++)
                    {
                        Hediff injury = HediffMaker.MakeHediff(!stress && i % 3 == 0 ? DefDatabase<HediffDef>.GetNamed("Bruise") :
                            HediffDefOf.Cut, patient, stress ? parts[j % parts.Count] : patient.RaceProps.body.corePart);
                        injury.Severity = stress ? Rand.Range(4f, 7f) : Rand.Range(2f, 4f);
                        patient.health.AddHediff(injury);
                    }
                    if (stress || i % 3 != 0) patient.health.AddHediff(HediffDefOf.BloodLoss).Severity = stress ? Rand.Range(.55f, .88f) : Rand.Range(.15f, .5f);
                    if (patient.Dead) throw new InvalidOperationException("Patient died during generation.");
                    var bed = (Building_Bed)ThingMaker.MakeThing(ThingDefOf.SleepingSpot);
                    bed.SetFaction(Faction.OfPlayer); bed.Medical = true;
                    GenSpawn.Spawn(bed, CellFinder.RandomClosewalkCellNear(map.Center, map, 7), map);
                }
                Thing medicine = ThingMaker.MakeThing(ThingDefOf.MedicineIndustrial); medicine.stackCount = config.Scenario == "stress-v1" ? config.PatientCount : 18;
                GenPlace.TryPlaceThing(medicine, CellFinder.RandomClosewalkCellNear(map.Center, map,
                    config.Seed % 2 == 0 ? 30 : 10), map, ThingPlaceMode.Near);
                Log.Message("[SAR engine] Built seed=" + config.Seed + "; save this initial state before Begin.");
            }
            finally { Rand.PopState(); }
        }

        private static Pawn Generate(Map map, int seed, string name, int radius)
        {
            Pawn pawn = null;
            for (int i = 0; i < 40; i++)
            {
                pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
                    forceGenerateNewPawn: true, canGeneratePawnRelations: false));
                if (!pawn.WorkTypeIsDisabled(WorkTypeDefOf.Doctor) && !pawn.WorkTypeIsDisabled(SearchAndRescueDefOf.SAR_FieldRescue)) break;
                pawn.Destroy(); pawn = null;
            }
            if (pawn == null) throw new InvalidOperationException("No qualified benchmark pawn.");
            pawn.Name = new NameTriple("SAR", Prefix(seed) + name, "Benchmark");
            GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(map.Center, map, radius), map);
            foreach (WorkTypeDef work in DefDatabase<WorkTypeDef>.AllDefsListForReading)
                Compatibility.SetWorkPriorityForMigration(pawn, work, 0);
            pawn.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait_Wander, 1), JobCondition.InterruptForced);
            return pawn;
        }

        [DebugAction("Search and Rescue", "Begin engine benchmark", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        internal static void Begin()
        {
            if (activeMap != null) throw new InvalidOperationException("Finish the active benchmark first.");
            request = ReadRequest();
            Map map = Find.CurrentMap;
            patients = map.mapPawns.AllPawnsSpawned.Where(p => p.LabelShort.Contains(Prefix(request.Seed) + "Patient")).ToList();
            doctors = map.mapPawns.AllPawnsSpawned.Where(p => p.LabelShort.Contains(Prefix(request.Seed) + "Doctor")).ToList();
            if (patients.Count != request.PatientCount || doctors.Count != request.DoctorCount) throw new InvalidOperationException("Scenario seed/pawn count mismatch.");
            doctors.AddRange(map.mapPawns.AllPawnsSpawned.Where(p => p.LabelShort.Contains(Prefix(request.Seed) + "Hauler")));
            if (doctors.Count != request.DoctorCount + request.HaulerCount) throw new InvalidOperationException("Hauler count mismatch.");
            var settings = SearchAndRescueMod.Settings;
            previous = new EngineBenchmarkRequest { MedicineDetourTolerance = settings.MedicineDetourTolerance,
                TreatmentSwitchReluctance = settings.TreatmentSwitchReluctance, TreatmentBeforeTransportPriority = settings.TreatmentBeforeTransportPriority };
            previousMode = settings.MedicalCoordinationMode;
            settings.MedicalCoordinationMode = MedicalCoordinationMode.AllTending;
            Apply(request);
            first.Clear(); last.Clear(); positions.Clear(); events.Clear();
            rounds = switches = errors = conflicts = 0; burden = distance = 0;
            stableSince = completionTick = -1; observedDeaths.Clear();
            initialMedicine = MedicineCount(map);
            start = Find.TickManager.TicksGame; activeMap = map;
            foreach (Pawn p in doctors) positions[p] = p.Position;
            Application.logMessageReceived += OnLog;
            Log.Message("[SAR engine] Begin " + request.RunId + " seed=" + request.Seed);
        }
        private static void Apply(EngineBenchmarkRequest value)
        {
            var settings = SearchAndRescueMod.Settings;
            settings.MedicineDetourTolerance = value.MedicineDetourTolerance;
            settings.TreatmentSwitchReluctance = value.TreatmentSwitchReluctance;
            settings.TreatmentBeforeTransportPriority = value.TreatmentBeforeTransportPriority;
        }
        private static void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors++;
        }
        internal static void Observe(Pawn doctor, Pawn patient)
        {
            if (!Active || !patients.Contains(patient)) return;
            int elapsed = Find.TickManager.TicksGame-start;
            if (!first.ContainsKey(patient)) first[patient] = elapsed;
            if (last.TryGetValue(patient, out Pawn old) && old != doctor) switches++;
            last[patient] = doctor; rounds++;
            events.Add(elapsed + " tend " + doctor.LabelShort + " -> " + patient.LabelShort);
        }
        internal static void Tick(Map map)
        {
            if (activeMap == null) return;
            if (Find.CurrentMap != activeMap) { Finish("aborted-map-change"); return; }
            if (map != activeMap) return;
            int elapsed = Find.TickManager.TicksGame-start;
            if (elapsed % 30 == 0)
            {
                burden += patients.Sum(p => p.Dead ? 1d : (double)(p.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss)?.Severity ?? 0f)) * 30;
                foreach (Pawn p in doctors.Where(p => p.Spawned))
                {
                    distance += Math.Sqrt(p.Position.DistanceToSquared(positions[p])); positions[p] = p.Position;
                }
                var owners = doctors.Where(p => p.CurJob != null && Compatibility.IsTreatmentJob(p.CurJob.def))
                    .Select(p => CompatibilityRegistry.PatientFor(p, p.CurJob)).Where(p => p != null).GroupBy(p => p);
                conflicts += owners.Count(g => g.Count() > 1);
                foreach (Pawn p in patients.Where(p => p.Dead || p.Destroyed))
                    if (observedDeaths.Add(p)) events.Add(elapsed + " death " + p.LabelShort);
                bool allTreated = patients.All(p => p.Dead || p.Destroyed || !Compatibility.NeedsAnyFieldTreatment(p)) &&
                    !activeMap.mapPawns.AllPawnsSpawned.Any(p => p.CurJob != null &&
                        Compatibility.IsTreatmentJob(p.CurJob.def) &&
                        patients.Contains(CompatibilityRegistry.PatientFor(p, p.CurJob)));
                if (!allTreated) { stableSince = -1; completionTick = -1; }
                else if (stableSince < 0) stableSince = elapsed;
                if (stableSince >= 0 && elapsed - stableSince >= 180)
                {
                    completionTick = stableSince;
                    if (request.Scenario == "routine-v1") { Finish("completed"); return; }
                }
            }
            if (elapsed >= request.Horizon) Finish(request.Scenario == "stress-v1" ? "observed" : "timeout");
        }
        private static int MedicineCount(Map map)
        {
            var items = new HashSet<Thing>(map.listerThings.AllThings.Where(t => t.def.IsMedicine));
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned.Concat(patients ?? new List<Pawn>()).Concat(doctors ?? new List<Pawn>()).Distinct())
            {
                if (pawn.inventory != null) foreach (Thing t in pawn.inventory.innerContainer) if (t.def.IsMedicine) items.Add(t);
                Thing carried = pawn.carryTracker?.CarriedThing;
                if (carried != null && carried.def.IsMedicine) items.Add(carried);
            }
            return items.Where(t => !t.Destroyed).Sum(t => t.stackCount);
        }
        [DebugAction("Search and Rescue", "Finish engine benchmark", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void FinishAction() => Finish("aborted-manual");
        internal static void Finish(string status)
        {
            if (activeMap == null) return;
            try
            {
                int elapsed = Find.TickManager.TicksGame-start;
                var result = new EngineBenchmarkResult { Request = request, Status = status, Elapsed = elapsed,
                    Patients = patients.Count, Deaths = patients.Count(p => p.Dead || p.Destroyed),
                    Survivors = patients.Count(p => !p.Dead && !p.Destroyed),
                    Stabilized = patients.Count(p => !p.Dead && !p.Destroyed && !Compatibility.NeedsAnyFieldTreatment(p)),
                    DoctorCount = request.DoctorCount, HaulerCount = request.HaulerCount,
                    MedicineConsumed = Math.Max(0, initialMedicine - MedicineCount(activeMap)),
                    Untended = patients.Where(p => !p.Dead).Sum(p => p.health.hediffSet.hediffs.Count(h => h.TendableNow())),
                    RemainingPatients = patients.Count(p => !p.Dead && !p.Destroyed && Compatibility.NeedsAnyFieldTreatment(p)),
                    CompletionTick = completionTick,
                    Rounds = rounds, Switches = switches, Errors = errors, OwnershipConflicts = conflicts,
                    BloodBurden = burden/(patients.Count*(double)request.Horizon),
                    FirstTreatmentDelay = patients.Sum(p => first.TryGetValue(p, out int time) ? time : request.Horizon)/(patients.Count*(double)request.Horizon),
                    WalkDistance = distance, Events = events.ToArray() };
                result.Score = status.StartsWith("aborted", StringComparison.Ordinal) || errors > 0 || conflicts > 0 ? -1e9 :
                    1000d * result.Survivors + 100d * result.Stabilized / result.Patients +
                    10d * (1 - Math.Min(1, result.BloodBurden)) +
                    5d * (1 - Math.Min(1, result.FirstTreatmentDelay)) +
                    5d * (result.CompletionTick < 0 ? 0 : 1d - result.CompletionTick / (double)request.Horizon) +
                    2d / (1 + result.MedicineConsumed / (double)result.Patients) +
                    1d / (1 + switches) + 1d / (1 + distance / 1000d);
                Directory.CreateDirectory(DirectoryPath);
                string destination = Path.Combine(DirectoryPath, request.RunId + ".xml");
                string temporary = destination + ".tmp";
                using (var file = File.Create(temporary))
                    new XmlSerializer(typeof(EngineBenchmarkResult)).Serialize(file, result);
                if (File.Exists(destination)) File.Delete(destination);
                File.Move(temporary, destination);
                Log.Message("[SAR engine] " + request.RunId + " " + status + " score=" + result.Score.ToString("F2") + " deaths=" + result.Deaths);
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
                Apply(previous); SearchAndRescueMod.Settings.MedicalCoordinationMode = previousMode;
                activeMap = null;
            }
        }
    }

    // Seed each real engine tick in an isolated scope; UI frame randomness stays outside it.
    [HarmonyPatch(typeof(TickManager), nameof(TickManager.DoSingleTick))]
    internal static class EngineBenchmarkRandomScope
    {
        private static void Prefix(out bool __state)
        {
            __state = EngineBenchmarkDiagnostics.Active;
            if (__state) Rand.PushState(EngineBenchmarkDiagnostics.TickSeed);
        }
        private static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__state) Rand.PopState();
            return __exception;
        }
    }
}
