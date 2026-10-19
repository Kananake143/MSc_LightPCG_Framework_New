using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace LightPCG.Core
{
    // ════════════════════════════════════════════════════════════════════
    // BOT PLANNER (MID-M solver bot, logical part)
    //
    // The bot sees what a player sees: the fixed board (walls, door, emitter,
    // receiver), the emitter direction, and the cell, type and orientation of
    // every movable component. It does NOT see the intended solution, the
    // certificate or which components are decoys.
    //
    // Action model = legal actions of Chapter 3 (Section 3.2.4), with walking:
    //   - rotate a component in place, or carry it to a free interior cell allowed
    //     by MovementRule (study rule: one cell, i.e. a 4-adjacent free cell)
    //   - the bot must be able to walk (4-connected, over free cells) to a cell
    //     next to the component, and, after picking it up, to a cell next to the
    //     target. No teleporting.
    //   - orientations are searched by optical class (OpticsRules), because two
    //     orientations of the same class give identical beam behaviour.
    //
    // Search = weighted A* over (component layout), unit cost per action.
    // Successors are restricted to actions that can change the current beam
    // (beam-relevant action generation):
    //   (a) rotate a component the beam hits, to another class;
    //   (b) move any component onto a free cell of the beam that would exist
    //       without it, in a class that deflects the beam;
    //   (c) move a component the beam hits to a free cell off the beam ("park").
    // Two pruning rules bound the branching factor and make the search
    // incomplete: only the SourcesPerType nearest off-beam components of each
    // type are considered as sources for (b), and only the ParkingCandidates
    // nearest free cells are considered for (c). Both values are reported.
    // Under a carry limit, only off-beam components that can reach a free beam
    // cell in one carry count as sources (the others cannot produce (b) actions).
    // The bot does not plan multi-carry approaches (two or more carries of the
    // same component towards the beam that do not change it on the way); this
    // is a further source of incompleteness, reported in Chapter 3.
    // ════════════════════════════════════════════════════════════════════

    public sealed class BotConfig
    {
        public int MaxExpanded = 100000;     // node limit (expanded nodes)
        public int MaxDepth = 12;            // maximum plan length (actions)
        public double TimeLimitMs = 8000;    // wall-clock limit for the search
        public float HeuristicWeight = 2f;   // f = g + w * h
        public int SourcesPerType = 2;       // pruning rule for (b)
        public int ParkingCandidates = 3;    // pruning rule for (c)
        // The movement rule is NOT configured here: it is read from MovementRule (PuzzleState.cs),
        // the single definition shared with the scramble, PuzzleState.Apply and MobilityAnalyzer.
    }

    public enum SearchOutcome { Found, AlreadySolved, NodeLimit, TimeLimit, Exhausted, NoAgentCell }

    public sealed class PlanResult
    {
        public SearchOutcome Outcome;
        public List<Move> Plan = new List<Move>();
        public int Expanded;          // nodes taken from the open list (= SearchNodes)
        public int Generated;         // successor states created
        public int Duplicates;        // successors discarded as already seen
        public int MaxDepthReached;
        public double TimeMs;
        public bool Found => Outcome == SearchOutcome.Found || Outcome == SearchOutcome.AlreadySolved;
    }

    /// <summary>Walking model shared by the planner and the Unity bot.</summary>
    public static class Navigation
    {
        public static readonly Vector2Int[] D4 =
            { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };

        public static bool InBounds(GridModel b, Vector2Int c)
            => c.x >= 0 && c.y >= 0 && c.x < b.Width && c.y < b.Height;

        /// <summary>A cell the bot can stand on or walk through.</summary>
        public static bool Walkable(GridModel b, IReadOnlyDictionary<Vector2Int, Piece> pieces, Vector2Int c)
        {
            if (!InBounds(b, c) || pieces.ContainsKey(c)) return false;
            var t = b.GetTile(c.x, c.y);
            return t == TileType.Empty || OpticsRules.IsMovable(t);   // movable tile without a piece = vacated cell
        }

        /// <summary>A cell a component may be placed on.</summary>
        public static bool Placeable(GridModel b, IReadOnlyDictionary<Vector2Int, Piece> pieces, Vector2Int c)
        {
            if (c.x < 1 || c.y < 1 || c.x >= b.Width - 1 || c.y >= b.Height - 1) return false;
            return Walkable(b, pieces, c);
        }

        /// <summary>BFS distances from start over walkable cells (start itself is always included).</summary>
        public static Dictionary<Vector2Int, int> Distances(GridModel b, IReadOnlyDictionary<Vector2Int, Piece> pieces,
                                                            Vector2Int start)
        {
            var dist = new Dictionary<Vector2Int, int> { [start] = 0 };
            var q = new Queue<Vector2Int>(); q.Enqueue(start);
            while (q.Count > 0)
            {
                var c = q.Dequeue(); int dc = dist[c];
                foreach (var d in D4)
                {
                    var n = c + d;
                    if (dist.ContainsKey(n) || !Walkable(b, pieces, n)) continue;
                    dist[n] = dc + 1; q.Enqueue(n);
                }
            }
            return dist;
        }

        /// <summary>Nearest reachable cell next to target (the bot stands there to manipulate target), or null.</summary>
        public static Vector2Int? StandingCell(Dictionary<Vector2Int, int> dist, Vector2Int target, Vector2Int avoid)
        {
            Vector2Int? best = null; int bd = int.MaxValue;
            foreach (var d in D4)
            {
                var n = target + d;
                if (n == avoid) continue;
                if (dist.TryGetValue(n, out int v) && v < bd) { bd = v; best = n; }
            }
            return best;
        }

        /// <summary>Shortest walking path start -> goal (excluding start), or null.</summary>
        public static List<Vector2Int> Path(GridModel b, IReadOnlyDictionary<Vector2Int, Piece> pieces,
                                            Vector2Int start, Vector2Int goal)
        {
            if (start == goal) return new List<Vector2Int>();
            var parent = new Dictionary<Vector2Int, Vector2Int> { [start] = start };
            var q = new Queue<Vector2Int>(); q.Enqueue(start);
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                foreach (var d in D4)
                {
                    var n = c + d;
                    if (parent.ContainsKey(n) || !Walkable(b, pieces, n)) continue;
                    parent[n] = c;
                    if (n == goal)
                    {
                        var p = new List<Vector2Int>();
                        for (var x = goal; x != start; x = parent[x]) p.Add(x);
                        p.Reverse(); return p;
                    }
                    q.Enqueue(n);
                }
            }
            return null;
        }

        /// <summary>First walkable cell next to the emitter (where the bot starts).</summary>
        public static Vector2Int StartCell(GridModel b, IReadOnlyDictionary<Vector2Int, Piece> pieces, Vector2Int emitter)
        {
            foreach (var d in D4) { var n = emitter + d; if (Walkable(b, pieces, n)) return n; }
            return -Vector2Int.one;
        }
    }

    public static class BotPlanner
    {
        struct Node
        {
            public int Parent;
            public Move Move;
            public int G;
            public Vector2Int Agent;
        }

        // ── Zobrist hashing of (cell, type, optical class); deterministic table ──
        static ulong[] zobrist;
        static ulong Z(GridModel b, Vector2Int c, Piece p)
        {
            int cells = b.Width * b.Height;
            if (zobrist == null || zobrist.Length < cells * 8)
            {
                var r = new System.Random(1234567);
                zobrist = new ulong[Math.Max(cells, 1024) * 8];
                var buf = new byte[8];
                for (int i = 0; i < zobrist.Length; i++) { r.NextBytes(buf); zobrist[i] = BitConverter.ToUInt64(buf, 0); }
            }
            int cls = OpticsRules.EffectiveClass(p.Type, p.Rot);
            int k = (p.Type == TileType.Mirror ? 0 : 2) + cls;     // 0..4
            return zobrist[(c.y * b.Width + c.x) * 8 + k];
        }
        static ulong Hash(GridModel b, Dictionary<Vector2Int, Piece> pieces)
        {
            ulong h = 0; foreach (var kv in pieces) h ^= Z(b, kv.Key, kv.Value); return h;
        }

        /// <summary>
        /// Plan a sequence of legal, walkable actions that makes the beam reach the receiver.
        /// </summary>
        public static PlanResult Plan(GridModel board, Vector2Int emitter, Vector2Int emitDir, Vector2Int receiver,
                                      PuzzleState start, Vector2Int agentCell, BotConfig cfg)
        {
            var sw = Stopwatch.StartNew();
            var res = new PlanResult();
            if (BeamTracer.Trace(board, emitter, emitDir, start.Pieces).HitReceiver)
            { res.Outcome = SearchOutcome.AlreadySolved; res.TimeMs = sw.Elapsed.TotalMilliseconds; return res; }
            if (agentCell == -Vector2Int.one)
            { res.Outcome = SearchOutcome.NoAgentCell; res.TimeMs = sw.Elapsed.TotalMilliseconds; return res; }

            var nodes = new List<Node> { new Node { Parent = -1, G = 0, Agent = agentCell } };
            var seen = new HashSet<ulong> { Hash(board, start.Pieces) };
            var open = new MinHeap();
            open.Push(Priority(0, Heuristic(board, emitter, emitDir, receiver, start.Pieces, out int d0), d0, cfg), 0);

            res.Outcome = SearchOutcome.Exhausted;   // unless a limit is hit or a plan is found
            while (open.Count > 0)
            {
                if (res.Expanded >= cfg.MaxExpanded) { res.Outcome = SearchOutcome.NodeLimit; break; }
                if (sw.Elapsed.TotalMilliseconds > cfg.TimeLimitMs) { res.Outcome = SearchOutcome.TimeLimit; break; }

                int ni = open.Pop();
                var node = nodes[ni];
                res.Expanded++;
                if (node.G >= cfg.MaxDepth) continue;

                var state = Reconstruct(board, start, nodes, ni);
                foreach (var (m, agentAfter) in Successors(board, emitter, emitDir, state, node.Agent, cfg))
                {
                    var child = state.Clone();
                    if (!child.Apply(m, board)) continue;
                    res.Generated++;
                    ulong h = Hash(board, child.Pieces);
                    if (!seen.Add(h)) { res.Duplicates++; continue; }

                    int g = node.G + 1;
                    nodes.Add(new Node { Parent = ni, Move = m, G = g, Agent = agentAfter });
                    int ci = nodes.Count - 1;
                    if (g > res.MaxDepthReached) res.MaxDepthReached = g;

                    var beam = BeamTracer.Trace(board, emitter, emitDir, child.Pieces);
                    if (beam.HitReceiver)
                    {
                        res.Outcome = SearchOutcome.Found;
                        res.Plan = ExtractPlan(nodes, ci);
                        res.TimeMs = sw.Elapsed.TotalMilliseconds;
                        return res;
                    }
                    int hv = Heuristic(board, emitter, emitDir, receiver, child.Pieces, out int dist);
                    open.Push(Priority(g, hv, dist, cfg), ci);
                }
            }
            res.TimeMs = sw.Elapsed.TotalMilliseconds;
            return res;
        }

        // primary key f = g + w*h, then distance of the beam to the receiver, then insertion order (heap)
        static double Priority(int g, int h, int dist, BotConfig cfg) => g + cfg.HeuristicWeight * h + dist * 1e-3;

        /// <summary>
        /// h = 0 if the beam hits the receiver; 1 if some free cell on the beam is in line with the receiver
        /// and the line is clear (one placement could finish); otherwise 2. dist = smallest Manhattan distance
        /// between a beam cell and the receiver (tie-breaker).
        /// </summary>
        static int Heuristic(GridModel b, Vector2Int emitter, Vector2Int emitDir, Vector2Int receiver,
                             Dictionary<Vector2Int, Piece> pieces, out int dist)
        {
            var beam = BeamTracer.Trace(b, emitter, emitDir, pieces);
            dist = int.MaxValue;
            if (beam.HitReceiver) { dist = 0; return 0; }
            bool oneMove = false;
            foreach (var c in beam.Path)
            {
                int md = Mathf.Abs(c.x - receiver.x) + Mathf.Abs(c.y - receiver.y);
                if (md < dist) dist = md;
                if (!oneMove && (c.x == receiver.x || c.y == receiver.y) &&
                    (Navigation.Placeable(b, pieces, c) || pieces.ContainsKey(c)) && ClearLine(b, pieces, c, receiver))
                    oneMove = true;
            }
            if (dist == int.MaxValue) dist = b.Width + b.Height;
            return oneMove ? 1 : 2;
        }

        static bool ClearLine(GridModel b, Dictionary<Vector2Int, Piece> pieces, Vector2Int from, Vector2Int to)
        {
            var d = new Vector2Int(Math.Sign(to.x - from.x), Math.Sign(to.y - from.y));
            for (var c = from + d; c != to; c += d)
            {
                if (pieces.ContainsKey(c)) return false;
                var t = b.GetTile(c.x, c.y);
                if (t == TileType.Wall || t == TileType.Door || t == TileType.Emitter || t == TileType.Receiver) return false;
            }
            return true;
        }

        static PuzzleState Reconstruct(GridModel b, PuzzleState start, List<Node> nodes, int i)
        {
            var moves = new List<Move>();
            for (int k = i; nodes[k].Parent >= 0; k = nodes[k].Parent) moves.Add(nodes[k].Move);
            var s = start.Clone();
            for (int k = moves.Count - 1; k >= 0; k--) s.Apply(moves[k], b);
            return s;
        }

        static List<Move> ExtractPlan(List<Node> nodes, int i)
        {
            var plan = new List<Move>();
            for (int k = i; nodes[k].Parent >= 0; k = nodes[k].Parent) plan.Add(nodes[k].Move);
            plan.Reverse();
            return plan;
        }

        /// <summary>Beam-relevant legal actions (see header). Each comes with the cell where the bot ends.</summary>
        static IEnumerable<(Move, Vector2Int)> Successors(GridModel b, Vector2Int emitter, Vector2Int emitDir,
                                                         PuzzleState s, Vector2Int agent, BotConfig cfg)
        {
            var pieces = s.Pieces;
            var dist = Navigation.Distances(b, pieces, agent);
            var beam = BeamTracer.Trace(b, emitter, emitDir, pieces);
            var beamCells = new HashSet<Vector2Int>(beam.Path);

            // components the bot can reach, with the cell it stands on
            var reachable = new List<(Vector2Int cell, Piece p, Vector2Int stand, int d)>();
            foreach (var kv in pieces)
            {
                var st = Navigation.StandingCell(dist, kv.Key, -Vector2Int.one);
                if (st.HasValue) reachable.Add((kv.Key, kv.Value, st.Value, dist[st.Value]));
            }
            reachable.Sort((x, y) => x.d != y.d ? x.d.CompareTo(y.d)
                                   : (x.cell.x != y.cell.x ? x.cell.x.CompareTo(y.cell.x) : x.cell.y.CompareTo(y.cell.y)));

            // (a) rotate a component on the beam
            foreach (var r in reachable)
            {
                if (!beamCells.Contains(r.cell)) continue;
                int cls = OpticsRules.EffectiveClass(r.p.Type, r.p.Rot);
                foreach (int rot in OpticsRules.ClassRepresentatives(r.p.Type))
                    if (OpticsRules.EffectiveClass(r.p.Type, rot) != cls)
                        yield return (new Move { PieceId = r.p.Id, From = r.cell, To = r.cell, Rot = rot }, r.stand);
            }

            if (!MovementRule.AllowRelocation) yield break;

            // sources for (b): every on-beam component + nearest SourcesPerType off-beam components of each type
            var perType = new Dictionary<TileType, int>();
            foreach (var r in reachable)
            {
                bool onBeam = beamCells.Contains(r.cell);
                if (!onBeam && MovementRule.MaxCarryDistance > 0 && !CanReachBeam(b, pieces, beam.Path, r.cell)) continue;
                if (!onBeam)
                {
                    perType.TryGetValue(r.p.Type, out int n);
                    if (n >= cfg.SourcesPerType) continue;
                    perType[r.p.Type] = n + 1;
                }

                // state without the component (after pick-up)
                var without = new Dictionary<Vector2Int, Piece>(pieces);
                without.Remove(r.cell);
                var beam2 = onBeam ? BeamTracer.Trace(b, emitter, emitDir, without) : beam;
                var dist2 = Navigation.Distances(b, without, r.stand);

                // (b) place on a free cell of the beam, in a deflecting class
                foreach (var c in beam2.Path)
                {
                    if (c == r.cell || !Navigation.Placeable(b, without, c)) continue;
                    if (!MovementRule.CarryOk(r.cell, c)) continue;
                    var st2 = Navigation.StandingCell(dist2, c, c);
                    if (!st2.HasValue) continue;
                    foreach (int rot in OpticsRules.ClassRepresentatives(r.p.Type))
                    {
                        if (r.p.Type == TileType.Refractor && OpticsRules.EffectiveClass(r.p.Type, rot) == 0) continue; // pass = no effect
                        yield return (new Move { PieceId = r.p.Id, From = r.cell, To = c, Rot = rot }, st2.Value);
                    }
                }

                // (c) park an on-beam component off the beam
                if (onBeam)
                {
                    var beam2Cells = new HashSet<Vector2Int>(beam2.Path);
                    var cand = new List<(Vector2Int c, Vector2Int st, int d)>();
                    foreach (var kv in dist2)
                    {
                        var c = kv.Key;
                        if (c == r.cell || beam2Cells.Contains(c) || beamCells.Contains(c)) continue;
                        if (!Navigation.Placeable(b, without, c) || !MovementRule.CarryOk(r.cell, c)) continue;
                        var st2 = Navigation.StandingCell(dist2, c, c);
                        if (st2.HasValue) cand.Add((c, st2.Value, kv.Value));
                    }
                    cand.Sort((x, y) => x.d != y.d ? x.d.CompareTo(y.d)
                                      : (x.c.x != y.c.x ? x.c.x.CompareTo(y.c.x) : x.c.y.CompareTo(y.c.y)));
                    for (int i = 0; i < cand.Count && i < cfg.ParkingCandidates; i++)
                        yield return (new Move { PieceId = r.p.Id, From = r.cell, To = cand[i].c, Rot = r.p.Rot }, cand[i].st);
                }
            }
        }

        /// <summary>True if an off-beam component at 'from' can be carried onto a free beam cell in one action.</summary>
        static bool CanReachBeam(GridModel b, Dictionary<Vector2Int, Piece> pieces, List<Vector2Int> beamPath, Vector2Int from)
        {
            foreach (var c in beamPath)
                if (MovementRule.CarryOk(from, c) && c != from && Navigation.Placeable(b, pieces, c)) return true;
            return false;
        }

        /// <summary>Binary min-heap on (priority, insertion order) -> deterministic ties.</summary>
        sealed class MinHeap
        {
            readonly List<(double p, long o, int v)> a = new List<(double, long, int)>();
            long counter;
            public int Count => a.Count;
            static bool Less((double p, long o, int v) x, (double p, long o, int v) y) => x.p < y.p || (x.p == y.p && x.o < y.o);
            public void Push(double p, int v)
            {
                a.Add((p, counter++, v));
                int i = a.Count - 1;
                while (i > 0) { int par = (i - 1) / 2; if (!Less(a[i], a[par])) break; (a[i], a[par]) = (a[par], a[i]); i = par; }
            }
            public int Pop()
            {
                var top = a[0].v; var last = a[a.Count - 1]; a.RemoveAt(a.Count - 1);
                if (a.Count > 0)
                {
                    a[0] = last; int i = 0;
                    while (true)
                    {
                        int l = 2 * i + 1, r = l + 1, m = i;
                        if (l < a.Count && Less(a[l], a[m])) m = l;
                        if (r < a.Count && Less(a[r], a[m])) m = r;
                        if (m == i) break;
                        (a[i], a[m]) = (a[m], a[i]); i = m;
                    }
                }
                return top;
            }
        }
    }
}
