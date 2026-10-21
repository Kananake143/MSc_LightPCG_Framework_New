EVIDENCE: Replay pilot, run 4 (development run, generator fix; superseded by run 5)

Date run:      2026-10-21
Code version:  MIDM-v3-Carry1-2026-10-21b
Git tag:       pilot-run4-2026-10-21
Commit hash:   <ใส่หลังสร้าง tag>

Configuration
  Mode: Replay (Certificate Replay Mode ON), Total Runs 50, Design Stride 3
  Base Seed: 20261016 (run i uses seed 20261016 + i)
  Steps 3-9, Decoys 0-4, KRel 0-3 (140 design cells, 50 sampled)
  Max Generation Attempts: 50 (GridVisualizer)
  Exit Door Wait 5, Target Frame Rate 60
  PuzzleBot unchanged (PhysicsWait 0.15, VerifyTimeout 1.0)
  LaserSystem maxBounces 24 (emitter prefab)
  BatchRunner count in scene: 1 (AI_Agent)
  Output folder: PCG_Output_pilot50_v4

Change since run 3 (same day)
  BackwardChainingGenerator: a level is rejected and regenerated when the
  emitter or the receiver is placed on the cell in front of the door (the only
  cell that gives access to the door). Implemented as a failure of the main
  chain (FailChain), checked after all random numbers of the attempt have been
  drawn.

Result
  Rows: 50, valid levels: 49, invalid levels: 1 (Run 10)
  Logic-physics agreement (valid levels): 49/49 = 100.0% (Wilson 95% CI 92.7-100.0)
  Gate criterion: point estimate >= 95% -> PASS
  DoorOpened 49, Replans 0, PlanFailedPhysically 0, WalkTeleports 0
  No level with an emitter or receiver in front of the door (no FailReason,
  no "Exit blocked" on the HUD).

Verification against run 3
  Compared numerically with run 3 (same configuration): every column except
  CodeVersion and timing is identical in 49 of 50 rows. Only Run 10 differs,
  which is the level that had the emitter in front of the door. The fix
  therefore affects only the levels it is meant to affect.

Known notes
  - Run 10 (seed 20261026, Steps 9, Decoys 3, KRel 0): LevelValid = 0 after 50
    generation attempts (reason: "chain incomplete: emitter placed at a fallback
    cell off the chain"). In run 3 this level was accepted at attempt 26, which
    is the attempt rejected by the new rule; the following 24 attempts were also
    rejected. Steps 9 levels need many attempts (mean 8.9 in run 3, maximum 27).
    Cause: the attempt cap (50), not a defect of the generator.
  - Consequence: a cap that is too low would drop hard-to-generate levels
    (mostly Steps 9) from the data. Run 5 raises the cap to 200 and records it
    in run_info.txt.
  - Runs 36 and 43: KRel 3 requested, KRelApplied 2 (only 2 solution objects).

Status: SUPERSEDED by run 5 (Max Generation Attempts 200). Kept to show that the
generator fix changes only the affected level.

Files
  PCG_Pilot50_Replay.csv, run_info.txt,