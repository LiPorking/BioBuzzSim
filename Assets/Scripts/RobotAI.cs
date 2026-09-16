using System.Collections.Generic;
using UnityEngine;

// Simple autonomous behaviour used for AI partners/opponents and for every robot's AUTO OpMode.
public class RobotAI : IRobotController
{
    enum State { Collect, Shoot, Flower, Park, Garden }

    static readonly Dictionary<GameElement, Robot> claims = new Dictionary<GameElement, Robot>();

    readonly bool autoMode;
    State state;
    GameElement target;
    Flower flowerTarget;
    float stateTime, stuckTime, lastProgress;
    Vector3 lastPos;
    float lateral;
    float targetSince;
    readonly Dictionary<GameElement, float> blacklist = new Dictionary<GameElement, float>();

    public RobotAI(bool autoMode, int seed)
    {
        this.autoMode = autoMode;
        lateral = ((seed * 37) % 5 - 2) * 0.12f;
    }

    public static void ClearClaims() => claims.Clear();

    float recoverUntil, blockedSince = -1;
    RobotCommand recoverCmd;

    public RobotCommand Tick(Robot r)
    {
        var c = Think(r);
        // Universal un-stick: commanded to move but not moving (e.g. a ball wedged against the HIVE frame).
        if (Time.time < recoverUntil) return recoverCmd;
        bool wantsMove = Mathf.Abs(c.forward) > 0.3f || Mathf.Abs(c.strafe) > 0.3f || Mathf.Abs(c.turn) > 0.5f;
        bool moving = Util.Flat(r.rb.velocity).magnitude > 0.05f || Mathf.Abs(r.rb.angularVelocity.y) > 0.35f;
        if (wantsMove && !moving)
        {
            if (blockedSince < 0) blockedSince = Time.time;
            if (Time.time - blockedSince > 0.8f)
            {
                blockedSince = -1;
                recoverUntil = Time.time + 0.55f;
                recoverCmd = new RobotCommand { forward = FreeDirection(r, c.forward), strafe = -c.strafe * 0.6f, turn = c.turn > 0 ? -0.5f : 0.5f };
                return recoverCmd;
            }
        }
        else blockedSince = -1;
        return c;
    }

    // Back away in whichever direction (robot forward/back) has room.
    static float FreeDirection(Robot r, float wanted)
    {
        int mask = (1 << Layers.Field) | (1 << Layers.Robot);
        Vector3 half = new Vector3(r.HalfWidth * 0.9f, 0.05f, r.HalfLength * 0.9f);
        Vector3 basePos = r.transform.position + Vector3.up * 0.08f;
        bool frontFree = !Blocked(r, basePos + r.transform.forward * 0.18f, half, mask);
        bool backFree = !Blocked(r, basePos - r.transform.forward * 0.18f, half, mask);
        if (frontFree && !backFree) return 0.8f;
        if (backFree && !frontFree) return -0.8f;
        return wanted > 0.1f ? -0.8f : 0.8f;
    }

    static bool Blocked(Robot r, Vector3 center, Vector3 half, int mask)
    {
        var hits = Physics.OverlapBox(center, half, r.transform.rotation, mask, QueryTriggerInteraction.Ignore);
        foreach (var h in hits) if (!r.Colliders.Contains(h) && h.name != "TileSurface" && h.name != "VenueFloor") return true;
        return false;
    }

    RobotCommand Think(Robot r)
    {
        var mm = MatchManager.I;
        stateTime += Time.fixedDeltaTime;
        float timeLeft = mm != null ? mm.MatchTimeLeft : 999f;
        r.aiTarget = null;

        // Stuck detection
        if ((r.transform.position - lastPos).sqrMagnitude > 0.0004f) { lastPos = r.transform.position; lastProgress = Time.time; }

        // Pick state
        State want = state;
        float parkTime = 2.5f + Vector3.Distance(r.transform.position, ParkPoint(r)) / Mathf.Max(0.3f, r.spec.maxSpeed) * 1.4f;
        if (!autoMode && mm != null && mm.period == Period.Teleop && timeLeft < parkTime) want = State.Park;
        else if (r.held.Count >= Robot.Capacity || (r.held.Count > 0 && (state == State.Shoot || state == State.Garden))) want = r.HasLauncher ? State.Shoot : State.Garden;
        else if (autoMode && mm != null && mm.period == Period.Auto && timeLeft - Dims.TeleopTime < 3f && r.HasLauncher && r.held.Count > 0) want = State.Shoot;
        else want = State.Collect;
        if (want != state) { state = want; stateTime = 0; Release(r); }

        switch (state)
        {
            case State.Park: return GoTo(r, ParkPoint(r), null, 0.03f);
            case State.Shoot: return DoShoot(r, mm);
            case State.Garden: return DoGarden(r);
            default: return DoCollect(r, mm);
        }
    }

    void Release(Robot r)
    {
        if (target != null && claims.TryGetValue(target, out var who) && who == r) claims.Remove(target);
        target = null;
        flowerTarget = null;
    }

    Vector3 ParkPoint(Robot r)
    {
        var z = Field.LoadingOf(r.alliance).Center;
        float s = r.alliance.Sign();
        z.x = s * (Dims.Half - r.HalfLength - 0.02f);
        z.z += (r.station == 1 ? -1 : 1) * 0.12f;
        return z;
    }

    bool OwnSide(Robot r, Vector3 p) => r.alliance == Alliance.Red ? p.x < -0.08f : p.x > 0.08f;

    RobotCommand DoCollect(Robot r, MatchManager mm)
    {
        if (target != null && Time.time - targetSince > 6f)
        {
            blacklist[target] = Time.time + 12f;   // could not get it: try something else for a while
            Release(r);
        }
        if (target != null && (!target.IsFree || target.transform.position.y > 0.12f || Time.time - lastProgress > 2.5f && stateTime > 3f))
        {
            Release(r);
            lastProgress = Time.time;
        }
        if (target == null && flowerTarget == null) PickTarget(r, mm);

        if (target != null)
        {
            Vector3 p = target.transform.position;
            Vector3 face = p - r.transform.position;
            var c = GoTo(r, p - Util.Flat(face).normalized * (r.HalfLength * 0.6f), p, 0.0f);
            c.intake = true;
            return c;
        }
        if (flowerTarget != null)
        {
            var bp = flowerTarget.BottomPollen();
            if (bp == null || stateTime > 9f) { Release(r); return default; }
            // Pose so the FLOWER mechanism (or intake) lines up with the Retrieval Opening.
            Vector3 mech = r.spec.flowerMech == FlowerMechType.Intake ? r.spec.intakeCenter : r.spec.flowerMechPoint;
            Vector3 md = Util.Flat(mech).normalized;
            float yaw = Mathf.Atan2(-flowerTarget.inward.x, -flowerTarget.inward.z) * Mathf.Rad2Deg - Mathf.Atan2(md.x, md.z) * Mathf.Rad2Deg;
            Quaternion q = Quaternion.Euler(0, yaw, 0);
            Vector3 pos = bp.transform.position - q * (Util.Flat(mech) * Dims.IN) + flowerTarget.inward * 0.02f;
            float err = Mathf.Abs(Mathf.DeltaAngle(r.transform.eulerAngles.y, yaw));
            var c = GoToPose(r, pos, yaw);
            bool close = Util.Flat(pos - r.transform.position).magnitude < 0.12f && err < 15f;
            c.intake = true;
            c.flower = close;
            return c;
        }
        // Nothing to do: hold a central spot on our side.
        return GoTo(r, new Vector3(r.alliance.Sign() * 0.9f, 0, 0), null, 0.1f);
    }

    void PickTarget(Robot r, MatchManager mm)
    {
        bool auto = mm != null && mm.period == Period.Auto;
        GameElement best = null;
        float bestScore = float.MaxValue;
        foreach (var e in GameElement.All)
        {
            if (!e.IsFree) continue;
            Vector3 p = e.transform.position;
            if (p.y > 0.1f || e.rb.velocity.sqrMagnitude > 1f) continue;
            if (e.type == ElementType.Nectar && e.color != r.alliance) continue;
            if (Mathf.Abs(p.x) > Dims.Half - 0.03f || Mathf.Abs(p.z) > Dims.Half - 0.03f) continue;
            bool nearFlower = false;
            foreach (var f in Flower.All) if (f.HorizDist(p) < 0.1f) nearFlower = true;
            if (nearFlower) continue;
            if (claims.TryGetValue(e, out var who) && who != r && who != null) continue;
            if (blacklist.TryGetValue(e, out var until) && Time.time < until) continue;
            if (!PlanCompiler.Reachable(r.spec, new Vector2(p.x, p.z), new Vector2(r.transform.position.x, r.transform.position.z))) continue;
            if (auto && !OwnSide(r, p)) continue;
            if (Field.GardenOf(r.alliance).ContainsPartially(p, e.Radius) && (!r.HasLauncher || (mm != null && mm.MatchTimeLeft < 40f))) continue;
            // under the HIVE frame is awkward to reach
            if (Mathf.Abs(Mathf.Abs(p.x) - Dims.FrameWidth * 0.5f) < 0.12f && Mathf.Abs(p.z) < Dims.FrameDepth * 0.5f + 0.05f) continue;
            float score = Vector3.Distance(p, r.transform.position) + (e.type == ElementType.Nectar ? -0.3f : 0f);
            if (score < bestScore) { bestScore = score; best = e; }
        }
        if (best != null && bestScore < 3.5f)
        {
            target = best;
            targetSince = Time.time;
            claims[best] = r;
            return;
        }
        if (r.spec.flowerMech != FlowerMechType.None)
        {
            float bd = float.MaxValue;
            foreach (var f in Flower.All)
            {
                if (f.BottomPollen() == null) continue;
                if (auto && !OwnSide(r, f.transform.position)) continue;
                float d = Vector3.Distance(f.transform.position, r.transform.position);
                if (d < bd) { bd = d; flowerTarget = f; }
            }
        }
    }

    RobotCommand DoShoot(Robot r, MatchManager mm)
    {
        var hive = Field.HiveOf(r.alliance);
        Vector3 aim = hive.AimPoint();
        bool nectarTime = mm != null && (mm.LastMinute) && r.held.Count > 0 && r.held[0].type == ElementType.Nectar && r.spec.launcher == LauncherType.Flywheel;
        Flower f = nectarTime ? BestFlowerFor(r) : null;
        if (f != null) aim = f.TopCenter;

        float dist;   // desired horizontal distance from the LAUNCH POINT to the target
        if (r.spec.launcher == LauncherType.Catapult)
        {
            float lh = r.spec.launchPoint.y * Dims.IN;
            dist = Util.RangeFor(r.spec.launchFixed, aim.y - lh, r.spec.launchAngle * Mathf.Deg2Rad, false);
            if (float.IsNaN(dist)) dist = 1.1f;
        }
        else dist = f != null ? 0.75f : 1.45f;

        Vector3 outward = f != null ? f.inward : hive.OpeningDir();
        if (Time.time > spotUntil || Vector3.Dot(outward, spotOutward) < 0.9f || spotForFlower != (f != null))
        {
            spot = ChooseSpot(r, aim, outward, dist, f != null);
            spotOutward = outward;
            spotForFlower = f != null;
            spotUntil = Time.time + 4f;
        }

        r.aiTarget = aim;
        float yaw = r.YawToward(aim);
        float offset = r.spec.turret ? 0f : Mathf.Abs(r.spec.launchPoint.z) * Dims.IN;
        // distance/direction as they will be once the robot has turned to face the target
        Vector3 fromCenter = Util.Flat(aim - r.transform.position);
        float shotDist = fromCenter.magnitude - offset;
        bool facingOpening = f != null || Vector3.Dot(fromCenter.normalized, -outward) > 0.5f;
        bool rangeOk;
        if (r.spec.launcher == LauncherType.Catapult) rangeOk = Mathf.Abs(shotDist - dist) < 0.14f;
        else
        {
            float v = Util.LaunchSpeed(shotDist, aim.y - r.LaunchPoint.y, r.spec.launchAngle * Mathf.Deg2Rad);
            rangeOk = !float.IsNaN(v) && v >= r.spec.launchMin && v <= r.spec.launchMax && shotDist > (f != null ? 0.55f : 1.15f) && shotDist < 2.3f;
            if (r.spec.turret) rangeOk = r.Solve(aim).valid && shotDist > (f != null ? 0.5f : 0.95f) && shotDist < 2.4f;
        }
        bool inPosition = facingOpening && rangeOk;
        Vector3 toSpot = Util.Flat(spot - r.transform.position);
        RobotCommand c;
        if (!inPosition)
        {
            c = toSpot.magnitude > 0.12f ? GoTo(r, spot, null, 0.05f) : GoToPose(r, spot, yaw);
            if (toSpot.magnitude <= 0.12f && Mathf.Abs(r.AlignError(aim)) < 5f) spotUntil = 0f; // at the spot but still invalid: re-plan
        }
        else if (r.spec.turret)
        {
            // turret: keep driving to the spot and fire whenever the turret is locked
            c = toSpot.magnitude > 0.12f ? GoTo(r, spot, null, 0.05f) : new RobotCommand();
            c.shoot = true;
        }
        else
        {
            float err = r.AlignError(aim);
            c = new RobotCommand { turn = Mathf.Clamp(err / 25f, -1, 1) };
            bool rangeFine = true;
            if (r.spec.launcher == LauncherType.Catapult && Mathf.Abs(err) < 8f)
            {
                // fixed-power launcher: creep along the shot line until the range is right
                float rangeErr = shotDist - dist;
                rangeFine = Mathf.Abs(rangeErr) < 0.03f;
                if (!rangeFine) c.forward = Mathf.Clamp(rangeErr * 5f, -0.35f, 0.35f) * (r.spec.shootsBackward ? -1f : 1f);
            }
            c.shoot = rangeFine && Mathf.Abs(err) < 2.5f && r.rb.velocity.magnitude < 0.12f && Mathf.Abs(r.rb.angularVelocity.y) < 0.3f;
        }
        return c;
    }

    Vector3 spot, spotOutward;
    float spotUntil;
    bool spotForFlower;

    Vector3 ChooseSpot(Robot r, Vector3 aim, Vector3 outward, float launchDist, bool flower)
    {
        float offset = r.spec.turret ? 0f : Mathf.Abs(r.spec.launchPoint.z) * Dims.IN;
        float m = Dims.Half - Mathf.Max(r.HalfLength, r.HalfWidth) - 0.06f;
        var mm = MatchManager.I;
        bool auto = autoMode || (mm != null && mm.period == Period.Auto);
        Vector3 best = Util.Flat(aim) + outward * (launchDist + offset);
        float bestScore = float.MaxValue;
        float[] dds = r.spec.launcher == LauncherType.Catapult || flower ? new[] { launchDist } : new[] { launchDist, launchDist - 0.15f, launchDist + 0.2f };
        for (float ang = -55f; ang <= 55.1f; ang += 5f)
        foreach (float dd in dds)
        {
            Vector3 dir = Quaternion.Euler(0, ang, 0) * outward;
            Vector3 p = Util.Flat(aim) + dir * (dd + offset);
            if (Mathf.Abs(p.x) > m || Mathf.Abs(p.z) > m) continue;
            if (auto && !flower && (r.alliance == Alliance.Red ? p.x > -(r.HalfLength + 0.03f) : p.x < r.HalfLength + 0.03f)) continue;
            if (Mathf.Abs(p.x) < Dims.FrameWidth * 0.5f + 0.3f && Mathf.Abs(p.z) < Dims.FrameDepth * 0.5f + 0.3f) continue;
            bool nearFlower = false;
            foreach (var fl in Flower.All) if (Util.Flat(fl.transform.position - p).magnitude < r.HalfLength + 0.15f) nearFlower = true;
            if (nearFlower && !flower) continue;
            float score = Mathf.Abs(ang) * 0.01f + Mathf.Abs(dd - launchDist) * 1.5f + Util.Flat(p - r.transform.position).magnitude * 0.35f
                + (Mathf.Sign(p.x) == r.alliance.Sign() ? 0f : 0.5f) + lateral * Mathf.Sign(ang) * 0.3f;
            if (mm != null) foreach (var o in mm.robots) if (o != r && Util.Flat(o.transform.position - p).magnitude < 0.5f) score += 0.8f;
            if (score < bestScore) { bestScore = score; best = p; }
        }
        return best;
    }

    Flower BestFlowerFor(Robot r)
    {
        Flower best = null;
        float bd = float.MaxValue;
        foreach (var f in Flower.All)
        {
            var res = f.Evaluate();
            float d = Vector3.Distance(f.transform.position, r.transform.position) + (res.owner == r.alliance ? 3f : 0f);
            if (d < bd) { bd = d; best = f; }
        }
        return best;
    }

    // Robots without a launcher push their load into their GARDEN.
    RobotCommand DoGarden(Robot r)
    {
        var g = Field.GardenOf(r.alliance);
        Vector3 wallDir = new Vector3(0, 0, Mathf.Sign(g.Center.z));
        Vector3 spot = g.Center - wallDir * (r.HalfLength + 0.12f);
        float yaw = Mathf.Atan2(wallDir.x, wallDir.z) * Mathf.Rad2Deg;
        var c = GoToPose(r, spot, yaw);
        if (Util.Flat(spot - r.transform.position).magnitude < 0.1f) c.outtake = true;
        return c;
    }

    // ------------------------------------------------------------------ navigation
    // The two frame triangles stand in the planes x = +/- FrameWidth/2, z in +/- FrameDepth/2.
    static Vector3 Waypoint(Robot r, Vector3 pos, Vector3 dest)
    {
        float rad = Mathf.Sqrt(r.HalfLength * r.HalfLength + r.HalfWidth * r.HalfWidth);
        float hd = Dims.FrameDepth * 0.5f;
        foreach (float sx in new[] { -1f, 1f })
        {
            float fx = sx * Dims.FrameWidth * 0.5f;
            Vector2 a = new Vector2(fx, -hd), b = new Vector2(fx, hd);
            Vector2 p = new Vector2(pos.x, pos.z), q = new Vector2(dest.x, dest.z);
            // only a problem if the path actually has to cross the frame plane within the triangle
            bool crosses = (p.x - fx) * (q.x - fx) < 0;
            if (!crosses || SegSegDistance(p, q, a, b) >= rad * 0.8f) continue;
            float side = Mathf.Abs(pos.z) > 0.02f ? Mathf.Sign(pos.z) : Mathf.Sign(dest.z + 0.001f);
            if (Mathf.Abs(dest.z) > hd && Mathf.Sign(dest.z) != side && Mathf.Abs(pos.z) < hd) side = Mathf.Sign(dest.z);
            float zEnd = side * (hd + rad + 0.12f);
            var w = new Vector3(fx, 0, zEnd);
            // once level with the end, continue straight to the destination
            if (Mathf.Abs(pos.z) >= hd + rad * 0.9f && Mathf.Sign(pos.z) == side) continue;
            if (Util.Flat(w - pos).magnitude < 0.12f) continue;
            return w;
        }
        return dest;
    }

    static float SegSegDistance(Vector2 p1, Vector2 q1, Vector2 p2, Vector2 q2)
    {
        if (SegmentsIntersect(p1, q1, p2, q2)) return 0f;
        return Mathf.Min(Mathf.Min(PointSeg(p1, p2, q2), PointSeg(q1, p2, q2)), Mathf.Min(PointSeg(p2, p1, q1), PointSeg(q2, p1, q1)));
    }

    static float PointSeg(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = ab.sqrMagnitude < 1e-8f ? 0 : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
        return Vector2.Distance(p, a + ab * t);
    }

    static bool SegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        float Cross(Vector2 u, Vector2 v) => u.x * v.y - u.y * v.x;
        float d1 = Cross(b - a, c - a), d2 = Cross(b - a, d - a), d3 = Cross(d - c, a - c), d4 = Cross(d - c, b - c);
        return d1 * d2 < 0 && d3 * d4 < 0;
    }

    RobotCommand GoTo(Robot r, Vector3 dest, Vector3? faceAt, float tolerance)
    {
        Vector3 pos = r.transform.position;
        Vector3 wp = Waypoint(r, pos, dest);
        if (wp != dest) { dest = wp; faceAt = null; tolerance = 0.1f; }
        Vector3 to = Util.Flat(dest - pos);
        Vector3 steer = to.normalized * Mathf.Min(1f, to.magnitude / 0.35f) + Avoid(r);
        var c = new RobotCommand();
        if (to.magnitude < tolerance && faceAt == null) return c;

        if (r.spec.drive == DriveType.Mecanum)
        {
            Vector3 local = r.transform.InverseTransformDirection(steer);
            c.forward = Mathf.Clamp(local.z, -1, 1);
            c.strafe = Mathf.Clamp(local.x, -1, 1);
            Vector3 look = faceAt.HasValue ? Util.Flat(faceAt.Value - pos) : to;
            if (look.sqrMagnitude > 0.0001f)
                c.turn = Mathf.Clamp(Vector3.SignedAngle(r.transform.forward, look, Vector3.up) / 30f, -1, 1);
        }
        else
        {
            float ang = Vector3.SignedAngle(r.transform.forward, steer, Vector3.up);
            bool reverse = Mathf.Abs(ang) > 120f && to.magnitude < 0.6f && faceAt == null;
            if (reverse) ang = Mathf.DeltaAngle(0, ang + 180f);
            c.turn = Mathf.Clamp(ang / 35f, -1, 1);
            float drive = Mathf.Clamp01(1f - Mathf.Abs(ang) / 70f) * Mathf.Min(1f, steer.magnitude);
            c.forward = reverse ? -drive : drive;
        }
        if (Time.time - lastProgress > 1.5f && to.magnitude > 0.2f)
        {
            c.forward = -0.6f;
            c.turn = 0.8f;
            if (Time.time - lastProgress > 2.3f) lastProgress = Time.time;
        }
        return c;
    }

    RobotCommand GoToPose(Robot r, Vector3 dest, float yaw)
    {
        Vector3 pos = r.transform.position;
        Vector3 to = Util.Flat(dest - pos);
        float err = Mathf.DeltaAngle(r.transform.eulerAngles.y, yaw);
        var c = new RobotCommand();
        if (r.spec.drive == DriveType.Mecanum)
        {
            Vector3 local = r.transform.InverseTransformDirection(to.normalized * Mathf.Min(1f, to.magnitude / 0.25f) + Avoid(r) * 0.5f);
            c.forward = Mathf.Clamp(local.z, -1, 1);
            c.strafe = Mathf.Clamp(local.x, -1, 1);
            c.turn = Mathf.Clamp(err / 25f, -1, 1);
            return c;
        }
        if (to.magnitude > 0.08f)
        {
            // drive to the spot first (forwards or backwards), then rotate in place
            float ang = Vector3.SignedAngle(r.transform.forward, to, Vector3.up);
            bool reverse = Mathf.Abs(ang) > 90f;
            if (reverse) ang = Mathf.DeltaAngle(0, ang + 180f);
            c.turn = Mathf.Clamp(ang / 30f, -1, 1);
            float drive = Mathf.Clamp01(1f - Mathf.Abs(ang) / 45f) * Mathf.Min(1f, to.magnitude / 0.3f);
            c.forward = reverse ? -drive : drive;
        }
        else c.turn = Mathf.Clamp(err / 25f, -1, 1);
        return c;
    }

    Vector3 Avoid(Robot r)
    {
        Vector3 pos = r.transform.position;
        Vector3 push = Vector3.zero;
        float clearance = r.HalfLength + 0.1f;
        // HIVE frame triangles lie in the planes x = +/- FrameWidth/2
        foreach (float sx in new[] { -1f, 1f })
        {
            float fx = sx * Dims.FrameWidth * 0.5f;
            float dz = Mathf.Max(0, Mathf.Abs(pos.z) - Dims.FrameDepth * 0.5f);
            float dx = pos.x - fx;
            float d = new Vector2(dx, dz).magnitude;
            if (d < clearance) push += new Vector3(dx, 0, Mathf.Sign(pos.z) * dz).normalized * (clearance - d) * 4f;
        }
        foreach (var f in Flower.All)
        {
            Vector3 d = Util.Flat(pos - f.transform.position);
            float c2 = r.HalfLength + 0.06f;
            if (d.magnitude < c2 && (flowerTarget != f)) push += d.normalized * (c2 - d.magnitude) * 3f;
        }
        if (MatchManager.I != null)
            foreach (var o in MatchManager.I.robots)
            {
                if (o == r) continue;
                Vector3 d = Util.Flat(pos - o.transform.position);
                float c2 = r.HalfLength + o.HalfLength + 0.08f;
                if (d.magnitude < c2 && d.sqrMagnitude > 1e-5f) push += d.normalized * (c2 - d.magnitude) * 3f;
            }
        return push;
    }
}
