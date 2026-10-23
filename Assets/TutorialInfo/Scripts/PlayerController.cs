using System.Collections;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif
using LightPCG.Core;
using LightPCG.Systems;

namespace LightPCG.Play
{
    /// <summary>
    /// Human-playable mode for the demo scene ONLY (PlayDemo). It does not touch the experiment code:
    /// it reuses the same grid, the same Carry1 rule (MovementRule) and the same pick-up/place logic as PuzzleBot.
    ///
    /// Controls:  WASD / arrows = move (and face)   E = pick up / put down the piece in front of you
    ///            Not carrying: hold a direction to run. Carrying: the first press turns (moves the ghost), the next press in that direction takes one slower step.
    ///            R = rotate the piece in front (or the carried piece)   Q = cancel carrying (put back)
    ///            F = toggle free-carry sandbox (NOT the rule used in the experiments)
    /// Carry1:    a carried piece can only be put down on a cell next to the cell it was picked up from.
    /// Ghost:     while carrying, a translucent copy shows where E will place the piece (green = allowed, red = not allowed).
    /// Goal:      make the beam reach the receiver (door opens), then walk into the door cell.
    /// </summary>
    public class PlayerController : MonoBehaviour
    {
        public GridVisualizer gridVisualizer;
        [Header("Movement")]
        [Tooltip("Not carrying: hold a direction key to run continuously, cell by cell.")]
        public float runSpeed = 9f;
        [Tooltip("Carrying: one cell per key press, slower, so you can choose where to put the piece down.")]
        public float carrySpeed = 4f;
        [Tooltip("Log every step (cell and world position) to the Console. For checking the movement, then switch off.")]
        public bool debugMoves = false;
        public float turnDuration = 0.15f;

        // ---- state read by the UI ----
        public bool Won { get; private set; }
        public bool Carrying => held != null;
        public string Message { get; private set; } = "";
        public int Moves { get; private set; }        // pick-and-place actions + in-place rotations (counted like the bot's PlanLength)
        public int Rotations { get; private set; }
        public int Carries { get; private set; }
        public int WalkSteps { get; private set; }
        public float ElapsedSeconds { get; private set; }
        public bool InputEnabled = true;               // the UI switches this off while the bot is solving
        public string Hint { get; private set; } = ""; // why the ghost is red (shown by the UI)

        [Header("Ghost preview")]
        [Tooltip("Optional transparent material (URP Unlit, Surface Type = Transparent). If empty, Sprites/Default is tried.")]
        public Material ghostMaterial;
        public Color ghostOk = new Color(0.25f, 1f, 0.35f, 0.45f);
        public Color ghostBad = new Color(1f, 0.25f, 0.2f, 0.45f);

        [Header("Rules")]
        [Tooltip("Sandbox only. false = Carry1, the rule used in all experiments. The bot is never affected by this switch.")]
        public bool FreeCarry = false;

        GridModel grid;
        Vector2Int cell, facing = Vector2Int.up, doorCell = -Vector2Int.one, heldFrom;
        GameObject held, ghost;
        Material ghostMat;
        TileType heldType;
        int heldRot;
        bool busy, ready, started;
        float t0;
        Vector2Int queued;           // one buffered direction press (carry mode), so a press during a step is not lost
        CharacterController cc;

        void Awake() { cc = GetComponent<CharacterController>(); }

        IEnumerator Start()
        {
            if (gridVisualizer == null) gridVisualizer = FindFirstObjectByType<GridVisualizer>();
            yield return null;                       // let GridVisualizer.Start() generate the first level
            BeginLevel();
        }

        /// <summary>Call this after every GenerateLevel().</summary>
        public void BeginLevel()
        {
            StopAllCoroutines();
            busy = false;
            DestroyGhost();
            FreeCarry = false;                       // every new level starts under the studied rule (Carry1)
            held = null; Won = false; Message = ""; Hint = "";
            queued = Vector2Int.zero; started = false; ElapsedSeconds = 0f;
            Moves = Rotations = Carries = WalkSteps = 0;
            grid = gridVisualizer != null ? gridVisualizer.LevelGrid : null;
            ready = grid != null && gridVisualizer.LastLevelValid;
            if (!ready) { Message = "No valid level."; return; }

            doorCell = FindTile(TileType.Door);
            Vector2Int e = gridVisualizer.LastEmitter;
            Vector2Int[] dirs = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
            bool placed = false;
            foreach (var d in dirs)
            {
                var c = e + d;
                if (grid.GetTile(c.x, c.y) == TileType.Empty) { SetBodyAt(c); placed = true; break; }
            }
            if (!placed) { ready = false; Message = "No free start cell."; return; }
            t0 = Time.time;
        }

        void Update()
        {
            if (!ready || Won) return;
            if (started) ElapsedSeconds = Time.time - t0;      // the clock starts at your first action
            if (held != null) UpdateGhost();
            if (!InputEnabled) return;

            // Carrying: remember the latest key press even while a step is still in progress.
            if (held != null) { var q = ReadDirection(false); if (q != Vector2Int.zero) queued = q; }
            if (busy) return;

            Vector2Int dir = queued != Vector2Int.zero ? queued : ReadDirection(held == null);   // free: hold to run
            queued = Vector2Int.zero;
            if (dir != Vector2Int.zero)
            {
                MarkStarted();
                if (held != null && dir != facing) TurnOnly(dir);   // carrying: first press only aims the ghost, the next press in the same direction steps
                else StartCoroutine(TryMove(dir));
                return;
            }
            if (PlayKeys.Down(KeyCode.E)) { MarkStarted(); Interact(); }
            else if (PlayKeys.Down(KeyCode.R)) { MarkStarted(); RotateFront(); }
            else if (PlayKeys.Down(KeyCode.Q)) CancelCarry();
            else if (PlayKeys.Down(KeyCode.F)) ToggleFreeCarry();
        }

        void MarkStarted() { if (!started) { started = true; t0 = Time.time; } }

        void TurnOnly(Vector2Int dir)
        {
            facing = dir;
            transform.rotation = Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.y));
            Message = "";
        }

        void ToggleFreeCarry()
        {
            if (held != null) { Message = "Put the piece down first."; return; }
            FreeCarry = !FreeCarry;
            Message = FreeCarry ? "Sandbox: free carry (not the rule used in the experiments)."
                                : "Carry1: one cell from where you picked the piece up.";
        }

        // ------------------------------------------------------------ movement
        IEnumerator TryMove(Vector2Int dir)
        {
            busy = true;
            facing = dir;
            transform.rotation = Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.y));
            Vector2Int next = cell + dir;
            if (CanStep(next))
            {
                if (cc != null) cc.enabled = false;
                Vector3 dest = gridVisualizer.GridToWorld(next.x, next.y); dest.y = transform.position.y;
                float speed = held == null ? runSpeed : carrySpeed;
                while ((transform.position - dest).sqrMagnitude > 1e-4f)
                {
                    transform.position = Vector3.MoveTowards(transform.position, dest, speed * Time.deltaTime);
                    yield return null;
                }
                if (debugMoves) Debug.Log($"[Player] dir={dir} cell {cell}->{next} world={transform.position}");
                if (cc != null) cc.enabled = true;
                cell = next; WalkSteps++;
                Message = "";
                if (cell == doorCell && DoorIsOpen()) { Won = true; Message = "Level complete!"; }
            }
            else Message = "Blocked.";
            busy = false;
        }

        bool CanStep(Vector2Int c)
        {
            if (c == doorCell) return DoorIsOpen();
            return grid.IsWalkable(c.x, c.y) && grid.GetTile(c.x, c.y) == TileType.Empty;
        }

        // The door object is destroyed by ReceiverDetector when it opens (tag "Door").
        static bool DoorIsOpen() => GameObject.FindGameObjectsWithTag("Door").Length == 0;

        // ------------------------------------------------------------ actions
        void Interact()
        {
            Vector2Int target = cell + facing;
            if (held == null)
            {
                TileType t = grid.GetTile(target.x, target.y);
                if (!OpticsRules.IsMovable(t)) { Message = "Nothing to pick up there."; return; }
                if (!MovementRule.AllowRelocation) { Message = "Pieces cannot be carried in this mode."; return; }
                gridVisualizer.SpawnedObjects.TryGetValue(target, out var go);
                heldType = t; heldFrom = target; held = go;
                heldRot = go != null ? OpticsRules.Snap(t, go.transform.eulerAngles.y) : OpticsRules.AllowedRotations(t)[0];
                grid.SetTile(target.x, target.y, TileType.Empty);
                gridVisualizer.SpawnedObjects.Remove(target);
                if (go != null) go.SetActive(false);                     // carried: invisible to the laser
                CreateGhost();
                Message = FreeCarry ? "Carrying. The ghost shows where E will place it."
                                    : "Carrying (Carry1). The ghost shows where E will place it; green = allowed.";
            }
            else
            {
                if (!CanPlace(target, out string why)) { Message = why; return; }
                bool moved = target != heldFrom;
                PlaceHeld(target);
                if (moved) { Carries++; Moves++; Message = "Placed."; }
                else Message = "Put back.";
            }
        }

        void PlaceHeld(Vector2Int target)
        {
            grid.SetTile(target.x, target.y, heldType);
            gridVisualizer.SpawnedObjects[target] = held;
            if (held != null)
            {
                held.transform.position = gridVisualizer.GridToWorld(target.x, target.y);
                held.transform.rotation = Quaternion.Euler(0f, heldRot, 0f);
                held.SetActive(true);
            }
            held = null;
            queued = Vector2Int.zero;
            DestroyGhost();
            Hint = "";
        }

        void CancelCarry()
        {
            if (held == null) return;
            // Put it back where it came from, with the orientation it had. (Orientation chosen while carrying is kept.)
            PlaceHeld(heldFrom);
            Message = "Put back.";
        }

        void RotateFront()
        {
            if (held != null)
            {
                heldRot = NextRotation(heldType, heldRot);
                Message = "Carried piece rotated. Press E to place it.";
                return;
            }
            Vector2Int target = cell + facing;
            TileType t = grid.GetTile(target.x, target.y);
            if (!OpticsRules.IsMovable(t)) { Message = "Nothing to rotate there."; return; }
            if (!gridVisualizer.SpawnedObjects.TryGetValue(target, out var go) || go == null) return;
            int cur = OpticsRules.Snap(t, go.transform.eulerAngles.y);
            StartCoroutine(TurnObject(go, NextRotation(t, cur)));
            Rotations++; Moves++;
            Message = "";
        }

        static int NextRotation(TileType t, int cur)
        {
            int[] a = OpticsRules.AllowedRotations(t);
            int i = System.Array.IndexOf(a, cur);
            return a[(i + 1 + a.Length) % a.Length];
        }

        IEnumerator TurnObject(GameObject go, int rot)
        {
            busy = true;
            Quaternion a = go.transform.rotation, b = Quaternion.Euler(0f, rot, 0f);
            for (float t = 0f; t < turnDuration; t += Time.deltaTime)
            {
                go.transform.rotation = Quaternion.Slerp(a, b, t / turnDuration);
                yield return null;
            }
            go.transform.rotation = b;
            busy = false;
        }

        // ------------------------------------------------------------ ghost preview
        bool CanPlace(Vector2Int target, out string why)
        {
            why = "";
            if (grid.GetTile(target.x, target.y) != TileType.Empty || target == doorCell)
            { why = "That cell is not free."; return false; }
            if (!FreeCarry && !MovementRule.CarryOk(heldFrom, target))
            { why = "Carry1: the piece can only move one cell from where you picked it up."; return false; }
            return true;
        }

        void CreateGhost()
        {
            DestroyGhost();
            if (held == null) return;
            ghost = Instantiate(held, gridVisualizer.transform);   // child of the grid, so a new level clears it; prefab = Transform + MeshFilter + MeshRenderer + BoxCollider, no scripts
            ghost.name = "GhostPreview";
            foreach (var col in ghost.GetComponentsInChildren<Collider>(true)) Destroy(col);   // the laser must never see the ghost
            ghost.tag = "Untagged";
            SetLayerRecursive(ghost.transform, 2);            // Ignore Raycast
            ghost.SetActive(true);

            Material baseMat = ghostMaterial;
            if (baseMat == null)
            {
                var sh = Shader.Find("Sprites/Default");
                if (sh != null) baseMat = new Material(sh);
            }
            foreach (var r in ghost.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                ghostMat = baseMat != null ? new Material(baseMat) : new Material(r.sharedMaterial);
                r.material = ghostMat;
            }
        }

        void UpdateGhost()
        {
            if (ghost == null) return;
            Vector2Int target = cell + facing;
            bool ok = CanPlace(target, out string why);
            Hint = ok ? "" : why;
            Vector3 p = gridVisualizer.GridToWorld(target.x, target.y);
            ghost.transform.position = p;
            ghost.transform.rotation = Quaternion.Euler(0f, heldRot, 0f);
            Color c = ok ? ghostOk : ghostBad;
            if (ghostMat != null)
            {
                if (ghostMat.HasProperty("_BaseColor")) ghostMat.SetColor("_BaseColor", c);
                if (ghostMat.HasProperty("_Color")) ghostMat.SetColor("_Color", c);
            }
        }

        void DestroyGhost()
        {
            if (ghost != null) Destroy(ghost);
            ghost = null; ghostMat = null;
        }

        void OnDisable() { DestroyGhost(); }

        static void SetLayerRecursive(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform child in t) SetLayerRecursive(child, layer);
        }

        // ------------------------------------------------------------ helpers
        public bool LaserOnReceiver()
        {
            var lasers = FindObjectsByType<LaserSystem>(FindObjectsSortMode.None);
            if (lasers.Length == 0) return false;
            foreach (var l in lasers) if (!l.IsHittingReceiver) return false;
            return true;
        }

        void SetBodyAt(Vector2Int c)
        {
            if (cc != null) cc.enabled = false;
            Vector3 p = gridVisualizer.GridToWorld(c.x, c.y); p.y = 0.5f;
            transform.position = p;
            if (cc != null) cc.enabled = true;
            cell = c;
        }

        Vector2Int FindTile(TileType t)
        {
            for (int x = 0; x < grid.Width; x++)
                for (int y = 0; y < grid.Height; y++)
                    if (grid.GetTile(x, y) == t) return new Vector2Int(x, y);
            return -Vector2Int.one;
        }

        static Vector2Int ReadDirection(bool hold)
        {
            System.Func<KeyCode, bool> k = hold ? (System.Func<KeyCode, bool>)PlayKeys.Held : PlayKeys.Down;
            if (k(KeyCode.W) || k(KeyCode.UpArrow)) return Vector2Int.up;
            if (k(KeyCode.S) || k(KeyCode.DownArrow)) return Vector2Int.down;
            if (k(KeyCode.A) || k(KeyCode.LeftArrow)) return Vector2Int.left;
            if (k(KeyCode.D) || k(KeyCode.RightArrow)) return Vector2Int.right;
            return Vector2Int.zero;
        }
    }

    /// <summary>Key presses that work with the old Input Manager, the new Input System, or both.</summary>
    static class PlayKeys
    {
        public static bool Held(KeyCode k)
        {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            var kb = Keyboard.current;
            if (kb == null) return false;
            switch (k)
            {
                case KeyCode.W: return kb.wKey.isPressed;
                case KeyCode.A: return kb.aKey.isPressed;
                case KeyCode.S: return kb.sKey.isPressed;
                case KeyCode.D: return kb.dKey.isPressed;
                case KeyCode.UpArrow: return kb.upArrowKey.isPressed;
                case KeyCode.DownArrow: return kb.downArrowKey.isPressed;
                case KeyCode.LeftArrow: return kb.leftArrowKey.isPressed;
                case KeyCode.RightArrow: return kb.rightArrowKey.isPressed;
                default: return false;
            }
#else
            return Input.GetKey(k);
#endif
        }

        public static bool Down(KeyCode k)
        {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            var kb = Keyboard.current;
            if (kb == null) return false;
            switch (k)
            {
                case KeyCode.W: return kb.wKey.wasPressedThisFrame;
                case KeyCode.A: return kb.aKey.wasPressedThisFrame;
                case KeyCode.S: return kb.sKey.wasPressedThisFrame;
                case KeyCode.D: return kb.dKey.wasPressedThisFrame;
                case KeyCode.E: return kb.eKey.wasPressedThisFrame;
                case KeyCode.R: return kb.rKey.wasPressedThisFrame;
                case KeyCode.Q: return kb.qKey.wasPressedThisFrame;
                case KeyCode.F: return kb.fKey.wasPressedThisFrame;
                case KeyCode.UpArrow: return kb.upArrowKey.wasPressedThisFrame;
                case KeyCode.DownArrow: return kb.downArrowKey.wasPressedThisFrame;
                case KeyCode.LeftArrow: return kb.leftArrowKey.wasPressedThisFrame;
                case KeyCode.RightArrow: return kb.rightArrowKey.wasPressedThisFrame;
                default: return false;
            }
#else
            return Input.GetKeyDown(k);
#endif
        }
    }
}
