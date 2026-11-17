using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SearchAndRescue
{
    internal static class MultiLevelRescue
    {
        private const string Strata = "AzraelGodKing.Strata";
        private delegate bool EstimateTrip(Map map, Pawn pawn, IntVec3 from, IntVec3 to, out float length);

        internal static readonly JobDef StrataRescue = DefDatabase<JobDef>.GetNamedSilentFail("Strata_RescueToLevel");
        private static readonly EstimateTrip EstimateAcrossBands = Bind<EstimateTrip>(
            "astryl.AsAboveSoBelow2", "AsAboveSoBelow.ABBandDistance", "TryCrossLevel");
        private static readonly Func<Map, IntVec3, IntVec3, bool> SameBand = Bind<Func<Map, IntVec3, IntVec3, bool>>(
            "astryl.AsAboveSoBelow2", "AsAboveSoBelow.ABBands", "SameBand");
        private static readonly Func<Map, Map, List<MapPortal>> RouteCandidates =
            Bind<Func<Map, Map, List<MapPortal>>>(Strata, "Strata.LevelGraph", "FirstStepCandidates");
        private static readonly Func<bool> StrataScanning = Bind<Func<bool>>(Strata, "Strata.CrossFloorJobScan", "get_IsScanning");
        private static readonly Func<Pawn, Map, IntVec3, Func<Job>, Job> ScanAt =
            Bind<Func<Pawn, Map, IntVec3, Func<Job>, Job>>(Strata, "Strata.CrossFloorJobScan", "ScanAt");
        private static readonly Func<Map, Map, IntVec3, Pawn, IntVec3, MapPortal> FirstStep =
            Bind<Func<Map, Map, IntVec3, Pawn, IntVec3, MapPortal>>(Strata, "Strata.LevelGraph", "BestFirstStepRequiringArrival");
        private static readonly Func<Map, bool> Linked = Bind<Func<Map, bool>>(Strata, "Strata.LevelGraph", "AnyLinkFrom");
        private static readonly Func<Building_Bed, Pawn, IntVec3, bool, bool> UsableBed =
            Bind<Func<Building_Bed, Pawn, IntVec3, bool, bool>>(Strata, "Strata.RescueBedAcrossLevels", "IsUsableRescueBed");
        private static readonly Func<Pawn, bool> CanRelay = Bind<Func<Pawn, bool>>(Strata, "Strata.PawnRelay", "CanRelay");
        private static readonly Func<Pawn, bool> CanWorkRelay = Bind<Func<Pawn, bool>>(Strata, "Strata.StrataPawnUtility", "CanWorkRelay");
        private static readonly FieldInfo Settings = ModsConfig.IsActive(Strata)
            ? AccessTools.Field(AccessTools.TypeByName("Strata.StrataMod"), "Settings") : null;
        private static readonly PropertyInfo WorkRelayActive = Settings == null ? null : AccessTools.Property(Settings.FieldType, "WorkRelayActive");

        internal static bool StrataReady => StrataRescue != null && FirstStep != null && Linked != null && RouteCandidates != null &&
            ScanAt != null && UsableBed != null && StrataScanning != null && CanRelay != null && CanWorkRelay != null && WorkRelayActive != null;
        internal static bool BandsReady => EstimateAcrossBands != null;
        internal static bool InStrataScan => StrataScanning?.Invoke() == true;

        private static T Bind<T>(string package, string typeName, string method) where T : Delegate
        {
            if (!ModsConfig.IsActive(package)) return null;
            var type = AccessTools.TypeByName(typeName);
            return type == null ? null : Delegate.CreateDelegate(typeof(T), type, method, false, false) as T;
        }

        // Check each landing from the cell the worker would actually occupy.
        private static MapPortal RouteTo(Pawn worker, Map destination, IntVec3 target, out IntVec3 landing)
        {
            landing = IntVec3.Invalid;
            Map floor = worker.Map;
            IntVec3 position = worker.Position;
            MapPortal first = null;
            for (int hops = 0; hops < Find.Maps.Count; hops++)
            {
                MapPortal stairs = null;
                void SelectStairs()
                {
                    stairs = FirstStep(floor, destination, position, worker, target);
                    if (stairs != null && (stairs.IsForbidden(worker) || !stairs.IsEnterable(out _))) stairs = null;
                }
                if (first == null) SelectStairs();
                else ScanAt(worker, floor, position, () => { SelectStairs(); return null; });
                if (stairs == null) return null;
                first ??= stairs;
                floor = stairs.GetOtherMap();
                position = stairs.GetDestinationLocation();
                if (floor == null || !position.InBounds(floor)) return null;
                if (floor == destination)
                {
                    landing = position;
                    return first;
                }
            }
            return null;
        }

        internal static Job Approach(Pawn worker)
        {
            if (!StrataReady || InStrataScan || worker?.Map == null || !CanRelay(worker) || !CanWorkRelay(worker) ||
                !WorkerEligibility.WorkerOperational(worker, worker.Map) || !Compatibility.CanPerformRescueWork(worker) ||
                Settings.GetValue(null) is not { } settings || !(bool)WorkRelayActive.GetValue(settings)) return null;
            foreach (Map floor in Find.Maps)
            {
                if (floor == worker.Map || !Linked(floor) || RouteCandidates(worker.Map, floor).Count == 0) continue;
                var coordinator = floor.GetComponent<SearchAndRescueCoordinator>();
                foreach (Pawn patient in floor.mapPawns.AllPawnsSpawned)
                {
                    if (!coordinator.CanProbeRemoteRescue(worker, patient) || coordinator.HasIncomingRescuer(patient, worker)) continue;
                    var stairs = RouteTo(worker, floor, patient.Position, out var landing);
                    if (stairs == null) continue;
                    bool feasible = false;
                    ScanAt(worker, floor, landing, () =>
                    {
                        feasible = worker.CanReserveAndReach(patient, PathEndMode.ClosestTouch, Danger.Deadly) &&
                            RescueDestinationPlanner.TryFind(floor, worker, patient, out _, out _, out _);
                        return null;
                    });
                    if (!feasible) continue;
                    var job = JobMaker.MakeJob(JobDefOf.EnterPortal, stairs, patient);
                    coordinator.TrackStrataTrip(worker, patient, null, job);
                    return job;
                }
            }
            return null;
        }

        internal static bool TryFindStrataBed(Pawn worker, Pawn patient, out Building_Bed bed, out MapPortal portal)
        {
            bed = null;
            portal = null;
            if (!StrataReady || !worker.RaceProps.Humanlike || !Linked(worker.Map) ||
                patient.IsPrisonerOfColony || MechanicalCare.IsPatient(patient) ||
                !HealthAIUtility.WantsToBeRescued(patient) || patient.CurrentBed() != null) return false;

            bool wantsMedical = HealthAIUtility.ShouldSeekMedicalRest(patient) || HealthAIUtility.ShouldSeekMedicalRestUrgent(patient);
            bool needsSupport = Compatibility.NeedsLifeSupportBed(patient);
            int bestRank = int.MaxValue;
            int bestDistance = int.MaxValue;
            foreach (Map floor in Find.Maps)
            {
                if (floor == worker.Map || !Linked(floor) || RouteCandidates(worker.Map, floor).Count == 0) continue;
                var supported = needsSupport ? new HashSet<Building_Bed>(Compatibility.FindLifeSupportBeds(floor)) : null;
                foreach (Thing thing in floor.listerThings.ThingsInGroup(ThingRequestGroup.Bed))
                {
                    if (thing is not Building_Bed candidate || candidate.IsForbidden(worker.Faction) ||
                        Compatibility.IsTemporaryFieldTendBed(candidate)) continue;
                    int rank = supported?.Contains(candidate) == true ? 0 : candidate.Medical == wantsMedical ? 1 : 2;
                    if (rank > bestRank) continue;
                    var stairs = RouteTo(worker, floor, candidate.Position, out var landing);
                    if (stairs == null || !UsableBed(candidate, patient, landing, false) ||
                        !Compatibility.RescueBedHasReservationCapacity(candidate, patient, worker, acrossMaps: true)) continue;
                    int distance = landing.DistanceToSquared(candidate.Position);
                    if (rank == bestRank && distance >= bestDistance) continue;
                    bed = candidate;
                    portal = stairs;
                    bestRank = rank;
                    bestDistance = distance;
                }
            }
            return bed != null;
        }

        internal static double TravelDistance(Pawn worker, IntVec3 from, IntVec3 to) =>
            Math.Sqrt(DistanceSquared(worker, from, to));

        internal static double DistanceSquared(Pawn worker, IntVec3 from, IntVec3 to)
        {
            if (EstimateAcrossBands == null || SameBand?.Invoke(worker.Map, from, to) == true)
                return from.DistanceToSquared(to);
            return worker.Map.GetComponent<SearchAndRescueCoordinator>().BandDistanceSquared(worker, from, to);
        }

        internal static double EstimateBandDistanceSquared(Pawn worker, IntVec3 from, IntVec3 to) =>
            EstimateAcrossBands(worker.Map, worker, from, to, out float length)
                ? (double)length * length : from.DistanceToSquared(to);
    }

    public sealed class WorkGiver_StrataRescueApproach : WorkGiver
    {
        public override Job NonScanJob(Pawn pawn) => MultiLevelRescue.Approach(pawn);
    }
}
