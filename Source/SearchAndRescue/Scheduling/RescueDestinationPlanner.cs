using RimWorld;
using Verse;
using Verse.AI;
using System.Linq;

namespace SearchAndRescue
{
    internal static class RescueDestinationPlanner
    {
        internal static bool TryFind(
            Map map,
            Pawn rescuer,
            Pawn patient,
            out Building_Bed bed,
            out IntVec3 destination)
        {
            bed = null;
            destination = IntVec3.Invalid;
            if (rescuer == null || patient == null || !rescuer.Spawned || !patient.Spawned ||
                rescuer.Map != map || patient.Map != map)
            {
                return false;
            }

            bed = MechanicalCare.IsPatient(patient) ? null : Compatibility.FindBestRescueBed(patient, rescuer);
            if (bed != null)
            {
                if (!Compatibility.RescueBedHasReservationCapacity(bed, patient, rescuer) ||
                    !DestinationAllowedForAnimal(rescuer, bed.Position))
                {
                    bed = null;
                    return false;
                }

                destination = bed.Position;
                return true;
            }

            Designation rescuePoint = map.designationManager
                .SpawnedDesignationsOfDef(SearchAndRescueDefOf.SAR_RescuePoint)
                .FirstOrDefault();
            // Automatic admission persists while a casualty remains downed. Do not issue
            // another carry to the point they have already reached. Keep this after bed
            // selection so a newly available bed still enables onward evacuation; moving
            // the point or the patient also naturally enables a new route, including on load.
            if (rescuePoint == null || RescueCompleted(patient, rescuePoint.target.Cell, null) ||
                !rescuer.CanReach(rescuePoint.target.Cell, PathEndMode.OnCell, Danger.Deadly))
            {
                return false;
            }

            if (!DestinationAllowedForAnimal(rescuer, rescuePoint.target.Cell))
            {
                return false;
            }

            destination = rescuePoint.target.Cell;
            return true;
        }

        internal static bool DestinationAllowedForAnimal(Pawn worker, IntVec3 destination)
        {
            if (!Compatibility.IsTrainedRescueAnimal(worker) || worker.playerSettings == null ||
                !worker.playerSettings.RespectsAllowedArea)
            {
                return true;
            }

            Area area = worker.playerSettings.EffectiveAreaRestrictionInPawnCurrentMap;
            return area == null || area[destination];
        }

        internal static bool RescueCompleted(Pawn patient, IntVec3 destination, Building_Bed destinationBed)
        {
            if (destinationBed != null)
            {
                Building_Bed currentBed = patient.CurrentBed();
                return !destinationBed.Destroyed && currentBed == destinationBed &&
                       Compatibility.IsSafeRescueBed(currentBed, patient);
            }

            // A bed delivery only succeeds once the patient is actually tucked in. Proximity
            // is sufficient solely for the fallback rescue-point job.
            return destination.IsValid &&
                   patient.Position.DistanceToSquared(destination) <= 2f;
        }

        internal static bool IsInSafePatientBed(Pawn pawn)
        {
            Building_Bed bed = pawn.CurrentBed();
            return pawn.InBed() && Compatibility.IsSafeRescueBed(bed, pawn);
        }
    }
}
