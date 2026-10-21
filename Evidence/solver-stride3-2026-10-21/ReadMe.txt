EVIDENCE: Solver pilot, stride 3 (DEVELOPMENT RUN, not evaluation data)

Date run:      2026-10-21
Code version:  MIDM-v3-Carry1-2026-10-21c
Related tag:   pilot-run5-2026-10-21 (same code; later commits only add metadata to run_info.txt)
Commit hash:   <ใส่ hash ของ commit ที่เพิ่มโฟลเดอร์นี้>

Purpose
  Check what the solver bot (PuzzleBot, A*) does on levels with kRel 0-3 before the
  evaluation run: does success or search effort vary enough to be used as an outcome?
  This is development data. It was used to choose the outcome variables, not to tune
  the descriptors or their weights. The evaluation uses a different seed.

Configuration
  Mode: Solver (Certificate Replay Mode OFF), Total Runs 50, Design Stride 3
  Base Seed: 20261016 (run i uses seed 20261016 + i). Same 50 levels as the Replay
  pilot (run 5); only the bot mode differs.
  Steps 3-9, Decoys 0-4, KRel 0-3 (140 design cells, 50 sampled)
  Max Generation Attempts: 200
  Exit Door Wait 5, Target Frame Rate 60
  Planner: weighted A*, w = 2, node limit 100000, depth 12, time limit 8000 ms,
  1 replan; MovementRule Carry1 (see run_info.txt)
  Output folder: PCG_Output_solver_stride3

Result (n = 50 valid levels)
  Solved 50/50, DoorOpened 50, Replans 0, SearchTimedOut 0, PlanFailedPhysically 0,
  WalkTeleports 0. Success has no variance, so Solved cannot be an outcome variable.
  Search effort does vary:
    SearchNodes:     = 1 in 38% of levels; median 2; mean 3.24; maximum 20
    SearchGenerated: median 16.5; mean 31.4; maximum 294
    PlanLength:      mean 1.84; maximum 4 (CertificateLength: mean 3.52)
                     plan shorter than the certificate in 70% of levels, equal in 28%,
                     longer in 2% (one level)
  By KRelApplied (mean PlanLength / mean SearchNodes):
    0: 1.67 / 3.67    1: 1.67 / 2.00    2: 2.15 / 3.62    3: 1.90 / 3.60
  Spearman correlation with SearchGenerated (exploratory, n = 50, wide uncertainty):
    MIDM 0.36, MIDstatic 0.34, MIDmob 0.06, TotalObjects 0.30, CertificateLength 0.30
  The mobility part of MID-M shows almost no association with search effort in this
  sample. This is reported as observed; no descriptor or weight was changed because of it.

Decisions taken from this run
  - Outcomes for the evaluation: SearchNodes, SearchGenerated, PlanLength
    (SolveTimeMs and WalkCells only as sensitivity analyses: they are dominated by walking time).
  - Solved is reported descriptively (the bot solves every level).

Known notes
  - Run 14: FailReason "no walking path to (0; 9)" with Solved = 1. The same level had no
    FailReason in the Replay pilot (run 5), so the generator did not block the door.
    The final arrangement chosen by the bot (1 relocation) most likely put a piece in
    front of the door (not verified by inspecting the layout). It does not affect Solved,
    SolveTimeMs or WalkCells. HUD counter: "Exit blocked: 1" of 50.
  - Runs 36 and 43: KRel 3 requested, KRelApplied 2 (only 2 solution objects).
  - Run 10 (Steps 9) needed 66 generation attempts (cap 200).

Files
  PCG_Solver50_stride3.csv  raw CSV from BatchRunner (do not edit)
  run_info.txt              run metadata written by BatchRunner
  pilot_scale_check.csv, pilot_rq1.csv   output of analyse_midm.py --pilot
  analysis_output.txt       console output of analyse_midm.py --pilot
  hud_solver_stride3.png    on-screen counters at the end of the run
  README.txt                this file

Reproduce the analysis (from the Analysis folder):
  py analyse_midm.py ..\Evidence\solver-stride3-2026-10-21\PCG_Solver50_stride3.csv --out ..\results\solver_stride3 --pilot
## Exit blocked (Run 14)

1 of 50 runs shows FailReason "no walking path to (x; y)" with Solved = true.
Observed in the scene: the bot placed a mirror to adjust the beam direction, and after
the door opened that mirror stood on the cell in front of the door, so the bot
could not walk to the exit.

- Solved, SolveTimeMs and WalkCells are unaffected, because Finish() records them
  before ExitDoor runs.
- The same level exits normally in Replay mode (the certificate does not place
  a piece there), so this comes from the solver's own final arrangement, not
  from the generator.
- The bot is not changed before the evaluation run: forbidding that cell would
  change the search space and therefore SearchNodes.
- The rate is reported from the "Exit blocked" counter in the evaluation run.
