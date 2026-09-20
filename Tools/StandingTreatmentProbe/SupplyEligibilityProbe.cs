using System;
using System.Collections;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

static class SupplyEligibilityProbe
{
    static Type T(string name) => AccessTools.TypeByName("SearchAndRescue." + name);
    internal static void Run(Pawn doctor, Pawn patient, Hediff wound, Action<bool, string> check)
    {
        Map map = patient.Map;
        Type type = T("SearchAndRescueCoordinator");
        object coordinator = map.components.First(c => c.GetType() == type);
        var owners = (IDictionary)AccessTools.Property(type, "activeByTarget").GetValue(coordinator, null);
        var plans = (IDictionary)AccessTools.Field(type, "carePlans").GetValue(coordinator);
        object settings = AccessTools.Property(T("SearchAndRescueMod"), "Settings").GetValue(null, null);
        var modeField = AccessTools.Field(settings.GetType(), "MedicalCoordinationMode");
        object oldMode = modeField.GetValue(settings);
        var oldCare = patient.playerSettings.medCare;
        Thing medicine = ThingMaker.MakeThing(ThingDefOf.MedicineIndustrial); medicine.stackCount = 25;
        Pawn hauler = null;
        for (int attempt = 0; attempt < 100; attempt++)
        {
            hauler = (Pawn)AccessTools.Method(T("CasevacAndBoundaryDiagnostics"), "Spawn")
                .Invoke(null, new object[] { map, "Supply Guard", map.Center });
            if (!hauler.WorkTypeIsDisabled(WorkTypeDefOf.Hauling)) break;
            hauler.Destroy(); hauler = null;
        }
        if (hauler == null) throw new InvalidOperationException("Could not generate a capable supply worker");
        IntVec3 medicineCell = map.AllCells.First(cell => cell.Standable(map) &&
            cell.DistanceToSquared(patient.Position) > 400 &&
            hauler.CanReach(cell, PathEndMode.OnCell, Danger.Deadly));
        map.fogGrid.Unfog(medicineCell);
        GenSpawn.Spawn(medicine, medicineCell, map);
        medicine.SetForbidden(false);
        foreach (WorkTypeDef def in new[] { WorkTypeDefOf.Hauling, DefDatabase<WorkTypeDef>.GetNamed("SAR_FieldRescue") })
            AccessTools.Method(T("Compatibility"), "SetWorkPriorityForMigration").Invoke(null, new object[] { hauler, def, 1 });
        int now = Find.TickManager.TicksGame;
        object supplyStage = Enum.Parse(T("SearchAndRescueStage"), "Supply");
        object pending = Activator.CreateInstance(AccessTools.Inner(type, "PendingAssignment"),
            new object[] { patient, supplyStage, 1000000d, now, false, now + 600, null, null, medicine, 1 });
        Func<bool> valid = () => (bool)AccessTools.Method(type, "PendingAssignmentValid").Invoke(coordinator, new object[] { hauler, pending, now });
        Func<bool> offers = () => ((IEnumerable)AccessTools.Method(type, "BuildSupplyTasks").Invoke(coordinator, new object[] { now }))
            .Cast<object>().Any(task => AccessTools.Field(task.GetType(), "Target").GetValue(task) == patient);
        Action refresh = () =>
        {
            AccessTools.Method(type, "NotifyGlobalSettingsChanged").Invoke(null, null);
            plans[patient] = AccessTools.Method(T("MedicalCarePlan"), "Build").Invoke(null, new object[] { patient, now });
        };
        Action<string, bool> own = (stage, medicated) =>
        {
            Job job = JobMaker.MakeJob(stage == "Rescue" ? JobDefOf.Rescue : JobDefOf.TendPatient, patient);
            if (medicated) job.targetB = medicine;
            owners[patient] = Activator.CreateInstance(T("ActiveAssignment"), new object[] {
                doctor, patient, job, Enum.Parse(T("SearchAndRescueStage"), stage), patient.Position,
                now, 1, 1f, 1f, Enum.Parse(T("CareOrigin"), "ManualTreatment"), 0f, 0f });
        };
        Hediff bruise = null;
        try
        {
            patient.playerSettings.medCare = MedicalCareCategory.Best;
            modeField.SetValue(settings, Enum.Parse(modeField.FieldType, "AllTending"));
            refresh();
            check((bool)AccessTools.Method(type, "SupplyTargetReady").Invoke(coordinator, new object[] { patient, now }), "unowned casualty is eligible for supply");
            check(valid(), "unowned pending delivery accepted: " + AccessTools.Method(type, "DebugPendingInvalidReason").Invoke(coordinator, new object[] { hauler, pending, now }));
            check(offers(), "unowned casualty generates supply candidates");
            Job transition = JobMaker.MakeJob(JobDefOf.Wait_MaintainPosture, 600);
            hauler.jobs.StartJob(transition, JobCondition.InterruptForced);
            WorkGiverDef prioritizedWork = DefDatabase<WorkGiverDef>.AllDefsListForReading
                .First(def => def.workType == WorkTypeDefOf.Doctor && def.prioritizeSustains);
            hauler.mindState.priorityWork.Set(hauler.Position, prioritizedWork);
            var pendingWorkers = (IDictionary)AccessTools.Field(type, "pendingByWorker").GetValue(coordinator);
            try
            {
                check(!transition.playerForced && hauler.mindState.priorityWork.IsPrioritized,
                    "sustained prioritized work fixture is between forced jobs");
                check(!valid(), "sustained prioritized work rejects pending supply during transition");
                pendingWorkers[hauler] = pending;
                AccessTools.Method(type, "TryWakePendingWorker").Invoke(coordinator, new object[] { hauler });
                check(hauler.CurJob == transition && hauler.mindState.priorityWork.IsPrioritized,
                    "deferred SAR wake preserves sustained prioritized work and current transition");
            }
            finally
            {
                pendingWorkers.Remove(hauler);
                hauler.mindState.priorityWork.Clear();
            }
            check(valid(), "supply eligibility returns after vanilla clears prioritized work");
            foreach (string stage in new[] { "Rescue", "Capture", "Restock", "FollowupTreat" })
            {
                own(stage, false);
                check(!offers() && !valid(), stage + " primary owner blocks both supply generation and pending delivery");
                string reason = (string)AccessTools.Method(type, "DebugPendingInvalidReason").Invoke(coordinator, new object[] { hauler, pending, now });
                check(reason.Contains("supply-target(active=" + stage + ")"), stage + " supply rejection has a specific diagnostic");
            }
            own("Treat", true);
            check(!offers() && !valid(), "medicated treatment blocks duplicate supply generation and pending delivery");
            own("Treat", false);
            check(offers() && valid(), "urgent dry treatment retains concurrent supply candidates and pending delivery");
            owners.Remove(patient);
            patient.health.RemoveHediff(wound);
            bruise = HediffMaker.MakeHediff(HediffDef.Named("Bruise"), patient, patient.RaceProps.body.corePart);
            bruise.Severity = 4; patient.health.AddHediff(bruise);
            refresh();
            check(!(bool)AccessTools.Method(type, "NeedsFieldStabilization").Invoke(null, new object[] { patient }), "routine injury fixture has no emergency");
            check(!offers() && !valid(), "routine casualty stays outside emergency supply even in AllTending");
            own("Treat", false);
            check(!offers() && !valid(), "routine dry treatment does not create rejected emergency supply tasks");
            owners.Remove(patient);
            patient.health.RemoveHediff(bruise); bruise = null;
            patient.health.AddHediff(wound);
            refresh();
            check(offers() && valid(), "releasing the primary owner immediately restores supply eligibility");
            check(map.areaManager.TryMakeNewAllowed(out Area_Allowed area), "supply area fixture created");
            Area oldArea = hauler.playerSettings.AreaRestrictionInPawnCurrentMap;
            try
            {
                area.Invert();
                hauler.playerSettings.AreaRestrictionInPawnCurrentMap = area;
                check(valid(), "supply accepted when pickup and patient are inside allowed area");
                area[patient.Position] = false;
                check(hauler.CanReach(patient, PathEndMode.Touch, Danger.Deadly),
                    "out-of-area patient remains physically reachable");
                check(!valid(), "pending delivery rejects reachable patient outside allowed area");
                Job rejected = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("SAR_DeliverMedicalSupply"), medicine, patient);
                rejected.count = 1;
                check(!rejected.GetCachedDriver(hauler).TryMakePreToilReservations(false),
                    "delivery driver rejects out-of-area patient before reserving medicine");
                area[patient.Position] = true;
                area[medicine.Position] = false;
                check(!valid(), "pending delivery rejects medicine outside allowed area");
                area[medicine.Position] = true;
                check(valid(), "expanding allowed area restores delivery eligibility");
                Job delivery = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("SAR_DeliverMedicalSupply"), medicine, patient);
                delivery.count = 1;
                hauler.jobs.StartJob(delivery, JobCondition.InterruptForced);
                check(hauler.CurJob == delivery, "in-area delivery starts successfully");
                int deliveryId = delivery.loadID;
                area[patient.Position] = false;
                hauler.jobs.curDriver.DriverTick();
                check(hauler.CurJob?.loadID != deliveryId, "editing area during delivery stops the stale job");
                check(!map.reservationManager.ReservationsReadOnly.Any(reservation =>
                        reservation.Claimant == hauler && reservation.Target.Thing == medicine),
                    "cancelled delivery releases its medicine reservation");
            }
            finally
            {
                hauler.playerSettings.AreaRestrictionInPawnCurrentMap = oldArea;
                area.Delete();
            }
        }
        finally
        {
            owners.Remove(patient);
            if (bruise != null) patient.health.RemoveHediff(bruise);
            if (!patient.health.hediffSet.hediffs.Contains(wound)) patient.health.AddHediff(wound);
            patient.playerSettings.medCare = oldCare;
            modeField.SetValue(settings, oldMode);
            medicine.Destroy(); hauler.Destroy();
            AccessTools.Method(type, "NotifyGlobalSettingsChanged").Invoke(null, null);
        }
    }
}
