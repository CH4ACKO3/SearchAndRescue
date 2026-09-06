using System;
using System.Linq;
using UnityEngine;
using Verse;

namespace SearchAndRescue
{
    public enum RescueWorkMode
    {
        Hauling,
        NursingPreferred,
        NursingOnly
    }

    public enum MedicalCoordinationMode
    {
        MarkedOnly,
        EmergencyAuto,
        AllTending
    }

    public sealed class SearchAndRescueSettings : ModSettings
    {
        public RescueWorkMode RescueWorkMode = RescueWorkMode.Hauling;
        public MedicalCoordinationMode MedicalCoordinationMode = MedicalCoordinationMode.EmergencyAuto;
        public bool PreemptRoutineWorkForEmergencies = true;
        public bool EnableRescuerStandby = true;
        public float RecentMarkerMemoryHours = 4f;
        public float StandbyLeadSeconds = 3f;
        public float BleedingSensitivity = 1f;
        public float BloodLossWarningHours = 18f;
        public float UrgentSurgeryTransportPriority = 1f;
        public float TreatmentBeforeTransportPriority = 1f;
        public float TreatmentSwitchReluctance = 1f;
        public float MedicineDetourTolerance = 1f;
        public int MissionKitPatientCount = 3;
        public int MissionKitConsumableCount = 6;
        public float FieldSupplyRadius = 8f;

        internal int RecentMarkerMemoryTicks => Mathf.RoundToInt(RecentMarkerMemoryHours * 2500f);
        internal int StandbyLeadTicks => Mathf.RoundToInt(StandbyLeadSeconds * 60f);
        internal int BloodLossWarningTicks => Mathf.RoundToInt(BloodLossWarningHours * 2500f);
        internal float MajorBleedThreshold => 0.08f / BleedingSensitivity;
        internal float TotalBleedThreshold => 0.12f / BleedingSensitivity;
        internal double SurgeryTransportWeight => 90000d * UrgentSurgeryTransportPriority;
        internal double TreatmentBeforeTransportWeight => 180000d * TreatmentBeforeTransportPriority;
        internal double TreatmentSwitchMargin => 60000d * TreatmentSwitchReluctance;
        internal double EmergencyMedicineRouteCost => 325d / MedicineDetourTolerance;
        internal double FollowupMedicineRouteCost => 900d / MedicineDetourTolerance;
        internal double SupplyRouteCost => 1000d / MedicineDetourTolerance;
        internal int FieldSupplyRadiusSquared => Mathf.RoundToInt(FieldSupplyRadius * FieldSupplyRadius);

        public override void ExposeData()
        {
            Scribe_Values.Look(ref RescueWorkMode, "rescueWorkMode", RescueWorkMode.Hauling);
            Scribe_Values.Look(ref MedicalCoordinationMode, "medicalCoordinationMode",
                MedicalCoordinationMode.EmergencyAuto);
            Scribe_Values.Look(ref PreemptRoutineWorkForEmergencies,
                "preemptRoutineWorkForEmergencies", true);
            Scribe_Values.Look(ref EnableRescuerStandby, "enableRescuerStandby", true);
            Scribe_Values.Look(ref RecentMarkerMemoryHours, "recentMarkerMemoryHours", 4f);
            Scribe_Values.Look(ref StandbyLeadSeconds, "standbyLeadSeconds", 3f);
            Scribe_Values.Look(ref BleedingSensitivity, "bleedingSensitivity", 1f);
            Scribe_Values.Look(ref BloodLossWarningHours, "bloodLossWarningHours", 18f);
            Scribe_Values.Look(ref UrgentSurgeryTransportPriority,
                "urgentSurgeryTransportPriority", 1f);
            Scribe_Values.Look(ref TreatmentBeforeTransportPriority,
                "treatmentBeforeTransportPriority", 1f);
            Scribe_Values.Look(ref TreatmentSwitchReluctance, "treatmentSwitchReluctance", 1f);
            Scribe_Values.Look(ref MedicineDetourTolerance, "medicineDetourTolerance", 1f);
            Scribe_Values.Look(ref MissionKitPatientCount, "missionKitPatientCount", 3);
            Scribe_Values.Look(ref MissionKitConsumableCount, "missionKitConsumableCount", 6);
            Scribe_Values.Look(ref FieldSupplyRadius, "fieldSupplyRadius", 8f);
            ClampValues();
            base.ExposeData();
        }

        internal void ClampValues()
        {
            RecentMarkerMemoryHours = Mathf.Clamp(RecentMarkerMemoryHours, 0f, 12f);
            StandbyLeadSeconds = Mathf.Clamp(StandbyLeadSeconds, 0f, 10f);
            BleedingSensitivity = Mathf.Clamp(BleedingSensitivity, 0.5f, 2f);
            BloodLossWarningHours = Mathf.Clamp(BloodLossWarningHours, 2f, 24f);
            UrgentSurgeryTransportPriority = Mathf.Clamp(UrgentSurgeryTransportPriority, 0f, 2f);
            TreatmentBeforeTransportPriority = Mathf.Clamp(TreatmentBeforeTransportPriority, 0f, 2f);
            TreatmentSwitchReluctance = Mathf.Clamp(TreatmentSwitchReluctance, 0f, 2f);
            MedicineDetourTolerance = Mathf.Clamp(MedicineDetourTolerance, 0.25f, 2f);
            MissionKitPatientCount = Mathf.Clamp(MissionKitPatientCount, 1, 6);
            MissionKitConsumableCount = Mathf.Clamp(MissionKitConsumableCount, 1, 12);
            FieldSupplyRadius = Mathf.Clamp(FieldSupplyRadius, 2f, 16f);
        }

        internal void ResetToDefaults()
        {
            RescueWorkMode = RescueWorkMode.Hauling;
            MedicalCoordinationMode = MedicalCoordinationMode.EmergencyAuto;
            PreemptRoutineWorkForEmergencies = true;
            EnableRescuerStandby = true;
            RecentMarkerMemoryHours = 4f;
            StandbyLeadSeconds = 3f;
            BleedingSensitivity = 1f;
            BloodLossWarningHours = 18f;
            UrgentSurgeryTransportPriority = 1f;
            TreatmentBeforeTransportPriority = 1f;
            TreatmentSwitchReluctance = 1f;
            MedicineDetourTolerance = 1f;
            MissionKitPatientCount = 3;
            MissionKitConsumableCount = 6;
            FieldSupplyRadius = 8f;
        }
    }

    public sealed class SearchAndRescueMod : Mod
    {
        private enum SettingsPage
        {
            General,
            Advanced,
            Compatibility
        }

        internal static SearchAndRescueSettings Settings { get; private set; }

        private SettingsPage selectedPage;
        private Vector2 generalScroll;
        private Vector2 advancedScroll;
        private Vector2 compatibilityScroll;

        public SearchAndRescueMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<SearchAndRescueSettings>();
            Settings.ClampValues();
        }

        public override string SettingsCategory()
        {
            return "Search and Rescue";
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Settings.ClampValues();
            Rect tabsRect = new Rect(inRect.x, inRect.y, inRect.width, 34f);
            DrawTabs(tabsRect);
            Rect body = new Rect(inRect.x, tabsRect.yMax + 10f, inRect.width,
                inRect.height - tabsRect.height - 10f);

            bool changed = false;
            switch (selectedPage)
            {
                case SettingsPage.General:
                    changed = DrawGeneralPage(body);
                    break;
                case SettingsPage.Advanced:
                    changed = DrawAdvancedPage(body);
                    break;
                case SettingsPage.Compatibility:
                    DrawCompatibilityPage(body);
                    break;
            }

            if (changed)
            {
                Settings.ClampValues();
                SearchAndRescueCoordinator.NotifyGlobalSettingsChanged();
            }
        }

        private void DrawTabs(Rect rect)
        {
            const float gap = 6f;
            float width = (rect.width - gap * 2f) / 3f;
            DrawTab(new Rect(rect.x, rect.y, width, rect.height), SettingsPage.General,
                "SAR_Settings_Tab_General");
            DrawTab(new Rect(rect.x + width + gap, rect.y, width, rect.height), SettingsPage.Advanced,
                "SAR_Settings_Tab_Advanced");
            DrawTab(new Rect(rect.x + (width + gap) * 2f, rect.y, width, rect.height),
                SettingsPage.Compatibility, "SAR_Settings_Tab_Compatibility");
        }

        private void DrawTab(Rect rect, SettingsPage page, string labelKey)
        {
            if (selectedPage == page)
            {
                Widgets.DrawHighlightSelected(rect);
            }
            if (Widgets.ButtonText(rect, labelKey.Translate()))
            {
                selectedPage = page;
            }
        }

        private bool DrawGeneralPage(Rect outer)
        {
            Rect view = ScrollView(outer, ref generalScroll, 900f);
            Listing_Standard listing = new Listing_Standard { ColumnWidth = view.width };
            listing.Begin(view);
            bool changed = false;

            Section(listing, "SAR_Settings_Section_Scope");
            Description(listing, "SAR_Settings_FieldRescueWorkType_Desc");
            listing.Gap(10f);
            changed |= CycleMode(listing, "SAR_Settings_MedicalCoordinationMode",
                "SAR_Settings_MedicalCoordinationMode_Desc",
                "SAR_Settings_MedicalCoordinationMode_" + Settings.MedicalCoordinationMode,
                () => Settings.MedicalCoordinationMode = NextMode(Settings.MedicalCoordinationMode));
            changed |= CycleMode(listing, "SAR_Settings_RescueWorkMode",
                "SAR_Settings_RescueWorkMode_Desc",
                "SAR_Settings_RescueWorkMode_" + Settings.RescueWorkMode,
                () => Settings.RescueWorkMode = NextMode(Settings.RescueWorkMode));
            if (Settings.RescueWorkMode != RescueWorkMode.Hauling && !Compatibility.NurseJobAvailable)
            {
                Warning(listing, "SAR_Settings_NurseNotDetected");
            }
            changed |= Checkbox(listing, "SAR_Settings_PreemptRoutine",
                "SAR_Settings_PreemptRoutine_Desc", ref Settings.PreemptRoutineWorkForEmergencies);

            Section(listing, "SAR_Settings_Section_Continuity");
            changed |= Checkbox(listing, "SAR_Settings_EnableStandby",
                "SAR_Settings_EnableStandby_Desc", ref Settings.EnableRescuerStandby);
            changed |= Slider(listing, "SAR_Settings_StandbyLead",
                "SAR_Settings_StandbyLead_Desc", ref Settings.StandbyLeadSeconds,
                0f, 10f, 0.5f, "SAR_Settings_Value_Seconds");
            changed |= Slider(listing, "SAR_Settings_MarkerMemory",
                "SAR_Settings_MarkerMemory_Desc", ref Settings.RecentMarkerMemoryHours,
                0f, 12f, 0.5f, "SAR_Settings_Value_Hours");

            listing.Gap(12f);
            if (Widgets.ButtonText(listing.GetRect(32f), "SAR_Settings_ResetDefaults".Translate()))
            {
                Settings.ResetToDefaults();
                changed = true;
            }

            listing.End();
            Widgets.EndScrollView();
            return changed;
        }

        private bool DrawAdvancedPage(Rect outer)
        {
            Rect view = ScrollView(outer, ref advancedScroll, 1370f);
            Listing_Standard listing = new Listing_Standard { ColumnWidth = view.width };
            listing.Begin(view);
            bool changed = false;

            Section(listing, "SAR_Settings_Section_Triage");
            changed |= Slider(listing, "SAR_Settings_BleedingSensitivity",
                "SAR_Settings_BleedingSensitivity_Desc", ref Settings.BleedingSensitivity,
                0.5f, 2f, 0.05f, "SAR_Settings_Value_Percent");
            changed |= Slider(listing, "SAR_Settings_BloodLossWarning",
                "SAR_Settings_BloodLossWarning_Desc", ref Settings.BloodLossWarningHours,
                2f, 24f, 1f, "SAR_Settings_Value_Hours");
            changed |= Slider(listing, "SAR_Settings_SurgeryPriority",
                "SAR_Settings_SurgeryPriority_Desc", ref Settings.UrgentSurgeryTransportPriority,
                0f, 2f, 0.05f, "SAR_Settings_Value_Percent");
            changed |= Slider(listing, "SAR_Settings_TreatmentTransportPriority",
                "SAR_Settings_TreatmentTransportPriority_Desc",
                ref Settings.TreatmentBeforeTransportPriority,
                0f, 2f, 0.05f, "SAR_Settings_Value_Percent");
            changed |= Slider(listing, "SAR_Settings_SwitchReluctance",
                "SAR_Settings_SwitchReluctance_Desc", ref Settings.TreatmentSwitchReluctance,
                0f, 2f, 0.05f, "SAR_Settings_Value_Percent");

            Section(listing, "SAR_Settings_Section_Logistics");
            changed |= Slider(listing, "SAR_Settings_MedicineDetour",
                "SAR_Settings_MedicineDetour_Desc", ref Settings.MedicineDetourTolerance,
                0.25f, 2f, 0.05f, "SAR_Settings_Value_Percent");
            changed |= IntSlider(listing, "SAR_Settings_KitPatients",
                "SAR_Settings_KitPatients_Desc", ref Settings.MissionKitPatientCount, 1, 6);
            changed |= IntSlider(listing, "SAR_Settings_KitConsumables",
                "SAR_Settings_KitConsumables_Desc", ref Settings.MissionKitConsumableCount, 1, 12);
            changed |= Slider(listing, "SAR_Settings_FieldSupplyRadius",
                "SAR_Settings_FieldSupplyRadius_Desc", ref Settings.FieldSupplyRadius,
                2f, 16f, 1f, "SAR_Settings_Value_Cells");

            listing.End();
            Widgets.EndScrollView();
            return changed;
        }

        private void DrawCompatibilityPage(Rect outer)
        {
            CompatibilityCatalogEntry[] entries = CompatibilityCatalog.Entries
                .OrderBy(entry => entry.CurrentLevel == CompatibilitySupportLevel.Disabled)
                .ThenBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            Text.Font = GameFont.Small;
            TaggedString description = "SAR_Settings_Compatibility_Desc".Translate();
            float descriptionHeight = Text.CalcHeight(description, outer.width - 18f);
            float contentHeight = 32f + descriptionHeight + 10f + 28f + entries.Length * 32f + 4f;
            Rect view = ScrollView(outer, ref compatibilityScroll, contentHeight);
            float y = 0f;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, y, view.width, 30f),
                "SAR_Settings_Compatibility_Title".Translate());
            Text.Font = GameFont.Small;
            y += 32f;
            Widgets.Label(new Rect(0f, y, view.width, descriptionHeight), description);
            y += descriptionHeight + 10f;

            Rect header = new Rect(0f, y, view.width, 28f);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(header.x + 6f, header.y, header.width * 0.72f - 12f, header.height),
                "SAR_Settings_Compatibility_Mod".Translate());
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(new Rect(header.x + header.width * 0.72f, header.y,
                header.width * 0.28f - 8f, header.height),
                "SAR_Settings_Compatibility_Status".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            y += header.height;

            foreach (CompatibilityCatalogEntry entry in entries)
            {
                Rect row = new Rect(0f, y, view.width, 32f);
                Widgets.DrawHighlightIfMouseover(row);
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(row.x + 6f, row.y, row.width * 0.72f - 12f, row.height),
                    entry.DisplayName);

                CompatibilitySupportLevel level = entry.CurrentLevel;
                GUI.color = StatusColor(level);
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(new Rect(row.x + row.width * 0.72f, row.y,
                    row.width * 0.28f - 8f, row.height), StatusLabel(level));
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;

                string detail = entry.DetailKey.Translate();
                string tooltip = level == CompatibilitySupportLevel.Disabled
                    ? "SAR_Settings_Compatibility_TooltipDisabled".Translate(
                        StatusLabel(entry.SupportedLevel), detail)
                    : "SAR_Settings_Compatibility_TooltipActive".Translate(StatusLabel(level), detail);
                TooltipHandler.TipRegion(row, tooltip);
                y += row.height;
            }

            Widgets.EndScrollView();
        }

        private static Rect ScrollView(Rect outer, ref Vector2 scroll, float contentHeight)
        {
            Rect view = new Rect(0f, 0f, outer.width - 18f, contentHeight);
            Widgets.BeginScrollView(outer, ref scroll, view);
            return view;
        }

        private static void Section(Listing_Standard listing, string labelKey)
        {
            listing.Gap(10f);
            Text.Font = GameFont.Medium;
            listing.Label(labelKey.Translate());
            Text.Font = GameFont.Small;
            listing.GapLine(5f);
        }

        private static bool CycleMode(
            Listing_Standard listing,
            string labelKey,
            string descriptionKey,
            string valueKey,
            Action cycle)
        {
            listing.Label(labelKey.Translate());
            bool clicked = Widgets.ButtonText(listing.GetRect(32f), valueKey.Translate());
            if (clicked)
            {
                cycle();
            }
            Description(listing, descriptionKey);
            listing.Gap(10f);
            return clicked;
        }

        private static bool Checkbox(
            Listing_Standard listing,
            string labelKey,
            string descriptionKey,
            ref bool value)
        {
            bool before = value;
            listing.CheckboxLabeled(labelKey.Translate(), ref value);
            Description(listing, descriptionKey);
            listing.Gap(10f);
            return before != value;
        }

        private static bool Slider(
            Listing_Standard listing,
            string labelKey,
            string descriptionKey,
            ref float value,
            float minimum,
            float maximum,
            float step,
            string valueFormatKey)
        {
            float before = value;
            float displayed = valueFormatKey == "SAR_Settings_Value_Percent" ? value * 100f : value;
            listing.Label(labelKey.Translate() + ": " + valueFormatKey.Translate(displayed.ToString("0.#")));
            value = Mathf.Round(listing.Slider(value, minimum, maximum) / step) * step;
            Description(listing, descriptionKey);
            listing.Gap(10f);
            return !Mathf.Approximately(before, value);
        }

        private static bool IntSlider(
            Listing_Standard listing,
            string labelKey,
            string descriptionKey,
            ref int value,
            int minimum,
            int maximum)
        {
            int before = value;
            listing.Label(labelKey.Translate() + ": " + value);
            value = Mathf.RoundToInt(listing.Slider(value, minimum, maximum));
            Description(listing, descriptionKey);
            listing.Gap(10f);
            return before != value;
        }

        private static void Description(Listing_Standard listing, string key)
        {
            TaggedString text = key.Translate();
            Rect rect = listing.GetRect(Text.CalcHeight(text, listing.ColumnWidth));
            GUI.color = Color.gray;
            Widgets.Label(rect, text);
            GUI.color = Color.white;
        }

        private static void Warning(Listing_Standard listing, string key)
        {
            TaggedString text = key.Translate();
            Rect rect = listing.GetRect(Text.CalcHeight(text, listing.ColumnWidth));
            GUI.color = new Color(1f, 0.78f, 0.3f);
            Widgets.Label(rect, text);
            GUI.color = Color.white;
            listing.Gap(6f);
        }

        private static string StatusLabel(CompatibilitySupportLevel level)
        {
            return ("SAR_Compatibility_Status_" + level).Translate();
        }

        private static Color StatusColor(CompatibilitySupportLevel level)
        {
            switch (level)
            {
                case CompatibilitySupportLevel.Integration:
                    return new Color(0.42f, 0.92f, 0.58f);
                case CompatibilitySupportLevel.Compatible:
                    return new Color(0.48f, 0.82f, 0.95f);
                case CompatibilitySupportLevel.Partial:
                    return new Color(1f, 0.78f, 0.3f);
                case CompatibilitySupportLevel.Incompatible:
                    return new Color(1f, 0.42f, 0.4f);
                default:
                    return Color.gray;
            }
        }

        private static MedicalCoordinationMode NextMode(MedicalCoordinationMode mode)
        {
            switch (mode)
            {
                case MedicalCoordinationMode.MarkedOnly:
                    return MedicalCoordinationMode.EmergencyAuto;
                case MedicalCoordinationMode.EmergencyAuto:
                    return MedicalCoordinationMode.AllTending;
                default:
                    return MedicalCoordinationMode.MarkedOnly;
            }
        }

        private static RescueWorkMode NextMode(RescueWorkMode mode)
        {
            switch (mode)
            {
                case RescueWorkMode.Hauling:
                    return RescueWorkMode.NursingPreferred;
                case RescueWorkMode.NursingPreferred:
                    return RescueWorkMode.NursingOnly;
                default:
                    return RescueWorkMode.Hauling;
            }
        }
    }
}
