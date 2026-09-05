using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SearchAndRescue
{
    public sealed class JobDriver_CaptureInPlace : JobDriver
    {
        private const int CaptureDurationTicks = 120;

        private Pawn TargetPawn => job.targetA.Pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(TargetPawn, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOnAggroMentalStateAndHostile(TargetIndex.A);
            this.FailOn(() => TargetPawn == null || !TargetPawn.Downed || TargetPawn.IsPrisonerOfColony);

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch)
                .FailOnDespawnedNullOrForbidden(TargetIndex.A)
                .FailOnSomeonePhysicallyInteracting(TargetIndex.A);

            yield return Toils_General.Wait(CaptureDurationTicks)
                .WithProgressBarToilDelay(TargetIndex.A, true);

            yield return Toils_General.Do(() =>
            {
                Pawn prisoner = TargetPawn;
                prisoner.GetLord()?.Notify_PawnAttemptArrested(prisoner);
                GenClamor.DoClamor(prisoner, 10f, ClamorDefOf.Harm);

                if (!prisoner.IsPrisoner && !prisoner.IsSlave)
                {
                    QuestUtility.SendQuestTargetSignals(prisoner.questTags, "Arrested", prisoner.Named("SUBJECT"));
                    if (prisoner.Faction != null)
                    {
                        QuestUtility.SendQuestTargetSignals(prisoner.Faction.questTags, "FactionMemberArrested", prisoner.Faction.Named("FACTION"));
                    }
                }

                if (prisoner.guest.Released)
                {
                    prisoner.guest.Released = false;
                    prisoner.guest.SetExclusiveInteraction(PrisonerInteractionModeDefOf.MaintainOnly);
                    GenGuest.RemoveHealthyPrisonerReleasedThoughts(prisoner);
                }

                if (!prisoner.IsPrisonerOfColony)
                {
                    prisoner.guest.CapturedBy(Faction.OfPlayer, pawn);
                }

                if (prisoner.playerSettings == null)
                {
                    prisoner.playerSettings = new Pawn_PlayerSettings(prisoner);
                }
            });
        }
    }
}
