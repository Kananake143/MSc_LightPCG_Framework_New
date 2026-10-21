EVIDENCE: Replay pilot, all 140 design cells (PILOT GATE, used in thesis)

Date run:      2026-10-21
Code version:  MIDM-v3-Carry1-2026-10-21c
Git tag:       pilot140-2026-10-21
Commit hash:   <ใส่หลังสร้าง tag>

Configuration
  Mode: Replay (Certificate Replay Mode ON), Total Runs 140, Design Stride 1
  Base Seed: 20261016 (run i uses seed 20261016 + i)
  Steps 3-9, Decoys 0-4, KRel 0-3: each of the 140 design cells exactly once
  Max Generation Attempts: 200 (recorded in run_info.txt)
  Exit Door Wait 5, Target Frame Rate 60
  PuzzleBot unchanged (PhysicsWait 0.15, VerifyTimeout 1.0)
  LaserSystem maxBounces 24 (emitter prefab)
  BatchRunner count in scene: 1 (AI_Agent)
  Output folder: PCG_Output_pilot140_replay

Result
  Valid levels: 140/140, invalid: 0
  Logic-physics agreement: 140/140 = 100.0% (Wilson 95% CI 97.3-100.0)
  Gate criterion: point estimate >= 95% -> PASS; the lower bound of the CI is also above 95%.
  LaserHit 140, DoorOpened 140, Replans 0, PlanFailedPhysically 0, WalkTeleports 0.
  No FailReason in any row: no level with the exit blocked by the generator.
  Descriptor identities hold in all rows (Mr*Nsol = KRelApplied;
  CertificateLength = KRelApplied + Mo*Nsol; PlanLength = CertificateLength;
  Relocations = KRelApplied; NActions <= 14*TotalObjects).
  Generation attempts: mean 4.1, median 2, maximum 65 (Steps 9, mean 11.8); 2 levels above 50.

Notes
  - Counted by the Decoys column (decoys actually placed) the sample covers 138
    distinct cells: in Run 62 (requested 3, placed 2) and Run 104 (requested 4,
    placed 3) the generator could not place all requested decoys. Requested Steps,
    Decoys and KRel match the design for all 140 runs.
  - KRelApplied: 0:35, 1:35, 2:40, 3:30 (KRel 3 is capped by the number of solution objects).
  - Earlier development runs: see the other Evidence folders (pilot runs 1-5, Run 10 diagnostic,
    solver stride 3).

Files
  PCG_Pilot140_Replay.csv, run_info.txt, pilot_scale_check.csv, pilot_rq1.csv,
  analysis_output.txt, hud_pilot140.png, README.txt

Reproduce the analysis (from the Analysis folder):
  py analyse_midm.py ..\Evidence\pilot140-2026-10-21\PCG_Pilot140_Replay.csv --out ..\results\pilot140 --pilot