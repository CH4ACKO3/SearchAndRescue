using RimWorld;
using Verse;

namespace SearchAndRescue
{
    internal static class TargetEligibility
    {
        public static bool IsLivingFleshPawn(Pawn pawn)
        {
            return pawn != null && pawn.Spawned && !pawn.Dead && pawn.RaceProps.IsFlesh;
        }

        public static bool CanReceiveFieldCare(Pawn pawn)
        {
            return IsLivingFleshPawn(pawn) && CanReceiveFieldCareAfterDrop(pawn);
        }

        public static bool CanReceiveFieldCareAfterDrop(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || !pawn.RaceProps.IsFlesh ||
                (!pawn.RaceProps.Humanlike && !pawn.RaceProps.Animal))
            {
                return false;
            }

            if (ModsConfig.AnomalyActive && pawn.IsMutant &&
                pawn.mutant?.Def?.entitledToMedicalCare != true)
            {
                return false;
            }

            // Hostile humanlikes can first be secured by the capture stage. Animals have no
            // equivalent prisoner state, so accepting them would let a healed beast attack
            // its rescuers immediately.
            return !pawn.RaceProps.Animal || !pawn.HostileTo(Faction.OfPlayer);
        }

        public static bool CanBeCaptured(Pawn pawn)
        {
            if (!IsLivingFleshPawn(pawn) || !pawn.RaceProps.Humanlike)
            {
                return false;
            }

            // Anomaly entities/mutants that belong on a holding platform must not be fed
            // through the prisoner-bed Capture driver. Containment can be added later as a
            // distinct stage with its own destination and reservations.
            return !ModsConfig.AnomalyActive ||
                   pawn.TryGetComp<CompHoldingPlatformTarget>()?.CanBeCaptured != true;
        }
    }
}
