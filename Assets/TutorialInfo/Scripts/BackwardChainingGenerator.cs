using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace LightPCG.Core
{
    /// <summary>
    /// v17 — MID-M support
    ///
    /// Changes from v16:
    ///   - OrderedSolutionBends: main-chain bends in EMITTER -> RECEIVER order
    ///     (the chain is built receiver-first, so the list is reversed at the end)
    ///   - DecoyPlacements: cell and type of every decoy actually placed
    ///   - DecoyCount now counts decoys actually placed (v16 stored the requested number)
    ///   - MainChainComplete / ChainFailureReason: false when a bend cell was occupied
    ///     (v16 skipped the object but still turned the path), when a segment had
    ///     length 0, or when the emitter fell back to a cell off the path
    ///   - EmitterCell / EmitterBeamDir: where the emitter is and which way the beam
    ///     must leave it to retrace the chain
    ///   Generation rules themselves are unchanged from v16.
    ///
    /// v16 —improve Cl
    ///
    /// Changes from v15:
    ///   - add property ClNumerator 
    ///     Cl = N_totalObjects / 256 
    ///     (N_totalObjects = SolutionObjects + Decoys )
    /// </summary>
    public class BackwardChainingGenerator
    {
        private GridModel grid;
        private int W, H;
        private HashSet<Vector2Int> solutionCells = new HashSet<Vector2Int>();
        private HashSet<Vector2Int> pillarCells = new HashSet<Vector2Int>();

        // ── Existing public properties ────────────────────────────────
        public int SolutionObjectCount { get; private set; }
        public int DecoyCount { get; private set; }
        public int TotalObjectCount => SolutionObjectCount + DecoyCount;

        /// <summary>
        ///   Cl = N_totalObjects / 256
        ///  N_totalObjects = SolutionObjects + Decoys (all object on grid)
        /// BatchRunner use property instead of SolutionObjectCount
        /// Cl -> visual density of all layout
        /// </summary>
        public int ClNumerator => TotalObjectCount;   // = SolutionObjectCount + DecoyCount

        // ── MID component data (read by GridVisualizer → BatchRunner) ────────
        /// <summary>Number of Mirror objects placed on the solution path.</summary>
        public int MirrorCount { get; private set; }

        /// <summary>Number of Refractor objects placed on the solution path.</summary>
        public int RefractorCount { get; private set; }

        /// <summary>
        /// Number of times the bend type changed (Mirror→Refractor or vice-versa)
        /// along the solution path across all chains.
        /// Used to compute Ch (Rule Heterogeneity).
        /// </summary>
        public int RuleTransitions { get; private set; }

        // ── MID-M data (read by GridVisualizer -> IntendedSolution / PuzzleScrambler) ──
        /// <summary>Main-chain bends, emitter -> receiver order.</summary>
        public List<(Vector2Int cell, TileType type)> OrderedSolutionBends { get; private set; }
            = new List<(Vector2Int cell, TileType type)>();
        /// <summary>Every decoy actually placed.</summary>
        public List<(Vector2Int cell, TileType type)> DecoyPlacements { get; private set; }
            = new List<(Vector2Int cell, TileType type)>();
        /// <summary>True when every bend of the main chain received an object and the emitter sits at the end of the chain.</summary>
        public bool MainChainComplete { get; private set; }
        public string ChainFailureReason { get; private set; }
        public Vector2Int EmitterCell { get; private set; } = -Vector2Int.one;
        /// <summary>Direction the beam must leave the emitter (opposite of the build direction).</summary>
        public Vector2Int EmitterBeamDir { get; private set; }

        // build-time state of the chain currently being built
        private bool buildingMainChain;
        private readonly List<(Vector2Int cell, TileType type)> chainBends = new List<(Vector2Int, TileType)>();

        // ── Constructor ───────────────────────────────────────────────
        public BackwardChainingGenerator(GridModel g)
        { grid = g; W = g.Width; H = g.Height; }

        // ════════════════════════════════════════════════════════════
        // ENTRY POINT
        // ════════════════════════════════════════════════════════════
        public void GenerateValidPuzzle(int steps, int emitterCount = 1, int decoys = 1)
        {
            grid.ClearGrid();
            solutionCells.Clear();
            pillarCells.Clear();

            SolutionObjectCount = 0;
            DecoyCount = 0;
            MirrorCount = 0;
            RefractorCount = 0;
            RuleTransitions = 0;
            OrderedSolutionBends.Clear();
            DecoyPlacements.Clear();
            MainChainComplete = true;
            ChainFailureReason = null;
            EmitterCell = -Vector2Int.one;
            EmitterBeamDir = Vector2Int.zero;

            // 1. Outer walls
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                    if (x == 0 || x == W - 1 || y == 0 || y == H - 1)
                        grid.SetTile(x, y, TileType.Wall);

            // 2. Door + Receiver
            Vector2Int door = RandomSafeWallCell();
            grid.SetTile(door.x, door.y, TileType.Door);
            Vector2Int recv = ReceiverNearDoor(door);
            grid.SetTile(recv.x, recv.y, TileType.Receiver);
            solutionCells.Add(recv);
            solutionCells.Add(door);

            // 3. Main chain (receiver -> emitter), then expose it emitter -> receiver
            buildingMainChain = true;
            BuildChain(recv, Mathf.Max(steps, 2));
            buildingMainChain = false;
            OrderedSolutionBends = Enumerable.Reverse(chainBends).ToList();

            // 4. Extra emitters
            for (int e = 1; e < emitterCount; e++)
            {
                var s = RandomInnerEmpty();
                if (s != -Vector2Int.one)
                    BuildChain(s, Mathf.Max(2, steps - 1));
            }

            // 5. Decoys
            PlaceDecoys(decoys);
            DecoyCount = DecoyPlacements.Count;   // actual number placed (may be < requested)
            if (DecoyCount < decoys)
                Debug.LogWarning($"[PCG] only {DecoyCount}/{decoys} decoys could be placed.");

            // 6. Pillar obstacles
            PlacePillars(steps);

            Debug.Log($"[PCG] door@{door} recv@{recv} steps={steps} " +
                      $"solutionObjs={SolutionObjectCount} decoys={DecoyCount} " +
                      $"totalObjs={TotalObjectCount} (ClNumerator={ClNumerator}) " +
                      $"mirrors={MirrorCount} refractors={RefractorCount} " +
                      $"ruleTransitions={RuleTransitions}");
        }

        // ════════════════════════════════════════════════════════════
        // CHAIN BUILDER
        // ════════════════════════════════════════════════════════════
        void BuildChain(Vector2Int chainStart, int steps)
        {
            int cx = chainStart.x, cy = chainStart.y;
            Vector2Int dir = RandomInwardDir(cx, cy);

            // Track the previous bend type to count transitions (Cₕ)
            TileType previousBend = TileType.Empty; // sentinel = no previous bend yet
            if (buildingMainChain) chainBends.Clear();

            for (int i = 0; i < steps; i++)
            {
                // Walk along current direction
                int stepLen = Random.Range(3, 7);
                int walked = 0;
                for (int s = 0; s < stepLen; s++)
                {
                    int nx = cx + dir.x, ny = cy + dir.y;
                    if (nx <= 1 || nx >= W - 2 || ny <= 1 || ny >= H - 2) break;
                    cx = nx; cy = ny;
                    walked++;
                    solutionCells.Add(new Vector2Int(cx, cy));
                }
                if (walked == 0 && i < steps - 1) FailChain($"segment {i} has length 0 (inner margin)");

                if (i < steps - 1)
                {
                    // ── Bend type selection ──────────────────────────────
                    // steps < 4  → Mirror only  (beginner levels stay simple)
                    // steps >= 4 → 40% chance of Refractor  (adds Rule Heterogeneity)
                    TileType bend = TileType.Mirror;
                    if (steps >= 4 && Random.value < 0.4f)
                        bend = TileType.Refractor;

                    if (grid.GetTile(cx, cy) == TileType.Empty)
                    {
                        grid.SetTile(cx, cy, bend);
                        solutionCells.Add(new Vector2Int(cx, cy));
                        SolutionObjectCount++;

                        // Count per-type for Cᵢ
                        if (bend == TileType.Mirror) MirrorCount++;
                        else RefractorCount++;

                        // Count transition for Cₕ (only when previous bend exists)
                        if (previousBend != TileType.Empty && bend != previousBend)
                            RuleTransitions++;

                        previousBend = bend;
                        if (buildingMainChain) chainBends.Add((new Vector2Int(cx, cy), bend));
                    }
                    else
                    {
                        // v16 behaviour kept (path still turns), but the chain is now flagged
                        FailChain($"bend {i} cell {new Vector2Int(cx, cy)} already occupied by {grid.GetTile(cx, cy)}");
                    }

                    dir = Rotate90(dir);
                }
            }

            PlaceEmitterAgainstWall(cx, cy, dir);
        }

        // ════════════════════════════════════════════════════════════
        // PILLAR OBSTACLES  (unchanged from v14)
        // ════════════════════════════════════════════════════════════
        void PlacePillars(int steps)
        {
            int targetPillars = steps <= 5
                ? Mathf.Clamp(steps * 2, 3, 10)
                : Mathf.Clamp(steps, 2, 5);

            int attempts = targetPillars * 15;

            for (int a = 0; a < attempts && targetPillars > 0; a++)
            {
                int px = Random.Range(2, W - 2);
                int py = Random.Range(2, H - 2);

                if (grid.GetTile(px, py) != TileType.Empty) continue;
                if (IsOnSolutionPath(px, py, 2)) continue;
                if (IsAdjacentToPillar(px, py)) continue;
                if (Would2x2Block(px, py)) continue;
                if (WouldBlockCorridor(px, py)) continue;

                grid.SetTile(px, py, TileType.Wall);
                pillarCells.Add(new Vector2Int(px, py));
                targetPillars--;
            }
        }

        bool IsAdjacentToPillar(int x, int y)
        {
            int[] dx = { 0, 0, 1, -1 };
            int[] dy = { 1, -1, 0, 0 };
            for (int i = 0; i < 4; i++)
                if (pillarCells.Contains(new Vector2Int(x + dx[i], y + dy[i]))) return true;
            return false;
        }

        bool Would2x2Block(int x, int y)
        {
            int[,] corners = { { 0, 0 }, { 1, 0 }, { 0, 1 }, { 1, 1 } };
            for (int cx = x - 1; cx <= x; cx++)
                for (int cy = y - 1; cy <= y; cy++)
                {
                    bool allWall = true;
                    for (int i = 0; i < 4; i++)
                    {
                        int nx = cx + corners[i, 0], ny = cy + corners[i, 1];
                        if (nx == x && ny == y) continue;
                        if (nx < 0 || nx >= W || ny < 0 || ny >= H) { allWall = false; break; }
                        if (grid.GetTile(nx, ny) != TileType.Wall) { allWall = false; break; }
                    }
                    if (allWall) return true;
                }
            return false;
        }

        bool WouldBlockCorridor(int x, int y)
        {
            bool nWall = IsWallOrEdge(x, y + 1);
            bool sWall = IsWallOrEdge(x, y - 1);
            bool eWall = IsWallOrEdge(x + 1, y);
            bool wWall = IsWallOrEdge(x - 1, y);
            if (nWall && sWall) return true;
            if (eWall && wWall) return true;
            return false;
        }

        bool IsWallOrEdge(int x, int y)
        {
            if (x < 0 || x >= W || y < 0 || y >= H) return true;
            return grid.GetTile(x, y) == TileType.Wall;
        }

        bool IsOnSolutionPath(int x, int y, int radius)
        {
            for (int dx = -radius; dx <= radius; dx++)
                for (int dy = -radius; dy <= radius; dy++)
                    if (solutionCells.Contains(new Vector2Int(x + dx, y + dy))) return true;
            return false;
        }

        // ════════════════════════════════════════════════════════════
        // DECOYS
        // ════════════════════════════════════════════════════════════
        void PlaceDecoys(int count)
        {
            for (int i = 0; i < count * 20 && count > 0; i++)
            {
                int x = Random.Range(2, W - 2), y = Random.Range(2, H - 2);
                if (grid.GetTile(x, y) != TileType.Empty) continue;
                if (IsCornerZone(x, y)) continue;
                if (solutionCells.Contains(new Vector2Int(x, y))) continue;
                if (IsOnSolutionPath(x, y, 1)) continue;
                var t = (Random.value > 0.5f) ? TileType.Mirror : TileType.Refractor;
                grid.SetTile(x, y, t);
                DecoyPlacements.Add((new Vector2Int(x, y), t));
                count--;
            }
        }

        // ════════════════════════════════════════════════════════════
        // EMITTER PLACEMENT
        // ════════════════════════════════════════════════════════════
        void FailChain(string reason)
        {
            if (!buildingMainChain) return;
            if (MainChainComplete) ChainFailureReason = reason;
            MainChainComplete = false;
        }

        void SetEmitter(Vector2Int c, Vector2Int buildDir, bool onPath)
        {
            grid.SetTile(c.x, c.y, TileType.Emitter);
            solutionCells.Add(c);
            if (!buildingMainChain) return;
            EmitterCell = c;
            EmitterBeamDir = -buildDir;
            if (!onPath) FailChain("emitter placed at a fallback cell off the chain");
        }

        void PlaceEmitterAgainstWall(int cx, int cy, Vector2Int dir)
        {
            for (int s = 0; s < Mathf.Max(W, H); s++)
            {
                int nx = cx + dir.x, ny = cy + dir.y;
                bool boundary = nx <= 0 || nx >= W - 1 || ny <= 0 || ny >= H - 1 ||
                    grid.GetTile(nx, ny) == TileType.Wall ||
                    grid.GetTile(nx, ny) == TileType.Door;

                if (boundary)
                {
                    if (grid.GetTile(cx, cy) == TileType.Empty && !IsCornerZone(cx, cy))
                        SetEmitter(new Vector2Int(cx, cy), dir, true);
                    else
                    {
                        var fb = FindSafeInnerEdgeEmpty();
                        if (fb != -Vector2Int.one) SetEmitter(fb, dir, false);
                        else FailChain("no cell available for the emitter");
                    }
                    return;
                }

                if (grid.GetTile(nx, ny) == TileType.Empty)
                {
                    cx = nx; cy = ny;
                    solutionCells.Add(new Vector2Int(cx, cy));
                }
                else
                {
                    if (grid.GetTile(cx, cy) == TileType.Empty && !IsCornerZone(cx, cy))
                        SetEmitter(new Vector2Int(cx, cy), dir, true);
                    else FailChain("emitter cell blocked");
                    return;
                }
            }

            if (grid.GetTile(cx, cy) == TileType.Empty && !IsCornerZone(cx, cy))
                SetEmitter(new Vector2Int(cx, cy), dir, true);
            else FailChain("emitter cell blocked");
        }

        // ════════════════════════════════════════════════════════════
        // HELPERS
        // ════════════════════════════════════════════════════════════
        Vector2Int ReceiverNearDoor(Vector2Int door)
        {
            Vector2Int inDir = InwardDirFromWall(door);
            Vector2Int inner = door + inDir;
            Vector2Int wallDir = new Vector2Int(inDir.y, inDir.x);

            int[] offsets = { 2, 3, -2, -3, 1, -1 };
            foreach (int o in offsets)
            {
                Vector2Int c = inner + wallDir * o;
                if (!InBoundsInner(c.x, c.y) || IsCornerZone(c.x, c.y)) continue;
                TileType t = grid.GetTile(c.x, c.y);
                if ((t == TileType.Empty || t == TileType.Wall) &&
                    c.x > 0 && c.x < W - 1 && c.y > 0 && c.y < H - 1)
                    return c;
            }
            Vector2Int fb = door + inDir * 2;
            return (InBoundsInner(fb.x, fb.y) && !IsCornerZone(fb.x, fb.y))
                ? fb : door + inDir;
        }

        Vector2Int InwardDirFromWall(Vector2Int w)
        {
            if (w.x == 0) return Vector2Int.right;
            if (w.x == W - 1) return Vector2Int.left;
            if (w.y == 0) return new Vector2Int(0, 1);
            return new Vector2Int(0, -1);
        }

        Vector2Int RandomSafeWallCell()
        {
            var c = new List<Vector2Int>();
            for (int x = 3; x < W - 3; x++)
            {
                c.Add(new Vector2Int(x, 0));
                c.Add(new Vector2Int(x, H - 1));
            }
            for (int y = 3; y < H - 3; y++)
            {
                c.Add(new Vector2Int(0, y));
                c.Add(new Vector2Int(W - 1, y));
            }
            return c.Count > 0 ? c[Random.Range(0, c.Count)] : new Vector2Int(W / 2, 0);
        }

        bool IsCornerZone(int x, int y)
        {
            int m = 2;
            return (x <= m || x >= W - 1 - m) && (y <= m || y >= H - 1 - m);
        }

        bool InBoundsInner(int x, int y)
            => x > 0 && x < W - 1 && y > 0 && y < H - 1;

        Vector2Int FindSafeInnerEdgeEmpty()
        {
            for (int x = 3; x < W - 3; x++)
            {
                if (grid.GetTile(x, 1) == TileType.Empty) return new Vector2Int(x, 1);
                if (grid.GetTile(x, H - 2) == TileType.Empty) return new Vector2Int(x, H - 2);
            }
            for (int y = 3; y < H - 3; y++)
            {
                if (grid.GetTile(1, y) == TileType.Empty) return new Vector2Int(1, y);
                if (grid.GetTile(W - 2, y) == TileType.Empty) return new Vector2Int(W - 2, y);
            }
            return -Vector2Int.one;
        }

        Vector2Int RandomInwardDir(int x, int y)
        {
            var dirs = new List<Vector2Int>
            {
                Vector2Int.right, Vector2Int.left,
                new Vector2Int(0, 1), new Vector2Int(0, -1)
            };
            // Fisher-Yates shuffle
            for (int i = dirs.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                var t = dirs[i]; dirs[i] = dirs[j]; dirs[j] = t;
            }
            foreach (var d in dirs)
            {
                int nx = x + d.x * 2, ny = y + d.y * 2;
                if (nx > 1 && nx < W - 2 && ny > 1 && ny < H - 2) return d;
            }
            return dirs[0];
        }

        Vector2Int RandomInnerEmpty()
        {
            for (int a = 0; a < 100; a++)
            {
                int x = Random.Range(2, W - 2), y = Random.Range(2, H - 2);
                if (grid.GetTile(x, y) == TileType.Empty) return new Vector2Int(x, y);
            }
            return -Vector2Int.one;
        }

        Vector2Int Rotate90(Vector2Int d) => (Random.value > 0.5f)
            ? new Vector2Int(-d.y, d.x)
            : new Vector2Int(d.y, -d.x);
    }
}