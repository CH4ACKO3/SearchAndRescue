using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SearchAndRescue
{
    public sealed class Designator_SearchAndRescue : Designator
    {
        internal const string CommandIconPath = "UI/Icons/ThingCategories/Medicine";

        private readonly Designator_Capture capture = new Designator_Capture();
        private readonly Designator_Treat treat = new Designator_Treat();
        private readonly Designator_Rescue rescue = new Designator_Rescue();

        public override DrawStyleCategoryDef DrawStyleCategory => DrawStyleCategoryDefOf.FilledRectangle;

        public Designator_SearchAndRescue()
        {
            defaultLabel = "SAR_Command_Label".Translate();
            defaultDesc = "SAR_Command_Desc".Translate();
            icon = ContentFinder<Texture2D>.Get(CommandIconPath);
            soundDragSustain = SoundDefOf.Designate_DragStandard;
            soundDragChanged = SoundDefOf.Designate_DragStandard_Changed;
            soundSucceeded = SoundDefOf.Designate_Haul;
            useMouseIcon = true;
        }

        public override AcceptanceReport CanDesignateCell(IntVec3 cell)
        {
            if (!cell.InBounds(Map) || cell.Fogged(Map))
            {
                return false;
            }

            return cell.GetThingList(Map).Any(thing => CanDesignateThing(thing).Accepted);
        }

        public override void DesignateSingleCell(IntVec3 cell)
        {
            foreach (Pawn pawn in cell.GetThingList(Map).OfType<Pawn>().ToList())
            {
                if (CanDesignateThing(pawn).Accepted)
                {
                    DesignateThing(pawn);
                }
            }
        }

        public override AcceptanceReport CanDesignateThing(Thing thing)
        {
            return capture.CanDesignateThing(thing).Accepted ||
                   treat.CanDesignateThing(thing).Accepted ||
                   rescue.CanDesignateThing(thing).Accepted;
        }

        public override void DesignateThing(Thing thing)
        {
            // Capture is added first so the other two designators can immediately accept a
            // hostile humanlike pawn during the same drag operation.
            foreach (Designator_PawnStage stage in new Designator_PawnStage[] { capture, treat, rescue })
            {
                if (stage.CanDesignateThing(thing).Accepted)
                {
                    stage.DesignateThing(thing);
                }
            }
        }

        public override IEnumerable<FloatMenuOption> RightClickFloatMenuOptions
        {
            get
            {
                yield return SelectOption("SAR_Command_All", new Designator_SearchAndRescue());
                yield return SelectOption("SAR_Treat_Label", new Designator_Treat());
                yield return SelectOption("SAR_Rescue_Label", new Designator_Rescue());
                yield return SelectOption("SAR_Capture_Label", new Designator_Capture());
                yield return new FloatMenuOption("SAR_SetRescuePoint".Translate(), () =>
                    Find.DesignatorManager.Select(new Designator_RescuePoint()));

                List<Designation> points = Map.designationManager
                    .SpawnedDesignationsOfDef(SearchAndRescueDefOf.SAR_RescuePoint).ToList();
                System.Action clearPoints = points.Count == 0 ? null : (System.Action)(() =>
                {
                    foreach (Designation point in points)
                    {
                        Map.designationManager.RemoveDesignation(point);
                    }
                    Map.GetComponent<SearchAndRescueCoordinator>()?.NotifyRescuePointChanged();
                });
                yield return new FloatMenuOption("SAR_ClearRescuePoint".Translate(), clearPoints);

                List<Designation> orders = Map.designationManager.AllDesignations
                    .Where(designation => designation.def == SearchAndRescueDefOf.SAR_Capture ||
                                          designation.def == SearchAndRescueDefOf.SAR_Treat ||
                                          designation.def == SearchAndRescueDefOf.SAR_Rescue)
                    .ToList();
                SearchAndRescueCoordinator coordinator = Map.GetComponent<SearchAndRescueCoordinator>();
                System.Action clearOrders = orders.Count == 0 && !coordinator.HasRecentMarkerMemories
                    ? null
                    : (System.Action)(() =>
                {
                    foreach (Designation order in orders)
                    {
                        Map.designationManager.RemoveDesignation(order);
                    }
                    coordinator.ClearRecentMarkerMemories();
                });
                yield return new FloatMenuOption("SAR_ClearAllOrders".Translate(orders.Count), clearOrders);
            }
        }

        private static FloatMenuOption SelectOption(string labelKey, Designator designator)
        {
            return new FloatMenuOption(labelKey.Translate(), () => Find.DesignatorManager.Select(designator));
        }
    }

    public abstract class Designator_PawnStage : Designator
    {
        public override DrawStyleCategoryDef DrawStyleCategory => DrawStyleCategoryDefOf.FilledRectangle;

        protected abstract DesignationDef StageDesignation { get; }
        protected override DesignationDef Designation => StageDesignation;

        protected Designator_PawnStage(string labelKey, string descriptionKey, string iconPath)
        {
            defaultLabel = labelKey.Translate();
            defaultDesc = descriptionKey.Translate();
            icon = ContentFinder<Texture2D>.Get(iconPath);
            soundDragSustain = SoundDefOf.Designate_DragStandard;
            soundDragChanged = SoundDefOf.Designate_DragStandard_Changed;
            soundSucceeded = SoundDefOf.Designate_Haul;
            useMouseIcon = true;
        }

        public override AcceptanceReport CanDesignateCell(IntVec3 cell)
        {
            if (!cell.InBounds(Map) || cell.Fogged(Map))
            {
                return false;
            }

            return cell.GetThingList(Map).Any(thing => CanDesignateThing(thing).Accepted);
        }

        public override void DesignateSingleCell(IntVec3 cell)
        {
            foreach (Pawn pawn in cell.GetThingList(Map).OfType<Pawn>().ToList())
            {
                if (CanDesignateThing(pawn).Accepted)
                {
                    DesignateThing(pawn);
                }
            }
        }

        public override void DesignateThing(Thing thing)
        {
            if (Map.designationManager.DesignationOn(thing, StageDesignation) == null)
            {
                Map.designationManager.AddDesignation(new StageDesignation(thing, StageDesignation));
                SearchAndRescueStage stage = StageDesignation == SearchAndRescueDefOf.SAR_Capture
                    ? SearchAndRescueStage.Capture
                    : StageDesignation == SearchAndRescueDefOf.SAR_Treat
                        ? SearchAndRescueStage.Treat
                        : SearchAndRescueStage.Rescue;
                Map.GetComponent<SearchAndRescueCoordinator>()?.NotifyStageDesignationAdded(thing as Pawn, stage);
            }
        }

        protected AcceptanceReport BasicPawnCheck(Thing thing, bool allowAnimals, out Pawn pawn)
        {
            pawn = thing as Pawn;
            if (!TargetEligibility.IsLivingFleshPawn(pawn) || pawn.Map != Map ||
                (!pawn.RaceProps.Humanlike && (!allowAnimals || !pawn.RaceProps.Animal)))
            {
                return (allowAnimals ? "SAR_OnlyHumanlikeOrAnimal" : "SAR_OnlyHumanlike").Translate();
            }

            if (Map.designationManager.DesignationOn(pawn, StageDesignation) != null)
            {
                return false;
            }

            return true;
        }
    }

    public sealed class Designator_Treat : Designator_PawnStage
    {
        protected override DesignationDef StageDesignation => SearchAndRescueDefOf.SAR_Treat;

        public Designator_Treat() : base("SAR_Treat_Label", "SAR_Treat_Desc", "UI/Designators/Tame")
        {
        }

        public override AcceptanceReport CanDesignateThing(Thing thing)
        {
            AcceptanceReport basic = BasicPawnCheck(thing, true, out Pawn pawn);
            if (!basic.Accepted)
            {
                return basic;
            }

            if (pawn.RaceProps.Animal && pawn.HostileTo(Faction.OfPlayer))
            {
                return "SAR_HostileAnimalUnsupported".Translate();
            }

            if (pawn.HostileTo(Faction.OfPlayer) && Map.designationManager.DesignationOn(pawn, SearchAndRescueDefOf.SAR_Capture) == null)
            {
                return "SAR_TreatmentRequiresCapture".Translate();
            }

            return Compatibility.NeedsAnyFieldTreatment(pawn)
                ? AcceptanceReport.WasAccepted
                : "SAR_NoTreatmentNeeded".Translate();
        }
    }

    public sealed class Designator_Rescue : Designator_PawnStage
    {
        protected override DesignationDef StageDesignation => SearchAndRescueDefOf.SAR_Rescue;

        public Designator_Rescue() : base("SAR_Rescue_Label", "SAR_Rescue_Desc", "UI/Designators/Haul")
        {
        }

        public override AcceptanceReport CanDesignateThing(Thing thing)
        {
            AcceptanceReport basic = BasicPawnCheck(thing, true, out Pawn pawn);
            if (!basic.Accepted)
            {
                return basic;
            }

            if (!pawn.Downed)
            {
                return "SAR_MustBeDowned".Translate();
            }

            if (pawn.RaceProps.Animal && pawn.HostileTo(Faction.OfPlayer))
            {
                return "SAR_HostileAnimalUnsupported".Translate();
            }

            if (pawn.HostileTo(Faction.OfPlayer) && Map.designationManager.DesignationOn(pawn, SearchAndRescueDefOf.SAR_Capture) == null)
            {
                return "SAR_RescueRequiresCapture".Translate();
            }

            Building_Bed currentBed = pawn.CurrentBed();
            if (pawn.InBed() && Compatibility.IsSafeRescueBed(currentBed, pawn))
            {
                // The combined command adds treatment before it evaluates rescue. Adding a
                // rescue designation here would be cleaned up as an already-completed
                // delivery and would also retire that freshly added treatment designation.
                // A pawn already in a valid destination needs care, not another bed transfer.
                return "SAR_AlreadyInSafeBed".Translate();
            }

            return true;
        }

        public override IEnumerable<FloatMenuOption> RightClickFloatMenuOptions
        {
            get
            {
                foreach (FloatMenuOption option in base.RightClickFloatMenuOptions)
                {
                    yield return option;
                }

                yield return new FloatMenuOption("SAR_SetRescuePoint".Translate(), () =>
                    Find.DesignatorManager.Select(new Designator_RescuePoint()));

                List<Designation> points = Map.designationManager
                    .SpawnedDesignationsOfDef(SearchAndRescueDefOf.SAR_RescuePoint).ToList();
                System.Action clear = points.Count == 0 ? null : (System.Action)(() =>
                {
                    foreach (Designation point in points)
                    {
                        Map.designationManager.RemoveDesignation(point);
                    }
                    Map.GetComponent<SearchAndRescueCoordinator>()?.NotifyRescuePointChanged();
                });
                yield return new FloatMenuOption("SAR_ClearRescuePoint".Translate(), clear);
            }
        }
    }

    public sealed class Designator_Capture : Designator_PawnStage
    {
        protected override DesignationDef StageDesignation => SearchAndRescueDefOf.SAR_Capture;

        public Designator_Capture() : base("SAR_Capture_Label", "SAR_Capture_Desc", "UI/Designators/Hunt")
        {
        }

        public override AcceptanceReport CanDesignateThing(Thing thing)
        {
            AcceptanceReport basic = BasicPawnCheck(thing, false, out Pawn pawn);
            if (!basic.Accepted)
            {
                return basic;
            }

            if (!TargetEligibility.CanBeCaptured(pawn) || !pawn.Downed ||
                pawn.IsPrisonerOfColony || !pawn.HostileTo(Faction.OfPlayer))
            {
                return "SAR_CaptureRequiresHostile".Translate();
            }

            return true;
        }
    }

    internal sealed class Designator_RescuePoint : Designator
    {
        protected override DesignationDef Designation => SearchAndRescueDefOf.SAR_RescuePoint;
        public override DrawStyleCategoryDef DrawStyleCategory => DrawStyleCategoryDefOf.FilledRectangle;

        public Designator_RescuePoint()
        {
            defaultLabel = "SAR_RescuePoint_Label".Translate();
            defaultDesc = "SAR_RescuePoint_Desc".Translate();
            icon = ContentFinder<Texture2D>.Get("UI/Designators/PlanOn");
            soundSucceeded = SoundDefOf.Designate_PlanAdd;
            useMouseIcon = true;
        }

        public override AcceptanceReport CanDesignateCell(IntVec3 cell)
        {
            if (!cell.InBounds(Map) || cell.Fogged(Map) || !cell.Standable(Map))
            {
                return "SAR_InvalidRescuePoint".Translate();
            }

            return true;
        }

        public override void DesignateSingleCell(IntVec3 cell)
        {
            foreach (Designation point in Map.designationManager.SpawnedDesignationsOfDef(Designation).ToList())
            {
                Map.designationManager.RemoveDesignation(point);
            }

            Map.designationManager.AddDesignation(new Designation(cell, Designation));
            Map.GetComponent<SearchAndRescueCoordinator>()?.NotifyRescuePointChanged();
        }
    }
}
