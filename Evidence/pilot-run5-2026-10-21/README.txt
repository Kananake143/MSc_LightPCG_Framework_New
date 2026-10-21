EVIDENCE: Replay pilot, run 5 (PILOT GATE, final code and settings, used in thesis)

Date run:      2026-10-21
Code version:  MIDM-v3-Carry1-2026-10-21c
Git tag:       pilot-run5-2026-10-21
Commit hash:   <ใส่หลังสร้าง tag>

Configuration
  Mode: Replay (Certificate Replay Mode ON), Total Runs 50, Design Stride 3
  Base Seed: 20261016 (run is uses seed 20261016 + i)
  Steps 3-9, Decoys 0-4, KRel 0-3 (140 design cells, 50 sampled)
  Max Generation Attempts: 200 (GridVisualizer; recorded in run_info.txt)
  Exit Door Wait 5, Target Frame Rate 60
  PuzzleBot unchanged (PhysicsWait 0.15, VerifyTimeout 1.0)
  LaserSystem maxBounces 24 (emitter prefab)
  BatchRunner count in scene: 1 (AI_Agent)
  Output folder: PCG_Output_pilot50_v5

Result
  Valid levels: 50/50, invalid: 0
  Logic-physics agreement: 50/50 = 100.0% (Wilson 95% CI 92.9-100.0)
  Gate criterion: point estimate >= 95% -> PASS
  DoorOpened 50, Replans 0, PlanFailedPhysically 0, WalkTeleports 0
  No FailReason in any row: no level with the emitter or receiver in front of the door.
  Descriptor identities hold in all rows (Mr*Nsol = KRelApplied;
  CertificateLength = KRelApplied + Mo*Nsol; PlanLength = CertificateLength;
  Relocations = KRelApplied).

Verification against run 4
  Every column except CodeVersion and timing is identical in 49 of 50 rows.
  Only Run 10 differs: with the cap raised from 50 to 200 it is generated at
  attempt 66 (Steps 9, 8 solution objects) and solved (PlanLength 7).

Development history (see the other Evidence folders)
  run 1/2: first pilots (a duplicate BatchRunner was found afterwards; data identical)
  run 3  : ExitDoor stops instead of crossing walls; HUD; no change in data
  run 4  : generator rejects levels with emitter/receiver in front of the door;
           Run 10 hit the 50-attempt cap
  run 5  : cap raised to 200 (this run)

Notes
  - Runs 36 and 43: KRel 3 requested, KRelApplied 2 (only 2 solution objects).
    The analysis uses KRelApplied.
  - Steps 9 levels need many generation attempts (run 3: 27, run 5 Run 10: 66).

Files
  PCG_Pilot50_Replay.csv, run_info.txt, pilot_scale_check.csv, pilot_rq1.csv,
  analysis_output.txt, hud_run5.png, README.txt
Note: run_info.txt of this run does not list maxGenerationAttempts (the line was added to
BatchRunner afterwards). The value 200 was set in the GridVisualizer Inspector for this run;
Run 10 reached attempt 66 in the CSV, which is above the previous cap of 50.

Reproduce the analysis (from the Analysis folder):
  py analyse_midm.py ..\Evidence\pilot-run5-2026-10-21\PCG_Pilot50_Replay.csv --out ..\results\pilot --pilot