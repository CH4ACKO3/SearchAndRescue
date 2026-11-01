using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace SearchAndRescue
{
    // Capacity is unchanged during synchronous graph construction. An available slot
    // still needs the normal bed finder, permissions and reachability checks.
    internal sealed class RescueBedSnapshot
    {
        private readonly List<(int Slots, HashSet<Pawn> Claimants)> beds =
            new List<(int, HashSet<Pawn>)>();

        internal RescueBedSnapshot(Map map)
        {
            foreach (Building_Bed bed in map.listerThings.ThingsInGroup(ThingRequestGroup.Bed)
                         .OfType<Building_Bed>())
            {
                if (!bed.Spawned || bed.Destroyed || Compatibility.IsTemporaryFieldTendBed(bed) ||
                    !bed.AnyUnoccupiedSleepingSlot) continue;
                var claimants = new HashSet<Pawn>();
                map.reservationManager.ReserversOf(bed, claimants);
                claimants.Remove(null);
                for (int slot = 0; slot < bed.SleepingSlotsCount; slot++)
                {
                    Pawn occupant = bed.GetCurOccupant(slot);
                    if (occupant != null) claimants.Add(occupant);
                }
                beds.Add((bed.SleepingSlotsCount, claimants));
            }
        }

        internal bool HasCapacity(Pawn patient, Pawn rescuer)
        {
            foreach (var bed in beds)
            {
                int count = bed.Claimants.Count;
                if (bed.Claimants.Contains(patient)) count--;
                if (rescuer != patient && bed.Claimants.Contains(rescuer)) count--;
                if (count < bed.Slots) return true;
            }
            return false;
        }
    }
}
