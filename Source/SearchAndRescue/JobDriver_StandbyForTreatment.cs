using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace SearchAndRescue
{
    public sealed class JobDriver_StandbyForTreatment : JobDriver
    {
        private Pawn Patient => job.targetA.Pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // Deliberately do not reserve the patient: the doctor needs that reservation.
            return Patient != null;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            Toil wait = ToilMaker.MakeToil("WaitForFieldTreatment");
            wait.defaultCompleteMode = ToilCompleteMode.Never;
            wait.tickIntervalAction = delta =>
            {
                Pawn patient = Patient;
                if (patient == null || !patient.Spawned || patient.Dead ||
                    !SearchAndRescueJobContext.ShouldWaitForFieldTreatment(pawn, patient, job))
                {
                    ReadyForNextToil();
                }
            };
            yield return wait;
        }
    }
}
