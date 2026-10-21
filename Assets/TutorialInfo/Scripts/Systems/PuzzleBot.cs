using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using LightPCG.Core;

namespace LightPCG.Systems
{
    /// <summary>
    /// MID-M solver bot. Replaces AISolverAgent as the puzzle solver.
    ///
    /// 1. Observe   : reads the board, emitter, receiver and every movable component
    ///                (cell, type, orientation) from the scene. It does not read the
    ///                intended solution, the certificate or the decoy flags.
    /// 2. Plan      : BotPlanner (weighted A*, legal walkable actions, optical classes).
    ///                Runs on a worker thread so the frame rate is not affected.
    /// 3. Execute   : walks cell by cell (4-connected grid path, no teleporting) to a cell
    ///                next to the component, picks it up, walks next to the target, places
    ///                and orients it.
    /// 4. Verify    : the physical laser (LaserSystem.IsHittingReceiver). If the laser
    ///                misses after the plan, the bot observes the scene again and replans
    ///                (at most maxReplans times).
    /// 5. Exit      : waits for the door (ReceiverDetector) and walks out.
    ///
    /// The public fields keep the names that BatchRunner writes to the CSV.
    /// </summary>
    public class PuzzleBot : MonoBehaviour
    {
        [Header("References")]
        public GridVisualizer gridVisualizer;

        [Header("Planner (Chapter 3, Table 3.4)")]
        public int maxExpandedNodes = 100000;
        public int maxPlanDepth = 12;
        public float searchTimeLimitMs = 8000f;
        public float heuristicWeight = 2f;
        public int sourcesPerType = 2;
        public int parkingCandidates = 3;
        // Movement rule: not set here. The bot reads MovementRule (PuzzleState.cs), the same
        // definition used by the scramble and the mobility descriptors (study rule: carry one cell).
        public bool searchOnWorkerThread = true;
        public int maxReplans = 1;

        [Header("Motion")]
        public float moveSpeed = 6f;            // world units per second
        public float turnDuration = 0.15f;      // time to rotate a component
        public float physicsWait = 0.15f;       // wait for the laser after each action
        public float verifyTimeout = 1.0f;      // wait for the laser after the plan
        public float doorTimeout = 1.5f;        // wait for ReceiverDetector to open the door
        public bool exitThroughDoor = true;

        // ── results read by BatchRunner (same names as AISolverAgent) ──
        [HideInInspector] public bool WasSolved, SearchTimedOut, PlanFailedPhysically, LaserHit, DoorOpened;
        [HideInInspector] public int SolveIterations;          // expanded nodes, all searches (= SearchNodes)
        [HideInInspector] public float SolveTimeMs, SearchTimeMs, ExecutionTimeMs;
        [HideInInspector] public int TotalPlacements, InPlaceRotations, Relocations;
        [HideInInspector] public string SearchPhase = "None";  // outcome of the first search
        [HideInInspector] public string FinalPhase = "None";
        [HideInInspector] public int WalkTeleports;            // always 0: the bot never teleports while solving
        [HideInInspector] public int SweepIterations, SweepRelocations;   // always 0: no sweep fallback
        // ── extra bot results ──
        [HideInInspector] public int SearchGenerated, PlanLength, WalkCells, Replans, MaxDepthReached;
        [HideInInspector] public string FailReason = "";

        public System.Action<bool> OnSolveComplete;

        GridModel grid;
        bool running;
        Vector2Int agentCell;
        float tStart, tExec;
        CharacterController cc;
        bool lastWalkOk;
        bool countWalk = true;

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            int agentLayer = LayerMask.NameToLayer("Agent");
            if (agentLayer >= 0) SetLayerRecursive(transform,agentLayer);
            else Debug.LogWarning("[Bot] Layer 'Agent' not found — the bot's collider can block the laser.");
        }

        static void SetLayerRecursive(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform child in t) SetLayerRecursive(child, layer);
        }

        void Start()
        {
            if (gridVisualizer == null) gridVisualizer = FindFirstObjectByType<GridVisualizer>();
        }

        // ════════════════════════════════════════════════════════════
        // ENTRY POINTS
        // ════════════════════════════════════════════════════════════
        public void StartSolve() { ResetRun(); StartCoroutine(SolvePipeline()); }

        /// <summary>Pilot: execute the generator's certificate physically, without search.</summary>
        public void StartReplay(List<Move> certificate)
        {
            ResetRun();
            SearchPhase = "Replay";
            StartCoroutine(ReplayPipeline(new List<Move>(certificate)));
        }

        void ResetRun()
        {
            StopAllCoroutines();
            if (gridVisualizer == null) gridVisualizer = FindFirstObjectByType<GridVisualizer>();
            WasSolved = SearchTimedOut = PlanFailedPhysically = LaserHit = DoorOpened = false;
            SolveIterations = TotalPlacements = InPlaceRotations = Relocations = 0;
            WalkTeleports = SweepIterations = SweepRelocations = 0;
            SearchGenerated = PlanLength = WalkCells = Replans = MaxDepthReached = 0;
            SolveTimeMs = SearchTimeMs = ExecutionTimeMs = 0f;
            SearchPhase = FinalPhase = "None"; FailReason = "";
            running = true;
            countWalk = true;
        }

        BotConfig Config() => new BotConfig
        {
            MaxExpanded = maxExpandedNodes, MaxDepth = maxPlanDepth, TimeLimitMs = searchTimeLimitMs,
            HeuristicWeight = heuristicWeight, SourcesPerType = sourcesPerType, ParkingCandidates = parkingCandidates
        };

        // ════════════════════════════════════════════════════════════
        // PIPELINES
        // ════════════════════════════════════════════════════════════
        IEnumerator SolvePipeline()
        {
            yield return new WaitForEndOfFrame();
            yield return new WaitForSeconds(0.3f);      // let the level and laser settle
            grid = gridVisualizer.LevelGrid;
            tStart = Time.realtimeSinceStartup;

            var em = gridVisualizer.LastEmitter; var ed = gridVisualizer.LastEmitDir; var rc = gridVisualizer.LastReceiver;
            var state = Observe();
            agentCell = Navigation.StartCell(grid, state.Pieces, em);
            if (agentCell == -Vector2Int.one) { FailReason = "no free cell next to the emitter"; Finish(false); yield break; }
            PlaceBodyAt(agentCell);                      // spawn next to the emitter (not counted as a teleport)
            yield return new WaitForSeconds(physicsWait);

            if (LaserOnReceiver()) { SearchPhase = "AlreadySolved"; tExec = Time.realtimeSinceStartup; yield return Conclude(); yield break; }

            for (int attempt = 0; attempt <= maxReplans; attempt++)
            {
                // ── plan ──
                PlanResult pr = null;
                var cfg = Config();
                var snapshot = state;
                var start = agentCell;
                float s0 = Time.realtimeSinceStartup;
                if (searchOnWorkerThread)
                {
                    var task = Task.Run(() => BotPlanner.Plan(grid, em, ed, rc, snapshot, start, cfg));
                    while (!task.IsCompleted) yield return null;
                    if (task.IsFaulted) { FailReason = "planner exception: " + task.Exception?.GetBaseException().Message; Finish(false); yield break; }
                    pr = task.Result;
                }
                else pr = BotPlanner.Plan(grid, em, ed, rc, snapshot, start, cfg);
                SearchTimeMs += (Time.realtimeSinceStartup - s0) * 1000f;

                SolveIterations += pr.Expanded;
                SearchGenerated += pr.Generated;
                MaxDepthReached = Mathf.Max(MaxDepthReached, pr.MaxDepthReached);
                if (attempt == 0) SearchPhase = pr.Outcome.ToString();
                if (pr.Outcome == SearchOutcome.TimeLimit) SearchTimedOut = true;
                if (!pr.Found) { FailReason = "search: " + pr.Outcome; break; }

                // ── execute ──
                PlanLength += pr.Plan.Count;
                if (attempt == 0) tExec = Time.realtimeSinceStartup;
                bool executed = true;
                foreach (var m in pr.Plan)
                {
                    bool ok = false;
                    yield return ExecuteMove(m, r => ok = r);
                    if (!ok) { executed = false; break; }
                    if (LaserOnReceiver()) break;        // finished early
                }

                yield return WaitForLaser();
                if (LaserOnReceiver()) { yield return Conclude(); yield break; }

                // ── replan from what the bot sees now ──
                PlanFailedPhysically = true;
                LogMismatch(em, ed);
                DumpPhysical();
                if (executed) FailReason = "laser missed after the plan";
                state = Observe();
                if (attempt < maxReplans) Replans++;
            }
            if (tExec <= 0f) tExec = Time.realtimeSinceStartup;
            Finish(false);
        }
        void LogMismatch(Vector2Int em, Vector2Int ed)
        {
            var logical = BeamTracer.Trace(grid, em, ed, Observe().Pieces);
            var sb = new System.Text.StringBuilder();
            sb.Append($"[Bot][MISMATCH] logicalHit={logical.HitReceiver} agentCell={agentCell} path=");
            foreach (var c in logical.Path) sb.Append(c).Append(' ');
            foreach (var l in FindObjectsByType<LaserSystem>(FindObjectsSortMode.None))
                sb.Append($"\n  laserHit={l.IsHittingReceiver} lastTag={l.LastHitTag} obj={l.LastHitName} layer={l.LastHitLayer} at={l.LastHitPoint}");
            Debug.Log(sb.ToString());
        }

        void DumpPhysical()
        {
            var sb = new System.Text.StringBuilder("[Bot][PHYS]");
            foreach (var l in FindObjectsByType<LaserSystem>(FindObjectsSortMode.None))
            {
                sb.Append("\n beam: ");
                foreach (var p in l.Points) sb.Append($"({p.x:0.0},{p.z:0.0}) ");
            }
            for (int x = 0; x < grid.Width; x++)
                for (int y = 0; y < grid.Height; y++)
                {
                    var t = grid.GetTile(x, y);
                    if (!OpticsRules.IsMovable(t)) continue;
                    var c = new Vector2Int(x, y);
                    gridVisualizer.SpawnedObjects.TryGetValue(c, out var go);
                    string dict = go == null ? "null"
                        : $"{go.name} pos=({go.transform.position.x:0.0},{go.transform.position.z:0.0}) yaw={go.transform.eulerAngles.y:0} cls={OpticsRules.EffectiveClass(t, OpticsRules.Snap(t, go.transform.eulerAngles.y))}";
                    Vector3 w = gridVisualizer.GridToWorld(x, y); w.y = 0.5f;
                    var cols = Physics.OverlapSphere(w, 0.1f, ~0, QueryTriggerInteraction.Ignore);
                    var phys = new System.Text.StringBuilder();
                    foreach (var col in cols)
                        phys.Append($"[{col.name} tag={col.tag} n={col.GetComponents<Collider>().Length} yaw={col.transform.eulerAngles.y:0}] ");
                    sb.Append($"\n cell({x},{y}) {t} dict={dict} | phys={phys}");
                }
            Debug.Log(sb.ToString());
        }
        IEnumerator ReplayPipeline(List<Move> cert)
        {
            yield return new WaitForEndOfFrame();
            yield return new WaitForSeconds(0.3f);
            grid = gridVisualizer.LevelGrid;
            tStart = tExec = Time.realtimeSinceStartup;
            var state = Observe();
            agentCell = Navigation.StartCell(grid, state.Pieces, gridVisualizer.LastEmitter);
            if (agentCell == -Vector2Int.one) { FailReason = "no free cell next to the emitter"; Finish(false); yield break; }
            PlaceBodyAt(agentCell);
            yield return new WaitForSeconds(physicsWait);

            PlanLength = cert.Count;
            foreach (var m in cert)
            {
                bool ok = false;
                yield return ExecuteMove(m, r => ok = r);
                if (!ok) { Finish(false); yield break; }
            }
            yield return WaitForLaser();
            if (LaserOnReceiver()) { yield return Conclude(); yield break; }
            PlanFailedPhysically = true; FailReason = "certificate replay: laser missed";
            Finish(false);
        }

        IEnumerator Conclude()
        {
            ExecutionTimeMs = (Time.realtimeSinceStartup - tExec) * 1000f;
            // DoorOpened: ReceiverDetector needs a sustained hit before it opens the door
            var rd = FindFirstObjectByType<ReceiverDetector>();
            float t = 0f;
            while (rd != null && !rd.IsDoorOpen && t < doorTimeout) { t += Time.deltaTime; yield return null; }
            Finish(true);
            if (exitThroughDoor && DoorOpened) yield return ExitDoor();
        }

        void Finish(bool solved)
        {
            if (!running) return;
            running = false;
            WasSolved = solved;
            LaserHit = LaserOnReceiver();
            var rd = FindFirstObjectByType<ReceiverDetector>();
            DoorOpened = rd != null && rd.IsDoorOpen;
            SolveTimeMs = (Time.realtimeSinceStartup - tStart) * 1000f;
            if (ExecutionTimeMs <= 0f && tExec > 0f) ExecutionTimeMs = (Time.realtimeSinceStartup - tExec) * 1000f;
            FinalPhase = solved ? (SearchPhase == "Replay" ? "Replay" : (Replans > 0 ? "Solved-Replanned" : "Solved"))
                                : "Failed-" + (string.IsNullOrEmpty(FailReason) ? SearchPhase : FailReason.Split(':')[0]);
            Debug.Log($"[Bot] {(solved ? "SOLVED" : "FAILED")} phase={FinalPhase} nodes={SolveIterations} " +
                      $"actions={TotalPlacements} walk={WalkCells} {FailReason}");
            OnSolveComplete?.Invoke(solved);
        }

        // ════════════════════════════════════════════════════════════
        // OBSERVATION
        // ════════════════════════════════════════════════════════════
        /// <summary>The bot's view of the puzzle: every movable component with its snapped orientation.</summary>
        PuzzleState Observe()
        {
            var s = new PuzzleState(); int id = 0;
            for (int x = 0; x < grid.Width; x++)
                for (int y = 0; y < grid.Height; y++)
                {
                    var t = grid.GetTile(x, y);
                    if (!OpticsRules.IsMovable(t)) continue;
                    var c = new Vector2Int(x, y);
                    float yaw = 0f;
                    if (gridVisualizer.SpawnedObjects.TryGetValue(c, out var go) && go != null) yaw = go.transform.eulerAngles.y;
                    s.Pieces[c] = new Piece { Id = id++, Type = t, Rot = OpticsRules.Snap(t, yaw), IsDecoy = false };
                }
            return s;
        }

        // ════════════════════════════════════════════════════════════
        // EXECUTION
        // ════════════════════════════════════════════════════════════
        IEnumerator ExecuteMove(Move m, System.Action<bool> done)
        {
            var pieces = Observe().Pieces;
            if (!pieces.TryGetValue(m.From, out var piece)) { FailReason = $"no component at {m.From}"; done(false); yield break; }

            // walk next to the component
            var dist = Navigation.Distances(grid, pieces, agentCell);
            var stand = Navigation.StandingCell(dist, m.From, -Vector2Int.one);
            if (!stand.HasValue) { FailReason = $"cannot walk to the component at {m.From}"; done(false); yield break; }
            yield return WalkTo(stand.Value, pieces);
            if (!lastWalkOk) { done(false); yield break; }
            yield return Face(m.From);

            TotalPlacements++;
            if (m.From == m.To)
            {
                InPlaceRotations++;
                if (gridVisualizer.SpawnedObjects.TryGetValue(m.From, out var goR) && goR != null)
                    yield return TurnObject(goR, m.Rot);
            }
            else
            {
                Relocations++;
                GameObject held = PickUp(m.From);
                var without = new Dictionary<Vector2Int, Piece>(pieces); without.Remove(m.From);
                var dist2 = Navigation.Distances(grid, without, agentCell);
                var stand2 = Navigation.StandingCell(dist2, m.To, m.To);
                if (!stand2.HasValue)
                {
                    Place(m.From, piece.Type, held, piece.Rot);       // put it back
                    FailReason = $"cannot walk next to the target {m.To}"; done(false); yield break;
                }
                yield return WalkTo(stand2.Value, without);
                if (!lastWalkOk)
                {
                    Place(m.From, piece.Type, held, piece.Rot);
                    done(false); yield break;
                }
                yield return Face(m.To);
                Place(m.To, piece.Type, held, m.Rot);
            }
            yield return new WaitForSeconds(physicsWait);
            done(true);
        }

        /// <summary>Walks along the shortest 4-connected path over free cells. Never teleports.</summary>
        IEnumerator WalkTo(Vector2Int goal, IReadOnlyDictionary<Vector2Int, Piece> pieces)
        {
            var path = Navigation.Path(grid, pieces, agentCell, goal);
            lastWalkOk = path != null;
            if (path == null) { FailReason = $"no walking path to {goal}"; yield break; }
            if (cc != null) cc.enabled = false;           // kinematic grid motion; the planner already avoids obstacles
            foreach (var step in path)
            {
                Vector3 dest = gridVisualizer.GridToWorld(step.x, step.y); dest.y = transform.position.y;
                Vector3 dir = dest - transform.position; dir.y = 0f;
                if (dir.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(dir);
                while ((transform.position - dest).sqrMagnitude > 1e-4f)
                {
                    transform.position = Vector3.MoveTowards(transform.position, dest, moveSpeed * Time.deltaTime);
                    yield return null;
                }
                agentCell = step;
                if (countWalk) WalkCells++;
            }
            if (cc != null) cc.enabled = true;
        }

        IEnumerator Face(Vector2Int cell)
        {
            Vector3 d = gridVisualizer.GridToWorld(cell.x, cell.y) - transform.position; d.y = 0f;
            if (d.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(d);
            yield return null;
        }

        IEnumerator TurnObject(GameObject go, int rot)
        {
            Quaternion a = go.transform.rotation, b = Quaternion.Euler(0f, rot, 0f);
            for (float t = 0f; t < turnDuration; t += Time.deltaTime)
            {
                go.transform.rotation = Quaternion.Slerp(a, b, t / turnDuration);
                yield return null;
            }
            go.transform.rotation = b;                    // exact final orientation
        }

        GameObject PickUp(Vector2Int cell)
        {
            gridVisualizer.SpawnedObjects.TryGetValue(cell, out var go);
            grid.SetTile(cell.x, cell.y, TileType.Empty);
            gridVisualizer.SpawnedObjects.Remove(cell);
            if (go != null) go.SetActive(false);          // carried: not visible to the laser
            return go;
        }

        void Place(Vector2Int cell, TileType type, GameObject go, int rot)
        {
            grid.SetTile(cell.x, cell.y, type);
            gridVisualizer.SpawnedObjects[cell] = go;
            if (go == null) return;
            go.transform.position = gridVisualizer.GridToWorld(cell.x, cell.y);
            go.transform.rotation = Quaternion.Euler(0f, rot, 0f);
            go.SetActive(true);
        }

        void PlaceBodyAt(Vector2Int cell)
        {
            if (cc != null) cc.enabled = false;
            Vector3 p = gridVisualizer.GridToWorld(cell.x, cell.y); p.y = 0.5f;
            transform.position = p;
            if (cc != null) cc.enabled = true;
            agentCell = cell;
        }

        // ════════════════════════════════════════════════════════════
        // LASER / DOOR
        // ════════════════════════════════════════════════════════════
        bool LaserOnReceiver()
        {
            var lasers = FindObjectsByType<LaserSystem>(FindObjectsSortMode.None);
            if (lasers.Length == 0) return false;
            foreach (var l in lasers) if (!l.IsHittingReceiver) return false;
            return true;
        }

        IEnumerator WaitForLaser()
        {
            float t = 0f;
            while (!LaserOnReceiver() && t < verifyTimeout) { t += Time.deltaTime; yield return null; }
        }

        IEnumerator ExitDoor()
        {
            Vector2Int door = FindTile(TileType.Door);
            if (door == -Vector2Int.one) yield break;
            countWalk = false;
            // the door is open: treat its cell as walkable for the exit only
            grid.SetTile(door.x, door.y, TileType.Empty);
            var pieces = Observe().Pieces;
            yield return WalkTo(door, pieces);
            
            if (!lastWalkOk)
            {
                // the cell in front of the door is blocked (e.g. by the emitter):
                // do not cross walls. FailReason was already set by WalkTo.
                grid.SetTile(door.x, door.y, TileType.Door);
                countWalk = true;
                yield break;
            }
            
            Vector2Int outward = door.x == 0 ? Vector2Int.left : door.x == grid.Width - 1 ? Vector2Int.right
                               : door.y == 0 ? Vector2Int.down : Vector2Int.up;
            Vector3 beyond = gridVisualizer.GridToWorld(door.x + outward.x, door.y + outward.y); beyond.y = transform.position.y;
            while ((transform.position - beyond).sqrMagnitude > 1e-3f)
            {
                transform.position = Vector3.MoveTowards(transform.position, beyond, moveSpeed * Time.deltaTime);
                yield return null;
            }
            grid.SetTile(door.x, door.y, TileType.Door);
            countWalk = true;
        }

        Vector2Int FindTile(TileType t)
        {
            for (int x = 0; x < grid.Width; x++)
                for (int y = 0; y < grid.Height; y++)
                    if (grid.GetTile(x, y) == t) return new Vector2Int(x, y);
            return -Vector2Int.one;
        }
    }
}
