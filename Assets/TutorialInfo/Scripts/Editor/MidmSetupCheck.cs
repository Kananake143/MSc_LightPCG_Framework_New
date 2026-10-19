#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using LightPCG.Systems;
using LightPCG.Research;

namespace LightPCG.EditorTools
{
    /// <summary>
    /// Menu: LightPCG > Check MID-M Setup
    /// Verifies tags, the Agent layer, scene references and Inspector values
    /// before a pilot or evaluation run. Open the experiment scene first.
    /// </summary>
    public static class MidmSetupCheck
    {
        [MenuItem("LightPCG/Check MID-M Setup")]
        public static void Run()
        {
            var errors = new List<string>();
            var warnings = new List<string>();

            // 1. Tags used by LaserSystem / GridVisualizer / ReceiverDetector
            var tags = UnityEditorInternal.InternalEditorUtility.tags;
            foreach (var t in new[] { "Wall", "Emitter", "Receiver", "Mirror", "Refractor", "Door" })
                if (!tags.Contains(t)) errors.Add($"Tag '{t}' is missing (Project Settings > Tags and Layers).");

            // 2. Agent layer
            if (LayerMask.NameToLayer("Agent") < 0)
                errors.Add("Layer 'Agent' is missing. Without it the agent's collider can block the laser.");

            // 3. Scene components
            var gv = Object.FindFirstObjectByType<GridVisualizer>();
            var bot = Object.FindFirstObjectByType<PuzzleBot>();
            var legacy = Object.FindFirstObjectByType<AISolverAgent>();
            var br = Object.FindFirstObjectByType<BatchRunner>();
            if (gv == null) errors.Add("No GridVisualizer in the open scene.");
            if (bot == null) errors.Add("No PuzzleBot in the open scene. Add it to the agent GameObject.");
            if (legacy != null && legacy.enabled)
                errors.Add("AISolverAgent is still enabled. Disable or remove it: it starts solving on Start() and would move components at the same time as PuzzleBot.");
            if (bot != null)
            {
                if (bot.maxExpandedNodes != 100000 || bot.maxPlanDepth != 12 || Mathf.Abs(bot.searchTimeLimitMs - 8000f) > 0.5f)
                    warnings.Add($"PuzzleBot limits are {bot.maxExpandedNodes} nodes / depth {bot.maxPlanDepth} / {bot.searchTimeLimitMs} ms; Chapter 3 uses 100000 / 12 / 8000.");
                if (!LightPCG.Core.MovementRule.IsStudyRule)
                    warnings.Add($"MovementRule is {LightPCG.Core.MovementRule.Describe()}; Chapter 3 (Section 3.2.4) uses Carry1 (rotate in place or carry one cell).");
            }
            if (br == null) errors.Add("No BatchRunner in the open scene.");

            if (gv != null)
            {
                if (gv.emitterCount != 1) errors.Add($"GridVisualizer.emitterCount = {gv.emitterCount}; MID-M supports exactly 1.");
                if (gv.desiredWidth != 16 || gv.desiredHeight != 16)
                    warnings.Add($"Grid is {gv.desiredWidth}x{gv.desiredHeight}; the report describes 16x16.");
                if (gv.emitterPrefab == null || gv.emitterPrefab.GetComponent<LaserSystem>() == null)
                    errors.Add("Emitter prefab is missing or has no LaserSystem.");
                if (gv.receiverPrefab == null || gv.receiverPrefab.GetComponentInChildren<ReceiverDetector>() == null)
                    errors.Add("Receiver prefab is missing or has no ReceiverDetector.");
                foreach (var (name, prefab) in new[] { ("Mirror", gv.mirrorPrefab), ("Refractor", gv.refractorPrefab) })
                {
                    if (prefab == null) { errors.Add($"{name} prefab is not assigned."); continue; }
                    if (prefab.GetComponentInChildren<Collider>() == null)
                        warnings.Add($"{name} prefab has no collider; GridVisualizer will add a BoxCollider to the root.");
                    var col = prefab.GetComponentInChildren<Collider>();
                    if (col != null && col.gameObject != prefab && !col.gameObject.CompareTag(name))
                        errors.Add($"{name} prefab: the collider is on child '{col.gameObject.name}' which is not tagged '{name}'. " +
                                   "The laser reads the tag of the collider it hits.");
                }
                if (gv.emitterPrefab != null)
                {
                    var ls = gv.emitterPrefab.GetComponent<LaserSystem>();
                    if (ls != null && ls.maxBounces < 12)
                        warnings.Add($"LaserSystem.maxBounces = {ls.maxBounces} on the emitter prefab; use at least 12 (24 recommended).");
                }
            }

            if (br != null)
            {
                if (br.gridVisualizer == null) warnings.Add("BatchRunner.gridVisualizer is empty (it will be found at runtime).");
                if (br.solverBot == null) warnings.Add("BatchRunner.solverBot is empty (it will be found at runtime).");
                int cells = (br.maxSteps - br.startSteps + 1) * br.decoyLevels.Length * br.kRelLevels.Length;
                if (br.totalRuns < cells)
                    warnings.Add($"totalRuns = {br.totalRuns} is smaller than the {cells} design cells; some cells will be empty.");
                if (br.codeVersion.Contains(",")) errors.Add("BatchRunner.codeVersion must not contain a comma (CSV).");
            }

            string msg = errors.Count == 0 && warnings.Count == 0
                ? "All MID-M setup checks passed."
                : (errors.Count > 0 ? "ERRORS\n- " + string.Join("\n- ", errors) + "\n\n" : "") +
                  (warnings.Count > 0 ? "WARNINGS\n- " + string.Join("\n- ", warnings) : "");
            foreach (var e in errors) Debug.LogError("[MID-M setup] " + e);
            foreach (var w in warnings) Debug.LogWarning("[MID-M setup] " + w);
            EditorUtility.DisplayDialog("MID-M setup check", msg, "OK");
        }
    }
}
#endif
