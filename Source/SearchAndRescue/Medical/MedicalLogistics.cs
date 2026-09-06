using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SearchAndRescue
{
    internal enum MedicalIntervention
    {
        None,
        VanillaTend,
        CombatExtendedStabilize,
        Rh2FirstAid,
        MoreInjuriesFirstAid,
        Cpr,
        Suction,
        Defibrillate,
        Epinephrine,
        Tourniquet,
        RemoveTourniquet,
        HemostaticAgent,
        Bandage,
        Saline,
        Blood,
        HemogenTransfusion,
        MechRepair,
        NativeRobotTend
    }

    internal interface IFieldMedicalResourceProvider
    {
        string Id { get; }
        bool Active { get; }
        void AddDemands(Pawn patient, ICollection<MedicalResourceDemand> demands);
    }

    internal static class FieldMedicalResourceProviders
    {
        private sealed class DelegateProvider : IFieldMedicalResourceProvider
        {
            private readonly Func<bool> active;
            private readonly Action<Pawn, ICollection<MedicalResourceDemand>> addDemands;

            public string Id { get; }
            public bool Active => active();

            public DelegateProvider(
                string id,
                Func<bool> active,
                Action<Pawn, ICollection<MedicalResourceDemand>> addDemands)
            {
                Id = id;
                this.active = active;
                this.addDemands = addDemands;
            }

            public void AddDemands(Pawn patient, ICollection<MedicalResourceDemand> demands)
            {
                addDemands(patient, demands);
            }
        }

        private static readonly List<IFieldMedicalResourceProvider> Providers =
            new List<IFieldMedicalResourceProvider>
            {
                new DelegateProvider(
                    "more-injuries",
                    () => Compatibility.UsesMoreInjuries,
                    MedicalCarePlan.AddMoreInjuriesDemands),
                new DelegateProvider(
                    "hemogen-transfusion",
                    () => Compatibility.UsesHemogenTransfusion,
                    MedicalCarePlan.AddHemogenTransfusionDemand)
            };

        internal static void Register(IFieldMedicalResourceProvider provider)
        {
            if (provider != null && Providers.All(existing => existing.Id != provider.Id))
            {
                Providers.Add(provider);
            }
        }

        internal static void AddDemands(Pawn patient, ICollection<MedicalResourceDemand> demands)
        {
            foreach (IFieldMedicalResourceProvider provider in Providers.Where(provider => provider.Active))
            {
                provider.AddDemands(patient, demands);
            }
        }
    }

    internal enum MedicalResourceAccess
    {
        Treatment,
        Relocation
    }

    internal sealed class MedicalResourceDemand
    {
        public readonly ThingDef ResourceDef;
        public readonly MedicalIntervention Intervention;
        public readonly int Count;
        public readonly bool Essential;
        public readonly bool Reusable;
        public readonly double Benefit;

        public MedicalResourceDemand(
            ThingDef resourceDef,
            MedicalIntervention intervention,
            int count,
            bool essential,
            bool reusable,
            double benefit)
        {
            ResourceDef = resourceDef;
            Intervention = intervention;
            Count = Math.Max(1, count);
            Essential = essential;
            Reusable = reusable;
            Benefit = benefit;
        }
    }

    internal sealed class MedicalCarePlan
    {
        public readonly Pawn Patient;
        public readonly int BuiltAt;
        public readonly int BloodLossDeadline;
        public readonly int EssentialMedicineRounds;
        public readonly IReadOnlyList<MedicalResourceDemand> Demands;

        public MedicalCarePlan(
            Pawn patient,
            int builtAt,
            int bloodLossDeadline,
            int essentialMedicineRounds,
            IReadOnlyList<MedicalResourceDemand> demands)
        {
            Patient = patient;
            BuiltAt = builtAt;
            BloodLossDeadline = bloodLossDeadline;
            EssentialMedicineRounds = essentialMedicineRounds;
            Demands = demands;
        }

        public static MedicalCarePlan Build(Pawn patient, int now)
        {
            List<MedicalResourceDemand> demands = new List<MedicalResourceDemand>();
            if (patient?.health == null || MechanicalCare.IsPatient(patient) || RobotMedicalProfile.OwnsMedicineSelection(patient))
            {
                return new MedicalCarePlan(patient, now, int.MaxValue, 0, demands);
            }

            int significantUntended = patient.health.hediffSet.hediffs.Count(hediff =>
                hediff.TendableNow() && (hediff.BleedRate >= 0.04f || hediff.CurStage?.lifeThreatening == true ||
                                       InfectionPriority.IsInfection(hediff)));
            int medicineRounds = Compatibility.EffectiveMedicalCare(patient) <= MedicalCareCategory.NoMeds
                ? 0
                : significantUntended == 0
                    ? patient.health.HasHediffsNeedingTend() ? 1 : 0
                    : Mathf.Clamp(significantUntended, 1, 4);

            // CE's Stabilize driver walks every currently stabilizable wound in one job and
            // consumes one medicine only after that complete loop. Budgeting per wound causes
            // pointless multi-pack pickups and disagrees with CE's actual consumption model.
            if (medicineRounds > 0 && Compatibility.CombatExtendedCanStabilize(patient))
            {
                medicineRounds = 1;
            }

            if (medicineRounds > 0)
            {
                demands.Add(new MedicalResourceDemand(
                    null,
                    MedicalIntervention.VanillaTend,
                    medicineRounds,
                    true,
                    false,
                    1.0d));
            }

            FieldMedicalResourceProviders.AddDemands(patient, demands);

            return new MedicalCarePlan(
                patient,
                now,
                HealthUtility.TicksUntilDeathDueToBloodLoss(patient),
                medicineRounds,
                demands);
        }

        internal static void AddMoreInjuriesDemands(Pawn patient, ICollection<MedicalResourceDemand> demands)
        {
            if (!RobotMedicalProfile.AllowsBiologicalEmergency(patient)) return;
            // More Injuries treats every consumable/reusable treatment device as requiring
            // at least the NoMeds+ policy. CPR remains an equipment-free option and is added
            // later by Compatibility.FindTreatmentOptions.
            if (!Compatibility.AllowsMedicalDevices(patient))
            {
                return;
            }

            bool choking = HasHediff(patient, "ChokingOnBlood");
            Hediff cardiac = patient.health.hediffSet.hediffs.FirstOrDefault(hediff =>
                hediff.def.defName == "CardiacArrest");
            bool heartAttack = HasHediff(patient, "HeartAttack");
            if (choking && Compatibility.MoreInjuriesSuctionDevice != null &&
                Compatibility.IsMedicalInterventionUnlocked(MedicalIntervention.Suction))
            {
                demands.Add(new MedicalResourceDemand(
                    Compatibility.MoreInjuriesSuctionDevice,
                    MedicalIntervention.Suction,
                    1,
                    false,
                    true,
                    2.2d));
            }

            if ((heartAttack || cardiac?.CurStageIndex == 0) &&
                Compatibility.MoreInjuriesDefibrillator != null &&
                Compatibility.IsMedicalInterventionUnlocked(MedicalIntervention.Defibrillate))
            {
                demands.Add(new MedicalResourceDemand(
                    Compatibility.MoreInjuriesDefibrillator,
                    MedicalIntervention.Defibrillate,
                    1,
                    heartAttack,
                    true,
                    2.5d));
            }

            Hediff adrenaline = patient.health.hediffSet.hediffs.FirstOrDefault(hediff =>
                hediff.def.defName == "AdrenalineRush");
            if (cardiac != null && (adrenaline == null || adrenaline.Severity < 0.25f) &&
                Compatibility.MoreInjuriesEpinephrine != null &&
                Compatibility.IsMedicalInterventionUnlocked(MedicalIntervention.Epinephrine))
            {
                demands.Add(new MedicalResourceDemand(
                    Compatibility.MoreInjuriesEpinephrine,
                    MedicalIntervention.Epinephrine,
                    1,
                    false,
                    false,
                    3.0d));
            }

            List<Hediff> majorBleeds = patient.health.hediffSet.hediffs
                .Where(hediff => !hediff.IsTended() && hediff.BleedRate >= 0.08f)
                .ToList();
            if (majorBleeds.Count > 0)
            {
                int hemostasisCount = majorBleeds.Count(Compatibility.MoreInjuriesCanUseHemostasis);
                if (hemostasisCount > 0 && Compatibility.MoreInjuriesHemostaticAgent != null &&
                    Compatibility.IsMedicalInterventionUnlocked(MedicalIntervention.HemostaticAgent))
                {
                    demands.Add(new MedicalResourceDemand(
                        Compatibility.MoreInjuriesHemostaticAgent,
                        MedicalIntervention.HemostaticAgent,
                        Mathf.Clamp(hemostasisCount, 1, 3),
                        true,
                        false,
                        4.0d));
                }
                else if (hemostasisCount > 0 && Compatibility.MoreInjuriesBandage != null &&
                         Compatibility.IsMedicalInterventionUnlocked(MedicalIntervention.Bandage))
                {
                    demands.Add(new MedicalResourceDemand(
                        Compatibility.MoreInjuriesBandage,
                        MedicalIntervention.Bandage,
                        Mathf.Clamp(hemostasisCount, 1, 3),
                        true,
                        false,
                        3.5d));
                }

                bool limbCanUseTourniquet = majorBleeds.Any(hediff =>
                    Compatibility.MoreInjuriesTourniquetLimbFor(hediff) != null);
                if (limbCanUseTourniquet && Compatibility.MoreInjuriesTourniquet != null &&
                    Compatibility.IsMedicalInterventionUnlocked(MedicalIntervention.Tourniquet))
                {
                    demands.Add(new MedicalResourceDemand(
                        Compatibility.MoreInjuriesTourniquet,
                        MedicalIntervention.Tourniquet,
                        1,
                        false,
                        false,
                        4.2d));
                }
            }

            int salineRequired = Compatibility.MoreInjuriesSalineBag != null &&
                                 Compatibility.IsMedicalInterventionUnlocked(MedicalIntervention.Saline)
                ? Compatibility.MoreInjuriesPlannedTransfusions(
                    patient,
                    MedicalIntervention.Saline)
                : 0;
            if (salineRequired > 0)
            {
                demands.Add(new MedicalResourceDemand(
                    Compatibility.MoreInjuriesSalineBag,
                    MedicalIntervention.Saline,
                    salineRequired,
                    false,
                    false,
                    2.4d));
            }

            // Blood is a real alternative rather than an else-branch. If saline is clinically
            // safe but unavailable, the graph can immediately choose an existing blood bag.
            // It also remains eligible for hemodilution after saline has lowered BloodLoss
            // below the ordinary 0.449 stabilization threshold.
            int bloodRequired = Compatibility.MoreInjuriesBloodBag != null &&
                                Compatibility.IsMedicalInterventionUnlocked(MedicalIntervention.Blood)
                ? Compatibility.MoreInjuriesPlannedTransfusions(
                    patient,
                    MedicalIntervention.Blood)
                : 0;
            if (bloodRequired > 0)
            {
                demands.Add(new MedicalResourceDemand(
                    Compatibility.MoreInjuriesBloodBag,
                    MedicalIntervention.Blood,
                    bloodRequired,
                    false,
                    false,
                    2.25d));
            }
        }

        internal static void AddHemogenTransfusionDemand(
            Pawn patient,
            ICollection<MedicalResourceDemand> demands)
        {
            if (!Compatibility.HasHemogenTransfusionNeed(patient) || Compatibility.HemogenPack == null)
            {
                return;
            }

            demands.Add(new MedicalResourceDemand(
                Compatibility.HemogenPack,
                MedicalIntervention.HemogenTransfusion,
                Compatibility.HemogenPacksRequired(patient),
                false,
                false,
                1.6d));
        }

        private static bool HasHediff(Pawn patient, string defName)
        {
            return patient.health.hediffSet.hediffs.Any(hediff => hediff.def.defName == defName);
        }
    }

    internal sealed class MedicalTreatmentOption
    {
        public static readonly MedicalTreatmentOption Invalid = new MedicalTreatmentOption(
            MedicalIntervention.None, null, 0, false, false, 0d, 0d);

        public readonly MedicalIntervention Intervention;
        public readonly Thing Resource;
        public readonly int Count;
        public readonly bool FromInventory;
        public readonly bool Reusable;
        public readonly double Benefit;
        public readonly double RouteDistance;

        public bool IsValid => Intervention != MedicalIntervention.None;

        public MedicalTreatmentOption(
            MedicalIntervention intervention,
            Thing resource,
            int count,
            bool fromInventory,
            bool reusable,
            double benefit,
            double routeDistance)
        {
            Intervention = intervention;
            Resource = resource;
            Count = Math.Max(0, count);
            FromInventory = fromInventory;
            Reusable = reusable;
            Benefit = benefit;
            RouteDistance = routeDistance;
        }
    }

    internal sealed class MedicalKitBundle
    {
        public readonly IReadOnlyList<ThingCount> Items;
        public readonly IReadOnlyList<Pawn> PlannedPatients;
        public int TotalCount => Items.Sum(item => item.Count);
        public bool IsEmpty => Items.Count == 0;

        public MedicalKitBundle(IEnumerable<ThingCount> items, IEnumerable<Pawn> plannedPatients = null)
        {
            Items = items?.Where(item => item.Thing != null && item.Count > 0).ToList()
                    ?? new List<ThingCount>();
            PlannedPatients = plannedPatients?.Where(patient => patient != null).Distinct().ToList()
                              ?? new List<Pawn>();
        }
    }

    internal sealed class MedicalResourceLedger
    {
        private static int FieldSupplyRadiusSquared =>
            SearchAndRescueMod.Settings?.FieldSupplyRadiusSquared ?? 64;
        // Match vanilla Toils_Tend reservations so multiple doctors/logistics workers can
        // take disjoint counts from one stack and can coexist with ordinary TendPatient jobs.
        internal const int SharedStackReservationMaxPawns = Toils_Tend.MaxMedicineReservations;

        private readonly Map map;
        private readonly Dictionary<Thing, List<ResourceClaim>> claims =
            new Dictionary<Thing, List<ResourceClaim>>();
        private readonly Dictionary<ThingDef, double> consumableScarcitySnapshot =
            new Dictionary<ThingDef, double>();
        private readonly Dictionary<ThingDef, double> reusableScarcitySnapshot =
            new Dictionary<ThingDef, double>();
        private readonly Dictionary<PickupAccessKey, bool> pickupAccessSnapshot =
            new Dictionary<PickupAccessKey, bool>();
        private List<FieldSupplyReference> fieldSupplyReferences = new List<FieldSupplyReference>();
        private readonly Dictionary<Thing, List<FieldSupplyReference>> fieldSupplyReferencesBySupply =
            new Dictionary<Thing, List<FieldSupplyReference>>();
        private readonly Dictionary<Pawn, List<FieldSupplyReference>> fieldSupplyReferencesByPatient =
            new Dictionary<Pawn, List<FieldSupplyReference>>();
        private bool fieldSupplyReferenceIndexDirty = true;
        private bool schedulingSnapshotActive;

        public MedicalResourceLedger(Map map)
        {
            this.map = map;
        }

        public void BeginSchedulingSnapshot()
        {
            consumableScarcitySnapshot.Clear();
            reusableScarcitySnapshot.Clear();
            pickupAccessSnapshot.Clear();
            schedulingSnapshotActive = true;
        }

        public void EndSchedulingSnapshot()
        {
            schedulingSnapshotActive = false;
            consumableScarcitySnapshot.Clear();
            reusableScarcitySnapshot.Clear();
            pickupAccessSnapshot.Clear();
        }

        public void Cleanup(int now)
        {
            foreach (Thing thing in claims.Keys.ToList())
            {
                claims[thing].RemoveAll(claim => claim.ExpiresAt <= now || claim.Worker == null ||
                    claim.Worker.Destroyed || claim.Worker.Dead || claim.Worker.MapHeld != map ||
                    claim.Worker.Downed || claim.Worker.InMentalState ||
                    claim.Worker.health?.capacities?.CapableOf(PawnCapacityDefOf.Manipulation) != true ||
                    claim.Patient == null || claim.Patient.Destroyed || claim.Patient.Dead ||
                    claim.Patient.MapHeld != map);
                if (claims[thing].Count == 0 || thing == null || thing.Destroyed)
                {
                    claims.Remove(thing);
                }
            }

            if (fieldSupplyReferences.RemoveAll(reference => !FieldSupplyReferenceValid(reference)) > 0)
            {
                InvalidateFieldSupplyReferenceIndex();
            }
        }

        public void ClearTransientClaims()
        {
            claims.Clear();
        }

        public void ExposeData()
        {
            Scribe_Collections.Look(
                ref fieldSupplyReferences,
                "searchAndRescueFieldSupplyReferences",
                LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                fieldSupplyReferences = fieldSupplyReferences?
                    .Where(reference => reference != null)
                    .ToList() ?? new List<FieldSupplyReference>();
                InvalidateFieldSupplyReferenceIndex();
            }
        }

        public void RegisterFieldSupply(Thing supply, Pawn patient, int count)
        {
            if (supply == null || supply.Destroyed || !supply.Spawned || supply.Map != map ||
                supply.IsForbidden(Faction.OfPlayer) || patient == null || patient.Destroyed ||
                patient.Dead || patient.MapHeld != map)
            {
                return;
            }

            AddFieldSupplyReference(supply, patient, count);

            if (claims.TryGetValue(supply, out List<ResourceClaim> existingClaims))
            {
                // Drop-near can merge the delivery into a stack that another treatment
                // already claimed. Preserve those pre-existing owners rather than turning
                // a valid in-flight treatment into an unrelated-stack access violation.
                foreach (ResourceClaim claim in existingClaims.Where(claim => claim.Patient != patient))
                {
                    AddFieldSupplyReference(supply, claim.Patient, claim.Count);
                }
            }
        }

        public void RetainPatientFieldSupplyReferences(Pawn patient, Func<Thing, bool> retain)
        {
            if (patient == null)
            {
                return;
            }

            if (fieldSupplyReferences.RemoveAll(reference =>
                reference.Patient == patient &&
                (reference.Supply == null || retain == null || !retain(reference.Supply))) > 0)
            {
                InvalidateFieldSupplyReferenceIndex();
            }
        }

        public bool ReleaseFieldSupplyReferences(Thing supply)
        {
            if (supply == null)
            {
                return false;
            }

            bool removed = fieldSupplyReferences.RemoveAll(reference => reference.Supply == supply) > 0;
            if (removed)
            {
                InvalidateFieldSupplyReferenceIndex();
            }
            return removed;
        }

        public void ForgetDestroyedSupply(Thing supply)
        {
            if (supply == null)
            {
                return;
            }

            fieldSupplyReferences.RemoveAll(reference => reference.Supply == supply);
            InvalidateFieldSupplyReferenceIndex();
            claims.Remove(supply);
        }

        public int ReconcileNearbyFieldSupplyReferences(
            Pawn patient,
            IEnumerable<Thing> orderedSupplies,
            int requestedCount,
            Func<Thing, bool> referenceScope)
        {
            if (patient == null || requestedCount <= 0 || referenceScope == null)
            {
                return 0;
            }

            List<Thing> candidates = orderedSupplies?
                .Where(supply => supply != null && !supply.Destroyed && supply.Spawned &&
                                 supply.Map == map && !supply.IsForbidden(Faction.OfPlayer) &&
                                 referenceScope(supply))
                .Distinct()
                .ToList() ?? new List<Thing>();
            Dictionary<Thing, int> desired = new Dictionary<Thing, int>();
            int remaining = requestedCount;
            foreach (Thing supply in candidates)
            {
                int committedToOtherPatients = CommittedCountForOtherPatients(supply, patient);
                int capacity = Math.Max(
                    0,
                    supply.stackCount - committedToOtherPatients);
                int allocated = Math.Min(remaining, capacity);
                if (allocated > 0)
                {
                    desired[supply] = allocated;
                    remaining -= allocated;
                }
                if (remaining <= 0)
                {
                    break;
                }
            }

            // Reconcile rather than only appending. A falling wound budget, changed medicine
            // policy, or a nearer replacement stack must release obsolete quota immediately.
            bool indexChanged = fieldSupplyReferences.RemoveAll(reference =>
                reference.Patient == patient && reference.Supply != null &&
                referenceScope(reference.Supply) && !desired.ContainsKey(reference.Supply)) > 0;
            foreach (KeyValuePair<Thing, int> pair in desired)
            {
                FieldSupplyReference existing = fieldSupplyReferences.FirstOrDefault(reference =>
                    reference.Supply == pair.Key && reference.Patient == patient);
                if (existing == null)
                {
                    fieldSupplyReferences.Add(new FieldSupplyReference(pair.Key, patient, pair.Value));
                    indexChanged = true;
                }
                else
                {
                    existing.Count = pair.Value;
                }
            }

            if (indexChanged)
            {
                InvalidateFieldSupplyReferenceIndex();
            }

            return desired.Values.Sum();
        }

        private void AddFieldSupplyReference(Thing supply, Pawn patient, int count)
        {
            if (patient == null || patient.Destroyed || patient.Dead)
            {
                return;
            }

            FieldSupplyReference existing = fieldSupplyReferences.FirstOrDefault(reference =>
                reference.Supply == supply && reference.Patient == patient);
            if (existing == null)
            {
                fieldSupplyReferences.Add(new FieldSupplyReference(
                    supply,
                    patient,
                    Math.Min(supply.stackCount, Math.Max(1, count))));
                InvalidateFieldSupplyReferenceIndex();
                return;
            }

            existing.Count = Math.Min(supply.stackCount, existing.Count + Math.Max(1, count));
        }

        public bool IsProtectedFieldSupply(Thing supply)
        {
            return ReferencesForSupply(supply).Any(FieldSupplyReferenceValid);
        }

        public IEnumerable<Thing> ProtectedFieldSupplies()
        {
            EnsureFieldSupplyReferenceIndex();
            return fieldSupplyReferencesBySupply
                .Where(pair => pair.Value.Any(FieldSupplyReferenceValid))
                .Select(pair => pair.Key);
        }

        public void ReleasePatientFieldSupplyReferences(Pawn patient)
        {
            if (patient != null)
            {
                if (fieldSupplyReferences.RemoveAll(reference => reference.Patient == patient) > 0)
                {
                    InvalidateFieldSupplyReferenceIndex();
                }
            }
        }

        public int ReferencedCountForPatient(Pawn patient, Func<Thing, bool> scope)
        {
            if (patient == null || scope == null)
            {
                return 0;
            }

            int total = 0;
            foreach (IGrouping<Thing, FieldSupplyReference> group in ReferencesForPatient(patient)
                         .Where(reference => FieldSupplyReferenceValid(reference) &&
                                             scope(reference.Supply))
                         .GroupBy(reference => reference.Supply))
            {
                List<FieldSupplyReference> references = ReferencesForSupply(group.Key)
                    .Where(FieldSupplyReferenceValid)
                    .ToList();
                total += FieldSupplyAllocation(group.Key, patient, references);
            }
            return total;
        }

        public bool IsClaimedMedicalSupply(Thing supply)
        {
            return supply != null && !supply.Destroyed && claims.TryGetValue(supply, out List<ResourceClaim> list) &&
                   list.Any(ActiveClaim);
        }

        public bool IsClaimedByOtherWorker(Thing supply, Pawn worker)
        {
            return supply != null && !supply.Destroyed && claims.TryGetValue(supply, out List<ResourceClaim> list) &&
                   list.Any(claim => claim.Worker != worker && ActiveClaim(claim));
        }

        public bool IsFieldSupplyFor(Thing supply, Pawn patient)
        {
            return patient != null && ReferencesForSupply(supply).Any(reference =>
                reference.Patient == patient &&
                FieldSupplyReferenceValid(reference));
        }

        public IEnumerable<Thing> AvailableFieldSupplies(Pawn worker, Pawn patient)
        {
            if (worker == null || patient == null)
            {
                return Enumerable.Empty<Thing>();
            }

            return ReferencesForPatient(patient)
                .Where(FieldSupplyReferenceValid)
                .Select(reference => reference.Supply)
                .Where(supply => supply != null && supply.Spawned && !supply.IsForbidden(worker) &&
                                 AvailableForTreatment(supply, worker, patient) > 0 &&
                                 CanReserveAndReachForPickupCached(worker, supply, 1))
                .Distinct();
        }

        internal static bool CanReserveAndReachForPickup(Pawn worker, Thing thing, int count)
        {
            return worker != null && thing != null && thing.Spawned &&
                   worker.CanReserveAndReach(
                       thing,
                       PathEndMode.ClosestTouch,
                       Danger.Deadly,
                       SharedStackReservationMaxPawns,
                        Math.Max(1, count));
        }

        internal bool CanReserveAndReachForPickupCached(Pawn worker, Thing thing, int count)
        {
            int normalizedCount = Math.Max(1, count);
            PickupAccessKey key = new PickupAccessKey(worker, thing, normalizedCount);
            if (schedulingSnapshotActive && pickupAccessSnapshot.TryGetValue(key, out bool cached))
            {
                SearchAndRescuePerformanceDiagnostics.RecordPickupReachabilityCache(true);
                return cached;
            }

            SearchAndRescuePerformanceDiagnostics.RecordPickupReachabilityCache(false);
            long started = SearchAndRescuePerformanceDiagnostics.Begin(SarPerformancePhase.PickupReachability);
            bool available = CanReserveAndReachForPickup(worker, thing, normalizedCount);
            SearchAndRescuePerformanceDiagnostics.End(SarPerformancePhase.PickupReachability, started);
            if (schedulingSnapshotActive)
            {
                pickupAccessSnapshot[key] = available;
            }
            return available;
        }

        public void ReleaseWorker(Pawn worker)
        {
            if (worker == null)
            {
                return;
            }

            foreach (Thing thing in claims.Keys.ToList())
            {
                claims[thing].RemoveAll(claim => claim.Worker == worker);
                if (claims[thing].Count == 0)
                {
                    claims.Remove(thing);
                }
            }
        }

        public void ReleasePatient(Pawn patient)
        {
            if (patient == null)
            {
                return;
            }

            ReleasePatientClaims(patient);
            if (fieldSupplyReferences.RemoveAll(reference => reference.Patient == patient) > 0)
            {
                InvalidateFieldSupplyReferenceIndex();
            }
        }

        public void ReleasePatientClaims(Pawn patient)
        {
            if (patient == null)
            {
                return;
            }

            foreach (Thing thing in claims.Keys.ToList())
            {
                claims[thing].RemoveAll(claim => claim.Patient == patient);
                if (claims[thing].Count == 0)
                {
                    claims.Remove(thing);
                }
            }
        }

        public bool TryClaim(
            Thing thing,
            Pawn worker,
            Pawn patient,
            int count,
            bool reusable,
            int expiresAt,
            MedicalResourceAccess access)
        {
            if (thing == null || thing.Destroyed || worker == null || patient == null)
            {
                return false;
            }

            int requested = reusable ? 1 : Math.Max(1, count);
            int available = access == MedicalResourceAccess.Relocation
                ? AvailableForRelocation(thing, worker)
                : AvailableForTreatment(thing, worker, patient);
            if (available < requested)
            {
                return false;
            }

            if (!claims.TryGetValue(thing, out List<ResourceClaim> list))
            {
                list = new List<ResourceClaim>();
                claims.Add(thing, list);
            }
            list.Add(new ResourceClaim(worker, patient, requested, reusable, expiresAt));
            return true;
        }

        public int AvailableForTreatment(Thing thing, Pawn worker, Pawn patient)
        {
            if (thing == null || thing.Destroyed)
            {
                return 0;
            }

            List<FieldSupplyReference> validReferences = ReferencesForSupply(thing)
                .Where(FieldSupplyReferenceValid)
                .ToList();
            if (validReferences.Count > 0)
            {
                int allocation = FieldSupplyAllocation(thing, patient, validReferences);
                if (allocation <= 0)
                {
                    return 0;
                }

                // A protected merged stack is divided by its durable per-patient reference
                // counts. Claims belonging to another patient consume that patient's own
                // allocation and must not make this patient's disjoint quota disappear.
                return Math.Max(0, allocation - ClaimedCount(thing, worker, patient));
            }

            return Math.Max(0, thing.stackCount - ClaimedCount(thing, worker));
        }

        private static int FieldSupplyAllocation(
            Thing supply,
            Pawn patient,
            IReadOnlyCollection<FieldSupplyReference> validReferences)
        {
            if (supply == null || patient == null || validReferences == null || validReferences.Count == 0)
            {
                return 0;
            }

            List<IGrouping<Pawn, FieldSupplyReference>> groups = validReferences
                .Where(reference => reference.Patient != null && reference.Count > 0)
                .GroupBy(reference => reference.Patient)
                .OrderBy(group => group.Key.thingIDNumber)
                .ToList();
            int requestedTotal = groups.Sum(group => group.Sum(reference => reference.Count));
            int availableTotal = Math.Max(0, supply.stackCount);
            if (requestedTotal <= 0 || availableTotal <= 0)
            {
                return 0;
            }

            // Normally the sum of references exactly matches the delivered stack. If some
            // of a merged stack was consumed between coordinator passes, apportion the
            // remaining units proportionally and distribute the integer remainder in stable
            // pawn-id order. This keeps allocations disjoint without depending on list order.
            int distributable = Math.Min(availableTotal, requestedTotal);
            Dictionary<Pawn, int> allocations = groups.ToDictionary(
                group => group.Key,
                group => (int)((long)distributable * group.Sum(reference => reference.Count) /
                               requestedTotal));
            int remainder = distributable - allocations.Values.Sum();
            foreach (IGrouping<Pawn, FieldSupplyReference> group in groups)
            {
                if (remainder-- <= 0)
                {
                    break;
                }
                allocations[group.Key]++;
            }

            return allocations.TryGetValue(patient, out int allocated) ? allocated : 0;
        }

        public int AvailableForRelocation(Thing thing, Pawn worker = null)
        {
            if (thing == null || thing.Destroyed)
            {
                return 0;
            }

            return Math.Max(0, thing.stackCount - ProtectedOrClaimedCount(thing, worker));
        }

        public Thing FindBest(
            Pawn worker,
            Pawn patient,
            ThingDef def,
            bool reusable,
            int count = 1,
            bool inventoryOnly = false,
            bool allowPatientInventory = true)
        {
            if (worker == null || patient == null || def == null)
            {
                return null;
            }

            IEnumerable<Thing> inventory = worker.inventory?.innerContainer
                ?.Where(thing => thing.def == def &&
                                 AvailableForTreatment(thing, worker, patient) >= (reusable ? 1 : count))
                ?? Enumerable.Empty<Thing>();
            Thing held = inventory.FirstOrDefault();
            if (held != null || inventoryOnly)
            {
                return held;
            }

            Thing patientHeld = allowPatientInventory
                ? patient.inventory?.innerContainer
                    ?.FirstOrDefault(thing => thing.def == def &&
                        AvailableForTreatment(thing, worker, patient) >= (reusable ? 1 : count))
                : null;
            if (patientHeld != null)
            {
                // Some field interventions intentionally consume the casualty's own kit
                // (Emergency Transfusions is the canonical case). Do not force a detour or
                // reject a hostile-faction prisoner merely because the holder is the patient.
                return patientHeld;
            }

            IEnumerable<Thing> onMap = map.listerThings.ThingsOfDef(def)
                .Where(thing => thing.Spawned && !thing.IsForbidden(worker) &&
                                AvailableForTreatment(thing, worker, patient) >= (reusable ? 1 : count) &&
                                CanReserveAndReachForPickupCached(worker, thing, reusable ? 1 : count));
            return onMap
                .Concat(AvailableInOtherPawnInventories(worker, patient, def, reusable, count))
                .OrderBy(thing => worker.Position.DistanceToSquared(thing.PositionHeld) +
                                  thing.PositionHeld.DistanceToSquared(patient.Position))
                .FirstOrDefault();
        }

        public Thing FindBestOnMap(Pawn worker, Pawn patient, ThingDef def, bool reusable, int count = 1)
        {
            return AvailableOnMap(worker, patient, def, reusable, count).FirstOrDefault();
        }

        public IEnumerable<Thing> AvailableOnMap(
            Pawn worker,
            Pawn patient,
            ThingDef def,
            bool reusable,
            int count = 1)
        {
            if (worker == null || patient == null || def == null)
            {
                return Enumerable.Empty<Thing>();
            }
            return map.listerThings.ThingsOfDef(def)
                .Where(thing => thing.Spawned && !thing.IsForbidden(worker) &&
                                AvailableForTreatment(thing, worker, patient) >= (reusable ? 1 : count) &&
                                CanReserveAndReachForPickupCached(worker, thing, reusable ? 1 : count))
                .OrderBy(thing => worker.Position.DistanceToSquared(thing.Position) +
                                  thing.Position.DistanceToSquared(patient.Position));
        }

        public IEnumerable<Thing> AvailableForRestock(
            Pawn worker,
            Pawn patient,
            ThingDef def,
            bool reusable,
            int count = 1)
        {
            int needed = reusable ? 1 : Math.Max(1, count);
            IEnumerable<Thing> unreferencedMapSupplies = map.listerThings.ThingsOfDef(def)
                .Where(thing => thing.Spawned && !thing.IsForbidden(worker) &&
                                AvailableForRelocation(thing, worker) >= needed &&
                                CanReserveAndReachForPickupCached(worker, thing, needed))
                .OrderBy(thing => worker.Position.DistanceToSquared(thing.Position) +
                                  thing.Position.DistanceToSquared(patient.Position));
            return unreferencedMapSupplies
                .Concat(AvailableInOtherPawnInventories(worker, patient, def, reusable, count));
        }

        public IEnumerable<Thing> AvailableMedicines(Pawn worker, Pawn patient)
        {
            if (worker == null || patient == null)
            {
                yield break;
            }

            IEnumerable<Thing> inventory = worker.inventory?.innerContainer
                ?.Where(thing => thing.def.IsMedicine) ?? Enumerable.Empty<Thing>();
            foreach (Thing thing in inventory.Where(thing => Compatibility.AllowsMedicine(patient, thing) &&
                                                              AvailableForTreatment(thing, worker, patient) > 0))
            {
                yield return thing;
            }

            // Patient-held medicine is a direct source for CE stabilization and supported
            // tending/device drivers. Keep it outside the same-faction donor filter: a
            // prisoner, neutral casualty or animal may legitimately supply their own dose.
            if (patient != worker)
            {
                IEnumerable<Thing> patientInventory = patient.inventory?.innerContainer
                    ?.Where(thing => thing.def.IsMedicine) ?? Enumerable.Empty<Thing>();
                foreach (Thing thing in patientInventory.Where(thing =>
                             Compatibility.AllowsMedicine(patient, thing) &&
                             AvailableForTreatment(thing, worker, patient) > 0))
                {
                    yield return thing;
                }
            }

            foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.Medicine)
                         .Where(thing => thing.Spawned && !thing.IsForbidden(worker) &&
                                         Compatibility.AllowsMedicine(patient, thing) &&
                                         AvailableForTreatment(thing, worker, patient) > 0 &&
                                         CanReserveAndReachForPickupCached(worker, thing, 1)))
            {
                yield return thing;
            }


            foreach (Thing thing in AvailableInOtherPawnInventories(worker, patient, null, false, 1)
                         .Where(thing => thing.def.IsMedicine && Compatibility.AllowsMedicine(patient, thing)))
            {
                yield return thing;
            }
        }

        public double ScarcityPrice(Thing thing, bool reusable)
        {
            if (thing == null)
            {
                return 0d;
            }

            Dictionary<ThingDef, double> snapshot = reusable
                ? reusableScarcitySnapshot
                : consumableScarcitySnapshot;
            if (schedulingSnapshotActive && snapshot.TryGetValue(thing.def, out double cached))
            {
                return cached;
            }

            int mapTotal = reusable
                ? map.listerThings.ThingsOfDef(thing.def).Count(candidate => candidate.Spawned && !candidate.Destroyed)
                : map.listerThings.ThingsOfDef(thing.def).Where(candidate => candidate.Spawned && !candidate.Destroyed)
                    .Sum(candidate => candidate.stackCount);
            int carriedTotal = map.mapPawns.AllPawnsSpawned
                .SelectMany(pawn => pawn.inventory?.innerContainer ?? Enumerable.Empty<Thing>())
                .Where(candidate => candidate.def == thing.def && !candidate.Destroyed)
                .Sum(candidate => reusable ? 1 : candidate.stackCount);
            int total = mapTotal + carriedTotal;
            double price = (reusable ? 45000d : 18000d) / Math.Max(1, total);
            if (schedulingSnapshotActive)
            {
                snapshot[thing.def] = price;
            }
            return price;
        }

        private int ClaimedCount(Thing thing, Pawn excludingWorker)
        {
            if (!claims.TryGetValue(thing, out List<ResourceClaim> list))
            {
                return 0;
            }
            return list.Where(claim => claim.Worker != excludingWorker && ActiveClaim(claim))
                .Sum(claim => claim.Count);
        }

        private bool ActiveClaim(ResourceClaim claim)
        {
            return claim?.Worker != null && !claim.Worker.Destroyed && !claim.Worker.Dead &&
                   claim.Worker.MapHeld == map && claim.Patient != null &&
                   !claim.Patient.Destroyed && !claim.Patient.Dead && claim.Patient.MapHeld == map &&
                   claim.ExpiresAt > Find.TickManager.TicksGame;
        }

        private readonly struct PickupAccessKey : IEquatable<PickupAccessKey>
        {
            private readonly Pawn worker;
            private readonly Thing thing;
            private readonly int count;

            public PickupAccessKey(Pawn worker, Thing thing, int count)
            {
                this.worker = worker;
                this.thing = thing;
                this.count = count;
            }

            public bool Equals(PickupAccessKey other)
            {
                return worker == other.worker && thing == other.thing && count == other.count;
            }

            public override bool Equals(object obj)
            {
                return obj is PickupAccessKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = worker?.GetHashCode() ?? 0;
                    hash = hash * 397 ^ (thing?.GetHashCode() ?? 0);
                    return hash * 397 ^ count;
                }
            }
        }

        private int ClaimedCount(Thing thing, Pawn excludingWorker, Pawn patient)
        {
            if (!claims.TryGetValue(thing, out List<ResourceClaim> list))
            {
                return 0;
            }
            return list.Where(claim => claim.Worker != excludingWorker && claim.Patient == patient &&
                                       ActiveClaim(claim))
                .Sum(claim => claim.Count);
        }

        private IEnumerable<Thing> AvailableInOtherPawnInventories(
            Pawn worker,
            Pawn patient,
            ThingDef def,
            bool reusable,
            int count)
        {
            if (worker == null || patient == null)
            {
                return Enumerable.Empty<Thing>();
            }

            int needed = reusable ? 1 : Math.Max(1, count);
            return map.mapPawns.AllPawnsSpawned
                .Where(holder => holder != worker && holder.Faction == worker.Faction &&
                                 holder.inventory != null && !holder.Destroyed)
                .SelectMany(holder => holder.inventory.innerContainer
                    .Where(thing => (def == null || thing.def == def) &&
                                    AvailableForRelocation(thing, worker) >= needed &&
                                    CanTakeFromInventoryHolder(worker, holder, thing, needed, patient))
                    .Select(thing => new { Holder = holder, Thing = thing }))
                .OrderBy(candidate => worker.Position.DistanceToSquared(candidate.Holder.Position) +
                                      candidate.Holder.Position.DistanceToSquared(patient.Position))
                .Select(candidate => candidate.Thing);
        }

        internal static Pawn InventoryHolder(Thing thing)
        {
            return (thing?.holdingOwner?.Owner as Pawn_InventoryTracker)?.pawn;
        }

        internal static bool CanTakeFromInventoryHolder(
            Pawn worker,
            Pawn holder,
            Thing thing,
            int count,
            Pawn patient = null)
        {
            if (worker == null || holder == null || holder == worker || thing == null ||
                count <= 0 || holder.Destroyed || holder.Dead || !holder.Spawned ||
                holder.Map != worker.Map || holder.Faction != worker.Faction ||
                holder.inventory?.innerContainer.Contains(thing) != true ||
                thing.Destroyed || thing.stackCount < count || IsBeingUsedByHolder(holder, thing))
            {
                return false;
            }

            if (Compatibility.IsVehiclePawn(holder))
            {
                return Compatibility.VehicleCargoSourceAvailable(holder, worker);
            }

            bool holderIsPatient = holder == patient;
            bool activeManagedDonor = holder.Map?.GetComponent<SearchAndRescueCoordinator>()
                                          ?.IsActiveJob(holder, holder.CurJob) == true;
            if (holder.InMentalState || holder.mindState?.duty != null ||
                holder.carryTracker?.CarriedThing is Pawn ||
                holder.Downed && !holderIsPatient ||
                holder.Drafted && !activeManagedDonor)
            {
                return false;
            }

            return worker.CanReach(holder, PathEndMode.Touch, Danger.Deadly);
        }

        internal static bool TryTransferFromInventoryHolder(
            Pawn holder,
            Pawn receiver,
            Thing resource,
            int count,
            bool toCarryTracker,
            Pawn patient = null)
        {
            if (!CanTakeFromInventoryHolder(receiver, holder, resource, count, patient) ||
                toCarryTracker && receiver.carryTracker?.CarriedThing != null)
            {
                return false;
            }

            ThingOwner destination = toCarryTracker
                ? receiver.carryTracker.innerContainer
                : receiver.inventory?.innerContainer;
            if (destination == null)
            {
                return false;
            }

            if (!Compatibility.IsVehiclePawn(holder))
            {
                int transferred = holder.inventory.innerContainer.TryTransferToContainer(
                    resource,
                    destination,
                    count,
                    out Thing _,
                    true);
                return transferred >= count;
            }

            // Vehicle Framework maintains cargo mass/stat caches through its public API.
            // A raw ThingOwner transfer works superficially but skips CargoRemoved.
            Thing taken = Compatibility.TakeFromVehicleCargo(holder, resource, count);
            if (taken == null || taken.stackCount != count)
            {
                if (taken != null)
                {
                    Compatibility.ReturnToVehicleCargo(holder, taken);
                }
                return false;
            }

            if (destination.TryAdd(taken, true))
            {
                return true;
            }

            if (!Compatibility.ReturnToVehicleCargo(holder, taken) && holder.Spawned && holder.Map != null)
            {
                GenPlace.TryPlaceThing(taken, holder.Position, holder.Map, ThingPlaceMode.Near);
            }
            return false;
        }

        internal static bool IsBeingUsedByHolder(Pawn holder, Thing thing)
        {
            Job current = holder?.CurJob;
            if (current == null || thing == null)
            {
                return false;
            }

            if (current.targetA.Thing == thing || current.targetB.Thing == thing || current.targetC.Thing == thing)
            {
                return true;
            }

            return current.targetQueueA?.Any(target => target.Thing == thing) == true ||
                   current.targetQueueB?.Any(target => target.Thing == thing) == true;
        }

        private int CommittedCountForOtherPatients(Thing supply, Pawn patient)
        {
            return CommittedCount(supply, patient, null);
        }

        private int ProtectedOrClaimedCount(Thing supply, Pawn excludingWorker)
        {
            return CommittedCount(supply, null, excludingWorker);
        }

        private int CommittedCount(Thing supply, Pawn excludingPatient, Pawn excludingWorker)
        {
            IReadOnlyList<FieldSupplyReference> references = ReferencesForSupply(supply);
            claims.TryGetValue(supply, out List<ResourceClaim> supplyClaims);
            int committed = 0;

            for (int index = 0; index < references.Count; index++)
            {
                FieldSupplyReference reference = references[index];
                Pawn patient = reference?.Patient;
                if (patient == null || patient == excludingPatient ||
                    !FieldSupplyReferenceValid(reference) ||
                    EarlierReferenceForPatient(references, index, patient))
                {
                    continue;
                }

                int referenceCount = 0;
                for (int other = index; other < references.Count; other++)
                {
                    FieldSupplyReference candidate = references[other];
                    if (candidate?.Patient == patient && FieldSupplyReferenceValid(candidate))
                    {
                        referenceCount += candidate.Count;
                    }
                }

                int claimCount = ActiveClaimCountForPatient(
                    supplyClaims,
                    patient,
                    excludingWorker);
                // A treatment claim against a field stack consumes that patient's durable
                // allocation; it is not an additional reservation on top of the reference.
                committed += Math.Max(referenceCount, claimCount);
            }

            if (supplyClaims == null)
            {
                return committed;
            }

            for (int index = 0; index < supplyClaims.Count; index++)
            {
                ResourceClaim claim = supplyClaims[index];
                Pawn patient = claim?.Patient;
                if (patient == null || patient == excludingPatient ||
                    claim.Worker == excludingWorker || !ActiveClaim(claim) ||
                    HasValidReferenceForPatient(references, patient) ||
                    EarlierActiveClaimForPatient(supplyClaims, index, patient, excludingWorker))
                {
                    continue;
                }

                committed += ActiveClaimCountForPatient(
                    supplyClaims,
                    patient,
                    excludingWorker);
            }

            return committed;
        }

        private bool HasValidReferenceForPatient(
            IReadOnlyList<FieldSupplyReference> references,
            Pawn patient)
        {
            for (int index = 0; index < references.Count; index++)
            {
                FieldSupplyReference reference = references[index];
                if (reference?.Patient == patient && FieldSupplyReferenceValid(reference))
                {
                    return true;
                }
            }
            return false;
        }

        private bool EarlierReferenceForPatient(
            IReadOnlyList<FieldSupplyReference> references,
            int exclusiveEnd,
            Pawn patient)
        {
            for (int index = 0; index < exclusiveEnd; index++)
            {
                FieldSupplyReference reference = references[index];
                if (reference?.Patient == patient && FieldSupplyReferenceValid(reference))
                {
                    return true;
                }
            }
            return false;
        }

        private bool EarlierActiveClaimForPatient(
            IReadOnlyList<ResourceClaim> supplyClaims,
            int exclusiveEnd,
            Pawn patient,
            Pawn excludingWorker)
        {
            for (int index = 0; index < exclusiveEnd; index++)
            {
                ResourceClaim claim = supplyClaims[index];
                if (claim?.Patient == patient && claim.Worker != excludingWorker &&
                    ActiveClaim(claim))
                {
                    return true;
                }
            }
            return false;
        }

        private int ActiveClaimCountForPatient(
            IReadOnlyList<ResourceClaim> supplyClaims,
            Pawn patient,
            Pawn excludingWorker)
        {
            if (supplyClaims == null)
            {
                return 0;
            }

            int count = 0;
            for (int index = 0; index < supplyClaims.Count; index++)
            {
                ResourceClaim claim = supplyClaims[index];
                if (claim?.Patient == patient && claim.Worker != excludingWorker &&
                    ActiveClaim(claim))
                {
                    count += claim.Count;
                }
            }
            return count;
        }

        private IReadOnlyList<FieldSupplyReference> ReferencesForSupply(Thing supply)
        {
            if (supply == null)
            {
                return Array.Empty<FieldSupplyReference>();
            }

            EnsureFieldSupplyReferenceIndex();
            return fieldSupplyReferencesBySupply.TryGetValue(
                supply,
                out List<FieldSupplyReference> references)
                ? references
                : Array.Empty<FieldSupplyReference>();
        }

        private IReadOnlyList<FieldSupplyReference> ReferencesForPatient(Pawn patient)
        {
            if (patient == null)
            {
                return Array.Empty<FieldSupplyReference>();
            }

            EnsureFieldSupplyReferenceIndex();
            return fieldSupplyReferencesByPatient.TryGetValue(
                patient,
                out List<FieldSupplyReference> references)
                ? references
                : Array.Empty<FieldSupplyReference>();
        }

        private void InvalidateFieldSupplyReferenceIndex()
        {
            fieldSupplyReferenceIndexDirty = true;
        }

        private void EnsureFieldSupplyReferenceIndex()
        {
            if (!fieldSupplyReferenceIndexDirty)
            {
                return;
            }

            fieldSupplyReferencesBySupply.Clear();
            fieldSupplyReferencesByPatient.Clear();
            foreach (FieldSupplyReference reference in fieldSupplyReferences)
            {
                if (reference?.Supply != null)
                {
                    if (!fieldSupplyReferencesBySupply.TryGetValue(
                            reference.Supply,
                            out List<FieldSupplyReference> supplyReferences))
                    {
                        supplyReferences = new List<FieldSupplyReference>();
                        fieldSupplyReferencesBySupply.Add(reference.Supply, supplyReferences);
                    }
                    supplyReferences.Add(reference);
                }

                if (reference?.Patient != null)
                {
                    if (!fieldSupplyReferencesByPatient.TryGetValue(
                            reference.Patient,
                            out List<FieldSupplyReference> patientReferences))
                    {
                        patientReferences = new List<FieldSupplyReference>();
                        fieldSupplyReferencesByPatient.Add(reference.Patient, patientReferences);
                    }
                    patientReferences.Add(reference);
                }
            }
            fieldSupplyReferenceIndexDirty = false;
        }

        private bool FieldSupplyReferenceValid(FieldSupplyReference reference)
        {
            Thing supply = reference?.Supply;
            Pawn patient = reference?.Patient;
            return reference != null && reference.Count > 0 && supply != null && !supply.Destroyed &&
                   supply.Spawned && supply.Map == map && !supply.IsForbidden(Faction.OfPlayer) &&
                   patient != null && !patient.Destroyed && !patient.Dead &&
                   patient.Spawned && patient.Map == map &&
                   map.GetComponent<SearchAndRescueCoordinator>()?.RetainsFieldSupplyReference(patient) == true &&
                   supply.Position.DistanceToSquared(patient.Position) <= FieldSupplyRadiusSquared;
        }

        private sealed class FieldSupplyReference : IExposable
        {
            public Thing Supply;
            public Pawn Patient;
            public int Count;

            public FieldSupplyReference()
            {
            }

            public FieldSupplyReference(Thing supply, Pawn patient, int count)
            {
                Supply = supply;
                Patient = patient;
                Count = count;
            }

            public void ExposeData()
            {
                Scribe_References.Look(ref Supply, "supply");
                Scribe_References.Look(ref Patient, "patient");
                Scribe_Values.Look(ref Count, "count", 1);
            }
        }

        private sealed class ResourceClaim
        {
            public readonly Pawn Worker;
            public readonly Pawn Patient;
            public readonly int Count;
            public readonly bool Reusable;
            public readonly int ExpiresAt;

            public ResourceClaim(Pawn worker, Pawn patient, int count, bool reusable, int expiresAt)
            {
                Worker = worker;
                Patient = patient;
                Count = count;
                Reusable = reusable;
                ExpiresAt = expiresAt;
            }
        }
    }
}
