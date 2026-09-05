using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SearchAndRescue
{
    [StaticConstructorOnStartup]
    internal sealed class Command_SearchAndRescuePawn : Command_Action
    {
        private static readonly Texture2D MedicineIcon =
            ContentFinder<Texture2D>.Get(Designator_SearchAndRescue.CommandIconPath);

        private readonly Pawn target;

        public override IEnumerable<FloatMenuOption> RightClickFloatMenuOptions
        {
            get
            {
                yield return Option("SAR_Command_All", new Designator_SearchAndRescue());
                yield return Option("SAR_Treat_Label", new Designator_Treat());
                yield return Option("SAR_Rescue_Label", new Designator_Rescue());
                yield return Option("SAR_Capture_Label", new Designator_Capture());
            }
        }

        public Command_SearchAndRescuePawn(Pawn target)
        {
            this.target = target;
            defaultLabel = "SAR_Command_Label".Translate();
            defaultDesc = "SAR_Command_Desc".Translate();
            icon = MedicineIcon;
            activateSound = SoundDefOf.Designate_Haul;
            groupKey = 0x534153;
            Order = -15f;
            action = () => Apply(new Designator_SearchAndRescue());
        }

        private FloatMenuOption Option(string labelKey, Designator designator)
        {
            AcceptanceReport report = CanApply(designator);
            FloatMenuOption option = new FloatMenuOption(
                labelKey.Translate(),
                report.Accepted ? (System.Action)(() => Apply(designator)) : null);
            if (!report.Accepted && !report.Reason.NullOrEmpty())
            {
                option.tooltip = report.Reason;
            }
            return option;
        }

        private AcceptanceReport CanApply(Designator designator)
        {
            if (target == null || target.Dead || !target.Spawned || target.Map != Find.CurrentMap)
            {
                return AcceptanceReport.WasRejected;
            }

            return designator.CanDesignateThing(target);
        }

        private void Apply(Designator designator)
        {
            if (CanApply(designator).Accepted)
            {
                designator.DesignateThing(target);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    internal static class Pawn_SearchAndRescueCommandPatch
    {
        private static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            __result = AddCommand(__result, __instance);
        }

        private static IEnumerable<Gizmo> AddCommand(IEnumerable<Gizmo> original, Pawn pawn)
        {
            foreach (Gizmo gizmo in original)
            {
                yield return gizmo;
            }

            if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Map != Find.CurrentMap)
            {
                yield break;
            }

            Designator_SearchAndRescue designator = new Designator_SearchAndRescue();
            if (designator.CanDesignateThing(pawn).Accepted)
            {
                // One command is emitted per selected pawn. RimWorld groups identical
                // commands and invokes each member, so left-click and right-click actions
                // naturally apply to every eligible pawn in a multi-selection.
                yield return new Command_SearchAndRescuePawn(pawn);
            }
        }
    }

}
