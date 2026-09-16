using System.Collections.Generic;
using UnityEngine;

// Executes an AutoPlan on a physics robot. Drive runs are re-planned from the robot's actual pose
// (so errors do not accumulate) and tracked with feedforward + feedback (RAMSETE for tank drives).
public class PlanController : IRobotController
{
    readonly AutoPlan plan;
    readonly List<object> tasks = new List<object>();   // List<PlanWaypoint> (drive run) or PlanStep
    int index = -1;
    float taskTime;
    public float elapsed;
    Trajectory traj;
    GameElement collectTarget;
    int heldAtStart;
    Flower flower;
    bool built;

    public string CurrentLabel { get; private set; } = "";
    public bool Finished => built && index >= tasks.Count;

    public PlanController(AutoPlan plan) { this.plan = plan; }

    void Build()
    {
        built = true;
        foreach (var st in plan.waypoints[0].steps) tasks.Add(st);
        var runs = PlanCompiler.Runs(plan, out var ends);
        for (int r = 0; r < runs.Count; r++)
        {
            tasks.Add(runs[r]);
            foreach (var st in plan.waypoints[ends[r]].steps) tasks.Add(st);
        }
    }

    public RobotCommand Tick(Robot r)
    {
        if (!built) Build();
        float dt = Time.fixedDeltaTime;
        elapsed += dt;
        taskTime += dt;
        r.aiTarget = null;
        r.targetFlower = false;
        var c = new RobotCommand();

        if (index < 0 || TaskDone(r)) Advance(r);
        if (index < tasks.Count)
        {
            if (tasks[index] is List<PlanWaypoint>) c = Follow(r, traj, taskTime);
            else c = RunStep(r, (PlanStep)tasks[index]);
        }

        // parallel timeline markers
        foreach (var m in plan.markers)
        {
            if (elapsed < m.time || elapsed > m.time + m.duration) continue;
            switch (m.type)
            {
                case MarkerType.Intake: c.intake = true; break;
                case MarkerType.Outtake: c.outtake = true; break;
                case MarkerType.FlowerMech: c.flower = true; break;
                case MarkerType.ShootOnMove: c.shoot = true; break;
            }
        }
        // a fixed launcher firing from a marker must not steal the steering while driving
        if (c.shoot && !r.spec.turret && index < tasks.Count && tasks[index] is List<PlanWaypoint>) r.suppressAimTurn = true;
        return c;
    }

    void Advance(Robot r)
    {
        index++;
        taskTime = 0;
        traj = null;
        collectTarget = null;
        flower = null;
        if (index >= tasks.Count) { CurrentLabel = "Done"; return; }
        Vector2 pos = new Vector2(r.transform.position.x, r.transform.position.z);
        float heading = r.transform.eulerAngles.y;
        heldAtStart = r.held.Count;
        if (tasks[index] is List<PlanWaypoint> run)
        {
            traj = PathPlanner.PlanRun(pos, heading, run, r.spec, plan.accelScale);
            CurrentLabel = "Drive";
            return;
        }
        var st = (PlanStep)tasks[index];
        CurrentLabel = st.Label;
        switch (st.type)
        {
            case StepType.CollectNearest:
                collectTarget = Vision(r);
                if (collectTarget != null)
                {
                    Vector2 ball = new Vector2(collectTarget.transform.position.x, collectTarget.transform.position.z);
                    Vector2 app = PlanCompiler.IntakeApproach(r.spec, ball, pos, out float h);
                    traj = PathPlanner.PlanTo(pos, heading, app, h, r.spec, plan.accelScale, 1.0f);
                }
                break;
            case StepType.ScoreFlower:
                {
                    int f = st.flower;
                    if (f < 0)
                    {
                        float bd = float.MaxValue;
                        foreach (var fl in Flower.All) { float d = fl.HorizDist(r.transform.position); if (d < bd) { bd = d; f = fl.index; } }
                    }
                    foreach (var fl in Flower.All) if (fl.index == f) flower = fl;
                    Vector2 spot = PlanCompiler.FlowerSpot(r.spec, f, out float h);
                    if (r.spec.turret) h = heading;
                    traj = PathPlanner.PlanTo(pos, heading, spot, h, r.spec, plan.accelScale, 1.2f);
                }
                break;
            case StepType.Park:
                {
                    Vector2 spot = PlanCompiler.ParkSpot(r.spec, r.alliance, out float h);
                    traj = PathPlanner.PlanTo(pos, heading, spot, h, r.spec, plan.accelScale, 1.4f);
                }
                break;
        }
    }

    // Simulated camera: nearest element in the field of view, otherwise nearest anywhere.
    GameElement Vision(Robot r)
    {
        GameElement best = null, bestAny = null;
        float bd = float.MaxValue, bdAny = float.MaxValue;
        Vector3 fwd = r.transform.forward;
        foreach (var e in GameElement.All)
        {
            if (!e.IsFree) continue;
            Vector3 p = e.transform.position;
            if (p.y > 0.1f) continue;
            if (e.type == ElementType.Nectar && e.color != r.alliance) continue;
            if (Mathf.Abs(p.x) > Dims.Half - 0.04f || Mathf.Abs(p.z) > Dims.Half - 0.04f) continue;
            bool inFlower = false;
            foreach (var f in Flower.All) if (f.HorizDist(p) < 0.1f) inFlower = true;
            if (inFlower) continue;
            Vector3 to = Util.Flat(p - r.transform.position);
            if (!PlanCompiler.Reachable(r.spec, new Vector2(p.x, p.z), new Vector2(r.transform.position.x, r.transform.position.z))) continue;
            if (p.x * r.alliance.Sign() < -0.05f && MatchManager.I != null && MatchManager.I.period == Period.Auto) continue;
            float d = to.magnitude;
            if (d > PlanCompiler.VisionRange) continue;
            if (d < bdAny) { bdAny = d; bestAny = e; }
            if (Vector3.Angle(fwd, to) <= PlanCompiler.VisionFov && d <= PlanCompiler.VisionRange && d < bd) { bd = d; best = e; }
        }
        return best ?? bestAny;
    }

    bool TaskDone(Robot r)
    {
        if (index >= tasks.Count) return false;
        if (tasks[index] is List<PlanWaypoint>)
        {
            if (traj == null) return true;
            var end = traj.End;
            float posErr = Vector2.Distance(end.p, new Vector2(r.transform.position.x, r.transform.position.z));
            float hErr = Mathf.Abs(Mathf.DeltaAngle(r.transform.eulerAngles.y, end.heading));
            return (taskTime >= traj.Duration && posErr < 0.05f && hErr < 6f) || taskTime > traj.Duration + 1.2f;
        }
        var st = (PlanStep)tasks[index];
        switch (st.type)
        {
            case StepType.CollectNearest:
                return r.held.Count > heldAtStart || r.held.Count >= Robot.Capacity || taskTime >= st.duration || collectTarget == null;
            case StepType.ScoreFlower:
                return traj != null && taskTime > traj.Duration + 0.4f + st.duration;
            case StepType.Park:
                return traj == null || taskTime > traj.Duration + 0.3f;
            default:
                return taskTime >= st.duration;
        }
    }

    RobotCommand RunStep(Robot r, PlanStep st)
    {
        var c = new RobotCommand();
        switch (st.type)
        {
            case StepType.Shoot:
                c.shoot = true;       // the robot aims (turret or body) and fires when locked
                break;
            case StepType.Outtake:
                c.outtake = true;
                break;
            case StepType.CollectNearest:
                c.intake = true;
                if (collectTarget != null && !collectTarget.IsFree) { collectTarget = Vision(r); traj = null; }
                if (collectTarget == null) { c.turn = 0.5f; break; }
                if (traj != null && taskTime < traj.Duration) c = Follow(r, traj, taskTime);
                else
                {
                    // final approach straight at the ball
                    Vector3 to = Util.Flat(collectTarget.transform.position - r.transform.position);
                    float err = Vector3.SignedAngle(r.transform.forward, to, Vector3.up);
                    c.turn = Mathf.Clamp(err / 25f, -1, 1);
                    c.forward = Mathf.Clamp01(1f - Mathf.Abs(err) / 40f) * 0.45f;
                }
                c.intake = true;
                break;
            case StepType.ScoreFlower:
                if (traj != null && taskTime < traj.Duration) c = Follow(r, traj, taskTime);
                else if (flower != null)
                {
                    r.aiTarget = flower.TopCenter;
                    c.shoot = true;
                }
                break;
            case StepType.Park:
                if (traj != null) c = Follow(r, traj, taskTime);
                break;
        }
        return c;
    }

    // Trajectory tracking.
    public static RobotCommand Follow(Robot r, Trajectory traj, float t)
    {
        var c = new RobotCommand();
        if (traj == null) return c;
        var d = traj.Sample(t);
        Vector2 pos = new Vector2(r.transform.position.x, r.transform.position.z);
        float yaw = r.transform.eulerAngles.y;
        Vector2 err = d.p - pos;
        float maxTurnRad = r.spec.maxTurnDeg * Mathf.Deg2Rad;
        float hErrDeg = Mathf.DeltaAngle(yaw, d.heading);

        if (r.spec.drive == DriveType.Mecanum)
        {
            Vector2 vw = d.v + err * 3.5f;
            Vector3 local = r.transform.InverseTransformDirection(new Vector3(vw.x, 0, vw.y));
            c.forward = Mathf.Clamp(local.z / r.spec.maxSpeed, -1, 1);
            c.strafe = Mathf.Clamp(local.x / (r.spec.maxSpeed * 0.85f), -1, 1);
            c.turn = Mathf.Clamp(d.omega / r.spec.maxTurnDeg + hErrDeg / 35f, -1, 1);
            return c;
        }

        // RAMSETE (b = 2, zeta = 0.7), with yaw measured clockwise from +Z
        Vector2 fwd = new Vector2(Mathf.Sin(yaw * Mathf.Deg2Rad), Mathf.Cos(yaw * Mathf.Deg2Rad));
        Vector2 right = new Vector2(fwd.y, -fwd.x);
        float ex = Vector2.Dot(err, fwd), ey = Vector2.Dot(err, right);
        Vector2 hd = new Vector2(Mathf.Sin(d.heading * Mathf.Deg2Rad), Mathf.Cos(d.heading * Mathf.Deg2Rad));
        float vd = Vector2.Dot(d.v, hd);
        float wd = d.omega * Mathf.Deg2Rad;
        float eth = hErrDeg * Mathf.Deg2Rad;
        const float b = 2f, zeta = 0.7f;
        float k = 2f * zeta * Mathf.Sqrt(wd * wd + b * vd * vd) + 0.8f;
        float sinc = Mathf.Abs(eth) < 1e-3f ? 1f : Mathf.Sin(eth) / eth;
        float v = vd * Mathf.Cos(eth) + k * ex;
        float w = wd + k * eth + b * vd * sinc * ey;
        c.forward = Mathf.Clamp(v / r.spec.maxSpeed, -1, 1);
        c.turn = Mathf.Clamp(w / maxTurnRad, -1, 1);
        return c;
    }
}
