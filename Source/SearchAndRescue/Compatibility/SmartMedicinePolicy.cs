using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;

namespace SearchAndRescue
{
    internal static partial class Compatibility
    {
        // Query the same policy inputs as SM.Find, without searching/reserving medicine.
        // This preserves Pharmacist advice, SM's temporary care rollback and per-wound
        // overrides in budgeting, supply selection and the final dose validation.
        private static readonly Lazy<MethodInfo> SmartCare = new Lazy<MethodInfo>(() =>
            FindLoadedType("SmartMedicine.GetPawnMedicalCareCategory")?.GetMethod("GetCare"));
        private static readonly Lazy<MethodInfo> SmartTendHediffs = new Lazy<MethodInfo>(() =>
            FindLoadedType("SmartMedicine.FindBestMedicine")?.GetMethod("HediffsToTend"));
        private static readonly Lazy<MethodInfo> SmartHediffCare = new Lazy<MethodInfo>(() =>
            FindLoadedType("SmartMedicine.PriorityCareSettingsComp")?.GetMethod("Get"));
        private static readonly Lazy<MethodInfo> SmartOriginalCare = new Lazy<MethodInfo>(() =>
            FindLoadedType("SmartMedicine.DefaultCareFix")?.GetMethod("GetOriginalCare"));
        private static readonly Lazy<MethodInfo> SmartCareComponent = new Lazy<MethodInfo>(() =>
            FindLoadedType("SmartMedicine.DefaultCareFix")?.GetMethod("GetComp",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy));

        private static bool TryGetSmartMedicineCare(Pawn patient, out MedicalCareCategory care)
        {
            care = MedicalCareCategory.NoCare;
            if (patient == null || SmartCare.Value == null || SmartTendHediffs.Value == null ||
                SmartHediffCare.Value == null) return false;
            try
            {
                var baseline = (MedicalCareCategory)SmartCare.Value.Invoke(null, new object[] { patient });
                object component = SmartCareComponent.Value?.Invoke(null, null);
                if (component != null && SmartOriginalCare.Value?.Invoke(component,
                        new object[] { patient }) is MedicalCareCategory original) baseline = original;
                var overrides = SmartHediffCare.Value.Invoke(null, null) as IDictionary<Hediff, MedicalCareCategory>;
                var hediffs = SmartTendHediffs.Value.Invoke(null, new object[] { patient }) as IEnumerable<Hediff>;
                if (overrides == null || hediffs == null) return false;
                var wounds = hediffs.ToList();
                // Non-tendable blood loss still needs the baseline device permission.
                care = wounds.Count == 0 ? baseline : wounds.Select(hediff =>
                    overrides.TryGetValue(hediff, out MedicalCareCategory specific) ? specific : baseline).Max();
                return true;
            }
            catch (Exception exception)
            {
                Log.WarningOnce("[Search and Rescue] Smart Medicine care policy lookup failed; using standard care. " +
                    exception.GetBaseException().Message, 196320790);
                return false;
            }
        }
    }
}
