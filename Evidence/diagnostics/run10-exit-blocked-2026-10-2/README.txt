DIAGNOSTIC: Run 10 "no walking path to (15; 12)"

Date: 2026-10-21
Code: MIDM-v3-Carry1-2026-10-19 (before the ExitDoor change)
Config: Replay, Total Runs 10, Design Stride 3, Base Seed 20261016
        (runs 1-10 identical to the pilot; all non-timing columns match)
Purpose: find out why Run 10 reports FailReason although Solved = 1.

Finding
  Door at (15,12) in the outer wall. The emitter was placed at (14,12), the only
  cell that gives access to the door. BackwardChainingGenerator.PlaceEmitterAgainstWall
  treats a Door in front of the beam as a boundary, so the emitter can end right
  in front of the door. The receiver sits at (14,10) (ReceiverNearDoor, offset 2).
  After the puzzle is solved, PuzzleBot.ExitDoor cannot find a walking path to
  the door, and in this code version the bot then moved straight through the
  walls to the cell outside the door.

Impact on results: none.
  Solved, LaserHit, DoorOpened, SolveTimeMs and ExecutionTimeMs are recorded in
  Finish() before ExitDoor runs, and WalkCells excludes the exit walk.
  Only the FailReason text is affected.

Frequency: 1 of 50 pilot levels (about 2%, Wilson 95% CI 0.4-10.5%).

Decision: generator unchanged (to keep seeds and the pilot valid). ExitDoor now
stops when no path exists. Limitation to report: the generator does not
guarantee that the cell in front of the door is free.

Files: Run10Check.csv, run10_screenshot.png