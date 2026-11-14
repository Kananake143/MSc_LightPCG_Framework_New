A Procedural Content Generation Framework for Light-Based Puzzles

Exploring Algorithmic Control of Difficulty and Rule Interaction

MSc dissertation project (CMP504), Abertay University. 
Author: Kananake Yimsirivattana 
Supervisor: Dr.Jung In Jung and Dr.William Kavanagh

This repository contains the Unity project behind the dissertation. It has three parts:

CGRPG, a constraint-guided reverse path generator. It builds light-puzzle levels backwards from the solution, scrambles them with legal moves only, and stores a replayable solution certificate for every level.
PuzzleBot, a bounded weighted-A* solver bot. It finds a solution on its own, without seeing the certificate, the intended solution or which pieces are decoys.
MID-M, a descriptor computed from the generated level before anyone solves it. The dissertation tests it against the bot's search effort.

The puzzle: make the laser beam from the emitter reach the receiver by rotating mirrors and refractors, and by carrying them one cell at a time. The door then opens and the player walks out.

Note: the playable demo is a demonstration only. All results in the dissertation come from automated bot runs. No human players took part and no human data were collected.

1. Quick start: play the demo (no Unity needed)
Download PuzzleDemo_Windows.zip from the Releases page of this repository: [Release link]
Unzip the whole folder. Do not run the .exe from inside the zip.
Run [GameName].exe
If Windows SmartScreen warns about an unknown publisher, choose "More info", then "Run anyway". The program is not code-signed.

Tested on: Windows 10

Controls
W A S D or arrow keys: walk (hold to run). Face a piece by walking into it.
R: rotate the piece in front of you, or the piece you carry.
E: pick up the piece in front of you. While carrying, the first arrow press only turns you (the ghost moves to the cell in front of you) and the next press in the same direction takes one step. Press E again to put the piece down where the ghost is shown.
Q: put the carried piece back.
F: sandbox mode (free carry). This is not the rule used in the research.
Tab: hide or show the panels.

Ghost colour: green means the placement is allowed, red means it is not.

Movement rule used in the research (Carry1): a piece can only be moved to a cell next to the one it was picked up from. The bot and every measurement in the dissertation use this rule.

What the panels show

Left panel: the level (seed, Steps, Decoys, pieces to relocate), the MID-M value with its static and mobility components, your own statistics (actions, steps walked, time) and, after the bot has run, its result.

Right panel: sliders for Steps (3 to 9), Decoys (0 to 4) and pieces to relocate, KRel (0 to 3), a seed box, a "Generate level" button, three example levels, and a "Let the bot solve it" button.

The three example levels are real runs of the 980-level evaluation (same seed and design cell), so they can be looked up in PCG_Eval980.csv:

Example 1 (easy): evaluation run 1, seed 20262017, Steps 3, Decoys 0, KRel 0. The bot needed 1 search node.
Example 2 (medium): evaluation run 228, seed 20262244, Steps 6, Decoys 2, KRel 2. The bot needed 14 search nodes.
Example 3 (hard for the bot): evaluation run 93, seed 20262109, Steps 4, Decoys 3, KRel 2. The bot needed 58 search nodes, the most in the set and the highest MID-M of the set.
2. Open the project in Unity

Unity version: 6000.0.76f1 (the version used for all runs).

Clone the repository and open the folder with Unity Hub ("Add project from disk"). The first import takes a few minutes. The project works with the old Input Manager, the new Input System, or both.

Scenes:

PlayDemo is the playable demonstration scene. It is the scene built into the executable. Open this one if you only want to try the game.
SampleScene is the experiment scene. It contains the BatchRunner, which runs the 980 evaluation levels automatically and writes the results file. WARNING: it starts as soon as you press Play (runOnStart is on) and takes hours. Do not press Play in this scene unless you want to re-run the experiment.


Before any experiment run, use the menu LightPCG, then "Check MID-M Setup". It checks the tags, the Agent layer, the scene components, the 16 x 16 grid, the solver limits and the Carry1 rule, and that the old solver component (AISolverAgent) is not enabled.

3. Code guide

Namespaces: LightPCG.Core (pure logic, no Unity physics), LightPCG.Systems (scene objects), LightPCG.Research (experiments), LightPCG.Play (demo) and LightPCG.EditorTools (editor menu).

Core logic:

GridModel.cs: the board and tile types (empty, wall, emitter, receiver, mirror, refractor, door).
OpticsRules.cs: the one optical rule table (allowed orientations, optical classes and beam deflection), shared by the generator, the bot and the scene.
PuzzleState.cs: MovementRule (Carry1), Piece, Move, PuzzleState.Apply and the logical BeamTracer.
BackwardChainingGenerator.cs: CGRPG. Builds the beam path backwards from the receiver and places the emitter, decoys and pillar walls.
PuzzleScrambler.cs: the intended solution, the legal scramble of the solved layout, the solution certificate, certificate verification and walking reachability.
MobilityAnalyzer.cs: the mobility components of MID-M (Mr, Mo, Mi, Md).
MidCalculator.cs: the static components and the MID-M composite (weight 0.5 between static and mobility).
BotPlanner.cs: the weighted A* planner (f = g + 2h, limits of 100,000 nodes, depth 12 and 8 seconds) and the walking model.
LayoutExporter.cs: writes one JSON layout per level, so every feature can be recomputed without Unity.

Scene objects:

PuzzleBot.cs: the solver bot in the scene. It plans, walks, picks up, rotates and places pieces.
LaserSystem.cs and ReceiverDetector.cs: the physical laser, and the receiver that opens the door after a sustained hit (0.3 seconds).
GridVisualizer.cs: builds the level in the scene from a seed and the design parameters.

Experiments, demo and tools:

BatchRunner.cs: runs the automated experiment and writes the CSV and run_info.txt.
DemoUI.cs and PlayerController.cs: demo scene only. The interface and the human-playable mode.
MidmSetupCheck.cs: the editor menu check before an experiment.
AISolverAgent.cs: an earlier solver, kept for history. It was replaced by PuzzleBot and must stay disabled.
4. Reproduce the results

The evaluation has 980 instances: 7 Steps x 5 Decoys x 4 KRel = 140 design cells, 7 instances per cell, base seed 20262016.

Archived versions (git tags)
pilot140-2026-10-21: replay pilot of 140 instances (the pilot gate).
dev-solver-stride3-2026-10-21: the solver pilot.
analysis-plan-frozen-2026-10-22: the pre-specified analysis script analyse_midm.py (commit 5e1ef27).
eval980-2026-10-22: the evaluation run, with PCG_Eval980.csv and the run-information file.
verification-2026-10-23: the offline implementation checks (commit c968c12). Between the evaluation tag and this one, only new files were added: the demo scripts, the demo scene and the test project.
[submission-2026-11-19]: the version submitted for marking.

CodeVersion recorded in every row of the results file: MIDM-v3-Carry1-2026-10-21c

Re-run the experiment (optional, takes hours)
Open SampleScene and select the BatchRunner object.
Set totalRuns to 980, baseSeed to 20262016, designStride to 1, certificateReplayMode off, and codeVersion to the value above.
Press Play. The run writes PCG_Results.csv, run_info.txt and one layout JSON per level to the PCG_Output folder, next to the Assets folder.

Each run takes roughly 10 seconds (an estimate), so the full batch takes in the order of 2 to 3 hours. The design is deterministic, so the same seed and design cell regenerate the same level.

Re-run the analysis
[python analyse_midm.py PCG_Eval980.csv]


Offline implementation checks

The test project and its output files are archived at the tag verification-2026-10-23 (see Appendix B of the dissertation).

5. Limitations
The effort measured is the computational effort of one bounded heuristic bot, not human-perceived difficulty.
The demo was added after the evaluation and was not used to collect any result.
In 20 of 980 evaluation runs (2.04%) the puzzle was solved but the final placement left no walking path to the door. This is reported in the dissertation.
