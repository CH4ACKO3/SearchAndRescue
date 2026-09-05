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
        private static Type priorityTreatmentMapComponentType;
        private static FieldInfo tendablePawnsField;
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
                priorityTreatmentMapComponentType = priorityTreatmentType.Assembly
                    .GetType("TKS_PriorityTreatment.MapComponent_PriorityTreatment", false);
                tendablePawnsField = priorityTreatmentMapComponentType?.GetField(
                    "tendablePawns",
                    BindingFlags.Public | BindingFlags.Instance);
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
            // PotentialPatientsGlobal filters future PTR cache rebuilds, but a pawn may become
            // SAR-managed between rebuilds. Remove those stale entries before PTR's original
            // method can end the doctor's current job and select one of them.
            RemoveManagedPatientsFromPriorityTreatmentCache(pawn?.Map);

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
            if (BlocksManagedWakeup(current.def, PriorityTreatmentAllowsWakeUp()))
            {
                return false;
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

        internal static bool BlocksManagedWakeup(JobDef currentJob, bool wakeUpToTend)
        {
            return !wakeUpToTend && IsSleepingJob(currentJob);
        }

        private static bool IsSleepingJob(JobDef jobDef)
        {
            return jobDef == JobDefOf.Wait_WithSleeping || jobDef == JobDefOf.Wait_Asleep ||
                   jobDef == JobDefOf.LayDownResting || jobDef == JobDefOf.LayDown;
        }

        private static void RemoveManagedPatientsFromPriorityTreatmentCache(Map map)
        {
            if (map == null || priorityTreatmentMapComponentType == null || tendablePawnsField == null)
            {
                return;
            }

            try
            {
                MapComponent component = map.GetComponent(priorityTreatmentMapComponentType);
                if (component == null || !(tendablePawnsField.GetValue(component) is IList<Pawn> patients))
                {
                    return;
                }

                for (int index = patients.Count - 1; index >= 0; index--)
                {
                    if (SearchAndRescueJobContext.HasManagedTreatmentOrder(patients[index]))
                    {
                        patients.RemoveAt(index);
                    }
                }
            }
            catch (Exception exception)
            {
                Log.WarningOnce("[Search and Rescue] Could not filter Priority Treatment's cached patients. " +
                                exception.GetBaseException().Message, 196320757);
            }
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
                EnsurePriorityTreatmentSettings();
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

        private static bool PriorityTreatmentAllowsWakeUp()
        {
            try
            {
                EnsurePriorityTreatmentSettings();
                FieldInfo wakeUpToTend = priorityTreatmentSettingsType?.GetField(
                    "wakeUpToTend",
                    BindingFlags.Public | BindingFlags.Instance);
                return wakeUpToTend?.GetValue(priorityTreatmentSettings) as bool? ?? false;
            }
            catch
            {
                // Avoid waking a sleeping responder when a future PTR release changes its
                // settings implementation and the user's preference cannot be read.
                return false;
            }
        }

        private static void EnsurePriorityTreatmentSettings()
        {
            if (priorityTreatmentSettings != null || priorityTreatmentSettingsType == null)
            {
                return;
            }

            MethodInfo getMod = typeof(LoadedModManager).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(method => method.Name == "GetMod" && method.IsGenericMethodDefinition &&
                                          method.GetParameters().Length == 0);
            object mod = getMod?.MakeGenericMethod(priorityTreatmentType).Invoke(null, null);
            MethodInfo getSettings = typeof(Mod).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(method => method.Name == "GetSettings" && method.IsGenericMethodDefinition &&
                                          method.GetParameters().Length == 0);
            priorityTreatmentSettings = getSettings?.MakeGenericMethod(priorityTreatmentSettingsType)
                .Invoke(mod, null);
        }
    }
}
