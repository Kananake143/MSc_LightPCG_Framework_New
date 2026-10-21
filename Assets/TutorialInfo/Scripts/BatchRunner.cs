using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using LightPCG.Core;
using LightPCG.Systems;

namespace LightPCG.Research
{
    /// <summary>
    /// Runs N automated attempts and exports one CSV row + one layout JSON per level.
    ///
    /// MID-M (Chapter 3):
    ///   MID_static = (Cl~ + Ch + Ci) / 3,         Cl~ = TotalObjects / Nobj_max
    ///   MID_mob    = (Mr + Mo + Mi + Md) / 4      (computed from the initial state, before solving)
    ///   MID-M      = 0.5 * MID_static + 0.5 * MID_mob
    /// Weights are fixed in MidCalculator and are never tuned on the evaluation data.
    /// The legacy formula (0.5/0.3/0.2, Cl = TotalObjects/256) is also written for sensitivity analysis.
    ///
    /// Modes:
    ///   certificateReplayMode = true  -> pilot: replay each certificate physically (logic–physics agreement)
    ///   certificateReplayMode = false -> evaluation: the solver bot (PuzzleBot) plans and acts on its own
    /// </summary>
    public class BatchRunner : MonoBehaviour
    {
        [Header("Experiment")]
        public int totalRuns = 50;
        public bool runOnStart = true;
        [Tooltip("Free-text tag written to every row, e.g. git commit or date.")]
        public string codeVersion = "MID-M v1";

        [Header("Stratified design: Steps x Decoys x kRel")]
        public int startSteps = 3;
        public int maxSteps = 9;
        public int[] decoyLevels = { 0, 1, 2, 3, 4 };
        public int[] kRelLevels = { 0, 1, 2, 3 };
        [Tooltip("Run i uses design cell (i * designStride) mod cells. 1 = plain round-robin (evaluation). " +
                 "Use a stride coprime with the cell count (e.g. 3 for 140 cells) so a short pilot covers every kRel level.")]
        public int designStride = 1;

        [Header("MID-M experiment")]
        public int baseSeed = 20261016;
        [Tooltip("Pilot: replay the solution certificate instead of running the solver.")]
        public bool certificateReplayMode = false;

        [Header("References")]
        public GridVisualizer gridVisualizer;
        [Tooltip("Solver bot. Replaces AISolverAgent (remove or disable that component).")]
        public PuzzleBot solverBot;

        [Header("Output (written to <project>/PCG_Output, outside Assets)")]
        public string outputFolder = "PCG_Output";
        public string csvFileName = "PCG_Results.csv";
        public bool exportLayouts = true;

        [Header("Timing")]
        public float exitDoorWait = 5.0f;
        public int targetFrameRate = 60;

        private readonly List<string> rows = new List<string>();
        private int validRuns, solvedRuns;
        private int currentRun, failedRuns, invalidRuns, exitBlockedRuns;
        [Header("HUD")]
        public bool showHud = true;
        private GUIStyle hudStyle;
        private string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", outputFolder));

        static readonly string Header =
            "Run,Seed,CodeVersion,Mode,LevelValid,RejectReason,GenerationAttempts," +
            "Steps,KRel,KRelApplied,SolutionObjects,Mirrors,Refractors,RuleTransitions,Decoys,TotalObjects,NobjMax,GridWidth,GridHeight," +
            "ClLegacy,MIDlegacy,ClNorm,Ch,Ci,MIDstatic," +
            "Mr,Mo,Mi,Md,MIDmob,MIDM,NActions,NInteract,NDecoyInteract,InitialOnBeam," +
            "CertificateValid,CertificateReachable,CertificateLength," +
            "Solved,SearchPhase,FinalPhase,SearchTimedOut,PlanFailedPhysically,LaserHit,DoorOpened,WalkTeleports," +
            "SearchNodes,SearchTimeMs,TotalPlacements,InPlaceRotations,Relocations,ExecutionTimeMs," +
            "SweepIterations,SweepRelocations,SolveTimeMs,GenerationTimeMs,"+
            "Solver,MovementRule,SearchGenerated,PlanLength,WalkCells,Replans,MaxDepthReached,FailReason";

        void Awake()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = targetFrameRate;   // search yields per frame, so fix the frame rate
        }

        void Start()
        {
            if (gridVisualizer == null) gridVisualizer = FindFirstObjectByType<GridVisualizer>();
            if (solverBot == null) solverBot = FindFirstObjectByType<PuzzleBot>();
            if (solverBot == null) Debug.LogError("[Batch] No PuzzleBot in the scene. Add it to the agent GameObject.");
            if (runOnStart) StartCoroutine(RunBatch());
        }

        public void StartBatch() => StartCoroutine(RunBatch());

        // Deterministic round-robin: Steps cycles fastest, then Decoys, then kRel.
        (int steps, int decoys, int kRel) GetDifficultyForRun(int i)
        {
            int sR = Mathf.Max(1, maxSteps - startSteps + 1);
            int dR = Mathf.Max(1, decoyLevels.Length);
            int kR = Mathf.Max(1, kRelLevels.Length);
            int cells = sR * dR * kR;
            i = (int)(((long)i * Mathf.Max(1, designStride)) % cells);
            int steps = startSteps + i % sR;
            int decoys = decoyLevels.Length > 0 ? decoyLevels[(i / sR) % dR] : 0;
            int kRel = kRelLevels.Length > 0 ? kRelLevels[(i / (sR * dR)) % kR] : 0;
            return (steps, decoys, kRel);
        }

        IEnumerator RunBatch()
        {
            int cells = (maxSteps - startSteps + 1) * decoyLevels.Length * kRelLevels.Length;
            Debug.Log($"[Batch] {totalRuns} runs | {cells} design cells | mode=" +
                      (certificateReplayMode ? "certificate replay" : "solver"));
            rows.Clear(); validRuns = solvedRuns = 0;
            currentRun = failedRuns = invalidRuns = exitBlockedRuns = 0;
            int nObjMax = (maxSteps - 1) + decoyLevels.Max();   // check against generator: Nsol <= Steps - 1

            for (int i = 0; i < totalRuns; i++)
            {
                int run = i + 1;
                currentRun = run;

                int seed = baseSeed + run;
                var d = GetDifficultyForRun(i);
                gridVisualizer.minSteps = d.steps;
                gridVisualizer.maxSteps = d.steps;
                gridVisualizer.decoyCount = d.decoys;
                gridVisualizer.kRel = d.kRel;

                float gStart = Time.realtimeSinceStartup;
                gridVisualizer.GenerateLevel(seed);
                float gMs = (Time.realtimeSinceStartup - gStart) * 1000f;

                yield return new WaitForEndOfFrame();
                yield return new WaitForSeconds(0.3f);

                if (!gridVisualizer.LastLevelValid)
                {
                    invalidRuns++;
                    rows.Add(Row(run, seed, d.steps, d.kRel, nObjMax, gMs, false));
                    continue;
                }
                validRuns++;

                bool waiting = true;
                solverBot.OnSolveComplete = _ => { waiting = false; };
                if (certificateReplayMode) solverBot.StartReplay(gridVisualizer.LastScramble.Certificate);
                else solverBot.StartSolve();
                while (waiting) yield return null;

                yield return new WaitForSeconds(exitDoorWait);

                if (solverBot.WasSolved) solvedRuns++; else failedRuns++;
                if (solverBot.WasSolved && (solverBot.FailReason ?? "").StartsWith("no walking path"))
                    exitBlockedRuns++;
                rows.Add(Row(run, seed, d.steps, d.kRel, nObjMax, gMs, true));

                if (exportLayouts)
                    LayoutExporter.Write(Path.Combine(OutDir, "layouts"), run, seed,
                        gridVisualizer.LevelGrid, gridVisualizer.LastEmitter, gridVisualizer.LastEmitDir,
                        gridVisualizer.LastReceiver, gridVisualizer.LastSolution, gridVisualizer.LastScramble);

                if (run % 10 == 0)
                {
                    Debug.Log($"[Batch] {run}/{totalRuns} | valid={validRuns} solved={solvedRuns}");
                    ExportCSV();   // checkpoint: a crash does not lose the whole batch
                }
                yield return new WaitForSeconds(0.1f);
            }
            ExportCSV();
            Debug.Log($"[Batch] COMPLETE valid={validRuns}/{totalRuns} solved={solvedRuns} -> {OutDir}");
        }

        void OnGUI()
        {
            if (!showHud || totalRuns <= 0) return;
            if (hudStyle == null)
            {
                hudStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
                hudStyle.normal.textColor = Color.white;
            }
            string text = $"Level {currentRun} / {totalRuns}\nSolved: {solvedRuns}\nFailed: {failedRuns}";
            int lines = 3;
            if (invalidRuns > 0) { text += $"\nInvalid: {invalidRuns}"; lines++; }
            if (exitBlockedRuns > 0) { text += $"\nExit blocked: {exitBlockedRuns}"; lines++; }
            GUI.Box(new Rect(10, 10, 290, 16 + 30 * lines), GUIContent.none);
            GUI.Label(new Rect(20, 16, 280, 30 * lines), text, hudStyle);
        }
        string Row(int run, int seed, int steps, int kRel, int nObjMax, float gMs, bool valid)
        {
            var g = gridVisualizer; var a = solverBot; var ci = CultureInfo.InvariantCulture;
            string F(float v) => v.ToString("F4", ci);
            string F2(float v) => v.ToString("F2", ci);
            string B(bool v) => v ? "1" : "0";
            string mode = certificateReplayMode ? "Replay" : "Solver";

            if (!valid)
                return string.Join(",", new[] { run.ToString(), seed.ToString(), codeVersion, mode, "0",
                    (g.LastRejectReason ?? "").Replace(',', ';'), g.LastGenerationAttempts.ToString(),
                    steps.ToString(), kRel.ToString() }) + new string(',', Header.Split(',').Length - 9);

            var mob = g.LastMobility;
            var mid = MidCalculator.Compute(g.LastTotalObjectCount, nObjMax, g.desiredWidth, g.desiredHeight,
                                            g.LastSolutionObjectCount, g.LastRuleTransitions, g.LastRefractorCount, mob);
            var scr = g.LastScramble;
            var cols = new List<string>
            {
                run.ToString(), seed.ToString(), codeVersion, mode, "1", "", g.LastGenerationAttempts.ToString(),
                steps.ToString(), kRel.ToString(), scr.KRelApplied.ToString(),
                g.LastSolutionObjectCount.ToString(), g.LastMirrorCount.ToString(), g.LastRefractorCount.ToString(),
                g.LastRuleTransitions.ToString(), g.LastDecoyCount.ToString(), g.LastTotalObjectCount.ToString(),
                nObjMax.ToString(), g.desiredWidth.ToString(), g.desiredHeight.ToString(),
                F(mid.ClLegacy), F(mid.MidLegacy), F(mid.ClNorm), F(mid.Ch), F(mid.Ci), F(mid.MidStatic),
                F(mob.Mr), F(mob.Mo), F(mob.Mi), F(mob.Md), F(mid.MidMobility), F(mid.MidM),
                mob.NActions.ToString(), mob.NInteract.ToString(), mob.NDecoyInteract.ToString(), mob.InitialOnBeam.ToString(),
                B(scr.CertificateValid), B(scr.CertificateReachable), scr.Certificate.Count.ToString(),
                B(a.WasSolved), a.SearchPhase, a.FinalPhase, B(a.SearchTimedOut), B(a.PlanFailedPhysically),
                B(a.LaserHit), B(a.DoorOpened), a.WalkTeleports.ToString(),
                a.SolveIterations.ToString(), F2(a.SearchTimeMs), a.TotalPlacements.ToString(),
                a.InPlaceRotations.ToString(), a.Relocations.ToString(), F2(a.ExecutionTimeMs),
                a.SweepIterations.ToString(), a.SweepRelocations.ToString(), F2(a.SolveTimeMs), F2(gMs),
                "PuzzleBot", LightPCG.Core.MovementRule.Describe(), a.SearchGenerated.ToString(), a.PlanLength.ToString(),
                a.WalkCells.ToString(), a.Replans.ToString(), a.MaxDepthReached.ToString(),
                (a.FailReason ?? "").Replace(',', ';')
            };
            return string.Join(",", cols);
        }

        void ExportCSV()
        {
            Directory.CreateDirectory(OutDir);
            var sb = new StringBuilder();
            sb.AppendLine(Header);
            foreach (var r in rows) sb.AppendLine(r);
            File.WriteAllText(Path.Combine(OutDir, csvFileName), sb.ToString());

            // Run metadata for Chapter 3 (reproducibility)
            File.WriteAllText(Path.Combine(OutDir, "run_info.txt"),
                $"codeVersion={codeVersion}\nunity={Application.unityVersion}\nbaseSeed={baseSeed}\n" +
                $"totalRuns={totalRuns}\nmode={(certificateReplayMode ? "replay" : "solver")}\n" +
                $"solver=PuzzleBot movementRule={LightPCG.Core.MovementRule.Describe()} " +
                $"nodeLimit={(solverBot != null ? solverBot.maxExpandedNodes : 0)} depth={(solverBot != null ? solverBot.maxPlanDepth : 0)} " +
                $"timeLimitMs={(solverBot != null ? solverBot.searchTimeLimitMs : 0)} w={(solverBot != null ? solverBot.heuristicWeight : 0)}\n" +
                $"targetFrameRate={targetFrameRate}\ndevice={SystemInfo.deviceModel}\ncpu={SystemInfo.processorType}\n" +
                $"designStride={designStride}\nmaxGenerationAttempts={(gridVisualizer != null ? gridVisualizer.maxGenerationAttempts : 0)}\nsteps={startSteps}..{maxSteps}\ndecoys={string.Join("/", decoyLevels)}\nkRel={string.Join("/", kRelLevels)}\n" +
                $"midWeights=static 1/3 each; mobility 1/4 each; alpha={MidCalculator.ALPHA}\n");
        }
    }
}
