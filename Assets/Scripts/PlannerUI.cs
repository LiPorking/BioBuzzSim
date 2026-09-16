using System.Collections.Generic;
using UnityEngine;
using static UIKit;

// AUTO PLANNER: place waypoints (position + heading) on a top-down FIELD, get an obstacle-aware
// time-optimal path, add actions on a timeline, preview by scrubbing, then run the real physics
// simulation and scrub through the recording like a video.
public class PlannerUI : MonoBehaviour
{
    public static PlannerUI Active;
    enum View { Edit, Simulating, Replay }

    AutoPlan plan;
    CompiledPlan compiled;
    bool dirty = true;
    View view = View.Edit;
    Robot robot;
    PlanController simController;

    float playhead;
    bool playing;
    float playSpeed = 1f;
    int selWp = -1, selMarker = -1;

    // field dragging
    enum Drag { None, WpMove, WpHeading, Scrub, MarkerMove, MarkerResize }
    Drag drag;
    float dragOffsetT;

    // UI overlays
    bool showLoad;
    Vector2 panelScroll, loadScroll;
    string status = "";
    float statusUntil;

    // recording
    class Frame
    {
        public float t;
        public Vector3 robotPos;
        public float robotYaw, turret;
        public Vector3[] pos;
        public bool[] active;
        public int held, autoPts;
        public Quaternion hiveRed, hiveBlue;
        public string label;
    }
    readonly List<Frame> frames = new List<Frame>();
    readonly List<GameElement> recElems = new List<GameElement>();
    readonly List<float> shotTimes = new List<float>();
    readonly List<float> tipTimes = new List<float>();
    int simTips, simAuto, simShots;
    float simTime;
    int fixedCount;

    // visuals
    Transform vis;
    LineRenderer pathLine, trailLine;

    static readonly Rect TopBar = new Rect(0, 0, 1920, 62);
    static readonly Rect FieldRect = new Rect(0, 62, 1400, 748);
    static readonly Rect Panel = new Rect(1400, 62, 520, 748);
    static readonly Rect Timeline = new Rect(0, 810, 1920, 270);
    const float LaneX = 60f, LaneW = 1820f;

    // ------------------------------------------------------------------ open / close
    // Plans are not tied to a robot: whichever robot a station uses runs the plan.
    // The editor previews and simulates with the RED 1 robot from the main menu.
    static int PreviewRobot => Mathf.Clamp(GameConfig.slots[0].robot, 0, RobotLibrary.All.Count - 1);

    public void Open()
    {
        if (plan != null) plan.robot = PreviewRobot;
        Active = this;
        enabled = true;
        MatchManager.I.period = Period.Planner;
        MatchManager.I.autoOnly = false;
        if (plan == null)
        {
            plan = new AutoPlan { robot = PreviewRobot, alliance = Alliance.Red };
            GameConfig.StartPose(0, plan.Spec, out var p, out var y);
            if (!string.IsNullOrEmpty(GameConfig.slots[0].autoPlan)) { p = Dims.V(-72f + plan.Spec.lengthIn * 0.5f, 0, 0); y = 90; }
            plan.waypoints.Add(new PlanWaypoint { x = p.x, z = p.z, heading = y, stop = true });
        }
        if (vis == null)
        {
            vis = new GameObject("PlannerVisuals").transform;
            pathLine = MakeLine("PlannedPath", new Color(0.2f, 0.9f, 1f, 0.95f), 0.025f);
            trailLine = MakeLine("ActualPath", new Color(1f, 0.55f, 0.1f, 0.95f), 0.02f);
        }
        vis.gameObject.SetActive(true);
        EnterEdit();
    }

    public void Close()
    {
        Active = null;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        Time.timeScale = 1f;
        if (vis) vis.gameObject.SetActive(false);
        enabled = false;
    }

    LineRenderer MakeLine(string name, Color c, float w)
    {
        var go = new GameObject(name);
        go.transform.SetParent(vis, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.sharedMaterial = Util.LineMat();
        lr.startColor = lr.endColor = c;
        lr.widthMultiplier = w;
        lr.useWorldSpace = true;
        lr.positionCount = 0;
        lr.numCornerVertices = 2;
        return lr;
    }

    void Status(string s) { status = s; statusUntil = Time.unscaledTime + 4f; }

    // ------------------------------------------------------------------ world
    void RebuildWorld(bool forSim)
    {
        Physics.simulationMode = SimulationMode.FixedUpdate;
        Game.I.RebuildWorldNow(false);
        var mm = MatchManager.I;
        mm.Red.nectarInArea = 5; mm.Blue.nectarInArea = 5;
        mm.Red.nectarGrants = mm.Blue.nectarGrants = 0;
        mm.Red.nectarEntered = mm.Blue.nectarEntered = 0;
        mm.Red.foulPointsReceived = mm.Blue.foulPointsReceived = 0;
        var w0 = plan.waypoints[0];
        robot = Robot.Create(Game.World, plan.Spec, plan.alliance, 1, new Vector3(w0.x, 0, w0.z), w0.heading, plan.alliance == Alliance.Red ? "RED 1" : "BLUE 1");
        robot.Preload(Game.World);
        mm.robots.Clear();
        mm.robots.Add(robot);
        if (forSim)
        {
            simController = new PlanController(plan.Clone());
            robot.autoController = simController;
        }
    }

    void EnterEdit()
    {
        view = View.Edit;
        playing = false;
        Time.timeScale = 1f;
        MatchManager.I.period = Period.Planner;
        RebuildWorld(false);
        shotZoneKey = "";
        if (shotZone) shotZone.SetActive(true);
        robot.enabled = false;
        robot.rb.isKinematic = true;
        Physics.simulationMode = SimulationMode.Script;   // frozen field while editing
        trailLine.positionCount = 0;
        dirty = true;
    }

    void StartSimulation()
    {
        if (plan.waypoints.Count == 0) return;
        view = View.Simulating;
        frames.Clear();
        shotTimes.Clear();
        tipTimes.Clear();
        simShots = simTips = 0;
        RebuildWorld(true);
        recElems.Clear();
        foreach (var e in Resources.FindObjectsOfTypeAll<GameElement>())
            if (e.gameObject.scene.IsValid() && e.transform.IsChildOf(Game.World)) recElems.Add(e);
        Robot.OnShot = (r, e, target, speed) => { if (view == View.Simulating) { simShots++; shotTimes.Add(simTime); } };
        simTime = 0;
        fixedCount = 0;
        playhead = 0;
        MatchManager.I.BeginAutoOnly();
        Time.timeScale = playSpeed >= 2f ? 2f : 1f;
        Status("Simulating AUTO with physics...");
    }

    void FixedUpdate()
    {
        if (view != View.Simulating) return;
        var mm = MatchManager.I;
        simTime = mm.period == Period.Auto ? mm.t : simTime;
        int tips = Field.HiveOf(plan.alliance) ? Field.HiveOf(plan.alliance).Tips : 0;
        if (tips > simTips) { simTips = tips; tipTimes.Add(simTime); }
        if (fixedCount++ % 8 == 0) Record(simTime);
        if (mm.autoOnlyDone)
        {
            Record(Dims.AutoTime);
            simAuto = mm.ScoreOf(plan.alliance).AutoPts;
            view = View.Replay;
            Time.timeScale = 1f;
            Physics.simulationMode = SimulationMode.Script;
            robot.enabled = false;
            robot.rb.isKinematic = true;
            foreach (var e in recElems) if (e) { e.rb.isKinematic = true; }
            playhead = 0;
            playing = true;
            BuildTrail();
            Status($"Simulation done: {simAuto} AUTO points, {simTips} TIPS, {simShots} shots. Scrub the timeline to replay.");
        }
    }

    void Record(float t)
    {
        var f = new Frame
        {
            t = t,
            robotPos = robot.transform.position,
            robotYaw = robot.transform.eulerAngles.y,
            turret = robot.turretYaw,
            pos = new Vector3[recElems.Count],
            active = new bool[recElems.Count],
            held = robot.held.Count,
            autoPts = MatchManager.I.ScoreOf(plan.alliance).AutoPts,
            label = simController != null ? simController.CurrentLabel : "",
            hiveRed = Field.RedHive ? Field.RedHive.transform.localRotation : Quaternion.identity,
            hiveBlue = Field.BlueHive ? Field.BlueHive.transform.localRotation : Quaternion.identity,
        };
        for (int i = 0; i < recElems.Count; i++)
        {
            var e = recElems[i];
            f.active[i] = e && e.gameObject.activeInHierarchy;
            f.pos[i] = e ? e.transform.position : Vector3.zero;
        }
        frames.Add(f);
    }

    void ApplyFrame(float t)
    {
        if (frames.Count == 0) return;
        int i = 0;
        while (i < frames.Count - 2 && frames[i + 1].t < t) i++;
        var a = frames[i];
        var b = frames[Mathf.Min(i + 1, frames.Count - 1)];
        float k = b.t > a.t ? Mathf.Clamp01((t - a.t) / (b.t - a.t)) : 0f;
        robot.transform.position = Vector3.Lerp(a.robotPos, b.robotPos, k);
        robot.transform.rotation = Quaternion.Euler(0, Mathf.LerpAngle(a.robotYaw, b.robotYaw, k), 0);
        if (Field.RedHive) Field.RedHive.transform.localRotation = Quaternion.Slerp(a.hiveRed, b.hiveRed, k);
        if (Field.BlueHive) Field.BlueHive.transform.localRotation = Quaternion.Slerp(a.hiveBlue, b.hiveBlue, k);
        if (robot.parts.turret) robot.parts.turret.localRotation = Quaternion.Euler(0, Mathf.LerpAngle(a.turret, b.turret, k), 0);
        for (int j = 0; j < recElems.Count; j++)
        {
            var e = recElems[j];
            if (!e) continue;
            if (e.gameObject.activeSelf != a.active[j]) e.gameObject.SetActive(a.active[j]);
            if (e.transform.parent != Game.World) e.transform.SetParent(Game.World, true);
            e.transform.position = Vector3.Lerp(a.pos[j], b.pos[j], k);
        }
    }

    void BuildTrail()
    {
        trailLine.positionCount = frames.Count;
        for (int i = 0; i < frames.Count; i++) trailLine.SetPosition(i, frames[i].robotPos + Vector3.up * 0.03f);
    }

    // ------------------------------------------------------------------ compile & visuals
    void Recompile()
    {
        dirty = false;
        var balls = new List<Vector3>();
        foreach (var e in GameElement.All)
        {
            if (!e.IsFree || e.transform.position.y > 0.1f) continue;
            bool inFlower = false;
            foreach (var f in Flower.All) if (f.HorizDist(e.transform.position) < 0.1f) inFlower = true;
            if (e.type == ElementType.Nectar && e.color != plan.alliance) continue;
            if (!inFlower) balls.Add(e.transform.position);
        }
        compiled = PlanCompiler.Compile(plan, balls);
        // planned path polyline
        var pts = new List<Vector3>();
        if (compiled.duration > 0)
            for (float t = 0; t <= compiled.duration + 0.001f; t += 0.04f)
            {
                compiled.Eval(t, out var p, out _, out _);
                pts.Add(new Vector3(p.x, 0.02f, p.y));
            }
        pathLine.positionCount = pts.Count;
        pathLine.SetPositions(pts.ToArray());
        RebuildMarkers();
        RebuildShotZone();
        playhead = Mathf.Min(playhead, Mathf.Max(0, compiled.duration));
    }

    readonly List<GameObject> wpObjects = new List<GameObject>();
    GameObject shotZone;
    string shotZoneKey = "";

    // Green dots: positions (robot centre) from which a launch into the up CELL works.
    void RebuildShotZone()
    {
        var hive = Field.HiveOf(plan.alliance);
        if (hive == null) return;
        string key = plan.robot + "_" + plan.alliance + "_" + hive.upEnd;
        if (key == shotZoneKey && shotZone) return;
        shotZoneKey = key;
        if (shotZone) Destroy(shotZone);
        shotZone = new GameObject("ShotZone");
        shotZone.transform.SetParent(vis, false);
        var spec = plan.Spec;
        if (spec.launcher == LauncherType.None) return;
        Vector3 aim = hive.AimPoint(), outward = hive.OpeningDir();
        for (float x = -Dims.Half + 0.1f; x < Dims.Half; x += 0.1f)
            for (float z = -Dims.Half + 0.1f; z < Dims.Half; z += 0.1f)
            {
                var p = new Vector2(x, z);
                if (!PathPlanner.Free(p, spec)) continue;
                if (!PlanCompiler.ShotQuality(spec, p, aim, outward, out _)) continue;
                Util.Box(shotZone.transform, "z", new Vector3(x, 0.006f, z), new Vector3(0.05f, 0.002f, 0.05f), new Color(0.2f, 1f, 0.3f, 0.35f), false, null, true);
            }
    }

    void RebuildMarkers()
    {
        foreach (var o in wpObjects) if (o) Destroy(o);
        wpObjects.Clear();
        var spec = plan.Spec;
        for (int i = 0; i < plan.waypoints.Count; i++)
        {
            var w = plan.waypoints[i];
            var root = new GameObject($"WP{i}");
            root.transform.SetParent(vis, false);
            root.transform.position = new Vector3(w.x, 0.03f, w.z);
            root.transform.rotation = Quaternion.Euler(0, w.heading, 0);
            Color c = i == selWp ? new Color(1f, 0.85f, 0.1f) : i == 0 ? new Color(0.2f, 0.95f, 0.3f) : w.steps.Count > 0 ? new Color(1f, 0.5f, 0.2f) : Color.white;
            Util.Box(root.transform, "footprint", Vector3.zero, new Vector3(spec.widthIn * Dims.IN, 0.004f, spec.lengthIn * Dims.IN), new Color(c.r, c.g, c.b, 0.18f), false, null, true);
            Util.Cyl(root.transform, "dot", Vector3.up * 0.01f, 0.07f, 0.01f, c, Vector3.up);
            Util.Box(root.transform, "arrow", new Vector3(0, 0.02f, 0.16f), new Vector3(0.018f, 0.01f, 0.3f), c, false);
            Util.Ball(root.transform, "handle", new Vector3(0, 0.03f, 0.32f), 0.05f, c);
            if (!w.stop && i > 0 && w.steps.Count == 0) Util.Cyl(root.transform, "passThrough", Vector3.up * 0.012f, 0.1f, 0.004f, new Color(0.3f, 0.8f, 1f, 0.5f), Vector3.up);
            wpObjects.Add(root);
        }
    }

    // ------------------------------------------------------------------ per frame
    void Update()
    {
        if (Active != this) return;
        var cam = Camera.main;
        if (CameraRig.I) { CameraRig.I.plannerViewport = GuiToViewport(FieldRect); CameraRig.I.plannerFlip = plan.alliance == Alliance.Blue ? 180f : 0f; }

        if (view == View.Edit)
        {
            if (dirty) Recompile();
            if (playing) { playhead += Time.unscaledDeltaTime * playSpeed; if (playhead > compiled.duration) { playhead = compiled.duration; playing = false; } }
            compiled.Eval(playhead, out var p, out var h, out _);
            if (compiled.segs.Count == 0 && plan.waypoints.Count > 0) { p = new Vector2(plan.waypoints[0].x, plan.waypoints[0].z); h = plan.waypoints[0].heading; }
            robot.transform.SetPositionAndRotation(new Vector3(p.x, 0, p.y), Quaternion.Euler(0, h, 0));
            HandleFieldMouse(cam);
            if (Input.GetKeyDown(KeyCode.Delete) && selWp > 0 && GUIUtility.keyboardControl == 0) { plan.waypoints.RemoveAt(selWp); selWp = -1; dirty = true; }
        }
        else if (view == View.Replay)
        {
            float len = frames.Count > 0 ? frames[frames.Count - 1].t : 0f;
            if (playing) { playhead += Time.unscaledDeltaTime * playSpeed; if (playhead > len) { playhead = len; playing = false; } }
            ApplyFrame(playhead);
        }
        else playhead = simTime;

        if (GUIUtility.keyboardControl == 0 && Input.GetKeyDown(KeyCode.Space) && view != View.Simulating) playing = !playing;
        if (UIKit.ConsumeBack() && view != View.Edit) EnterEdit();
    }

    Vector3? MouseOnField(Camera cam)
    {
        if (cam == null || !FieldRect.Contains(MouseGui()) || showLoad) return null;
        var ray = cam.ScreenPointToRay(Input.mousePosition);
        if (Mathf.Abs(ray.direction.y) < 1e-4f) return null;
        float t = -ray.origin.y / ray.direction.y;
        return ray.origin + ray.direction * t;
    }

    void HandleFieldMouse(Camera cam)
    {
        var mp = MouseOnField(cam);
        if (Input.GetMouseButtonUp(0) && (drag == Drag.WpMove || drag == Drag.WpHeading)) drag = Drag.None;
        if (mp == null) return;
        Vector3 m = mp.Value;
        float lim = Dims.Half - 0.05f;

        if (drag == Drag.WpMove && selWp >= 0)
        {
            var w = plan.waypoints[selWp];
            float hl = Mathf.Min(plan.Spec.lengthIn, plan.Spec.widthIn) * 0.5f * Dims.IN;
            w.x = Mathf.Clamp(m.x, -Dims.Half + hl, Dims.Half - hl);
            w.z = Mathf.Clamp(m.z, -Dims.Half + hl, Dims.Half - hl);
            dirty = true;
            return;
        }
        if (drag == Drag.WpHeading && selWp >= 0)
        {
            var w = plan.waypoints[selWp];
            Vector3 d = m - w.Pos;
            if (d.sqrMagnitude > 0.0004f)
            {
                float h = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                if (!Input.GetKey(KeyCode.LeftAlt)) h = Mathf.Round(h / 5f) * 5f;
                w.heading = Mathf.Repeat(h, 360f);
                dirty = true;
            }
            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            int hit = -1; bool handle = false;
            for (int i = 0; i < plan.waypoints.Count; i++)
            {
                var w = plan.waypoints[i];
                Vector3 hp = w.Pos + Quaternion.Euler(0, w.heading, 0) * Vector3.forward * 0.32f;
                if (Util.Flat(m - hp).magnitude < 0.06f) { hit = i; handle = true; break; }
                if (Util.Flat(m - w.Pos).magnitude < 0.12f) hit = i;
            }
            if (hit >= 0)
            {
                selWp = hit; selMarker = -1;
                drag = handle ? Drag.WpHeading : Drag.WpMove;
                dirty = true;
            }
            else if (Mathf.Abs(m.x) < lim && Mathf.Abs(m.z) < lim)
            {
                // add a waypoint (Ctrl: insert after the selected one)
                int insertAt = Input.GetKey(KeyCode.LeftControl) && selWp >= 0 ? selWp + 1 : plan.waypoints.Count;
                var prev = plan.waypoints[Mathf.Clamp(insertAt - 1, 0, plan.waypoints.Count - 1)];
                Vector3 d = m - prev.Pos;
                float h = d.sqrMagnitude > 0.001f ? Mathf.Round(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg / 5f) * 5f : prev.heading;
                plan.waypoints.Insert(insertAt, new PlanWaypoint { x = m.x, z = m.z, heading = Mathf.Repeat(h, 360f), speed = Mathf.Min(1.2f, plan.Spec.maxSpeed), stop = true });
                selWp = insertAt;
                drag = Drag.WpMove;
                dirty = true;
            }
        }
        if (Input.GetMouseButtonDown(1))
        {
            for (int i = 1; i < plan.waypoints.Count; i++)
                if (Util.Flat(m - plan.waypoints[i].Pos).magnitude < 0.12f) { plan.waypoints.RemoveAt(i); selWp = -1; dirty = true; break; }
        }
        float wheel = Input.mouseScrollDelta.y;
        if (wheel != 0 && selWp >= 0)
        {
            plan.waypoints[selWp].heading = Mathf.Repeat(plan.waypoints[selWp].heading - wheel * (Input.GetKey(KeyCode.LeftShift) ? 5f : 15f), 360f);
            dirty = true;
        }
    }

    // ------------------------------------------------------------------ GUI
    void OnGUI()
    {
        if (Active != this) return;
        UIKit.Begin();
        DrawTopBar();
        DrawPanel();
        DrawTimeline();
        DrawFieldOverlay();
        if (showLoad) DrawLoad();
    }

    Vector2 WorldToGui(Vector3 w)
    {
        var cam = Camera.main;
        Vector3 s = cam.WorldToScreenPoint(w);
        return new Vector2((s.x - offset.x) / scale, (Screen.height - s.y - offset.y) / scale);
    }

    void DrawTopBar()
    {
        Fill(TopBar, new Color(0.05f, 0.06f, 0.08f, 0.97f));
        GUI.Label(new Rect(16, 12, 260, 40), "<b>AUTO PLANNER</b>", h2);
        GUI.Label(new Rect(280, 18, 60, 30), "Name", small);
        string nn = GUI.TextField(new Rect(334, 12, 230, 38), plan.name, 40, field);
        if (nn != plan.name) plan.name = nn;
        bool edit = view == View.Edit;
        if (Button(new Rect(978, 10, 100, 42), plan.alliance.ToString().ToUpper()) && edit)
        {
            plan = plan.ForAlliance(plan.alliance.Other());
            EnterEdit();
        }
        if (Button(new Rect(1086, 10, 80, 42), "NEW") && edit)
        {
            plan = null;
            selWp = -1;
            plan = new AutoPlan { robot = PreviewRobot };
            GameConfig.StartPose(0, plan.Spec, out var p, out var y);
            plan.waypoints.Add(new PlanWaypoint { x = p.x, z = p.z, heading = y });
            plan.markers.Clear();
            EnterEdit();
        }
        if (Button(new Rect(1174, 10, 86, 42), "SAVE")) { plan.Save(); Status($"Saved \"{plan.name}\""); }
        if (Button(new Rect(1268, 10, 86, 42), "LOAD") && edit) showLoad = !showLoad;
        GUI.Label(new Rect(1366, 10, 110, 42), "Use as\nAUTO for", tiny);
        for (int i = 0; i < 4; i++)
        {
            string lbl = (i < 2 ? "R" : "B") + (i % 2 + 1);
            if (Button(new Rect(1440 + i * 56, 12, 50, 38), lbl, btnSmall, GameConfig.slots[i].autoPlan == plan.name))
            {
                plan.Save();
                var sp = GameConfig.slots[i];
                sp.autoPlan = plan.name;
                if (sp.control == ControlType.Empty) sp.control = ControlType.AI;
                Status($"\"{plan.name}\" will run as {GameConfig.SlotName(i)}'s AUTO (mirrored automatically for blue)");
            }
        }
        if (Button(new Rect(1790, 10, 120, 42), "MENU")) { plan.Save(); Close(); Game.I.BackToMenu(); }
    }

    void DrawLoad()
    {
        var r = new Rect(1060, 62, 420, 500);
        Fill(r, new Color(0.03f, 0.03f, 0.05f, 0.98f));
        GUI.Label(new Rect(r.x + 12, r.y + 8, 300, 34), "<b>Saved plans</b>", h2);
        var names = AutoPlan.SavedNames();
        loadScroll = GUI.BeginScrollView(new Rect(r.x + 8, r.y + 48, r.width - 16, r.height - 110), loadScroll, new Rect(0, 0, r.width - 40, names.Count * 46));
        for (int i = 0; i < names.Count; i++)
        {
            if (Button(new Rect(0, i * 46, r.width - 110, 40), names[i]))
            {
                var p = AutoPlan.Load(names[i]);
                if (p != null && p.waypoints.Count > 0) { p.robot = PreviewRobot; plan = p; selWp = -1; showLoad = false; EnterEdit(); Status($"Loaded \"{p.name}\""); }
            }
            if (Button(new Rect(r.width - 104, i * 46, 64, 40), "DEL")) AutoPlan.Delete(names[i]);
        }
        GUI.EndScrollView();
        if (Button(new Rect(r.x + 12, r.yMax - 54, r.width - 24, 44), "CLOSE")) showLoad = false;
    }

    void DrawFieldOverlay()
    {
        // waypoint numbers
        for (int i = 0; i < plan.waypoints.Count; i++)
        {
            var g = WorldToGui(plan.waypoints[i].Pos);
            if (!FieldRect.Contains(g)) continue;
            GUI.Label(new Rect(g.x + 10, g.y - 30, 90, 24), i == 0 ? "<b>START</b>" : $"<b>{i}</b>", smallCenter);
        }
        // current action / target
        string info;
        if (view == View.Edit && compiled != null)
        {
            compiled.Eval(playhead, out _, out _, out var seg);
            info = seg != null ? $"{playhead:0.00}s  {seg.label}" : "";
            if (seg != null && seg.kind == SegKind.Step && (seg.step.type == StepType.CollectNearest || seg.step.type == StepType.Shoot || seg.step.type == StepType.ScoreFlower))
            {
                var g = WorldToGui(seg.target);
                Outline(new Rect(g.x - 14, g.y - 14, 28, 28), new Color(1f, 0.6f, 0.1f), 3);
            }
        }
        else if (view == View.Simulating) info = $"SIMULATING {simTime:0.0}s  {simController?.CurrentLabel}   (physics, real timing)";
        else
        {
            var f = FrameAt(playhead);
            info = f != null ? $"REPLAY {playhead:0.00}s  {f.label}   held {f.held}   AUTO pts {f.autoPts}" : "";
        }
        Fill(new Rect(10, 72, 760, 34), new Color(0, 0, 0, 0.6f));
        GUI.Label(new Rect(20, 72, 760, 34), info, body);
        GUI.Label(new Rect(10, 770, 1380, 34), view == View.Edit
            ? "Click: add waypoint · drag dot: move · drag knob: heading (Alt = free) · wheel: rotate · right-click / Del: delete · Ctrl+click: insert after selected · Space: play"
            : "Space: play/pause · drag the timeline to scrub · B / BACK TO EDIT to change the plan", small);
        if (Time.unscaledTime < statusUntil)
        {
            Fill(new Rect(200, 116, 1000, 56), new Color(0, 0, 0, 0.8f));
            GUI.Label(new Rect(210, 116, 980, 56), status, banner);
        }
    }

    Frame FrameAt(float t)
    {
        if (frames.Count == 0) return null;
        foreach (var f in frames) if (f.t >= t) return f;
        return frames[frames.Count - 1];
    }

    void DrawPanel()
    {
        Fill(Panel, new Color(0.05f, 0.06f, 0.08f, 0.96f));
        var spec = plan.Spec;
        panelScroll = GUI.BeginScrollView(new Rect(Panel.x, Panel.y + 6, Panel.width, Panel.height - 12), panelScroll, new Rect(0, 0, Panel.width - 24, 1500));
        float x = 14, w = Panel.width - 52, y = 0;
        bool edit = view == View.Edit;

        
        if (compiled != null)
        {
            float dist = 0;
            foreach (var s in compiled.segs) if (s.traj != null) dist += s.traj.Length;
            string col = compiled.duration > Dims.AutoTime ? "#ff7070" : "#7cff7c";
            GUI.Label(new Rect(x, y, w, 26), $"Plan time <color={col}><b>{compiled.duration:0.00} s</b></color> / {Dims.AutoTime:0} s · path {dist:0.00} m", body); y += 32;
        }
        if (edit)
        {
            float acc = Stepper(new Rect(x, y, w, 34), "Acceleration used", plan.accelScale * spec.accel, 0.25f, 0.5f, spec.accel, "0.00");
            if (Mathf.Abs(acc / spec.accel - plan.accelScale) > 1e-4f) { plan.accelScale = acc / spec.accel; dirty = true; }
            y += 40;
            if (Button(new Rect(x, y, w, 36), "Snap START to nearest wall (G304)")) { SnapStart(); dirty = true; }
            y += 46;
        }

        // ---- selected waypoint
        if (selWp >= 0 && selWp < plan.waypoints.Count)
        {
            var wp = plan.waypoints[selWp];
            Fill(new Rect(0, y, Panel.width - 24, 2), new Color(1, 0.85f, 0.2f, 0.6f)); y += 8;
            GUI.Label(new Rect(x, y, w, 30), selWp == 0 ? "<b>START POSE</b>" : $"<b>WAYPOINT {selWp}</b>", h2); y += 34;
            GUI.Label(new Rect(x, y, w, 26), $"x {wp.x / Dims.IN:0.0} in   z {wp.z / Dims.IN:0.0} in   heading {wp.heading:0}°", body); y += 30;
            if (edit)
            {
                if (Button(new Rect(x, y, 90, 32), "-15°")) { wp.heading = Mathf.Repeat(wp.heading - 15, 360); dirty = true; }
                if (Button(new Rect(x + 96, y, 90, 32), "+15°")) { wp.heading = Mathf.Repeat(wp.heading + 15, 360); dirty = true; }
                if (selWp > 0 && Button(new Rect(x + w - 120, y, 120, 32), "DELETE")) { plan.waypoints.RemoveAt(selWp); selWp = -1; dirty = true; GUI.EndScrollView(); return; }
                y += 40;
                if (selWp > 0)
                {
                    float sp = Stepper(new Rect(x, y, w, 34), "Max speed into here (m/s)", wp.speed, 0.1f, 0.2f, spec.maxSpeed, "0.0");
                    if (sp != wp.speed) { wp.speed = sp; dirty = true; }
                    y += 40;
                    bool stop = wp.steps.Count > 0 || selWp == plan.waypoints.Count - 1 || wp.stop;
                    bool ns = Toggle(new Rect(x, y, w, 34), stop, "Stop here (off = drive through smoothly)");
                    if (ns != wp.stop && wp.steps.Count == 0) { wp.stop = ns; dirty = true; }
                    y += 40;
                    if (spec.drive == DriveType.Tank)
                    {
                        bool rv = Toggle(new Rect(x, y, w, 34), wp.reverse, "Drive backwards into this waypoint");
                        if (rv != wp.reverse) { wp.reverse = rv; dirty = true; }
                        y += 40;
                    }
                }
            }
            GUI.Label(new Rect(x, y, w, 28), "<b>Actions after arriving</b> (the robot stops)", body); y += 30;
            for (int i = 0; i < wp.steps.Count; i++)
            {
                var st = wp.steps[i];
                Fill(new Rect(x - 4, y - 2, w + 8, 76), new Color(1, 1, 1, 0.04f));
                GUI.Label(new Rect(x, y, w - 60, 28), $"{i + 1}. {st.Label}", body);
                if (edit && Button(new Rect(x + w - 50, y, 50, 30), "X")) { wp.steps.RemoveAt(i); dirty = true; break; }
                y += 34;
                if (edit && st.type != StepType.Park)
                {
                    string what = st.type == StepType.CollectNearest ? "Timeout" : st.type == StepType.ScoreFlower ? "Fire time" : "Duration";
                    float d = Stepper(new Rect(x, y, st.type == StepType.ScoreFlower ? w - 130 : w, 34), what + " (s)", st.duration, 0.25f, 0.25f, 15f, "0.00");
                    if (d != st.duration) { st.duration = d; dirty = true; }
                    if (st.type == StepType.ScoreFlower && Button(new Rect(x + w - 124, y, 124, 34), st.flower < 0 ? "nearest" : $"FLOWER {st.flower + 1}"))
                    { st.flower = st.flower >= 3 ? -1 : st.flower + 1; dirty = true; }
                }
                y += 42;
            }
            if (edit)
            {
                string[] names = { "Shoot", "Wait", "Collect nearest", "Score FLOWER", "Outtake", "Park" };
                for (int k = 0; k < names.Length; k++)
                {
                    float bx = x + (k % 3) * (w / 3f);
                    if (Button(new Rect(bx, y + (k / 3) * 40, w / 3f - 6, 34), "+ " + names[k], btnSmall))
                    {
                        var st = new PlanStep { type = (StepType)k, duration = k == 2 ? 3f : k == 3 ? 1.5f : k == 0 ? (spec.barrels > 1 ? 1.2f : 2f) : 1f };
                        wp.steps.Add(st);
                        wp.stop = true;
                        dirty = true;
                    }
                }
                y += 86;
            }
        }
        else
        {
            GUI.Label(new Rect(x, y, w, 60), "Select a waypoint on the field to edit its speed, stop behaviour and actions.", small);
            y += 64;
        }

        // ---- markers
        Fill(new Rect(0, y, Panel.width - 24, 2), new Color(0.3f, 0.8f, 1f, 0.6f)); y += 8;
        GUI.Label(new Rect(x, y, w, 30), "<b>TIMELINE MARKERS</b> (run while moving)", body); y += 34;
        if (edit)
        {
            string[] mnames = { "Intake", "Fire on move", "FLOWER mech", "Outtake" };
            for (int k = 0; k < 4; k++)
                if (Button(new Rect(x + (k % 2) * (w / 2f), y + (k / 2) * 40, w / 2f - 6, 34), $"+ {mnames[k]} @ {playhead:0.0}s", btnSmall))
                {
                    plan.markers.Add(new PlanMarker { type = (MarkerType)k, time = playhead, duration = k == 0 ? 2f : 1.5f });
                    selMarker = plan.markers.Count - 1;
                }
            y += 86;
        }
        for (int i = 0; i < plan.markers.Count; i++)
        {
            var mk = plan.markers[i];
            Fill(new Rect(x - 4, y - 2, w + 8, 36), i == selMarker ? new Color(1, 0.85f, 0.2f, 0.12f) : new Color(1, 1, 1, 0.03f));
            GUI.Label(new Rect(x, y + 3, 260, 28), $"{mk.Label}  {mk.time:0.0}–{mk.time + mk.duration:0.0}s", small);
            if (edit)
            {
                if (Button(new Rect(x + w - 214, y, 50, 30), "-")) mk.duration = Mathf.Max(0.25f, mk.duration - 0.25f);
                if (Button(new Rect(x + w - 160, y, 50, 30), "+")) mk.duration += 0.25f;
                if (Button(new Rect(x + w - 106, y, 50, 30), "@")) mk.time = playhead;
                if (Button(new Rect(x + w - 50, y, 50, 30), "X")) { plan.markers.RemoveAt(i); selMarker = -1; break; }
            }
            y += 38;
        }

        // ---- warnings
        if (compiled != null && compiled.warnings.Count > 0)
        {
            y += 8;
            GUI.Label(new Rect(x, y, w, 28), "<b><color=#ffd24d>CHECKS</color></b>", body); y += 28;
            var seen = new HashSet<string>();
            foreach (var wn in compiled.warnings)
            {
                if (!seen.Add(wn)) continue;
                GUI.Label(new Rect(x, y, w, 46), "• " + wn, small);
                y += 44;
            }
        }
        GUI.EndScrollView();
    }

    void SnapStart()
    {
        var w = plan.waypoints[0];
        var spec = plan.Spec;
        float hl = spec.lengthIn * 0.5f * Dims.IN + 0.002f;
        float dx = Dims.Half - Mathf.Abs(w.x), dz = Dims.Half - Mathf.Abs(w.z);
        if (dx < dz) { w.x = Mathf.Sign(w.x) * (Dims.Half - hl); w.heading = w.x < 0 ? 90f : 270f; }
        else { w.z = Mathf.Sign(w.z) * (Dims.Half - hl); w.heading = w.z < 0 ? 0f : 180f; }
        // keep it on our side
        float s = plan.alliance.Sign();
        float minX = spec.widthIn * 0.5f * Dims.IN + 0.02f;
        if (w.x * s < minX) w.x = s * Mathf.Max(minX, Mathf.Abs(w.x));
    }

    // ------------------------------------------------------------------ timeline
    float TimelineLength => view == View.Edit ? Mathf.Max(Dims.AutoTime, (compiled?.duration ?? 0) + 1f) : Dims.AutoTime;
    float TX(float t) => LaneX + t / TimelineLength * LaneW;
    float XT(float x) => Mathf.Clamp((x - LaneX) / LaneW * TimelineLength, 0, TimelineLength);

    void DrawTimeline()
    {
        Fill(Timeline, new Color(0.035f, 0.04f, 0.055f, 0.98f));
        float y0 = Timeline.y + 8;
        bool edit = view == View.Edit;

        if (Button(new Rect(20, y0, 110, 40), playing ? "PAUSE" : "PLAY") && view != View.Simulating) playing = !playing;
        if (Button(new Rect(136, y0, 60, 40), "|<")) { playhead = 0; }
        if (Button(new Rect(202, y0, 90, 40), $"x{playSpeed:0.##}")) playSpeed = playSpeed >= 2f ? 0.25f : playSpeed * 2f;
        float len = view == View.Replay && frames.Count > 0 ? frames[frames.Count - 1].t : compiled != null ? compiled.duration : 0f;
        GUI.Label(new Rect(304, y0 + 6, 220, 32), $"<b>{playhead:0.00}</b> / {len:0.00} s", body);

        if (view == View.Edit)
        {
            if (Button(new Rect(540, y0, 330, 40), "RUN PHYSICS SIMULATION")) StartSimulation();
            GUI.Label(new Rect(890, y0 + 6, 780, 32), "Preview uses the planned trajectory; the simulation runs the real robot physics for 30 s.", small);
        }
        else if (view == View.Simulating)
        {
            if (Button(new Rect(540, y0, 220, 40), "STOP")) EnterEdit();
            GUI.Label(new Rect(780, y0 + 6, 900, 32), $"Simulating... {simTime:0.0} s  ·  shots {simShots}  ·  TIPS {simTips}", body);
        }
        else
        {
            if (Button(new Rect(540, y0, 220, 40), "BACK TO EDIT")) EnterEdit();
            if (Button(new Rect(770, y0, 220, 40), "RUN AGAIN")) StartSimulation();
            GUI.Label(new Rect(1010, y0 + 6, 880, 32), $"Result: <b>{simAuto}</b> AUTO points · {simTips} TIPS · {simShots} shots · planned {compiled?.duration:0.0} s", body);
        }

        // ruler
        float ry = y0 + 56;
        Rect ruler = new Rect(LaneX, ry, LaneW, 26);
        Fill(ruler, new Color(1, 1, 1, 0.05f));
        float T = TimelineLength;
        for (int s = 0; s <= Mathf.FloorToInt(T); s++)
        {
            float px = TX(s);
            bool major = s % 5 == 0;
            Fill(new Rect(px, ry + (major ? 0 : 14), 1, major ? 26 : 12), new Color(1, 1, 1, major ? 0.5f : 0.25f));
            if (major) GUI.Label(new Rect(px + 3, ry - 2, 40, 22), s.ToString(), tiny);
        }
        // AUTO limit
        float limX = TX(Dims.AutoTime);
        if (limX < LaneX + LaneW) Fill(new Rect(limX, ry, LaneX + LaneW - limX, 190), new Color(1f, 0.2f, 0.2f, 0.12f));
        Fill(new Rect(limX - 1, ry, 2, 190), new Color(1f, 0.3f, 0.3f, 0.9f));

        // sequence lane
        float sy = ry + 34;
        GUI.Label(new Rect(4, sy + 6, 56, 30), "PATH", tiny);
        Fill(new Rect(LaneX, sy, LaneW, 44), new Color(1, 1, 1, 0.03f));
        if (compiled != null)
            foreach (var seg in compiled.segs)
            {
                Rect r = new Rect(TX(seg.t0), sy + 2, Mathf.Max(3f, TX(seg.t1) - TX(seg.t0)), 40);
                Color c = SegColor(seg);
                Fill(r, c);
                if (seg.overTime) Outline(r, Color.red, 2);
                if (seg.waypoint == selWp) Outline(r, new Color(1f, 0.9f, 0.2f), 2);
                if (r.width > 40) GUI.Label(new Rect(r.x + 4, r.y + 2, r.width - 6, r.height - 4), seg.label, tiny);
            }

        // markers lane
        float my = sy + 52;
        GUI.Label(new Rect(4, my + 6, 56, 30), "MARK", tiny);
        Fill(new Rect(LaneX, my, LaneW, 44), new Color(1, 1, 1, 0.03f));
        for (int i = 0; i < plan.markers.Count; i++)
        {
            var mk = plan.markers[i];
            Rect r = MarkerRect(mk, i, my);
            Fill(r, mk.type == MarkerType.ShootOnMove ? new Color(0.95f, 0.35f, 0.2f, 0.9f) : mk.type == MarkerType.Intake ? new Color(0.2f, 0.75f, 0.35f, 0.9f) : new Color(0.6f, 0.4f, 0.9f, 0.9f));
            Fill(new Rect(r.xMax - 6, r.y, 6, r.height), new Color(1, 1, 1, 0.5f));
            if (i == selMarker) Outline(r, new Color(1f, 0.9f, 0.2f), 2);
            if (r.width > 40) GUI.Label(new Rect(r.x + 4, r.y, r.width - 10, r.height), mk.Label, tiny);
        }

        // events lane (simulation)
        float ey = my + 52;
        GUI.Label(new Rect(4, ey + 2, 56, 26), "SIM", tiny);
        Fill(new Rect(LaneX, ey, LaneW, 28), new Color(1, 1, 1, 0.03f));
        foreach (var st in shotTimes) Fill(new Rect(TX(st) - 1, ey + 4, 3, 20), new Color(1f, 0.85f, 0.2f));
        foreach (var tt in tipTimes) { Fill(new Rect(TX(tt) - 3, ey, 7, 28), plan.alliance.Col()); GUI.Label(new Rect(TX(tt) + 5, ey + 2, 60, 24), "TIP", tiny); }
        if (view == View.Edit && frames.Count == 0) GUI.Label(new Rect(LaneX + 8, ey + 2, 700, 24), "shots and TIPS appear here after a simulation", tiny);

        // playhead
        float phx = TX(playhead);
        Fill(new Rect(phx - 1, ry - 4, 3, ey + 30 - ry + 4), new Color(1f, 1f, 1f, 0.95f));
        Fill(new Rect(phx - 7, ry - 8, 15, 10), Color.white);

        HandleTimelineMouse(ruler, sy, my, ey);
    }

    Rect MarkerRect(PlanMarker mk, int i, float my) => new Rect(TX(mk.time), my + 4 + (i % 2) * 18, Mathf.Max(8f, TX(mk.time + mk.duration) - TX(mk.time)), 18);

    static Color SegColor(PlanSeg s)
    {
        if (s.kind == SegKind.Drive) return new Color(0.15f, 0.45f, 0.85f, 0.9f);
        switch (s.step.type)
        {
            case StepType.Shoot: return new Color(0.95f, 0.55f, 0.15f, 0.9f);
            case StepType.CollectNearest: return new Color(0.2f, 0.7f, 0.35f, 0.9f);
            case StepType.ScoreFlower: return new Color(0.65f, 0.35f, 0.85f, 0.9f);
            case StepType.Park: return new Color(0.15f, 0.65f, 0.65f, 0.9f);
            default: return new Color(0.45f, 0.45f, 0.5f, 0.9f);
        }
    }

    void HandleTimelineMouse(Rect ruler, float sy, float my, float ey)
    {
        var e = Event.current;
        if (e.button != 0) return;
        Vector2 m = e.mousePosition;
        if (e.type == EventType.MouseDown)
        {
            if (view != View.Simulating && m.y >= ruler.y - 8 && m.y <= ruler.yMax && m.x >= LaneX - 10 && m.x <= LaneX + LaneW + 10)
            { drag = Drag.Scrub; playing = false; playhead = XT(m.x); e.Use(); return; }
            if (view == View.Edit && m.y >= my && m.y <= my + 44)
            {
                for (int i = plan.markers.Count - 1; i >= 0; i--)
                {
                    Rect r = MarkerRect(plan.markers[i], i, my);
                    if (!r.Contains(m)) continue;
                    selMarker = i;
                    drag = m.x > r.xMax - 8 ? Drag.MarkerResize : Drag.MarkerMove;
                    dragOffsetT = XT(m.x) - plan.markers[i].time;
                    e.Use();
                    return;
                }
            }
            if (m.y >= sy && m.y <= sy + 44 && compiled != null && view != View.Simulating)
            {
                float t = XT(m.x);
                foreach (var seg in compiled.segs) if (t >= seg.t0 && t < seg.t1) { selWp = seg.waypoint; dirty = view == View.Edit; }
                playhead = t;
                playing = false;
                e.Use();
            }
            if (m.y >= ey && m.y <= ey + 28 && view != View.Simulating) { drag = Drag.Scrub; playing = false; playhead = XT(m.x); e.Use(); }
        }
        else if (e.type == EventType.MouseDrag)
        {
            if (drag == Drag.Scrub) { playhead = XT(m.x); if (view == View.Replay) playhead = Mathf.Min(playhead, frames.Count > 0 ? frames[frames.Count - 1].t : 0); e.Use(); }
            else if (drag == Drag.MarkerMove && selMarker >= 0) { plan.markers[selMarker].time = Mathf.Max(0, XT(m.x) - dragOffsetT); e.Use(); }
            else if (drag == Drag.MarkerResize && selMarker >= 0) { var mk = plan.markers[selMarker]; mk.duration = Mathf.Max(0.25f, XT(m.x) - mk.time); e.Use(); }
        }
        else if (e.type == EventType.MouseUp)
        {
            if (drag == Drag.Scrub || drag == Drag.MarkerMove || drag == Drag.MarkerResize) { drag = Drag.None; e.Use(); }
        }
    }

    // ------------------------------------------------------------------ autotest hooks
    public AutoPlan Plan { get => plan; set { plan = value; if (Active == this) EnterEdit(); } }
    public CompiledPlan Compiled { get { if (dirty) Recompile(); return compiled; } }
    public void TestRunSimulation() => StartSimulation();
    public bool TestSimDone => view == View.Replay;
    public int TestAutoPoints => simAuto;
    public int TestTips => simTips;
    public int TestShots => simShots;
    public void TestScrub(float t) { playing = false; playhead = t; }
    public Vector3 TestRobotPos => robot ? robot.transform.position : Vector3.zero;
    public float TestRobotYaw => robot ? robot.transform.eulerAngles.y : 0f;
    public string TestLabel => simController?.CurrentLabel ?? "";
}
