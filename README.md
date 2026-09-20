# BIOBUZZ Simulator (FTC 2026-2027)
[![Instagram](https://img.shields.io/badge/Instagram-E4405F?style=for-the-badge&logo=instagram&logoColor=white)](https://www.instagram.com/biobuzzsim) instagram


You can send Suggestions To me in Discord: ".lipor"

A MoSimulator-style 3D driving game for the FIRST Tech Challenge 2026-2027 game
**BIOBUZZ presented by RTX**, built in **Unity 6000.6.2f1** (built-in render pipeline).
Everything in the scene is generated from code using the dimensions in the
**BIOBUZZ Competition Manual V1** (`Assets/Scripts/Dims.cs` cites the section for every value).

## Play

Run `Build/BioBuzzSim.exe`, or open this folder in Unity 6000.6 and press Play
(`Assets/Scenes/Main.unity`). Build again with **BIOBUZZ ▸ Build Windows**.

Modes: **Match** (30 s AUTO → 8 s transition → 2:00 TELEOP, full scoring and RP), **Free Practice**
and the **AUTO PLANNER**. Each of the 4 stations can be Keyboard, Gamepad 1-4, AI or empty, and can run
the built-in AUTO or any saved planner file. The whole menu can be driven with a gamepad
(D-pad / stick to move, A to select, B to go back).


## Cameras

**C** (gamepad **R3**) cycles: Driver Station, **Driver Station (tracking)**, Third Person,
Chase, Overhead, Audience. **G** (**L3**) flips the third-person camera 180 degrees.

*Driver Station (tracking)* stands where the drivers stand, but as a person rather than a
tripod: they shift their weight along the wall, their head rises and falls, and their eyes
stay on the ROBOT wherever it drives.

## AUTO PLANNER

1. Pick the robot and alliance, click the field to add waypoints, drag the dot to move and the knob
   to set the heading (mouse wheel rotates, right-click deletes, Ctrl+click inserts).
2. Paths avoid the HIVE frame and FLOWERS (A* + spline smoothing) and are timed with the robot's
   top speed, acceleration, cornering and turn-rate limits. Per waypoint: speed limit, stop or drive
   through, and (tank) drive backwards.
3. **Actions at a waypoint** (the robot stops): Shoot for N s (aims first), Wait, Collect nearest
   (simulated camera, 40° FOV / 2.8 m, with a timeout), Score FLOWER, Outtake, Park.
   **Timeline markers** run while moving: Intake, Fire on the move (turret robots), FLOWER mechanism, Outtake.
4. Scrub the timeline to preview. Green dots on the field show where a shot into the up CELL works.
   CHECKS lists G304 start-pose problems, G402 crossings, G410 and over-time steps.
5. **RUN PHYSICS SIMULATION** runs the real robot for the 30 s AUTO; afterwards the timeline becomes
   a recording you can scrub like a video (robot, elements, HIVE tips, shots, AUTO points).
6. **SAVE**, then "Use as AUTO for R1/R2/B1/B2" (plans are mirrored automatically for the other
   alliance). Files live in `%USERPROFILE%\AppData\LocalLow\BIOBUZZ Sim\BIOBUZZ Simulator\AutoPlans`.

## This Game is built from the menual, but do not take everything as right.
* FIELD 144 x 144 in, 36 tiles, ALLIANCE AREAS 97 x 54 in, LOADING ZONES 23 x 11 in, GARDENS 23 x 2 in (9.2, 9.3, Fig 9-2/9-3).
* HIVE Structure: frame 49.46 x 38.95 in, pivots at 43.95 in, two bi-stable HIVES 25.5 in apart,
  CELLS 20 x 14 in opening, 12 in deep, 18.84 in apart, 30° tilt, opening 53.5-65.6 in above the tiles,
  AprilTag clusters under each CELL (9.6, 9.9, Fig 9-8 to 9-11).
* FLOWERS on the perimeter: 4 in top ring at 21.5 in, 1.25 in backstop, 3.55 in retrieval opening,
  0.43 in lower ring with 2.79 in hole, POLLEN falls through the middle ring but NECTAR does not (9.7, Fig 9-12).
* POLLEN 2.8 in (40), NECTAR 3.6 in (8 per alliance), staging per 10.3.1.
* Scoring per Table 10-2, RP thresholds Table 10-3, audio cues Table 9-1, G402 / G407 / G408 / G410 / G426 / G427.

