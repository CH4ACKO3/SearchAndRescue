using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

static class Rh2RoundProbe
{
    static Type T(string name) => AccessTools.TypeByName("SearchAndRescue." + name);
    internal static void Run(Map map, Action<bool, string> check)
    {
        JobDef firstAid = DefDatabase<JobDef>.GetNamedSilentFail("CP_FirstAid");
        if (firstAid == null) return;
        Pawn doctor = Spawn(map, "RH2 Round Doctor");
        Pawn patient = Spawn(map, "RH2 Round Patient");
        foreach (WorkTypeDef work in new[] { WorkTypeDefOf.Doctor, DefDatabase<WorkTypeDef>.GetNamed("SAR_FieldRescue") })
            AccessTools.Method(T("Compatibility"), "SetWorkPriorityForMigration").Invoke(null, new object[] { doctor, work, 1 });
        IntVec3 origin = map.AllCells.Where(cell => cell.Standable(map) &&
            (cell + IntVec3.East).InBounds(map) && (cell + IntVec3.East).Standable(map))
            .OrderBy(cell => cell.DistanceToSquared(map.Center)).First();
        doctor.Position = origin; patient.Position = origin + IntVec3.East;
        doctor.Position = patient.InteractionCell;
        map.fogGrid.Unfog(doctor.Position); map.fogGrid.Unfog(patient.Position);
        patient.playerSettings.medCare = MedicalCareCategory.NoMeds;
        Hediff cut = HediffMaker.MakeHediff(HediffDefOf.Cut, patient, patient.RaceProps.body.corePart);
        cut.Severity = 4; patient.health.AddHediff(cut);
        Hediff blood = HediffMaker.MakeHediff(HediffDefOf.BloodLoss, patient);
        blood.Severity = .2f; patient.health.AddHediff(blood);
        patient.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait_MaintainPosture, 3000));
        patient.jobs.posture = PawnPosture.LayingOnGroundNormal;
        map.designationManager.AddDesignation(new Designation(patient, DefDatabase<DesignationDef>.GetNamed("SAR_Treat")));
        object coordinator = map.components.First(component => component.GetType() == T("SearchAndRescueCoordinator"));
        object claims = AccessTools.Field(coordinator.GetType(), "activeClaims").GetValue(coordinator);
        Job job = JobMaker.MakeJob(firstAid, patient);
        object assignment = Activator.CreateInstance(T("ActiveAssignment"), new object[] {
            doctor, patient, job, Enum.Parse(T("SearchAndRescueStage"), "Treat"), patient.Position,
            Find.TickManager.TicksGame,
            AccessTools.Method(coordinator.GetType(), "CountUntendedHediffs").Invoke(null, new object[] { patient }),
            patient.health.hediffSet.BleedRateTotal,
            AccessTools.Method(T("Compatibility"), "FieldEmergencySeverity").Invoke(null, new object[] { patient }),
            Enum.Parse(T("CareOrigin"), "ManualTreatment"), .2f, 0f });
        try
        {
            AccessTools.Method(claims.GetType(), "Register", new[] { assignment.GetType() }).Invoke(claims, new[] { assignment });
            doctor.jobs.StartJob(job, JobCondition.InterruptForced);
            int identity = job.loadID;
            check(doctor.CurJobDef == firstAid, "RH2 aggregate driver started");
            check(doctor.CanReachImmediate(patient, PathEndMode.InteractionCell), "RH2 fixture reaches the native interaction cell");
            // This synchronous fixture isolates the tend bar; pathfinding normally
            // dispatches its arrival callback on a later game tick.
            doctor.pather.StopDead();
            doctor.jobs.curDriver.Notify_PatherArrived();
            check(doctor.CurJob?.loadID == identity, "RH2 arrival enters the treatment bar (current=" + doctor.CurJobDef?.defName +
                ", end=" + AccessTools.Field(assignment.GetType(), "EndCondition").GetValue(assignment) +
                ", forbidden=" + patient.IsForbidden(doctor) + ", posture=" + patient.GetPosture() + ")");
            var monitor = AccessTools.Method(coordinator.GetType(), "MonitorActiveTreatmentRounds");
            monitor.Invoke(coordinator, null);
            check(doctor.CurJob?.loadID == identity, "unchanged RH2 patient keeps the initial treatment bar");
            blood.Severity -= .001f;
            check(!cut.TryGetComp<HediffComp_TendDuration>().IsTended, "passive recovery fixture has no treated wound");
            monitor.Invoke(coordinator, null);
            check(doctor.CurJob?.loadID == identity, "passive blood-loss recovery does not restart RH2 first aid");
            for (int tick = 0; tick < 2000 && doctor.CurJob?.loadID == identity; tick++)
            {
                doctor.pather.PatherTick();
                doctor.jobs.curDriver.DriverTick();
                monitor.Invoke(coordinator, null);
            }
            check(cut.TryGetComp<HediffComp_TendDuration>().IsTended,
                "RH2 progress bar reaches a real wound treatment (job=" + doctor.CurJobDef?.defName +
                ", toil=" + doctor.jobs.curDriver?.CurToilIndex + ", posture=" + patient.GetPosture() + ")");
            check((int)AccessTools.Field(assignment.GetType(), "CommittedTreatmentRounds").GetValue(assignment) > 0,
                "RH2 native tend callback records the committed round");
            check(doctor.CurJob?.loadID != identity, "RH2 aggregate yields after the committed treatment");
        }
        finally
        {
            AccessTools.Method(claims.GetType(), "ReleasePrimary").Invoke(claims, new object[] { patient });
            map.designationManager.RemoveAllDesignationsOn(patient);
            doctor.Destroy(); patient.Destroy();
        }
    }
    static Pawn Spawn(Map map, string name) => (Pawn)AccessTools.Method(T("CasevacAndBoundaryDiagnostics"), "Spawn")
        .Invoke(null, new object[] { map, name, map.Center });
}
