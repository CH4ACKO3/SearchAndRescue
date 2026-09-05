using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SearchAndRescue
{
    [HarmonyPatch(typeof(Pawn_DraftController), nameof(Pawn_DraftController.Drafted), MethodType.Setter)]
    internal static class PawnDraftController_SearchAndRescueSchedulePatch
    {
        private static void Prefix(Pawn_DraftController __instance, bool value)
        {
            Pawn pawn = __instance?.pawn;
            if (value || pawn?.Drafted != true)
            {
                return;
            }

            pawn.MapHeld?.GetComponent<SearchAndRescueCoordinator>()?.NotifyWorkerUndrafting(pawn);
        }
    }

    internal static class SearchAndRescueJobContext
    {
        public static bool IsActive(Pawn pawn, Job job, SearchAndRescueStage? stage = null)
        {
            return pawn?.Map?.GetComponent<SearchAndRescueCoordinator>()?.IsActiveJob(pawn, job, stage) == true;
        }

        public static bool HasManagedBattlefieldOrder(Pawn patient)
        {
            Map map = patient?.MapHeld;
            return map != null &&
                   (map.designationManager.DesignationOn(patient, SearchAndRescueDefOf.SAR_Capture) != null ||
                    map.designationManager.DesignationOn(patient, SearchAndRescueDefOf.SAR_Treat) != null ||
                    map.designationManager.DesignationOn(patient, SearchAndRescueDefOf.SAR_Rescue) != null);
        }

        public static bool HasManagedTreatmentOrder(Pawn patient)
        {
            Map map = patient?.MapHeld;
            if (map == null)
            {
                return false;
            }

            if (map.designationManager.DesignationOn(patient, SearchAndRescueDefOf.SAR_Treat) != null)
            {
                return true;
            }

            if (map.GetComponent<SearchAndRescueCoordinator>()?.OwnsAutonomousTreatment(patient) == true)
            {
                return true;
            }

            // Rescue is transport ownership, not treatment ownership. A rescue-only mark
            // must therefore remain visible to vanilla doctors and Priority Treatment.
            // Capture is the exception while the target is still hostile: allowing an
            // autonomous tend to revive an uncaptured enemy can immediately put the colony
            // back in combat. Once the pawn is actually a prisoner, normal doctor work is
            // safe again unless an explicit SAR_Treat mark still owns treatment.
            return map.designationManager.DesignationOn(patient, SearchAndRescueDefOf.SAR_Capture) != null &&
                   !patient.IsPrisonerOfColony &&
                   patient.HostileTo(Faction.OfPlayer);
        }

        public static bool HasManagedCaptureOrder(Pawn patient)
        {
            Map map = patient?.MapHeld;
            return map != null &&
                   map.designationManager.DesignationOn(
                       patient,
                       SearchAndRescueDefOf.SAR_Capture) != null;
        }

        public static bool HasManagedTransportOrder(Pawn patient)
        {
            Map map = patient?.MapHeld;
            return map != null &&
                   (map.designationManager.DesignationOn(
                        patient,
                        SearchAndRescueDefOf.SAR_Rescue) != null ||
                    map.designationManager.DesignationOn(
                        patient,
                        SearchAndRescueDefOf.SAR_Capture) != null ||
                    map.GetComponent<SearchAndRescueCoordinator>()?.OwnsAutonomousTransport(patient) == true);
        }

        public static bool IsPatientTakeToBedJob(Job job)
        {
            return job != null && CompatibilityRegistry.HasRole(
                       job.def,
                       PatientJobRole.Transport) &&
                   CompatibilityRegistry.PatientFor(null, job, PatientJobRole.Transport) != null;
        }

        public static bool ShouldWaitForFieldTreatment(Pawn worker, Pawn patient, Job standbyJob)
        {
            return patient?.MapHeld?.GetComponent<SearchAndRescueCoordinator>()
                ?.ShouldContinueStandby(worker, patient, standbyJob) == true;
        }

        public static bool IsProtectedFieldSupply(Thing thing)
        {
            Map map = thing?.MapHeld ?? MedicalResourceLedger.InventoryHolder(thing)?.MapHeld;
            return map?.GetComponent<SearchAndRescueCoordinator>()
                ?.IsProtectedFieldSupply(thing) == true;
        }

        public static bool IsClaimedMedicalSupply(Thing thing)
        {
            Map map = thing?.MapHeld ?? MedicalResourceLedger.InventoryHolder(thing)?.MapHeld;
            return map?.GetComponent<SearchAndRescueCoordinator>()
                ?.IsClaimedMedicalSupply(thing) == true;
        }

        public static bool IsProtectedOrClaimedMedicalSupply(Thing thing)
        {
            return IsProtectedFieldSupply(thing) || IsClaimedMedicalSupply(thing);
        }
    }

    [HarmonyPatch(typeof(HaulAIUtility), nameof(HaulAIUtility.PawnCanAutomaticallyHaulFast))]
    [HarmonyPriority(Priority.First)]
    internal static class HaulAIUtility_SearchAndRescueFieldSupplyPatch
    {
        private static bool Prefix(Thing t, bool forced, ref bool __result)
        {
            if (forced || !SearchAndRescueJobContext.IsProtectedOrClaimedMedicalSupply(t))
            {
                return true;
            }

            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(HaulAIUtility), nameof(HaulAIUtility.PawnCanAutomaticallyHaul))]
    [HarmonyPriority(Priority.First)]
    internal static class HaulAIUtilityGeneral_SearchAndRescueFieldSupplyPatch
    {
        private static bool Prefix(Thing t, bool forced, ref bool __result)
        {
            if (forced || !SearchAndRescueJobContext.IsProtectedOrClaimedMedicalSupply(t))
            {
                return true;
            }

            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(HaulAIUtility), "PawnCanAutomaticallyHaulFast_NewTemp")]
    [HarmonyPriority(Priority.First)]
    internal static class HaulAIUtilityNewTemp_SearchAndRescueFieldSupplyPatch
    {
        private static bool Prefix(Thing t, bool forced, ref bool __result)
        {
            if (forced || !SearchAndRescueJobContext.IsProtectedOrClaimedMedicalSupply(t))
            {
                return true;
            }

            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(WorkGiver_HaulGeneral), nameof(WorkGiver_HaulGeneral.JobOnThing))]
    [HarmonyPriority(Priority.First)]
    internal static class WorkGiverHaulGeneral_SearchAndRescueFieldSupplyPatch
    {
        private static bool Prefix(Thing t, bool forced, ref Job __result)
        {
            if (forced || !SearchAndRescueJobContext.IsProtectedOrClaimedMedicalSupply(t))
            {
                return true;
            }

            __result = null;
            return false;
        }
    }

    [HarmonyPatch(typeof(HealthAIUtility), nameof(HealthAIUtility.CanRescueNow))]
    [HarmonyPriority(Priority.First)]
    internal static class HealthAIUtility_SearchAndRescueStageOrderPatch
    {
        private static void Postfix(Pawn patient, bool forced, ref bool __result)
        {
            if (__result && !forced && SearchAndRescueJobContext.HasManagedTransportOrder(patient))
            {
                // Ordinary colonist work, trained-animal ThinkTrees, and rescue mods that use
                // the vanilla predicate must leave managed casualties to the stage scheduler.
                // An explicit player rescue order remains an intentional override.
                __result = false;
            }
        }
    }

    // Hospitality replaces the guest branch without calling CanRescueNow. Gate the
    // scanner result as well, including when a third-party prefix skips the original.
    [HarmonyPatch(typeof(WorkGiver_RescueDowned), nameof(WorkGiver_RescueDowned.HasJobOnThing))]
    internal static class WorkGiverRescueDowned_SearchAndRescueOrderPatch
    {
        private static void Postfix(Thing t, bool forced, ref bool __result)
        {
            if (__result && !forced && t is Pawn patient &&
                SearchAndRescueJobContext.HasManagedTransportOrder(patient))
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(WorkGiver_RescueDowned), nameof(WorkGiver_RescueDowned.JobOnThing))]
    internal static class WorkGiverRescueDownedJob_SearchAndRescueOrderPatch
    {
        private static bool Prefix(Thing t, bool forced, ref Job __result)
        {
            if (forced || !(t is Pawn patient) ||
                !SearchAndRescueJobContext.HasManagedTransportOrder(patient)) return true;
            __result = null;
            return false;
        }
    }

    [HarmonyPatch(typeof(WorkGiver_Warden_TakeToBed), nameof(WorkGiver_Warden_TakeToBed.TryMakeJob))]
    [HarmonyPriority(Priority.First)]
    internal static class WorkGiverWardenTakeToBed_SearchAndRescueStageOrderPatch
    {
        private static bool Prefix(Thing t, bool forced, ref Job __result)
        {
            if (forced || !(t is Pawn patient) ||
                !SearchAndRescueJobContext.HasManagedBattlefieldOrder(patient))
            {
                return true;
            }

            // Do not let autonomous wardens construct a TakeWoundedPrisonerToBed job for a
            // casualty owned by the combined scheduler. Blocking only in the job driver's
            // reservation hook is too late: StartJob treats that rejection as an error and
            // puts the warden into a finite recovery Wait, producing a retry/wait loop.
            // Forced calls are kept for explicit player orders and ritual cleanup.
            __result = null;
            return false;
        }
    }

    [HarmonyPatch(typeof(WorkGiver_Tend), nameof(WorkGiver_Tend.HasJobOnThing))]
    [HarmonyAfter("uuugggg.rimworld.SmartMedicine.main")]
    internal static class WorkGiverTend_SearchAndRescueMarkedPatientPatch
    {
        private static void Postfix(Thing t, bool forced, ref bool __result)
        {
            if (__result && !forced && t is Pawn patient &&
                SearchAndRescueJobContext.HasManagedTreatmentOrder(patient))
            {
                // A marked casualty is owned by the combined scheduler. Allowing the
                // ordinary urgent-tend scanner to race it can rebuild a TendPatient job
                // every tick when a protected field-medicine stack is involved.
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(WorkGiver_Tend), nameof(WorkGiver_Tend.JobOnThing))]
    [HarmonyPriority(Priority.First)]
    [HarmonyBefore("uuugggg.rimworld.SmartMedicine.main")]
    internal static class WorkGiverTendJob_SearchAndRescueMarkedPatientPatch
    {
        private static bool Prefix(Thing t, bool forced, ref Job __result)
        {
            if (forced || !(t is Pawn patient) ||
                !SearchAndRescueJobContext.HasManagedTreatmentOrder(patient))
            {
                return true;
            }

            __result = null;
            return false;
        }
    }

    [HarmonyPatch(typeof(JobDriver_TendPatient), nameof(JobDriver_TendPatient.TryMakePreToilReservations))]
    [HarmonyPriority(Priority.First)]
    internal static class JobDriverTendPatient_SearchAndRescueOwnershipPatch
    {
        private static bool Prefix(Pawn ___pawn, Job ___job, ref bool __result)
        {
            if (___job == null || ___job.playerForced ||
                SearchAndRescueJobContext.IsActive(___pawn, ___job, SearchAndRescueStage.Treat) ||
                !SearchAndRescueJobContext.HasManagedTreatmentOrder(___job.targetA.Pawn))
            {
                return true;
            }

            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Toils_Haul), nameof(Toils_Haul.CheckForGetOpportunityDuplicate))]
    [HarmonyPriority(Priority.First)]
    internal static class ToilsHaul_SearchAndRescueTendMedicineQuotaPatch
    {
        private static void Postfix(Toil __result, TargetIndex haulableInd)
        {
            if (__result?.initAction == null)
            {
                return;
            }

            Action original = __result.initAction;
            __result.initAction = () =>
            {
                Pawn actor = __result.actor;
                Job job = actor?.CurJob;
                if (job?.def != JobDefOf.TendPatient ||
                    !SearchAndRescueJobContext.IsActive(actor, job, SearchAndRescueStage.Treat))
                {
                    original();
                    return;
                }

                Thing medicine = job.GetTarget(haulableInd).Thing;
                if (medicine == null || medicine.Destroyed)
                {
                    // Another shared-stack user may consume the final physical unit between
                    // graph validation and this arrival boundary. The vanilla opportunity-
                    // duplicate toil dereferences the now-empty target. Retire the stale job
                    // normally so the managed-job callback can release its lease and rematch.
                    actor.jobs.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }

                if (SearchAndRescueJobContext.IsProtectedOrClaimedMedicalSupply(medicine))
                {
                    // This vanilla optimization may append adjacent duplicates after the SAR
                    // graph has claimed an exact medicine quota. Skipping it preserves the
                    // job.count lease and prevents one doctor from vacuuming units referenced
                    // by another casualty. StartCarryThing follows and takes the claimed count.
                    return;
                }

                original();
            };
        }
    }

    [HarmonyPatch(typeof(CompForbiddable), "set_Forbidden")]
    internal static class CompForbiddable_SearchAndRescueFieldSupplyPatch
    {
        private static void Postfix(CompForbiddable __instance)
        {
            Thing supply = __instance?.parent;
            supply?.MapHeld?.GetComponent<SearchAndRescueCoordinator>()
                ?.NotifyFieldSupplyForbiddenChanged(supply);
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.Destroy))]
    internal static class ThingDestroy_SearchAndRescueLifecyclePatch
    {
        private static void Prefix(Thing __instance, out SearchAndRescueCoordinator __state)
        {
            __state = (__instance is Building_Bed ||
                       SearchAndRescueJobContext.IsProtectedOrClaimedMedicalSupply(__instance))
                ? __instance?.MapHeld?.GetComponent<SearchAndRescueCoordinator>()
                : null;
        }

        private static void Postfix(Thing __instance, SearchAndRescueCoordinator __state)
        {
            if (__state == null)
            {
                return;
            }

            if (__instance is Building_Bed bed)
            {
                __state.NotifyPatientBedDestroyed(bed);
            }
            else
            {
                __state.NotifyMedicalSupplyDestroyed(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(TendUtility), nameof(TendUtility.DoTend))]
    [HarmonyPriority(Priority.Last)]
    internal static class TendUtility_SearchAndRescueCommittedRoundPatch
    {
        private static void Postfix(Pawn doctor, Pawn patient)
        {
            doctor?.Map?.GetComponent<SearchAndRescueCoordinator>()
                ?.NotifyTreatmentCommitted(doctor, patient);
        }
    }

    [HarmonyPatch(typeof(JobDriver_TakeToBed), nameof(JobDriver_TakeToBed.TryMakePreToilReservations))]
    [HarmonyPriority(Priority.First)]
    internal static class JobDriverTakeToBed_SearchAndRescueStageOrderPatch
    {
        private static bool Prefix(Pawn ___pawn, Job ___job, ref bool __result)
        {
            if (SearchAndRescueJobContext.IsActive(___pawn, ___job, SearchAndRescueStage.Rescue) &&
                ___job.count < 1)
            {
                // Defensive normalization for a job restored from an older save or built by
                // a compatible provider before the coordinator's constructor guard existed.
                ___job.count = 1;
            }

            if (!SearchAndRescueJobContext.IsPatientTakeToBedJob(___job) || ___job.playerForced ||
                SearchAndRescueJobContext.IsActive(___pawn, ___job, SearchAndRescueStage.Rescue))
            {
                return true;
            }

            Pawn patient = ___job.targetA.Pawn;
            if (!SearchAndRescueJobContext.HasManagedTransportOrder(patient))
            {
                return true;
            }

            // Safety net for every vanilla/third-party job that actually uses the
            // TakeToBed driver: Rescue, Capture, TakeWoundedPrisonerToBed, and compatible
            // aliases do not all consult HealthAIUtility.CanRescueNow before construction.
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), "TryOpportunisticJob")]
    [HarmonyPriority(Priority.First)]
    internal static class PawnJobTracker_OpportunisticWorkCompatibilityPatch
    {
        private static bool Prefix(Pawn ___pawn, Job finalizerJob, ref Job __result)
        {
            if (!SearchAndRescueJobContext.IsActive(___pawn, finalizerJob))
            {
                return true;
            }

            // Also bypasses transpilers such as While You're Up for this one emergency trip.
            __result = null;
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.TryTakeOrderedJob))]
    [HarmonyPriority(Priority.First)]
    internal static class PawnJobTracker_SearchAndRescuePlayerOverridePatch
    {
        private static void Prefix(Pawn ___pawn, Job job)
        {
            ___pawn?.Map?.GetComponent<SearchAndRescueCoordinator>()
                ?.NotifyPlayerOrderedPatientJob(___pawn, job);
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.ClearQueuedJobs))]
    internal static class PawnJobTracker_SearchAndRescuePlayerQueueReleasePatch
    {
        private static void Prefix(Pawn ___pawn, out List<Pawn> __state)
        {
            __state = null;
            JobQueue queue = ___pawn?.jobs?.jobQueue;
            if (queue == null || queue.Count == 0)
            {
                return;
            }

            foreach (QueuedJob queuedJob in queue)
            {
                Job job = queuedJob?.job;
                if (job?.playerForced != true)
                {
                    continue;
                }

                Pawn patient = CompatibilityRegistry.PatientFor(___pawn, job);
                if (patient == null ||
                    (!SearchAndRescueJobContext.HasManagedBattlefieldOrder(patient) &&
                     !SearchAndRescueJobContext.HasManagedTreatmentOrder(patient)))
                {
                    continue;
                }

                __state ??= new List<Pawn>();
                if (!__state.Contains(patient))
                {
                    __state.Add(patient);
                }
            }
        }

        private static void Postfix(List<Pawn> __state)
        {
            if (__state == null)
            {
                return;
            }

            foreach (Pawn patient in __state)
            {
                patient?.MapHeld?.GetComponent<SearchAndRescueCoordinator>()
                    ?.NotifyPlayerPatientQueueReleased(patient);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.EndCurrentJob))]
    [HarmonyAfter("net.avilmask.rimworld.mod.CommonSense")]
    internal static class PawnJobTracker_CommonSenseCompatibilityPatch
    {
        private static void Prefix(Pawn ___pawn, JobCondition condition)
        {
            if (condition != JobCondition.Succeeded ||
                !SearchAndRescueJobContext.IsActive(___pawn, ___pawn?.CurJob, SearchAndRescueStage.Treat))
            {
                return;
            }

            // Common Sense may have just queued an unforced cleaning job after tending.
            // Remove only that generated job; explicit player-queued work is preserved.
            ___pawn.jobs.jobQueue.RemoveAll(___pawn,
                queuedJob => queuedJob.def == JobDefOf.Clean && !queuedJob.playerForced);
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.EndCurrentJob))]
    internal static class PawnJobTracker_SearchAndRescueSchedulePatch
    {
        private static void Prefix(Pawn ___pawn, JobCondition condition, out Job __state)
        {
            __state = ___pawn?.CurJob;
            ___pawn?.Map?.GetComponent<SearchAndRescueCoordinator>()
                ?.NotifyManagedJobEnding(___pawn, __state, condition);
        }

        private static void Postfix(Pawn ___pawn, JobCondition condition, Job __state)
        {
            SearchAndRescueCoordinator coordinator = ___pawn?.Map?.GetComponent<SearchAndRescueCoordinator>();
            coordinator?.NotifyManagedJobEnded(___pawn, __state, condition);
            coordinator?.NotifyExternalPatientJobEnded(___pawn, __state);
            coordinator?.NotifyRoutineWorkBoundary(___pawn, __state, condition);
        }
    }

    [HarmonyPatch(typeof(DesignationManager), nameof(DesignationManager.RemoveDesignation))]
    internal static class DesignationManager_SearchAndRescueSchedulePatch
    {
        private static void Postfix(Designation __0)
        {
            Designation designation = __0;
            if (!(designation?.target.Thing is Pawn pawn) ||
                designation.def != SearchAndRescueDefOf.SAR_Capture &&
                designation.def != SearchAndRescueDefOf.SAR_Treat &&
                designation.def != SearchAndRescueDefOf.SAR_Rescue)
            {
                return;
            }

            SearchAndRescueStage stage = designation.def == SearchAndRescueDefOf.SAR_Capture
                ? SearchAndRescueStage.Capture
                : designation.def == SearchAndRescueDefOf.SAR_Treat
                    ? SearchAndRescueStage.Treat
                    : SearchAndRescueStage.Rescue;
            pawn.MapHeld?.GetComponent<SearchAndRescueCoordinator>()
                ?.NotifyStageDesignationRemoved(pawn, stage);
        }
    }

    [HarmonyPatch(typeof(JobGiver_RescueNearby), "TryGiveJob")]
    internal static class JobGiverRescueNearby_SearchAndRescueAnimalPatch
    {
        private static bool Prefix(Pawn pawn, ref Job __result)
        {
            if (!Compatibility.IsTrainedRescueAnimal(pawn) || pawn.Downed || pawn.InMentalState)
            {
                return true;
            }

            Job searchAndRescueJob = pawn.Map?.GetComponent<SearchAndRescueCoordinator>()
                ?.TryIssueJob(pawn, SearchAndRescueStage.Rescue, RescueWorkProvider.Animal);
            if (searchAndRescueJob == null)
            {
                return true;
            }

            __result = searchAndRescueJob;
            return false;
        }
    }
}
