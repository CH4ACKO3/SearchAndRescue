using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace SearchAndRescue
{
    internal enum CompatibilitySupportLevel
    {
        Integration,
        Compatible,
        Partial,
        Incompatible,
        Disabled
    }

    internal sealed class CompatibilityCatalogEntry
    {
        public readonly string DisplayName;
        public readonly string DetailKey;
        public readonly CompatibilitySupportLevel SupportedLevel;
        public readonly string[] PackageIds;
        private readonly Func<bool> adapterReady;

        public CompatibilityCatalogEntry(
            string displayName,
            string detailKey,
            CompatibilitySupportLevel supportedLevel,
            string[] packageIds,
            Func<bool> adapterReady = null)
        {
            DisplayName = displayName;
            DetailKey = detailKey;
            SupportedLevel = supportedLevel;
            PackageIds = packageIds ?? Array.Empty<string>();
            this.adapterReady = adapterReady;
        }

        public bool Active => PackageIds.Any(CompatibilityRegistry.IsModActive);

        public CompatibilitySupportLevel CurrentLevel
        {
            get
            {
                if (!Active)
                {
                    return CompatibilitySupportLevel.Disabled;
                }

                // Adapter readiness is independent from the support label. A failed optional
                // endpoint downgrades either an integration or compatibility promise to partial.
                return adapterReady != null && !adapterReady()
                    ? CompatibilitySupportLevel.Partial
                    : SupportedLevel;
            }
        }
    }

    internal static class CompatibilityCatalog
    {
        public static readonly IReadOnlyList<CompatibilityCatalogEntry> Entries =
            new List<CompatibilityCatalogEntry>
            {
                Entry("Paniel the Automata", "Paniel", CompatibilitySupportLevel.Partial,
                    null, "ahndemi.panieltheautomata", "kalospacer.AhnDemi.PanieltheAutomata"),
                Entry("Androids for RW 1.6", "Androids", CompatibilitySupportLevel.Partial,
                    null, "ChJees.Androids14"),
                Entry("Androids Expanded", "AndroidsExpanded", CompatibilitySupportLevel.Partial,
                    null, "peptide.androidsexpanded14"),
                Entry("kemomimihouse HardworkingExt", "Hardworking", CompatibilitySupportLevel.Partial,
                    () => HardworkingCompatibility.Ready, "Moo.Hardworking.Kz"),
                Entry("Combat Extended", "CombatExtended", CompatibilitySupportLevel.Integration,
                    () => Compatibility.UsesCombatExtended, "CETeam.CombatExtended"),
                Entry("More Injuries (Continued)", "MoreInjuries", CompatibilitySupportLevel.Integration,
                    () => Compatibility.UsesMoreInjuries, "th3fr3d.extendedinjuries"),
                Entry("Medical System Expansion 2", "MSE2", CompatibilitySupportLevel.Partial,
                    null, "mse2.core"),
                Entry("EPOE-Forked", "EPOE", CompatibilitySupportLevel.Compatible,
                    null, "vat.epoeforked"),
                Entry("Smart Medicine - Continued", "SmartMedicine", CompatibilitySupportLevel.Compatible,
                    () => Compatibility.UsesSmartMedicine, "memegoddess.smartmedicine"),
                Entry("Pharmacist: Represcribed", "Pharmacist", CompatibilitySupportLevel.Compatible,
                    () => Compatibility.UsesPharmacist,
                    "fluffy.pharmacist", "syrchalis.pharmacist", "kopp.pharmacist"),
                Entry("Choose Your Medicine", "ChooseMedicine", CompatibilitySupportLevel.Compatible,
                    () => Compatibility.UsesChooseYourMedicine,
                    "kopp.chooseyourmedicine", "Kopp.ChooseYourMedicine"),
                Entry("Medical Tab", "MedicalTab", CompatibilitySupportLevel.Compatible,
                    null, "memegoddess.medicaltab", "fluffy.medicaltab"),
                Entry("1trickPwnyta's Defaults", "Defaults", CompatibilitySupportLevel.Compatible,
                    null, "defaults.1trickpwnyta"),
                Entry("Emergency Transfusions", "EmergencyTransfusions", CompatibilitySupportLevel.Integration,
                    () => Compatibility.UsesEmergencyTransfusions, "Pausbrak.EmergencyTransfusions"),
                Entry("Hemogen Pack - Emergency transfusion", "HemogenDirect", CompatibilitySupportLevel.Integration,
                    () => Compatibility.UsesHemogenDirect, "aoba.hemogendirect"),
                Entry("Death Rattle Continued", "DeathRattle", CompatibilitySupportLevel.Compatible,
                    null, "troopersmith1.deathrattle"),
                Entry("[RH2] BCD: First Aid", "RH2FirstAid", CompatibilitySupportLevel.Compatible,
                    null, "RH2.BCDs.First.Aid"),
                Entry("[RH2] CPERS: Arrest Here!", "RH2Arrest", CompatibilitySupportLevel.Compatible,
                    null, "RH2.CPERS.Arrest.Here"),
                Entry("Dubs Rimkit", "DubsRimkit", CompatibilitySupportLevel.Compatible,
                    null, "Dubwise.DubsRimkit"),
                Entry("Trauma Team Complete", "TraumaTeam", CompatibilitySupportLevel.Compatible,
                    null, "EdelweissPirate.traumateamcomplete"),
                Entry("Move the Patient", "MovePatient", CompatibilitySupportLevel.Compatible,
                    () => Compatibility.UsesMoveThePatient,
                    "konstantynopolitaneczka.movethepatient"),
                Entry("MedPod", "MedPod", CompatibilitySupportLevel.Compatible,
                    null, "sumghai.Medpod"),
                Entry("[RH2] BCD: CASEVAC", "Casevac", CompatibilitySupportLevel.Compatible,
                    null, "RH2.BCD.CASEVAC"),
                Entry("Smarter Capture Them", "SmarterCapture", CompatibilitySupportLevel.Compatible,
                    null, "lke.Smarter.CaptureThem"),
                Entry("Pick Up And Haul", "PickUpAndHaul", CompatibilitySupportLevel.Compatible,
                    null, "mehni.pickupandhaul"),
                Entry("Hospitality", "Hospitality", CompatibilitySupportLevel.Partial,
                    null, "orion.hospitality"),
                Entry("Vehicle Framework", "VehicleFramework", CompatibilitySupportLevel.Integration,
                    () => Compatibility.UsesVehiclesFramework, "smashphil.vehicleframework"),
                Entry("Nurse Job", "NurseJob", CompatibilitySupportLevel.Integration,
                    () => Compatibility.NurseJobAvailable, "darthsergeant.nursejob"),
                Entry("Work Tab", "WorkTab", CompatibilitySupportLevel.Compatible,
                    () => Compatibility.UsesWorkTab, "fluffy.worktab"),
                Entry("Mech Work Tab", "MechWorkTab", CompatibilitySupportLevel.Compatible,
                    null, "spacemoth.mechtab"),
                Entry("WVC - Work Modes", "WVCWorkModes", CompatibilitySupportLevel.Compatible,
                    null, "wvc.sergkart.biotech.moremechanoidsworkmodes"),
                Entry("Search and Destroy (Continued)", "SearchDestroy", CompatibilitySupportLevel.Compatible,
                    null, "memegoddess.searchanddestroy"),
                Entry("Common Sense", "CommonSense", CompatibilitySupportLevel.Compatible,
                    null, "avilmask.commonsense"),
                Entry("Priority Treatment Ressurected", "PriorityTreatment", CompatibilitySupportLevel.Compatible,
                    null, "tk421storm.prioritytreatmentressurected"),
                Entry("Treat Dying First", "TreatDyingFirst", CompatibilitySupportLevel.Compatible,
                    null, "dogard.treatdyingfirst"),
                Entry("Stabilize Bleeding", "StabilizeBleeding", CompatibilitySupportLevel.Partial,
                    null, "defi.stabilizebleeding"),
                Entry("Allies are Helpful", "AlliesHelpful", CompatibilitySupportLevel.Compatible,
                    null, "ninagoblin.alliesarehelpful"),
                Entry("No One Left Behind", "NoOneLeftBehind", CompatibilitySupportLevel.Partial,
                    null, "ninagoblin.nooneleftbehind"),
                Entry("[MOMO] Stay in bed", "StayInBed", CompatibilitySupportLevel.Compatible,
                    null, "momo.stayinbed"),
                Entry("Grievous Wounds", "GrievousWounds", CompatibilitySupportLevel.Compatible,
                    null, "sirprook.grievouswounds"),
                Entry("Vanilla Furniture Expanded - Medical Module", "VFEMedical",
                    CompatibilitySupportLevel.Compatible, null, "vanillaexpanded.vfemedical"),
                Entry("Sensible Bed Ownership", "SensibleBedOwnership",
                    CompatibilitySupportLevel.Compatible, null,
                    "sensiblebedownership.1trickpwnyta"),
                Entry("Yokai Village", "YokaiVillage", CompatibilitySupportLevel.Compatible,
                    null, "yokaimura.1.32")
            };

        private static CompatibilityCatalogEntry Entry(
            string displayName,
            string detailSuffix,
            CompatibilitySupportLevel level,
            Func<bool> adapterReady,
            params string[] packageIds)
        {
            return new CompatibilityCatalogEntry(
                displayName,
                "SAR_Compatibility_" + detailSuffix,
                level,
                packageIds,
                adapterReady);
        }
    }
}
