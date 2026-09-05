using System;

namespace SearchAndRescue
{
    [Flags]
    internal enum CareOrigin
    {
        None = 0,
        ManualTreatment = 1 << 0,
        ManualRescue = 1 << 1,
        ManualCapture = 1 << 2,
        AutomaticEmergency = 1 << 3,
        AutomaticRoutine = 1 << 4,
        AutomaticRescue = 1 << 5
    }

    /// <summary>
    /// One immutable admission decision for one scheduler snapshot.  Modes change only
    /// which origins are admitted; every admitted patient continues through the same care
    /// plan, edge scoring, matching, claim, logistics, and job materialization pipeline.
    /// </summary>
    internal readonly struct CareAdmission
    {
        public readonly CareOrigin Origin;

        public CareAdmission(CareOrigin origin)
        {
            Origin = origin;
        }

        public bool IsValid => Origin != CareOrigin.None;
        public bool HasManualTreatment => (Origin & CareOrigin.ManualTreatment) != 0;
        public bool HasAutomaticTreatment =>
            (Origin & (CareOrigin.AutomaticEmergency | CareOrigin.AutomaticRoutine)) != 0;
        public bool HasTreatment => HasManualTreatment || HasAutomaticTreatment;
        public bool HasRescue =>
            (Origin & (CareOrigin.ManualRescue | CareOrigin.AutomaticRescue)) != 0;
        public bool HasCapture => (Origin & CareOrigin.ManualCapture) != 0;
        public bool AllowsEmergencyTreatment => HasManualTreatment ||
                                                (Origin & CareOrigin.AutomaticEmergency) != 0;
        public bool AllowsFollowupTreatment => HasManualTreatment ||
                                               (Origin & CareOrigin.AutomaticRoutine) != 0;
        public bool AllowsLogistics => AllowsEmergencyTreatment;

        public bool AllowsStage(SearchAndRescueStage stage)
        {
            switch (stage)
            {
                case SearchAndRescueStage.Capture:
                    return HasCapture;
                case SearchAndRescueStage.Treat:
                case SearchAndRescueStage.Restock:
                case SearchAndRescueStage.Supply:
                    return AllowsEmergencyTreatment;
                case SearchAndRescueStage.FollowupTreat:
                    return AllowsFollowupTreatment;
                case SearchAndRescueStage.Rescue:
                    return HasRescue;
                default:
                    return false;
            }
        }

        public override string ToString()
        {
            return Origin.ToString();
        }
    }
}
