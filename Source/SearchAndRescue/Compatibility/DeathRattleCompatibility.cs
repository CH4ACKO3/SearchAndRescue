using System.Collections.Generic;
using System.Linq;
using Verse;

namespace SearchAndRescue
{
    internal static class DeathRattleCompatibility
    {
        private static readonly HashSet<string> HospitalConditions = new HashSet<string>
        {
            "ClinicalDeathNoHeartbeat", "ClinicalDeathAsphyxiation", "LiverFailure",
            "KidneyFailure", "IntestinalFailure", "Coma"
        };

        internal static bool IsHospitalEmergency(Hediff hediff) =>
            hediff != null && HospitalConditions.Contains(hediff.def.defName) &&
            hediff.def.comps?.Any(comp => comp.compClass?.Namespace == "DeathRattle") == true &&
            hediff.CurStage?.lifeThreatening == true && !hediff.ShouldRemove;

        internal static double TransportPriority(Pawn patient)
        {
            if (patient?.health?.hediffSet == null) return 0d;
            return patient.health.hediffSet.hediffs.Where(IsHospitalEmergency)
                .Select(hediff =>
                {
                    bool oxygen = hediff.def.defName == "ClinicalDeathNoHeartbeat" ||
                                  hediff.def.defName == "ClinicalDeathAsphyxiation";
                    // The native no-pulse/oxygen timers progress much faster than organ
                    // failure. Use bounded transport pressure, not a fictional tend job.
                    return (oxygen ? 2d : 1d) + System.Math.Min(1d,
                        hediff.Severity / System.Math.Max(0.001f, hediff.def.lethalSeverity));
                }).DefaultIfEmpty(0d).Max();
        }
    }
}
