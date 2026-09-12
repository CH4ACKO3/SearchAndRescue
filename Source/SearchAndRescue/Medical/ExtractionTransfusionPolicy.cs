using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SearchAndRescue
{
    // A policy marker, not a medical condition. Blood loss recovery ends the policy.
    public sealed class Hediff_ExtractionRecovery : Hediff
    {
        public Pawn Extractor;
        public int ExtractionJobId = -1;
        public bool ExtractionInProgress => Extractor?.CurJob?.loadID == ExtractionJobId &&
            ExtractionTransfusionPolicy.IsExtraction(Extractor.CurJob);
        public override bool ShouldRemove => !ExtractionInProgress &&
            (pawn?.health?.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss)?.Severity ?? 0f) <= 0f;
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref Extractor, "extractor");
            Scribe_Values.Look(ref ExtractionJobId, "extractionJobId", -1);
        }
    }

    internal static class ExtractionTransfusionPolicy
    {
        internal static Hediff_ExtractionRecovery Marker(Pawn patient) =>
            patient?.health?.hediffSet.hediffs.Find(h => h is Hediff_ExtractionRecovery) as Hediff_ExtractionRecovery;
        internal static bool Blocks(Pawn patient)
        {
            Hediff_ExtractionRecovery marker = Marker(patient);
            return marker != null && !marker.ShouldRemove;
        }
        internal static bool IsExtraction(Job job) => job?.RecipeDef?.Worker is Recipe_ExtractHemogen;
        internal static bool IsTransfusion(JobDef job) => job != null &&
            (job.defName == "UseBloodBag" || job.defName == "UseSalineBag" ||
             job.defName == "HD_AdministerHemogen" || job.defName == "ET_TransfuseBlood");
        internal static void Begin(Pawn doctor)
        {
            Job job = doctor?.CurJob;
            if (!IsExtraction(job) || !(job.targetA.Thing is Pawn patient)) return;
            var marker = Marker(patient);
            if (marker == null)
            {
                marker = (Hediff_ExtractionRecovery)HediffMaker.MakeHediff(
                    DefDatabase<HediffDef>.GetNamed("SAR_ExtractionRecovery"), patient);
                marker.Extractor = doctor;
                marker.ExtractionJobId = job.loadID;
                patient.health.AddHediff(marker);
            }
            else
            {
                marker.Extractor = doctor;
                marker.ExtractionJobId = job.loadID;
            }
        }
        internal static void Clear(Pawn patient)
        {
            var marker = Marker(patient);
            if (marker != null) patient.health.RemoveHediff(marker);
            SearchAndRescueCoordinator.NotifyGlobalSettingsChanged();
        }
    }

    [HarmonyPatch(typeof(Toils_Recipe), nameof(Toils_Recipe.DoRecipeWork))]
    internal static class ExtractionRecoveryStartPatch
    {
        private static void Postfix(Toil __result)
        {
            Action original = __result.initAction;
            __result.initAction = () =>
            {
                // Runs when the doctor actually begins recipe work, not while fetching supplies.
                ExtractionTransfusionPolicy.Begin(__result.actor);
                original?.Invoke();
            };
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    internal static class ExtractionRecoveryGizmoPatch
    {
        private static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
        {
            foreach (Gizmo gizmo in __result) yield return gizmo;
            if (ExtractionTransfusionPolicy.Marker(__instance) == null ||
                (__instance.Faction != Faction.OfPlayer && !__instance.IsPrisonerOfColony && !__instance.IsSlaveOfColony)) yield break;
            yield return new Command_Action
            {
                defaultLabel = "SAR_ResumeTransfusion".Translate(),
                defaultDesc = "SAR_ResumeTransfusionDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("SearchAndRescue/Designations/Treat"),
                action = () => ExtractionTransfusionPolicy.Clear(__instance)
            };
        }
    }
}

