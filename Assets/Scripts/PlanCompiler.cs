using System.Collections.Generic;
using UnityEngine;

public enum SegKind { Drive, Step }

public class PlanSeg
{
    public SegKind kind;
    public float t0, t1;
    public Trajectory traj;          // drive segment, or the approach part of a dynamic step
    public PlanStep step;
    public int waypoint;             // waypoint this segment ends at / belongs to
    public int firstWaypoint;        // drive: first waypoint of the run
    public Vector2 holdPos;
    public float holdHeading, turnFrom, turnTo, turnTime;
    public Vector3 target;           // shoot / collect / flower target
    public string label;
    public bool overTime;            // estimated duration exceeds the step's timeout
    public float Duration => t1 - t0;
}

public class CompiledPlan
{
    public readonly List<PlanSeg> segs = new List<PlanSeg>();
    public readonly List<string> warnings = new List<string>();
    public float duration;

    public PlanSeg SegAt(float t)
    {
        foreach (var s in segs) if (t >= s.t0 && t < s.t1) return s;
        return segs.Count > 0 ? segs[segs.Count - 1] : null;
    }

    public void Eval(float t, out Vector2 pos, out float heading, out PlanSeg seg)
    {
        seg = SegAt(t);
        pos = Vector2.zero; heading = 0;
        if (seg == null) return;
        float lt = Mathf.Clamp(t - seg.t0, 0, seg.Duration);
        if (seg.traj != null && lt <= seg.traj.Duration)
        {
            var p = seg.traj.Sample(lt);
            pos = p.p; heading = p.heading;
            return;
        }
        pos = seg.holdPos;
        float after = lt - (seg.traj != null ? seg.traj.Duration : 0f);
        heading = seg.turnTime > 0 ? Mathf.LerpAngle(seg.turnFrom, seg.turnTo, Mathf.SmoothStep(0, 1, Mathf.Clamp01(after / seg.turnTime))) : seg.holdHeading;
    }
}

// Turns an AutoPlan into a timeline with realistic durations (used for preview, the timeline and markers).
public static class PlanCompiler
{
    public const float VisionFov = 40f;       // +/- degrees (typical FTC webcam / Limelight 3A)
    public const float VisionRange = 2.8f;

    public static List<List<PlanWaypoint>> Runs(AutoPlan plan, out List<int> runEnds)
    {
        var runs = new List<List<PlanWaypoint>>();
        runEnds = new List<int>();
        var cur = new List<PlanWaypoint>();
        for (int i = 1; i < plan.waypoints.Count; i++)
        {
            var w = plan.waypoints[i];
            cur.Add(w);
            bool end = w.stop || w.steps.Count > 0 || i == plan.waypoints.Count - 1;
            if (end) { runs.Add(cur); runEnds.Add(i); cur = new List<PlanWaypoint>(); }
        }
        return runs;
    }

    public static Vector2 IntakeApproach(RobotSpec spec, Vector2 ball, Vector2 from, out float heading)
    {
        Vector2 d = ball - from;
        heading = d.sqrMagnitude > 1e-6f ? Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg : 0f;
        float reach = spec.intakeCenter.z * Dims.IN;
        return ball - d.normalized * reach;
    }

    // The intake can get to the ball: the approach pose is collision free.
    public static bool Reachable(RobotSpec spec, Vector2 ball, Vector2 from)
    {
        Vector2 app = IntakeApproach(spec, ball, from, out _);
        return PathPlanner.Free(app, spec) || PathPlanner.Free(ball - (ball - from).normalized * (spec.lengthIn * 0.5f * Dims.IN + 0.05f), spec);
    }

    // Distance from the launch point to the up CELL, and whether that is a good shot (fixed or turret launcher).
    public static bool ShotQuality(RobotSpec spec, Vector2 pos, Vector3 aim, Vector3 outward, out float launchDist)
    {
        float offset = spec.turret ? 0f : Mathf.Abs(spec.launchPoint.z) * Dims.IN;
        Vector2 to = new Vector2(aim.x, aim.z) - pos;
        launchDist = to.magnitude - offset;
        bool facing = Vector2.Dot(to.normalized, -new Vector2(outward.x, outward.z)) > 0.5f;
        float min = spec.launcher == LauncherType.Catapult ? 0.85f : 1.1f;
        float max = spec.launcher == LauncherType.Catapult ? 1.25f : 2.1f;
        return facing && launchDist >= min && launchDist <= max;
    }

    static RobotSpec spec;

    public static int PickVisionBall(Vector2 pos, float heading, List<Vector3> balls, HashSet<int> taken, Alliance a, out bool inView)
    {
        int best = -1, bestAny = -1;
        float bd = float.MaxValue, bdAny = float.MaxValue;
        Vector2 fwd = new Vector2(Mathf.Sin(heading * Mathf.Deg2Rad), Mathf.Cos(heading * Mathf.Deg2Rad));
        for (int i = 0; i < balls.Count; i++)
        {
            if (taken.Contains(i)) continue;
            Vector2 p = new Vector2(balls[i].x, balls[i].z);
            if (spec != null && !Reachable(spec, p, pos)) continue;
            if (p.x * a.Sign() < -0.05f) continue;                 // opponent side: G402 in AUTO
            float d = Vector2.Distance(p, pos);
            if (d > VisionRange) continue;                          // camera range (scanning covers all angles)
            if (d < bdAny) { bdAny = d; bestAny = i; }
            float ang = Mathf.Abs(Vector2.Angle(fwd, p - pos));
            if (ang <= VisionFov && d <= VisionRange && d < bd) { bd = d; best = i; }
        }
        inView = best >= 0;
        return best >= 0 ? best : bestAny;
    }

    public static Vector2 FlowerSpot(RobotSpec spec, int flower, out float heading)
    {
        var fp = Field.FlowerPos(flower, out var inward);
        float dist = spec.launcher == LauncherType.Catapult ? 0.95f : 0.85f;
        Vector2 spot = new Vector2(fp.x + inward.x * dist, fp.z + inward.z * dist);
        heading = Mathf.Atan2(-inward.x, -inward.z) * Mathf.Rad2Deg + (spec.shootsBackward ? 180f : 0f);
        return spot;
    }

    public static Vector2 ParkSpot(RobotSpec spec, Alliance a, out float heading)
    {
        var z = Field.LoadingOf(a).Center;
        float s = a.Sign();
        heading = s < 0 ? 90f : 270f;
        return new Vector2(s * (Dims.Half - spec.lengthIn * 0.5f * Dims.IN - 0.03f), z.z);
    }

    static float TurnTime(float from, float to, RobotSpec spec)
    {
        float ang = Mathf.Abs(Mathf.DeltaAngle(from, to));
        return ang < 2f ? 0f : ang / (spec.maxTurnDeg * 0.6f) + 0.15f;
    }

    public static float FixedLauncherYaw(RobotSpec spec, Vector2 pos, Vector3 target)
    {
        Vector2 d = new Vector2(target.x, target.z) - pos;
        return Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg + (spec.shootsBackward ? 180f : 0f);
    }

    public static CompiledPlan Compile(AutoPlan plan, List<Vector3> balls)
    {
        var cp = new CompiledPlan();
        if (plan.waypoints.Count == 0) return cp;
        spec = plan.Spec;
        var taken = new HashSet<int>();
        var w0 = plan.waypoints[0];
        Vector2 pos = new Vector2(w0.x, w0.z);
        float heading = w0.heading;
        float t = 0;
        Vector3 hiveAim = Field.HiveOf(plan.alliance) ? Field.HiveOf(plan.alliance).AimPoint() : Vector3.zero;
        int held = Robot.Capacity;

        void AddSteps(PlanWaypoint w, int wi)
        {
            foreach (var st in w.steps)
            {
                var seg = new PlanSeg { kind = SegKind.Step, step = st, waypoint = wi, t0 = t, label = st.Label };
                switch (st.type)
                {
                    case StepType.Shoot:
                        seg.target = hiveAim;
                        if (!spec.turret && spec.launcher != LauncherType.None)
                        {
                            seg.turnFrom = heading;
                            seg.turnTo = FixedLauncherYaw(spec, pos, hiveAim);
                            seg.turnTime = TurnTime(heading, seg.turnTo, spec);
                            heading = seg.turnTo;
                        }
                        seg.t1 = t + Mathf.Max(st.duration, seg.turnTime + 0.25f);
                        if (spec.launcher != LauncherType.None && Field.HiveOf(plan.alliance) &&
                            !ShotQuality(spec, pos, hiveAim, Field.HiveOf(plan.alliance).OpeningDir(), out float ld))
                            cp.warnings.Add($"WP{wi}: shooting from {ld:0.00} m, not in front of the up CELL's opening - use the green shot zone ({(spec.launcher == LauncherType.Catapult ? "0.85-1.25" : "1.1-2.1")} m)");
                        if (spec.launcher == LauncherType.None) cp.warnings.Add($"WP{wi}: {spec.name} has no launcher - Shoot does nothing");
                        held = 0;
                        break;
                    case StepType.CollectNearest:
                        {
                            int bi = PickVisionBall(pos, heading, balls, taken, plan.alliance, out bool inView);
                            float scan = inView ? 0f : 0.8f;   // rotate to find something
                            if (bi >= 0)
                            {
                                taken.Add(bi);
                                Vector2 ball = new Vector2(balls[bi].x, balls[bi].z);
                                Vector2 app = IntakeApproach(spec, ball, pos, out float h);
                                seg.traj = PathPlanner.PlanTo(pos, heading, app, h, spec, plan.accelScale, 1.0f);
                                seg.target = balls[bi];
                                pos = app + (ball - app).normalized * 0.05f;
                                heading = h;
                                float est = scan + seg.traj.Duration + 0.35f;
                                seg.overTime = est > st.duration;
                                seg.t1 = t + Mathf.Min(est, Mathf.Max(st.duration, 0.5f));
                                held = Mathf.Min(Robot.Capacity, held + 1);
                            }
                            else seg.t1 = t + st.duration;
                            if (!inView) seg.label += " (nothing in camera view: scan)";
                        }
                        break;
                    case StepType.ScoreFlower:
                        {
                            int f = st.flower;
                            if (f < 0)
                            {
                                float bd = float.MaxValue;
                                for (int i = 0; i < 4; i++) { var fp = Field.FlowerPos(i, out _); float d = Vector2.Distance(new Vector2(fp.x, fp.z), pos); if (d < bd) { bd = d; f = i; } }
                            }
                            Vector2 spot = FlowerSpot(spec, f, out float h);
                            if (spec.turret) h = heading;
                            seg.traj = PathPlanner.PlanTo(pos, heading, spot, h, spec, plan.accelScale, 1.2f);
                            var fpos = Field.FlowerPos(f, out _);
                            seg.target = fpos + Vector3.up * Dims.FlowerTopHeight;
                            pos = spot; heading = h;
                            seg.holdPos = pos; seg.holdHeading = h;
                            seg.t1 = t + seg.traj.Duration + 0.4f + st.duration;
                            cp.warnings.Add($"WP{wi}: G410 - NECTAR in a FLOWER before 1:00 is a MAJOR FOUL; in AUTO only POLLEN may be placed");
                            held = 0;
                        }
                        break;
                    case StepType.Park:
                        {
                            Vector2 spot = ParkSpot(spec, plan.alliance, out float h);
                            seg.traj = PathPlanner.PlanTo(pos, heading, spot, h, spec, plan.accelScale, 1.4f);
                            pos = spot; heading = h;
                            seg.t1 = t + seg.traj.Duration;
                        }
                        break;
                    default:
                        seg.t1 = t + st.duration;
                        break;
                }
                seg.holdPos = pos;
                seg.holdHeading = heading;
                cp.segs.Add(seg);
                t = seg.t1;
            }
        }

        AddSteps(w0, 0);
        var runs = Runs(plan, out var ends);
        for (int r = 0; r < runs.Count; r++)
        {
            var traj = PathPlanner.PlanRun(pos, heading, runs[r], spec, plan.accelScale);
            var seg = new PlanSeg
            {
                kind = SegKind.Drive, traj = traj, t0 = t, t1 = t + Mathf.Max(0.05f, traj.Duration),
                waypoint = ends[r], firstWaypoint = ends[r] - runs[r].Count + 1,
            };
            seg.label = runs[r].Count > 1 ? $"Drive WP{seg.firstWaypoint}-{seg.waypoint}" : $"Drive to WP{seg.waypoint}";
            var last = runs[r][runs[r].Count - 1];
            pos = new Vector2(last.x, last.z);
            heading = last.heading;
            seg.holdPos = pos; seg.holdHeading = heading;
            cp.segs.Add(seg);
            t = seg.t1;
            // G402 risk: robot centre crosses into the opposing half during AUTO
            foreach (var p in traj.pts)
                if (p.p.x * plan.alliance.Sign() < -(spec.lengthIn * 0.5f * Dims.IN + 0.05f))
                { cp.warnings.Add($"{seg.label}: crosses into the opposing side (G402 risk in AUTO)"); break; }
            AddSteps(plan.waypoints[ends[r]], ends[r]);
        }
        cp.duration = t;

        // start pose checks (G304)
        var sp = new Vector2(w0.x, w0.z);
        float hl = spec.lengthIn * 0.5f * Dims.IN, hw = spec.widthIn * 0.5f * Dims.IN;
        Vector2 f2 = new Vector2(Mathf.Sin(w0.heading * Mathf.Deg2Rad), Mathf.Cos(w0.heading * Mathf.Deg2Rad));
        Vector2 r2 = new Vector2(f2.y, -f2.x);
        float maxExt = 0;
        bool ownSide = true;
        foreach (var c in new[] { sp + f2 * hl + r2 * hw, sp + f2 * hl - r2 * hw, sp - f2 * hl + r2 * hw, sp - f2 * hl - r2 * hw })
        {
            maxExt = Mathf.Max(maxExt, Mathf.Max(Mathf.Abs(c.x), Mathf.Abs(c.y)));
            if (c.x * plan.alliance.Sign() < 0) ownSide = false;
        }
        if (maxExt < Dims.Half - 0.02f) cp.warnings.Add("Start: robot must touch the FIELD perimeter (G304.C)");
        if (maxExt > Dims.Half + 0.01f) cp.warnings.Add("Start: robot overlaps the perimeter");
        if (!ownSide) cp.warnings.Add("Start: robot must be fully on its own ALLIANCE side (G304.A)");
        if (Field.LoadingOf(plan.alliance).ContainsPartially(new Vector3(sp.x, 0, sp.y), hw)) cp.warnings.Add("Start: robot may not start in the LOADING ZONE (G304.E)");
        if (cp.duration > Dims.AutoTime) cp.warnings.Add($"Plan takes {cp.duration:0.0} s - AUTO is {Dims.AutoTime:0} s; the rest will not run");
        foreach (var m in plan.markers)
            if (m.type == MarkerType.ShootOnMove && !spec.turret) { cp.warnings.Add("Fire-on-the-move markers only fire when a fixed launcher happens to be aimed; use a turret robot"); break; }
        foreach (var s in cp.segs) if (s.overTime) cp.warnings.Add($"{s.label}: estimated time exceeds its timeout");
        return cp;
    }
}
