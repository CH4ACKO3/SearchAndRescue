using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace SearchAndRescue
{
    internal static class RobotMedicalProfile
    {
        internal static bool OwnsMedicineSelection(Pawn patient) => patient?.def != null &&
            (patient.def.defName == "Paniel_Race" || patient.def.modExtensions?.Any(extension =>
                extension.GetType().FullName == "Androids.MechanicalPawnProperties") == true);

        internal static bool AllowsBiologicalEmergency(Pawn patient) =>
            !OwnsMedicineSelection(patient) && patient?.def?.defName != "ChjAndroid" &&
            patient?.RaceProps?.IsMechanoid != true;

        internal static MedicalTreatmentOption TreatmentOption(Pawn worker, Pawn patient)
        {
            if (!Compatibility.CanPerformTreatmentWork(worker) || !patient.Spawned ||
                !patient.health.HasHediffsNeedingTend() ||
                !worker.CanReach(patient, PathEndMode.Touch, Danger.Deadly))
                return MedicalTreatmentOption.Invalid;

            // Paniel supplies its medicine permission; Androids supplies repair parts.
            // Keep the native finder and native Tend finalizer together.
            Thing resource = HealthAIUtility.FindBestMedicine(worker, patient);
            Pawn holder = MedicalResourceLedger.InventoryHolder(resource);
            if (resource != null)
            {
                if (resource.Destroyed || resource.stackCount < 1) return MedicalTreatmentOption.Invalid;
                if (holder == null)
                {
                    if (!resource.Spawned || resource.Map != worker.Map || resource.IsForbidden(worker) ||
                        !worker.CanReserve(resource, 10, 1) ||
                        !worker.CanReach(resource, PathEndMode.ClosestTouch, Danger.Deadly))
                        return MedicalTreatmentOption.Invalid;
                }
                else if (holder != worker && (Compatibility.IsVehiclePawn(holder) ||
                    holder.IsForbidden(worker) ||
                    !MedicalResourceLedger.CanTakeFromInventoryHolder(worker, holder, resource, 1, patient)))
                    return MedicalTreatmentOption.Invalid;
            }
            double distance = resource == null ? worker.Position.DistanceTo(patient.Position) :
                worker.Position.DistanceTo(resource.PositionHeld) + resource.PositionHeld.DistanceTo(patient.Position);
            return new MedicalTreatmentOption(MedicalIntervention.NativeRobotTend, resource,
                resource == null ? 0 : 1, holder != null, false, 1d, distance);
        }
    }
}
