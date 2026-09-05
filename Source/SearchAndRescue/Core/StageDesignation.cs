using UnityEngine;
using Verse;

namespace SearchAndRescue
{
    public sealed class StageDesignation : Designation
    {
        public StageDesignation()
        {
        }

        public StageDesignation(LocalTargetInfo target, DesignationDef def) : base(target, def)
        {
        }

        public override void DesignationDraw()
        {
            if (target.HasThing && !target.Thing.Spawned)
            {
                return;
            }

            Vector3 location = DrawLoc();
            if (def == SearchAndRescueDefOf.SAR_Capture)
            {
                location.x -= 0.36f;
            }
            else if (def == SearchAndRescueDefOf.SAR_Rescue)
            {
                location.x += 0.36f;
            }

            // Keep the stage badges close to the pawn's head and compact enough that
            // all three can be read as one status cluster when combined.
            location.z += 0.88f;
            location.y += 0.02f;
            Matrix4x4 matrix = Matrix4x4.TRS(location, Quaternion.identity, new Vector3(0.34f, 1f, 0.34f));
            Graphics.DrawMesh(MeshPool.plane10, matrix, IconMat, 0);
        }
    }
}
