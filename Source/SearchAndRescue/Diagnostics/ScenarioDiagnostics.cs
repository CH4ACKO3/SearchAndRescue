using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;

namespace SearchAndRescue
{
    /// <summary>
    /// Development-only fixture preparation. Generated pawns, injuries, beds and supplies are
    /// ordinary game objects placed on a disposable quick-test map, while each preset isolates
    /// work permissions and designations for deterministic SAR regressions.
    /// </summary>
    internal static class ScenarioDiagnostics
    {
        private static readonly int[] DoctorSkillLevels = { 4, 7, 10, 13, 16, 19 };
        private static readonly HashSet<Building_Bed> GeneratedMedicalSpots =
            new HashSet<Building_Bed>();

        [DebugAction("Search and Rescue", "Configure mass-casualty fixture",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ConfigureMassCasualtyFixture()
        {
            ConfigureBenchmark(BenchmarkPreset.LargeMixed);
        }

        [DebugAction("Search and Rescue", "Build disposable alpha benchmark population",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void BuildDisposableAlphaBenchmarkPopulation()
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                Log.Error("[Search and Rescue] Cannot build a fixture without a current map.");
                return;
            }

            const int casualtyCount = 18;
            const int hostileCasualtyCount = 12;
            const int doctorCount = 6;
            const int carrierCount = 6;
            int generatedResponders = 0;
            int generationAttempts = 0;
            while (!HasResponderCapacity(map, doctorCount, carrierCount) &&
                   generationAttempts++ < 60)
            {
                SpawnColonist(map, FindFixtureCell(map, generatedResponders++, responder: true));
            }

            int existingCasualties = map.mapPawns.FreeColonistsSpawned.Count(pawn =>
                pawn.Downed && !pawn.Dead && pawn.RaceProps?.Humanlike == true);
            int generatedCasualties = 0;
            while (existingCasualties + generatedCasualties < casualtyCount &&
                   generationAttempts++ < 120)
            {
                Pawn casualty = SpawnColonist(
                    map,
                    FindFixtureCell(map, generatedCasualties, responder: false));
                if (casualty == null)
                {
                    continue;
                }

                AddBenchmarkInjuries(casualty);
                HealthUtility.TryAnesthetize(casualty);
                if (casualty.Downed && !casualty.Dead)
                {
                    generatedCasualties++;
                }
                else if (!casualty.Destroyed)
                {
                    casualty.Destroy();
                }
            }

            EnsureHostileCasualties(map, hostileCasualtyCount);
            EnsureMedicalSleepingSpots(map, casualtyCount);
            EnsureMedicine(map, ThingDefOf.MedicineIndustrial, 150, 0);
            EnsureMedicine(map, ThingDefOf.MedicineHerbal, 150, 8);

            bool ready = HasResponderCapacity(map, doctorCount, carrierCount) &&
                         map.mapPawns.FreeColonistsSpawned.Count(pawn =>
                             pawn.Downed && !pawn.Dead &&
                             pawn.RaceProps?.Humanlike == true) >= casualtyCount;
            if (!ready)
            {
                Log.Error("[Search and Rescue] Disposable benchmark generation could not " +
                          "produce the required viable population.");
                return;
            }

            Log.Message("[Search and Rescue] Disposable alpha benchmark population built. " +
                        "Run one of the Configure benchmark actions while paused.");
        }

        [DebugAction("Search and Rescue", "Remove disposable benchmark medical spots",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void RemoveDisposableBenchmarkMedicalSpots()
        {
            int removed = 0;
            foreach (Building_Bed bed in GeneratedMedicalSpots.ToList())
            {
                if (bed != null && !bed.Destroyed)
                {
                    bed.Destroy(DestroyMode.Vanish);
                    removed++;
                }
            }
            GeneratedMedicalSpots.Clear();
            Log.Message($"[Search and Rescue] Removed {removed} disposable benchmark medical spots.");
        }

        [DebugAction("Search and Rescue", "Configure benchmark - small mixed (6x2x2)",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ConfigureSmallMixedBenchmark()
        {
            ConfigureBenchmark(BenchmarkPreset.SmallMixed);
        }

        [DebugAction("Search and Rescue", "Configure benchmark - treatment graph (18x6)",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ConfigureTreatmentGraphBenchmark()
        {
            ConfigureBenchmark(BenchmarkPreset.TreatmentGraph);
        }

        [DebugAction("Search and Rescue", "Configure benchmark - rescue without beds (18x6)",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ConfigureRescueWithoutBedsBenchmark()
        {
            ConfigureBenchmark(BenchmarkPreset.RescueWithoutBeds);
        }

        [DebugAction("Search and Rescue", "Configure benchmark - medical logistics (12x3x6)",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ConfigureMedicalLogisticsBenchmark()
        {
            ConfigureBenchmark(BenchmarkPreset.MedicalLogistics);
        }

        [DebugAction("Search and Rescue", "Configure benchmark - capture triage (12x4x4)",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ConfigureCaptureTriageBenchmark()
        {
            ConfigureBenchmark(BenchmarkPreset.CaptureTriage);
        }

        private static void ConfigureBenchmark(BenchmarkPreset preset)
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                Log.Error("[Search and Rescue] Cannot configure fixture without a current map.");
                return;
            }

            List<Pawn> casualties = (preset.HostileTargets
                    ? map.mapPawns.AllPawnsSpawned.Where(pawn => pawn.HostileTo(Faction.OfPlayer))
                    : map.mapPawns.FreeColonistsSpawned.AsEnumerable())
                .Where(pawn => pawn.Downed && !pawn.Dead && pawn.RaceProps?.Humanlike == true)
                .OrderBy(pawn => pawn.thingIDNumber)
                .Take(preset.Casualties)
                .ToList();
            List<Pawn> responders = map.mapPawns.FreeColonistsSpawned
                .Where(pawn => !pawn.Downed && !pawn.Dead && !pawn.InMentalState)
                .OrderBy(pawn => pawn.thingIDNumber)
                .ToList();
            List<Pawn> doctors = responders
                .Where(pawn => !pawn.WorkTypeIsDisabled(WorkTypeDefOf.Doctor))
                .Take(preset.Doctors)
                .ToList();
            HashSet<Pawn> doctorSet = new HashSet<Pawn>(doctors);
            List<Pawn> carriers = responders
                .Where(pawn => !doctorSet.Contains(pawn) &&
                               !pawn.WorkTypeIsDisabled(WorkTypeDefOf.Hauling))
                .Take(preset.Carriers)
                .ToList();

            if (casualties.Count < preset.Casualties || doctors.Count < preset.Doctors ||
                carriers.Count < preset.Carriers)
            {
                Log.Error($"[Search and Rescue] Benchmark '{preset.Name}' needs " +
                          $"casualties={preset.Casualties}, doctors={preset.Doctors}, " +
                          $"carriers={preset.Carriers}; found " +
                          $"casualties={casualties.Count}, doctors={doctors.Count}, " +
                          $"carriers={carriers.Count}. " +
                          (preset.HostileTargets
                              ? "Targets must be downed hostile humanlikes."
                              : "Targets must be downed free colonists."));
                return;
            }

            SearchAndRescueCoordinator coordinator = map.GetComponent<SearchAndRescueCoordinator>();
            coordinator?.ClearBenchmarkFixture();
            ConfigureBenchmarkRescuePoint(map, coordinator, preset.Capture);

            foreach (Pawn responder in responders)
            {
                DisableAllWork(responder);
            }
            for (int index = 0; index < doctors.Count; index++)
            {
                ConfigureDoctor(doctors[index], index);
            }
            for (int index = 0; index < carriers.Count; index++)
            {
                ConfigureCarrier(carriers[index], index);
            }

            List<Pawn> enabledTransportWorkers = responders.Where(worker =>
                    Compatibility.CanPerformRescueWork(worker) || Compatibility.CanPerformSupplyWork(worker))
                .ToList();
            if (enabledTransportWorkers.Count != carriers.Count ||
                enabledTransportWorkers.Any(worker => !carriers.Contains(worker)))
            {
                Log.Warning("[Search and Rescue] Benchmark role isolation drifted after Work Tab setup. " +
                            $"expectedTransport={carriers.Count} actualTransport=" +
                            string.Join(",", enabledTransportWorkers.Select(worker => worker.ThingID)));
            }
            List<Pawn> enabledTreatmentWorkers = responders
                .Where(Compatibility.CanPerformAnyTreatmentWork)
                .ToList();
            if (enabledTreatmentWorkers.Count != doctors.Count ||
                enabledTreatmentWorkers.Any(worker => !doctors.Contains(worker)))
            {
                Log.Warning("[Search and Rescue] Benchmark treatment-role isolation drifted. " +
                            $"expectedDoctors={doctors.Count} actualTreatment=" +
                            string.Join(",", enabledTreatmentWorkers.Select(worker => worker.ThingID)));
            }

            foreach (Pawn casualty in casualties)
            {
                // Hostile pawns normally gain player settings only during vanilla capture.
                // Initialize them up front so the benchmark does not accidentally test the
                // fallback herbal-only policy while providing industrial medicine.
                casualty.playerSettings = casualty.playerSettings ?? new Pawn_PlayerSettings(casualty);
                casualty.playerSettings.medCare = MedicalCareCategory.Best;
                if (preset.Capture)
                {
                    AddStage(map, coordinator, casualty, SearchAndRescueDefOf.SAR_Capture,
                        SearchAndRescueStage.Capture);
                }
                if (preset.Treat)
                {
                    AddStage(map, coordinator, casualty, SearchAndRescueDefOf.SAR_Treat,
                        SearchAndRescueStage.Treat);
                }
                if (preset.Rescue)
                {
                    AddStage(map, coordinator, casualty, SearchAndRescueDefOf.SAR_Rescue,
                        SearchAndRescueStage.Rescue);
                }
            }

            SearchAndRescuePerformanceDiagnostics.SetBenchmarkScenario(map, preset.Name);
            int medicalBeds = map.listerBuildings.AllBuildingsColonistOfClass<Building_Bed>()
                .Count(bed => bed.Medical);
            int medicineStacks = map.listerThings.AllThings
                .Count(thing => thing.Spawned && !thing.Destroyed && thing.def.IsMedicine);
            Log.Message($"[Search and Rescue] Benchmark '{preset.Name}' configured: " +
                        $"casualties={casualties.Count} " +
                        $"doctors={doctors.Count} " +
                        $"carriers={carriers.Count} " +
                        $"stages={preset.StageLabel} medicalBeds={medicalBeds} " +
                        $"medicineStacks={medicineStacks}\n" +
                        " casualties=" + string.Join(",", casualties.Select(pawn => pawn.ThingID)) + "\n" +
                        " doctors=" + string.Join(",", doctors
                            .Select(pawn => $"{pawn.ThingID}:{pawn.skills?.GetSkill(SkillDefOf.Medicine)?.Level ?? 0}")) +
                        "\n carriers=" + string.Join(",", carriers
                            .Select(pawn => pawn.ThingID)));
        }

        private static bool HasResponderCapacity(Map map, int doctorsNeeded, int carriersNeeded)
        {
            List<Pawn> responders = map.mapPawns.FreeColonistsSpawned
                .Where(pawn => !pawn.Downed && !pawn.Dead && !pawn.InMentalState)
                .OrderBy(pawn => pawn.thingIDNumber)
                .ToList();
            List<Pawn> doctors = responders
                .Where(pawn => !pawn.WorkTypeIsDisabled(WorkTypeDefOf.Doctor))
                .Take(doctorsNeeded)
                .ToList();
            HashSet<Pawn> doctorSet = new HashSet<Pawn>(doctors);
            int carriers = responders.Count(pawn =>
                !doctorSet.Contains(pawn) &&
                !pawn.WorkTypeIsDisabled(WorkTypeDefOf.Hauling));
            return doctors.Count >= doctorsNeeded && carriers >= carriersNeeded;
        }

        private static Pawn SpawnColonist(Map map, IntVec3 cell)
        {
            return SpawnPawn(map, PawnKindDefOf.Colonist, Faction.OfPlayer, cell);
        }

        private static Pawn SpawnPawn(
            Map map,
            PawnKindDef kind,
            Faction faction,
            IntVec3 cell)
        {
            try
            {
                Pawn pawn = PawnGenerator.GeneratePawn(kind, faction);
                return GenSpawn.Spawn(pawn, cell, map) as Pawn;
            }
            catch (System.Exception exception)
            {
                Log.WarningOnce("[Search and Rescue] Benchmark pawn generation failed: " +
                                exception.GetBaseException().Message,
                    196320761);
                return null;
            }
        }

        private static void EnsureHostileCasualties(Map map, int desired)
        {
            int existing = map.mapPawns.AllPawnsSpawned.Count(pawn =>
                pawn.Downed && !pawn.Dead && pawn.RaceProps?.Humanlike == true &&
                pawn.HostileTo(Faction.OfPlayer));
            if (existing >= desired)
            {
                return;
            }

            Faction faction = Find.FactionManager.AllFactionsListForReading
                .FirstOrDefault(candidate =>
                    candidate != null && candidate.HostileTo(Faction.OfPlayer) &&
                    candidate.def?.humanlikeFaction == true);
            faction = faction ?? Faction.OfAncientsHostile ?? Faction.OfPirates;
            if (faction == null && FactionDefOf.Pirate != null)
            {
                // The developer quick-test world contains no generated hostile humanlike
                // faction. Create the same vanilla pirate faction a normal world would have
                // so the capture benchmark remains self-contained and reproducible.
                try
                {
                    faction = FactionGenerator.NewGeneratedFactionWithRelations(
                        FactionDefOf.Pirate,
                        new List<FactionRelation>
                        {
                            new FactionRelation(Faction.OfPlayer, FactionRelationKind.Hostile)
                        });
                    Find.FactionManager.Add(faction);
                    Faction.OfPlayer.SetRelation(
                        new FactionRelation(faction, FactionRelationKind.Hostile));
                }
                catch (System.Exception exception)
                {
                    Log.WarningOnce("[Search and Rescue] Could not create the disposable " +
                                    "benchmark pirate faction: " +
                                    exception.GetBaseException().Message,
                        196320762);
                }
            }
            PawnKindDef kind = faction?.def?.basicMemberKind ?? PawnKindDefOf.AncientSoldier;
            if (kind == null)
            {
                Log.Warning("[Search and Rescue] No hostile humanlike faction was available; " +
                            "the capture benchmark was not populated.");
                return;
            }

            int generated = 0;
            int attempts = 0;
            while (existing + generated < desired && attempts++ < desired * 4)
            {
                Pawn casualty = SpawnPawn(
                    map,
                    kind,
                    faction,
                    FindFixtureCell(map, generated + 24, responder: false));
                if (casualty == null)
                {
                    continue;
                }

                AddBenchmarkInjuries(casualty);
                HealthUtility.TryAnesthetize(casualty);
                if (casualty.Downed && !casualty.Dead)
                {
                    generated++;
                }
                else if (!casualty.Destroyed)
                {
                    casualty.Destroy();
                }
            }
        }

        private static IntVec3 FindFixtureCell(Map map, int index, bool responder)
        {
            int side = index % 6;
            int row = index / 6;
            IntVec3 preferred = map.Center +
                                new IntVec3(
                                    responder ? -16 - row * 2 : 6 + side * 4,
                                    0,
                                    responder ? -10 + side * 4 : -18 + row * 6);
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(preferred, 18f, true))
            {
                if (cell.InBounds(map) && cell.Standable(map) &&
                    cell.GetFirstPawn(map) == null &&
                    !cell.GetThingList(map).OfType<Building_Bed>().Any())
                {
                    return cell;
                }
            }

            return CellFinder.RandomClosewalkCellNear(map.Center, map, 30);
        }

        private static void AddBenchmarkInjuries(Pawn pawn)
        {
            List<BodyPartRecord> parts = pawn.health.hediffSet
                .GetNotMissingParts()
                .Where(part => part.depth == BodyPartDepth.Outside && !part.IsCorePart)
                .Take(4)
                .ToList();
            foreach (BodyPartRecord part in parts)
            {
                Hediff_Injury injury = HediffMaker.MakeHediff(
                    HediffDefOf.Cut,
                    pawn,
                    part) as Hediff_Injury;
                if (injury == null)
                {
                    continue;
                }

                // Four shallow, independently tendable wounds provide bleeding pressure
                // without the random instant deaths produced by DamageUntilDowned.
                injury.Severity = 4f;
                pawn.health.AddHediff(injury, part);
            }
        }

        private static void EnsureMedicalSleepingSpots(Map map, int desired)
        {
            int existing = map.listerBuildings.AllBuildingsColonistOfClass<Building_Bed>()
                .Count(bed => bed.Medical && bed.def.building.bed_humanlike);
            for (int index = existing; index < desired; index++)
            {
                Building_Bed bed = ThingMaker.MakeThing(ThingDefOf.SleepingSpot) as Building_Bed;
                if (bed == null)
                {
                    return;
                }

                IntVec3 cell = FindFixtureCell(map, index + desired, responder: true);
                bed.SetFaction(Faction.OfPlayer);
                GenSpawn.Spawn(bed, cell, map);
                bed.Medical = true;
                GeneratedMedicalSpots.Add(bed);
            }
        }

        private static void EnsureMedicine(
            Map map,
            ThingDef medicineDef,
            int desiredCount,
            int cellOffset)
        {
            int existing = map.listerThings.ThingsOfDef(medicineDef)
                .Where(thing => thing.Spawned && !thing.Destroyed)
                .Sum(thing => thing.stackCount);
            int remaining = desiredCount - existing;
            int index = 0;
            while (remaining > 0)
            {
                Thing medicine = ThingMaker.MakeThing(medicineDef);
                medicine.stackCount = Mathf.Min(remaining, medicine.def.stackLimit);
                GenSpawn.Spawn(
                    medicine,
                    FindFixtureCell(map, cellOffset + index++, responder: true),
                    map);
                medicine.SetForbidden(false, false);
                remaining -= medicine.stackCount;
            }
        }

        private sealed class BenchmarkPreset
        {
            public static readonly BenchmarkPreset LargeMixed =
                new BenchmarkPreset("large-mixed-18x6x6", 18, 6, 6, false, true, true);
            public static readonly BenchmarkPreset SmallMixed =
                new BenchmarkPreset("small-mixed-6x2x2", 6, 2, 2, false, true, true);
            public static readonly BenchmarkPreset TreatmentGraph =
                new BenchmarkPreset("treatment-graph-18x6", 18, 6, 0, false, true, false);
            public static readonly BenchmarkPreset RescueWithoutBeds =
                new BenchmarkPreset("rescue-no-beds-18x6", 18, 0, 6, false, false, true);
            public static readonly BenchmarkPreset MedicalLogistics =
                new BenchmarkPreset("medical-logistics-12x3x6", 12, 3, 6, false, true, false);
            public static readonly BenchmarkPreset CaptureTriage =
                new BenchmarkPreset("capture-triage-12x4x4", 12, 4, 4, true, true, true, true);

            public readonly string Name;
            public readonly int Casualties;
            public readonly int Doctors;
            public readonly int Carriers;
            public readonly bool HostileTargets;
            public readonly bool Capture;
            public readonly bool Treat;
            public readonly bool Rescue;

            private BenchmarkPreset(
                string name,
                int casualties,
                int doctors,
                int carriers,
                bool hostileTargets,
                bool treat,
                bool rescue,
                bool capture = false)
            {
                Name = name;
                Casualties = casualties;
                Doctors = doctors;
                Carriers = carriers;
                HostileTargets = hostileTargets;
                Capture = capture;
                Treat = treat;
                Rescue = rescue;
            }

            public string StageLabel =>
                string.Join("+", new[]
                    {
                        Capture ? "capture" : null,
                        Treat ? "treat" : null,
                        Rescue ? "rescue" : null
                    }
                    .Where(stage => stage != null));
        }

        private static void DisableAllWork(Pawn responder)
        {
            Compatibility.DisableAllWorkForBenchmark(responder);
        }

        private static void ConfigureDoctor(Pawn responder, int index)
        {
            Compatibility.SetWorkPriorityForBenchmark(
                responder,
                SearchAndRescueDefOf.SAR_FieldRescue,
                1);
            Compatibility.SetWorkPriorityForBenchmark(responder, WorkTypeDefOf.Doctor, 1);
            if (responder.skills == null)
            {
                return;
            }

            SkillRecord medicine = responder.skills.GetSkill(SkillDefOf.Medicine);
            if (medicine != null)
            {
                medicine.Level = DoctorSkillLevels[Mathf.Min(index, DoctorSkillLevels.Length - 1)];
            }
        }

        private static void ConfigureCarrier(Pawn responder, int index)
        {
            Compatibility.SetWorkPriorityForBenchmark(
                responder,
                SearchAndRescueDefOf.SAR_FieldRescue,
                1);
            Compatibility.SetWorkPriorityForBenchmark(responder, WorkTypeDefOf.Hauling, 1);
            if (index < 2 && !responder.WorkTypeIsDisabled(WorkTypeDefOf.Warden))
            {
                Compatibility.SetWorkPriorityForBenchmark(responder, WorkTypeDefOf.Warden, 1);
            }
        }

        private static void AddStage(
            Map map,
            SearchAndRescueCoordinator coordinator,
            Pawn casualty,
            DesignationDef designationDef,
            SearchAndRescueStage stage)
        {
            if (map.designationManager.DesignationOn(casualty, designationDef) != null)
            {
                return;
            }

            map.designationManager.AddDesignation(new StageDesignation(casualty, designationDef));
            coordinator?.NotifyStageDesignationAdded(casualty, stage);
        }

        private static void ConfigureBenchmarkRescuePoint(
            Map map,
            SearchAndRescueCoordinator coordinator,
            bool enabled)
        {
            foreach (Designation designation in map.designationManager
                         .SpawnedDesignationsOfDef(SearchAndRescueDefOf.SAR_RescuePoint)
                         .ToList())
            {
                map.designationManager.RemoveDesignation(designation);
            }

            if (enabled)
            {
                IntVec3 cell = FindFixtureCell(map, 48, responder: true);
                map.designationManager.AddDesignation(
                    new Designation(cell, SearchAndRescueDefOf.SAR_RescuePoint));
            }
            coordinator?.NotifyRescuePointChanged();
        }
    }
}
