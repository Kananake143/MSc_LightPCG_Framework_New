using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LightPCG.Core
{
    public struct MobilityFeatures
    {
        public float Mr;   // relocation demand   = misplaced solution pieces / Nsol
        public float Mo;   // rotation demand     = in-place solution pieces with wrong optical class / Nsol
        public float Mi;   // interaction opportunity = interaction-changing single actions / all single actions
        public float Md;   // decoy interference  = interaction-changing actions involving a decoy / interaction-changing actions
        public int NActions, NInteract, NDecoyInteract, InitialOnBeam;
    }

    /// <summary>
    /// Mobility components of MID-M. Computed from the INITIAL state and the
    /// intended solution only, before the solver runs. Never uses solver
    /// outputs (Relocations, SolvePhase, ...), which would leak the outcome.
    /// The single-action space follows MovementRule (study rule: rotate in place,
    /// or carry one cell to a 4-adjacent free interior cell). Walking is not
    /// considered here: a single action counts if it is legal on the board.
    /// </summary>
    public static class MobilityAnalyzer
    {
        public static MobilityFeatures Compute(GridModel board, Vector2Int emitter, Vector2Int emitDir,
                                               PuzzleState initial, IntendedSolution sol)
        {
            var f = new MobilityFeatures();
            int nSol = sol.SolutionCount;

            // ── M_r and M_o: distance from the intended solution (upper bound on the nearest solution)
            int misplaced = 0, misoriented = 0;
            foreach (var kv in sol.Target)
            {
                initial.TryFind(kv.Key, out var cell, out var p);
                if (cell != kv.Value.cell) misplaced++;
                else if (OpticsRules.EffectiveClass(p.Type, p.Rot) !=
                         OpticsRules.EffectiveClass(p.Type, kv.Value.rot)) misoriented++;
            }
            f.Mr = nSol > 0 ? (float)misplaced / nSol : 0f;
            f.Mo = nSol > 0 ? (float)misoriented / nSol : 0f;

            // ── M_i and M_d: one-step action space
            var b0 = BeamTracer.Trace(board, emitter, emitDir, initial.Pieces);
            var onBeamCells = new HashSet<Vector2Int>(b0.Path);
            var hit0 = b0.HitPieceIds;
            f.InitialOnBeam = hit0.Count;

            var empty = new List<Vector2Int>();
            for (int x = 1; x < board.Width - 1; x++)
                for (int y = 1; y < board.Height - 1; y++)
                {
                    var c = new Vector2Int(x, y);
                    var t = board.GetTile(x, y);
                    if (!initial.Pieces.ContainsKey(c) && (t == TileType.Empty || OpticsRules.IsMovable(t)))
                        empty.Add(c);
                }

            var decoyIds = new HashSet<int>(initial.Pieces.Values.Where(p => p.IsDecoy).Select(p => p.Id));

            foreach (var kv in initial.Pieces.ToList())
            {
                var cell = kv.Key; var p = kv.Value;
                int curClass = OpticsRules.EffectiveClass(p.Type, p.Rot);
                bool pieceOnBeam = hit0.Contains(p.Id);
                var reps = OpticsRules.ClassRepresentatives(p.Type);

                // (a) rotate in place to every other optical class
                foreach (int r in reps)
                {
                    if (OpticsRules.EffectiveClass(p.Type, r) == curClass) continue;
                    f.NActions++;
                    if (!pieceOnBeam) continue;                  // off-beam rotation cannot change the beam
                    Evaluate(board, emitter, emitDir, initial, new Move { PieceId = p.Id, From = cell, To = cell, Rot = r },
                             hit0, b0.HitReceiver, p.IsDecoy, decoyIds, ref f);
                }

                // (b) relocate to every empty cell allowed by MovementRule
                //     (study rule: the 4-adjacent free cells), one action per optical class
                foreach (var to in empty)
                {
                    if (!MovementRule.AllowRelocation || !MovementRule.CarryOk(cell, to)) continue;
                    foreach (int r in reps)
                    {
                        f.NActions++;
                        // Moving an off-beam piece to an off-beam cell cannot change the beam.
                        if (!pieceOnBeam && !onBeamCells.Contains(to)) continue;
                        Evaluate(board, emitter, emitDir, initial, new Move { PieceId = p.Id, From = cell, To = to, Rot = r },
                                 hit0, b0.HitReceiver, p.IsDecoy, decoyIds, ref f);
                    }
                }
            }

            f.Mi = f.NActions > 0 ? (float)f.NInteract / f.NActions : 0f;
            f.Md = f.NInteract > 0 ? (float)f.NDecoyInteract / f.NInteract : 0f;
            return f;
        }

        static void Evaluate(GridModel board, Vector2Int emitter, Vector2Int emitDir, PuzzleState s0, Move m,
                             List<int> hit0, bool recv0, bool movedIsDecoy, HashSet<int> decoyIds,
                             ref MobilityFeatures f)
        {
            var s = s0.Clone();
            if (!s.Apply(m, board)) return;
            var b = BeamTracer.Trace(board, emitter, emitDir, s.Pieces);
            bool changed = b.HitReceiver != recv0 || !b.HitPieceIds.SequenceEqual(hit0);
            if (!changed) return;
            f.NInteract++;
            if (movedIsDecoy || b.HitPieceIds.Any(decoyIds.Contains)) f.NDecoyInteract++;
        }
    }
}
