using System;
using RimWorld;
using Verse;

namespace SearchAndRescue
{
    internal static class InfectionPriority
    {
        // WoundInfection also covers adapters which retain the native infection Def.
        // Do not infer medical behavior from translated labels or arbitrary mod Def names.
        internal static bool IsInfection(Hediff hediff) => hediff?.def == HediffDefOf.WoundInfection;

        internal static bool NeedsUrgentTend(Pawn patient)
        {
            if (patient?.health == null) return false;
            foreach (Hediff hediff in patient.health.hediffSet.hediffs)
                if (IsInfection(hediff) && hediff.TendableNow() &&
                    patient.health.immunity.GetImmunity(hediff.def) < 1f) return true;
            return false;
        }

        internal static double Urgency(Pawn patient)
        {
            if (patient?.health == null) return 0d;
            double urgency = 0d;
            foreach (Hediff hediff in patient.health.hediffSet.hediffs)
            {
                if (!IsInfection(hediff)) continue;
                urgency = Math.Max(urgency, InfectionPriorityRules.Urgency(
                    hediff.Severity, hediff.def.lethalSeverity,
                    patient.health.immunity.GetImmunity(hediff.def), hediff.TendableNow()));
            }
            // Multiple infected body parts share one patient's immunity race; do not stack
            // the same risk repeatedly until it overwhelms immediate hemorrhage/CPR.
            return urgency;
        }
    }
}
