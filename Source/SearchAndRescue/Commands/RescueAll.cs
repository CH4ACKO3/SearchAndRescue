using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SearchAndRescue
{
    public sealed class Designator_RescueAll : Designator
    {
        private enum Scope { Friendly, DownedHostile, Everyone }

        private readonly Designator_SearchAndRescue combined = new Designator_SearchAndRescue();

        public Designator_RescueAll()
        {
            defaultLabel = "SAR_RescueAll_Label".Translate();
            defaultDesc = "SAR_RescueAll_Desc".Translate();
            icon = ContentFinder<Texture2D>.Get(Designator_SearchAndRescue.CommandIconPath);
            soundSucceeded = SoundDefOf.Designate_Haul;
        }

        // This is an immediate map-wide command, so do not enter Designator's drag mode.
        public override void ProcessInput(Event ev) => Apply(Map, Scope.Friendly);

        public override AcceptanceReport CanDesignateCell(IntVec3 cell) => false;

        public override IEnumerable<FloatMenuOption> RightClickFloatMenuOptions
        {
            get
            {
                Map map = Map;
                yield return Option(map, Scope.Friendly, "SAR_RescueAll_Friendly");
                yield return Option(map, Scope.DownedHostile, "SAR_RescueAll_Hostile");
                yield return Option(map, Scope.Everyone, "SAR_RescueAll_Everyone");
            }
        }

        private FloatMenuOption Option(Map map, Scope scope, string label)
        {
            int count = Targets(map, scope).Count();
            return new FloatMenuOption(label.Translate(count),
                count == 0 ? null : (Action)(() => Apply(map, scope)));
        }

        private IEnumerable<Pawn> Targets(Map map, Scope scope)
        {
            if (map == null || map != Find.CurrentMap)
                yield break;

            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn.Dead || pawn.Position.Fogged(map))
                    continue;

                bool hostile = pawn.HostileTo(Faction.OfPlayer);
                if (scope == Scope.Friendly && (hostile ||
                    (pawn.Faction != Faction.OfPlayer &&
                     pawn.Faction?.RelationKindWith(Faction.OfPlayer) != FactionRelationKind.Ally)))
                    continue;
                if (scope == Scope.DownedHostile && (!hostile || !pawn.Downed))
                    continue;

                if (combined.CanDesignateThing(pawn).Accepted)
                    yield return pawn;
            }
        }

        private void Apply(Map map, Scope scope)
        {
            // A float menu can outlive a map switch. Never apply its action to another map.
            if (map == null || map != Find.CurrentMap)
                return;

            List<Pawn> targets = Targets(map, scope).ToList();
            foreach (Pawn pawn in targets)
                combined.DesignateThing(pawn);

            Finalize(targets.Count > 0);
            Messages.Message("SAR_RescueAll_Result".Translate(targets.Count),
                MessageTypeDefOf.NeutralEvent, historical: false);
        }
    }
}
