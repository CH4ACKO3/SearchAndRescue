using System.Collections.Generic;
using Verse;
using Verse.AI;
using RimWorld;

namespace SearchAndRescue
{
    public sealed partial class SearchAndRescueCoordinator
    {
        // The destination map owns the claim while its worker is on another map.
        private List<StrataTrip> strataTrips;

        private sealed class StrataTrip : IExposable
        {
            internal Pawn Worker;
            internal Pawn Patient;
            internal Building_Bed Bed;
            internal int JobId;
            internal bool CarryingToBed;

            internal bool Active
            {
                get
                {
                    if (Worker == null || Worker.Dead || Patient == null || Patient.Dead) return false;
                    Job current = Worker.CurJob;
                    if (CarryingToBed)
                    {
                        if (Bed?.Spawned != true || Patient.CurrentBed() == Bed) return false;
                        if (Worker.carryTracker?.CarriedThing == Patient) return true;
                        if (current?.def == JobDefOf.Rescue && current.targetA.Thing == Patient &&
                            current.targetB.Thing == Bed) return true;
                    }
                    return current != null && current.loadID == JobId;
                }
            }

            public void ExposeData()
            {
                Scribe_References.Look(ref Worker, "worker");
                Scribe_References.Look(ref Patient, "patient");
                Scribe_References.Look(ref Bed, "bed");
                Scribe_Values.Look(ref JobId, "jobId");
                Scribe_Values.Look(ref CarryingToBed, "carryingToBed");
            }
        }

        private void PruneStrataTrips() => strataTrips?.RemoveAll(trip => trip?.Active != true);

        internal void TrackStrataTrip(Pawn worker, Pawn patient, Building_Bed bed, Job job)
        {
            PruneStrataTrips();
            strataTrips ??= new List<StrataTrip>();
            strataTrips.Add(new StrataTrip { Worker = worker, Patient = patient, Bed = bed,
                CarryingToBed = bed != null, JobId = job.loadID });
        }

        internal bool HasIncomingRescuer(Pawn patient, Pawn worker)
        {
            PruneStrataTrips();
            return strataTrips?.Exists(trip => !trip.CarryingToBed && trip.Patient == patient && trip.Worker != worker) == true;
        }

        internal void AddIncomingBedOccupants(Building_Bed bed, Pawn patient, HashSet<Pawn> occupants)
        {
            PruneStrataTrips();
            if (strataTrips == null) return;
            foreach (var trip in strataTrips)
                if (trip.Bed == bed && trip.Patient != patient) occupants.Add(trip.Patient);
        }
    }
}
