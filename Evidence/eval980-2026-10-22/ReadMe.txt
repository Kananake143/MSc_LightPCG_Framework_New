# eval980-2026-10-22 — Evaluation run (Solver mode)

Status: EVALUATION data (not development data). Generated with the analysis plan
frozen beforehand (tag analysis-plan-frozen-2026-10-22, commit <5e1ef27>).

## Configuration
- CodeVersion: MIDM-v3-Carry1-2026-10-21c (same code as pilot140 and solver-stride3)
- Mode: Solver (Certificate Replay Mode OFF), PuzzleBot, MovementRule Carry1
- Bot: nodeLimit=100000, depth=12, timeLimitMs=8000, w=2, 1 replan
- Runs: 980 = 7 x 140 design cells (Steps 3-9 x Decoys 0-4 x KRel 0-3), stride 1
- Base seed: 20262016 (run i uses seed 20262016 + i; seeds 20262017-20262996),
  separate from all development seeds (20261016 base)
- maxGenerationAttempts=200, LaserSystem maxBounces=24
- Unity 6000.0.76f1, Dell Inspiron 15 7000 (i7-7700HQ), 60 fps target
- Run date/time: 22 Oct 2026, <start>-<end>

## Outcome of the run
- 980/980 valid levels, 980/980 solved, 0 replans, 0 timeouts, 0 walking teleports
- Requested design cells are balanced: 7 runs per cell, 245 runs per requested KRel level
- Solved but exit blocked: 20/980 (2.04%, Wilson 95% CI 1.32-3.13). The bot's final
  placement can leave a piece on the cell in front of the door. Solved, SolveTimeMs and
  WalkCells are recorded before the exit walk and are unaffected.
- Decoys placed < decoys requested in 4/980 levels (by 1 piece each; Steps 7, 8, 9, 9).
  The Decoys column records pieces actually placed, so grouping by it gives 6-8 runs
  per cell; grouping by the requested cell gives 7.
- KRelApplied counts (KRel limited by number of solution objects): 245 / 245 / 280 / 210
  for KRelApplied 0 / 1 / 2 / 3.
- SearchNodes == 1 in 29.7% of levels; median 2; maximum 58.

## Files
- PCG_Eval980.csv: raw results, one row per run (unedited)
- run_info.txt: run metadata written by BatchRunner
- hud_final.png, console_complete.png: screenshots at the end of the run
- analysis_output.txt: terminal output of analyse_midm.py
- analysis_results/: T0-T8 tables produced by analyse_midm.py

## Reproduce the analysis
py analyse_midm.py ..\Evidence\eval980-2026-10-22\PCG_Eval980.csv --out results
(analyse_midm.py as in tag analysis-plan-frozen-2026-10-22, no later changes)

## Notes
- The analysis plan (outcomes, models, weight sensitivity) was fixed before this run.
  Anything computed afterwards is labelled post-hoc and stored separately.
- The "runs per cell 6/8" line printed by analyse_midm.py comes from grouping by
  placed (not requested) decoys; it does not indicate an unbalanced run.
- Tag: eval980-2026-10-22 (commit <hash>)