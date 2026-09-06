using System;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace SearchAndRescue
{
    internal sealed class ActiveStandby
    {
        public readonly Pawn Worker;
        public readonly Pawn Target;
        public readonly Job Job;
        public readonly JobDef JobDef;
        public readonly JobIdentity Identity;
        public readonly Pawn Doctor;
        public readonly Job TreatmentJob;
        public readonly JobIdentity TreatmentIdentity;
        public readonly int ExpectedTreatmentEndTick;

        public ActiveStandby(
            Pawn worker,
            Pawn target,
            Job job,
            Pawn doctor,
            Job treatmentJob,
            int expectedTreatmentEndTick)
        {
            Worker = worker;
            Target = target;
            Job = job;
            JobDef = job?.def;
            Identity = ActiveJobClaims.IdentityOf(job);
            Doctor = doctor;
            TreatmentJob = treatmentJob;
            TreatmentIdentity = ActiveJobClaims.IdentityOf(treatmentJob);
            ExpectedTreatmentEndTick = expectedTreatmentEndTick;
        }
    }

    internal sealed class ActiveAssignment
    {
        public readonly Pawn Worker;
        public readonly Pawn Target;
        public readonly Job Job;
        // RimWorld pools Job instances. Once an old job ends, the same object can be
        // cleared and reused for a wander/wait job while this fallback record still
        // holds its reference. Identity captures both definition and native loadID.
        public readonly JobDef JobDef;
        public readonly JobIdentity Identity;
        public readonly SearchAndRescueStage Stage;
        public readonly IntVec3 Destination;
        public readonly Building_Bed DestinationBed;
        public readonly bool DestinationIsBed;
        public readonly int StartedAt;
        public readonly int InitialUntendedHediffs;
        public readonly float InitialBleedRate;
        public readonly float InitialEmergencySeverity;
        public readonly float InitialBloodLossSeverity;
        public readonly float InitialHemodilutionSeverity;
        public readonly int InitialTourniquetCount;
        public readonly CareOrigin Origin;
        public readonly int TreatmentRoundBudget;
        public bool RoundEffectSeen;
        public int CommittedTreatmentRounds;
        public bool ActualStartObserved;
        public JobCondition EndCondition;

        public ActiveAssignment(
            Pawn worker,
            Pawn target,
            Job job,
            SearchAndRescueStage stage,
            IntVec3 destination,
            int startedAt,
            int initialUntendedHediffs,
            float initialBleedRate,
            float initialEmergencySeverity,
            CareOrigin origin,
            float initialBloodLossSeverity,
            float initialHemodilutionSeverity)
        {
            Worker = worker;
            Target = target;
            Job = job;
            JobDef = job?.def;
            Identity = ActiveJobClaims.IdentityOf(job);
            Stage = stage;
            Destination = destination;
            DestinationBed = stage == SearchAndRescueStage.Rescue
                ? job.targetB.Thing as Building_Bed
                : null;
            DestinationIsBed = DestinationBed != null;
            StartedAt = startedAt;
            InitialUntendedHediffs = initialUntendedHediffs;
            InitialBleedRate = initialBleedRate;
            InitialEmergencySeverity = initialEmergencySeverity;
            Origin = origin;
            InitialBloodLossSeverity = initialBloodLossSeverity;
            InitialHemodilutionSeverity = initialHemodilutionSeverity;
            InitialTourniquetCount = target.health.hediffSet.hediffs.Count(hediff =>
                hediff.def.defName == "TourniquetApplied");
            TreatmentRoundBudget = AssignmentStageRules.IsTreatment(stage) && job.def == JobDefOf.TendPatient &&
                                   job.targetB.Thing != null
                ? Math.Max(1, job.count)
                : int.MaxValue;
        }
    }
}
