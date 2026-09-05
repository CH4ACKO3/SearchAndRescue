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
    internal static class PriorityTreatmentRegressionDiagnostics
    {
        [DebugAction("Search and Rescue", "Run Priority Treatment wake-up regressions",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Run()
        {
            RunPolicyChecks();
            Type priorityTreatmentType = AccessTools.TypeByName("TKS_PriorityTreatment.TKS_PriorityTreatment");
            Type componentType = AccessTools.TypeByName("TKS_PriorityTreatment.MapComponent_PriorityTreatment");
            Type settingsType = AccessTools.TypeByName("TKS_PriorityTreatment.TKS_PriorityTreatmentSettings");
            if (priorityTreatmentType == null || componentType == null || settingsType == null)
            {
                Log.Message("[SAR Priority Treatment regression] SKIP: Priority Treatment is not loaded");
                return;
            }

            Map map = Find.CurrentMap;
            Pawn worker = null;
            Pawn managedPatient = null;
            Pawn ordinaryPatient = null;
            Designation treatmentDesignation = null;
            IList<Pawn> cachedPatients = null;
            List<Pawn> originalPatients = null;
            FieldInfo wakeUpField = null;
            object settings = null;
            bool originalWakeUp = false;
            bool wakeUpCaptured = false;
            try
            {
                MapComponent component = map.GetComponent(componentType);
                FieldInfo patientsField = AccessTools.Field(componentType, "tendablePawns");
                if (component == null || patientsField == null)
                {
                    Log.Message("[SAR Priority Treatment regression] SKIP: live patient cache unavailable");
                    return;
                }
                cachedPatients = patientsField.GetValue(component) as IList<Pawn>;
                if (cachedPatients == null)
                {
                    Log.Message("[SAR Priority Treatment regression] SKIP: live patient cache unavailable");
                    return;
                }

                settings = GetSettings(priorityTreatmentType, settingsType);
                wakeUpField = AccessTools.Field(settingsType, "wakeUpToTend");
                if (settings == null || wakeUpField == null)
                {
                    Log.Message("[SAR Priority Treatment regression] SKIP: live wake-up setting unavailable");
                    return;
                }

                originalPatients = cachedPatients.ToList();
                originalWakeUp = (bool)wakeUpField.GetValue(settings);
                wakeUpCaptured = true;
                worker = SpawnColonist(map, -4);
                managedPatient = SpawnColonist(map, 0);
                ordinaryPatient = SpawnColonist(map, 4);
                treatmentDesignation = new Designation(managedPatient, SearchAndRescueDefOf.SAR_Treat);
                map.designationManager.AddDesignation(treatmentDesignation);

                cachedPatients.Clear();
                cachedPatients.Add(managedPatient);
                cachedPatients.Add(ordinaryPatient);
                InvokeCompatibility("RemoveManagedPatientsFromPriorityTreatmentCache", map);
                Check(cachedPatients.Count == 1 && cachedPatients[0] == ordinaryPatient,
                    "live PTR cache removes only the SAR-managed patient");

                Job sleepJob = JobMaker.MakeJob(JobDefOf.LayDown, worker.Position);
                worker.jobs.curJob = sleepJob;
                worker.jobs.curDriver = new DiagnosticJobDriver
                {
                    pawn = worker,
                    job = sleepJob,
                    asleep = true
                };
                if (worker.needs?.food != null)
                {
                    worker.needs.food.CurLevelPercentage = 1f;
                }

                wakeUpField.SetValue(settings, false);
                Check(!CanRequestManagedOverride(worker),
                    "live false wake-up setting blocks a sleeping managed override");
                wakeUpField.SetValue(settings, true);
                Check(CanRequestManagedOverride(worker),
                    "live true wake-up setting permits a sleeping managed override");
                sleepJob.playerForced = true;
                Check(!CanRequestManagedOverride(worker),
                    "player-forced sleeping job remains protected when wake-up is enabled");
            }
            catch (Exception exception)
            {
                Log.Error("[SAR Priority Treatment regression] ERROR: " + exception.GetBaseException());
            }
            finally
            {
                if (worker?.jobs != null)
                {
                    worker.jobs.curDriver = null;
                    worker.jobs.curJob = null;
                }
                if (settings != null && wakeUpField != null && wakeUpCaptured)
                {
                    wakeUpField.SetValue(settings, originalWakeUp);
                }
                if (cachedPatients != null && originalPatients != null)
                {
                    cachedPatients.Clear();
                    foreach (Pawn patient in originalPatients)
                    {
                        cachedPatients.Add(patient);
                    }
                }
                if (treatmentDesignation != null)
                {
                    map.designationManager.RemoveDesignation(treatmentDesignation);
                }
                Destroy(worker);
                Destroy(managedPatient);
                Destroy(ordinaryPatient);
            }
        }

        private static void RunPolicyChecks()
        {
            Check(PriorityTreatmentCompatibility.BlocksManagedWakeup(JobDefOf.Wait_WithSleeping, false),
                "sleeping wait is protected when wake-up is disabled");
            Check(PriorityTreatmentCompatibility.BlocksManagedWakeup(JobDefOf.Wait_Asleep, false),
                "asleep wait is protected when wake-up is disabled");
            Check(PriorityTreatmentCompatibility.BlocksManagedWakeup(JobDefOf.LayDownResting, false),
                "resting lay-down is protected when wake-up is disabled");
            Check(PriorityTreatmentCompatibility.BlocksManagedWakeup(JobDefOf.LayDown, false),
                "lay-down is protected when wake-up is disabled");
            Check(!PriorityTreatmentCompatibility.BlocksManagedWakeup(JobDefOf.LayDown, true),
                "wake-up setting permits a managed override");
            Check(!PriorityTreatmentCompatibility.BlocksManagedWakeup(JobDefOf.Ingest, false),
                "non-sleep work is unaffected");
            Check(!PriorityTreatmentCompatibility.BlocksManagedWakeup(null, false),
                "an idle responder is unaffected");
        }

        private static object GetSettings(Type modType, Type settingsType)
        {
            MethodInfo getMod = typeof(LoadedModManager).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(method => method.Name == "GetMod" && method.IsGenericMethodDefinition &&
                                 method.GetParameters().Length == 0);
            object mod = getMod.MakeGenericMethod(modType).Invoke(null, null);
            MethodInfo getSettings = typeof(Mod).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .First(method => method.Name == "GetSettings" && method.IsGenericMethodDefinition &&
                                 method.GetParameters().Length == 0);
            return getSettings.MakeGenericMethod(settingsType).Invoke(mod, null);
        }

        private static Pawn SpawnColonist(Map map, int offset)
        {
            Pawn pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
            IntVec3 cell = CellFinder.RandomClosewalkCellNear(
                map.Center + new IntVec3(offset, 0, 0), map, 4);
            return GenSpawn.Spawn(pawn, cell, map) as Pawn;
        }

        private static bool CanRequestManagedOverride(Pawn pawn)
        {
            return (bool)InvokeCompatibility("CanRequestManagedOverride", pawn);
        }

        private static object InvokeCompatibility(string methodName, object argument)
        {
            return AccessTools.Method(typeof(PriorityTreatmentCompatibility), methodName)
                .Invoke(null, new[] { argument });
        }

        private static void Destroy(Pawn pawn)
        {
            if (pawn != null && !pawn.Destroyed)
            {
                pawn.Destroy();
            }
        }

        private static void Check(bool pass, string label)
        {
            Log.Message("[SAR Priority Treatment regression] " + (pass ? "PASS: " : "FAIL: ") + label);
        }

        private sealed class DiagnosticJobDriver : JobDriver
        {
            public override bool TryMakePreToilReservations(bool errorOnFailed)
            {
                return true;
            }

            protected override IEnumerable<Toil> MakeNewToils()
            {
                yield break;
            }
        }
    }
}
