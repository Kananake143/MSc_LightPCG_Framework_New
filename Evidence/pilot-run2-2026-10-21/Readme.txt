EVIDENCE: Replay pilot, run 2 (PILOT GATE, used in thesis)

Date run:        2026-10-21
Code version:    MIDM-v3-Carry1-2026-10-19
Git tag:         pilot-run2-2026-10-21
Commit hash:     dbd8e6c

Configuration
  Mode:                      Replay (Certificate Replay Mode ON)
  Total Runs:                50
  Design Stride:             3
  Base Seed:                 20261016 (run i uses seed 20261016 + i)
  Start Steps / Max Steps:   3 / 9
  Decoy Levels / KRel Levels: 0-4 / 0-3  (140 design cells, 50 sampled)
  Exit Door Wait:            5
  Target Frame Rate:         60
  PuzzleBot:                 unchanged (PhysicsWait 0.15, VerifyTimeout 1.0)
  LaserSystem maxBounces:    24 (emitter prefab)
  Output folder:             PCG_Output_pilot50_v2
  BatchRunner count in scene: 1 (on AI_Agent)

Result
  Logic-physics agreement:   50/50 = 100.0% (Wilson 95% CI 92.9-100.0)
  Gate criterion:            point estimate >= 95%  -> PASS
  DoorOpened 50, Replans 0, PlanFailedPhysically 0, WalkTeleports 0
  Descriptor identities checked on all rows (Mr*Nsol = KRelApplied;
  CertificateLength = KRelApplied + Mo*Nsol; PlanLength = CertificateLength;
  NActions <= 14*TotalObjects): all hold.

Reproducibility
  Compared with run 1 (same configuration, 2026-10-21): every column except
  timing (ExecutionTimeMs, SolveTimeMs, GenerationTimeMs, SearchTimeMs) is
  identical in all 50 rows. SolveTimeMs ratio run2/run1: mean 1.006,
  range 0.973-1.046.

Known notes
  - Run 10: FailReason "no walking path to (15; 12)" but Solved = 1,
    DoorOpened = 1, Replans = 0. Under investigation.
  - Runs 36 and 43: KRel = 3 requested, KRelApplied = 2 (only 2 solution
    objects). Analysis uses KRelApplied.
  - Run 1 (Evidence\pilot-run1-2026-10-21) found a duplicate BatchRunner
    (on _GridManager, Run On Start ON) in the scene afterwards. It was
    removed and this run repeated. Run 1 data are identical to run 2.

Files in this folder
  PCG_Pilot50_Replay.csv      raw CSV from BatchRunner (do not edit)
  pilot_scale_check.csv       output of analyse_midm.py --pilot
  pilot_rq1.csv               output of analyse_midm.py --pilot
  analysis_output.txt         console output of analyse_midm.py --pilot
  README.txt                  this file

Reproduce the analysis (from the Analysis folder):
  py analyse_midm.py ..\Evidence\pilot-run2-2026-10-21\PCG_Pilot50_Replay.csv --out ..\results\pilot --pilot