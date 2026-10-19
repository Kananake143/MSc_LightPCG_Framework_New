using UnityEngine;

namespace LightPCG.Core
{
    /// <summary>
    /// Single source of truth for optical behaviour on the grid.
    /// Used by the generator, the bot (BotPlanner / PuzzleBot) and the
    /// physical laser (LaserSystem), so that a plan found by logical search
    /// behaves identically when executed in the scene.
    ///
    /// Orientation set (discrete, 90-degree steps):
    ///   Mirror    : 45, 135, 225, 315
    ///               45/225  = "/"  (d -> ( d.y,  d.x))
    ///               135/315 = "\"  (d -> (-d.y, -d.x))
    ///   Refractor : 0, 90, 180, 270
    ///               0/180   = pass-through
    ///               90      = turn left  (counter-clockwise)  d -> (-d.y,  d.x)
    ///               270     = turn right (clockwise)          d -> ( d.y, -d.x)
    ///
    /// Coordinates: grid x = world +x (east), grid y = world +z (north).
    /// Orientation = yaw (transform.eulerAngles.y), clockwise from north seen from above.
    /// Left / right are relative to the direction of travel.
    ///
    /// CONFIRMED refractor rule (Chapter 3, Table 3.2). Derived from the earlier code:
    ///   - BackwardChainingGenerator puts refractors on bends that turn LEFT or RIGHT,
    ///     so the rule must allow both turns from every incoming direction.
    ///   - Earlier AISolverAgent.RefractorDeflect: 0/180 pass, 90 AND 270 turn left
    ///     (no right turn -> chains with a right-turning refractor were unsolvable).
    ///   - Earlier LaserSystem.Deflect90: pass only at 0; otherwise the exit side for an
    ///     axis-aligned beam was decided by the sign of a float rounding residue
    ///     (dot product ~1e-8), i.e. undefined by design.
    ///   Adopted: keep 0 = pass (solver and laser agreed), keep the solver at 90 (left)
    ///   and 180 (pass), and give 270 the right turn the generator needs.
    ///   Agreement with the earlier solver: 12 of 16 orientation-direction combinations.
    /// Change the table here only, never in two places.
    /// </summary>
    public static class OpticsRules
    {
        public static readonly int[] MirrorRotations    = { 45, 135, 225, 315 };
        public static readonly int[] RefractorRotations = { 0, 90, 180, 270 };

        public static int[] AllowedRotations(TileType t)
            => t == TileType.Mirror ? MirrorRotations : RefractorRotations;

        public static bool IsAllowedRotation(TileType t, int rot)
            => System.Array.IndexOf(AllowedRotations(t), rot) >= 0;

        /// <summary>Snap any angle to the nearest allowed orientation for this type.</summary>
        public static int Snap(TileType t, float degrees)
        {
            float a = ((degrees % 360f) + 360f) % 360f;
            int best = 0; float bestD = float.MaxValue;
            foreach (int r in AllowedRotations(t))
            {
                float d = Mathf.Abs(Mathf.DeltaAngle(a, r));
                if (d < bestD) { bestD = d; best = r; }
            }
            return best;
        }

        /// <summary>
        /// Effective optical class. Two orientations with the same class
        /// produce identical beam behaviour, so they are not distinct states.
        /// Mirror: 0 = "/", 1 = "\".  Refractor: 0 = pass, 1 = left, 2 = right.
        /// </summary>
        public static int EffectiveClass(TileType t, int rot)
        {
            rot = Snap(t, rot);
            if (t == TileType.Mirror) return (rot == 45 || rot == 225) ? 0 : 1;
            if (rot == 0 || rot == 180) return 0;
            return rot == 90 ? 1 : 2;
        }

        public static int EffectiveClassCount(TileType t) => t == TileType.Mirror ? 2 : 3;

        /// <summary>One representative rotation per effective class.</summary>
        public static int[] ClassRepresentatives(TileType t)
            => t == TileType.Mirror ? new[] { 45, 135 } : new[] { 0, 90, 270 };

        public static Vector2Int Deflect(TileType t, int rot, Vector2Int d)
        {
            int c = EffectiveClass(t, rot);
            if (t == TileType.Mirror)
                return c == 0 ? new Vector2Int(d.y, d.x) : new Vector2Int(-d.y, -d.x);
            if (c == 0) return d;
            return c == 1 ? new Vector2Int(-d.y, d.x) : new Vector2Int(d.y, -d.x);
        }

        /// <summary>First allowed rotation that turns inDir into outDir, or -1.</summary>
        public static int RotationFor(TileType t, Vector2Int inDir, Vector2Int outDir)
        {
            foreach (int r in AllowedRotations(t))
                if (Deflect(t, r, inDir) == outDir) return r;
            return -1;
        }

        public static bool IsMovable(TileType t) => t == TileType.Mirror || t == TileType.Refractor;
    }
}
