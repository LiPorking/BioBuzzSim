using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

// Command-line self test:  BioBuzzSim.exe -autotest <outDir> [-timescale N]
// Plays a full 4-AI match, logs state every few seconds and saves screenshots, then quits.
public class AutoTest : MonoBehaviour
{
    string dir;
    readonly StringBuilder sb = new StringBuilder();

    public static void TryStart(Game g)
    {
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] != "-autotest") continue;
            var t = g.gameObject.AddComponent<AutoTest>();
            t.dir = i + 1 < args.Length ? args[i + 1] : "autotest";
            return;
        }
    }

    float Arg(string name, float def)
    {
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)) return v;
        return def;
    }

    IEnumerator Start()
    {
        Directory.CreateDirectory(dir);
        Application.logMessageReceived += (c, st, type) => { if (type == LogType.Exception || type == LogType.Error) sb.AppendLine($"[{type}] {c}\n{st}"); };
        yield return new WaitForSecondsRealtime(1.0f);
        Shot("00_menu");

        for (int i = 0; i < 4; i++) GameConfig.slots[i].control = ControlType.AI;
        if (Arg("-pads", 0) > 0)
        {
            float until = Time.realtimeSinceStartup + Arg("-seconds", 4f);
            foreach (var d in UnityEngine.InputSystem.InputSystem.devices) sb.AppendLine($"DEVICE {d.displayName} layout={d.layout} path={d.path}");
            foreach (var n in Bindings.ConnectedControllers()) sb.AppendLine("GAMEPAD " + n);
            while (Time.realtimeSinceStartup < until)
            {
                var gp = Bindings.GamepadFor(1);
                if (gp != null && (gp.wasUpdatedThisFrame || Time.frameCount % 60 == 0))
                    sb.AppendLine($"t={Time.realtimeSinceStartup:0.0} L={Bindings.Stick(1, false)} R={Bindings.Stick(1, true)} RT={Bindings.Held(Act.Fire, 1)} LT={Bindings.Held(Act.Intake, 1)} A={Bindings.PadHeld(1, Pad.A)}");
                yield return null;
            }
            File.WriteAllText(Path.Combine(dir, "autotest.txt"), sb.ToString());
            Application.Quit();
            yield break;
        }
        if (Arg("-aimcheck", 0) > 0)
        {
            // human-controlled turret robot runs the built-in AUTO, then TELEOP: aim must follow the up CELL
            GameConfig.slots[0].control = ControlType.Keyboard;
            GameConfig.slots[0].robot = 4;
            Game.I.StartGame(false);
            var m2 = MatchManager.I;
            Time.timeScale = 4f;
            while (!(m2.period == Period.Teleop && m2.t > 1f)) yield return null;
            var r1 = m2.FocusRobot();
            var hive = Field.HiveOf(r1.alliance);
            sb.AppendLine($"teleop: aiTarget={(r1.aiTarget.HasValue ? r1.aiTarget.Value.ToString() : "null")} target={r1.CurrentTarget()} upCell={hive.AimPoint()} upEnd={hive.upEnd}");
            // tip the HIVE the other way and check the target follows
            int before = hive.upEnd;
            foreach (var e in GameElement.All)
                if (e.IsFree && e.type == ElementType.Pollen && e.transform.position.y < 0.1f)
                {
                    e.Place(hive.AimPoint());
                    e.rb.velocity = Vector3.zero;
                }
            float until = Time.time + 4f;
            while (hive.upEnd == before && Time.time < until) yield return null;
            yield return new WaitForSeconds(1f);
            sb.AppendLine($"after tip: upEnd {before}->{hive.upEnd} aiTarget={(r1.aiTarget.HasValue ? r1.aiTarget.Value.ToString() : "null")} target={r1.CurrentTarget()} upCell={hive.AimPoint()} match={(Vector3.Distance(r1.CurrentTarget(), hive.AimPoint()) < 0.01f)}");
            Time.timeScale = 1f;
            File.WriteAllText(Path.Combine(dir, "autotest.txt"), sb.ToString());
            Application.Quit();
            yield break;
        }
        if (Arg("-feedcheck", 0) > 0)
        {
            GameConfig.slots[0].control = ControlType.Keyboard;
            Game.I.StartGame(true);
            yield return new WaitForSecondsRealtime(1.5f);
            var r1 = MatchManager.I.FocusRobot();
            foreach (var al in new[] { Alliance.Red, Alliance.Blue })
            {
                r1.alliance = al;
                foreach (var p in new[] { new Vector3(0.8f, 0, 0.8f), new Vector3(-0.8f, 0, 0.8f), new Vector3(-0.8f, 0, -0.8f), new Vector3(0.8f, 0, -0.8f) })
                {
                    r1.transform.position = p;
                    string q = (p.z > 0 ? "top" : "bottom") + " " + (p.x < 0 ? "red" : "blue");
                    var f = r1.FeedPoint();
                    string d = (f.z > 0 ? "top" : "bottom") + " " + (f.x < 0 ? "red" : "blue");
                    sb.AppendLine($"{al} robot in {q} -> feeds to {d}  {f}");
                }
            }
            r1.alliance = Alliance.Red;
            Game.I.rig.mode = CameraRig.Mode.Audience;
            yield return new WaitForSecondsRealtime(0.5f);
            Shot("f1_hud");
            yield return new WaitForSecondsRealtime(0.5f);
            MatchManager.I.redScore = new Breakdown { leave = 6, autoTips = 40, teleTips = 60, cell = 8, ownedFlower = 16, bottomNectar = 10, garden = 3, telePark = 10, fouls = 5, rp = 4 };
            MatchManager.I.blueScore = new Breakdown { leave = 3, autoTips = 20, teleTips = 40, cell = 4, ownedFlower = 6, bottomNectar = 5, garden = 2, telePark = 5, rp = 1 };
            MatchManager.I.period = Period.Results;
            yield return new WaitForSecondsRealtime(0.6f);
            Shot("f2_results");
            yield return new WaitForSecondsRealtime(0.4f);
            File.WriteAllText(Path.Combine(dir, "autotest.txt"), sb.ToString());
            Application.Quit();
            yield break;
        }
        if (Arg("-flowertest", 0) > 0)
        {
            GameConfig.slots[0].control = ControlType.Keyboard;
            GameConfig.slots[0].robot = (int)Arg("-probot", 4);
            Game.I.StartGame(true);
            yield return new WaitForSecondsRealtime(1.5f);
            var mmf = MatchManager.I;
            var bot = mmf.FocusRobot();
            bot.controller = new ShootOnly();
            bot.targetFlower = true;
            Flower target = null;
            foreach (var f in Flower.All) if (f.index == 3) target = f;   // FLOWER on the red wall
            int scored = 0, shots = 0;
            float[] dists = { 0.6f, 0.8f, 1.0f, 1.3f };
            float[] sideways = { 0f, 0.35f, -0.35f };
            foreach (float dist in dists)
            foreach (float side in sideways)
            foreach (var type in new[] { ElementType.Pollen, ElementType.Nectar })
            {
                // clear the robot, put one element in it and park it facing sideways (turret must aim)
                while (bot.held.Count > 0) { var h = bot.held[0]; bot.Drop(h); h.gameObject.SetActive(false); }
                Vector3 side3 = Vector3.Cross(Vector3.up, target.inward) * side;
                Vector3 pos = target.transform.position + target.inward * dist + side3;
                bot.rb.velocity = Vector3.zero; bot.rb.angularVelocity = Vector3.zero;
                bot.rb.position = new Vector3(pos.x, 0, pos.z);
                bot.rb.rotation = Quaternion.Euler(0, 20f, 0);
                bot.transform.SetPositionAndRotation(bot.rb.position, bot.rb.rotation);
                var el = GameElement.Create(Game.World, type, Alliance.Red, bot.transform.position + Vector3.up * 0.3f);
                bot.Take(el);
                shots++;
                float until = Time.time + 4f;
                string rel = "";
                Robot.OnShot = (rr, ee, tt, sp) => { if (ee == el) { var so = rr.Solve(tt); rel = $"pitch={so.pitch:0.0} v={sp:0.00} yaw={so.yaw:0.0} turret={rr.Yaw + rr.turretYaw:0.0} from={ee.transform.position} tgt={tt}"; } };
                while (Time.time < until && bot.held.Contains(el)) yield return null;
                float land = Time.time + 2.5f;
                bool inside = false;
                float closest = 99f; Vector3 atRing = Vector3.zero; bool crossed = false;
                Vector3 prev = el.transform.position;
                while (Time.time < land)
                {
                    var p = el.transform.position;
                    if (target.HorizDist(p) < 2.2f * Dims.IN && p.y < Dims.FlowerTopHeight + 0.02f && p.y > 0.02f) inside = true;
                    float ringY = target.TopCenter.y;
                    if (!crossed && prev.y >= ringY && p.y < ringY) { crossed = true; atRing = p; closest = target.HorizDist(p); }
                    prev = p;
                    yield return new WaitForFixedUpdate();
                }
                sb.AppendLine($"   {rel} crossRingPlane={(crossed ? (closest * 100f).ToString("0.0") + "cm from centre at " + atRing : "never")}");
                if (inside) scored++;
                sb.AppendLine($"dist={dist:0.0} side={side:0.00} {type}: {(inside ? "IN" : "miss")}  final={el.transform.position}");
                el.gameObject.SetActive(false);
            }
            sb.AppendLine($"FLOWER TEST {RobotLibrary.All[bot.spec == null ? 0 : GameConfig.slots[0].robot].name}: {scored}/{shots}");
            File.WriteAllText(Path.Combine(dir, "autotest.txt"), sb.ToString());
            Application.Quit();
            yield break;
        }
        if (Arg("-controls", 0) > 0)
        {
            GameUI.testPage = GameUI.Page.Controls;
            yield return new WaitForSecondsRealtime(0.6f);
            Shot("c1_controls");
            yield return new WaitForSecondsRealtime(0.4f);
            GameUI.testPage = GameUI.Page.Main;
            yield return new WaitForSecondsRealtime(0.6f);
            Shot("c2_menu");
            yield return new WaitForSecondsRealtime(0.4f);
            GameUI.testRobotPopup = true;
            yield return new WaitForSecondsRealtime(0.6f);
            Shot("c3_popup");
            yield return new WaitForSecondsRealtime(0.4f);
            Application.Quit();
            yield break;
        }
        if (Arg("-planner", 0) > 0)
        {
            yield return PlannerTest((int)Arg("-probot", 0));
            File.WriteAllText(Path.Combine(dir, "autotest.txt"), sb.ToString());
            Application.Quit();
            yield break;
        }
        if (Arg("-flow", 0) > 0)
        {
            // exercise restart / menu / practice transitions
            GameConfig.slots[0].control = ControlType.Keyboard;
            Game.I.StartGame(false);
            yield return new WaitForSecondsRealtime(5f);
            Game.I.Restart();
            yield return new WaitForSecondsRealtime(4f);
            sb.AppendLine($"after restart: period={MatchManager.I.period} robots={MatchManager.I.robots.Count} elements={GameElement.All.Count} flowers={Flower.All.Count}");
            Game.I.BackToMenu();
            yield return new WaitForSecondsRealtime(1f);
            sb.AppendLine($"menu: period={MatchManager.I.period} robots={MatchManager.I.robots.Count} elements={GameElement.All.Count} flowers={Flower.All.Count}");
            Game.I.StartGame(true);
            yield return new WaitForSecondsRealtime(3f);
            var mmp = MatchManager.I;
            foreach (var a in new[] { Alliance.Red, Alliance.Blue }) mmp.RequestNectar(a, true);
            yield return new WaitForSecondsRealtime(2f);
            sb.AppendLine($"practice: period={mmp.period} robots={mmp.robots.Count} elements={GameElement.All.Count} nectarR={mmp.Red.nectarInArea} score R{mmp.redScore.Total} B{mmp.blueScore.Total}");
            Game.I.rig.mode = CameraRig.Mode.Follow;
            yield return new WaitForSecondsRealtime(1f);
            Shot("flow_practice");
            yield return new WaitForSecondsRealtime(0.5f);
            File.WriteAllText(Path.Combine(dir, "autotest.txt"), sb.ToString());
            Application.Quit();
            yield break;
        }
        bool human = Arg("-human", 0) > 0;
        if (human) GameConfig.slots[0].control = ControlType.Keyboard;
        GameConfig.slots[0].robot = (int)Arg("-r1", 0);
        GameConfig.slots[1].robot = (int)Arg("-r2", 1);
        GameConfig.slots[2].robot = (int)Arg("-b1", 2);
        GameConfig.slots[3].robot = (int)Arg("-b2", 3);
        Game.I.StartGame(false);
        var mm = MatchManager.I;
        for (int k = 0; k < 12; k++)
        {
            yield return new WaitForFixedUpdate();
            foreach (var r in mm.robots) if (r.station == 2 && r.alliance == Alliance.Red) sb.AppendLine($"spawn k={k} {r.label} pos={r.transform.position.x:0.000},{r.transform.position.y:0.000},{r.transform.position.z:0.000} yaw={r.transform.eulerAngles.y:0.0} v={r.rb.velocity} contacts=[{string.Join(",", r.contacts)}]");
        }
        var rig = Game.I.rig;

        if (human)
        {
            rig.mode = CameraRig.Mode.DriverStation;
            yield return new WaitForSecondsRealtime(4.5f);   // into AUTO
            Shot("h1_driverstation");
            yield return new WaitForSecondsRealtime(0.3f);
            rig.mode = CameraRig.Mode.ThirdPerson;
            yield return new WaitForSecondsRealtime(1.0f);
            Shot("h2_thirdperson");
            yield return new WaitForSecondsRealtime(0.3f);
            yield return new WaitForSecondsRealtime(0.3f);
            File.WriteAllText(Path.Combine(dir, "autotest.txt"), sb.ToString());
            Application.Quit();
            yield break;
        }
        rig.mode = CameraRig.Mode.Audience;
        yield return new WaitForSecondsRealtime(0.5f);
        Shot("01_start_audience");
        rig.mode = CameraRig.Mode.Overhead;
        yield return null;
        Shot("02_start_overhead");
        rig.mode = CameraRig.Mode.Audience;

        Robot.OnShot = (r, e, target, speed) => StartCoroutine(TrackShot(r, e, target, speed, mm));
        SoundFx.OnPlay = n => { if (n != "shot") sb.AppendLine($"SOUND {n} period={mm.period} t={mm.t:0.00} left={mm.MatchTimeLeft:0.0}"); };
        Time.timeScale = Arg("-timescale", 2f);
        float nextLog = 0;
        int shot = 3;
        float[] shotTimes = { 140f, 125f, 100f, 70f, 45f, 15f };
        int si = 0;
        int closeUps = 0;
        while (mm.period != Period.Results)
        {
            if (Time.time >= nextLog)
            {
                nextLog = Time.time + 5f;
                Snapshot(mm);
            }
            if (si < shotTimes.Length && mm.period != Period.PreMatch && mm.MatchTimeLeft <= shotTimes[si])
            {
                var m = si % 2 == 0 ? CameraRig.Mode.Audience : CameraRig.Mode.Overhead;
                rig.mode = m;
                yield return null;
                Shot($"{shot++:00}_{Mathf.CeilToInt(mm.MatchTimeLeft)}s_{m}");
                si++;
            }
            if (closeUps < 3 && mm.period == Period.Teleop && mm.MatchTimeLeft < 90f - closeUps * 25f)
            {
                closeUps++;
                rig.enabled = false;
                foreach (var r in mm.robots)
                {
                    var cam = Camera.main.transform;
                    cam.position = r.transform.position + new Vector3(0.0f, 1.1f, -0.55f);
                    cam.LookAt(r.transform.position);
                    yield return null;
                    Shot($"close_{closeUps}_{r.label.Replace(' ', '_')}");
                    yield return null;
                    sb.AppendLine($"CLOSEUP {closeUps} {r.label} pos={r.transform.position} yaw={r.transform.eulerAngles.y:0} angVel={r.rb.angularVelocity.y:0.00} sleeping={r.rb.IsSleeping()} kin={r.rb.isKinematic} cmd=({r.cmd.forward:0.00},{r.cmd.turn:0.00}) held={r.held.Count}");
                }
                rig.enabled = true;
            }
            if (Time.realtimeSinceStartup > 400f) { sb.AppendLine("TIMEOUT"); break; }
            yield return null;
        }
        Time.timeScale = 1f;
        Snapshot(mm);
        yield return new WaitForSecondsRealtime(0.5f);
        rig.mode = CameraRig.Mode.Audience;
        Shot("99_results");
        sb.AppendLine("LOG:");
        foreach (var l in mm.log) sb.AppendLine("  " + l);
        File.WriteAllText(Path.Combine(dir, "autotest.txt"), sb.ToString());
        yield return new WaitForSecondsRealtime(0.3f);
        Application.Quit();
    }

    IEnumerator PlannerTest(int robotIndex)
    {
        Game.I.StartPlanner();
        yield return null;
        var p = Game.I.GetComponent<PlannerUI>();
        var spec = RobotLibrary.All[robotIndex];
        float hl = spec.lengthIn * 0.5f * Dims.IN;
        var plan = new AutoPlan { name = "autotest_" + robotIndex, robot = robotIndex, alliance = Alliance.Red };
        plan.waypoints.Add(new PlanWaypoint { x = -Dims.Half + hl + 0.002f, z = 0f, heading = 90f });
        var shoot1 = new PlanWaypoint { x = -1.2f, z = -1.45f, heading = spec.turret ? 45f : 0f, speed = 1.3f };
        shoot1.steps.Add(new PlanStep { type = StepType.Shoot, duration = spec.barrels > 1 ? 1.4f : 2.2f });
        plan.waypoints.Add(new PlanWaypoint { x = -1.1f, z = -0.55f, heading = 160f, speed = 1.3f, stop = false });
        plan.waypoints.Add(shoot1);
        var collect = new PlanWaypoint { x = -1.25f, z = -1.3f, heading = 250f, speed = 1.0f };
        for (int i = 0; i < 3; i++) collect.steps.Add(new PlanStep { type = StepType.CollectNearest, duration = 3f });
        plan.waypoints.Add(collect);
        var shoot2 = new PlanWaypoint { x = -1.2f, z = -1.45f, heading = spec.turret ? 90f : 0f, speed = 1.3f };
        shoot2.steps.Add(new PlanStep { type = StepType.Shoot, duration = 2f });
        shoot2.steps.Add(new PlanStep { type = StepType.Park });
        plan.waypoints.Add(shoot2);
        if (spec.turret) plan.markers.Add(new PlanMarker { type = MarkerType.ShootOnMove, time = 0.5f, duration = 2.5f });
        p.Plan = plan;
        yield return null;
        var cp = p.Compiled;
        sb.AppendLine($"PLAN robot={spec.name} duration={cp.duration:0.00}s segs={cp.segs.Count}");
        foreach (var s in cp.segs)
            sb.AppendLine($"  seg {s.kind} {s.label} t0={s.t0:0.00} t1={s.t1:0.00} trajLen={(s.traj != null ? s.traj.Length : 0):0.00} trajT={(s.traj != null ? s.traj.Duration : 0):0.00}");
        foreach (var w in cp.warnings) sb.AppendLine("  WARN " + w);
        yield return new WaitForSecondsRealtime(0.4f);
        Shot("p1_edit");
        p.TestScrub(cp.duration * 0.35f);
        yield return new WaitForSecondsRealtime(0.3f);
        Shot("p2_edit_scrub");

        float t0 = Time.realtimeSinceStartup;
        p.TestRunSimulation();
        float nextLog = 0;
        while (!p.TestSimDone && Time.realtimeSinceStartup - t0 < 90f)
        {
            var mm = MatchManager.I;
            if (mm.t >= nextLog)
            {
                nextLog = mm.t + 1f;
                // compare with the plan preview at the same time
                cp.Eval(mm.t, out var pp, out var ph, out var seg);
                var rp = p.TestRobotPos;
                sb.AppendLine($"  sim t={mm.t:0.0} task='{p.TestLabel}' plannedSeg='{seg?.label}' robot=({rp.x:0.00},{rp.z:0.00}) yaw={p.TestRobotYaw:0} planned=({pp.x:0.00},{pp.y:0.00}) h={ph:0} err={Vector2.Distance(pp, new Vector2(rp.x, rp.z)):0.00}");
            }
            if (Mathf.Abs(mm.t - 12f) < 0.02f) Shot("p3_simulating");
            yield return null;
        }
        sb.AppendLine($"SIM done={p.TestSimDone} autoPts={p.TestAutoPoints} tips={p.TestTips} shots={p.TestShots}");
        yield return new WaitForSecondsRealtime(0.5f);
        p.TestScrub(8f);
        yield return new WaitForSecondsRealtime(0.3f);
        Shot("p4_replay");
    }

    IEnumerator TrackShot(Robot r, GameElement e, Vector3 target, float speed, MatchManager mm)
    {
        Vector3 from = e.transform.position;
        float best = 99f; Vector3 bestP = from;
        float t0 = Time.time;
        var hive = Field.HiveOf(r.alliance);
        int end = hive.upEnd;
        bool inCell = false;
        while (Time.time - t0 < 2.5f && e != null && e.IsFree)
        {
            float d = Vector3.Distance(e.transform.position, target);
            if (d < best) { best = d; bestP = e.transform.position; }
            if (hive.InCell(e, end)) inCell = true;
            yield return new WaitForFixedUpdate();
        }
        bool stays = e != null && hive.InCell(e, end);
        sb.AppendLine($"SHOT {r.label} {mm.MatchTimeLeft:0.0}s from=({from.x:0.00},{from.y:0.00},{from.z:0.00}) target=({target.x:0.00},{target.y:0.00},{target.z:0.00}) dist={Util.Flat(target - from).magnitude:0.00} v={speed:0.00} miss={best:0.000} at=({bestP.x:0.00},{bestP.y:0.00},{bestP.z:0.00}) enteredCell={inCell} stayed={stays} yawErr={r.AlignError(target):0.0}");
    }

    class ShootOnly : IRobotController
    {
        public RobotCommand Tick(Robot r) => new RobotCommand { shoot = true };
    }

    void Shot(string name)
    {
        ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
    }

    void Snapshot(MatchManager mm)
    {
        sb.AppendLine($"--- {mm.period} t={mm.t:0.0} left={mm.MatchTimeLeft:0.0}  RED {mm.redScore.Total} (tips {mm.redScore.tipCount}, cell {mm.redScore.cell}, garden {mm.redScore.garden}, flower {mm.redScore.ownedFlower}+{mm.redScore.bottomNectar})  BLUE {mm.blueScore.Total} (tips {mm.blueScore.tipCount}, cell {mm.blueScore.cell}, garden {mm.blueScore.garden}, flower {mm.blueScore.ownedFlower}+{mm.blueScore.bottomNectar})");
        sb.AppendLine($"    hive red up={Field.RedHive.upEnd} w={Field.RedHive.WeightInCell(Field.RedHive.upEnd, false):0.0}  blue up={Field.BlueHive.upEnd} w={Field.BlueHive.WeightInCell(Field.BlueHive.upEnd, false):0.0}  nectar R{mm.Red.nectarInArea} B{mm.Blue.nectarInArea}  elements={GameElement.All.Count}");
        foreach (var r in mm.robots)
        {
            var p = r.transform.position;
            sb.AppendLine($"    {r.label,-7} {r.spec.name,-36} pos=({p.x:0.00},{p.y:0.00},{p.z:0.00}) yaw={r.transform.eulerAngles.y:0} held={r.held.Count} v={r.rb.velocity.magnitude:0.00} cmd=({r.cmd.forward:0.00},{r.cmd.strafe:0.00},{r.cmd.turn:0.00}{(r.cmd.intake ? " I" : "")}{(r.cmd.shoot ? " S" : "")}) contacts=[{string.Join(",", r.contacts)}]");
            r.contacts.Clear();
        }
        foreach (var f in Flower.All)
        {
            int n = 0; foreach (var e in GameElement.All) if (e.IsFree && f.HorizDist(e.transform.position) < 0.06f) n++;
            var res = f.Evaluate();
            sb.AppendLine($"    FLOWER{f.index + 1} column={n} inVolume={res.count} owner={res.owner} bottomPollen={(f.BottomPollen() != null)}");
        }
    }
}
