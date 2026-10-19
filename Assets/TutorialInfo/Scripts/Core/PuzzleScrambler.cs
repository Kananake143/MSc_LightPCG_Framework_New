using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LightPCG.Core
{
    /// <summary>Intended (solved) configuration produced by CGRPG.</summary>
    public sealed class IntendedSolution
    {
        public PuzzleState Solved;                               // solution + decoy pieces, solution rotations set
        public Dictionary<int, (Vector2Int cell, int rot)> Target // solution pieces only
            = new Dictionary<int, (Vector2Int, int)>();
        public List<Vector2Int> SolutionBeamPath = new List<Vector2Int>();
        public int SolutionCount => Target.Count;
        public string FailureReason;                              // null when valid

        /// <summary>
        /// Build the intended solution from the ordered bend cells of the
        /// reverse-constructed path (emitter -> ... -> receiver order).
        /// Each bend's orientation is derived from its incoming and outgoing
        /// directions using OpticsRules, so the solved state is defined by
        /// the same rules the solver and laser use.
        /// </summary>
        public static IntendedSolution Build(GridModel board, Vector2Int emitter, Vector2Int emitDir,
                                             Vector2Int receiver,
                                             IList<(Vector2Int cell, TileType type)> orderedBends,
                                             IList<(Vector2Int cell, TileType type)> decoys,
                                             System.Random rng)
        {
            var s = new IntendedSolution { Solved = new PuzzleState() };
            int id = 0;
            Vector2Int prev = emitter;
            Vector2Int inDir = emitDir;

            for (int i = 0; i < orderedBends.Count; i++)
            {
                var (cell, type) = orderedBends[i];
                Vector2Int next = i + 1 < orderedBends.Count ? orderedBends[i + 1].cell : receiver;
                Vector2Int seg = cell - prev;
                if (i == 0 && AxisDir(seg) != emitDir)
                { s.FailureReason = "first bend is not in line with the emitter direction"; return s; }
                if (i > 0) inDir = AxisDir(seg);
                Vector2Int outDir = AxisDir(next - cell);
                if (inDir == Vector2Int.zero || outDir == Vector2Int.zero)
                { s.FailureReason = $"non axis-aligned segment at bend {i}"; return s; }

                int rot = OpticsRules.RotationFor(type, inDir, outDir);
                if (rot < 0)
                { s.FailureReason = $"{type} cannot turn {inDir}->{outDir} at bend {i}"; return s; }

                s.Solved.Pieces[cell] = new Piece { Id = id, Type = type, Rot = rot, IsDecoy = false };
                s.Target[id] = (cell, rot);
                id++;
                prev = cell;
            }

            foreach (var (cell, type) in decoys)
            {
                var rots = OpticsRules.AllowedRotations(type);
                s.Solved.Pieces[cell] = new Piece
                { Id = id++, Type = type, Rot = rots[rng.Next(rots.Length)], IsDecoy = true };
            }

            var beam = BeamTracer.Trace(board, emitter, emitDir, s.Solved.Pieces);
            if (!beam.HitReceiver) { s.FailureReason = "intended configuration does not reach receiver"; return s; }
            // every solution piece must be used, in order
            var expected = Enumerable.Range(0, s.SolutionCount).ToList();
            if (!beam.HitPieceIds.SequenceEqual(expected))
            { s.FailureReason = "beam does not use solution pieces in intended order (decoy interference)"; return s; }

            s.SolutionBeamPath = beam.Path;
            return s;
        }

        static Vector2Int AxisDir(Vector2Int v)
        {
            if (v.x != 0 && v.y == 0) return new Vector2Int(Math.Sign(v.x), 0);
            if (v.y != 0 && v.x == 0) return new Vector2Int(0, Math.Sign(v.y));
            return Vector2Int.zero;
        }
    }

    public sealed class ScrambleResult
    {
        public PuzzleState Initial;
        public List<Move> Forward = new List<Move>();      // solved -> initial
        public List<Move> Certificate = new List<Move>();  // initial -> solved (legal replay)
        public bool CertificateValid;                     // logical replay reaches receiver, every action legal
        public bool CertificateReachable;                 // agent can walk to every source/target, as the bot does
        public int Attempts;
        public int KRelApplied;
        public int RelocationSteps;                       // single-cell carries applied per relocated piece
        public string MovementRuleUsed;                   // MovementRule.Describe() at scramble time
    }

    /// <summary>
    /// Builds the initial puzzle state from the intended solution through
    /// LEGAL moves only (MovementRule), and records the inverse sequence as a
    /// solution certificate. Replaying the certificate is a constructive proof
    /// that the instance is solvable under the implemented rules.
    ///
    /// Study rule (carry one cell): each of the kRel relocated solution pieces
    /// is carried relocationSteps times (default 1) to a 4-adjacent free
    /// interior cell that is neither a solution cell nor on the intended beam.
    /// The certificate undoes these carries in exact reverse order, so every
    /// certificate action is itself a one-cell carry or a rotation in place.
    /// </summary>
    public static class PuzzleScrambler
    {
        static readonly Vector2Int[] D4 =
            { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };

        public static ScrambleResult Scramble(GridModel board, Vector2Int emitter, Vector2Int emitDir,
                                              Vector2Int receiver, IntendedSolution sol,
                                              int kRel, System.Random rng, int maxAttempts = 30,
                                              bool requireReachable = true, int relocationSteps = 1)
        {
            kRel = Mathf.Clamp(kRel, 0, sol.SolutionCount);
            if (!MovementRule.AllowRelocation) kRel = 0;          // rotate-only game: nothing can be displaced
            relocationSteps = Math.Max(1, relocationSteps);
            var forbidden = new HashSet<Vector2Int>(sol.SolutionBeamPath);
            foreach (var t in sol.Target.Values) forbidden.Add(t.cell);

            ScrambleResult res = null;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                res = new ScrambleResult
                {
                    Attempts = attempt, KRelApplied = kRel, RelocationSteps = relocationSteps,
                    MovementRuleUsed = MovementRule.Describe()
                };
                var state = sol.Solved.Clone();

                // 1) relocate kRel randomly chosen solution pieces by legal carries off the solution beam
                var ids = sol.Target.Keys.OrderBy(_ => rng.Next()).ToList();
                var relocated = new HashSet<int>(ids.Take(kRel));
                var relocMoves = new List<(Move fwd, int rotBefore)>();
                bool ok = true;
                foreach (int pid in relocated)
                {
                    for (int step = 0; step < relocationSteps && ok; step++)
                    {
                        state.TryFind(pid, out var from, out var p);
                        var cand = CarryTargets(board, state, from, forbidden);
                        if (cand.Count == 0) { ok = false; break; }
                        var to = cand[rng.Next(cand.Count)];
                        var rots = OpticsRules.AllowedRotations(p.Type);
                        var m = new Move { PieceId = pid, From = from, To = to, Rot = rots[rng.Next(rots.Length)] };
                        if (!state.Apply(m, board)) { ok = false; break; }
                        res.Forward.Add(m);
                        relocMoves.Add((m, p.Rot));
                    }
                    if (!ok) break;
                }
                if (!ok) continue;

                // 2) randomise orientation of every other movable piece (solution pieces in place + decoys)
                foreach (var cell in state.Pieces.Keys.ToList())
                {
                    var p = state.Pieces[cell];
                    if (relocated.Contains(p.Id)) continue;
                    var rots = OpticsRules.AllowedRotations(p.Type);
                    int r = rots[rng.Next(rots.Length)];
                    if (r == p.Rot) continue;
                    var m = new Move { PieceId = p.Id, From = cell, To = cell, Rot = r };
                    state.Apply(m, board);
                    res.Forward.Add(m);
                }

                // 3) reject trivially solved initial states
                if (BeamTracer.Trace(board, emitter, emitDir, state.Pieces).HitReceiver) continue;

                // 4) certificate: undo the carries in reverse order (each undo is a legal carry of the
                //    same length and restores the previous orientation), then fix wrong orientations
                for (int i = relocMoves.Count - 1; i >= 0; i--)
                {
                    var (f, rotBefore) = relocMoves[i];
                    res.Certificate.Add(new Move { PieceId = f.PieceId, From = f.To, To = f.From, Rot = rotBefore });
                }
                foreach (var kv in sol.Target)
                {
                    if (relocated.Contains(kv.Key)) continue;
                    var p = state.Pieces[kv.Value.cell];
                    if (OpticsRules.EffectiveClass(p.Type, p.Rot) != OpticsRules.EffectiveClass(p.Type, kv.Value.rot))
                        res.Certificate.Add(new Move
                        { PieceId = kv.Key, From = kv.Value.cell, To = kv.Value.cell, Rot = kv.Value.rot });
                }

                res.Initial = state;
                res.CertificateValid = VerifyCertificate(board, emitter, emitDir, state, res.Certificate);
                res.CertificateReachable = CertificateReachable(board, emitter, state, res.Certificate);
                if (!res.CertificateValid) continue;                            // defensive: should not happen
                if (requireReachable && !res.CertificateReachable) continue;   // agent could not walk to a piece
                return res;
            }
            res.Initial = null;   // caller must regenerate the level
            return res;
        }

        /// <summary>Cells a piece at 'from' may be carried to under MovementRule, excluding forbidden cells.</summary>
        static List<Vector2Int> CarryTargets(GridModel b, PuzzleState s, Vector2Int from, HashSet<Vector2Int> forbidden)
        {
            var l = new List<Vector2Int>();
            if (MovementRule.MaxCarryDistance == 1)
            {
                foreach (var d in D4)
                {
                    var c = from + d;
                    if (!forbidden.Contains(c) && Navigation.Placeable(b, s.Pieces, c)) l.Add(c);
                }
                return l;
            }
            for (int x = 1; x < b.Width - 1; x++)
                for (int y = 1; y < b.Height - 1; y++)
                {
                    var c = new Vector2Int(x, y);
                    if (c == from || forbidden.Contains(c) || !MovementRule.CarryOk(from, c)) continue;
                    if (Navigation.Placeable(b, s.Pieces, c)) l.Add(c);
                }
            return l;
        }

        /// <summary>Logical replay: every action must be legal (PuzzleState.Apply, incl. MovementRule)
        /// and the final beam must reach the receiver.</summary>
        public static bool VerifyCertificate(GridModel board, Vector2Int emitter, Vector2Int emitDir,
                                             PuzzleState initial, List<Move> cert)
        {
            var s = initial.Clone();
            foreach (var m in cert) if (!s.Apply(m, board)) return false;
            return BeamTracer.Trace(board, emitter, emitDir, s.Pieces).HitReceiver;
        }

        /// <summary>
        /// Walking reachability, with the same walking model as the bot (Navigation):
        /// the agent starts on the first walkable cell next to the emitter, walks
        /// 4-connected over free cells (the door is not walkable), stands next to the
        /// source to pick up or rotate, and, for a carry, stands next to the target
        /// (not on it) with the piece removed from its source cell.
        /// </summary>
        public static bool CertificateReachable(GridModel board, Vector2Int emitter,
                                                PuzzleState initial, List<Move> cert)
        {
            var s = initial.Clone();
            var agent = Navigation.StartCell(board, s.Pieces, emitter);
            if (agent == -Vector2Int.one) return cert.Count == 0;
            foreach (var m in cert)
            {
                var dist = Navigation.Distances(board, s.Pieces, agent);
                var st = Navigation.StandingCell(dist, m.From, -Vector2Int.one);
                if (!st.HasValue) return false;
                if (m.From != m.To)
                {
                    var without = new Dictionary<Vector2Int, Piece>(s.Pieces);
                    without.Remove(m.From);
                    var dist2 = Navigation.Distances(board, without, st.Value);
                    var st2 = Navigation.StandingCell(dist2, m.To, m.To);
                    if (!st2.HasValue) return false;
                    agent = st2.Value;
                }
                else agent = st.Value;
                if (!s.Apply(m, board)) return false;
            }
            return true;
        }
    }
}
