using System.Collections.Generic;
using UnityEngine;

public struct RobotCommand
{
    public float forward, strafe, turn;   // robot frame, -1..1
    public bool intake, outtake, shoot, flower, feed;
}

public interface IRobotController
{
    RobotCommand Tick(Robot r);
}

public enum RobotMode { Disabled, Auto, Teleop }

public class Robot : MonoBehaviour
{
    public const int Capacity = 4; // G407: no more than 4 at a time

    public RobotSpec spec;
    public RobotParts parts;
    public Alliance alliance;
    public int station;              // 1 or 2
    public string label;
    public RobotMode mode = RobotMode.Disabled;
    public IRobotController controller;      // TELEOP
    public IRobotController autoController;  // AUTO OpMode
    public bool isHuman;
    public int humanSlot;

    public Rigidbody rb;
    public readonly List<Collider> Colliders = new List<Collider>();
    public readonly List<GameElement> held = new List<GameElement>();
    Transform[] slots;

    public RobotCommand cmd;
    public bool targetFlower;
    public Vector3? aiTarget;        // AI can aim at an arbitrary point
    public static System.Action<Robot, GameElement, Vector3, float> OnShot;
    public readonly HashSet<string> contacts = new HashSet<string>();

    public Vector3 startPos;
    public Quaternion startRot;
    public bool leave, autoPark, teleopPark, g402Called;

    float nextIntake, nextShot, nextOut, nextPoke;
    float catapultT = 1f;
    float flowerAnim;
    LineRenderer traj;
    AudioSource motor;
    static readonly Collider[] buf = new Collider[32];

    float IN => Dims.IN;
    public float HalfLength => spec.lengthIn * 0.5f * Dims.IN;
    public float HalfWidth => spec.widthIn * 0.5f * Dims.IN;
    public float turretYaw;          // degrees relative to the robot's forward
    public bool aimLocked, inRange;
    public bool suppressAimTurn;     // set by planners so firing does not steer a moving robot
    public float Yaw => transform.eulerAngles.y;
    public Vector3 TurretPivot => transform.TransformPoint(spec.turretPivot * Dims.IN);
    public Vector3 LaunchPoint => spec.turret
        ? TurretPivot + Quaternion.Euler(0, Yaw + turretYaw, 0) * (spec.launchPoint * Dims.IN)
        : transform.TransformPoint(spec.launchPoint * Dims.IN);
    public Vector3 LaunchFlatDir => spec.turret
        ? Quaternion.Euler(0, Yaw + turretYaw, 0) * Vector3.forward
        : transform.forward * (spec.shootsBackward ? -1f : 1f);
    public bool HasLauncher => spec.launcher != LauncherType.None;

    // ------------------------------------------------------------------ construction
    public static Robot Create(Transform world, RobotSpec spec, Alliance alliance, int station, Vector3 pos, float yaw, string label)
    {
        var go = new GameObject($"ROBOT {label} - {spec.name}");
        go.transform.SetParent(world, false);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.Euler(0, yaw, 0);
        var r = go.AddComponent<Robot>();
        r.spec = spec;
        r.alliance = alliance;
        r.station = station;
        r.label = label;
        r.startPos = pos;
        r.startRot = go.transform.rotation;
        r.Build();
        return r;
    }

    void Build()
    {
        var visual = new GameObject("Visual").transform;
        visual.SetParent(transform, false);
        parts = new RobotParts { root = visual, alliance = alliance };
        spec.build(parts, spec);

        // Colliders: frictionless chassis riding on small skids, plus the mechanism tower.
        float clearance = 0.15f * IN;
        float top = spec.chassisTopIn * IN;
        var chassis = Util.ColBox(transform, "ChassisCollider", new Vector3(0, (clearance + top) * 0.5f, 0),
            new Vector3(spec.widthIn * IN, top - clearance, (spec.lengthIn - 3.5f) * IN), null, Util.Frictionless);
        var upper = Util.ColBox(transform, "UpperCollider", spec.upperCenter * IN, spec.upperSize * IN, null, Util.Frictionless);
        foreach (var sx in new[] { -1f, 1f })
        foreach (var sz in new[] { -1f, 1f })
        {
            var skid = new GameObject("Skid");
            skid.transform.SetParent(transform, false);
            skid.transform.localPosition = new Vector3(sx * (HalfWidth - 1.5f * IN), 0.35f * IN, sz * (HalfLength - 3f * IN));
            var sc = skid.AddComponent<SphereCollider>();
            sc.radius = 0.35f * IN;
            sc.sharedMaterial = Util.Frictionless;
        }
        Util.SetLayer(gameObject, Layers.Robot);
        Colliders.AddRange(GetComponentsInChildren<Collider>());
        foreach (var c in visual.GetComponentsInChildren<Collider>()) Destroy(c);

        rb = gameObject.AddComponent<Rigidbody>();
        rb.mass = spec.massKg;
        rb.drag = 0.2f;
        rb.angularDrag = 2f;
        rb.centerOfMass = new Vector3(0, 2f * IN, 0);
        // Robots stay on the TILES: no climbing onto elements or over the perimeter.
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ | RigidbodyConstraints.FreezePositionY;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        float L = spec.lengthIn * IN, Wd = spec.widthIn * IN, Hh = spec.heightIn * IN, m = spec.massKg;
        rb.inertiaTensor = new Vector3(m * (Wd * Wd + Hh * Hh) / 12f, m * (Wd * Wd + L * L) / 12f, m * (L * L + Hh * Hh) / 12f);
        rb.inertiaTensorRotation = Quaternion.identity;

        slots = new Transform[Capacity];
        for (int i = 0; i < Capacity; i++)
        {
            var s = new GameObject($"Slot{i}").transform;
            s.SetParent(transform, false);
            s.localPosition = spec.slots[i] * IN;
            slots[i] = s;
        }
        motor = gameObject.AddComponent<AudioSource>();
        motor.clip = SoundFx.MotorClip;
        motor.loop = true;
        motor.volume = 0f;
        motor.spatialBlend = 0.5f;
        motor.dopplerLevel = 0f;
        if (motor.clip) motor.Play();
    }

    public void Preload(Transform world)
    {
        // 10.3.4: ROBOTS start the MATCH contacting 4 pre-loaded POLLEN.
        for (int i = 0; i < Capacity; i++)
        {
            var e = GameElement.Create(world, ElementType.Pollen, Alliance.None, slots[i].position);
            Take(e);
        }
    }

    public void EnableTrajectory(bool on)
    {
        if (!on) { if (traj) traj.enabled = false; return; }
        if (!traj)
        {
            var go = new GameObject("TrajectoryPreview");
            go.transform.SetParent(transform.parent, false);
            traj = go.AddComponent<LineRenderer>();
            traj.sharedMaterial = Util.LineMat();
            traj.widthMultiplier = 0.012f;
            traj.positionCount = 0;
            traj.startColor = new Color(1, 1, 0.3f, 0.9f);
            traj.endColor = new Color(1, 1, 0.3f, 0.1f);
            traj.useWorldSpace = true;
        }
        traj.enabled = true;
    }

    public void ResetToStart()
    {
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.position = startPos;
        rb.rotation = startRot;
        transform.SetPositionAndRotation(startPos, startRot);
    }

    // ------------------------------------------------------------------ loop
    void FixedUpdate()
    {
        IRobotController active = mode == RobotMode.Auto ? autoController : mode == RobotMode.Teleop ? controller : null;
        cmd = active != null ? active.Tick(this) : default;
        // FEED: lob elements to a spot on our own side (aims first, like Shoot)
        feedTarget = cmd.feed && HasLauncher ? FeedPoint() : (Vector3?)null;
        if (feedTarget.HasValue) cmd.shoot = true;
        // turrets shoot and score by themselves whenever the shot is good (never while the driver is intaking)
        else if (spec.turret && mode == RobotMode.Teleop && !cmd.intake && held.Count > 0 && TurretShotGood()) cmd.shoot = true;
        UpdateAim();
        suppressAimTurn = false;

        Drive();
        KeepInsideField();
        Unjam();
        Intake();
        FlowerMechanism();
        Outtake();
        if (cmd.shoot && aimLocked) Shoot();
        LayoutHeld();
    }

    void OnDisable()
    {
        if (motor) motor.volume = 0f;
    }

    void Update()
    {
        if (motor)
        {
            float sp = Mathf.Clamp01(Util.Flat(rb.velocity).magnitude / Mathf.Max(0.3f, spec.maxSpeed) + Mathf.Abs(rb.angularVelocity.y) * 0.08f);
            motor.volume = Mathf.MoveTowards(motor.volume, sp * 0.09f * SoundFx.Volume, Time.deltaTime * 0.6f);
            motor.pitch = 0.75f + sp * 0.6f;
        }
        Animate();
        if (traj && traj.enabled) DrawTrajectory();
    }

    void Drive()
    {
        float dt = Time.fixedDeltaTime;
        Vector3 v = Util.Flat(rb.velocity);
        Vector3 fwd = transform.forward, right = transform.right;
        float vf = Vector3.Dot(v, fwd), vs = Vector3.Dot(v, right);

        float tf = Mathf.Clamp(cmd.forward, -1, 1) * spec.maxSpeed;
        float ts = spec.drive == DriveType.Mecanum ? Mathf.Clamp(cmd.strafe, -1, 1) * spec.maxSpeed * 0.85f : 0f;
        if (spec.drive == DriveType.Mecanum)
        {
            // mecanum wheels saturate: scale the combined command
            float sum = Mathf.Abs(cmd.forward) + Mathf.Abs(cmd.strafe) * 0.85f + Mathf.Abs(cmd.turn) * 0.5f;
            if (sum > 1f) { tf /= sum; ts /= sum; }
        }
        float lateralAccel = spec.drive == DriveType.Tank ? 14f : spec.accel;
        float af = Mathf.Clamp((tf - vf) / dt, -spec.accel, spec.accel);
        float aS = Mathf.Clamp((ts - vs) / dt, -lateralAccel, lateralAccel);
        rb.AddForce(fwd * af + right * aS, ForceMode.Acceleration);

        // Yaw rate is driven directly (acceleration-limited); X/Z rotation is frozen.
        float w = rb.angularVelocity.y;
        float tw = Mathf.Clamp(cmd.turn, -1, 1) * spec.maxTurnDeg * Mathf.Deg2Rad;
        rb.angularVelocity = new Vector3(0, Mathf.MoveTowards(w, tw, 22f * dt), 0);
    }

    // Safety net against solver tunnelling through the perimeter.
    void KeepInsideField()
    {
        float maxX = 0, maxZ = 0;
        foreach (var c in Footprint()) { maxX = Mathf.Max(maxX, Mathf.Abs(c.x)); maxZ = Mathf.Max(maxZ, Mathf.Abs(c.y)); }
        Vector3 p = rb.position;
        float ex = maxX - (Dims.Half + 0.01f), ez = maxZ - (Dims.Half + 0.01f);
        if (ex <= 0 && ez <= 0) return;
        Vector3 v = rb.velocity;
        if (ex > 0) { p.x -= Mathf.Sign(p.x) * ex; if (v.x * p.x > 0) v.x = 0; }
        if (ez > 0) { p.z -= Mathf.Sign(p.z) * ez; if (v.z * p.z > 0) v.z = 0; }
        rb.position = p;
        rb.velocity = v;
    }

    // A foam ball pinned between a robot and a wall would squash and pop out; rigid bodies
    // cannot, so when the drivetrain is blocked, nudge the touching balls up and away.
    void Unjam()
    {
        bool wants = Mathf.Abs(cmd.forward) > 0.25f || Mathf.Abs(cmd.strafe) > 0.25f || Mathf.Abs(cmd.turn) > 0.4f;
        bool moving = Util.Flat(rb.velocity).magnitude > 0.04f || Mathf.Abs(rb.angularVelocity.y) > 0.3f;
        blockedTime = wants && !moving ? blockedTime + Time.fixedDeltaTime : 0f;
        if (blockedTime > 0.25f)
        {
            foreach (var e in touchingBalls)
            {
                if (e == null || !e.IsFree) continue;
                Vector3 away = Util.Flat(e.transform.position - transform.position).normalized;
                e.rb.velocity = away * 1.0f + Vector3.up * 0.9f;
                e.IgnoreRobot(this, 0.25f);
            }
            if (touchingBalls.Count > 0) blockedTime = 0f;
        }
        touchingBalls.Clear();
    }

    // ------------------------------------------------------------------ intake / storage
    bool InsideFlower(GameElement e)
    {
        foreach (var f in Flower.All)
            if (f.HorizDist(e.transform.position) < 2.2f * IN && e.transform.position.y < Dims.FlowerTopHeight + 1f) return true;
        return false;
    }

    bool InsideHive(GameElement e)
    {
        return (Field.RedHive && (Field.RedHive.InCell(e, 1) || Field.RedHive.InCell(e, -1)))
            || (Field.BlueHive && (Field.BlueHive.InCell(e, 1) || Field.BlueHive.InCell(e, -1)));
    }

    void Intake()
    {
        if (!cmd.intake || held.Count >= Capacity || Time.time < nextIntake) return;
        Vector3 c = transform.TransformPoint(spec.intakeCenter * IN);
        int n = Physics.OverlapBoxNonAlloc(c, spec.intakeSize * IN * 0.5f, buf, transform.rotation, Layers.BallMask, QueryTriggerInteraction.Ignore);
        GameElement best = null;
        float bestD = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            var e = buf[i].GetComponent<GameElement>();
            if (e == null || !e.IsFree) continue;
            if (e.rb.velocity.sqrMagnitude > 16f) continue;
            if (e.type == ElementType.Nectar && e.color != alliance)
            {
                // G408: do not CONTROL opponent NECTAR — rollers kick it back out.
                e.rb.velocity = Vector3.Lerp(e.rb.velocity, transform.forward * 0.8f, 0.5f);
                continue;
            }
            bool inFlower = InsideFlower(e);
            if (inFlower && !(spec.flowerMech == FlowerMechType.Intake && e.transform.position.y < Dims.MiddleRingBottom + 0.5f * IN)) continue;
            if (InsideHive(e)) continue;
            float d = (e.transform.position - c).sqrMagnitude;
            if (d < bestD) { bestD = d; best = e; }
        }
        if (best != null)
        {
            Take(best);
            nextIntake = Time.time + 0.12f;
        }
    }

    public void Take(GameElement e)
    {
        held.Add(e);
        e.Hold(this, slots[held.Count - 1]);
    }

    public void Drop(GameElement e)
    {
        held.Remove(e);
        LayoutHeld();
    }

    void LayoutHeld()
    {
        for (int i = 0; i < held.Count; i++)
        {
            var e = held[i];
            if (e.transform.parent != slots[i]) { e.transform.SetParent(slots[i], false); }
            e.transform.localPosition = Vector3.MoveTowards(e.transform.localPosition, Vector3.zero, 0.02f);
        }
    }

    void Outtake()
    {
        if (!cmd.outtake || held.Count == 0 || Time.time < nextOut) return;
        var e = held[held.Count - 1];
        held.RemoveAt(held.Count - 1);
        Vector3 p = transform.TransformPoint(new Vector3(0, 2.2f, spec.lengthIn * 0.5f + 1.8f) * IN);
        e.Release(p, transform.forward * 1.4f + Util.Flat(rb.velocity), this);
        nextOut = Time.time + 0.3f;
    }

    // Side-mounted FLOWER mechanisms knock the bottom POLLEN out through the Retrieval Opening.
    void FlowerMechanism()
    {
        bool active = spec.flowerMech == FlowerMechType.Side && cmd.flower;
        flowerAnim = Mathf.MoveTowards(flowerAnim, active ? 1 : 0, Time.fixedDeltaTime * 6f);
        if (!active || Time.time < nextPoke) return;
        Vector3 mp = transform.TransformPoint(spec.flowerMechPoint * IN);
        foreach (var f in Flower.All)
        {
            if (f.HorizDist(mp) > 0.25f) continue;
            var bp = f.BottomPollen();
            if (bp == null) continue;
            if (Vector3.Distance(mp, bp.transform.position) > spec.flowerMechRadius * IN + bp.Radius) continue;
            bp.rb.velocity = f.inward * 1.1f + Vector3.up * 0.55f;
            bp.lastTouch = alliance;
            nextPoke = Time.time + 0.45f;
        }
    }

    // ------------------------------------------------------------------ launcher
    Vector3? feedTarget;

    // Feed destinations, described for the red ALLIANCE (blue is the same rotated 180 degrees).
    // "Top" is the rear half of the FIELD (away from the audience), "bottom" the audience half.
    //   top blue    -> top red        top red    -> bottom red
    //   bottom red  -> top red        bottom blue -> bottom red
    public Vector3 FeedPoint()
    {
        float s = alliance == Alliance.Blue ? -1f : 1f;
        float x = transform.position.x * s, z = transform.position.z * s;
        bool top = z > 0f, ownSide = x < 0f;
        float tz = ownSide ? (top ? -1f : 1f) : (top ? 1f : -1f);
        return new Vector3(-1.05f * s, 0.1f, tz * 1.0f * s);
    }

    public Vector3 CurrentTarget()
    {
        if (aiTarget.HasValue) return aiTarget.Value;
        if (feedTarget.HasValue) return feedTarget.Value;
        if (targetFlower)
        {
            var f = NearestFlower();
            if (f) return f.TopCenter;
        }
        return Field.HiveOf(alliance).AimPoint();
    }

    public Flower NearestFlower()
    {
        Flower best = null;
        float bd = float.MaxValue;
        foreach (var f in Flower.All)
        {
            float d = (f.transform.position - transform.position).sqrMagnitude;
            if (d < bd) { bd = d; best = f; }
        }
        return best;
    }

    public struct AimSolution
    {
        public Vector3 point;      // (lead-compensated) point the ball is thrown at
        public float yaw, pitch, speed;
        public bool valid;
    }

    // Launch solution for the current target: yaw, hood angle and exit speed.
    // True when aiming into a FLOWER's top ring (needs a precise, steep, descending shot).
    public static bool IsFlowerTarget(Vector3 target) => FlowerAt(target) != null;

    public static Flower FlowerAt(Vector3 target)
    {
        foreach (var f in Flower.All) if ((f.TopCenter - target).sqrMagnitude < 0.0025f) return f;
        return null;
    }

    public AimSolution Solve(Vector3 target)
    {
        var flowerTarget = FlowerAt(target);
        bool precise = flowerTarget != null;
        var s = new AimSolution { point = target };
        Vector3 robotVel = Util.Flat(rb.velocity);
        int iterations = spec.turret && spec.shootOnMove ? 3 : 1;
        for (int i = 0; i < iterations; i++)
        {
            // Release point: turrets throw from the barrel tip, which sits launchPoint.z in front of
            // the turret pivot along the firing direction.
            Vector3 origin;
            if (spec.turret)
            {
                Vector3 toward = Util.Flat(s.point - TurretPivot);
                Vector3 dirFlat = toward.sqrMagnitude > 1e-6f ? toward.normalized : transform.forward;
                origin = TurretPivot + dirFlat * (spec.launchPoint.z * IN) + Vector3.up * (spec.launchPoint.y * IN);
            }
            else origin = LaunchPoint;

            Vector3 flat = Util.Flat(s.point - origin);
            float d = Mathf.Max(0.05f, flat.magnitude);
            float h = s.point.y - origin.y;
            s.pitch = spec.hoodMax > spec.hoodMin
                ? Mathf.Lerp(spec.hoodMax, spec.hoodMin, Mathf.Clamp01((d - 0.7f) / 1.8f))   // adjustable hood
                : spec.launchAngle;
            if (precise && spec.hoodMax > spec.hoodMin) s.pitch = SteepestDescendingPitch(d, h, s.pitch);
            float th = s.pitch * Mathf.Deg2Rad;
            if (spec.launcher == LauncherType.Catapult) { s.speed = spec.launchFixed; s.valid = true; }
            else
            {
                float v = Util.LaunchSpeed(d, h, th);
                s.valid = !float.IsNaN(v) && v <= spec.launchMax;
                s.speed = float.IsNaN(v) ? spec.launchMax : Mathf.Clamp(v, spec.launchMin, spec.launchMax);
            }
            s.yaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
            if (iterations > 1)
            {
                // shooting on the move: lead the target by the robot's velocity over the flight time
                float tof = d / Mathf.Max(0.5f, s.speed * Mathf.Cos(th));
                s.point = target - robotVel * tof;
            }
        }
        return s;
    }

    // Highest hood angle whose arc is already coming down when it reaches the target, so the ball
    // drops into a FLOWER ring instead of clipping it on the way up.
    float SteepestDescendingPitch(float d, float h, float fallback)
    {
        float g = -Physics.gravity.y;
        for (float a = spec.hoodMax; a >= spec.hoodMin; a -= 0.5f)
        {
            float th = a * Mathf.Deg2Rad;
            float v = Util.LaunchSpeed(d, h, th);
            if (float.IsNaN(v) || v > spec.launchMax || v < spec.launchMin) continue;
            float apex = v * v * Mathf.Sin(2f * th) / (2f * g);   // horizontal distance to the top of the arc
            if (apex < d * 0.8f) return a;
        }
        return fallback;
    }

    public float LaunchSpeedFor(Vector3 target) => Solve(target).speed;

    bool TurretShotGood()
    {
        var target = CurrentTarget();
        Vector2 pos = new Vector2(transform.position.x, transform.position.z);
        // automatic shooting is only for the HIVE; at a FLOWER the driver presses Shoot
        if (targetFlower) return false;
        var hive = Field.HiveOf(alliance);
        return hive && Solve(target).valid && PlanCompiler.ShotQuality(spec, pos, target, hive.OpeningDir(), out _);
    }

    // Yaw (degrees) the robot must face so a fixed launcher points at the target.
    public float YawToward(Vector3 target)
    {
        Vector3 to = Util.Flat(target - transform.position);
        float yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
        float lateral = spec.launchPoint.x * IN * (spec.shootsBackward ? -1 : 1);
        float dist = Mathf.Max(to.magnitude, 0.2f);
        yaw -= Mathf.Asin(Mathf.Clamp(lateral / dist, -1, 1)) * Mathf.Rad2Deg;
        if (spec.shootsBackward) yaw += 180f;
        return yaw;
    }

    // Remaining yaw error for the launcher (turret robots: what the robot body still has to turn).
    public float AlignError(Vector3 target)
    {
        if (!spec.turret) return Mathf.DeltaAngle(Yaw, YawToward(target));
        float rel = Mathf.DeltaAngle(Yaw, Solve(target).yaw);
        return Mathf.Abs(rel) <= spec.turretRange ? 0f : rel - Mathf.Sign(rel) * spec.turretRange;
    }

    // Holding FIRE aims first (turret, or by turning the robot) and releases once locked on.
    void UpdateAim()
    {
        if (!HasLauncher) { aimLocked = false; return; }
        var target = CurrentTarget();
        var sol = Solve(target);
        inRange = sol.valid;
        if (spec.turret)
        {
            float rel = Mathf.Clamp(Mathf.DeltaAngle(Yaw, sol.yaw), -spec.turretRange, spec.turretRange);
            if (!cmd.shoot && !spec.turretAlwaysTracks) rel = 0f;
            turretYaw = Mathf.MoveTowardsAngle(turretYaw, rel, spec.turretSpeed * Time.fixedDeltaTime);
            float err = Mathf.DeltaAngle(Yaw + turretYaw, sol.yaw);
            // the FLOWER opening leaves millimetres of clearance: lock only when dead on and settled
            aimLocked = IsFlowerTarget(target)
                ? Mathf.Abs(err) < 0.25f && Mathf.Abs(rb.angularVelocity.y) < 0.15f && Util.Flat(rb.velocity).magnitude < 0.08f
                : Mathf.Abs(err) < 1.8f;
            if (cmd.shoot && !aimLocked && Mathf.Abs(Mathf.DeltaAngle(Yaw, sol.yaw)) > spec.turretRange)
                cmd.turn = Mathf.Clamp(Mathf.DeltaAngle(Yaw, sol.yaw) / 30f, -1f, 1f);
        }
        else
        {
            float err = AlignError(target);
            if (cmd.shoot && !suppressAimTurn) cmd.turn = Mathf.Clamp(err / 22f, -1f, 1f);
            aimLocked = Mathf.Abs(err) < 2.2f && Mathf.Abs(rb.angularVelocity.y) < 0.6f;
        }
    }

    void Shoot()
    {
        if (!HasLauncher || held.Count == 0 || Time.time < nextShot) return;
        Vector3 target = CurrentTarget();
        var sol = Solve(target);
        int count = Mathf.Min(Mathf.Max(1, spec.barrels), held.Count);
        float spreadScale = spec.turret && IsFlowerTarget(target) ? 0f : 1f;   // turret FLOWER shots are exact
        Vector3 across = Vector3.Cross(Vector3.up, LaunchFlatDir).normalized;
        for (int i = 0; i < count; i++)
        {
            float speed = sol.speed * (1f + Random.Range(-spec.speedSpread, spec.speedSpread) * spreadScale);
            float pitch = sol.pitch + Random.Range(-spec.spreadDeg, spec.spreadDeg) * 0.6f * spreadScale;
            float yaw = (spec.turret ? sol.yaw : Yaw + (spec.shootsBackward ? 180f : 0f)) + Random.Range(-spec.spreadDeg, spec.spreadDeg) * spreadScale;
            Vector3 flat = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
            Vector3 dir = Quaternion.AngleAxis(-pitch, Vector3.Cross(Vector3.up, flat)) * flat;
            float offset = count > 1 ? (i - (count - 1) * 0.5f) * spec.barrelSpacing * IN : 0f;

            var e = held[0];
            held.RemoveAt(0);
            Vector3 release = spec.turret
                ? TurretPivot + flat * (spec.launchPoint.z * IN) + Vector3.up * (spec.launchPoint.y * IN)
                : LaunchPoint;
            e.Release(release - across * offset, dir * speed + Util.Flat(rb.velocity), this);
            OnShot?.Invoke(this, e, target, speed);
        }
        nextShot = Time.time + spec.feedInterval;
        catapultT = 0f;
        SoundFx.Play("shot");
    }

    readonly List<GameElement> touchingBalls = new List<GameElement>();
    float blockedTime;

    void OnCollisionStay(Collision c)
    {
        if (c.collider.gameObject.layer == Layers.Ball)
        {
            var e = c.collider.GetComponent<GameElement>();
            if (e != null && touchingBalls.Count < 16 && !touchingBalls.Contains(e)) touchingBalls.Add(e);
        }
        if (contacts.Count < 12 && c.collider.gameObject.layer != Layers.Ball) contacts.Add(c.collider.name + "@" + c.collider.transform.parent?.name);
    }

    void DrawTrajectory()
    {
        if (!HasLauncher) { traj.positionCount = 0; return; }
        var sol = Solve(CurrentTarget());
        float yaw = spec.turret ? Yaw + turretYaw : Yaw + (spec.shootsBackward ? 180f : 0f);
        Vector3 flat = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
        Vector3 v = Quaternion.AngleAxis(-sol.pitch, Vector3.Cross(Vector3.up, flat)) * flat * sol.speed + Util.Flat(rb.velocity);
        traj.startColor = aimLocked ? new Color(0.3f, 1f, 0.3f, 0.9f) : new Color(1, 1, 0.3f, 0.9f);
        Vector3 p = LaunchPoint;
        const int N = 48;
        traj.positionCount = N;
        float dt = 0.03f;
        for (int i = 0; i < N; i++)
        {
            traj.SetPosition(i, p);
            p += v * dt + 0.5f * Physics.gravity * dt * dt;
            v += Physics.gravity * dt;
            if (p.y < 0) p.y = 0;
        }
    }

    // ------------------------------------------------------------------ visuals
    void Animate()
    {
        float dt = Time.deltaTime;
        if (parts == null) return;
        float intakeSpin = cmd.intake ? 900f : cmd.outtake ? -600f : 0f;
        foreach (var t in parts.intakeSpinners) if (t) t.Rotate(Vector3.up, intakeSpin * dt, Space.Self);
        float fly = HasLauncher && spec.launcher == LauncherType.Flywheel && mode != RobotMode.Disabled ? 1800f : 0f;
        foreach (var t in parts.launcherSpinners) if (t) t.Rotate(Vector3.up, fly * dt, Space.Self);
        float wheelDeg = Vector3.Dot(rb.velocity, transform.forward) / (1.8f * IN) * Mathf.Rad2Deg;
        foreach (var t in parts.wheelSpinners) if (t) t.Rotate(Vector3.up, wheelDeg * dt, Space.Self);

        if (parts.turret) parts.turret.localRotation = Quaternion.Euler(0, turretYaw, 0);
        if (parts.hood && HasLauncher)
        {
            float pitch = Solve(CurrentTarget()).pitch;
            parts.hood.localRotation = Quaternion.Euler(-(pitch - 45f), 0, 0);
        }
        if (parts.catapultArm)
        {
            catapultT = Mathf.Min(1f, catapultT + dt / Mathf.Max(0.2f, spec.feedInterval));
            float a = catapultT < 0.15f ? Mathf.Lerp(0, -120, catapultT / 0.15f) : Mathf.Lerp(-120, 0, (catapultT - 0.15f) / 0.85f);
            parts.catapultArm.localRotation = Quaternion.Euler(a, 0, 0);
        }
        if (parts.flowerMech && spec.flowerMech == FlowerMechType.Side)
        {
            if (parts.flowerMechActive != Vector3.zero)
                parts.flowerMech.localRotation = Quaternion.Euler(Vector3.Lerp(parts.flowerMechRest, parts.flowerMechActive, flowerAnim));
            else if (flowerAnim > 0.01f)
                parts.flowerMech.Rotate(Vector3.up, 900f * dt, Space.Self);
        }
    }

    // ------------------------------------------------------------------ scoring geometry
    public Vector2[] Footprint()
    {
        Vector3 f = transform.forward * HalfLength, r = transform.right * HalfWidth, c = transform.position;
        Vector3[] w = { c + f + r, c + f - r, c - f - r, c - f + r };
        var res = new Vector2[4];
        for (int i = 0; i < 4; i++) res[i] = new Vector2(w[i].x, w[i].z);
        return res;
    }

    // "at least partially in" a zone: separating-axis test of footprint vs zone rectangle.
    public bool Overlaps(Zone z)
    {
        var pts = Footprint();
        var zp = new[] { z.min, new Vector2(z.max.x, z.min.y), z.max, new Vector2(z.min.x, z.max.y) };
        var axes = new[] { Vector2.right, Vector2.up, new Vector2(transform.forward.x, transform.forward.z), new Vector2(transform.right.x, transform.right.z) };
        foreach (var ax in axes)
        {
            float a0 = float.MaxValue, a1 = float.MinValue, b0 = float.MaxValue, b1 = float.MinValue;
            foreach (var p in pts) { float d = Vector2.Dot(p, ax); a0 = Mathf.Min(a0, d); a1 = Mathf.Max(a1, d); }
            foreach (var p in zp) { float d = Vector2.Dot(p, ax); b0 = Mathf.Min(b0, d); b1 = Mathf.Max(b1, d); }
            if (a1 < b0 || b1 < a0) return false;
        }
        return true;
    }

    public bool TouchingWall(float tol = 0.015f)
    {
        foreach (var p in Footprint())
            if (Mathf.Abs(p.x) > Dims.Half - tol || Mathf.Abs(p.y) > Dims.Half - tol) return true;
        return false;
    }
}
