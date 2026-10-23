using System.Collections;
using UnityEngine;
using LightPCG.Core;
using LightPCG.Systems;

namespace LightPCG.Play
{
    /// <summary>
    /// On-screen interface for the demo scene (PlayDemo) ONLY: start guide, level description (MID-M),
    /// player statistics, new-level controls, example levels and a button that lets the solver bot solve the level.
    /// It reads data from GridVisualizer / PlayerController / PuzzleBot and never changes their logic.
    /// Put it on the same GameObject as PlayerController and PuzzleBot (AI_Agent).
    /// </summary>
    public class DemoUI : MonoBehaviour
    {
        public GridVisualizer gridVisualizer;
        public PlayerController player;
        public PuzzleBot bot;

        [Header("New level (design parameters)")]
        [Range(3, 9)] public int steps = 4;
        [Range(0, 4)] public int decoys = 1;
        [Range(0, 3)] public int kRel = 1;
        public int seed = 1;

        // Same constant as the experiments: Nobj_max = (Steps_max - 1) + Decoys_max = 8 + 4
        const int NObjMax = 12;

        bool showIntro = true, hidePanels, generating, botRunning, botDone, laserOnReceiver;
        string seedText = "1", botSummary = "";
        float laserTimer;
        GUIStyle label, small, header, box, button, title, hint;
        bool stylesReady;

        // Example levels = real runs of the 980-level evaluation (same seed and design cell, so they can be looked up in the CSV).
        struct Example { public string name; public int steps, decoys, kRel, seed; public string note; }
        static readonly Example[] Examples =
        {
            new Example{ name="Example 1 (easy)",   steps=3, decoys=0, kRel=0, seed=20262017, note="Evaluation run 1: the bot needed 1 search node." },
            new Example{ name="Example 2 (medium)", steps=6, decoys=2, kRel=2, seed=20262244, note="Evaluation run 228: the bot needed 14 search nodes." },
            new Example{ name="Example 3 (hard for the bot)", steps=4, decoys=3, kRel=2, seed=20262109, note="Evaluation run 93: the bot needed 58 search nodes (highest MID-M in the set)." },
        };
        int exampleShown = -1;

        void Start()
        {
            if (gridVisualizer == null) gridVisualizer = FindFirstObjectByType<GridVisualizer>();
            if (player == null) player = GetComponent<PlayerController>();
            if (bot == null) bot = GetComponent<PuzzleBot>();
        }

        void Update()
        {
            if (!showIntro && TabPressed()) hidePanels = !hidePanels;
            if (player != null)
                player.InputEnabled = !showIntro && !generating && !botRunning && !botDone;

            laserTimer -= Time.unscaledDeltaTime;
            if (laserTimer <= 0f && player != null)
            {
                laserTimer = 0.2f;
                laserOnReceiver = player.LaserOnReceiver();
            }
        }

        static bool TabPressed()
        {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            var kb = UnityEngine.InputSystem.Keyboard.current;
            return kb != null && kb.tabKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Tab);
#endif
        }

        // ------------------------------------------------------------ actions
        void NewLevel(int s, int d, int k, int sd, int example = -1)
        {
            if (generating || botRunning) return;
            StartCoroutine(Generate(s, d, k, sd, example));
        }

        IEnumerator Generate(int s, int d, int k, int sd, int example)
        {
            generating = true; botDone = false; botSummary = ""; exampleShown = example;
            steps = s; decoys = d; kRel = k; seed = sd; seedText = sd.ToString();
            gridVisualizer.minSteps = s; gridVisualizer.maxSteps = s;
            gridVisualizer.decoyCount = d; gridVisualizer.kRel = k;
            gridVisualizer.GenerateLevel(sd);
            yield return new WaitForEndOfFrame();
            yield return new WaitForSeconds(0.2f);
            player.BeginLevel();
            generating = false;
        }

        void StartBot()
        {
            if (bot == null || botRunning || generating || player.Won || !gridVisualizer.LastLevelValid) return;
            botRunning = true; botDone = false; botSummary = "The bot is solving...";
            bot.OnSolveComplete = ok =>
            {
                botRunning = false; botDone = true;
                botSummary = (bot.WasSolved ? "Bot solved it" : "Bot did not solve it") +
                             $" | plan: {bot.PlanLength} actions | search nodes: {bot.SolveIterations}" +
                             $" | states generated: {bot.SearchGenerated} | cells walked: {bot.WalkCells}";
            };
            bot.StartSolve();
        }

        // ------------------------------------------------------------ GUI
        void InitStyles()
        {
            if (stylesReady) return;
            label = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true, richText = true };
            small = new GUIStyle(label) { fontSize = 12 };
            header = new GUIStyle(label) { fontSize = 15, fontStyle = FontStyle.Bold };
            title = new GUIStyle(label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            hint = new GUIStyle(label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            box = new GUIStyle(GUI.skin.box) { padding = new RectOffset(10, 10, 8, 8) };
            button = new GUIStyle(GUI.skin.button) { fontSize = 14 };
            stylesReady = true;
        }

        void OnGUI()
        {
            if (gridVisualizer == null || player == null) return;
            InitStyles();
            float s = Mathf.Max(1f, Screen.height / 720f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
            float vw = Screen.width / s, vh = Screen.height / s;

            if (showIntro) { DrawIntro(vw, vh); return; }

            if (!hidePanels) { DrawLeft(vh); DrawRight(vw, vh); }
            else if (GUI.Button(new Rect(vw - 130, 10, 120, 28), "Show panels (Tab)", button)) hidePanels = false;
            DrawBanner(vw, vh);
        }

        void DrawIntro(float vw, float vh)
        {
            GUI.Box(new Rect(0, 0, vw, vh), GUIContent.none);
            float w = Mathf.Min(760, vw - 40), h = Mathf.Min(620, vh - 40);
            Rect r = new Rect((vw - w) / 2, (vh - h) / 2, w, h);
            GUILayout.BeginArea(r, box);
            GUILayout.Label("Light puzzle: procedural level demo", title);
            GUILayout.Space(6);
            GUILayout.Label("<b>Goal.</b> Make the laser beam from the <color=#e6c200>emitter</color> (yellow) reach the <color=#e0605a>receiver</color> (red). " +
                            "The <color=#20b050>door</color> (green) then opens: walk into it to finish the level.", label);
            GUILayout.Space(4);
            GUILayout.Label("<b>Pieces.</b> Mirrors turn the beam by 90 degrees. Prisms (pink) let it pass straight or turn it left or right, depending on their rotation. Grey blocks are walls. " +
                            "Some pieces are decoys: they are not needed.", label);
            GUILayout.Space(4);
            GUILayout.Label("<b>Controls.</b>\n" +
                            "W A S D or arrow keys: walk (hold to run). Face a piece by walking into it.\n" +
                            "R: rotate the piece in front of you.\n" +
                            "E: pick up the piece in front of you. While carrying, the first arrow press only turns you (the ghost moves to the cell in front of you), the next press in the same direction takes one step. Press E again to put it down where the ghost is shown.\n" +
                            "Green ghost = allowed, red ghost = not allowed.   Q: put the piece back.   F: sandbox, free carry.", label);
            GUILayout.Space(4);
            GUILayout.Label("<b>Rule used in the research (Carry1).</b> A piece can only be moved to a cell next to the one it was picked up from. " +
                            "The bot and all measurements in the dissertation use this rule.", label);
            GUILayout.Space(4);
            GUILayout.Label("<b>What the panels show.</b> Every level is generated backwards from the solution and then scrambled. " +
                            "The left panel describes the level with MID-M, a number computed from the level itself before anyone plays it. " +
                            "The button <i>Let the bot solve it</i> shows the solver bot used in the experiments.\n" +
                            "<b>Reading MID-M.</b> relocate = share of solution pieces that must be moved; rotate = share that only need turning; " +
                            "beam-changing = share of single actions that change the beam; decoy = share of those that involve a decoy. " +
                            "Tab hides or shows the panels.", label);
            GUILayout.Space(4);
            GUILayout.Label("<i>This program is a demonstration. The results in the dissertation come from automated bot runs, not from human play, " +
                            "and no human data were collected.</i>", small);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Start", button, GUILayout.Height(36))) showIntro = false;
            GUILayout.EndArea();
        }

        void DrawLeft(float vh)
        {
            GUILayout.BeginArea(new Rect(10, 10, 300, vh - 20));
            GUILayout.BeginVertical(box);

            GUILayout.Label("Level", header);
            if (gridVisualizer.LastLevelValid)
            {
                int kApplied = gridVisualizer.LastScramble != null ? gridVisualizer.LastScramble.KRelApplied : 0;
                GUILayout.Label($"Seed {gridVisualizer.LastSeed}   |   Steps {gridVisualizer.LastSteps}   |   Decoys {gridVisualizer.LastDecoyCount}   |   Pieces to relocate {kApplied}", small);
                GUILayout.Label($"Movable pieces: {gridVisualizer.LastTotalObjectCount} = {gridVisualizer.LastMirrorCount + gridVisualizer.LastRefractorCount} in the solution ({gridVisualizer.LastMirrorCount} mirror(s), {gridVisualizer.LastRefractorCount} prism(s)) + {gridVisualizer.LastTotalObjectCount - gridVisualizer.LastMirrorCount - gridVisualizer.LastRefractorCount} decoy(s)", small);

                var m = gridVisualizer.LastMobility;
                var mid = MidCalculator.Compute(gridVisualizer.LastTotalObjectCount, NObjMax, gridVisualizer.desiredWidth, gridVisualizer.desiredHeight,
                                                gridVisualizer.LastSolutionObjectCount, gridVisualizer.LastRuleTransitions, gridVisualizer.LastRefractorCount, m);
                GUILayout.Space(4);
                GUILayout.Label("MID-M (of the generated level)", header);
                GUILayout.Label($"<b>MID-M {mid.MidM:F2}</b>  =  0.5 x static {mid.MidStatic:F2}  +  0.5 x mobility {mid.MidMobility:F2}", label);
                GUILayout.Label($"Static:  density {mid.ClNorm:F2}  |  rule changes {mid.Ch:F2}  |  prisms {mid.Ci:F2}", small);
                GUILayout.Label($"Mobility:  relocate {m.Mr:F2}  |  rotate {m.Mo:F2}  |  beam-changing actions {m.Mi:F2}  |  decoy involvement {m.Md:F2}", small);
            }
            else GUILayout.Label("No valid level. Try another seed.", label);

            GUILayout.Space(6);
            GUILayout.Label("You", header);
            GUILayout.Label($"Actions {player.Moves}  (rotations {player.Rotations}, carries {player.Carries})   |   steps walked {player.WalkSteps}   |   time {player.ElapsedSeconds:F0} s", small);
            GUILayout.Label(player.FreeCarry ? "Rule: <color=#e08000>sandbox, free carry (not the rule used in the experiments)</color>"
                                             : "Rule: Carry1 (the rule used in the experiments)", small);
            GUILayout.Label("Beam reaches the receiver: " + (laserOnReceiver ? "<color=#20b050>yes</color>" : "no"), small);

            if (!string.IsNullOrEmpty(botSummary))
            {
                GUILayout.Space(6);
                GUILayout.Label("Bot", header);
                GUILayout.Label(botSummary, small);
            }
            if (exampleShown >= 0)
            {
                GUILayout.Space(6);
                GUILayout.Label(Examples[exampleShown].note, small);
            }
            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        void DrawRight(float vw, float vh)
        {
            float w = 300;
            GUILayout.BeginArea(new Rect(vw - w - 10, 10, w, vh - 20));
            GUILayout.BeginVertical(box);

            GUILayout.Label("New level", header);
            steps = SliderInt("Steps (path length)", steps, 3, 9);
            decoys = SliderInt("Decoys", decoys, 0, 4);
            kRel = SliderInt("Pieces to relocate (KRel)", kRel, 0, 3);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Seed", small, GUILayout.Width(40));
            seedText = GUILayout.TextField(seedText, 9, GUILayout.Width(110));
            if (GUILayout.Button("Random", button)) seedText = Random.Range(1, 99999999).ToString();
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Generate level", button, GUILayout.Height(30)))
            {
                int sd; if (!int.TryParse(seedText, out sd)) { sd = 1; seedText = "1"; }
                NewLevel(steps, decoys, kRel, sd);
            }

            GUILayout.Space(8);
            GUILayout.Label("Example levels", header);
            for (int i = 0; i < Examples.Length; i++)
                if (GUILayout.Button(Examples[i].name, button))
                    NewLevel(Examples[i].steps, Examples[i].decoys, Examples[i].kRel, Examples[i].seed, i);

            GUILayout.Space(8);
            GUILayout.Label("Solver bot", header);
            GUI.enabled = !botRunning && !generating && !player.Won && gridVisualizer.LastLevelValid && !botDone;
            if (GUILayout.Button("Let the bot solve it", button, GUILayout.Height(30))) StartBot();
            GUI.enabled = true;
            if (botDone) GUILayout.Label("Generate a new level to play again.", small);

            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Hide (Tab)", button)) hidePanels = true;
            if (GUILayout.Button("Help", button)) showIntro = true;
            if (GUILayout.Button("Quit", button)) Application.Quit();
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        int SliderInt(string name, int value, int min, int max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, small, GUILayout.Width(150));
            value = Mathf.RoundToInt(GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(90)));
            GUILayout.Label(value.ToString(), small, GUILayout.Width(20));
            GUILayout.EndHorizontal();
            return value;
        }

        void DrawBanner(float vw, float vh)
        {
            string text = player.Won
                ? $"Level complete!  {player.Moves} actions, {player.WalkSteps} steps, {player.ElapsedSeconds:F0} s"
                : (!string.IsNullOrEmpty(player.Hint) ? player.Hint : player.Message);
            if (string.IsNullOrEmpty(text)) return;
            float w = Mathf.Min(640, vw - 740);
            if (w < 300) w = 300;
            Rect r = new Rect((vw - w) / 2f, vh - 70, w, 50);
            GUI.Box(r, GUIContent.none);
            GUI.Label(r, text, hint);
        }
    }
}
