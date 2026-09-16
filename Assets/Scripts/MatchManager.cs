using System.Collections.Generic;
using UnityEngine;

public enum Period { Menu, PreMatch, Auto, Transition, Teleop, PostMatch, Results, Practice, Planner }

public class AllianceState
{
    public readonly Alliance alliance;
    public int nectarInArea = 5;     // 10.3.1 B.ii
    public int nectarGrants;         // G426 A: one per TIP
    public int nectarEntered;
    public int foulPointsReceived;   // credited by opponent FOULS
    public AllianceState(Alliance a) { alliance = a; }
}

public class Breakdown
{
    public int leave, autoPark, autoTips;
    public int teleTips, cell, bottomNectar, ownedFlower, garden, telePark;
    public int fouls;
    public int tipCount;
    public int AutoPts => leave + autoPark + autoTips;
    public int TelePts => teleTips + cell + bottomNectar + ownedFlower + garden + telePark;
    public int Total => AutoPts + TelePts + fouls;
    public bool swarm, poll1, poll2;
    public int rp;
}

public class MatchManager : MonoBehaviour
{
    public static MatchManager I;

    public Period period = Period.Menu;
    public float t;                       // seconds into the current period
    public readonly List<Robot> robots = new List<Robot>();
    public readonly AllianceState Red = new AllianceState(Alliance.Red);
    public readonly AllianceState Blue = new AllianceState(Alliance.Blue);
    public Breakdown redScore = new Breakdown(), blueScore = new Breakdown();
    public readonly List<string> log = new List<string>();
    public string banner = "";
    float bannerUntil;
    readonly List<HumanController> humans = new List<HumanController>();
    readonly Dictionary<Robot, HumanController> humanOf = new Dictionary<Robot, HumanController>();
    float nextScore, nextHp;
    int lastCue = -1;
    bool flowerCue, whistleCue;
    bool finalized;
    public bool autoOnly;          // planner simulation: stop after AUTO
    public bool autoOnlyDone;

    public AllianceState State(Alliance a) => a == Alliance.Red ? Red : Blue;
    public Breakdown ScoreOf(Alliance a) => a == Alliance.Red ? redScore : blueScore;

    public bool FieldLive => period == Period.Auto || period == Period.Transition || period == Period.Teleop || period == Period.PostMatch || period == Period.Practice;

    // Seconds left in the MATCH (AUTO counts down from 2:30 to 2:00 on the FIELD timer).
    public float MatchTimeLeft
    {
        get
        {
            switch (period)
            {
                case Period.PreMatch: return Dims.AutoTime + Dims.TeleopTime;
                case Period.Auto: return Dims.AutoTime + Dims.TeleopTime - t;
                case Period.Transition: return Dims.TeleopTime;
                case Period.Teleop: return Dims.TeleopTime - t;
                case Period.Practice: return 999f;
                default: return 0f;
            }
        }
    }

    public bool LastMinute => period == Period.Practice || period == Period.PostMatch || (period == Period.Teleop && Dims.TeleopTime - t <= Dims.FlowerUnlock);

    void Awake() { I = this; }

    // Table 9-1 "MATCH stopped": foghorn when a running match is abandoned.
    public void MatchStopped()
    {
        if (period == Period.Auto || period == Period.Transition || period == Period.Teleop || period == Period.PreMatch)
            SoundFx.Play("stopped");
    }

    // ------------------------------------------------------------------ setup
    public void SpawnRobots(Transform world)
    {
        robots.Clear();
        humans.Clear();
        humanOf.Clear();
        RobotAI.ClearClaims();
        for (int i = 0; i < 4; i++)
        {
            var sc = GameConfig.slots[i];
            if (sc.control == ControlType.Empty) continue;
            var spec = RobotLibrary.All[Mathf.Clamp(sc.robot, 0, RobotLibrary.All.Count - 1)];
            GameConfig.StartPose(i, spec, out var pos, out var yaw);
            var r = Robot.Create(world, spec, GameConfig.SlotAlliance(i), GameConfig.SlotStation(i), pos, yaw, GameConfig.SlotName(i).ToUpper());
            r.autoController = new RobotAI(true, i);
            if (!string.IsNullOrEmpty(sc.autoPlan))
            {
                var plan = AutoPlan.Load(sc.autoPlan);
                if (plan != null) r.autoController = new PlanController(plan.ForAlliance(r.alliance));
            }
            if (sc.control == ControlType.AI)
            {
                r.controller = new RobotAI(false, i);
            }
            else
            {
                int slot = sc.control == ControlType.Keyboard ? 0 : (int)sc.control - (int)ControlType.Gamepad1 + 1;
                var h = new HumanController(slot);
                r.controller = h;
                r.isHuman = true;
                r.humanSlot = slot;
                humans.Add(h);
                humanOf[r] = h;
                r.EnableTrajectory(GameConfig.trajectory);
                if ((GameConfig.driverInAuto && string.IsNullOrEmpty(sc.autoPlan)) || GameConfig.practice) r.autoController = h;
            }
            r.Preload(world);
            robots.Add(r);
        }

        // ALLIANCE AREA NECTAR: 5 per ALLIANCE, created inactive (off the FIELD).
        foreach (var a in new[] { Alliance.Red, Alliance.Blue })
            for (int k = 0; k < 5; k++)
            {
                var e = GameElement.Create(world, ElementType.Nectar, a, new Vector3(a.Sign() * 2.6f, 0.8f, -0.2f + k * 0.1f));
                e.removed = true;
                e.gameObject.SetActive(false);
            }
    }

    // Planner simulation: a single AUTO period with the given robots.
    public void BeginAutoOnly()
    {
        finalized = false;
        autoOnly = true;
        autoOnlyDone = false;
        log.Clear();
        Enter(Period.Auto);
        SoundFx.Play("start");
    }

    public void Begin(bool practice)
    {
        autoOnly = false;
        finalized = false;
        log.Clear();
        if (practice)
        {
            period = Period.Practice;
            t = 0;
            foreach (var r in robots) r.mode = RobotMode.Teleop;
            Banner("PRACTICE", 2f);
        }
        else
        {
            period = Period.PreMatch;
            t = 0;
            foreach (var r in robots) r.mode = RobotMode.Disabled;
        }
    }

    public Robot FocusRobot()
    {
        foreach (var r in robots) if (r.isHuman) return r;
        return null;
    }

    public void Banner(string s, float secs = 3f)
    {
        banner = s;
        bannerUntil = Time.time + secs;
    }

    public string CurrentBanner => Time.time < bannerUntil ? banner : "";

    void Log(string s)
    {
        string stamp = period == Period.Practice ? "" : Util.Clock(MatchTimeLeft) + "  ";
        log.Add(stamp + s);
        if (log.Count > 40) log.RemoveAt(0);
    }

    // ------------------------------------------------------------------ flow
    void Update()
    {
        if (period == Period.Menu || period == Period.Results || period == Period.Planner) return;
        t += Time.deltaTime;

        switch (period)
        {
            case Period.PreMatch:
                if (t >= 3f) { Enter(Period.Auto); SoundFx.Play("start"); Banner("AUTO", 2f); }
                break;
            case Period.Auto:
                if (t >= Dims.AutoTime)
                {
                    EndAuto();
                    SoundFx.Play("endauto");
                    if (autoOnly)
                    {
                        Enter(Period.Planner);
                        ComputeScores(false);
                        autoOnlyDone = true;
                        return;
                    }
                    Enter(Period.Transition);
                }
                break;
            case Period.Transition:
                {
                    // Table 9-1: "Drivers, pick up your controllers, 3-2-1" between 0:08 and 0:01
                    float countdownAt = Dims.TransitionTime - SoundFx.Length("countdown");
                    float pickupAt = Mathf.Max(2.1f, countdownAt - SoundFx.Length("pickup"));
                    if (lastCue < 1 && t >= pickupAt) { lastCue = 1; SoundFx.Play("pickup"); Banner("Drivers, pick up your controllers", 2.5f); }
                    if (lastCue < 2 && t >= countdownAt) { lastCue = 2; SoundFx.Play("countdown"); }
                    if (t >= Dims.TransitionTime) { Enter(Period.Teleop); SoundFx.Play("teleop"); Banner("TELEOP", 2f); }
                }
                break;
            case Period.Teleop:
                {
                    float left = Dims.TeleopTime - t;
                    // each cue fires exactly once
                    if (left <= Dims.FlowerUnlock && !flowerCue) { flowerCue = true; Banner("FLOWER OWNERSHIP UNLOCKED — all NECTAR may be entered", 3f); }
                    if (left <= 20f && !whistleCue) { whistleCue = true; SoundFx.Play("whistle"); Banner("FINAL 20 SECONDS", 2f); }
                    if (left <= 0f) { Enter(Period.PostMatch); SoundFx.Play("endmatch"); }
                }
                break;
            case Period.PostMatch:
                if (!finalized && (t > 5f || (t > 1.5f && EverythingAtRest())))
                {
                    FinalizeMatch();
                    Enter(Period.Results);
                }
                break;
        }

        HandleHumans();
        if (FieldLive) CheckFouls();
        if (Time.time >= nextScore && period != Period.Results)
        {
            nextScore = Time.time + 0.2f;
            ComputeScores(false);
        }
    }

    void Enter(Period p)
    {
        period = p;
        t = 0;
        lastCue = -1;
        flowerCue = whistleCue = false;
        foreach (var r in robots)
            r.mode = p == Period.Auto ? RobotMode.Auto : p == Period.Teleop || p == Period.Practice ? RobotMode.Teleop : RobotMode.Disabled;
    }

    void EndAuto()
    {
        // 10.5 F: LEAVE and AUTO PARK are assessed at the end of AUTO.
        foreach (var r in robots)
        {
            r.leave = !r.TouchingWall() && Util.Flat(r.transform.position - r.startPos).magnitude > 0.05f;
            r.autoPark = r.Overlaps(Field.LoadingOf(r.alliance));
        }
    }

    bool EverythingAtRest()
    {
        foreach (var e in GameElement.All) if (e.IsFree && e.rb.velocity.sqrMagnitude > 0.01f) return false;
        foreach (var r in robots) if (r.rb.velocity.sqrMagnitude > 0.01f) return false;
        return true;
    }

    void FinalizeMatch()
    {
        finalized = true;
        // 10.5 G: TELEOP PARK assessed at the end of the MATCH.
        foreach (var r in robots) r.teleopPark = r.Overlaps(Field.LoadingOf(r.alliance));
        ComputeScores(true);
    }

    // ------------------------------------------------------------------ human player
    void HandleHumans()
    {
        foreach (var kv in humanOf)
        {
            if (kv.Value.HumanPlayerPressed()) RequestNectar(kv.Key.alliance, true);
        }
        if (period == Period.Practice && Input.GetKeyDown(KeyCode.F5)) Game.I.Restart();

        if (Time.time < nextHp || !FieldLive || period == Period.PostMatch) return;
        nextHp = Time.time + 0.6f;
        foreach (var a in new[] { Alliance.Red, Alliance.Blue })
        {
            bool hasHuman = false;
            foreach (var r in robots) if (r.alliance == a && r.isHuman) hasHuman = true;
            if (hasHuman && !GameConfig.autoHumanPlayer) continue;
            if (!CanEnterNectar(a)) continue;
            // wait until the LOADING ZONE is clear of NECTAR and ROBOTS (G427 C)
            var z = Field.LoadingOf(a);
            bool busy = false;
            foreach (var e in GameElement.All) if (e.IsFree && e.type == ElementType.Nectar && z.ContainsPartially(e.transform.position, 0.05f)) busy = true;
            foreach (var r in robots) if (r.Overlaps(z)) busy = true;
            if (!busy) RequestNectar(a, false);
        }
    }

    public bool CanEnterNectar(Alliance a)
    {
        var s = State(a);
        if (s.nectarInArea <= 0 || !FieldLive || period == Period.PostMatch || period == Period.Transition) return false;
        return s.nectarEntered < s.nectarGrants || LastMinute;   // G426
    }

    public void RequestNectar(Alliance a, bool manual)
    {
        if (!CanEnterNectar(a))
        {
            if (manual) Banner(State(a).nectarInArea <= 0 ? $"{a}: no NECTAR left in the ALLIANCE AREA" : $"G426: {a} may enter NECTAR after a HIVE TIP or with 60 s left", 2f);
            return;
        }
        GameElement nectar = null;
        foreach (var e in Resources.FindObjectsOfTypeAll<GameElement>())
            if (e.removed && e.type == ElementType.Nectar && e.color == a && e.gameObject.scene.IsValid()) { nectar = e; break; }
        var s = State(a);
        var z = Field.LoadingOf(a);
        Vector3 p = z.Center + new Vector3(-a.Sign() * 0.05f, 0.22f, Random.Range(-0.12f, 0.12f));
        if (nectar == null) nectar = GameElement.Create(Game.World, ElementType.Nectar, a, p);
        nectar.gameObject.SetActive(true);
        nectar.removed = false;
        nectar.transform.SetParent(Game.World, true);
        nectar.Release(p, Vector3.down * 0.5f, null);
        nectar.lastTouch = Alliance.None;
        s.nectarInArea--;
        if (!LastMinute) s.nectarEntered++;
        else s.nectarEntered = Mathf.Max(s.nectarEntered, s.nectarGrants);
        Log($"{a} HUMAN PLAYER entered NECTAR ({s.nectarInArea} left)");
    }

    public void NectarExited(GameElement e)
    {
        var s = State(e.color);
        s.nectarInArea++;
        e.ReturnToAllianceArea();
        Log($"{e.color} NECTAR left the FIELD and was returned to the ALLIANCE AREA");
    }

    // ------------------------------------------------------------------ events
    public void OnHiveTipped(Hive h)
    {
        bool auto = period == Period.Auto || period == Period.Transition || period == Period.PreMatch;
        if (auto) h.tipsAuto++; else h.tipsTeleop++;
        State(h.alliance).nectarGrants++;
        Log($"{h.alliance} HIVE TIPPED (#{h.Tips}){(auto ? " in AUTO" : "")}");
        Banner($"{h.alliance.ToString().ToUpper()} HIVE TIP!  +{Dims.PtsTip}", 1.5f);
    }

    void Foul(Alliance offender, bool major, string rule, string what)
    {
        if (offender == Alliance.None || period == Period.Practice) return;
        State(offender.Other()).foulPointsReceived += major ? Dims.PtsMajor : Dims.PtsMinor;
        Log($"{(major ? "MAJOR" : "MINOR")} FOUL on {offender}: {rule} {what}");
        Banner($"{(major ? "MAJOR" : "MINOR")} FOUL {offender} — {rule}", 2.5f);
    }

    void CheckFouls()
    {
        // G410: NECTAR only goes into FLOWERS with one minute left.
        foreach (var e in GameElement.All)
        {
            if (e.type != ElementType.Nectar || !e.IsFree) continue;
            bool inside = false;
            foreach (var f in Flower.All) if (f.InVolume(e)) { inside = true; break; }
            if (inside && !e.inFlowerVolume && !LastMinute && period != Period.PreMatch)
                Foul(e.lastTouch, true, "G410", "NECTAR entered a FLOWER before 1:00");
            e.inFlowerVolume = inside;
        }
        // G402: no AUTO opponent interference (crossing into the opposing side).
        if (period == Period.Auto)
            foreach (var r in robots)
            {
                if (r.g402Called) continue;
                float x = r.transform.position.x * r.alliance.Sign();
                if (x < -(r.HalfLength + 0.05f)) { r.g402Called = true; Foul(r.alliance, true, "G402", $"{r.label} crossed into the opposing side in AUTO"); }
            }
    }

    // ------------------------------------------------------------------ scoring (Table 10-2)
    void ComputeScores(bool final)
    {
        redScore = Compute(Alliance.Red, final);
        blueScore = Compute(Alliance.Blue, final);
        int rt = redScore.Total, bt = blueScore.Total;
        redScore.rp += rt > bt ? 3 : rt == bt ? 1 : 0;
        blueScore.rp += bt > rt ? 3 : rt == bt ? 1 : 0;
        if (final)
        {
            Log($"FINAL  RED {rt}  -  BLUE {bt}");
        }
    }

    Breakdown Compute(Alliance a, bool final)
    {
        var b = new Breakdown();
        bool afterAuto = period != Period.PreMatch && period != Period.Auto;
        foreach (var r in robots)
        {
            if (r.alliance != a) continue;
            if (afterAuto || period == Period.Practice)
            {
                if (r.leave) b.leave += Dims.PtsLeave;
                if (r.autoPark) b.autoPark += Dims.PtsPark;
            }
            bool parked = final ? r.teleopPark : r.Overlaps(Field.LoadingOf(a));
            if (parked && (period == Period.Teleop || period == Period.PostMatch || period == Period.Results || period == Period.Practice)) b.telePark += Dims.PtsPark;
        }
        var hive = Field.HiveOf(a);
        if (hive)
        {
            b.autoTips = hive.tipsAuto * Dims.PtsTip;
            b.teleTips = hive.tipsTeleop * Dims.PtsTip;
            b.tipCount = hive.Tips;
            b.cell = hive.CountInUpCell() * Dims.PtsCell;
        }
        foreach (var f in Flower.All)
        {
            var res = f.Evaluate();
            if (res.bottomNectar == a) b.bottomNectar += Dims.PtsBottomNectar;
            if (res.owner == a) b.ownedFlower += res.count * Dims.PtsOwnedFlower;
        }
        var g = Field.GardenOf(a);
        foreach (var e in GameElement.All)
            if (e.IsFree && g.ContainsPartially(e.transform.position, e.Radius)) b.garden += Dims.PtsGarden;
        b.fouls = State(a).foulPointsReceived;

        // Table 10-3 RP thresholds (All Other Events)
        b.swarm = b.leave + b.autoPark + b.telePark >= Dims.SwarmThreshold;
        b.poll1 = b.tipCount >= Dims.Pollinator1Tips;
        b.poll2 = b.tipCount >= Dims.Pollinator2Tips;
        b.rp = (b.swarm ? 1 : 0) + (b.poll1 ? 1 : 0) + (b.poll2 ? 1 : 0);
        return b;
    }
}
