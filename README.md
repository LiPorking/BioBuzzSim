# BIOBUZZ Simulator (FTC 2026-2027)

A MoSimulator-style 3D driving game for the FIRST Tech Challenge 2026-2027 game
**BIOBUZZ presented by RTX**, built in **Unity 2023.2.22f1** (built-in render pipeline).
Everything in the scene is generated from code using the dimensions in the
**BIOBUZZ Competition Manual V1** (`Assets/Scripts/Dims.cs` cites the section for every value).

## Play

Run `Build/BioBuzzSim.exe`, or open this folder in Unity 2023.2 and press Play
(`Assets/Scenes/Main.unity`). Build again with **BIOBUZZ ▸ Build Windows**.

Modes: **Match** (30 s AUTO → 8 s transition → 2:00 TELEOP, full scoring and RP), **Free Practice**
and the **AUTO PLANNER**. Each of the 4 stations can be Keyboard, Gamepad 1-4, AI or empty, and can run
the built-in AUTO or any saved planner file. The whole menu can be driven with a gamepad
(D-pad / stick to move, A to select, B to go back).

## Controls (all rebindable in **Controls**, saved automatically)

| Action | Keyboard (default) | Gamepad (default) |
|---|---|---|
| Drive | WASD / arrows | Left stick |
| Rotate | Q / E | Right stick X, D-pad left/right |
| Intake / outtake | Left Shift, RMB / R, Left Ctrl | LT / LB |
| **Fire** (aims, then shoots once locked on) | Space, LMB | RT |
| FLOWER mechanism | F | RB |
| Target HIVE ↔ FLOWER | Tab | B |
| Aim assist, manual speed | T, mouse wheel / = - | Y, D-pad up/down |
| HUMAN PLAYER enters NECTAR | H | A |
| Cycle camera / flip third-person 180° | C / G | R3 / L3 |
| Pause / reset robot | Esc, P / Backspace (F5 field) | Start / Back |

* **Camera-relative driving** (default): forward on the stick moves the robot *up the screen* in every
  camera. Tank robots turn toward the stick direction (or reverse if quicker); mecanum robots strafe.
  Turn it off for classic robot-relative arcade.
* **Fire** has no separate aim button: turret robots slew the turret, fixed launchers turn the
  whole robot, and the element is released only when locked (green trajectory = locked).
* **Cameras:** Driver Station, **Third Person** (behind the robot at a fixed angle that does not swing
  when the robot turns - Flip/L3 looks the other way), Chase, Overhead, Audience.

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

## What is modelled from the manual

* FIELD 144 x 144 in, 36 tiles, ALLIANCE AREAS 97 x 54 in, LOADING ZONES 23 x 11 in, GARDENS 23 x 2 in (9.2, 9.3, Fig 9-2/9-3).
* HIVE Structure: frame 49.46 x 38.95 in, pivots at 43.95 in, two bi-stable HIVES 25.5 in apart,
  CELLS 20 x 14 in opening, 12 in deep, 18.84 in apart, 30° tilt, opening 53.5-65.6 in above the tiles,
  AprilTag clusters under each CELL (9.6, 9.9, Fig 9-8 to 9-11).
* FLOWERS on the perimeter: 4 in top ring at 21.5 in, 1.25 in backstop, 3.55 in retrieval opening,
  0.43 in lower ring with 2.79 in hole, POLLEN falls through the middle ring but NECTAR does not (9.7, Fig 9-12).
* POLLEN 2.8 in (40), NECTAR 3.6 in (8 per alliance), staging per 10.3.1.
* Scoring per Table 10-2, RP thresholds Table 10-3, audio cues Table 9-1, G402 / G407 / G408 / G410 / G426 / G427.

## Robots

| Robot | Source |
|---|---|
| goBILDA StarterBot | goBILDA FTC StarterBot Resource Guide 2026-2027 (assembly instructions 3200-2627-0003, STEP) |
| REV DUO FTC Starter Bot | docs.revrobotics.com/ftc-kickoff-concepts (Build Guide PDF, Onshape) |
| AndyMark Robits StarterBot | andymark.com 2026-2027 Robits StarterBot (Assembly Guide, Robot Manual, STEP) |
| Studica Starter Bot | studica.com FTC Starter Bot Resource Guide (2026-27 Build Guide, STEP) - pre-kickoff design, no launcher |
| goBILDA StarterBot (Mecanum) | goBILDA mecanum StarterBot resource guide |
| AndyMark Robits StarterBot (Mecanum) | AndyMark "Robits BIOBUZZ Robot (Mecanum).STEP" |
| **Turret Bot** (concept) | Inspired by FRC 1690 Orbit "KEPLER" (2026 REBUILT): mecanum, roller-floor intake, 360° turret, adjustable hood, shoot-on-the-move |
| **Twin Cannon Bot** (concept) | Inspired by FRC 5614 Team Sycamore "Scyther" (2026 REBUILT): two side-by-side flywheel cannons, 2 elements per volley |

Starter bots are simplified primitive models: footprint, wheel types, mechanism layout, motor ratios and
capacity follow the guides; launch angle / exit point are estimated from the renders. The two concept
robots are FTC-sized designs inspired by those FRC robots, not published FTC builds.

## Assumptions (not in the manual)

* Perimeter wall height 12 in (AndyMark am-0481 kit).
* How many elements tip a HIVE ("enough") - adjustable in the menu, default 8 (POLLEN 1, NECTAR 1.6).
* Element masses, the FLOWER middle-ring hole (3.2 in), robot mass and speeds.

## Self test

`BioBuzzSim.exe -autotest <dir> [-timescale 3] [-r1 0 -r2 1 -b1 2 -b2 3] [-human 1] [-flow 1] [-controls 1] [-planner 1 -probot 6]`
plays a match / planner scenario, logs shots, tips, tracking error and scores, and saves screenshots.
