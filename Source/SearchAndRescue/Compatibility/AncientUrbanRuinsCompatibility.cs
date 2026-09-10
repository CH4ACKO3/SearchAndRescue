using System;
using System.Linq;
using HarmonyLib;
using Verse;

namespace SearchAndRescue
{
    internal static class AncientUrbanRuinsCompatibility
    {
        private static readonly Type CountType = AccessTools.TypeByName("AncientMarket_Libraray.CompUseableCount");
        private static readonly System.Reflection.PropertyInfo Count = CountType == null ? null : AccessTools.Property(CountType, "Count");

        private static ThingComp Component(Thing medicine) => CountType == null ? null :
            (medicine as ThingWithComps)?.AllComps.FirstOrDefault(c => CountType.IsInstanceOfType(c));

        // AUR preserves remaining uses only in its native DoTend replacement. CE's
        // Stabilize finish action consumes a whole Thing and cannot use that contract.
        internal static bool RequiresNativeTend(Thing medicine) => Component(medicine) != null;

        internal static void PrepareManagedMedicine(Pawn doctor, Pawn patient, Thing medicine)
        {
            if (Count == null || medicine == null || medicine.Destroyed ||
                doctor?.CurJob?.targetA.Pawn != patient ||
                !SearchAndRescueJobContext.IsActive(doctor, doctor.CurJob)) return;
            ThingComp comp = Component(medicine);
            if (comp == null) return;
            // AUR's public getter lazily initializes the counter, but its DoTend
            // prefix reads the nullable backing field directly. Use the actual dose
            // after pickup/splitting, so opening the item's inspector is unnecessary.
            Count.GetValue(comp, null);
        }
    }
}
