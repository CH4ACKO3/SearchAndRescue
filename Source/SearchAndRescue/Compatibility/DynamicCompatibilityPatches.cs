using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SearchAndRescue
{
    internal static class FiniteForcedWaitRecovery
    {
        private sealed class WaitLease
        {
            public readonly Pawn Patient;
            public readonly Job WaitJob;

            public WaitLease(Pawn patient, Job waitJob)
            {
                Patient = patient;
                WaitJob = waitJob;
            }
        }

        private static readonly Dictionary<Job, WaitLease> WaitByProviderJob =
            new Dictionary<Job, WaitLease>();

        internal static void RecordAfterForceWait(Pawn patient)
        {
            if (!IsFiniteForcedWait(patient))
            {
                return;
            }

            Pawn provider = ActiveMoreInjuriesProvider(patient);
            Job providerJob = provider?.CurJob;
            if (providerJob != null)
            {
                WaitByProviderJob[providerJob] = new WaitLease(patient, patient.CurJob);
            }
        }

        internal static bool HasActiveMoreInjuriesProvider(Pawn patient)
        {
            return ActiveMoreInjuriesProvider(patient) != null;
        }

        private static Pawn ActiveMoreInjuriesProvider(Pawn patient)
        {
            return patient?.MapHeld?.mapPawns?.AllPawnsSpawned.FirstOrDefault(candidate =>
                candidate != null && candidate != patient &&
                Compatibility.IsMoreInjuriesTreatmentJob(candidate.CurJobDef) &&
                CompatibilityRegistry.PatientFor(
                    candidate,
                    candidate.CurJob,
                    PatientJobRole.Treatment) == patient);
        }

        internal static void ReleaseAfterProviderEnds(Pawn provider, Job providerJob)
        {
            if (providerJob == null ||
                !WaitByProviderJob.TryGetValue(providerJob, out WaitLease lease))
            {
                return;
            }
            WaitByProviderJob.Remove(providerJob);

            Pawn patient = lease.Patient;
            if (patient == null || patient == provider || patient.CurJob != lease.WaitJob ||
                !IsFiniteForcedWait(patient))
            {
                return;
            }

            // Release only the exact Wait job observed immediately after this provider called
            // ForceWait. A finite Wait created by another mod or a player order is not evidence
            // that this More Injuries job owns it.
            if (!HasOtherActiveProvider(patient.MapHeld, patient, provider, providerJob))
            {
                patient.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: true);
            }
        }

        private static bool IsFiniteForcedWait(Pawn patient)
        {
            Job wait = patient?.CurJob;
            return patient?.jobs != null && wait != null && !wait.playerForced &&
                   wait.expiryInterval > 0 &&
                   (wait.def == JobDefOf.Wait || wait.def == JobDefOf.Wait_MaintainPosture);
        }

        private static bool HasOtherActiveProvider(
            Map map,
            Pawn patient,
            Pawn endingProvider,
            Job endingJob)
        {
            return map?.mapPawns?.AllPawnsSpawned != null &&
                   map.mapPawns.AllPawnsSpawned.Any(provider =>
                       provider != null && (provider != endingProvider || provider.CurJob != endingJob) &&
                       Compatibility.IsMoreInjuriesTreatmentJob(provider.CurJobDef) &&
                       CompatibilityRegistry.PatientFor(
                           provider,
                           provider.CurJob,
                           PatientJobRole.Treatment) == patient);
        }
    }

    internal static class TemporaryTendSpotLifecycle
    {
        internal static bool CurrentJobOwnsTemporaryBed(Pawn pawn, out Building_Bed bed)
        {
            bed = pawn?.CurJob?.targetA.Thing as Building_Bed;
            return pawn?.Spawned == true && bed != null && bed.Spawned && !bed.Destroyed &&
                   bed.MapHeld == pawn.MapHeld && bed.OccupiedRect().Contains(pawn.Position) &&
                   Compatibility.IsTemporaryFieldTendBed(bed) &&
                   (pawn.CurJob.def == JobDefOf.LayDown || pawn.CurJob.def == JobDefOf.LayDownAwake);
        }
    }

    [HarmonyPatch(typeof(PawnUtility), nameof(PawnUtility.ForceWait))]
    internal static class MoreInjuriesMedicalJob_SearchAndRescueForcedWaitLeasePatch
    {
        private static bool Prefix(Pawn pawn)
        {
            if (pawn?.Spawned != true ||
                (!SearchAndRescueJobContext.HasManagedTreatmentOrder(pawn) &&
                 !FiniteForcedWaitRecovery.HasActiveMoreInjuriesProvider(pawn)))
            {
                return true;
            }

            if (!TemporaryTendSpotLifecycle.CurrentJobOwnsTemporaryBed(pawn, out _))
            {
                return true;
            }

            // More Injuries calls ForceWait at the start of a device procedure. Vanilla
            // responds to an in-bed patient by starting a fresh LayDown job for that bed.
            // Ending the old LayDown first runs Smart Medicine's finish action, which destroys
            // TempSleepSpot before the fresh job can reserve it. The existing LayDown already
            // supplies the required posture and immobility, so retaining it is the only
            // state-preserving operation.
            return false;
        }

        private static void Postfix(Pawn pawn)
        {
            FiniteForcedWaitRecovery.RecordAfterForceWait(pawn);
        }
    }

    [HarmonyPatch(typeof(RestUtility), nameof(RestUtility.KickOutOfBed))]
    internal static class TemporaryOrRemovedBed_SearchAndRescueKickOutPatch
    {
        private static bool Prefix(Pawn pawn, ref Building_Bed bed)
        {
            if (pawn?.Spawned != true || !SearchAndRescueJobContext.HasManagedBattlefieldOrder(pawn))
            {
                return true;
            }

            Building_Bed currentBed = pawn.CurrentBed();
            if (currentBed == bed)
            {
                return true;
            }

            if (currentBed == null && !pawn.InBed())
            {
                // A destroyed/de-spawned bed may already have normalized posture before a
                // stale bed-toil finish action runs. There is nothing left to kick out.
                return false;
            }

            // Toils_Bed captures its original bed in a finish-action closure. If that bed was
            // dismantled, or an overlapping Smart Medicine spot became CurrentBed, passing the
            // captured instance to KickOutOfBed logs an error and can move the pawn to the
            // wrong sleeping slot. Let vanilla perform its normal posture cleanup against the
            // bed the pawn actually occupies; null is also valid and only clears the bed bit.
            bed = currentBed;
            return true;
        }
    }

    [HarmonyPatch(typeof(JobDriver), nameof(JobDriver.Cleanup))]
    internal static class MoreInjuriesMedicalJob_SearchAndRescueForcedWaitCleanupPatch
    {
        private static void Postfix(JobDriver __instance)
        {
            FiniteForcedWaitRecovery.ReleaseAfterProviderEnds(
                __instance?.pawn,
                __instance?.job);
        }
    }

    [HarmonyPatch]
    internal static class SmartMedicineStockUp_SearchAndRescueFieldSupplyPatch
    {
        private static Type StockUpType => AccessTools.TypeByName("SmartMedicine.JobGiver_StockUp");

        private static bool Prepare()
        {
            return TargetMethod() != null;
        }

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(StockUpType, "TryGiveJob");
        }

        private static void Postfix(ref Job __result)
        {
            Thing pickup = __result?.targetA.Thing;
            if (pickup != null && SearchAndRescueJobContext.IsProtectedOrClaimedMedicalSupply(pickup))
            {
                // Smart Medicine uses a custom GenClosest stock-up search rather than the
                // vanilla hauling predicates patched elsewhere. Include the current planning
                // cycle's soft claims so it cannot steal a stack between matching and pickup.
                __result = null;
            }
        }
    }

    internal static class PatientWorkOwnership
    {
        internal static bool IsForced(MethodBase method, object[] args)
        {
            ParameterInfo[] parameters = method?.GetParameters();
            if (parameters == null || args == null)
            {
                return false;
            }

            int count = Math.Min(parameters.Length, args.Length);
            for (int i = 0; i < count; i++)
            {
                if (parameters[i].ParameterType == typeof(bool) &&
                    string.Equals(parameters[i].Name, "forced", StringComparison.OrdinalIgnoreCase) &&
                    args[i] is bool forced)
                {
                    return forced;
                }
            }
            return false;
        }

        internal static Pawn WorkerFromArguments(object[] args)
        {
            return args?.OfType<Pawn>().FirstOrDefault();
        }

        internal static Pawn PatientFromArguments(object[] args)
        {
            if (args == null)
            {
                return null;
            }

            bool skippedWorker = false;
            foreach (object argument in args)
            {
                if (!(argument is Pawn pawn))
                {
                    continue;
                }
                if (!skippedWorker)
                {
                    skippedWorker = true;
                    continue;
                }
                return pawn;
            }
            return null;
        }

        internal static bool HasManagedOrderForRole(Pawn patient, PatientJobRole roles)
        {
            if (patient == null || roles == PatientJobRole.None)
            {
                return false;
            }

            return (roles & PatientJobRole.Treatment) != 0 &&
                       SearchAndRescueJobContext.HasManagedTreatmentOrder(patient) ||
                   (roles & PatientJobRole.Capture) != 0 &&
                       SearchAndRescueJobContext.HasManagedCaptureOrder(patient) ||
                   (roles & PatientJobRole.Transport) != 0 &&
                       SearchAndRescueJobContext.HasManagedTransportOrder(patient) ||
                   (roles & PatientJobRole.Facility) != 0 &&
                       (SearchAndRescueJobContext.HasManagedTreatmentOrder(patient) ||
                        SearchAndRescueJobContext.HasManagedTransportOrder(patient));
        }
    }

    [HarmonyPatch]
    internal static class SmartMedicineTempTendSpot_SearchAndRescueExistingBedPatch
    {
        private static Type TempSpotUtilityType => AccessTools.TypeByName("SmartMedicine.UseTempSleepSpot");

        private static bool Prepare()
        {
            return TargetMethod() != null;
        }

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(TempSpotUtilityType, "LayDownInPlace");
        }

        private static bool Prefix(Pawn pawn, ref Job __result)
        {
            if (pawn?.Spawned != true)
            {
                return true;
            }

            if (TemporaryTendSpotLifecycle.CurrentJobOwnsTemporaryBed(pawn, out _))
            {
                // PatientGoToBed can be evaluated while the current LayDown is still being
                // retired. CurrentBed() is not authoritative when temporary spots overlap: it
                // may return a different instance from the one owned by the running job.
                // Smart Medicine would then return a new LayDown for a spot destroyed by the
                // old driver's finish action. Keep the lifecycle-owning job instead.
                __result = null;
                return false;
            }

            if (!SearchAndRescueJobContext.HasManagedTreatmentOrder(pawn))
            {
                return true;
            }

            Building_Bed existingTemporaryBed = pawn.Position.GetThingList(pawn.Map)
                .OfType<Building_Bed>()
                .FirstOrDefault(Compatibility.IsTemporaryFieldTendBed);
            if (existingTemporaryBed != null && !pawn.CanReserve(existingTemporaryBed, 1, -1))
            {
                // Smart Medicine's fallback bypasses FindBedFor and does not perform the
                // reservation test expected of a job giver. Refuse an already-contested spot
                // here instead of letting StartJob emit a failed-pre-reservation warning.
                __result = null;
                return false;
            }

            Building_Bed occupiedRealBed = pawn.Position.GetThingList(pawn.Map)
                .OfType<Building_Bed>()
                .FirstOrDefault(bed => !Compatibility.IsTemporaryFieldTendBed(bed) &&
                                       OccupiesBed(pawn, bed));
            if (occupiedRealBed == null)
            {
                return true;
            }

            // Smart Medicine reaches LayDownInPlace only after vanilla FindBedFor returned
            // null. A casualty can nevertheless already physically occupy a real bed whose
            // only slot is no longer considered available. Spawning TempSleepSpot on that
            // same cell makes CurrentBed nondeterministic and later bed-toil cleanup tries to
            // kick the pawn out of a different bed. Remaining in the existing posture is the
            // safe fallback; the normal think tree supplies Wait_Downed/LayDown as needed.
            __result = null;
            return false;
        }

        private static bool OccupiesBed(Pawn pawn, Building_Bed bed)
        {
            for (int slot = 0; slot < bed.SleepingSlotsCount; slot++)
            {
                if (bed.GetCurOccupant(slot) == pawn)
                {
                    return true;
                }
            }
            return false;
        }
    }

    [HarmonyPatch]
    internal static class InBedJoy_SearchAndRescueTemporaryTendSpotPatch
    {
        private static MethodBase TargetMethod()
        {
            Type giverType = AccessTools.TypeByName("RimWorld.JobGiver_GetJoyInBed");
            return giverType == null ? null : AccessTools.Method(giverType, "TryGiveJob");
        }

        private static bool Prepare()
        {
            return TargetMethod() != null;
        }

        private static void Postfix(Pawn pawn, ref Job __result)
        {
            if (__result != null && SearchAndRescueJobContext.HasManagedTreatmentOrder(pawn) &&
                __result.targetA.Thing is Building_Bed bed && Compatibility.IsTemporaryFieldTendBed(bed))
            {
                // TempSleepSpot is a medical implementation detail, not a stable bed for
                // Pray or other in-bed joy jobs. Such jobs reserve/clean it as a normal bed
                // while Smart Medicine may independently retire it after field treatment.
                __result = null;
            }
        }
    }

    [HarmonyPatch]
    internal static class CombatExtendedStabilize_SearchAndRescueReservationPatch
    {
        private static MethodBase TargetMethod()
        {
            Type driverType = AccessTools.TypeByName("CombatExtended.JobDriver_Stabilize");
            return driverType == null
                ? null
                : AccessTools.Method(driverType, "TryMakePreToilReservations");
        }

        private static bool Prepare()
        {
            return TargetMethod() != null;
        }

        private static bool Prefix(
            bool errorOnFailed,
            Pawn ___pawn,
            Job ___job,
            ref bool __result)
        {
            bool managed = SearchAndRescueJobContext.IsActive(
                ___pawn,
                ___job,
                SearchAndRescueStage.Treat);
            bool smartMedicineAutonomous =
                ___job?.workGiverDef?.defName == "SmartMedicineStabilize";
            if (!managed && !smartMedicineAutonomous)
            {
                return true;
            }

            // CE's native driver reserves the medicine with maxPawns=1 even though its
            // pickup toil later uses vanilla's shared medicine reservation layer. Match the
            // SAR soft ledger and vanilla tending so doctors can reserve disjoint counts in
            // the same stack without producing a failed-reservation error.
            __result = ___pawn.Reserve(___job.targetA, ___job, 1, -1, null, errorOnFailed) &&
                       ___pawn.Reserve(
                           ___job.targetB,
                           ___job,
                           Toils_Tend.MaxMedicineReservations,
                           1,
                           null,
                           errorOnFailed);
            return false;
        }
    }

    [HarmonyPatch]
    internal static class SmartMedicineCombatExtendedStabilize_SearchAndRescuePreflightPatch
    {
        private static Type WorkGiverType => AccessTools.TypeByName(
            "SmartMedicine.Compatibility.CombatExtended.WorkGiver_Stabilize");

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(WorkGiverType, "JobOnThing");
        }

        private static bool Prepare()
        {
            return TargetMethod() != null &&
                   AccessTools.TypeByName("CombatExtended.JobDriver_Stabilize") != null;
        }

        private static void Postfix(Pawn healer, ref Job __result)
        {
            if (__result?.targetA.Thing == null || __result.targetB.Thing == null)
            {
                return;
            }

            Pawn patient = __result.targetA.Pawn;
            Thing proposedMedicine = __result.targetB.Thing;
            Thing medicine = SafeLocalMedicine(healer, patient, proposedMedicine);
            if (medicine == null)
            {
                // Smart Medicine deliberately searches every enabled colonist inventory,
                // even when onlyUseInventory is true. CE's Stabilize driver cannot complete
                // that route: its take-from-other-pawn branch walks to targetA (the patient),
                // then immediately fails unless the patient is also the medicine holder.
                // The two mods otherwise keep returning that zero-effect job in the same
                // WorkGiver scan. Prefer a safe medicine already carried by this healer;
                // otherwise decline this autonomous job and let SAR restock/deliver first.
                __result = null;
                return;
            }

            if (medicine != proposedMedicine)
            {
                __result.targetB = medicine;
            }

            int medicineCount = Math.Max(1, __result.count);
            if (!healer.CanReserve(__result.targetA, 1, -1) ||
                !healer.CanReserve(
                    __result.targetB,
                    MedicalResourceLedger.SharedStackReservationMaxPawns,
                    medicineCount))
            {
                // Smart Medicine checks only the patient before returning this job. CE then
                // reserves both targets during StartJob, so two doctors selected in one tick
                // can otherwise enter a failed-job loop over the same medicine stack.
                __result = null;
            }
        }

        internal static Thing SafeLocalMedicine(Pawn healer, Pawn patient, Thing proposed)
        {
            if (proposed != null && !proposed.Destroyed &&
                Compatibility.AllowsMedicine(patient, proposed) &&
                !SearchAndRescueJobContext.IsProtectedOrClaimedMedicalSupply(proposed))
            {
                Pawn holder = MedicalResourceLedger.InventoryHolder(proposed);
                if (proposed.Spawned || holder == healer || holder == patient ||
                    healer?.carryTracker?.CarriedThing == proposed)
                {
                    return proposed;
                }
            }

            IEnumerable<Thing> carried = healer?.carryTracker?.CarriedThing is Thing carriedThing
                ? new[] { carriedThing }
                : Enumerable.Empty<Thing>();
            IEnumerable<Thing> inventory = healer?.inventory?.innerContainer ?? Enumerable.Empty<Thing>();
            return carried.Concat(inventory)
                .Where(candidate => candidate != null && !candidate.Destroyed && candidate.def.IsMedicine &&
                                    Compatibility.AllowsMedicine(patient, candidate) &&
                                    !SearchAndRescueJobContext.IsProtectedOrClaimedMedicalSupply(candidate))
                .OrderByDescending(candidate => candidate.GetStatValue(StatDefOf.MedicalPotency))
                .FirstOrDefault();
        }
    }

    [HarmonyPatch]
    internal static class SmartMedicineCombatExtendedStabilize_SearchAndRescueScanPatch
    {
        private static MethodBase TargetMethod()
        {
            Type giverType = AccessTools.TypeByName(
                "SmartMedicine.Compatibility.CombatExtended.WorkGiver_Stabilize");
            return giverType == null ? null : AccessTools.Method(giverType, "HasJobOnThing");
        }

        private static bool Prepare()
        {
            return TargetMethod() != null &&
                   AccessTools.TypeByName("CombatExtended.JobDriver_Stabilize") != null;
        }

        private static void Postfix(Pawn pawn, Thing t, ref bool __result)
        {
            if (!__result || !(t is Pawn patient) ||
                !Compatibility.TryFindSmartMedicinePrimary(
                    pawn,
                    patient,
                    onlyUseInventory: true,
                    out Thing proposed))
            {
                return;
            }

            // Keep HasJobOnThing synchronized with the JobOnThing guard above. Returning
            // true here when CE cannot collect Smart Medicine's selected third-pawn stack
            // makes WorkGiver_Scanner report a target-without-job error.
            __result = SmartMedicineCombatExtendedStabilize_SearchAndRescuePreflightPatch
                .SafeLocalMedicine(pawn, patient, proposed) != null;
        }
    }

    [HarmonyPatch]
    internal static class CombatExtendedLoadoutPickup_SearchAndRescueSupplyPatch
    {
        private static MethodBase TargetMethod()
        {
            Type giverType = AccessTools.TypeByName("CombatExtended.JobGiver_UpdateLoadout");
            return giverType == null ? null : AccessTools.Method(giverType, "TryGiveJob");
        }

        private static bool Prepare()
        {
            return TargetMethod() != null;
        }

        private static void Postfix(ref Job __result)
        {
            Thing pickup = __result?.targetA.Thing;
            if (pickup != null && SearchAndRescueJobContext.IsProtectedOrClaimedMedicalSupply(pickup))
            {
                __result = null;
            }
        }
    }

    [HarmonyPatch]
    internal static class CombatExtendedLoadoutDrop_SearchAndRescueSupplyPatch
    {
        private static MethodBase TargetMethod()
        {
            Type utilityType = AccessTools.TypeByName("CombatExtended.Utility_HoldTracker");
            return utilityType == null
                ? null
                : AccessTools.Method(utilityType, "GetExcessThing");
        }

        private static bool Prepare()
        {
            return TargetMethod() != null;
        }

        private static void Postfix(ref bool __result, ref Thing dropThing, ref int dropCount)
        {
            if (__result && SearchAndRescueJobContext.IsProtectedOrClaimedMedicalSupply(dropThing))
            {
                dropThing = null;
                dropCount = 0;
                __result = false;
            }
        }
    }

    [HarmonyPatch]
    internal static class RegisteredPatientWorkGiver_SearchAndRescueOwnershipPatch
    {
        private static bool Prepare()
        {
            return CompatibilityRegistry.RegisteredWorkGiverMethods().Count > 0;
        }

        private static IEnumerable<MethodBase> TargetMethods()
        {
            return CompatibilityRegistry.RegisteredWorkGiverMethods().Keys
                .OfType<MethodInfo>()
                .Where(method => method.ReturnType == typeof(bool));
        }

        private static void Postfix(
            MethodBase __originalMethod,
            object __instance,
            object[] __args,
            ref bool __result)
        {
            if (!__result || PatientWorkOwnership.IsForced(__originalMethod, __args))
            {
                return;
            }

            Pawn patient = PatientWorkOwnership.PatientFromArguments(__args);
            Map map = patient?.MapHeld;
            PatientJobRole roles = CompatibilityRegistry.RoleForWorkGiver(
                __originalMethod,
                __instance);
            if (map != null && PatientWorkOwnership.HasManagedOrderForRole(patient, roles))
            {
                // One registered gate covers every autonomous third-party patient WorkGiver.
                // Forced float-menu orders remain an intentional external override.
                __result = false;
            }
        }
    }

    [HarmonyPatch]
    internal static class RegisteredPatientJobWorkGiver_SearchAndRescueOwnershipPatch
    {
        private static bool Prepare()
        {
            return CompatibilityRegistry.RegisteredWorkGiverMethods().Keys
                .OfType<MethodInfo>()
                .Any(method => method.ReturnType == typeof(Job));
        }

        private static IEnumerable<MethodBase> TargetMethods()
        {
            return CompatibilityRegistry.RegisteredWorkGiverMethods().Keys
                .OfType<MethodInfo>()
                .Where(method => method.ReturnType == typeof(Job));
        }

        private static void Postfix(
            MethodBase __originalMethod,
            object __instance,
            object[] __args,
            ref Job __result)
        {
            if (__result == null || PatientWorkOwnership.IsForced(__originalMethod, __args))
            {
                return;
            }

            PatientJobRole registeredRoles = CompatibilityRegistry.RoleForWorkGiver(
                __originalMethod,
                __instance);
            if (registeredRoles == PatientJobRole.None)
            {
                return;
            }

            Pawn worker = PatientWorkOwnership.WorkerFromArguments(__args);
            PatientJobRole resultRoles = CompatibilityRegistry.RolesFor(__result.def);
            PatientJobRole roles = resultRoles != PatientJobRole.None
                ? resultRoles
                : registeredRoles;
            Pawn patient = CompatibilityRegistry.PatientFor(worker, __result, roles) ??
                           PatientWorkOwnership.PatientFromArguments(__args);
            if (PatientWorkOwnership.HasManagedOrderForRole(patient, roles))
            {
                // Some scanners, notably MedPod's warden work, decide directly in
                // JobOnThing and never override HasJobOnThing.
                __result = null;
            }
        }
    }

    [HarmonyPatch]
    internal static class RegisteredPatientThinkNode_SearchAndRescueOwnershipPatch
    {
        private static bool Prepare()
        {
            return CompatibilityRegistry.RegisteredThinkNodeMethods().Count > 0;
        }

        private static IEnumerable<MethodBase> TargetMethods()
        {
            return CompatibilityRegistry.RegisteredThinkNodeMethods().Keys;
        }

        private static void Postfix(
            MethodBase __originalMethod,
            object[] __args,
            ref Job __result)
        {
            Pawn worker = __args?.OfType<Pawn>().FirstOrDefault();
            Pawn patient = worker?.mindState?.duty?.focus.Thing as Pawn;
            if (__result == null || patient == null)
            {
                return;
            }

            PatientJobRole resultRoles = CompatibilityRegistry.RolesFor(__result.def);
            PatientJobRole roles = resultRoles != PatientJobRole.None
                ? resultRoles
                : CompatibilityRegistry.RoleForThinkNode(__originalMethod);

            // A contracted Lord owns its patient before a concrete job exists. If that
            // provider is absent, a managed SAR order owns the matching role and the
            // autonomous ThinkNode must yield just like a registered WorkGiver.
            if (!CompatibilityRegistry.HasExternalOwner(patient, roles) &&
                PatientWorkOwnership.HasManagedOrderForRole(patient, roles))
            {
                __result = null;
            }
        }
    }

    [HarmonyPatch]
    internal static class RegisteredPatientJobValidator_SearchAndRescueCompatibilityPatch
    {
        private static bool Prepare()
        {
            return CompatibilityRegistry.RegisteredPatientJobValidatorMethods().Count > 0;
        }

        private static IEnumerable<MethodBase> TargetMethods()
        {
            return CompatibilityRegistry.RegisteredPatientJobValidatorMethods();
        }

        private static void Postfix(
            MethodBase __originalMethod,
            object __instance,
            object[] __args,
            ref bool __result)
        {
            if (__result)
            {
                return;
            }

            Job job = __args?.OfType<Job>().FirstOrDefault();
            PatientJobRole allowedRoles =
                CompatibilityRegistry.RoleForPatientJobValidator(__originalMethod);
            Pawn expectedPatient =
                CompatibilityRegistry.PatientForJobValidator(__originalMethod, __instance);
            if (job != null && expectedPatient != null &&
                CompatibilityRegistry.PatientFor(null, job, allowedRoles) == expectedPatient)
            {
                // Provider watchdogs should accept any treatment/transport job registered
                // with the common compatibility layer, including CE stabilization, More
                // Injuries devices/transfusions and future equivalents targeting the same
                // patient. Unrelated jobs remain subject to the provider's native policy.
                __result = true;
            }
        }
    }

    [HarmonyPatch]
    internal static class PickUpAndHaulUnload_SearchAndRescueFieldSupplyPatch
    {
        private sealed class HiddenSupplies
        {
            public readonly List<Thing> Things;
            public readonly bool OnlyProtectedEntries;

            public HiddenSupplies(List<Thing> things, bool onlyProtectedEntries)
            {
                Things = things;
                OnlyProtectedEntries = onlyProtectedEntries;
            }
        }

        private static MethodBase TargetMethod()
        {
            Type driverType = AccessTools.TypeByName("PickUpAndHaul.JobDriver_UnloadYourHauledInventory");
            return driverType == null
                ? null
                : AccessTools.Method(driverType, "FirstUnloadableThing");
        }

        private static bool Prepare()
        {
            return TargetMethod() != null;
        }

        private static void Prefix(
            HashSet<Thing> carriedThings,
            out HiddenSupplies __state)
        {
            List<Thing> hidden = carriedThings?
                .Where(SearchAndRescueJobContext.IsProtectedOrClaimedMedicalSupply)
                .ToList() ?? new List<Thing>();
            __state = new HiddenSupplies(
                hidden,
                carriedThings != null && carriedThings.Count > 0 && hidden.Count == carriedThings.Count);
            foreach (Thing protectedThing in hidden)
            {
                // Temporarily hide referenced supplies from PUAH's selection without
                // removing them from its persistent hauled-inventory ownership set.
                carriedThings.Remove(protectedThing);
            }
        }

        private static Exception Finalizer(
            Exception __exception,
            Pawn pawn,
            HashSet<Thing> carriedThings,
            HiddenSupplies __state)
        {
            if (carriedThings != null && __state != null)
            {
                foreach (Thing protectedThing in __state.Things.Where(
                             thing => thing != null && !thing.Destroyed))
                {
                    carriedThings.Add(protectedThing);
                }
            }

            if (__exception == null && __state?.OnlyProtectedEntries == true &&
                pawn?.CurJobDef?.defName == "UnloadYourHauledInventory")
            {
                // FirstUnloadableThing is called from the unload driver's target-selection
                // toil. If every indexed item is protected, terminate this attempt after the
                // helper returns; otherwise the three-tick loop spins until the claim expires.
                try
                {
                    pawn.jobs?.curDriver?.EndJobWith(JobCondition.Succeeded);
                }
                catch (Exception exception)
                {
                    Log.WarningOnce("[Search and Rescue] Could not stop an empty Pick Up And Haul " +
                                    "unload pass. " + exception.GetBaseException().Message,
                        196320756);
                }
            }
            return __exception;
        }
    }

    [HarmonyPatch]
    internal static class PickUpAndHaulUnloadChecker_SearchAndRescueFieldSupplyPatch
    {
        private static Type CheckerType => AccessTools.TypeByName("PickUpAndHaul.PawnUnloadChecker");
        private static Type HaulCompType => AccessTools.TypeByName("PickUpAndHaul.CompHauledToInventory");

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(CheckerType, "CheckIfPawnShouldUnloadInventory");
        }

        private static bool Prepare()
        {
            return TargetMethod() != null && HaulCompType != null;
        }

        private static bool Prefix(Pawn pawn)
        {
            try
            {
                ThingComp comp = pawn?.AllComps?.FirstOrDefault(candidate =>
                    candidate != null && HaulCompType.IsInstanceOfType(candidate));
                MethodInfo getHashSet = comp == null
                    ? null
                    : AccessTools.Method(HaulCompType, "GetHashSet");
                if (!(getHashSet?.Invoke(comp, null) is IEnumerable<Thing> indexed))
                {
                    return true;
                }

                // Do not enqueue a three-tick unload loop when every PUAH-indexed item is still
                // owned by a battlefield claim. Once any reference expires this check naturally
                // permits PUAH to unload that item again.
                return indexed.Any(thing =>
                    thing != null && !thing.Destroyed &&
                    !SearchAndRescueJobContext.IsProtectedOrClaimedMedicalSupply(thing));
            }
            catch (Exception exception)
            {
                Log.WarningOnce("[Search and Rescue] Pick Up And Haul unload inventory API changed; " +
                                "falling back to its native checker. " +
                                exception.GetBaseException().Message,
                    196320755);
                return true;
            }
        }
    }

    /// <summary>
    /// Allies are Helpful inserts autonomous TendPatient/Rescue jobs directly into an
    /// allied pawn's queue from Pawn.TickRare. Those jobs bypass WorkGiver arbitration,
    /// so a marked casualty can otherwise be claimed by SAR and by an ally at the same
    /// time. Keep the third-party behavior for every unmarked patient, but remove its
    /// unforced queued duplicate when SAR owns the corresponding treatment/transport
    /// stage. Active jobs remain covered by the common external-owner lease.
    /// </summary>
    [HarmonyPatch]
    internal static class AlliesAreHelpful_SearchAndRescueQueueOwnershipPatch
    {
        private static Type PatchType => AccessTools.TypeByName("PawnTendAndRescuePatch");

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(PatchType, "Postfix", new[] { typeof(Pawn) });
        }

        private static bool Prepare()
        {
            return TargetMethod() != null;
        }

        private static void Postfix(Pawn __instance)
        {
            JobQueue queue = __instance?.jobs?.jobQueue;
            if (queue == null)
            {
                return;
            }

            queue.RemoveAll(__instance, job =>
            {
                if (job?.def == null || job.playerForced)
                {
                    return false;
                }

                PatientJobRole roles = CompatibilityRegistry.RolesFor(job.def) &
                                       (PatientJobRole.Treatment | PatientJobRole.Transport);
                if (roles == PatientJobRole.None)
                {
                    return false;
                }

                Pawn patient = CompatibilityRegistry.PatientFor(__instance, job, roles);
                return patient != null &&
                       PatientWorkOwnership.HasManagedOrderForRole(patient, roles);
            });
        }
    }
}
