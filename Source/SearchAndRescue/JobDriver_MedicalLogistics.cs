using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace SearchAndRescue
{
    public sealed class JobDriver_RestockMedicalKit : JobDriver
    {
        private const TargetIndex ResourceIndex = TargetIndex.A;
        private const TargetIndex PatientIndex = TargetIndex.B;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (job.targetQueueA == null || job.targetQueueA.Count == 0)
            {
                return false;
            }

            for (int index = 0; index < job.targetQueueA.Count; index++)
            {
                Thing thing = job.targetQueueA[index].Thing;
                int count = job.countQueue != null && index < job.countQueue.Count
                    ? job.countQueue[index]
                    : 1;
                Pawn holder = MedicalResourceLedger.InventoryHolder(thing);
                if (thing == null || holder != null && MedicalResourceLedger.IsBeingUsedByHolder(holder, thing))
                {
                    return false;
                }

                if (holder != null && holder != pawn)
                {
                    // The resource ledger owns the item-level claim. Reserving the whole
                    // donor pawn would incorrectly block rescue/treatment jobs on that pawn.
                    if (!MedicalResourceLedger.CanTakeFromInventoryHolder(
                            pawn,
                            holder,
                            thing,
                            count,
                            job.targetB.Pawn))
                    {
                        return false;
                    }
                    continue;
                }
                else if (holder == null &&
                         (!pawn.CanReserve(
                              thing,
                              MedicalResourceLedger.SharedStackReservationMaxPawns,
                              count) ||
                          !pawn.Reserve(
                              thing,
                              job,
                              MedicalResourceLedger.SharedStackReservationMaxPawns,
                              count,
                              null,
                              errorOnFailed: false)))
                {
                    return false;
                }
            }
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(PatientIndex);

            Toil selectNext = ToilMaker.MakeToil("SelectNextMedicalKitItem");
            selectNext.initAction = () =>
            {
                if (job.targetQueueA == null || job.targetQueueA.Count == 0)
                {
                    return;
                }

                job.targetA = job.targetQueueA[0];
                job.targetQueueA.RemoveAt(0);
                Pawn originalHolder = MedicalResourceLedger.InventoryHolder(job.targetA.Thing);
                job.targetC = originalHolder == null
                    ? LocalTargetInfo.Invalid
                    : new LocalTargetInfo(originalHolder);
                job.count = 1;
                if (job.countQueue != null && job.countQueue.Count > 0)
                {
                    job.count = job.countQueue[0];
                    job.countQueue.RemoveAt(0);
                }
                if (originalHolder != pawn && Compatibility.UsesCombatExtended)
                {
                    int capacity = Compatibility.CombatExtendedInventoryCapacity(pawn, job.targetA.Thing);
                    if (capacity <= 0)
                    {
                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }
                    // Capacity can change after every previous item in a multi-item kit.
                    // Take the safe subset now; the released deficit is rematched afterwards.
                    job.count = Math.Min(job.count, capacity);
                }
            };
            selectNext.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return selectNext;

            Toil transferFromPawn = ToilMaker.MakeToil("TransferMedicalKitItemFromPawn");
            transferFromPawn.initAction = () =>
            {
                Thing resource = job.targetA.Thing;
                Pawn holder = MedicalResourceLedger.InventoryHolder(resource);
                if (resource == null || holder == null || holder == pawn || holder.inventory == null ||
                    MedicalResourceLedger.IsBeingUsedByHolder(holder, resource))
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                int wanted = System.Math.Max(1, job.count);
                if (resource.stackCount < wanted)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                if (!MedicalResourceLedger.TryTransferFromInventoryHolder(
                        holder,
                        pawn,
                        resource,
                        wanted,
                        toCarryTracker: false,
                        patient: job.targetB.Pawn))
                {
                    EndJobWith(JobCondition.Incompletable);
                }
            };
            transferFromPawn.defaultCompleteMode = ToilCompleteMode.Instant;

            Toil itemAcquired = ToilMaker.MakeToil("MedicalKitItemAcquired");
            itemAcquired.defaultCompleteMode = ToilCompleteMode.Instant;

            Toil goToItem = Toils_Goto.GotoThing(ResourceIndex, PathEndMode.ClosestTouch, true);
            yield return goToItem;

            Toil validateOwner = ToilMaker.MakeToil("ValidateMedicalKitItemOwner");
            validateOwner.initAction = () =>
            {
                Pawn expectedHolder = job.targetC.Pawn;
                Pawn actualHolder = MedicalResourceLedger.InventoryHolder(job.targetA.Thing);
                if (expectedHolder != actualHolder)
                {
                    // Do not convert a donor handoff into an unreserved ground pickup (or
                    // vice versa) if another job moved/dropped the resource while en route.
                    EndJobWith(JobCondition.Incompletable);
                }
            };
            validateOwner.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return validateOwner;
            yield return Toils_Jump.JumpIf(itemAcquired, () =>
                MedicalResourceLedger.InventoryHolder(job.targetA.Thing) == pawn);
            yield return Toils_Jump.JumpIf(transferFromPawn, () =>
            {
                Pawn holder = MedicalResourceLedger.InventoryHolder(job.targetA.Thing);
                return holder != null && holder != pawn;
            });
            yield return Toils_Haul.TakeToInventory(ResourceIndex, () => job.count);
            yield return Toils_Jump.Jump(itemAcquired);
            yield return transferFromPawn;
            yield return itemAcquired;
            yield return Toils_Jump.JumpIfHaveTargetInQueue(ResourceIndex, selectNext);
        }
    }

    public sealed class JobDriver_DeliverMedicalSupply : JobDriver
    {
        private const TargetIndex ResourceIndex = TargetIndex.A;
        private const TargetIndex PatientIndex = TargetIndex.B;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            Pawn holder = MedicalResourceLedger.InventoryHolder(job.targetA.Thing);
            // Persist the source selected at reservation time, including across save/load.
            job.targetC = holder == null ? LocalTargetInfo.Invalid : new LocalTargetInfo(holder);
            if (holder != null)
            {
                return MedicalResourceLedger.CanTakeFromInventoryHolder(
                    pawn,
                    holder,
                    job.targetA.Thing,
                    job.count,
                    job.targetB.Pawn);
            }

            if (!pawn.CanReserve(
                    job.targetA,
                    MedicalResourceLedger.SharedStackReservationMaxPawns,
                    job.count))
            {
                return false;
            }
            return pawn.Reserve(
                job.targetA,
                job,
                MedicalResourceLedger.SharedStackReservationMaxPawns,
                job.count,
                null,
                errorOnFailed: false);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(ResourceIndex);
            this.FailOnDestroyedOrNull(PatientIndex);
            this.FailOn(() => !job.targetB.Pawn.Spawned);

            yield return Toils_Goto.GotoThing(ResourceIndex, PathEndMode.ClosestTouch, true);

            Toil validateOwner = ToilMaker.MakeToil("ValidateMedicalSupplyOwner");
            validateOwner.initAction = () =>
            {
                if (job.targetC.Pawn != MedicalResourceLedger.InventoryHolder(job.targetA.Thing))
                {
                    // A dropped or newly pocketed source has different reservation needs.
                    EndJobWith(JobCondition.Incompletable);
                }
            };
            validateOwner.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return validateOwner;

            Toil itemAcquired = ToilMaker.MakeToil("MedicalSupplyAcquired");
            itemAcquired.defaultCompleteMode = ToilCompleteMode.Instant;

            Toil transferFromHolder = ToilMaker.MakeToil("TransferMedicalSupplyFromHolder");
            transferFromHolder.initAction = () =>
            {
                Thing resource = job.targetA.Thing;
                Pawn holder = MedicalResourceLedger.InventoryHolder(resource);
                if (holder == null || !MedicalResourceLedger.TryTransferFromInventoryHolder(
                        holder,
                        pawn,
                        resource,
                        Math.Max(1, job.count),
                        toCarryTracker: true,
                        patient: job.targetB.Pawn))
                {
                    EndJobWith(JobCondition.Incompletable);
                }
                else
                {
                    // A partial extraction leaves the original stack with its holder.
                    // Track the delivered split so later source destruction cannot cancel it.
                    job.targetA = pawn.carryTracker.CarriedThing;
                }
            };
            transferFromHolder.defaultCompleteMode = ToilCompleteMode.Instant;

            yield return Toils_Jump.JumpIf(transferFromHolder, () =>
                MedicalResourceLedger.InventoryHolder(job.targetA.Thing) is Pawn holder && holder != pawn);
            yield return Toils_Haul.StartCarryThing(ResourceIndex, false, true, false);
            yield return Toils_Jump.Jump(itemAcquired);
            yield return transferFromHolder;
            yield return itemAcquired;
            yield return Toils_Goto.GotoThing(PatientIndex, PathEndMode.Touch);

            Toil dropAndReference = ToilMaker.MakeToil("DropAndReferenceFieldMedicalSupply");
            dropAndReference.initAction = () =>
            {
                Thing carried = pawn.carryTracker.CarriedThing;
                Pawn patient = job.targetB.Pawn;
                Map map = pawn.Map;
                int deliveredCount = carried?.stackCount ?? 0;
                if (carried == null || patient == null || map == null ||
                    !pawn.carryTracker.TryDropCarriedThing(
                        pawn.Position,
                        ThingPlaceMode.Near,
                        out Thing dropped))
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                map.GetComponent<SearchAndRescueCoordinator>()
                    ?.NotifyFieldSupplyDelivered(pawn, dropped, patient, deliveredCount);
            };
            dropAndReference.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return dropAndReference;
        }
    }
}
