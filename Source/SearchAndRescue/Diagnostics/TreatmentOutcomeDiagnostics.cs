using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SearchAndRescue
{
    // Invoked only by the disposable capture probe. Fault injection is scoped to one
    // patient and removed in finally; no third-party binary or player save is changed.
    internal static class TreatmentOutcomeDiagnostics
    {
        private static Pawn subject;
        private static int mode;
        private static int invocations;
        private static bool Inject(Pawn patient)
        {
            if (patient != subject || mode == 0) return true;
            invocations++;
            if (mode == 2)
                foreach (Hediff h in patient.health.hediffSet.hediffs.Where(h => h.TendableNow()).ToList())
                    h.Tended(.6f, 1f);
            if (mode == 3) throw new InvalidOperationException("expected diagnostic treatment failure");
            return false;
        }

        internal static void Run(Pawn doctor, Pawn patient, SearchAndRescueCoordinator coordinator, Action<bool,string> check)
        {
            var claims = (ActiveJobClaims)AccessTools.Field(typeof(SearchAndRescueCoordinator), "activeClaims").GetValue(coordinator);
            var finish = AccessTools.Method(typeof(SearchAndRescueCoordinator), "FinishTreatmentRound");
            var progress = AccessTools.Method(typeof(SearchAndRescueCoordinator), "TreatmentProgressMade");
            var clear = AccessTools.Method(typeof(SearchAndRescueCoordinator), "ClearTargetRetries");
            var retries = AccessTools.Field(typeof(SearchAndRescueCoordinator), "retryByStage");
            var harmony = new Harmony("SAR.Diagnostics.TreatmentOutcome");
            var original = AccessTools.Method(typeof(TendUtility), nameof(TendUtility.DoTend));
            var patch = AccessTools.Method(typeof(TreatmentOutcomeDiagnostics), nameof(Inject));
            harmony.Patch(original, prefix: new HarmonyMethod(patch) { priority = Priority.First - 1 });
            subject = patient;
            Job previous = doctor.jobs.curJob;
            try
            {
                if (patient.Map.designationManager.DesignationOn(patient, SearchAndRescueDefOf.SAR_Treat) == null)
                    patient.Map.designationManager.AddDesignation(new Designation(patient, SearchAndRescueDefOf.SAR_Treat));
                Hediff cut = HediffMaker.MakeHediff(HediffDefOf.Cut, patient, patient.RaceProps.body.corePart);
                cut.Severity = 4; patient.health.AddHediff(cut);
                var medicine = (Medicine)ThingMaker.MakeThing(ThingDefOf.MedicineIndustrial); medicine.stackCount = 3;
                ActiveAssignment Assign()
                {
                    Job job = JobMaker.MakeJob(JobDefOf.TendPatient, patient);
                    doctor.jobs.curJob = job;
                    var a = new ActiveAssignment(doctor, patient, job, SearchAndRescueStage.Treat, patient.Position,
                        Find.TickManager.TicksGame, 1, patient.health.hediffSet.BleedRateTotal, 1f, CareOrigin.ManualTreatment, .4f, 0f);
                    claims.Register(a); return a;
                }
                clear.Invoke(coordinator, new object[] { patient });
                for (int round = 1; round <= 2; round++)
                {
                    ActiveAssignment a = Assign(); mode = 1;
                    TendUtility.DoTend(doctor, patient, medicine);
                    check(!cut.IsTended() && medicine.stackCount == 3 && a.CommittedTreatmentRounds == 0 &&
                        a.LastTreatmentHadNoEffect && doctor.CurJob.endAfterTendedOnce, "empty round exits without recording success " + round);
                    check(!(bool)progress.Invoke(null, new object[] { patient, a }), "blood-loss delta cannot grant native tending success " + round);
                    claims.ReleasePrimary(patient);
                    finish.Invoke(coordinator, new object[] { patient, a, Find.TickManager.TicksGame });
                    var dict = (System.Collections.IDictionary)retries.GetValue(coordinator);
                    object key = dict.Keys.Cast<object>().FirstOrDefault(k =>
                        AccessTools.Field(k.GetType(), "Target").GetValue(k) == patient &&
                        AccessTools.Field(k.GetType(), "Stage").GetValue(k).ToString() == "Treat");
                    object state = key == null ? null : dict[key];
                    check(state != null && (int)AccessTools.Field(state.GetType(), "FailureCount").GetValue(state) == round,
                        "empty round keeps progressive retry state " + round);
                }
                clear.Invoke(coordinator, new object[] { patient });
                ActiveAssignment replacement = Assign(); mode = 2;
                TendUtility.DoTend(doctor, patient, medicine);
                check(cut.IsTended() && medicine.stackCount == 3 && replacement.CommittedTreatmentRounds == 1,
                    "replacement prefix and unchanged medicine stack still record real treatment");
                claims.ReleasePrimary(patient);

                // Same wound count and already-tended status: renewed duration is evidence.
                Hediff disease = HediffMaker.MakeHediff(HediffDefOf.WoundInfection, patient, patient.RaceProps.body.corePart);
                disease.Severity = .1f; patient.health.AddHediff(disease);
                var duration = disease.TryGetComp<HediffComp_TendDuration>(); duration.tendTicksLeft = 1;
                check(disease.IsTended() && disease.TendableNow(), "disease can need retending while still tended");
                ActiveAssignment renewed = Assign(); mode = 0;
                TendUtility.DoTend(doctor, patient, null);
                check(duration.tendTicksLeft > 1 && renewed.CommittedTreatmentRounds == 1,
                    "renewing a disease tend counts without requiring a removed hediff");
                claims.ReleasePrimary(patient);

                cut = HediffMaker.MakeHediff(HediffDefOf.Cut, patient, patient.RaceProps.body.corePart);
                cut.Severity = 4; patient.health.AddHediff(cut);
                ActiveAssignment failed = Assign(); mode = 3;
                bool threw = false;
                try { TendUtility.DoTend(doctor, patient, null); }
                catch (InvalidOperationException) { threw = true; }
                check(threw && failed.CommittedTreatmentRounds == 0 && FieldTreatmentBoundary.EmergencyPatient == null,
                    "exception cannot commit and restores emergency scope");
                mode = 0; TendUtility.DoTend(doctor, patient, null);
                check(cut.IsTended() && failed.CommittedTreatmentRounds == 1, "next treatment works after exception");
                check(!FieldTreatmentBoundary.AllowsOption(patient, MedicalIntervention.VanillaTend, SearchAndRescueStage.Treat),
                    "emergency lane excludes stabilized native tend candidates");
                Hediff bruise = HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed("Bruise"), patient, patient.RaceProps.body.corePart);
                bruise.Severity = 3; patient.health.AddHediff(bruise);
                check(!FieldTreatmentBoundary.AllowsOption(patient, MedicalIntervention.VanillaTend, SearchAndRescueStage.Treat) &&
                    FieldTreatmentBoundary.AllowsOption(patient, MedicalIntervention.VanillaTend, SearchAndRescueStage.FollowupTreat),
                    "bedside routine wound belongs to followup lane only");
                check((double)AccessTools.Method(typeof(SearchAndRescueCoordinator), "EdgeWeight").Invoke(coordinator,
                    new object[] { doctor, patient, SearchAndRescueStage.FollowupTreat }) > 0d,
                    "followup edge scoring preserves the routine stage");
                claims.ReleasePrimary(patient);
                doctor.jobs.curJob = null;
                Job stabilizedJob = JobMaker.MakeJob(JobDefOf.TendPatient, patient);
                var stabilizedAssignment = new ActiveAssignment(doctor, patient, stabilizedJob, SearchAndRescueStage.Treat,
                    patient.Position, Find.TickManager.TicksGame, 1, 0f, 0f, CareOrigin.ManualTreatment, 0f, 0f);
                claims.Register(stabilizedAssignment);
                int stabilizedId = stabilizedJob.loadID;
                doctor.jobs.StartJob(stabilizedJob, JobCondition.InterruptForced);
                mode = 1; int beforeCalls = invocations;
                Toil final = Toils_Tend.FinalizeTend(patient); final.actor = doctor; final.initAction();
                check(invocations == beforeCalls && stabilizedAssignment.CommittedTreatmentRounds == 0 &&
                    !bruise.IsTended() && doctor.CurJob?.loadID != stabilizedId,
                    "stabilized emergency finalizer exits before DoTend and provider billing hooks" +
                    " calls=" + (invocations - beforeCalls) + " commits=" + stabilizedAssignment.CommittedTreatmentRounds);
                patient.health.RemoveHediff(bruise);
                claims.ReleasePrimary(patient);
                clear.Invoke(coordinator, new object[] { patient });
                doctor.jobs.curJob = null;
                doctor.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait, 1), JobCondition.InterruptForced);
                cut = HediffMaker.MakeHediff(HediffDefOf.Cut, patient, patient.RaceProps.body.corePart);
                cut.Severity = 4; patient.health.AddHediff(cut);
                patient.playerSettings.medCare = MedicalCareCategory.NoMeds;
                mode = 1;
                Job nativeJob = JobMaker.MakeJob(JobDefOf.TendPatient, patient);
                nativeJob.endAfterTendedOnce = true;
                var nativeAssignment = new ActiveAssignment(doctor, patient, nativeJob, SearchAndRescueStage.Treat,
                    patient.Position, Find.TickManager.TicksGame, 1, patient.health.hediffSet.BleedRateTotal, 1f,
                    CareOrigin.ManualTreatment, 0f, 0f);
                claims.Register(nativeAssignment);
                doctor.jobs.StartJob(nativeJob, JobCondition.InterruptForced);
                bool sawActive = false, sawEndedEmpty = false, sawBackoff = false;
                for (int tick = 0; tick < 4000; tick++)
                {
                    Find.TickManager.DoSingleTick();
                    if (claims.Primary.TryGetValue(patient, out ActiveAssignment running) &&
                        running.Worker == doctor && running.JobDef == JobDefOf.TendPatient) sawActive = true;
                    var dict = (System.Collections.IDictionary)retries.GetValue(coordinator);
                    object key = dict.Keys.Cast<object>().FirstOrDefault(k =>
                        AccessTools.Field(k.GetType(), "Target").GetValue(k) == patient &&
                        AccessTools.Field(k.GetType(), "Stage").GetValue(k).ToString() == "Treat");
                    if (key == null) continue;
                    sawBackoff = true;
                    sawEndedEmpty = doctor.CurJobDef != JobDefOf.TendPatient && !cut.IsTended();
                    if (sawEndedEmpty) break;
                }
                check(sawActive && sawEndedEmpty && sawBackoff && nativeAssignment.CommittedTreatmentRounds == 0,
                    "real native driver leaves empty managed tend round and scheduler backs off" +
                    " active=" + sawActive + " ended=" + sawEndedEmpty + " retry=" + sawBackoff);
                mode = 0;
                for (int tick = 0; tick < 5000 && !cut.IsTended(); tick++) Find.TickManager.DoSingleTick();
                check(cut.IsTended(), "real scheduler resumes successful treatment after provider recovers");
            }
            finally
            {
                mode = 0; subject = null; harmony.Unpatch(original, patch);
                if (doctor.CurJob != null) doctor.jobs.EndCurrentJob(JobCondition.InterruptForced, false);
                doctor.jobs.curJob = previous; claims.ReleasePrimary(patient);
                clear.Invoke(coordinator, new object[] { patient });
            }
        }
    }
}
