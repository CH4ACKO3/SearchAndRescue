using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace SearchAndRescue
{
    // A synchronous snapshot excludes passive healing between game ticks. Do not use
    // medicine consumption or Harmony's __runOriginal: replacement providers may tend
    // without either, and an empty vanilla round may still consume medicine.
    internal sealed class TreatmentOutcome
    {
        private readonly JobIdentity identity;
        private readonly List<Wound> wounds = new List<Wound>();

        private struct Wound
        {
            internal Hediff Hediff;
            internal int TendTicks;
            internal float Severity;
        }

        internal TreatmentOutcome(Pawn doctor, Pawn patient)
        {
            identity = ActiveJobClaims.IdentityOf(doctor.CurJob);
            foreach (Hediff hediff in patient.health.hediffSet.hediffs)
                if (hediff.TendableNow())
                    wounds.Add(new Wound { Hediff = hediff,
                        TendTicks = hediff.TryGetComp<HediffComp_TendDuration>()?.tendTicksLeft ?? -1,
                        Severity = hediff.Severity });
        }

        internal bool Matches(Pawn doctor) => identity.Matches(ActiveJobClaims.IdentityOf(doctor?.CurJob));

        internal bool Changed(Pawn patient)
        {
            foreach (Wound wound in wounds)
                if (!patient.health.hediffSet.hediffs.Contains(wound.Hediff) ||
                    !wound.Hediff.TendableNow() ||
                    (wound.Hediff.TryGetComp<HediffComp_TendDuration>()?.tendTicksLeft ?? -1) > wound.TendTicks ||
                    wound.Hediff.Severity < wound.Severity)
                    return true;
            return false;
        }
    }
}
