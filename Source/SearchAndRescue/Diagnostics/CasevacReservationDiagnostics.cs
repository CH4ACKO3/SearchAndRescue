using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using Verse;
using Verse.AI;

namespace SearchAndRescue
{
    internal static partial class CasevacAndBoundaryDiagnostics
    {
        [DebugAction("Search and Rescue", "Run CASEVAC reservation checks", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ReservationChecks()
        {
            Map map = Find.CurrentMap;
            Pawn a = Spawn(map, "Team A", map.Center);
            Pawn b = Spawn(map, "Team B", map.Center);
            Pawn patientA = Spawn(map, "Patient A", map.Center);
            Pawn patientB = Spawn(map, "Patient B", map.Center);
            var sharedBed = (Building_Bed)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("DoubleBed"), ThingDefOf.WoodLog);
            sharedBed.SetFaction(Faction.OfPlayer);
            GenSpawn.Spawn(sharedBed, CellFinder.RandomClosewalkCellNear(map.Center + new IntVec3(20, 0, 0), map, 2), map);
            sharedBed.Medical = true;
            patientA.health.AddHediff(HediffDefOf.Anesthetic);
            try
            {
                Job jobA = Compatibility.MakeCasevacJob(patientA, sharedBed);
                Job jobB = Compatibility.MakeCasevacJob(patientB, sharedBed);
                a.jobs.curJob = jobA;
                b.jobs.curJob = jobB;
                JobDriver driver = jobA.GetCachedDriver(a);
                AccessTools.Field(CasevacDriverCompatibility.DriverType, "rescuers").SetValue(driver, new List<Pawn> { a, b });
                var getter = AccessTools.PropertyGetter(CasevacDriverCompatibility.DriverType, "Rescuers");
                var members = (List<Pawn>)getter.Invoke(driver, null);
                Check(members.Contains(a) && !members.Contains(b), "cached helper on another patient is excluded from the native team");
                Check(a.Reserve(sharedBed, jobA, 2, 0) && b.Reserve(sharedBed, jobB, 2, 0), "two teams reserve independent slots in a shared bed");
                CasevacDriverCompatibility.InitializingDriver = driver;
                try
                {
                    map.reservationManager.Release(sharedBed, a, jobA);
                    map.reservationManager.Release(sharedBed, a, jobA);
                    map.reservationManager.ReleaseAllForTarget(sharedBed);
                    Check(map.reservationManager.ReservedBy(sharedBed, b, jobB), "CASEVAC delivery preserves the other patient's bed reservation");
                    Check(!map.reservationManager.ReservedBy(sharedBed, a, jobA), "repeated CASEVAC bed release leaves no stale reservation");
                }
                finally { CasevacDriverCompatibility.InitializingDriver = null; }
                map.reservationManager.ReleaseAllClaimedBy(a);
                map.reservationManager.ReleaseAllClaimedBy(b);
                a.jobs.curJob = null;
                b.jobs.curJob = null;
                for (int i = 0; i < 8; i++)
                {
                    a.jobs.StartJob(Compatibility.MakeCasevacJob(patientA, sharedBed));
                    a.jobs.StopAll();
                }
                Check(!map.reservationManager.ReservationsReadOnly.Any(r => r.Claimant == a),
                    "repeated solo cancellation leaves no reservation referring to pooled jobs");
                Check(!map.reservationManager.ReservedBy<JobDriver_TakeToBed>(sharedBed, a),
                    "generic bed reservation lookup remains safe after pooled job cleanup");
                if (Compatibility.CasevacBaseTicksPerMove(a) < Compatibility.CasevacBaseTicksPerMove(b))
                {
                    Pawn swap = a; a = b; b = swap;
                }
                a.Position = map.Center;
                patientA.Position = map.Center;
                b.Position = map.Center + new IntVec3(3, 0, 0);
                a.jobs.StartJob(Compatibility.MakeCasevacJob(patientA, sharedBed));
                b.jobs.StartJob(Compatibility.MakeCasevacJob(patientA, sharedBed));
                Check(Compatibility.IsCasevac(a.CurJob) && Compatibility.IsCasevac(b.CurJob),
                    "handoff fixture has two active native CASEVAC jobs before cancellation");
                a.jobs.StopAll();
                Check(Compatibility.IsCasevac(b.CurJob) && map.reservationManager.ReservedBy(sharedBed, b, b.CurJob),
                    "exiting bed owner transfers reservation to the surviving teammate");
                b.jobs.StopAll();
                Check(!map.reservationManager.ReservationsReadOnly.Any(r => r.Target == sharedBed),
                    "last teammate cancellation leaves bed available to the patient");
                b.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait, 600));
                b.Reserve(patientA, b.CurJob);
                a.jobs.StartJob(Compatibility.MakeCasevacJob(patientA, sharedBed));
                JobDriver pickupDriver = a.jobs.curDriver;
                var nativeToils = (IEnumerable<Toil>)AccessTools.Method(CasevacDriverCompatibility.DriverType, "MakeNewToils")
                    .Invoke(pickupDriver, null);
                Toil pickup = nativeToils.First(t => t.debugName == "StartCarryThing");
                pickup.actor = a;
                pickup.initAction();
                Check(patientA.Spawned && map.reservationManager.ReservedBy(patientA, b, b.CurJob) &&
                    !Compatibility.IsCasevac(a.CurJob), "pickup yields when another medical worker has reserved the patient");
            }
            finally
            {
                a.jobs.StopAll(); b.jobs.StopAll();
                map.reservationManager.ReleaseAllClaimedBy(a);
                map.reservationManager.ReleaseAllClaimedBy(b);
                a.jobs.curJob = null;
                b.jobs.curJob = null;
                a.Destroy(); b.Destroy(); patientA.Destroy(); patientB.Destroy(); sharedBed.Destroy();
            }
        }
    }
}
