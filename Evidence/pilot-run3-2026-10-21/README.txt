EVIDENCE: Replay pilot, run 3 (development run, superseded by run 4)

Date run:      2026-10-21
Code version:  MIDM-v3-Carry1-2026-10-21
Git tag:       pilot-run3-2026-10-21
Commit hash:   fe40f19

Configuration
  Mode: Replay (Certificate Replay Mode ON), Total Runs 50, Design Stride 3
  Base Seed: 20261016 (run i uses seed 20261016 + i)
  Steps 3-9, Decoys 0-4, KRel 0-3 (140 design cells, 50 sampled)
  Exit Door Wait 5, Target Frame Rate 60
  PuzzleBot unchanged (PhysicsWait 0.15, VerifyTimeout 1.0)
  LaserSystem maxBounces 24 (emitter prefab)
  BatchRunner count in scene: 1 (AI_Agent)
  Output folder: PCG_Output_pilot50_v3

Result
  Logic-physics agreement: 50/50 = 100.0% (Wilson 95% CI 92.9-100.0)
  Gate criterion: point estimate >= 95% -> PASS
  DoorOpened 50, Replans 0, PlanFailedPhysically 0, WalkTeleports 0

Changes since run 2 (same day)
  - PuzzleBot.ExitDoor: if there is no walking path to the door, stop instead
    of moving through walls (post-solve only).
  - BatchRunner: on-screen counters (Level / Solved / Failed / Exit blocked).
  - CodeVersion string updated to 2026-10-21.
  Verification: every column except CodeVersion and timing is identical to run 2
  in all 50 rows. SolveTimeMs ratio run3/run2: mean 0.997, range 0.957-1.014.

Known notes
  - Run 10: FailReason "no walking path to (15; 12)", Solved = 1. The emitter was
    placed in front of the door (see Evidence\diagnostics\run10-exit-blocked-2026-10-21).
    It does not affect Solved, SolveTimeMs or WalkCells. Rate: 1 of 50.
  - Runs 36 and 43: KRel 3 requested, KRelApplied 2 (only 2 solution objects).

Files
  PCG_Pilot50_Replay.csv, run_info.txt, pilot_scale_check.csv, pilot_rq1.csv,
  analysis_output.txt, hud_final.png, README.txt
Superseded by run 4 (generator fix for exit access). Kept to show that the ExitDoor change and the HUD did not change any recorded data.
Note: the tag pilot-run3-2026-10-21 points to the code (fe40f19).
The CSV and the other data files were added in the next commit (6813257).
pilot_rq1.csv and pilot_scale_check.csv were added in the next commit (6949a42).
No code changed between the four commits.
