using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace SearchAndRescue
{
    internal static class MechWorkerCompatibility
    {
        private static readonly HashSet<string> WorkModesSuppressedByThreats = new HashSet<string>
        {
            "WVC_WorkAndWaitEnemy",
            "WVC_SafeWorkAndRecharge",
            "WVC_EscortIfEnemyWorkAndRecharge"
        };

        private static readonly Dictionary<ThingDef, Dictionary<MechWorkModeDef, bool>>
            OrdinaryWorkPathByRace = new Dictionary<ThingDef, Dictionary<MechWorkModeDef, bool>>();

        internal static bool IsFieldResponderOptedIn(Pawn worker)
        {
            return Compatibility.IsColonyWorkMech(worker) &&
                   worker.Map?.GetComponent<SearchAndRescueCoordinator>()?.IsFieldResponder(worker) == true;
        }

        internal static bool CanRunSchedulerNow(Pawn worker)
        {
            if (!Compatibility.IsColonyWorkMech(worker))
            {
                return true;
            }

            if (worker.GetOverseer() == null || worker.GetMechControlGroup() == null ||
                worker.IsFormingCaravan() || worker.IsDeactivated() || !worker.Awake() || worker.IsCharging() ||
                worker.IsSelfShutdown() || worker.CurJobDef == JobDefOf.MechCharge ||
                worker.CurJobDef == JobDefOf.SelfShutdown ||
                worker.TryGetComp<CompCanBeDormant>()?.Awake == false ||
                worker.CurJob?.playerForced == true || worker.jobs?.jobQueue?.AnyPlayerForced == true)
            {
                return false;
            }

            if (worker.mindState?.priorityWork?.IsPrioritized == true)
            {
                return false;
            }

            Need_MechEnergy energy = worker.needs?.energy;
            if (energy != null && energy.CurLevel + 0.1f < JobGiver_GetEnergy.GetMinAutorechargeThreshold(worker))
            {
                return false;
            }

            MechWorkModeDef workMode = worker.GetMechWorkMode();
            if (!ModeHasOrdinaryWorkPath(worker, workMode))
            {
                return false;
            }

            return !WorkModesSuppressedByThreats.Contains(workMode.defName) ||
                   !GenHostility.AnyHostileActiveThreatToPlayer(
                       worker.Map,
                       countDormantPawnsAsHostile: true);
        }

        internal static bool SupportsNativeWorkType(Pawn worker, WorkTypeDef workType)
        {
            return Compatibility.IsColonyWorkMech(worker) && workType != null &&
                   worker.RaceProps.mechEnabledWorkTypes?.Contains(workType) == true &&
                   !worker.WorkTypeIsDisabled(workType);
        }

        internal static int DefaultNativeWorkTypePriority(Pawn worker, WorkTypeDef workType)
        {
            if (!SupportsNativeWorkType(worker, workType)) return 0;
            MechWorkTypePriority configured = worker.RaceProps.mechWorkTypePriorities?
                .FirstOrDefault(entry => entry.def == workType);
            return configured?.priority ?? 3;
        }

        private static bool ModeHasOrdinaryWorkPath(Pawn worker, MechWorkModeDef workMode)
        {
            if (worker?.def == null || workMode == null)
            {
                return false;
            }

            if (!OrdinaryWorkPathByRace.TryGetValue(worker.def, out Dictionary<MechWorkModeDef, bool> byMode))
            {
                byMode = new Dictionary<MechWorkModeDef, bool>();
                OrdinaryWorkPathByRace.Add(worker.def, byMode);
            }

            if (!byMode.TryGetValue(workMode, out bool allowsWork))
            {
                ThinkNode root = worker.RaceProps.thinkTreeMain?.thinkRoot;
                allowsWork = root != null && root.ThisAndChildrenRecursive
                    .OfType<ThinkNode_ConditionalWorkMode>()
                    .Where(node => node.workMode == workMode)
                    .Any(node => HasReachableOrdinaryWorkBranch(node, workMode));
                byMode.Add(workMode, allowsWork);
            }

            return allowsWork;
        }

        private static bool HasReachableOrdinaryWorkBranch(
            ThinkNode_ConditionalWorkMode modeNode,
            MechWorkModeDef workMode)
        {
            int workIndex = modeNode.subNodes.FindIndex(child => child is JobGiver_Work);
            if (workIndex < 0)
            {
                return false;
            }

            // The WVC modes below put a threat/escort branch before ordinary work. Their
            // live threat condition is checked in CanRunSchedulerNow. For other modded
            // modes, accept only the same harmless prefixes as vanilla Work: allowed-area
            // correction and energy acquisition. Unknown earlier job givers win before
            // JobGiver_Work, so treating that mode as work-capable would bypass its policy.
            if (WorkModesSuppressedByThreats.Contains(workMode.defName))
            {
                return true;
            }

            for (int index = 0; index < workIndex; index++)
            {
                ThinkNode child = modeNode.subNodes[index];
                if (!(child is JobGiver_SeekAllowedArea) && !(child is JobGiver_GetEnergy))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
