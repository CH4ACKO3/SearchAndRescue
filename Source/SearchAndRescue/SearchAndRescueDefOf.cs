using RimWorld;
using Verse;

namespace SearchAndRescue
{
    [DefOf]
    public static class SearchAndRescueDefOf
    {
        public static DesignationDef SAR_Treat;
        public static DesignationDef SAR_Rescue;
        public static DesignationDef SAR_Capture;
        public static DesignationDef SAR_RescuePoint;
        public static JobDef SAR_EvacuateToPoint;
        public static JobDef SAR_CaptureInPlace;
        public static JobDef SAR_WaitForFieldTreatment;
        public static JobDef SAR_RestockMedicalKit;
        public static JobDef SAR_DeliverMedicalSupply;
        public static WorkTypeDef SAR_FieldRescue;
        public static WorkGiverDef SAR_CaptureMarked;
        public static WorkGiverDef SAR_TreatMarked;
        public static WorkGiverDef SAR_FollowupTreatMarked;
        public static WorkGiverDef SAR_AutomaticRoutineTreat;
        public static WorkGiverDef SAR_RescueMarkedHauling;

        static SearchAndRescueDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(SearchAndRescueDefOf));
        }
    }
}
