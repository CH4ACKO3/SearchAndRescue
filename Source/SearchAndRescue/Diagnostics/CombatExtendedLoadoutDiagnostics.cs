using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using Verse;
using Verse.AI;

namespace SearchAndRescue
{
    internal static class CombatExtendedLoadoutDiagnostics
    {
        private const string MedicName = "SAR CE Loadout Medic";
        private const string PatientName = "SAR CE Loadout Patient";
        private static int preparedAt;
        private static string selectedMedicineId;
        private static int selectedInjuryLoadId = -1;

        [DebugAction("Search and Rescue", "Create CE loadout medical fixture",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void CreateFixture()
        {
            if (!Compatibility.UsesCombatExtended)
            {
                Log.Message("[SAR CE loadout regression] SKIP: Combat Extended is not active.");
                return;
            }

            Map map = Find.CurrentMap;
            if (FindNamed(map, MedicName) != null)
            {
                Log.Message("[SAR CE loadout regression] NOT RUN: this map already contains the persistent " +
                            "fixture. Run 'Check CE loadout treatment consumption'.");
                return;
            }
            Pawn worker = SpawnNamed(map, MedicName, -6, requireDoctor: true);
            Pawn patient = SpawnNamed(map, PatientName, 0);
            worker.workSettings.SetPriority(SearchAndRescueDefOf.SAR_FieldRescue, 3);
            worker.workSettings.SetPriority(WorkTypeDefOf.Doctor, 3);
            worker.skills.GetSkill(SkillDefOf.Medicine).Level = 12;
            patient.playerSettings.medCare = MedicalCareCategory.Best;

            ThingDef medicineDef = ThingDefOf.MedicineIndustrial;
            ThingDef bloodDef = Compatibility.MoreInjuriesBloodBag;
            ThingDef deviceDef = DefDatabase<ThingDef>.GetNamedSilentFail("Defibrillator") ??
                                 DefDatabase<ThingDef>.GetNamedSilentFail("SuctionDevice");
            var slots = new List<(ThingDef Def, int Count)> { (medicineDef, 2) };
            if (bloodDef != null) slots.Add((bloodDef, 1));
            if (deviceDef != null) slots.Add((deviceDef, 1));
            object loadout = CeApi.CreateAndAssign(worker, "SAR CE medical fixture", slots);

            Thing medicine = AddInventory(worker, medicineDef, 2);
            Thing blood = bloodDef == null ? null : AddInventory(worker, bloodDef, 1);
            Thing device = deviceDef == null ? null : AddInventory(worker, deviceDef, 1);
            CeApi.Refresh(worker);

            var ledger = new MedicalResourceLedger(map);
            Check(ledger.AvailableMedicines(worker, patient).Contains(medicine),
                "SAR sees medicine carried for CE loadout");
            if (blood != null)
                Check(ledger.FindBest(worker, patient, bloodDef, false, 1) == blood,
                    "SAR selects carried More Injuries blood");
            if (device != null)
                Check(ledger.FindBest(worker, patient, deviceDef, true, 1) == device,
                    "SAR selects carried More Injuries device");
            if (blood != null)
                CheckMoreInjuriesOption(worker, patient, ledger, blood,
                    MedicalIntervention.Blood, reusable: false);
            if (device != null)
                CheckMoreInjuriesOption(worker, patient, ledger, device,
                    deviceDef.defName == "Defibrillator"
                        ? MedicalIntervention.Defibrillate
                        : MedicalIntervention.Suction,
                    reusable: true);
            Check(!CeApi.GetAnythingForDrop(worker, out _, out _),
                "CE unload keeps exact loadout medical quota");

            RunProtectedSourceCheck(map, patient);

            Thing refill = ThingMaker.MakeThing(medicineDef);
            refill.stackCount = 6;
            GenSpawn.Spawn(refill, Cell(map, 12), map);
            preparedAt = Find.TickManager.TicksGame;

            Hediff injury = HediffMaker.MakeHediff(HediffDefOf.Cut, patient,
                patient.RaceProps.body.AllParts.First(part => part.depth == BodyPartDepth.Outside));
            injury.Severity = 8f;
            patient.health.AddHediff(injury);
            selectedInjuryLoadId = injury.loadID;
            MedicalCarePlan plan = MedicalCarePlan.Build(patient, Find.TickManager.TicksGame);
            MedicalTreatmentOption option = Compatibility.FindTreatmentOptions(worker, patient, plan, ledger)
                .FirstOrDefault(candidate =>
                    candidate.Intervention == MedicalIntervention.CombatExtendedStabilize &&
                    candidate.Resource == medicine);
            Check(option?.IsValid == true && option.FromInventory,
                "SAR treatment option uses carried CE loadout medicine");
            Job treatment = Compatibility.MakeTreatmentRoundJob(worker, patient, option);
            Check(treatment?.def?.defName == "Stabilize" && treatment.targetB.Thing == medicine,
                "SAR constructs native CE stabilization from carried medicine");
            selectedMedicineId = medicine.ThingID;
            if (treatment != null)
            {
                // Drafting keeps CE's normal loadout ThinkNode from refilling between the
                // treatment-completion observation and the explicit refill phase below.
                worker.drafter.Drafted = true;
                // CE's driver touches the patient but does not reserve a standing patient's
                // movement. Keep this synthetic patient still so the result measures CE's
                // treatment rather than an unrelated AI move order interrupting the loop.
                patient.drafter.Drafted = true;
                patient.jobs.StopAll();
                worker.jobs.StartJob(treatment, JobCondition.InterruptForced);
            }

            Log.Message("[SAR CE loadout regression] Fixture retained for save/load. Advance at least 600 ticks, " +
                        "then run 'Check CE loadout treatment consumption'. Pawns: " + worker.ThingID + ", " +
                        patient.ThingID + "; selectedResource=" + selectedMedicineId + "; loadout=" + loadout);
        }

        [DebugAction("Search and Rescue", "Check CE loadout treatment consumption",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void CheckTreatmentConsumption()
        {
            Map map = Find.CurrentMap;
            Pawn worker = FindNamed(map, MedicName);
            Pawn patient = FindNamed(map, PatientName);
            if (worker == null || patient == null)
            {
                Log.Message("[SAR CE loadout regression] NOT RUN: create the fixture on this map first.");
                return;
            }

            if (worker.CurJobDef?.defName == "Stabilize")
            {
                Log.Message("[SAR CE loadout regression] NOT READY: CE stabilization is still running.");
                return;
            }

            int carried = worker.inventory.innerContainer.TotalStackCountOfDef(ThingDefOf.MedicineIndustrial);
            Thing original = worker.inventory.innerContainer
                .FirstOrDefault(thing => selectedMedicineId == null || thing.ThingID == selectedMedicineId);
            Check(Find.TickManager.TicksGame - preparedAt >= 600 || preparedAt == 0,
                "fixture observed after treatment window");
            Hediff selectedInjury = selectedInjuryLoadId >= 0
                ? patient.health.hediffSet.hediffs
                    .FirstOrDefault(hediff => hediff.loadID == selectedInjuryLoadId)
                : patient.health.hediffSet.hediffs
                    .Where(hediff => hediff.def == HediffDefOf.Cut)
                    .OrderByDescending(hediff => hediff.Severity)
                    .FirstOrDefault();
            Check(selectedInjury == null ? selectedInjuryLoadId >= 0 : CeApi.IsStabilized(selectedInjury),
                "native CE stabilization affected the selected wound");
            Check(carried == 1, "one selected loadout medicine was consumed before refill");
            Check(original == null || original.stackCount == 1,
                "recorded resource stack lost exactly one unit: " + (selectedMedicineId ?? "after-load fallback"));
            Check(worker.drafter?.Drafted == true && worker.CurJobDef != JobDefOf.TakeCountToInventory,
                "drafted observation boundary prevented automatic CE refill");

            Job refill = CeApi.GetUpdateJob(worker);
            Check(refill?.def == JobDefOf.TakeCountToInventory &&
                  refill.targetA.Thing?.def == ThingDefOf.MedicineIndustrial && refill.count == 1,
                "CE creates an exact one-unit refill after SAR treatment consumption");
            if (refill != null)
                worker.jobs.StartJob(refill, JobCondition.InterruptForced);
            Log.Message("[SAR CE loadout regression] Refill started. Advance at least 300 ticks, then run " +
                        "'Finish CE loadout refill fixture'.");
        }

        [DebugAction("Search and Rescue", "Finish CE loadout refill fixture",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void FinishRefill()
        {
            Pawn worker = FindNamed(Find.CurrentMap, MedicName);
            if (worker == null)
            {
                Log.Message("[SAR CE loadout regression] NOT RUN: create the fixture on this map first.");
                return;
            }
            if (worker.CurJobDef == JobDefOf.TakeCountToInventory)
            {
                Log.Message("[SAR CE loadout regression] NOT READY: CE refill is still running.");
                return;
            }

            CeApi.Refresh(worker);
            int carried = worker.inventory.innerContainer.TotalStackCountOfDef(ThingDefOf.MedicineIndustrial);
            Check(carried == 2, "CE refill restored configured medicine count");
            Job next = CeApi.GetUpdateJob(worker);
            Check(next == null || next.def != JobDefOf.TakeCountToInventory ||
                  next.targetA.Thing?.def != ThingDefOf.MedicineIndustrial,
                "CE reports no remaining medical loadout deficit");
            Log.Message("[SAR CE loadout regression] COMPLETE: actual treatment consumption and refill checked. " +
                        "Fixture pawns remain on the map and may be saved.");
        }

        [DebugAction("Search and Rescue", "Run MI CE loadout inventory regressions",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void RunMoreInjuriesInventoryRegressions()
        {
            if (!Compatibility.UsesCombatExtended || !Compatibility.UsesMoreInjuries)
            {
                Log.Message("[SAR CE loadout regression] SKIP: Combat Extended and More Injuries must both be active.");
                return;
            }

            Map map = Find.CurrentMap;
            Pawn worker = SpawnNamed(map, "SAR MI CE Inventory Probe", -10, requireDoctor: true);
            Pawn patient = SpawnNamed(map, "SAR MI CE Inventory Patient", -7);
            object loadout = null;
            try
            {
                worker.workSettings.SetPriority(SearchAndRescueDefOf.SAR_FieldRescue, 3);
                worker.workSettings.SetPriority(WorkTypeDefOf.Doctor, 3);
                worker.skills.GetSkill(SkillDefOf.Medicine).Level = 12;

                var cases = new[]
                {
                    new MiInventoryCase(Compatibility.MoreInjuriesBloodBag,
                        MedicalIntervention.Blood, "UseBloodBag"),
                    new MiInventoryCase(Compatibility.MoreInjuriesSalineBag,
                        MedicalIntervention.Saline, "UseSalineBag"),
                    new MiInventoryCase(Compatibility.MoreInjuriesHemostaticAgent,
                        MedicalIntervention.HemostaticAgent, "UseHemostaticAgent"),
                    new MiInventoryCase(Compatibility.MoreInjuriesBandage,
                        MedicalIntervention.Bandage, "UseBandage")
                };
                Check(cases[0].Def?.defName == "WholeBloodBag",
                    "Th3Fr3d More Injuries blood resource resolves to WholeBloodBag");
                Check(cases.All(test => test.Def != null),
                    "blood, saline, hemostatic agent, and bandage defs are loaded");
                if (cases.Any(test => test.Def == null)) return;

                loadout = CeApi.CreateAndAssign(worker, "SAR MI CE inventory probe",
                    cases.Select(test => (test.Def, 1)));
                var resources = new Dictionary<ThingDef, Thing>();
                foreach (MiInventoryCase test in cases)
                    resources[test.Def] = AddInventory(worker, test.Def, 1);
                CeApi.Refresh(worker);

                var ledger = new MedicalResourceLedger(map);
                foreach (MiInventoryCase test in cases)
                {
                    Thing resource = resources[test.Def];
                    var plan = new MedicalCarePlan(patient, Find.TickManager.TicksGame, int.MaxValue, 0,
                        new[] { new MedicalResourceDemand(test.Def, test.Intervention, 1, true, false, 1d) });
                    MedicalTreatmentOption option = Compatibility.FindTreatmentOptions(worker, patient, plan, ledger)
                        .FirstOrDefault(candidate => candidate.Intervention == test.Intervention);
                    Check(option?.Resource == resource && option.FromInventory,
                        "MI " + test.Intervention + " selects the CE loadout item from the doctor's inventory");
                    Job job = Compatibility.MakeTreatmentRoundJob(worker, patient, option);
                    Check(job?.def?.defName == test.JobDefName && job.targetB.Thing == resource,
                        "MI " + test.Intervention + " dispatcher targets that inventory item via " +
                        test.JobDefName);
                }

                Check(!CeApi.GetAnythingForDrop(worker, out _, out _),
                    "CE unload protects every exact MI loadout quota");
                Log.Message("[SAR CE loadout regression] COMPLETE: MI blood/saline/hemostatic/bandage " +
                            "inventory selection and native dispatch checked; inspect the PASS/FAIL lines above.");
            }
            finally
            {
                if (loadout != null) CeApi.Remove(worker, loadout);
                if (!worker.Destroyed) worker.Destroy(DestroyMode.Vanish);
                if (!patient.Destroyed) patient.Destroy(DestroyMode.Vanish);
            }
        }

        private sealed class MiInventoryCase
        {
            internal readonly ThingDef Def;
            internal readonly MedicalIntervention Intervention;
            internal readonly string JobDefName;

            internal MiInventoryCase(ThingDef def, MedicalIntervention intervention, string jobDefName)
            {
                Def = def;
                Intervention = intervention;
                JobDefName = jobDefName;
            }
        }

        private static void CheckMoreInjuriesOption(
            Pawn worker,
            Pawn patient,
            MedicalResourceLedger ledger,
            Thing resource,
            MedicalIntervention intervention,
            bool reusable)
        {
            var plan = new MedicalCarePlan(patient, Find.TickManager.TicksGame, int.MaxValue, 0,
                new[] { new MedicalResourceDemand(resource.def, intervention, 1, true, reusable, 1d) });
            MedicalTreatmentOption option = Compatibility.FindTreatmentOptions(worker, patient, plan, ledger)
                .FirstOrDefault(candidate => candidate.Intervention == intervention);
            Check(option?.Resource == resource && option.FromInventory,
                "MI " + intervention + " option keeps carried CE loadout resource");
            Job job = Compatibility.MakeTreatmentRoundJob(worker, patient, option);
            Check(job != null, "MI " + intervention + " dispatcher accepts carried CE loadout resource");
        }

        private static void RunProtectedSourceCheck(Map map, Pawn patient)
        {
            Pawn probe = SpawnNamed(map, "SAR CE Protected Source Probe", -14);
            object loadout = null;
            Thing protectedSupply = null;
            Thing ordinarySupply = null;
            try
            {
                loadout = CeApi.CreateAndAssign(probe, "SAR CE protected-source probe",
                    new List<(ThingDef Def, int Count)> { (ThingDefOf.MedicineIndustrial, 1) });
                CeApi.Refresh(probe);
                protectedSupply = ThingMaker.MakeThing(ThingDefOf.MedicineIndustrial);
                ordinarySupply = ThingMaker.MakeThing(ThingDefOf.MedicineIndustrial);
                GenSpawn.Spawn(protectedSupply, CellNear(probe, 2), map);
                GenSpawn.Spawn(ordinarySupply, CellNear(probe, 16), map);
                var ledger = (MedicalResourceLedger)AccessTools.Field(
                        typeof(SearchAndRescueCoordinator), "medicalResources")
                    .GetValue(map.GetComponent<SearchAndRescueCoordinator>());
                Check(ledger.TryClaim(protectedSupply, patient, patient, 1, false,
                        Find.TickManager.TicksGame + 600, MedicalResourceAccess.Treatment),
                    "fixture claims nearest medicine for SAR");
                Check(SearchAndRescueJobContext.IsProtectedOrClaimedMedicalSupply(protectedSupply),
                    "nearest CE candidate is SAR protected");
                Job refill = CeApi.GetUpdateJob(probe);
                Check(refill?.def == JobDefOf.TakeCountToInventory && refill.targetA.Thing == ordinarySupply,
                    "CE skips protected nearest supply and selects farther ordinary stock");
                ledger.ReleasePatientClaims(patient);
            }
            finally
            {
                if (loadout != null) CeApi.Remove(probe, loadout);
                if (!probe.Destroyed) probe.Destroy(DestroyMode.Vanish);
                if (protectedSupply != null && !protectedSupply.Destroyed)
                    protectedSupply.Destroy(DestroyMode.Vanish);
                if (ordinarySupply != null && !ordinarySupply.Destroyed)
                    ordinarySupply.Destroy(DestroyMode.Vanish);
            }
        }

        private static Pawn SpawnNamed(Map map, string name, int offset, bool requireDoctor = false)
        {
            Pawn existing = FindNamed(map, name);
            if (existing != null) return existing;
            Pawn pawn = null;
            for (int attempt = 0; attempt < 30; attempt++)
            {
                pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                if (!requireDoctor || !pawn.WorkTypeIsDisabled(WorkTypeDefOf.Doctor)) break;
                pawn.Destroy(DestroyMode.Vanish);
                pawn = null;
            }
            if (pawn == null) throw new InvalidOperationException("Could not generate a CE loadout doctor fixture.");
            pawn.Name = new NameTriple("SAR", name, "Fixture");
            GenSpawn.Spawn(pawn, Cell(map, offset), map);
            pawn.workSettings.EnableAndInitializeIfNotAlreadyInitialized();
            return pawn;
        }

        private static Pawn FindNamed(Map map, string name) => map.mapPawns.AllPawnsSpawned
            .FirstOrDefault(pawn => pawn.Name?.ToStringShort == name);

        private static Thing AddInventory(Pawn pawn, ThingDef def, int count)
        {
            Thing thing = ThingMaker.MakeThing(def);
            thing.stackCount = count;
            pawn.inventory.innerContainer.TryAdd(thing);
            return thing;
        }

        private static IntVec3 Cell(Map map, int offset) =>
            GenRadial.RadialCellsAround(map.Center + new IntVec3(offset, 0, 0), 10f, true)
                .First(cell => cell.InBounds(map) && cell.Standable(map) && cell.GetFirstPawn(map) == null);

        private static IntVec3 CellNear(Pawn pawn, int distance) =>
            GenRadial.RadialCellsAround(pawn.Position, distance, false)
                .Where(cell => cell.InBounds(pawn.Map) && cell.Standable(pawn.Map) &&
                               cell.GetFirstPawn(pawn.Map) == null)
                .OrderBy(cell => Math.Abs(cell.DistanceToSquared(pawn.Position) - distance * distance))
                .First();

        private static void Check(bool condition, string label)
        {
            Log.Message("[SAR CE loadout regression] " + (condition ? "PASS: " : "FAIL: ") + label);
        }

        private static class CeApi
        {
            private static readonly Type Loadout = AccessTools.TypeByName("CombatExtended.Loadout");
            private static readonly Type Slot = AccessTools.TypeByName("CombatExtended.LoadoutSlot");
            private static readonly Type Manager = AccessTools.TypeByName("CombatExtended.LoadoutManager");
            private static readonly Type Utility = AccessTools.TypeByName("CombatExtended.Utility_Loadouts");
            private static readonly Type UpdateGiver = AccessTools.TypeByName("CombatExtended.JobGiver_UpdateLoadout");
            private static readonly Type Inventory = AccessTools.TypeByName("CombatExtended.CompInventory");
            private static readonly Type HoldTracker = AccessTools.TypeByName("CombatExtended.Utility_HoldTracker");

            internal static object CreateAndAssign(
                Pawn pawn,
                string label,
                IEnumerable<(ThingDef Def, int Count)> items)
            {
                object loadout = Activator.CreateInstance(Loadout, new object[] { label });
                MethodInfo addSlot = AccessTools.Method(Loadout, "AddSlot", new[] { Slot });
                foreach ((ThingDef def, int count) in items)
                    addSlot.Invoke(loadout, new[] { Activator.CreateInstance(Slot, new object[] { def, count }) });
                AccessTools.Method(Manager, "AddLoadout", new[] { Loadout }).Invoke(null, new[] { loadout });
                AccessTools.Method(Utility, "SetLoadout", new[] { typeof(Pawn), Loadout })
                    .Invoke(null, new[] { (object)pawn, loadout });
                return loadout;
            }

            internal static void Refresh(Pawn pawn)
            {
                ThingComp comp = pawn.AllComps.FirstOrDefault(Inventory.IsInstanceOfType);
                AccessTools.Method(Inventory, "UpdateInventory").Invoke(comp, Array.Empty<object>());
            }

            internal static Job GetUpdateJob(Pawn pawn) =>
                (Job)AccessTools.Method(UpdateGiver, "GetUpdateLoadoutJob", new[] { typeof(Pawn) })
                    .Invoke(null, new object[] { pawn });

            internal static bool GetAnythingForDrop(Pawn pawn, out Thing thing, out int count)
            {
                object[] args = { pawn, null, 0 };
                bool result = (bool)AccessTools.Method(HoldTracker, "GetAnythingForDrop")
                    .Invoke(null, args);
                thing = args[1] as Thing;
                count = (int)args[2];
                return result;
            }

            internal static bool IsStabilized(Hediff hediff)
            {
                Type compType = AccessTools.TypeByName("CombatExtended.HediffComp_Stabilize");
                HediffComp comp = (hediff as HediffWithComps)?.comps
                    .FirstOrDefault(candidate => compType?.IsInstanceOfType(candidate) == true);
                return comp != null &&
                       AccessTools.Property(compType, "Stabilized").GetValue(comp) is bool stabilized &&
                       stabilized;
            }

            internal static void Remove(Pawn pawn, object loadout)
            {
                object fallback = AccessTools.Property(Manager, "DefaultLoadout").GetValue(null);
                AccessTools.Method(Utility, "SetLoadout", new[] { typeof(Pawn), Loadout })
                    .Invoke(null, new[] { (object)pawn, fallback });
                AccessTools.Method(Manager, "RemoveLoadout", new[] { Loadout })
                    .Invoke(null, new[] { loadout });
            }
        }
    }
}
