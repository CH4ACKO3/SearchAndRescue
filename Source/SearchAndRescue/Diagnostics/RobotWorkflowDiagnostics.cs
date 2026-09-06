using System;
using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using Verse;
using Verse.AI;

namespace SearchAndRescue
{
    /// <summary>
    /// Persistent, saveable fixture for robot medical compatibility. Each case starts the
    /// native TendPatient job selected by the owning robot mod; Finish only observes the
    /// result and deliberately leaves every pawn on the map.
    /// </summary>
    internal static class RobotWorkflowDiagnostics
    {
        private const string Prefix = "[SAR robot workflow] ";

        private sealed class RobotCase
        {
            internal readonly string Label;
            internal readonly string PawnKindDefName;
            internal readonly string RaceDefName;
            internal readonly int Offset;
            internal readonly bool NativeRobotTend;
            internal readonly string LocalRepairDefName;

            internal string DoctorName => "SAR Robot Doctor - " + Label;
            internal string PatientName => "SAR Robot Patient - " + Label;

            internal RobotCase(
                string label,
                string pawnKindDefName,
                string raceDefName,
                int offset,
                bool nativeRobotTend,
                string localRepairDefName)
            {
                Label = label;
                PawnKindDefName = pawnKindDefName;
                RaceDefName = raceDefName;
                Offset = offset;
                NativeRobotTend = nativeRobotTend;
                LocalRepairDefName = localRepairDefName;
            }
        }

        private static readonly RobotCase[] Cases =
        {
            new RobotCase("Paniel", "PN_ColonistPawn", "Paniel_Race", -30, true, "PN_RepairKit"),
            new RobotCase("Androids Droid", "ChjDroidColonist", "ChjDroid", -10, true, "ChjDroidRepairParts"),
            new RobotCase("Androids Android", "ChjAndroidColonist", "ChjAndroid", 10, false, null),
            new RobotCase("Androids Expanded Spacer Droid", "ChjSpacerDroidColonist", "ChjSpacerDroid", 30, true,
                "ChjDroidRepairParts")
        };

        private static readonly List<Thing> SessionResources = new List<Thing>();

        [DebugAction("Search and Rescue", "Start robot native-tend workflow",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Start()
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                Log.Error(Prefix + "FAIL map: no current map");
                return;
            }

            RemovePreviousFixture(map);
            SpawnSupply(map, ThingDefOf.MedicineIndustrial, 20, -2);
            SpawnSupply(map, DefDatabase<ThingDef>.GetNamedSilentFail("ChjDroidRepairParts"), 20, 0);
            SpawnSupply(map, DefDatabase<ThingDef>.GetNamedSilentFail("PN_RepairKit"), 20, 2);

            int started = 0;
            foreach (RobotCase robotCase in Cases)
            {
                try
                {
                    StartCase(map, robotCase);
                    started++;
                }
                catch (Exception error)
                {
                    Log.Error(Prefix + "FAIL " + robotCase.Label + ": " + error);
                }
            }

            Log.Message(Prefix + "START COMPLETE: native TendPatient jobs started=" + started +
                "/" + Cases.Length + ". Advance until the jobs finish, then run " +
                "'Finish robot native-tend workflow'. Fixture pawns remain available for saving.");
        }

        [DebugAction("Search and Rescue", "Finish robot native-tend workflow",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Finish()
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                Log.Error(Prefix + "FAIL map: no current map");
                return;
            }

            int passed = 0;
            foreach (RobotCase robotCase in Cases)
            {
                Pawn doctor = FindNamed(map, robotCase.DoctorName);
                Pawn patient = FindNamed(map, robotCase.PatientName);
                if (doctor == null || patient == null)
                {
                    Log.Error(Prefix + "FAIL " + robotCase.Label +
                        ": fixture pawn missing; run Start on this map first");
                    continue;
                }

                List<Hediff> cuts = patient.health.hediffSet.hediffs
                    .Where(hediff => hediff.def == HediffDefOf.Cut).ToList();
                bool tended = cuts.Any(hediff => hediff.IsTended());
                bool removedByNativeFinalizer = cuts.Count == 0;
                bool ceStabilized = !robotCase.NativeRobotTend && cuts.Any(IsCombatExtendedStabilized);
                bool woundResolved = tended || removedByNativeFinalizer || ceStabilized;
                bool biologicalEmergency = Compatibility.HasFieldTreatableEmergency(patient);
                bool jobFinished = doctor.CurJobDef != JobDefOf.TendPatient &&
                                   doctor.CurJobDef?.defName != "Stabilize";
                if (woundResolved && !biologicalEmergency && jobFinished)
                {
                    passed++;
                    Log.Message(Prefix + "PASS " + robotCase.Label + ": race=" +
                        patient.def.defName + "; wound=" +
                        (tended ? "tended" : ceStabilized ? "CE-stabilized" : "removed-by-native-finalizer") +
                        "; biologicalEmergency=false; " +
                        "doctorJob=" + (doctor.CurJobDef?.defName ?? "none"));
                }
                else
                {
                    Log.Error(Prefix + "FAIL " + robotCase.Label + ": race=" +
                        patient.def.defName + "; woundTended=" + tended +
                        "; woundRemoved=" + removedByNativeFinalizer +
                        "; ceStabilized=" + ceStabilized +
                        "; biologicalEmergency=" + biologicalEmergency + "; doctorJob=" +
                        (doctor.CurJobDef?.defName ?? "none"));
                }
            }

            Log.Message(Prefix + "FINISH COMPLETE: passed=" + passed + "/" + Cases.Length +
                ". All fixture pawns were retained on the map for save/reload inspection.");
        }

        private static void StartCase(Map map, RobotCase robotCase)
        {
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(robotCase.PawnKindDefName);
            if (kind == null)
                throw new InvalidOperationException("missing PawnKindDef " + robotCase.PawnKindDefName);
            if (kind.race?.defName != robotCase.RaceDefName)
                throw new InvalidOperationException("PawnKindDef " + robotCase.PawnKindDefName +
                    " resolved to race " + (kind.race?.defName ?? "null") +
                    ", expected " + robotCase.RaceDefName);

            Pawn doctor = GenerateDoctor();
            Pawn patient = GenerateWithoutRelations(kind);
            doctor.Name = new NameTriple("SAR", robotCase.DoctorName, "Fixture");
            patient.Name = new NameTriple("SAR", robotCase.PatientName, "Fixture");
            GenSpawn.Spawn(patient, FixtureCell(map, robotCase.Offset), map);
            GenSpawn.Spawn(doctor, FixtureCellNear(map, patient.Position), map);

            doctor.workSettings.EnableAndInitializeIfNotAlreadyInitialized();
            Compatibility.SetWorkPriorityForMigration(doctor, SearchAndRescueDefOf.SAR_FieldRescue, 1);
            Compatibility.SetWorkPriorityForMigration(doctor, WorkTypeDefOf.Doctor, 1);
            SkillRecord medicine = doctor.skills?.GetSkill(SkillDefOf.Medicine);
            if (medicine != null && !medicine.TotallyDisabled)
                medicine.Level = Math.Max(12, medicine.Level);
            if (patient.playerSettings != null)
                patient.playerSettings.medCare = MedicalCareCategory.Best;

            Thing ordinaryMedicine = ThingMaker.MakeThing(ThingDefOf.MedicineIndustrial);
            ordinaryMedicine.stackCount = 2;
            if (!doctor.inventory.innerContainer.TryAdd(ordinaryMedicine))
                throw new InvalidOperationException("could not give ordinary medicine to doctor");

            Thing localRepair = null;
            if (robotCase.LocalRepairDefName != null)
            {
                ThingDef repairDef = DefDatabase<ThingDef>.GetNamedSilentFail(robotCase.LocalRepairDefName);
                if (repairDef == null)
                    throw new InvalidOperationException("missing local repair supply " + robotCase.LocalRepairDefName);
                localRepair = SpawnSupplyNear(map, repairDef, 10, patient.Position);
            }

            foreach (Hediff oldTendable in patient.health.hediffSet.hediffs
                         .Where(hediff => hediff.def == HediffDefOf.Cut || hediff.TendableNow()).ToList())
                patient.health.RemoveHediff(oldTendable);
            BodyPartRecord part = patient.RaceProps.body.AllParts.FirstOrDefault(candidate =>
                                      candidate.depth == BodyPartDepth.Outside &&
                                      candidate.def != BodyPartDefOf.Head) ??
                                  patient.RaceProps.body.corePart;
            Hediff injury = HediffMaker.MakeHediff(HediffDefOf.Cut, patient, part);
            injury.Severity = 6f;
            patient.health.AddHediff(injury);
            if (!injury.TendableNow() || !patient.health.HasHediffsNeedingTend())
                throw new InvalidOperationException("generated Cut is not tendable");
            bool ownsMedicineSelection = RobotMedicalProfile.OwnsMedicineSelection(patient);
            if (ownsMedicineSelection != robotCase.NativeRobotTend)
                throw new InvalidOperationException("robot medicine ownership=" + ownsMedicineSelection +
                    ", expected=" + robotCase.NativeRobotTend);

            bool allowsBiologicalEmergency = RobotMedicalProfile.AllowsBiologicalEmergency(patient);
            bool biologicalEmergency = Compatibility.HasFieldTreatableEmergency(patient);
            if (allowsBiologicalEmergency || biologicalEmergency)
                throw new InvalidOperationException("robot entered biological emergency routing; allows=" +
                    allowsBiologicalEmergency + "; active=" + biologicalEmergency);

            MedicalCarePlan plan = MedicalCarePlan.Build(patient, Find.TickManager.TicksGame);
            if (localRepair != null)
                LogRepairCandidate(doctor, patient, localRepair, map);
            IReadOnlyList<MedicalTreatmentOption> options = Compatibility.FindTreatmentOptions(
                doctor, patient, plan, new MedicalResourceLedger(map));
            MedicalTreatmentOption option;
            if (robotCase.NativeRobotTend)
            {
                if (options.Count != 1 || options[0].Intervention != MedicalIntervention.NativeRobotTend)
                    throw new InvalidOperationException("expected one NativeRobotTend option; got " +
                        string.Join(",", options.Select(candidate => candidate.Intervention.ToString())));
                option = options[0];
            }
            else
            {
                option = options.FirstOrDefault(candidate => candidate.Resource == ordinaryMedicine &&
                    (candidate.Intervention == MedicalIntervention.VanillaTend ||
                     candidate.Intervention == MedicalIntervention.CombatExtendedStabilize));
                if (option == null)
                    throw new InvalidOperationException("expected ordinary-medicine VanillaTend or CE Stabilize; got " +
                        string.Join(",", options.Select(candidate => candidate.Intervention + "/" +
                            Describe(candidate.Resource))));
            }
            Job job = Compatibility.MakeTreatmentRoundJob(doctor, patient, option);
            bool expectedJob = option.Intervention == MedicalIntervention.CombatExtendedStabilize
                ? job?.def?.defName == "Stabilize"
                : job?.def == JobDefOf.TendPatient;
            if (!expectedJob || job.targetA.Pawn != patient || job.targetB.Thing != option.Resource)
                throw new InvalidOperationException("SAR did not construct the selected native treatment job");

            doctor.jobs.StartJob(job, JobCondition.InterruptForced);
            Log.Message(Prefix + "START " + robotCase.Label + ": doctor=" + doctor.ThingID +
                "; patient=" + patient.ThingID + "; race=" + patient.def.defName +
                "; intervention=" + option.Intervention + "; resource=" + Describe(option.Resource) +
                "; ordinaryMedicine=" + Describe(ordinaryMedicine) +
                "; biologicalEmergency=false; job=" + doctor.CurJobDef?.defName);
        }

        private static Pawn GenerateDoctor()
        {
            for (int attempt = 0; attempt < 50; attempt++)
            {
                Pawn doctor = GenerateWithoutRelations(PawnKindDefOf.Colonist);
                if (!doctor.WorkTypeIsDisabled(WorkTypeDefOf.Doctor) &&
                    !doctor.WorkTypeIsDisabled(SearchAndRescueDefOf.SAR_FieldRescue))
                    return doctor;
            }
            throw new InvalidOperationException("could not generate a doctor capable of medical and field rescue work");
        }

        private static Pawn GenerateWithoutRelations(PawnKindDef kind) => PawnGenerator.GeneratePawn(
            new PawnGenerationRequest(kind, Faction.OfPlayer,
                PawnGenerationContext.NonPlayer, forceGenerateNewPawn: true,
                canGeneratePawnRelations: false));

        private static bool IsCombatExtendedStabilized(Hediff hediff)
        {
            HediffWithComps withComps = hediff as HediffWithComps;
            if (withComps?.comps == null) return false;
            foreach (HediffComp comp in withComps.comps)
            {
                Type type = comp?.GetType();
                if (type?.FullName != "CombatExtended.HediffComp_Stabilize") continue;
                object value = type.GetProperty("Stabilized")?.GetValue(comp, null);
                if (value is bool stabilized && stabilized) return true;
            }
            return false;
        }

        private static void LogRepairCandidate(Pawn doctor, Pawn patient, Thing repair, Map map)
        {
            bool listed = map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver).Contains(repair);
            bool extension = repair.def.modExtensions?.Any(candidate =>
                candidate.GetType().FullName == "Androids.DroidRepairProperties") == true;
            Log.Message(Prefix + "REPAIR CANDIDATE " + patient.def.defName + ": " + Describe(repair) +
                "; medicineRounds=" + Medicine.GetMedicineCountToFullyHeal(patient) +
                "; haulableListed=" + listed + "; droidRepairExtension=" + extension +
                "; forbidden=" + repair.IsForbidden(doctor) +
                "; medCareAllows=" + patient.playerSettings.medCare.AllowsMedicine(repair.def) +
                "; reservable=" + doctor.CanReserve(repair, 10, 1) +
                "; reachable=" + doctor.CanReach(repair, PathEndMode.ClosestTouch, Danger.Deadly));
        }

        private static void SpawnSupply(Map map, ThingDef def, int count, int offset)
        {
            if (def == null)
            {
                Log.Warning(Prefix + "supply def unavailable; one robot mod may be inactive");
                return;
            }

            Thing thing = ThingMaker.MakeThing(def);
            thing.stackCount = Math.Min(count, def.stackLimit);
            GenSpawn.Spawn(thing, FixtureCell(map, offset), map);
            SessionResources.Add(thing);
            Log.Message(Prefix + "supply=" + Describe(thing));
        }

        private static Thing SpawnSupplyNear(Map map, ThingDef def, int count, IntVec3 center)
        {
            Thing thing = ThingMaker.MakeThing(def);
            thing.stackCount = Math.Min(count, def.stackLimit);
            GenSpawn.Spawn(thing, FixtureCellNear(map, center), map);
            SessionResources.Add(thing);
            return thing;
        }

        private static IntVec3 FixtureCell(Map map, int offset) =>
            GenRadial.RadialCellsAround(map.Center + new IntVec3(offset, 0, 0), 12f, true)
                .First(cell => cell.InBounds(map) && cell.Standable(map) &&
                               cell.GetFirstPawn(map) == null && cell.GetFirstItem(map) == null);

        private static IntVec3 FixtureCellNear(Map map, IntVec3 center) =>
            GenRadial.RadialCellsAround(center, 4f, false)
                .First(cell => cell.InBounds(map) && cell.Standable(map) &&
                               cell.GetFirstPawn(map) == null && cell.GetFirstItem(map) == null);

        private static Pawn FindNamed(Map map, string name) => map.mapPawns.AllPawnsSpawned
            .FirstOrDefault(pawn => pawn.Name?.ToStringShort == name);

        private static string Describe(Thing thing) => thing == null
            ? "none"
            : thing.def.defName + "/" + thing.ThingID + " x" + thing.stackCount +
              (thing.Spawned ? "@" + thing.Position : "@inventory");

        private static void RemovePreviousFixture(Map map)
        {
            foreach (RobotCase robotCase in Cases)
            {
                FindNamed(map, robotCase.DoctorName)?.Destroy(DestroyMode.Vanish);
                FindNamed(map, robotCase.PatientName)?.Destroy(DestroyMode.Vanish);
            }
            foreach (Thing resource in SessionResources.Where(resource => resource != null && !resource.Destroyed))
                resource.Destroy(DestroyMode.Vanish);
            SessionResources.Clear();
        }
    }
}
