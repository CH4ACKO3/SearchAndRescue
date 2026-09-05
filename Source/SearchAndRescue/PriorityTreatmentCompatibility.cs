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
    internal static class PriorityTreatmentCompatibility
    {
        private static Type priorityTreatmentType;
        private static Type priorityTreatmentSettingsType;
        private static IList<string> doctorWorkDefs;
        private static object priorityTreatmentSettings;

        internal static void Install(Harmony harmony)
        {
            priorityTreatmentType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("TKS_PriorityTreatment.TKS_PriorityTreatment", false))
                .FirstOrDefault(type => type != null);
            if (priorityTreatmentType == null)
            {
                return;
            }

            try
            {
                priorityTreatmentSettingsType = priorityTreatmentType.Assembly
                    .GetType("TKS_PriorityTreatment.TKS_PriorityTreatmentSettings", false);
                doctorWorkDefs = priorityTreatmentType.GetField(
                        "doctorWorkDefs",
                        BindingFlags.Public | BindingFlags.Static)
                    ?.GetValue(null) as IList<string>;

                MethodInfo potentialPatients = priorityTreatmentType.GetMethod(
                    "PotentialPatientsGlobal",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(Map) },
                    null);
                MethodInfo makePriorityJob = priorityTreatmentType.GetMethod(
                    "MakePriorityTreatmentJob",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[]
                    {
                        typeof(Pawn),
                        typeof(Pawn).MakeByRefType(),
                        typeof(Job).MakeByRefType(),
                        typeof(string)
                    },
                    null);
                if (potentialPatients == null || makePriorityJob == null)
                {
                    Log.Warning("[Search and Rescue] Priority Treatment was found, but its 1.6 integration points " +
                                "were not available. Falling back to the normal JobDriver safety checks.");
                    return;
                }

                harmony.Patch(
                    potentialPatients,
                    postfix: new HarmonyMethod(typeof(PriorityTreatmentCompatibility),
                        nameof(FilterManagedPatientsPostfix)));
                harmony.Patch(
                    makePriorityJob,
                    prefix: new HarmonyMethod(typeof(PriorityTreatmentCompatibility),
                        nameof(RouteManagedTreatmentPrefix)));
                Log.Message("[Search and Rescue] Priority Treatment scheduler bridge installed.");
            }
            catch (Exception exception)
            {
                Log.Warning("[Search and Rescue] Priority Treatment scheduler bridge failed; using late job safety " +
                            "checks only. " + exception.GetBaseException().Message);
            }
        }

        private static void FilterManagedPatientsPostfix(ref IEnumerable<Thing> __result)
        {
            if (__result != null)
            {
                __result = UnmanagedPatients(__result);
            }
        }

        private static IEnumerable<Thing> UnmanagedPatients(IEnumerable<Thing> patients)
        {
            foreach (Thing patient in patients)
            {
                if (!(patient is Pawn pawn) ||
                    !SearchAndRescueJobContext.HasManagedTreatmentOrder(pawn))
                {
                    yield return patient;
                }
            }
        }

        private static bool RouteManagedTreatmentPrefix(
            Pawn pawn,
            ref Pawn sickPawn,
            ref Job queuedJob,
            ref Job __result)
        {
            if (!CanRequestManagedOverride(pawn))
            {
                return true;
            }

            SearchAndRescueCoordinator coordinator = pawn.Map?.GetComponent<SearchAndRescueCoordinator>();
            Pawn managedTarget = null;
            Job managedJob = coordinator?.TryIssuePriorityTreatmentOverride(pawn, out managedTarget);
            if (managedJob == null || managedTarget == null)
            {
                return true;
            }

            // PTR normally ends the current interruptible job inside MakePriorityTreatmentJob.
            // Our prefix bypasses that body, so mirror the transition only after the graph has
            // successfully produced a tracked SAR job.
            if (pawn.CurJob != null)
            {
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, false);
            }

            sickPawn = managedTarget;
            queuedJob = null;
            __result = managedJob;
            return false;
        }

        private static bool CanRequestManagedOverride(Pawn pawn)
        {
            if (pawn == null || pawn.Map == null || pawn.jobs == null || pawn.Dead || pawn.Downed ||
                pawn.InMentalState || !(pawn.IsColonistPlayerControlled || pawn.IsColonyMechPlayerControlled))
            {
                return false;
            }

            if (pawn.jobs.jobQueue != null &&
                (pawn.jobs.jobQueue.AnyPlayerForced || pawn.jobs.jobQueue.Count != 0) ||
                pawn.mindState?.duty != null)
            {
                return false;
            }

            Job current = pawn.CurJob;
            if (current == null)
            {
                return !ShouldEatBeforeTreatment(pawn);
            }
            if (current.playerForced ||
                pawn.Map.GetComponent<SearchAndRescueCoordinator>()?.IsActiveJob(pawn, current) == true ||
                doctorWorkDefs?.Contains(current.def?.defName) == true ||
                current.def == JobDefOf.Ingest && PriorityTreatmentAllowsEating() ||
                pawn.jobs.curDriver?.PlayerInterruptable != true)
            {
                return false;
            }

            return !ShouldEatBeforeTreatment(pawn);
        }

        private static bool ShouldEatBeforeTreatment(Pawn pawn)
        {
            return PriorityTreatmentAllowsEating() && pawn.needs?.food != null &&
                   pawn.needs.food.CurCategory >= HungerCategory.UrgentlyHungry;
        }

        private static bool PriorityTreatmentAllowsEating()
        {
            try
            {
                if (priorityTreatmentSettings == null && priorityTreatmentSettingsType != null)
                {
                    MethodInfo getMod = typeof(LoadedModManager).GetMethods(
                            BindingFlags.Public | BindingFlags.Static)
                        .FirstOrDefault(method => method.Name == "GetMod" &&
                                                  method.IsGenericMethodDefinition &&
                                                  method.GetParameters().Length == 0);
                    object mod = getMod?.MakeGenericMethod(priorityTreatmentType).Invoke(null, null);
                    MethodInfo getSettings = typeof(Mod).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(method => method.Name == "GetSettings" &&
                                                  method.IsGenericMethodDefinition &&
                                                  method.GetParameters().Length == 0);
                    priorityTreatmentSettings = getSettings?.MakeGenericMethod(priorityTreatmentSettingsType)
                        .Invoke(mod, null);
                }

                FieldInfo allowEating = priorityTreatmentSettingsType?.GetField(
                    "allowEating",
                    BindingFlags.Public | BindingFlags.Instance);
                return allowEating?.GetValue(priorityTreatmentSettings) as bool? ?? true;
            }
            catch
            {
                // Preserve the safer/default PTR behavior when another release changes its
                // settings implementation.
                return true;
            }
        }
    }
}
