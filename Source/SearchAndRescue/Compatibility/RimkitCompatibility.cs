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
    internal static class RimkitCompatibility
    {
        private static readonly Type KitType = AccessTools.TypeByName("Dubs_Rimkit.CompMedkit");
        private static readonly MethodInfo FindKit = AccessTools.Method("Dubs_Rimkit.KitUtil:FindMedkit");
        private static readonly FieldInfo AutoUse = AccessTools.Field(KitType, "UseKitForTendJob");
        internal static readonly MethodInfo DriverToils = AccessTools.Method("Dubs_Rimkit.JobDriver_BandageOthers:MakeNewToils");
        internal static JobDef BandageJob => DefDatabase<JobDef>.GetNamedSilentFail("BandageOthers");
        internal static bool Ready => FindKit != null && AutoUse != null && DriverToils != null &&
            BandageJob?.driverClass?.FullName == "Dubs_Rimkit.JobDriver_BandageOthers";
        internal static bool IsManagedRound(Job job) => job?.def != null && job.def == BandageJob &&
            job.targetC.Thing is ThingWithComps equipment &&
            equipment.AllComps.Any(comp => KitType?.IsInstanceOfType(comp) == true);

        internal static CompApparelReloadable UsableKit(Pawn worker, Pawn patient)
        {
            if (!Ready || worker == null || patient == null || worker == patient ||
                worker.carryTracker?.CarriedThing != null ||
                !RobotMedicalProfile.AllowsBiologicalEmergency(patient)) return null;
            try
            {
                var kit = FindKit.Invoke(null, new object[] { worker }) as CompApparelReloadable;
                if (kit == null || kit.RemainingCharges <= 0 || !(bool)AutoUse.GetValue(kit) ||
                    kit.AmmoDef?.IsMedicine != true || !Compatibility.AllowsMedicine(patient, kit.AmmoDef)) return null;
                return kit;
            }
            catch (Exception exception)
            {
                Log.WarningOnce("[Search and Rescue] Rimkit lookup failed: " + exception.GetBaseException().Message, 196320791);
                return null;
            }
        }

        internal static MedicalTreatmentOption Option(Pawn worker, Pawn patient)
        {
            CompApparelReloadable kit = UsableKit(worker, patient);
            if (kit == null || !FieldTreatmentBoundary.Tendable(patient).Any()) return MedicalTreatmentOption.Invalid;
            return new MedicalTreatmentOption(MedicalIntervention.RimkitBandage, null, 1, true, false,
                1d + kit.AmmoDef.GetStatValueAbstract(StatDefOf.MedicalPotency) * 0.25d,
                worker.Position.DistanceTo(patient.Position), kit.parent);
        }

        internal static Job MakeJob(Pawn worker, Pawn patient, MedicalTreatmentOption option)
        {
            var kit = UsableKit(worker, patient);
            if (kit == null || kit.parent != option.Equipment || !FieldTreatmentBoundary.Tendable(patient).Any()) return null;
            // Native targetB is filled with the dose extracted by its driver. targetC
            // binds this assignment to the selected worn kit without claiming a map stack.
            return JobMaker.MakeJob(BandageJob, patient, LocalTargetInfo.Invalid, kit.parent);
        }
    }

    [HarmonyPatch]
    internal static class RimkitManagedRoundPatch
    {
        private static bool Prepare() => RimkitCompatibility.Ready;
        private static MethodBase TargetMethod() => RimkitCompatibility.DriverToils;
        private static void Postfix(JobDriver __instance, ref IEnumerable<Toil> __result)
        {
            // targetC survives save/load before transient coordinator claims are rebuilt.
            // Native/manual Rimkit jobs only populate targetA and do not enter this path.
            if (!RimkitCompatibility.IsManagedRound(__instance.job)) return;
            __result = OneRound(__instance, __result);
        }

        private static IEnumerable<Toil> OneRound(JobDriver driver, IEnumerable<Toil> native)
        {
            // The verified 1.6 driver is goto / extract dose / wait / finalize / jump.
            // Refuse an unknown layout rather than consuming a charge at the wrong step.
            List<Toil> toils = native.ToList();
            if (toils.Count != 5)
            {
                var stop = ToilMaker.MakeToil("SAR_RimkitUnsupportedDriver");
                stop.initAction = () => driver.EndJobWith(JobCondition.Incompletable);
                stop.defaultCompleteMode = ToilCompleteMode.Instant;
                yield return stop;
                yield break;
            }
            Action extract = toils[1].initAction;
            toils[1].initAction = () =>
            {
                Pawn patient = driver.job.targetA.Pawn;
                var kit = RimkitCompatibility.UsableKit(driver.pawn, patient);
                if (kit == null || kit.parent != driver.job.targetC.Thing ||
                    !FieldTreatmentBoundary.Tendable(patient).Any())
                {
                    driver.EndJobWith(JobCondition.Incompletable);
                    return;
                }
                extract();
            };
            Action finalize = toils[3].initAction;
            toils[3].initAction = () =>
            {
                Pawn patient = driver.job.targetA.Pawn;
                Thing dose = driver.job.targetB.Thing;
                if (patient == null || dose == null || !Compatibility.AllowsMedicine(patient, dose) ||
                    !FieldTreatmentBoundary.Tendable(patient).Any())
                {
                    driver.EndJobWith(JobCondition.Incompletable);
                    return;
                }
                finalize();
            };
            // Ending the native list after FinalizeTend leaves charge use and medicine
            // consumption native, but prevents its unbounded jump from opening another dose.
            for (int i = 0; i < 4; i++) yield return toils[i];
        }
    }
}
