using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;
using Verse.AI;

namespace SearchAndRescue
{
    internal static class Compatibility
    {
        private static readonly JobDef FirstAidJob = DefDatabase<JobDef>.GetNamedSilentFail("CP_FirstAid");
        private static readonly JobDef ArrestHereJob = DefDatabase<JobDef>.GetNamedSilentFail("CP_ImprisonInPlace");
        private static readonly JobDef StabilizeJob = DefDatabase<JobDef>.GetNamedSilentFail("Stabilize");
        private static readonly JobDef MoreInjuriesFirstAidJob =
            DefDatabase<JobDef>.GetNamedSilentFail("ProvideFirstAid");
        private static readonly JobDef MoreInjuriesCprJob =
            DefDatabase<JobDef>.GetNamedSilentFail("PerformCpr");
        private static readonly JobDef MoreInjuriesSuctionJob =
            DefDatabase<JobDef>.GetNamedSilentFail("UseSuctionDevice");
        private static readonly JobDef MoreInjuriesDefibrillatorJob =
            DefDatabase<JobDef>.GetNamedSilentFail("UseDefibrillator");
        private static readonly JobDef MoreInjuriesEpinephrineJob =
            DefDatabase<JobDef>.GetNamedSilentFail("UseEpinephrine");
        private static readonly JobDef MoreInjuriesTourniquetJob =
            DefDatabase<JobDef>.GetNamedSilentFail("UseTourniquet");
        private static readonly JobDef MoreInjuriesRemoveTourniquetJob =
            DefDatabase<JobDef>.GetNamedSilentFail("RemoveTourniquetSafely");
        private static readonly JobDef MoreInjuriesHemostaticJob =
            DefDatabase<JobDef>.GetNamedSilentFail("UseHemostaticAgent");
        private static readonly JobDef MoreInjuriesBandageJob =
            DefDatabase<JobDef>.GetNamedSilentFail("UseBandage");
        private static readonly JobDef MoreInjuriesSalineJob =
            DefDatabase<JobDef>.GetNamedSilentFail("UseSalineBag");
        private static readonly JobDef MoreInjuriesBloodJob =
            DefDatabase<JobDef>.GetNamedSilentFail("UseBloodBag");
        private static readonly JobDef HemogenDirectJob =
            DefDatabase<JobDef>.GetNamedSilentFail("HD_AdministerHemogen");
        private static readonly JobDef EmergencyTransfusionJob =
            DefDatabase<JobDef>.GetNamedSilentFail("ET_TransfuseBlood");
        private static readonly ThingDef HemogenPackDef =
            DefDatabase<ThingDef>.GetNamedSilentFail("HemogenPack");
        private static readonly ResearchProjectDef MoreInjuriesCprResearch =
            DefDatabase<ResearchProjectDef>.GetNamedSilentFail("Cpr");
        private static readonly ResearchProjectDef MoreInjuriesEmergencyMedicine =
            DefDatabase<ResearchProjectDef>.GetNamedSilentFail("EmergencyMedicine");
        private static readonly WorkTypeDef NursingWork = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Nursing");
        private static readonly TrainableDef RescueTraining = DefDatabase<TrainableDef>.GetNamedSilentFail("Rescue");
        private static readonly WorkGiverDef NursingRescueWorkGiver =
            DefDatabase<WorkGiverDef>.GetNamedSilentFail("SAR_RescueMarkedNursing");
        private static readonly WorkGiverDef NursingTreatmentWorkGiver =
            DefDatabase<WorkGiverDef>.GetNamedSilentFail("SAR_SupportiveCareMarkedNursing");
        private static readonly WorkGiverDef ParamedicRescueWorkGiver =
            DefDatabase<WorkGiverDef>.GetNamedSilentFail("SAR_RescueMarkedParamedic");
        private static readonly TraitDef SlowLearnerTrait =
            DefDatabase<TraitDef>.GetNamedSilentFail("SlowLearner");
        private static readonly Lazy<HashSet<HediffDef>> SurgeryRemovableHediffs =
            new Lazy<HashSet<HediffDef>>(() => new HashSet<HediffDef>(
                DefDatabase<RecipeDef>.AllDefsListForReading
                    .Where(recipe => recipe.removesHediff != null)
                    .Select(recipe => recipe.removesHediff)));

        private static readonly Func<Pawn, WorkGiverDef, int, int> WorkTabGetPriority = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("WorkTab.Pawn_Extensions", false))
            .Where(type => type != null)
            .Select(type => type.GetMethod("GetPriority", BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(Pawn), typeof(WorkGiverDef), typeof(int) }, null))
            .Where(method => method != null && method.ReturnType == typeof(int))
            .Select(method => (Func<Pawn, WorkGiverDef, int, int>)Delegate.CreateDelegate(
                typeof(Func<Pawn, WorkGiverDef, int, int>), method))
            .FirstOrDefault();
        private static readonly Func<Pawn, WorkTypeDef, int, int> WorkTabGetWorkTypePriority = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("WorkTab.Pawn_Extensions", false))
            .Where(type => type != null)
            .Select(type => type.GetMethod("GetPriority", BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(Pawn), typeof(WorkTypeDef), typeof(int) }, null))
            .Where(method => method != null && method.ReturnType == typeof(int))
            .Select(method => (Func<Pawn, WorkTypeDef, int, int>)Delegate.CreateDelegate(
                typeof(Func<Pawn, WorkTypeDef, int, int>), method))
            .FirstOrDefault();
        private static readonly MethodInfo WorkTabDisableAll = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("WorkTab.Pawn_Extensions", false))
            .Where(type => type != null)
            .Select(type => type.GetMethod("DisableAll", BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(Pawn) }, null))
            .FirstOrDefault(method => method != null);
        private static readonly MethodInfo WorkTabSetWorkTypePriority = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("WorkTab.Pawn_Extensions", false))
            .Where(type => type != null)
            .Select(type => type.GetMethod("SetPriority", BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(Pawn), typeof(WorkTypeDef), typeof(int), typeof(List<int>) }, null))
            .FirstOrDefault(method => method != null);
        private static readonly List<int> WorkTabAllHours = Enumerable.Range(0, 24).ToList();

        private static readonly MethodInfo SmartMedicineFindMethod = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("SmartMedicine.FindBestMedicine", false))
            .Where(type => type != null)
            .Select(type => type.GetMethod("Find", BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(Pawn), typeof(Pawn), typeof(int).MakeByRefType(), typeof(bool) }, null))
            .FirstOrDefault(method => method != null);

        private static readonly MethodInfo CombatExtendedCanBeStabilizedMethod = AppDomain.CurrentDomain
            .GetAssemblies()
            .Select(assembly => assembly.GetType("CombatExtended.CE_Utility", false))
            .Where(type => type != null)
            .Select(type => type.GetMethod(
                "CanBeStabilized",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(Hediff) },
                null))
            .FirstOrDefault(method => method != null);

        private static readonly Type CombatExtendedInventoryType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("CombatExtended.CompInventory", false))
            .FirstOrDefault(type => type != null);

        private static readonly MethodInfo CombatExtendedCanFitInventoryMethod = CombatExtendedInventoryType
            ?.GetMethod(
                "CanFitInInventory",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(Thing), typeof(int).MakeByRefType(), typeof(bool), typeof(bool) },
                null);

        private static readonly MethodInfo PharmacistTendAdviceMethod = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("Pharmacist.PharmacistUtility", false))
            .Where(type => type != null)
            .Select(type => type.GetMethod("TendAdvice", BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(Pawn) }, null))
            .FirstOrDefault(method => method != null && method.ReturnType == typeof(MedicalCareCategory));

        private static readonly Type ChooseYourMedicineUtilityType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("ChooseYourMedicine.Utility_GetList", false))
            .FirstOrDefault(type => type != null);

        private static readonly MethodInfo ChooseYourMedicineCareMethod = ChooseYourMedicineUtilityType?.GetMethod(
            "GetTheCorrectMedicalCareCategory",
            BindingFlags.Public | BindingFlags.Static,
            null,
            new[] { typeof(Pawn), typeof(bool).MakeByRefType() },
            null);

        private static readonly MethodInfo ChooseYourMedicineThingCareMethod = ChooseYourMedicineUtilityType?.GetMethod(
            "GetMedicalCareCategory",
            BindingFlags.Public | BindingFlags.Static,
            null,
            new[] { typeof(Thing) },
            null);

        private static readonly Type PatientTransferCompType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("PatientBedTransfer.CompPatientTransfer", false))
            .FirstOrDefault(type => type != null);

        private static readonly Type VehiclePawnType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("Vehicles.VehiclePawn", false))
            .FirstOrDefault(type => type != null);
        private static readonly MethodInfo VehicleTakeFromInventoryMethod = VehiclePawnType?.GetMethod(
            "TakeFromInventory",
            BindingFlags.Public | BindingFlags.Instance,
            null,
            new[] { typeof(Thing), typeof(int) },
            null);
        private static readonly MethodInfo VehicleAddOrTransferMethod = VehiclePawnType?.GetMethod(
            "AddOrTransfer",
            BindingFlags.Public | BindingFlags.Instance,
            null,
            new[] { typeof(Thing), typeof(int) },
            null);
        private static readonly FieldInfo VehiclePatherField = VehiclePawnType?.GetField(
            "vehiclePather",
            BindingFlags.Public | BindingFlags.Instance);
        private static readonly PropertyInfo VehiclePatherMovingProperty = VehiclePatherField?.FieldType.GetProperty(
            "Moving",
            BindingFlags.Public | BindingFlags.Instance);

        private static readonly MethodInfo FindBestPatientBedMethod = PatientTransferCompType?.GetMethod(
            "FindBestMedicalBedForPatient",
            BindingFlags.Public | BindingFlags.Instance,
            null,
            new[] { typeof(Pawn) },
            null);

        // Compatibility can be touched while RimWorld is still walking mod assemblies.
        // Resolving these eagerly permanently cached null whenever More Injuries happened
        // to load later, which silently suppressed all saline/blood demands for the session.
        private static readonly Lazy<MethodInfo> MoreInjuriesSalineCountMethod =
            new Lazy<MethodInfo>(() => FindMoreInjuriesTransfusionCountMethod(
                "MoreInjuries.HealthConditions.HeavyBleeding.Transfusions.JobDriver_UseSalineBag"));
        private static readonly Lazy<MethodInfo> MoreInjuriesBloodCountMethod =
            new Lazy<MethodInfo>(() => FindMoreInjuriesTransfusionCountMethod(
                "MoreInjuries.HealthConditions.HeavyBleeding.Transfusions.JobDriver_UseBloodBag"));
        private static readonly Lazy<Func<Hediff, bool>> MoreInjuriesHemostasisCanTreat =
            new Lazy<Func<Hediff, bool>>(() =>
            {
                MethodInfo method = FindLoadedType(
                        "MoreInjuries.HealthConditions.HeavyBleeding.JobDriver_HemostasisBase")
                    ?.GetMethod(
                        "JobCanTreat",
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        new[] { typeof(Hediff) },
                        null);
                return method == null || method.ReturnType != typeof(bool)
                    ? null
                    : (Func<Hediff, bool>)Delegate.CreateDelegate(typeof(Func<Hediff, bool>), method);
            });
        private static readonly Lazy<ThingDef> MoreInjuriesBloodDevice =
            new Lazy<ThingDef>(ResolveMoreInjuriesBloodDevice);

        public static bool UsesCombatExtended => StabilizeJob != null &&
            LoadedModManager.RunningModsListForReading.Any(mod =>
                string.Equals(mod.PackageId, "CETeam.CombatExtended", StringComparison.OrdinalIgnoreCase));
        public static bool UsesMoreInjuries => MoreInjuriesFirstAidJob != null &&
                                               IsRunningMod("th3fr3d.extendedinjuries");
        public static bool UsesSmartMedicine => SmartMedicineFindMethod != null;
        public static bool UsesPharmacist => PharmacistTendAdviceMethod != null;
        public static bool UsesChooseYourMedicine => ChooseYourMedicineCareMethod != null;
        public static bool UsesMoveThePatient => FindBestPatientBedMethod != null;
        public static bool UsesHemogenDirect => HemogenDirectJob != null && HemogenPackDef != null;
        public static bool UsesEmergencyTransfusions => EmergencyTransfusionJob != null && HemogenPackDef != null;
        public static bool UsesHemogenTransfusion => UsesEmergencyTransfusions || UsesHemogenDirect;
        public static bool NurseJobAvailable => NursingWork != null;
        public static bool UsesWorkTab => WorkTabGetPriority != null;

        internal static void DisableAllWorkForBenchmark(Pawn worker)
        {
            if (worker?.workSettings == null)
            {
                return;
            }

            if (WorkTabDisableAll != null)
            {
                try
                {
                    WorkTabDisableAll.Invoke(null, new object[] { worker });
                }
                catch (Exception exception)
                {
                    Log.WarningOnce("[Search and Rescue] Work Tab benchmark reset failed; " +
                                    "using vanilla work priorities. " +
                                    exception.GetBaseException().Message, 196320752);
                }
            }

            foreach (WorkTypeDef workType in DefDatabase<WorkTypeDef>.AllDefsListForReading)
            {
                if (!worker.WorkTypeIsDisabled(workType))
                {
                    // Work Tab stores a 24-hour schedule independently from the vanilla
                    // parent value. Explicitly clear every parent across every hour; its
                    // SetPriority implementation propagates that value to child WorkGivers.
                    SetWorkPriorityForBenchmark(worker, workType, 0);
                }
            }
        }

        internal static void SetWorkPriorityForBenchmark(Pawn worker, WorkTypeDef workType, int priority)
        {
            SetWorkTypePriority(worker, workType, priority, "benchmark");
        }

        internal static void SetWorkPriorityForMigration(Pawn worker, WorkTypeDef workType, int priority)
        {
            SetWorkTypePriority(worker, workType, priority, "migration");
        }

        private static void SetWorkTypePriority(
            Pawn worker,
            WorkTypeDef workType,
            int priority,
            string operation)
        {
            if (worker?.workSettings == null || workType == null || worker.WorkTypeIsDisabled(workType))
            {
                return;
            }

            worker.workSettings.SetPriority(workType, priority);
            if (WorkTabSetWorkTypePriority == null)
            {
                return;
            }

            try
            {
                WorkTabSetWorkTypePriority.Invoke(
                    null,
                    new object[] { worker, workType, priority, WorkTabAllHours });
            }
            catch (Exception exception)
            {
                Log.WarningOnce("[Search and Rescue] Work Tab " + operation + " priority setup failed; " +
                                "using the vanilla work priority. " +
                                exception.GetBaseException().Message,
                    196320753 + operation.GetHashCode());
            }
        }
        public static bool UsesVehiclesFramework => VehiclePawnType != null &&
            VehicleTakeFromInventoryMethod != null && VehicleAddOrTransferMethod != null &&
            VehiclePatherField != null && VehiclePatherMovingProperty?.PropertyType == typeof(bool);

        public static bool IsVehiclePawn(Pawn pawn)
        {
            return pawn != null && VehiclePawnType?.IsInstanceOfType(pawn) == true;
        }

        public static bool VehicleCargoSourceAvailable(Pawn vehicle, Pawn worker = null)
        {
            if (!UsesVehiclesFramework || !IsVehiclePawn(vehicle) || VehicleTakeFromInventoryMethod == null ||
                VehicleAddOrTransferMethod == null || vehicle.Destroyed || vehicle.Dead ||
                !vehicle.Spawned || vehicle.inventory == null || vehicle.Faction != Faction.OfPlayer)
            {
                return false;
            }

            if (worker != null && (worker.Map != vehicle.Map || worker.Faction != vehicle.Faction ||
                                   !worker.CanReach(vehicle, PathEndMode.Touch, Danger.Deadly)))
            {
                return false;
            }

            try
            {
                object pather = VehiclePatherField?.GetValue(vehicle);
                // Unknown movement state is not evidence that cargo is safe to unload.
                return pather != null && VehiclePatherMovingProperty.GetValue(pather, null) is bool moving && !moving;
            }
            catch (Exception exception)
            {
                Log.WarningOnce("[Search and Rescue] Could not query Vehicle Framework movement state. " +
                                exception.GetBaseException().Message,
                    196320754);
                return false;
            }
        }

        public static Thing TakeFromVehicleCargo(Pawn vehicle, Thing thing, int count)
        {
            if (!VehicleCargoSourceAvailable(vehicle) || thing == null || count <= 0 ||
                vehicle.inventory?.innerContainer.Contains(thing) != true || thing.stackCount < count)
            {
                return null;
            }

            try
            {
                return VehicleTakeFromInventoryMethod.Invoke(
                    vehicle,
                    new object[] { thing, count }) as Thing;
            }
            catch (Exception exception)
            {
                Log.WarningOnce("[Search and Rescue] Vehicle Framework cargo extraction failed. " +
                                exception.GetBaseException().Message,
                    196320755);
                return null;
            }
        }

        public static bool ReturnToVehicleCargo(Pawn vehicle, Thing thing)
        {
            if (!IsVehiclePawn(vehicle) || thing == null || thing.Destroyed ||
                VehicleAddOrTransferMethod == null)
            {
                return false;
            }

            try
            {
                int requested = thing.stackCount;
                return VehicleAddOrTransferMethod.Invoke(
                           vehicle,
                           new object[] { thing, requested }) is int transferred &&
                       transferred >= requested;
            }
            catch (Exception exception)
            {
                Log.WarningOnce("[Search and Rescue] Vehicle Framework cargo rollback failed. " +
                                exception.GetBaseException().Message,
                    196320756);
                return false;
            }
        }

        internal static int CombatExtendedStabilizableWoundCount(Pawn patient)
        {
            if (!UsesCombatExtended || patient?.health?.hediffSet == null ||
                CombatExtendedCanBeStabilizedMethod == null)
            {
                return 0;
            }

            int count = 0;
            // Keep this predicate byte-for-byte equivalent in meaning to CE's
            // JobDriver_Stabilize end condition. CanBeStabilized alone is broader than
            // the driver's input set: a Hediff can expose CE's stabilization comp while
            // not being in GetHediffsTendable() at this moment. Scheduling from the broad
            // set makes the driver succeed immediately without doing work, after which the
            // vanilla work tree can reacquire the same zero-effect job repeatedly in one tick.
            foreach (Hediff hediff in patient.health.hediffSet.GetHediffsTendable())
            {
                try
                {
                    if (CombatExtendedCanBeStabilizedMethod.Invoke(null, new object[] { hediff }) is bool can && can)
                    {
                        count++;
                    }
                }
                catch (Exception exception)
                {
                    Log.WarningOnce(
                        "[Search and Rescue] Could not query Combat Extended stabilization state. " +
                        exception.GetBaseException().Message,
                        170634921);
                    return 0;
                }
            }
            return count;
        }

        internal static bool CombatExtendedCanStabilize(Pawn patient)
        {
            return CombatExtendedStabilizableWoundCount(patient) > 0;
        }

        internal static int CombatExtendedInventoryCapacity(Pawn pawn, Thing thing)
        {
            if (!UsesCombatExtended || pawn == null || thing == null ||
                CombatExtendedInventoryType == null || CombatExtendedCanFitInventoryMethod == null)
            {
                return thing?.stackCount ?? 0;
            }

            ThingComp inventory = pawn.AllComps?.FirstOrDefault(CombatExtendedInventoryType.IsInstanceOfType);
            if (inventory == null)
            {
                return thing.stackCount;
            }

            try
            {
                object[] arguments = { thing, 0, false, false };
                return CombatExtendedCanFitInventoryMethod.Invoke(inventory, arguments) is bool fits && fits &&
                       arguments[1] is int count
                    ? Math.Max(0, Math.Min(thing.stackCount, count))
                    : 0;
            }
            catch (Exception exception)
            {
                Log.WarningOnce(
                    "[Search and Rescue] Could not query Combat Extended inventory capacity. " +
                    exception.GetBaseException().Message,
                    170634922);
                return 0;
            }
        }

        public static MedicalCareCategory EffectiveMedicalCare(Pawn patient)
        {
            MedicalCareCategory pawnLimit =
                patient?.playerSettings?.medCare ?? MedicalCareCategory.HerbalOrWorse;
            if (patient == null)
            {
                return pawnLimit;
            }

            if (TryGetChooseYourMedicinePolicy(patient, out List<MedicalCareCategory> categories,
                    out bool detailed))
            {
                if (categories.Count == 0)
                {
                    return MedicalCareCategory.NoMeds;
                }

                MedicalCareCategory advice = detailed ? categories.Max() : categories[0];
                return advice < pawnLimit ? advice : pawnLimit;
            }

            if (PharmacistTendAdviceMethod == null)
            {
                return pawnLimit;
            }

            try
            {
                MedicalCareCategory advice =
                    (MedicalCareCategory)PharmacistTendAdviceMethod.Invoke(null, new object[] { patient });
                // Pharmacist currently applies the pawn limit itself. Keep the explicit minimum
                // so a future version cannot accidentally broaden an individual care setting.
                return advice < pawnLimit ? advice : pawnLimit;
            }
            catch (Exception exception)
            {
                Log.WarningOnce("[Search and Rescue] Pharmacist care lookup failed; using the pawn's vanilla " +
                                "medical-care setting. " + exception.GetBaseException().Message, 196320746);
                return pawnLimit;
            }
        }

        public static bool AllowsMedicine(Pawn patient, ThingDef medicineDef)
        {
            if (medicineDef == null)
            {
                return false;
            }

            if (TryGetChooseYourMedicinePolicy(patient, out List<MedicalCareCategory> categories,
                    out bool detailed))
            {
                if (categories.Count == 0)
                {
                    return false;
                }

                MedicalCareCategory pawnLimit =
                    patient?.playerSettings?.medCare ?? MedicalCareCategory.HerbalOrWorse;
                if (!pawnLimit.AllowsMedicine(medicineDef))
                {
                    return false;
                }

                return detailed
                    ? categories.Contains(MedicineCareCategory(medicineDef))
                    : categories[0].AllowsMedicine(medicineDef);
            }

            return EffectiveMedicalCare(patient).AllowsMedicine(medicineDef);
        }

        public static bool AllowsMedicine(Pawn patient, Thing medicine)
        {
            if (medicine == null || !medicine.def.IsMedicine)
            {
                return false;
            }

            if (TryGetChooseYourMedicinePolicy(patient, out List<MedicalCareCategory> categories,
                    out bool detailed))
            {
                if (categories.Count == 0 ||
                    !(patient?.playerSettings?.medCare ?? MedicalCareCategory.HerbalOrWorse)
                        .AllowsMedicine(medicine.def))
                {
                    return false;
                }

                MedicalCareCategory category = ChooseYourMedicineCategory(medicine);
                return detailed ? categories.Contains(category) : categories[0].AllowsMedicine(medicine.def);
            }

            return EffectiveMedicalCare(patient).AllowsMedicine(medicine.def);
        }

        public static bool AllowsMedicalDevices(Pawn patient)
        {
            if (!RobotMedicalProfile.AllowsBiologicalEmergency(patient)) return false;
            // More Injuries' automatic device WorkGivers consistently reject both NoCare
            // and NoMeds. Preserve that policy even though its devices are not ThingDef
            // medicines and would otherwise bypass AllowsMedicine.
            return EffectiveMedicalCare(patient) > MedicalCareCategory.NoMeds;
        }

        public static double MedicinePreference(Pawn patient, Thing medicine)
        {
            if (medicine == null)
            {
                return double.MinValue;
            }

            double potency = medicine.GetStatValue(StatDefOf.MedicalPotency);
            if (!TryGetChooseYourMedicinePolicy(patient, out List<MedicalCareCategory> categories,
                    out bool detailed) || !detailed)
            {
                return potency;
            }

            int index = categories.IndexOf(ChooseYourMedicineCategory(medicine));
            return index < 0 ? double.MinValue : (categories.Count - index) * 100d + potency;
        }
        internal static ThingDef MoreInjuriesSuctionDevice =>
            DefDatabase<ThingDef>.GetNamedSilentFail("SuctionDevice");
        internal static ThingDef MoreInjuriesDefibrillator =>
            DefDatabase<ThingDef>.GetNamedSilentFail("Defibrillator");
        internal static ThingDef MoreInjuriesEpinephrine =>
            DefDatabase<ThingDef>.GetNamedSilentFail("Epinephrine");
        internal static ThingDef MoreInjuriesTourniquet =>
            DefDatabase<ThingDef>.GetNamedSilentFail("Tourniquet");
        internal static ThingDef MoreInjuriesHemostaticAgent =>
            DefDatabase<ThingDef>.GetNamedSilentFail("HemostaticAgent");
        internal static ThingDef MoreInjuriesBandage =>
            DefDatabase<ThingDef>.GetNamedSilentFail("Bandage");
        internal static ThingDef MoreInjuriesSalineBag =>
            DefDatabase<ThingDef>.GetNamedSilentFail("SalineBag");
        internal static ThingDef MoreInjuriesBloodBag => MoreInjuriesBloodDevice.Value;
        internal static ThingDef HemogenPack => HemogenPackDef;
        public static bool CanPerformCaptureWork(Pawn worker)
        {
            return CombinedFieldAndProviderPriority(
                worker,
                SearchAndRescueDefOf.SAR_CaptureMarked,
                WorkTypeDefOf.Warden) > 0;
        }

        public static bool CanPerformTreatmentWork(Pawn worker)
        {
            return worker != null && !worker.WorkTagIsDisabled(WorkTags.Caring) &&
                   CombinedFieldAndProviderPriority(
                       worker,
                       SearchAndRescueDefOf.SAR_TreatMarked,
                       WorkTypeDefOf.Doctor) > 0;
        }

        public static bool CanPerformSupportiveTreatmentWork(Pawn worker)
        {
            bool nursing = worker != null && !worker.WorkTagIsDisabled(WorkTags.Caring) &&
                           NursingWork != null && NursingTreatmentWorkGiver != null &&
                           CombinedFieldAndProviderPriority(
                               worker,
                               NursingTreatmentWorkGiver,
                               NursingWork) > 0;
            // Nurse Job remains an optional provider specialization. The new Field Rescue
            // work type is the persistent opt-in; its general treatment lane is the fallback.
            return nursing || CanPerformTreatmentWork(worker);
        }

        public static bool CanPerformAnyTreatmentWork(Pawn worker)
        {
            return CanPerformTreatmentWork(worker) || CanPerformSupportiveTreatmentWork(worker);
        }

        public static bool CanPerformFollowupTreatmentWork(Pawn worker)
        {
            return CanPerformMarkedFollowupTreatmentWork(worker) ||
                   CanPerformAutomaticRoutineTreatmentWork(worker);
        }

        public static bool CanPerformMarkedFollowupTreatmentWork(Pawn worker)
        {
            return worker != null && !worker.WorkTagIsDisabled(WorkTags.Caring) &&
                   CombinedFieldAndProviderPriority(
                       worker,
                       SearchAndRescueDefOf.SAR_FollowupTreatMarked,
                       WorkTypeDefOf.Doctor) > 0;
        }

        public static bool CanPerformAutomaticRoutineTreatmentWork(Pawn worker)
        {
            return worker != null && !worker.WorkTagIsDisabled(WorkTags.Caring) &&
                   CombinedFieldAndProviderPriority(
                       worker,
                       SearchAndRescueDefOf.SAR_AutomaticRoutineTreat,
                       WorkTypeDefOf.Doctor) > 0;
        }

        public static int CaptureWorkPriority(Pawn worker)
        {
            return CombinedFieldAndProviderPriority(
                worker,
                SearchAndRescueDefOf.SAR_CaptureMarked,
                WorkTypeDefOf.Warden);
        }

        public static int TreatmentWorkPriority(Pawn worker)
        {
            return CombinedFieldAndProviderPriority(
                worker,
                SearchAndRescueDefOf.SAR_TreatMarked,
                WorkTypeDefOf.Doctor);
        }

        public static int FollowupTreatmentWorkPriority(Pawn worker)
        {
            return CombinedFieldAndProviderPriority(
                worker,
                SearchAndRescueDefOf.SAR_FollowupTreatMarked,
                WorkTypeDefOf.Doctor);
        }

        public static int AutomaticRoutineTreatmentWorkPriority(Pawn worker)
        {
            return CombinedFieldAndProviderPriority(
                worker,
                SearchAndRescueDefOf.SAR_AutomaticRoutineTreat,
                WorkTypeDefOf.Doctor);
        }

        public static int SupportiveTreatmentWorkPriority(Pawn worker)
        {
            if (NursingWork != null && NursingTreatmentWorkGiver != null)
            {
                int priority = CombinedFieldAndProviderPriority(
                    worker,
                    NursingTreatmentWorkGiver,
                    NursingWork);
                if (priority > 0)
                {
                    return priority;
                }
            }

            return TreatmentWorkPriority(worker);
        }

        internal static bool IsSupportiveIntervention(MedicalIntervention intervention)
        {
            return IsTransfusionIntervention(intervention) ||
                   intervention == MedicalIntervention.HemostaticAgent ||
                   intervention == MedicalIntervention.Bandage ||
                   intervention == MedicalIntervention.Tourniquet ||
                   intervention == MedicalIntervention.RemoveTourniquet;
        }

        private static bool IsTransfusionIntervention(MedicalIntervention intervention)
        {
            return intervention == MedicalIntervention.Saline ||
                   intervention == MedicalIntervention.Blood ||
                   intervention == MedicalIntervention.HemogenTransfusion;
        }

        internal static bool CanPerformTreatmentIntervention(Pawn worker, MedicalIntervention intervention)
        {
            if (intervention == MedicalIntervention.Tourniquet &&
                !CanSafelyApplyMoreInjuriesTourniquet(worker))
            {
                return false;
            }

            return IsSupportiveIntervention(intervention)
                ? CanPerformSupportiveTreatmentWork(worker)
                : CanPerformTreatmentWork(worker);
        }

        private static bool CanSafelyApplyMoreInjuriesTourniquet(Pawn worker)
        {
            if (worker?.skills == null)
            {
                return false;
            }

            int requiredSkill = 3;
            if (SlowLearnerTrait != null && worker.story?.traits?.HasTrait(SlowLearnerTrait) == true)
            {
                requiredSkill += 2;
            }

            int medicine = worker.skills.GetSkill(SkillDefOf.Medicine)?.Level ?? 0;
            int intellectual = worker.skills.GetSkill(SkillDefOf.Intellectual)?.Level ?? 0;
            return medicine >= requiredSkill || intellectual >= requiredSkill;
        }

        internal static double TreatmentRoleFitBonus(Pawn worker, MedicalIntervention intervention)
        {
            if (!IsSupportiveIntervention(intervention) || NursingWork == null ||
                NursingTreatmentWorkGiver == null)
            {
                return 0d;
            }

            int nursingPriority = CombinedFieldAndProviderPriority(
                worker,
                NursingTreatmentWorkGiver,
                NursingWork);
            if (nursingPriority <= 0)
            {
                return 0d;
            }

            double bonus = 90000d + (5 - nursingPriority) * 6000d;
            bonus += Math.Min(2d, Math.Max(0.25d,
                worker.GetStatValue(StatDefOf.MedicalTendSpeed))) * 12000d;
            if (CanPerformTreatmentWork(worker))
            {
                // A pawn assigned to both work types may still provide supportive care, but the
                // graph should preserve scarce high-skill doctors for CPR, suction,
                // defibrillation, ordinary tending and other skill-sensitive interventions.
                int medicineSkill = worker.skills?.GetSkill(SkillDefOf.Medicine)?.Level ?? 0;
                bonus -= Math.Max(0, medicineSkill - 4) * 4500d;
            }

            return bonus;
        }

        public static double PredictTreatmentQuality(
            Pawn doctor,
            Pawn patient,
            MedicalIntervention intervention)
        {
            if (intervention == MedicalIntervention.MechRepair) return 1d;
            if (IsSupportiveIntervention(intervention))
            {
                // More Injuries blood/saline, hemostatic agents, bandages and tourniquets, plus
                // supported direct hemogen jobs, apply fixed effects. Skill changes duration,
                // not treatment quality.
                return 1d;
            }

            if (HasFieldTreatableEmergency(patient))
            {
                // More Injuries' CPR/defibrillation success is driven primarily by the raw
                // Medicine skill (roughly level/15 or level/10), not vanilla tend quality.
                int medicineSkill = doctor.skills?.GetSkill(SkillDefOf.Medicine)?.Level ?? 0;
                return Math.Max(0.05d, Math.Min(1.5d, medicineSkill / 12d));
            }

            double quality = doctor.GetStatValue(StatDefOf.MedicalTendQuality);
            if (UsesCombatExtended && CombatExtendedCanStabilize(patient) &&
                doctor?.RaceProps?.IsMechanoid != true)
            {
                // Medicine is selected only for the final matched pairs. CE's reduction is linear
                // in this doctor stat, so it preserves the desired doctor ordering.
                return Math.Max(0.05d, 2d * quality);
            }

            Building_Bed bed = patient.CurrentBed();
            if (bed != null)
            {
                quality += bed.GetStatValue(StatDefOf.MedicalTendQualityOffset);
            }

            if (FirstAidJob != null && !UsesSmartMedicine)
            {
                quality *= 0.75d;
            }

            return Math.Max(0.05d, quality);
        }

        internal static double TransfusionUrgencyBonus(Pawn patient, MedicalIntervention intervention)
        {
            if (!IsTransfusionIntervention(intervention) || patient?.health == null)
            {
                return 0d;
            }

            float bloodLoss = patient.health.hediffSet
                .GetFirstHediffOfDef(HediffDefOf.BloodLoss)?.Severity ?? 0f;
            Hediff shock = patient.health.hediffSet.hediffs.FirstOrDefault(hediff =>
                hediff.def.defName == "HypovolemicShock");
            if (shock?.Severity >= 0.5f)
            {
                // More Injuries' shock can keep progressing after bleeding has stopped and
                // ordinary wound tending cannot reverse the volume deficit. Prefer real blood
                // over saline, while leaving saline as the faster, hemodilution-limited fallback.
                double fluidPreference = intervention == MedicalIntervention.Blood
                    ? 70000d
                    : intervention == MedicalIntervention.HemogenTransfusion ? 50000d : 0d;
                return 520000d + fluidPreference +
                       Math.Min(1d, Math.Max(0d, shock.Severity)) * 180000d;
            }

            if (bloodLoss < 0.15f)
            {
                return 0d;
            }

            // Unlike vanilla bleeding pressure, this remains active after every wound has
            // been bandaged. More Injuries can still kill from the accumulated blood deficit.
            if (bloodLoss >= 0.45f)
            {
                return 260000d + Math.Min(1d, (bloodLoss - 0.45f) / 0.55f) * 220000d;
            }

            return 60000d + (bloodLoss - 0.15f) / 0.30f * 100000d;
        }

        public static Job MakeTreatmentRoundJob(Pawn doctor, Pawn patient)
        {
            return MakeTreatmentRoundJob(doctor, patient, MedicalTreatmentOption.Invalid);
        }

        public static Job MakeTreatmentRoundJob(
            Pawn doctor,
            Pawn patient,
            MedicalTreatmentOption selectedOption)
        {
            if (MechanicalCare.IsPatient(patient)) return MechanicalCare.MakeJob(doctor, patient);
            if (RobotMedicalProfile.OwnsMedicineSelection(patient))
            {
                MedicalTreatmentOption native = RobotMedicalProfile.TreatmentOption(doctor, patient);
                if (!native.IsValid || !CanStartAutomaticTreatmentJob(doctor, patient, JobDefOf.TendPatient)) return null;
                // Resource claims were made against this exact selection in the matching pass.
                if (selectedOption?.IsValid == true && native.Resource != selectedOption.Resource) return null;
                Job nativeJob = JobMaker.MakeJob(JobDefOf.TendPatient, patient, native.Resource);
                if (native.Resource != null && native.Resource.SpawnedParentOrMe != native.Resource)
                    nativeJob.targetC = native.Resource.SpawnedParentOrMe;
                ConfigureTreatmentRoundJob(nativeJob, patient, 1);
                return nativeJob;
            }
            if (selectedOption != null && selectedOption.IsValid)
            {
                Job selectedJob = MakeSelectedTreatmentJob(doctor, patient, selectedOption);
                if (selectedJob == null || !CanStartAutomaticTreatmentJob(doctor, patient, selectedJob))
                {
                    return null;
                }
                ConfigureTreatmentRoundJob(selectedJob, patient, Math.Max(1, selectedOption.Count));
                if (selectedOption.Intervention == MedicalIntervention.Saline ||
                    selectedOption.Intervention == MedicalIntervention.Blood ||
                    selectedOption.Intervention == MedicalIntervention.HemogenTransfusion)
                {
                    selectedJob.count = 1;
                }
                return selectedJob;
            }

            JobDef moreInjuriesJob = MoreInjuriesTreatmentJobFor(patient);
            if (moreInjuriesJob != null)
            {
                Job firstAid = JobMaker.MakeJob(moreInjuriesJob, patient);
                firstAid.count = 1;
                firstAid.playerForced = false;
                return firstAid;
            }

            if (UsesCombatExtended && CombatExtendedCanStabilize(patient) &&
                doctor?.RaceProps?.IsMechanoid != true)
            {
                Thing ceMedicine = FindCombatExtendedMedicine(doctor, patient);
                if (ceMedicine == null)
                {
                    return null;
                }

                Job stabilize = JobMaker.MakeJob(StabilizeJob, patient, ceMedicine);
                stabilize.count = 1;
                return stabilize;
            }

            JobDef treatmentJob = UsesSmartMedicine
                ? JobDefOf.TendPatient
                : (!UsesCombatExtended ? FirstAidJob : null) ?? JobDefOf.TendPatient;
            Job job;
            if (UsesSmartMedicine && TryMakeSmartMedicineJob(doctor, patient, out Job smartMedicineJob))
            {
                job = smartMedicineJob;
            }
            else
            {
                Thing medicine = HealthAIUtility.FindBestMedicine(doctor, patient);
                job = JobMaker.MakeJob(treatmentJob, patient, medicine);
                job.count = 1;
            }

            if (!CanStartAutomaticTreatmentJob(doctor, patient, job))
            {
                return null;
            }
            ConfigureTreatmentRoundJob(job, patient, 1);
            return job;
        }

        private static bool CanStartAutomaticTreatmentJob(Pawn doctor, Pawn patient, Job job)
        {
            return CanStartAutomaticTreatmentJob(doctor, patient, job?.def);
        }

        private static bool CanStartAutomaticTreatmentJob(Pawn doctor, Pawn patient, JobDef jobDef)
        {
            if (jobDef != JobDefOf.TendPatient || doctor?.Faction != Faction.OfPlayer)
            {
                return true;
            }

            // TendPatient normally limits player doctors to pawns covered by automatic
            // medical care. A Search and Rescue treatment designation is an explicit player
            // order, so it must also authorize field care for a neutral/wild animal. The
            // generated job is marked forced below solely when this override is required;
            // hostile animals remain excluded by TargetEligibility.
            return patient != null &&
                   (HealthAIUtility.ShouldBeTendedNowByPlayer(patient) || HasExplicitFieldCareOrder(patient));
        }

        private static bool HasExplicitFieldCareOrder(Pawn patient)
        {
            return patient?.Spawned == true && TargetEligibility.CanReceiveFieldCare(patient) &&
                   patient.Map.designationManager.DesignationOn(patient, SearchAndRescueDefOf.SAR_Treat) != null &&
                   patient.health.HasHediffsNeedingTend();
        }

        private static void ConfigureTreatmentRoundJob(Job job, Pawn patient, int count)
        {
            job.count = Math.Max(1, count);
            // JobDriver_TendPatient uses playerForced as its built-in escape hatch from
            // ShouldBeTendedNowByPlayer. Keep ordinary managed care automatic, and use the
            // hatch only for an explicitly marked patient that vanilla would otherwise skip.
            job.playerForced = job.def == JobDefOf.TendPatient && HasExplicitFieldCareOrder(patient) &&
                               !HealthAIUtility.ShouldBeTendedNowByPlayer(patient);
            // SAR field care is an explicit order from the ordinary work graph. Vanilla's
            // drafted-tend path temporarily holds a standing drafted patient still and resumes
            // their prior job afterwards; without it they can walk out of a partially filled bar.
            job.draftedTend = job.def == JobDefOf.TendPatient;
            if (job.def == JobDefOf.TendPatient)
            {
                // JobDriver_TendPatient checks this only after FinalizeTend. It therefore
                // gives us exactly one completed wound treatment without polling health
                // values and interrupting a partially filled progress bar.
                job.endAfterTendedOnce = true;
            }
        }

        public static bool RequiresTreatmentEffectMonitor(Job job)
        {
            if (job?.def == null || job.def == JobDefOf.TendPatient)
            {
                return false;
            }

            string defName = job.def.defName;
            // CE Stabilize is deliberately atomic here. Its driver loops quickly across
            // eligible bleeding wounds but consumes one whole medicine in FinishAction;
            // interrupting after each wound would turn one stabilization pass into N stacks.
            return defName == "CP_FirstAid" || defName == "ProvideFirstAid";
        }

        internal static IReadOnlyList<MedicalTreatmentOption> FindTreatmentOptions(
            Pawn doctor,
            Pawn patient,
            MedicalCarePlan plan,
            MedicalResourceLedger ledger)
        {
            if (doctor == null || patient == null || plan == null || ledger == null)
            {
                return Array.Empty<MedicalTreatmentOption>();
            }

            List<MedicalTreatmentOption> options = new List<MedicalTreatmentOption>();
            if (RobotMedicalProfile.OwnsMedicineSelection(patient))
            {
                MedicalTreatmentOption native = RobotMedicalProfile.TreatmentOption(doctor, patient);
                if (native.IsValid) options.Add(native);
                return options;
            }
            if (MechanicalCare.IsPatient(patient))
            {
                if (MechanicalCare.CanRepair(doctor, patient))
                    options.Add(new MedicalTreatmentOption(MedicalIntervention.MechRepair, null, 0,
                        false, false, 1d, doctor.Position.DistanceTo(patient.Position)));
                return options;
            }
            bool ceStabilizeAvailable = UsesCombatExtended &&
                                        CombatExtendedCanStabilize(patient) &&
                                        doctor.RaceProps?.IsMechanoid != true;
            bool vanillaTendAvailable = CanStartAutomaticTreatmentJob(
                doctor,
                patient,
                JobDefOf.TendPatient);
            foreach (MedicalResourceDemand demand in plan.Demands
                         .Where(demand => demand.ResourceDef != null)
                         .Where(demand => CanPerformTreatmentIntervention(doctor, demand.Intervention))
                         .OrderByDescending(demand => demand.Essential)
                         .ThenByDescending(demand => demand.Benefit))
            {
                Thing resource = ledger.FindBest(
                    doctor,
                    patient,
                    demand.ResourceDef,
                    demand.Reusable,
                    1,
                    allowPatientInventory:
                        demand.Intervention == MedicalIntervention.HemogenTransfusion &&
                        UsesEmergencyTransfusions);
                if (resource == null)
                {
                    continue;
                }

                int available = ledger.AvailableForTreatment(resource, doctor, patient);
                int plannedCount = demand.Reusable
                    ? 1
                    : Math.Max(1, Math.Min(demand.Count, available));

                bool inInventory = doctor.inventory?.innerContainer.Contains(resource) == true ||
                                   demand.Intervention == MedicalIntervention.HemogenTransfusion &&
                                   UsesEmergencyTransfusions &&
                                   patient.inventory?.innerContainer.Contains(resource) == true;
                double routeDistance = inInventory
                    ? Math.Sqrt(doctor.Position.DistanceToSquared(patient.Position))
                    : Math.Sqrt(doctor.Position.DistanceToSquared(resource.PositionHeld)) +
                      Math.Sqrt(resource.PositionHeld.DistanceToSquared(patient.Position));
                options.Add(new MedicalTreatmentOption(
                    demand.Intervention,
                    resource,
                    plannedCount,
                    inInventory,
                    demand.Reusable,
                    demand.Benefit,
                    routeDistance));
            }

            Hediff removableTourniquet = MoreInjuriesTourniquetForRemoval(patient);
            if (removableTourniquet != null &&
                CanPerformTreatmentIntervention(doctor, MedicalIntervention.RemoveTourniquet))
            {
                // Removing a tourniquet is a device-free, fixed-effect nursing procedure.
                // Keep it inside the same option graph so the worker claim, interruption and
                // completion notification rules are identical to every other intervention.
                options.Add(new MedicalTreatmentOption(
                    MedicalIntervention.RemoveTourniquet,
                    null,
                    1,
                    false,
                    false,
                    3.2d + removableTourniquet.Severity * 2d,
                    Math.Sqrt(doctor.Position.DistanceToSquared(patient.Position))));
            }

            // Medicine is a substitutable resource class rather than a fixed ThingDef. Prefer
            // inventory first, then potency, while respecting the patient's medical-care policy.
            // Patient-referenced field stacks remain additional candidates: otherwise a nearby
            // herbal delivery could be hidden by a more potent stack back at the hospital, and
            // the doctor would continue dry tending despite the completed supply run.
            if (plan.EssentialMedicineRounds > 0 && CanPerformTreatmentWork(doctor))
            {
                IEnumerable<Thing> availableMedicines = ledger.AvailableMedicines(doctor, patient)
                    .Where(thing => !ceStabilizeAvailable ||
                                    CombatExtendedCanCollectMedicineDirectly(
                                        doctor,
                                        patient,
                                        thing));
                Thing medicine = UsesChooseYourMedicine
                    ? HealthAIUtility.FindBestMedicine(doctor, patient)
                    : availableMedicines
                        .OrderByDescending(thing => doctor.inventory?.innerContainer.Contains(thing) == true)
                        .ThenByDescending(thing => MedicinePreference(patient, thing))
                        .ThenBy(thing => doctor.inventory?.innerContainer.Contains(thing) == true
                            ? 0
                            : doctor.Position.DistanceToSquared(thing.PositionHeld) +
                              thing.PositionHeld.DistanceToSquared(patient.Position))
                        .FirstOrDefault();
                if (medicine != null &&
                    (ledger.AvailableForTreatment(medicine, doctor, patient) <= 0 ||
                     ceStabilizeAvailable &&
                     !CombatExtendedCanCollectMedicineDirectly(doctor, patient, medicine)))
                {
                    medicine = null;
                }
                if (medicine == null && UsesChooseYourMedicine)
                {
                    // Its first choice may already be soft-claimed by another Search and Rescue
                    // worker. Preserve the configured category order while selecting the next
                    // ledger-available candidate instead of falling straight to dry tending.
                    medicine = availableMedicines
                        .OrderByDescending(thing => MedicinePreference(patient, thing))
                        .ThenBy(thing => doctor.inventory?.innerContainer.Contains(thing) == true
                            ? 0
                            : doctor.Position.DistanceToSquared(thing.PositionHeld) +
                              thing.PositionHeld.DistanceToSquared(patient.Position))
                        .FirstOrDefault();
                }
                IEnumerable<Thing> medicineCandidates = (medicine != null
                        ? new[] { medicine }
                        : Enumerable.Empty<Thing>())
                    .Concat(ledger.AvailableFieldSupplies(doctor, patient)
                        .Where(thing => thing.def.IsMedicine && AllowsMedicine(patient, thing)))
                    .Distinct();
                foreach (Thing candidateMedicine in medicineCandidates)
                {
                    int availableMedicine = Math.Min(
                        candidateMedicine.stackCount,
                        ledger.AvailableForTreatment(candidateMedicine, doctor, patient));
                    int medicineRoundBudget = Math.Max(
                        1,
                        Math.Min(plan.EssentialMedicineRounds, availableMedicine));
                    bool locallyHeld = doctor.inventory?.innerContainer.Contains(candidateMedicine) == true ||
                                       doctor.carryTracker?.CarriedThing == candidateMedicine ||
                                       patient.inventory?.innerContainer.Contains(candidateMedicine) == true;
                    double routeDistance = locallyHeld
                        ? Math.Sqrt(doctor.Position.DistanceToSquared(patient.Position))
                        : Math.Sqrt(doctor.Position.DistanceToSquared(candidateMedicine.PositionHeld)) +
                          Math.Sqrt(candidateMedicine.PositionHeld.DistanceToSquared(patient.Position));
                    MedicalIntervention intervention = ceStabilizeAvailable
                        ? MedicalIntervention.CombatExtendedStabilize
                        : FirstAidJob != null && !UsesSmartMedicine && !UsesCombatExtended
                            ? MedicalIntervention.Rh2FirstAid
                            : MedicalIntervention.VanillaTend;
                    if (intervention == MedicalIntervention.VanillaTend && !vanillaTendAvailable)
                    {
                        continue;
                    }
                    options.Add(new MedicalTreatmentOption(
                        intervention,
                        candidateMedicine,
                        medicineRoundBudget,
                        locallyHeld,
                        false,
                        1.0d + candidateMedicine.GetStatValue(StatDefOf.MedicalPotency) * 0.25d,
                        routeDistance));
                }
            }

            // CPR is the equipment-free fallback for choking and cardiac arrest. It remains
            // deliberately less valuable than a suitable device, so scarce equipment can be
            // priced and routed without making the patient untreatable when none is available.
            if (CanPerformTreatmentWork(doctor) && UsesMoreInjuries && RobotMedicalProfile.AllowsBiologicalEmergency(patient) &&
                IsMedicalInterventionUnlocked(MedicalIntervention.Cpr) &&
                patient.health.hediffSet.hediffs.Any(hediff =>
                    hediff.def.defName == "ChokingOnBlood" || hediff.def.defName == "CardiacArrest"))
            {
                options.Add(new MedicalTreatmentOption(
                    MedicalIntervention.Cpr,
                    null,
                    1,
                    false,
                    false,
                    1.0d,
                    Math.Sqrt(doctor.Position.DistanceToSquared(patient.Position))));
            }

            // Dry first aid is a real alternative, not merely a last resort after every map
            // medicine stack disappears. The coordinator compares this direct route with the
            // quality/medical benefit of every equipment detour. CE stabilization is the one
            // exception because its job cannot run without medicine.
            if (CanPerformTreatmentWork(doctor) && patient.health.HasHediffsNeedingTend() &&
                !ceStabilizeAvailable)
            {
                MedicalIntervention fallback = FirstAidJob != null && !UsesSmartMedicine && !UsesCombatExtended
                        ? MedicalIntervention.Rh2FirstAid
                        : MedicalIntervention.VanillaTend;
                if (fallback != MedicalIntervention.VanillaTend || vanillaTendAvailable)
                {
                    options.Add(new MedicalTreatmentOption(
                        fallback,
                        null,
                        1,
                        false,
                        false,
                        0.45d,
                        Math.Sqrt(doctor.Position.DistanceToSquared(patient.Position))));
                }
            }

            return options;
        }

        internal static bool IsMedicalInterventionUnlocked(MedicalIntervention intervention)
        {
            string researchDefName;
            switch (intervention)
            {
                case MedicalIntervention.Bandage:
                    researchDefName = "BasicAnatomy";
                    break;
                case MedicalIntervention.Tourniquet:
                case MedicalIntervention.RemoveTourniquet:
                case MedicalIntervention.Blood:
                    researchDefName = "BasicFirstAid";
                    break;
                case MedicalIntervention.Cpr:
                    researchDefName = "Cpr";
                    break;
                case MedicalIntervention.HemostaticAgent:
                    researchDefName = "AdvancedFirstAid";
                    break;
                case MedicalIntervention.Suction:
                case MedicalIntervention.Defibrillate:
                case MedicalIntervention.Saline:
                    researchDefName = "EmergencyMedicine";
                    break;
                case MedicalIntervention.Epinephrine:
                    researchDefName = "EpinephrineSynthesis";
                    break;
                default:
                    return true;
            }

            ResearchProjectDef research = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(researchDefName);
            return (research == null || research.IsFinished) &&
                   (intervention != MedicalIntervention.Epinephrine ||
                    MoreInjuriesSettingEnabled("EnableAdrenaline"));
        }

        internal static bool MoreInjuriesCanUseHemostasis(Hediff hediff)
        {
            if (hediff == null)
            {
                return false;
            }

            try
            {
                return MoreInjuriesHemostasisCanTreat.Value?.Invoke(hediff) == true;
            }
            catch
            {
                return false;
            }
        }

        internal static BodyPartRecord MoreInjuriesTourniquetLimbFor(Hediff hediff)
        {
            if (hediff == null || !hediff.TendableNow()) return null;
            // Mirror More Injuries' BleedRateByLimbEnumerable: a tourniquet is anchored at
            // the shoulder or leg containing the wound, not at an arbitrary outside part
            // such as the torso, head, hand, or foot. Deliberately omit its low-skill neck
            // accident easter egg from automatic Search and Rescue care.
            for (BodyPartRecord part = hediff?.Part; part != null; part = part.parent)
            {
                if (part.def == BodyPartDefOf.Shoulder || part.def == BodyPartDefOf.Leg)
                {
                    // The native driver succeeds immediately when this limb already has
                    // a tourniquet. Residual bleeding still needs ordinary wound care.
                    return hediff.pawn.health.hediffSet.hediffs.Any(existing =>
                        existing.def.defName == "TourniquetApplied" && existing.Part == part)
                        ? null : part;
                }
            }

            return null;
        }

        internal static int MoreInjuriesRequiredTransfusions(
            Pawn patient,
            MedicalIntervention intervention,
            bool fullyHeal = false)
        {
            if (patient == null ||
                (intervention != MedicalIntervention.Saline && intervention != MedicalIntervention.Blood))
            {
                return 0;
            }

            string typeName = intervention == MedicalIntervention.Saline
                ? "MoreInjuries.HealthConditions.HeavyBleeding.Transfusions.JobDriver_UseSalineBag"
                : "MoreInjuries.HealthConditions.HeavyBleeding.Transfusions.JobDriver_UseBloodBag";
            try
            {
                MethodInfo method = intervention == MedicalIntervention.Saline
                    ? MoreInjuriesSalineCountMethod.Value
                    : MoreInjuriesBloodCountMethod.Value;
                if (method == null)
                {
                    Log.WarningOnce("[Search and Rescue] More Injuries transfusion demand API was not found for " +
                                    typeName + ".",
                        196320748 + typeName.GetHashCode());
                    return 0;
                }
                return method.Invoke(null, new object[] { patient, fullyHeal }) is int count
                    ? Math.Max(0, count)
                    : 0;
            }
            catch (Exception exception)
            {
                Log.WarningOnce("[Search and Rescue] Could not query More Injuries transfusion demand. " +
                                exception.GetBaseException().Message,
                    196320748 + typeName.GetHashCode());
                return 0;
            }
        }

        internal static bool HasModerateMoreInjuriesHypovolemicShock(Pawn patient)
        {
            return UsesMoreInjuries && patient?.health?.hediffSet?.hediffs.Any(hediff =>
                hediff.def.defName == "HypovolemicShock" && hediff.Severity >= 0.5f) == true;
        }

        internal static int MoreInjuriesPlannedTransfusions(
            Pawn patient,
            MedicalIntervention intervention)
        {
            int stabilizationCount = MoreInjuriesRequiredTransfusions(patient, intervention);
            if (stabilizationCount > 0)
            {
                return stabilizationCount;
            }

            float bloodLoss = patient?.health?.hediffSet
                .GetFirstHediffOfDef(HediffDefOf.BloodLoss)?.Severity ?? 0f;
            if (!HasModerateMoreInjuriesHypovolemicShock(patient) || bloodLoss <= 0.30f)
            {
                return 0;
            }

            // A moderate shock below MI's normal severe-blood-loss cutoff gets one bounded
            // resuscitation dose. BloodLoss then falls below 0.30 (for saline) or much lower
            // (for whole blood), so a persistent/recovering shock Hediff cannot queue a loop.
            return MoreInjuriesRequiredTransfusions(patient, intervention, fullyHeal: true) > 0 ? 1 : 0;
        }

        private static Job MakeSelectedTreatmentJob(
            Pawn doctor,
            Pawn patient,
            MedicalTreatmentOption option)
        {
            switch (option.Intervention)
            {
                case MedicalIntervention.Cpr:
                    return MoreInjuriesCprJob == null ? null : JobMaker.MakeJob(MoreInjuriesCprJob, patient);
                case MedicalIntervention.Suction:
                    return MakeMoreInjuriesOneShotJob(
                        MoreInjuriesSuctionJob, doctor, patient, option.Resource, option.FromInventory);
                case MedicalIntervention.Defibrillate:
                    return MakeMoreInjuriesOneShotJob(
                        MoreInjuriesDefibrillatorJob, doctor, patient, option.Resource, option.FromInventory);
                case MedicalIntervention.Epinephrine:
                    return MakeMoreInjuriesOneShotJob(
                        MoreInjuriesEpinephrineJob, doctor, patient, option.Resource, option.FromInventory);
                case MedicalIntervention.HemostaticAgent:
                    return MakeMoreInjuriesOneShotJob(
                        MoreInjuriesHemostaticJob, doctor, patient, option.Resource, option.FromInventory);
                case MedicalIntervention.Bandage:
                    return MakeMoreInjuriesOneShotJob(
                        MoreInjuriesBandageJob, doctor, patient, option.Resource, option.FromInventory);
                case MedicalIntervention.Tourniquet:
                    return TryMakeTourniquetJob(doctor, patient, option.Resource);
                case MedicalIntervention.RemoveTourniquet:
                    {
                        BodyPartRecord part = MoreInjuriesTourniquetForRemoval(patient)?.Part;
                        return MoreInjuriesRemoveTourniquetJob == null || part == null
                            ? null
                            : TryCreateMoreInjuriesDispatcherJob(
                                "MoreInjuries.HealthConditions.HeavyBleeding.Tourniquets." +
                                "JobDriver_RemoveTourniquetSafely",
                                doctor,
                                patient,
                                part);
                    }
                case MedicalIntervention.Saline:
                    {
                        Type modeType = FindLoadedType(
                            "MoreInjuries.HealthConditions.HeavyBleeding.Transfusions.SalineTransfusionMode");
                        bool shockDose = MoreInjuriesRequiredTransfusions(
                            patient,
                            MedicalIntervention.Saline) == 0 &&
                            MoreInjuriesPlannedTransfusions(patient, MedicalIntervention.Saline) > 0;
                        object transfusionMode = modeType == null
                            ? null
                            : Enum.Parse(modeType, shockDose ? "ForceTransfusion" : "Stabilize");
                        Job salineJob = transfusionMode == null
                            ? null
                            : TryCreateMoreInjuriesDispatcherJob(
                                "MoreInjuries.HealthConditions.HeavyBleeding.Transfusions.JobDriver_UseSalineBag",
                                doctor,
                                patient,
                                option.Resource,
                                option.FromInventory,
                                transfusionMode);
                        SetMoreInjuriesOneShot(salineJob);
                        return salineJob;
                    }
                case MedicalIntervention.Blood:
                    {
                        bool shockDose = MoreInjuriesRequiredTransfusions(
                            patient,
                            MedicalIntervention.Blood) == 0 &&
                            MoreInjuriesPlannedTransfusions(patient, MedicalIntervention.Blood) > 0;
                        Job bloodJob = TryCreateMoreInjuriesDispatcherJob(
                            "MoreInjuries.HealthConditions.HeavyBleeding.Transfusions.JobDriver_UseBloodBag",
                            doctor,
                            patient,
                            option.Resource,
                            option.FromInventory,
                            shockDose);
                        SetMoreInjuriesOneShot(bloodJob);
                        return bloodJob;
                    }
                case MedicalIntervention.HemogenTransfusion:
                    {
                        if (option.Resource == null)
                        {
                            return null;
                        }

                        if (EmergencyTransfusionJob != null)
                        {
                            // Emergency Transfusions natively understands ground, doctor,
                            // patient and pack-animal inventories. Keep the intervention at one
                            // pack so claim -> complete -> notify -> rematch stays deterministic.
                            Job emergencyJob = JobMaker.MakeJob(EmergencyTransfusionJob, patient);
                            emergencyJob.targetQueueB = new List<LocalTargetInfo> { option.Resource };
                            emergencyJob.countQueue = new List<int> { 1 };
                            emergencyJob.count = 1;
                            return emergencyJob;
                        }

                        if (HemogenDirectJob == null)
                        {
                            return null;
                        }
                        Job hemogenJob = JobMaker.MakeJob(HemogenDirectJob, option.Resource, patient);
                        hemogenJob.count = 1;
                        return hemogenJob;
                    }
                case MedicalIntervention.CombatExtendedStabilize:
                    return StabilizeJob == null || option.Resource == null
                        ? null
                        : JobMaker.MakeJob(StabilizeJob, patient, option.Resource);
                case MedicalIntervention.Rh2FirstAid:
                    return JobMaker.MakeJob(FirstAidJob ?? JobDefOf.TendPatient, patient, option.Resource);
                case MedicalIntervention.VanillaTend:
                    // Smart Medicine already influenced option.Resource through its patched
                    // selection. Re-running its search here could select a stack referenced
                    // by a different casualty after our ledger claim was made.
                    return JobMaker.MakeJob(JobDefOf.TendPatient, patient, option.Resource);
                default:
                    return null;
            }
        }

        private static Job TryMakeTourniquetJob(Pawn doctor, Pawn patient, Thing tourniquet)
        {
            if (MoreInjuriesTourniquetJob == null || tourniquet == null)
            {
                return null;
            }

            BodyPartRecord part = patient.health.hediffSet.hediffs
                .Where(hediff => !hediff.IsTended() && hediff.BleedRate > 0f)
                .Select(hediff => new
                {
                    Hediff = hediff,
                    Limb = MoreInjuriesTourniquetLimbFor(hediff)
                })
                .Where(candidate => candidate.Limb != null)
                .GroupBy(candidate => candidate.Limb)
                .OrderByDescending(group => group.Sum(candidate => candidate.Hediff.BleedRate))
                .Select(group => group.Key)
                .FirstOrDefault();
            if (part == null)
            {
                return null;
            }

            try
            {
                Type driverType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(assembly => assembly.GetType(
                        "MoreInjuries.HealthConditions.HeavyBleeding.Tourniquets.JobDriver_UseTourniquet",
                        false))
                    .FirstOrDefault(type => type != null);
                MethodInfo dispatcherMethod = driverType?.GetMethod(
                    "GetDispatcher",
                    BindingFlags.Public | BindingFlags.Static);
                object dispatcher = dispatcherMethod?.Invoke(null, new object[] { doctor, patient, tourniquet, part });
                return dispatcher?.GetType().GetMethod("CreateJob", BindingFlags.Public | BindingFlags.Instance)
                    ?.Invoke(dispatcher, Array.Empty<object>()) as Job;
            }
            catch (Exception exception)
            {
                Log.WarningOnce("[Search and Rescue] More Injuries tourniquet job construction failed; " +
                                "falling back to another treatment option. " +
                                exception.GetBaseException().Message, 196320745);
                return null;
            }
        }

        private static void SetMoreInjuriesOneShot(Job job)
        {
            if (job?.source == null)
            {
                return;
            }

            try
            {
                FieldInfo oneShot = job.source.GetType().GetField(
                    "oneShot",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                oneShot?.SetValue(job.source, true);
                // The native driver uses this flag to reserve and consume one device, then
                // finish. That restores the scheduler's one-intervention/notify/rematch
                // contract instead of silently consuming an entire stack behind one claim.
                job.count = 1;
            }
            catch (Exception exception)
            {
                Log.WarningOnce("[Search and Rescue] Could not make More Injuries transfusion one-shot. " +
                                exception.GetBaseException().Message,
                    196320750 + job.def.shortHash);
            }
        }

        private static Job MakeMoreInjuriesOneShotJob(
            JobDef jobDef,
            Pawn doctor,
            Pawn patient,
            Thing resource,
            bool fromInventory)
        {
            if (jobDef == null || resource == null)
            {
                return null;
            }

            Job job = JobMaker.MakeJob(jobDef, patient, resource);
            try
            {
                Type parametersType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(assembly => assembly.GetType(
                        "MoreInjuries.AI.Jobs.JobDriver_UseMedicalDevice+ExtendedJobParameters",
                        false))
                    .FirstOrDefault(type => type != null);
                MethodInfo createMethod = parametersType?.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(method => method.Name == "Create" && method.IsGenericMethodDefinition);
                object parameters = createMethod?.MakeGenericMethod(parametersType)
                    .Invoke(null, new object[] { doctor, fromInventory, true });
                if (parameters is ILoadReferenceable loadReferenceable)
                {
                    // More Injuries' factory also registers the parameter object in the pawn
                    // comp. That makes the job source survive a save made mid-intervention.
                    job.source = loadReferenceable;
                }
            }
            catch (Exception exception)
            {
                // The coordinator still interrupts after the first observed treatment effect;
                // this only loses the failed-attempt one-shot guarantee on an incompatible MI build.
                Log.WarningOnce("[Search and Rescue] Could not configure a More Injuries one-shot job. " +
                                exception.GetBaseException().Message, 196320746);
            }
            return job;
        }

        private static Type FindLoadedType(string fullName)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(fullName, false))
                .FirstOrDefault(type => type != null);
        }

        private static MethodInfo FindMoreInjuriesTransfusionCountMethod(string typeName)
        {
            return FindLoadedType(typeName)?.GetMethod(
                "JobGetMedicalDeviceCountToFullyHeal",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(Pawn), typeof(bool) },
                null);
        }

        private static bool MoreInjuriesSettingEnabled(string propertyName)
        {
            try
            {
                object settings = FindLoadedType("MoreInjuries.MoreInjuriesMod")
                    ?.GetProperty("Settings", BindingFlags.Public | BindingFlags.Static)
                    ?.GetValue(null);
                PropertyInfo property = settings?.GetType().GetProperty(
                    propertyName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                return property?.GetValue(settings) is bool enabled ? enabled : true;
            }
            catch
            {
                return true;
            }
        }

        private static ThingDef ResolveMoreInjuriesBloodDevice()
        {
            try
            {
                Type driverType = FindLoadedType(
                    "MoreInjuries.HealthConditions.HeavyBleeding.Transfusions.JobDriver_UseBloodBag");
                ThingDef nativeDevice = driverType?.GetProperty(
                        "JobDeviceDef",
                        BindingFlags.Public | BindingFlags.Static)
                    ?.GetValue(null) as ThingDef;
                return nativeDevice ?? DefDatabase<ThingDef>.GetNamedSilentFail("WholeBloodBag");
            }
            catch
            {
                return DefDatabase<ThingDef>.GetNamedSilentFail("WholeBloodBag");
            }
        }

        private static Job TryCreateMoreInjuriesDispatcherJob(string driverTypeName, params object[] arguments)
        {
            if (arguments.Any(argument => argument == null))
            {
                return null;
            }

            try
            {
                Type driverType = FindLoadedType(driverTypeName);
                MethodInfo dispatcherMethod = driverType?.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(method => method.Name == "GetDispatcher" &&
                                              method.GetParameters().Length == arguments.Length);
                object dispatcher = dispatcherMethod?.Invoke(null, arguments);
                return dispatcher?.GetType().GetMethod("CreateJob", BindingFlags.Public | BindingFlags.Instance)
                    ?.Invoke(dispatcher, Array.Empty<object>()) as Job;
            }
            catch (Exception exception)
            {
                Log.WarningOnce("[Search and Rescue] More Injuries dispatcher job construction failed for " +
                                driverTypeName + ". " + exception.GetBaseException().Message,
                    196320747 + driverTypeName.GetHashCode());
                return null;
            }
        }

        public static bool CanPerformRescueWork(Pawn worker)
        {
            return RescueProviderFor(worker) != RescueWorkProvider.None;
        }

        public static bool CanPerformSupplyWork(Pawn worker)
        {
            return CombinedFieldAndProviderPriority(
                worker,
                SearchAndRescueDefOf.SAR_RescueMarkedHauling,
                WorkTypeDefOf.Hauling) > 0;
        }

        public static int SupplyWorkPriority(Pawn worker)
        {
            return CombinedFieldAndProviderPriority(
                worker,
                SearchAndRescueDefOf.SAR_RescueMarkedHauling,
                WorkTypeDefOf.Hauling);
        }

        public static RescueWorkProvider RescueProviderFor(Pawn worker)
        {
            if (IsTrainedRescueAnimal(worker) && !HardworkingCompatibility.IsWorker(worker))
            {
                return RescueWorkProvider.Animal;
            }

            if (IsColonyWorkMech(worker))
            {
                if (worker.RaceProps.mechEnabledWorkTypes?.Contains(WorkTypeDefOf.Hauling) == true &&
                    CombinedFieldAndProviderPriority(
                        worker,
                        SearchAndRescueDefOf.SAR_RescueMarkedHauling,
                        WorkTypeDefOf.Hauling) > 0)
                {
                    return RescueWorkProvider.Hauling;
                }

                return worker.RaceProps.mechEnabledWorkTypes?.Contains(WorkTypeDefOf.Doctor) == true &&
                       CombinedFieldAndProviderPriority(
                           worker,
                           ParamedicRescueWorkGiver,
                           WorkTypeDefOf.Doctor) > 0
                    ? RescueWorkProvider.Paramedic
                    : RescueWorkProvider.None;
            }

            if (worker.workSettings == null)
            {
                return RescueWorkProvider.None;
            }

            RescueWorkMode mode = SearchAndRescueMod.Settings?.RescueWorkMode ?? RescueWorkMode.Hauling;
            bool hauling = CombinedFieldAndProviderPriority(
                worker,
                SearchAndRescueDefOf.SAR_RescueMarkedHauling,
                WorkTypeDefOf.Hauling) > 0;
            bool nursing = NursingWork != null && NursingRescueWorkGiver != null &&
                           CombinedFieldAndProviderPriority(
                               worker,
                               NursingRescueWorkGiver,
                               NursingWork) > 0;
            if (mode == RescueWorkMode.NursingOnly)
            {
                return nursing ? RescueWorkProvider.Nursing : RescueWorkProvider.None;
            }

            if (mode == RescueWorkMode.NursingPreferred && nursing)
            {
                return RescueWorkProvider.Nursing;
            }

            return hauling ? RescueWorkProvider.Hauling : RescueWorkProvider.None;
        }

        public static int RescueWorkPriority(Pawn worker)
        {
            RescueWorkProvider provider = RescueProviderFor(worker);
            if (provider == RescueWorkProvider.Animal)
            {
                return 3;
            }

            if (provider == RescueWorkProvider.Nursing)
            {
                return CombinedFieldAndProviderPriority(
                    worker,
                    NursingRescueWorkGiver,
                    NursingWork);
            }

            if (provider == RescueWorkProvider.Paramedic)
            {
                return CombinedFieldAndProviderPriority(
                    worker,
                    ParamedicRescueWorkGiver,
                    WorkTypeDefOf.Doctor);
            }

            return CombinedFieldAndProviderPriority(
                worker,
                SearchAndRescueDefOf.SAR_RescueMarkedHauling,
                WorkTypeDefOf.Hauling);
        }

        public static double RescueWorkPreferenceBonus(Pawn worker)
        {
            RescueWorkMode mode = SearchAndRescueMod.Settings?.RescueWorkMode ?? RescueWorkMode.Hauling;
            return mode == RescueWorkMode.NursingPreferred && NursingWork != null &&
                   NursingRescueWorkGiver != null &&
                   CombinedFieldAndProviderPriority(
                       worker,
                       NursingRescueWorkGiver,
                       NursingWork) > 0
                ? 100000d
                : 0d;
        }

        public static bool IsColonyWorkMech(Pawn worker)
        {
            return ModsConfig.BiotechActive && worker != null && worker.IsColonyMechPlayerControlled &&
                   worker.RaceProps.IsWorkMech;
        }

        public static bool IsTrainedRescueAnimal(Pawn worker)
        {
            return worker != null && worker.Faction == Faction.OfPlayer && worker.RaceProps.Animal &&
                   RescueTraining != null && worker.training?.HasLearned(RescueTraining) == true;
        }

        public static bool CanCarryRescueTarget(Pawn worker, Pawn patient)
        {
            if (worker == null || patient == null || worker == patient || worker.carryTracker == null ||
                worker.carryTracker.CarriedThing != null)
            {
                return false;
            }

            // Vanilla pawn rescue inserts the whole pawn directly into this one-slot carry
            // tracker; item/caravan mass limits do not apply. In particular, trained rescue
            // animals need not be pack animals. Checking the real tracker state preserves
            // that behavior and avoids assigning a worker whose hands are already occupied.
            return true;
        }

        public static Building_Bed FindBestRescueBed(Pawn patient, Pawn rescuer)
        {
            GuestStatus? status = patient.IsPrisonerOfColony ? GuestStatus.Prisoner : patient.GuestStatus;
            if (FindBestPatientBedMethod != null && PatientTransferCompType != null)
            {
                try
                {
                    ThingComp transferComp = patient.AllComps?.FirstOrDefault(comp => PatientTransferCompType.IsInstanceOfType(comp));
                    Building_Bed preferred = transferComp == null
                        ? null
                        : FindBestPatientBedMethod.Invoke(transferComp, new object[] { patient }) as Building_Bed;
                    if (preferred != null && preferred.Spawned && !preferred.Destroyed && preferred.Medical &&
                        !IsTemporaryFieldTendBed(preferred) &&
                        RestUtility.IsValidBedFor(
                            preferred,
                            patient,
                            rescuer,
                            false,
                            true,
                            false,
                            status) &&
                        RescueBedHasReservationCapacity(preferred, patient, rescuer))
                    {
                        return preferred;
                    }
                }
                catch (Exception exception)
                {
                    Log.WarningOnce("[Search and Rescue] Move the Patient bed selection failed; using RimWorld's bed search. " +
                        exception.GetBaseException().Message, 196320741);
                }
            }

            Building_Bed bed = RestUtility.FindBedFor(patient, rescuer, false, false, status);
            if (bed != null && !IsTemporaryFieldTendBed(bed) &&
                RescueBedHasReservationCapacity(bed, patient, rescuer))
            {
                return bed;
            }

            bed = RestUtility.FindBedFor(patient, rescuer, false, true, status);
            if (bed != null && !IsTemporaryFieldTendBed(bed) &&
                RescueBedHasReservationCapacity(bed, patient, rescuer))
            {
                return bed;
            }

            // Smart Medicine's temporary tending spot is a medical Building_Bed and can
            // therefore win RimWorld's normal bed search while sitting under the casualty.
            // If it did, explicitly choose the nearest valid real bed instead.
            return FindNonTemporaryRescueBed(patient, rescuer, status, false)
                ?? FindNonTemporaryRescueBed(patient, rescuer, status, true);
        }

        public static bool IsTemporaryFieldTendBed(Building_Bed bed)
        {
            if (bed == null)
            {
                return false;
            }

            return bed.def?.defName == "TempSleepSpot" ||
                   bed.GetType().FullName == "SmartMedicine.Building_TempTendSpot";
        }

        public static bool IsSafeRescueBed(Building_Bed bed, Pawn patient)
        {
            if (bed == null || patient == null || !bed.Spawned || bed.Destroyed ||
                IsTemporaryFieldTendBed(bed))
            {
                return false;
            }

            // FindBestRescueBed deliberately permits vanilla's ordinary-bed fallback.
            // Delivery to that same valid bed must complete transport even when it is not
            // designated medical. Outstanding treatment is tracked independently.
            GuestStatus? status = patient.IsPrisonerOfColony ? GuestStatus.Prisoner : patient.GuestStatus;
            return RestUtility.CanUseBedNow(
                bed,
                patient,
                false,
                true,
                status);
        }

        public static bool RescueBedHasReservationCapacity(
            Building_Bed bed,
            Pawn patient,
            Pawn rescuer)
        {
            if (bed == null || bed.Destroyed || !bed.Spawned || rescuer == null ||
                bed.Map != rescuer.Map || !bed.AnyUnoccupiedSleepingSlot)
            {
                return false;
            }

            HashSet<Pawn> competingOccupants = new HashSet<Pawn>();
            for (int slot = 0; slot < bed.SleepingSlotsCount; slot++)
            {
                Pawn occupant = bed.GetCurOccupant(slot);
                if (occupant != null && occupant != patient)
                {
                    competingOccupants.Add(occupant);
                }
            }

            HashSet<Pawn> reservers = new HashSet<Pawn>();
            bed.Map.reservationManager.ReserversOf(bed, reservers);
            // JobDriver_TakeToBed clears the current patient's reservations before reserving
            // the destination, so that one claimant does not consume capacity here. Every
            // other patient/carrier is a real pending occupant and must consume one slot.
            competingOccupants.UnionWith(
                reservers.Where(pawn => pawn != null && pawn != patient && pawn != rescuer));
            return competingOccupants.Count < bed.SleepingSlotsCount;
        }

        private static Building_Bed FindNonTemporaryRescueBed(
            Pawn patient,
            Pawn rescuer,
            GuestStatus? status,
            bool ignoreOtherReservations)
        {
            if (patient?.MapHeld == null || rescuer == null)
            {
                return null;
            }

            return patient.MapHeld.listerThings.ThingsInGroup(ThingRequestGroup.Bed)
                .OfType<Building_Bed>()
                .Where(candidate => !IsTemporaryFieldTendBed(candidate) &&
                                    candidate.Spawned && !candidate.Destroyed &&
                                    RescueBedHasReservationCapacity(candidate, patient, rescuer) &&
                                    RestUtility.IsValidBedFor(candidate, patient, rescuer,
                                        false, false, ignoreOtherReservations, status) &&
                                    rescuer.CanReach(candidate, PathEndMode.Touch, Danger.Deadly))
                .OrderByDescending(candidate => candidate.Medical)
                .ThenBy(candidate => patient.PositionHeld.DistanceToSquared(candidate.Position))
                .FirstOrDefault();
        }

        public static Job MakeCaptureJob(Pawn target)
        {
            Job job = JobMaker.MakeJob(ArrestHereJob ?? SearchAndRescueDefOf.SAR_CaptureInPlace, target);
            job.playerForced = false;
            return job;
        }

        public static void RegisterPriorityTreatmentJobs()
        {
            try
            {
                Type priorityTreatmentType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(assembly => assembly.GetType("TKS_PriorityTreatment.TKS_PriorityTreatment", false))
                    .FirstOrDefault(type => type != null);
                FieldInfo doctorJobsField = priorityTreatmentType?.GetField(
                    "doctorWorkDefs",
                    BindingFlags.Public | BindingFlags.Static);
                if (!(doctorJobsField?.GetValue(null) is IList<string> doctorJobs))
                {
                    return;
                }

                foreach (string jobName in new[]
                         {
                             "CP_FirstAid", "Stabilize", "ProvideFirstAid", "UseSuctionDevice",
                             "PerformCpr", "UseDefibrillator", "UseEpinephrine", "UseTourniquet",
                             "UseHemostaticAgent", "UseBandage", "UseBloodBag", "UseSalineBag",
                             "SAR_EvacuateToPoint", "SAR_CaptureInPlace", "SAR_WaitForFieldTreatment",
                             "SAR_RestockMedicalKit", "SAR_DeliverMedicalSupply"
                         })
                {
                    if (!doctorJobs.Contains(jobName))
                    {
                        doctorJobs.Add(jobName);
                    }
                }
            }
            catch (Exception exception)
            {
                Log.WarningOnce("[Search and Rescue] Priority Treatment registration failed. " +
                    exception.GetBaseException().Message, 196320743);
            }
        }

        public static string ActiveCompatibilitySummary()
        {
            // The settings table is the canonical compatibility inventory. Reuse it here so
            // adding a compatibility profile cannot silently leave startup diagnostics stale.
            List<string> active = CompatibilityCatalog.Entries
                .Where(entry => entry.Active)
                .Select(entry => entry.DisplayName)
                .ToList();
            active.AddRange(CompatibilityRegistry.ActiveProfileNames());
            List<string> distinct = active.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return distinct.Count == 0 ? "none" : string.Join(", ", distinct);
        }

        public static bool NeedsAnyFieldTreatment(Pawn patient)
        {
            if (MechanicalCare.IsPatient(patient)) return MechanicalCare.NeedsRepair(patient);
            return patient?.health != null &&
                   EffectiveMedicalCare(patient) > MedicalCareCategory.NoCare &&
                   (patient.health.HasHediffsNeedingTend() || HasFieldTreatableEmergency(patient) ||
                    HasMoreInjuriesTransfusionNeed(patient) || HasHemogenTransfusionNeed(patient));
        }

        internal static Hediff MoreInjuriesTourniquetForRemoval(Pawn patient)
        {
            if (!UsesMoreInjuries || patient?.health?.hediffSet == null)
            {
                return null;
            }

            // More Injuries anchors each tourniquet to one shoulder/leg (or its special
            // neck case) and applies its coagulation multiplier only to that part's subtree.
            // Other limbs may still be bleeding heavily without justifying continued
            // ischemia here. Conversely, the tourniquet's own 95% bleed reduction must not
            // make it removable before every tendable bleed in this subtree is dealt with.
            return patient.health.hediffSet.hediffs
                .Where(hediff => hediff.def.defName == "TourniquetApplied" && hediff.Part != null)
                .FirstOrDefault(tourniquet => !patient.health.hediffSet.hediffs.Any(hediff =>
                    hediff != tourniquet && hediff.Bleeding && hediff.TendableNow() &&
                    IsOnBodyPartOrChildren(hediff.Part, tourniquet.Part)));
        }

        private static bool IsOnBodyPartOrChildren(BodyPartRecord part, BodyPartRecord ancestor)
        {
            for (BodyPartRecord current = part; current != null; current = current.parent)
            {
                if (current == ancestor)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool HasHemogenTransfusionNeed(Pawn patient)
        {
            return UsesHemogenTransfusion && patient?.RaceProps?.Humanlike == true &&
                   AllowsMedicalDevices(patient) &&
                   (patient.health?.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss)?.Severity ?? 0f) >= 0.15f;
        }

        public static int HemogenPacksRequired(Pawn patient)
        {
            float severity = patient?.health?.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss)?.Severity ?? 0f;
            return HasHemogenTransfusionNeed(patient)
                ? Math.Max(1, Math.Min(3, (int)Math.Ceiling(severity / 0.35f)))
                : 0;
        }

        public static bool HasMoreInjuriesTransfusionNeed(Pawn patient)
        {
            if (!UsesMoreInjuries || patient?.RaceProps?.Humanlike != true ||
                !AllowsMedicalDevices(patient))
            {
                return false;
            }

            bool needsSaline = MoreInjuriesSalineBag != null &&
                               IsMedicalInterventionUnlocked(MedicalIntervention.Saline) &&
                               MoreInjuriesPlannedTransfusions(
                                   patient,
                                   MedicalIntervention.Saline) > 0;
            bool needsBlood = MoreInjuriesBloodBag != null &&
                              IsMedicalInterventionUnlocked(MedicalIntervention.Blood) &&
                              MoreInjuriesPlannedTransfusions(
                                  patient,
                                  MedicalIntervention.Blood) > 0;
            return needsSaline || needsBlood;
        }

        public static bool HasFieldTreatableEmergency(Pawn patient)
        {
            if (!RobotMedicalProfile.AllowsBiologicalEmergency(patient)) return false;
            if (!UsesMoreInjuries || patient?.health == null)
            {
                return false;
            }

            // Safe removal is required for any supported flesh pawn once the limb is no
            // longer bleeding; unlike transfusion, it is not inherently humanlike-only.
            if (MoreInjuriesTourniquetForRemoval(patient) != null)
            {
                return true;
            }

            if (patient.RaceProps?.Humanlike != true)
            {
                return false;
            }

            bool cprReady = MoreInjuriesCprResearch?.IsFinished == true;
            bool equipmentReady = MoreInjuriesEmergencyMedicine?.IsFinished == true &&
                                  AllowsMedicalDevices(patient);
            return patient.health.hediffSet.hediffs.Any(hediff =>
                (hediff.def.defName == "ChokingOnBlood" ||
                 hediff.def.defName == "CardiacArrest") && (cprReady || equipmentReady) ||
                hediff.def.defName == "HeartAttack" && equipmentReady);
        }

        public static float FieldEmergencySeverity(Pawn patient)
        {
            if (MechanicalCare.IsPatient(patient)) return MechanicalCare.Damage(patient);
            if (patient?.health == null)
            {
                return 0f;
            }

            float severity = 0f;
            foreach (Hediff hediff in patient.health.hediffSet.hediffs)
            {
                if (hediff.def.defName == "ChokingOnBlood" ||
                    hediff.def.defName == "CardiacArrest" ||
                    hediff.def.defName == "HeartAttack")
                {
                    severity += Math.Max(0.05f, hediff.Severity);
                }
            }

            Hediff removableTourniquet = MoreInjuriesTourniquetForRemoval(patient);
            if (removableTourniquet != null)
            {
                severity += 0.25f + Math.Max(0f, removableTourniquet.Severity);
            }

            return severity;
        }

        public static double MedicalEmergencyUrgency(Pawn patient)
        {
            if (patient?.health == null)
            {
                return 0d;
            }

            double urgency = InfectionPriority.Urgency(patient);
            foreach (Hediff hediff in patient.health.hediffSet.hediffs)
            {
                if (InfectionPriority.IsInfection(hediff) || hediff.CurStage?.lifeThreatening != true)
                {
                    continue;
                }

                float lethalSeverity = hediff.def.lethalSeverity;
                double lethalPressure = lethalSeverity > 0f
                    ? Math.Min(1d, hediff.Severity / lethalSeverity)
                    : Math.Min(1d, Math.Max(0.15d, hediff.Severity));
                urgency += 0.8d + lethalPressure * 1.7d;
                if (IsUrgentSurgicalCondition(hediff))
                {
                    // Lung collapse and similar conditions cannot be fixed by the field-care
                    // edge. Their score belongs primarily on immediate evacuation.
                    urgency += 1.2d;
                }
            }

            return Math.Min(5d, urgency);
        }

        public static bool RequiresUrgentSurgery(Pawn patient)
        {
            return patient?.health?.hediffSet?.hediffs?.Any(IsUrgentSurgicalCondition) == true;
        }

        public static bool IsTreatmentJob(JobDef jobDef)
        {
            return CompatibilityRegistry.HasRole(jobDef, PatientJobRole.Treatment);
        }

        public static bool IsMoreInjuriesTreatmentJob(JobDef jobDef)
        {
            if (!UsesMoreInjuries || jobDef == null)
            {
                return false;
            }

            string defName = jobDef.defName;
            return defName == "ProvideFirstAid" || defName == "UseSuctionDevice" ||
                   defName == "PerformCpr" || defName == "UseDefibrillator" ||
                   defName == "UseEpinephrine" || defName == "UseMorphine" ||
                   defName == "UseKetamine" || defName == "UseChloroform" ||
                   defName == "UseSplint" || defName == "UseTourniquet" ||
                   defName == "RemoveTourniquetSafely" ||
                   defName == "RemoveTourniquetQuickly" ||
                   defName == "UseHemostaticAgent" || defName == "UseBandage" ||
                   defName == "UseBloodBag" || defName == "UseSalineBag" ||
                   defName == "HarvestBlood";
        }

        private static JobDef MoreInjuriesTreatmentJobFor(Pawn patient)
        {
            if (!RobotMedicalProfile.AllowsBiologicalEmergency(patient)) return null;
            if (!UsesMoreInjuries || patient?.RaceProps?.Humanlike != true || !patient.Downed ||
                MoreInjuriesCprResearch?.IsFinished != true)
            {
                return null;
            }

            if (patient.health.hediffSet.hediffs.Any(hediff =>
                    hediff.def.defName == "ChokingOnBlood" || hediff.def.defName == "CardiacArrest"))
            {
                // A direct CPR job is exactly one intervention, preserving Search and Rescue's
                // re-match-after-each-round behavior. The aggregate first-aid job would retain
                // the same doctor and continue through every minor wound.
                return MoreInjuriesCprJob;
            }

            // Heart attack is not CPR-treatable in More Injuries. Without a defibrillator
            // option the field-care edge must yield to evacuation instead of dispatching the
            // aggregate first-aid driver into an ineffective CPR child job.
            return null;
        }

        private static bool HasSurgeryFor(HediffDef hediffDef)
        {
            return SurgeryRemovableHediffs.Value.Contains(hediffDef);
        }

        private static bool IsUrgentSurgicalCondition(Hediff hediff)
        {
            // Do not prioritize elective bills or old scars. This lane is specifically for
            // life-threatening conditions that field tending cannot resolve but a loaded
            // surgery recipe can.
            return hediff?.CurStage?.lifeThreatening == true && !hediff.TendableNow() &&
                   HasSurgeryFor(hediff.def);
        }

        private static bool IsRunningMod(string packageId)
        {
            return LoadedModManager.RunningMods.Any(mod =>
                string.Equals(mod.PackageId, packageId, StringComparison.OrdinalIgnoreCase));
        }

        private static bool TryGetChooseYourMedicinePolicy(
            Pawn patient,
            out List<MedicalCareCategory> categories,
            out bool detailed)
        {
            categories = null;
            detailed = false;
            if (patient == null || ChooseYourMedicineCareMethod == null)
            {
                return false;
            }

            try
            {
                object[] arguments = { patient, false };
                object result = ChooseYourMedicineCareMethod.Invoke(null, arguments);
                categories = (result as IEnumerable<MedicalCareCategory>)?.ToList() ??
                             new List<MedicalCareCategory>();
                detailed = arguments[1] is bool value && value;
                return true;
            }
            catch (Exception exception)
            {
                Log.WarningOnce("[Search and Rescue] Choose Your Medicine policy lookup failed; using the pawn's " +
                                "vanilla medical-care setting. " + exception.GetBaseException().Message, 196320747);
                return false;
            }
        }

        private static MedicalCareCategory ChooseYourMedicineCategory(Thing medicine)
        {
            if (medicine != null && ChooseYourMedicineThingCareMethod != null)
            {
                try
                {
                    return (MedicalCareCategory)ChooseYourMedicineThingCareMethod.Invoke(
                        null, new object[] { medicine });
                }
                catch (Exception exception)
                {
                    Log.WarningOnce("[Search and Rescue] Choose Your Medicine category lookup failed; using " +
                                    "vanilla potency bands. " + exception.GetBaseException().Message, 196320748);
                }
            }

            return MedicineCareCategory(medicine?.def);
        }

        private static MedicalCareCategory MedicineCareCategory(ThingDef medicineDef)
        {
            if (medicineDef == null || !medicineDef.IsMedicine)
            {
                return MedicalCareCategory.NoMeds;
            }

            float potency = medicineDef.GetStatValueAbstract(StatDefOf.MedicalPotency);
            if (potency > ThingDefOf.MedicineIndustrial.GetStatValueAbstract(StatDefOf.MedicalPotency))
            {
                return MedicalCareCategory.Best;
            }
            if (potency > ThingDefOf.MedicineHerbal.GetStatValueAbstract(StatDefOf.MedicalPotency))
            {
                return MedicalCareCategory.NormalOrWorse;
            }
            return potency > 0f ? MedicalCareCategory.HerbalOrWorse : MedicalCareCategory.NoMeds;
        }

        internal static int FieldRescueWorkPriority(Pawn worker)
        {
            return WorkTypePriority(worker, SearchAndRescueDefOf.SAR_FieldRescue);
        }

        internal static int MechanicalRepairWorkPriority(Pawn worker, WorkGiverDef nativeProvider)
        {
            WorkGiverDef field = DefDatabase<WorkGiverDef>.GetNamedSilentFail("SAR_RepairMarkedMech");
            int fieldPriority = FieldRescueChildPriority(worker, field);
            int repairPriority = DetailedWorkPriority(worker, nativeProvider, nativeProvider.workType);
            return fieldPriority > 0 && repairPriority > 0 ? Math.Max(fieldPriority, repairPriority) : 0;
        }

        private static int FieldRescueChildPriority(Pawn worker, WorkGiverDef workGiver)
        {
            if (Compatibility.IsColonyWorkMech(worker))
            {
                return MechWorkerCompatibility.IsFieldResponderOptedIn(worker) ? 3 : 0;
            }

            return DetailedWorkPriority(
                worker,
                workGiver,
                SearchAndRescueDefOf.SAR_FieldRescue);
        }

        private static int CombinedFieldAndProviderPriority(
            Pawn worker,
            WorkGiverDef workGiver,
            WorkTypeDef providerWorkType)
        {
            if (!HardworkingCompatibility.IsWorkGiverAllowed(worker, workGiver)) return 0;

            if (HardworkingCompatibility.IsWorker(worker) &&
                (worker.WorkTypeIsDisabled(SearchAndRescueDefOf.SAR_FieldRescue) ||
                 worker.WorkTypeIsDisabled(providerWorkType) ||
                 worker.WorkTagIsDisabled(providerWorkType.workTags) ||
                 (workGiver != null && worker.WorkTagIsDisabled(workGiver.workTags)))) return 0;

            int fieldPriority = FieldRescueChildPriority(worker, workGiver);
            int providerPriority = WorkTypePriority(worker, providerWorkType);
            return fieldPriority <= 0 || providerPriority <= 0
                ? 0
                : Math.Max(fieldPriority, providerPriority);
        }

        private static int DetailedWorkPriority(Pawn worker, WorkGiverDef workGiver, WorkTypeDef fallbackWorkType)
        {
            if (worker == null || workGiver == null || fallbackWorkType == null)
            {
                return 0;
            }

            if (worker.workSettings != null)
            {
                if (WorkTabGetPriority != null && !HardworkingCompatibility.IsWorker(worker))
                {
                    try
                    {
                        return WorkTabGetPriority(worker, workGiver, -1);
                    }
                    catch (Exception exception)
                    {
                        Log.WarningOnce("[Search and Rescue] Work Tab priority lookup failed; using the parent work type. " +
                            exception.GetBaseException().Message, 196320744);
                    }
                }

                return worker.workSettings.GetPriority(fallbackWorkType);
            }

            if (!IsColonyWorkMech(worker))
            {
                return 0;
            }

            MechWorkTypePriority configured = worker.RaceProps.mechWorkTypePriorities?
                .FirstOrDefault(entry => entry.def == fallbackWorkType);
            return configured?.priority ?? 3;
        }

        private static int WorkTypePriority(Pawn worker, WorkTypeDef workType)
        {
            if (worker == null || workType == null)
            {
                return 0;
            }

            // The roster substitutes only for SAR_FieldRescue. Provider work remains
            // governed by the mech race and its live vanilla/Work Tab priority.
            bool colonyWorkMech = IsColonyWorkMech(worker);
            if (colonyWorkMech && !MechWorkerCompatibility.SupportsNativeWorkType(worker, workType))
            {
                return 0;
            }

            if (worker.workSettings != null)
            {
                int nativePriority = worker.workSettings.GetPriority(workType);
                if (colonyWorkMech && nativePriority <= 0)
                {
                    // Mech Work Tab keeps an hourly schedule separate from the native
                    // Pawn_WorkSettings value. Both are permissions: its enabled hourly
                    // value must never revive a provider the player disabled natively.
                    return 0;
                }

                if (WorkTabGetWorkTypePriority != null && !HardworkingCompatibility.IsWorker(worker))
                {
                    try
                    {
                        // Work Tab interprets -1 as the pawn's current local hour.
                        return WorkTabGetWorkTypePriority(worker, workType, -1);
                    }
                    catch (Exception exception)
                    {
                        Log.WarningOnce("[Search and Rescue] Work Tab work-type priority lookup failed; " +
                                        "using the vanilla work priority. " +
                                        exception.GetBaseException().Message, 196320749);
                    }
                }

                return nativePriority;
            }

            return colonyWorkMech
                ? MechWorkerCompatibility.DefaultNativeWorkTypePriority(worker, workType)
                : 0;
        }

        private static bool TryMakeSmartMedicineJob(Pawn doctor, Pawn patient, out Job job)
        {
            job = null;
            if (!TryFindSmartMedicinePrimary(doctor, patient, onlyUseInventory: false, out Thing primaryMedicine))
            {
                return false;
            }

            job = JobMaker.MakeJob(JobDefOf.TendPatient, patient, primaryMedicine);
            job.count = 1;
            return true;
        }

        internal static bool TryFindSmartMedicinePrimary(
            Pawn doctor,
            Pawn patient,
            bool onlyUseInventory,
            out Thing primaryMedicine)
        {
            primaryMedicine = null;
            if (SmartMedicineFindMethod == null)
            {
                return false;
            }

            try
            {
                object[] arguments = { doctor, patient, 0, onlyUseInventory };
                object result = SmartMedicineFindMethod.Invoke(null, arguments);
                List<ThingCount> medicines = (result as IEnumerable<ThingCount>)?.ToList() ?? new List<ThingCount>();
                primaryMedicine = medicines.Count > 0 ? medicines[0].Thing : null;
                return true;
            }
            catch (Exception exception)
            {
                Log.WarningOnce("[Search and Rescue] Smart Medicine selection failed; using its patched vanilla medicine search. " +
                    exception.GetBaseException().Message, 196320742);
                return false;
            }
        }

        private static Thing FindCombatExtendedMedicine(Pawn doctor, Pawn patient)
        {
            IEnumerable<Thing> carried = new[] { doctor.carryTracker.CarriedThing }
                .Where(thing => thing != null && thing.def.IsMedicine && AllowsMedicine(patient, thing));
            IEnumerable<Thing> doctorInventory = doctor.inventory?.innerContainer
                ?.Where(thing => thing.def.IsMedicine && AllowsMedicine(patient, thing)) ??
                Enumerable.Empty<Thing>();
            IEnumerable<Thing> patientInventory = patient.inventory?.innerContainer
                ?.Where(thing => thing.def.IsMedicine && AllowsMedicine(patient, thing)) ??
                Enumerable.Empty<Thing>();

            Thing heldMedicine = carried
                .Concat(patientInventory)
                .Concat(doctorInventory)
                .OrderByDescending(thing => thing.GetStatValue(StatDefOf.MedicalPotency))
                .FirstOrDefault();
            if (heldMedicine != null)
            {
                return heldMedicine;
            }

            Thing mapMedicine = HealthAIUtility.FindBestMedicine(doctor, patient);
            return CombatExtendedCanCollectMedicineDirectly(doctor, patient, mapMedicine)
                ? mapMedicine
                : null;
        }

        private static bool CombatExtendedCanCollectMedicineDirectly(
            Pawn doctor,
            Pawn patient,
            Thing medicine)
        {
            if (medicine == null || medicine.Destroyed)
            {
                return false;
            }

            // CE's Stabilize driver supports a spawned stack, the doctor's carried/inventory
            // medicine, and the patient's inventory. It does not support a third pawn or
            // vehicle as targetB: those sources must first pass through SAR restocking or an
            // implicit supply delivery so the eventual treatment edge has a collectable item.
            return medicine.Spawned ||
                   doctor?.carryTracker?.CarriedThing == medicine ||
                   doctor?.inventory?.innerContainer.Contains(medicine) == true ||
                   patient?.inventory?.innerContainer.Contains(medicine) == true;
        }
    }
}
