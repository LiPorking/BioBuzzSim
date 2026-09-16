using System.Collections.Generic;
using UnityEngine;
using static UIKit;

// Main menu, controls (rebinding), match HUD, pause and results. Works with mouse or gamepad.
public class GameUI : MonoBehaviour
{
    public enum Page { Main, Controls, Sources }
    public static Page testPage = (Page)(-1);
    public static bool testRobotPopup;
    Page page = Page.Main;
    bool paused;
    Vector2 scroll;

    // rebinding state: column 0/1 = keyboard keys, 2 = gamepad
    int rebindAct = -1, rebindCol;
    float rebindStarted;

    void Update()
    {
        var mmNow = MatchManager.I;
        Bindings.latchEnabled = mmNow != null && mmNow.FieldLive && !paused && PlannerUI.Active == null && !Game.I.Rebuilding;
        Bindings.Tick();
        UIKit.suppressNav = rebindAct >= 0;
        UIKit.NavUpdate();
        var mm = MatchManager.I;
        if (mm == null) return;

        bool wasRebinding = rebindAct >= 0;
        if (wasRebinding) CaptureBinding();
        if ((int)testPage >= 0) { page = testPage; testPage = (Page)(-1); }
        if (testRobotPopup)
        {
            testRobotPopup = false;
            var names = new List<string>();
            foreach (var s in RobotLibrary.All) names.Add(s.name);
            OpenPopup("RED 1 - robot", names, GameConfig.slots[0].robot, k => GameConfig.slots[0].robot = k);
        }

        // pause is always Esc (keyboard) / Start (gamepad)
        bool pausePressed = !wasRebinding && (Input.GetKeyDown(KeyCode.Escape) || (mm.period == Period.Menu && Bindings.AnyPadDown(Pad.Start)));
        if (mm.period == Period.Menu)
        {
            if (popupItems != null && (UIKit.ConsumeBack() || pausePressed)) { ClosePopup(); return; }
            if (!wasRebinding && (UIKit.ConsumeBack() || pausePressed) && page != Page.Main) { Bindings.Save(); page = Page.Main; UIKit.ResetFocus(); }
            return;
        }
        if (PlannerUI.Active != null || Game.I.Rebuilding) return;
        // fixed controller buttons (not rebindable): Options = restart, Share = main menu
        if (Bindings.AnyPadDown(Pad.Start)) { SoundFx.StopCues(); paused = false; Time.timeScale = 1; Game.I.Restart(); return; }
        if (Bindings.AnyPadDown(Pad.Back)) { SoundFx.StopCues(); paused = false; Time.timeScale = 1; Game.I.BackToMenu(); return; }
        if (pausePressed && mm.period != Period.Results)
        {
            paused = !paused;
            Time.timeScale = paused ? 0 : 1;
            UIKit.ResetFocus();
        }
    }

    // Esc while capturing = unbound.
    void CaptureBinding()
    {
        if (Time.unscaledTime - rebindStarted < 0.15f) return;
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (rebindCol == 0) Bindings.keys[rebindAct, 0] = KeyCode.None;
            else Bindings.pads[rebindAct] = Pad.None;
            rebindAct = -1;
            Bindings.Save();
            return;
        }
        if (rebindCol == 0)
        {
            if (Bindings.CaptureKey(out var k))
            {
                Bindings.keys[rebindAct, 0] = k;
                Bindings.keys[rebindAct, 1] = KeyCode.None;
                rebindAct = -1;
                Bindings.Save();
            }
        }
        else if (Bindings.CapturePad(out var p))
        {
            Bindings.pads[rebindAct] = p;
            rebindAct = -1;
            Bindings.Save();
        }
        else if (Time.unscaledTime - rebindStarted > 6f) rebindAct = -1;
    }

    void OnGUI()
    {
        UIKit.Begin();
        var mm = MatchManager.I;
        if (mm == null || PlannerUI.Active != null || Game.I.Rebuilding) return;

        if (mm.period == Period.Menu)
        {
            Fill(new Rect(40, 30, 1840, 1020), new Color(0.04f, 0.05f, 0.07f, 0.85f));
            if (page == Page.Controls) DrawControls();
            else
            {
                GUI.enabled = popupItems == null;
                DrawMenu();
                GUI.enabled = true;
                if (popupItems != null) DrawPopup();
            }
            return;
        }
        DrawHud(mm);
        if (mm.period == Period.Results) DrawResults(mm);
        if (paused) DrawPause();
    }

    // ------------------------------------------------------------------ main menu
    void DrawMenu()
    {
        GUI.Label(new Rect(80, 45, 1200, 90), "BIOBUZZ SIMULATOR", title);
        if (Button(new Rect(1810, 42, 56, 56), "X")) Application.Quit();

        GUI.Label(new Rect(80, 160, 600, 40), "ROBOTS", h1);
        var plans = AutoPlan.SavedNames();
        var pads = Bindings.ControllerNames();
        for (int i = 0; i < 4; i++) DrawSlot(i, new Rect(80, 210 + i * 200, 1080, 180), plans, pads);

        float x = 1200, y = 160;
        GUI.Label(new Rect(x, y, 600, 40), "SETTINGS", h1); y += 50;
        GameConfig.trajectory = Toggle(new Rect(x, y, 640, 34), GameConfig.trajectory, "Show launch trajectory"); y += 42;
        GameConfig.driverInAuto = Toggle(new Rect(x, y, 640, 34), GameConfig.driverInAuto, "Drivable autonomous"); y += 50;
        SoundFx.Volume = Stepper(new Rect(x, y, 640, 36), "Volume", SoundFx.Volume, 0.1f, 0f, 1f, "0.0");

        if (Button(new Rect(x, 700, 305, 62), "FREE PRACTICE")) Game.I.StartGame(true);
        if (Button(new Rect(x + 320, 700, 305, 62), "AUTO PLANNER")) Game.I.StartPlanner();
        if (Button(new Rect(x, 774, 625, 62), "CONTROLS")) { page = Page.Controls; ResetFocus(); }
        if (Button(new Rect(x, 860, 625, 150), "START MATCH", startStyle)) Game.I.StartGame(false);
    }

    GUIStyle startStyleCache;
    GUIStyle startStyle
    {
        get
        {
            if (startStyleCache == null)
            {
                startStyleCache = new GUIStyle(UIKit.btn) { fontSize = 44 };
                startStyleCache.normal.background = UIKit.Tex(new Color(0.85f, 0.62f, 0.05f));
                startStyleCache.hover.background = UIKit.Tex(new Color(1f, 0.75f, 0.12f));
                startStyleCache.normal.textColor = startStyleCache.hover.textColor = Color.black;
            }
            return startStyleCache;
        }
    }

    void DrawSlot(int i, Rect r, List<string> plans, List<string> pads)
    {
        var sc = GameConfig.slots[i];
        var a = GameConfig.SlotAlliance(i);
        Fill(new Rect(r.x, r.y, 12, r.height), a.Col());
        Fill(new Rect(r.x + 12, r.y, r.width - 12, r.height), new Color(1, 1, 1, 0.04f));
        GUI.Label(new Rect(r.x + 28, r.y + 10, 200, 40), GameConfig.SlotName(i).ToUpper(), h1);

        // controllers: Empty, AI, Keyboard, then every recognised controller by its system name
        var options = new List<ControlType> { ControlType.Empty, ControlType.AI, ControlType.Keyboard };
        for (int k = 0; k < pads.Count && k < 4; k++) options.Add(ControlType.Gamepad1 + k);
        int padIndex = sc.control >= ControlType.Gamepad1 ? (int)sc.control - (int)ControlType.Gamepad1 : -1;
        string label = sc.control == ControlType.Empty ? "Empty" : sc.control == ControlType.AI ? "AI" : sc.control == ControlType.Keyboard ? "Keyboard"
            : padIndex < pads.Count ? pads[padIndex] : "(disconnected)";
        if (Button(new Rect(r.x + 230, r.y + 12, 400, 60), label + "  ▼"))
        {
            var names = new List<string>();
            foreach (var o in options)
                names.Add(o == ControlType.Empty ? "Empty" : o == ControlType.AI ? "AI" : o == ControlType.Keyboard ? "Keyboard" : pads[(int)o - (int)ControlType.Gamepad1]);
            OpenPopup($"{GameConfig.SlotName(i)} - controller", names, options.IndexOf(sc.control), k => sc.control = options[k]);
        }
        if (Button(new Rect(r.x + 650, r.y + 12, 410, 60), RobotLibrary.All[Mathf.Clamp(sc.robot, 0, RobotLibrary.All.Count - 1)].name + "  ▼"))
        {
            var names = new List<string>();
            foreach (var s in RobotLibrary.All) names.Add(s.name);
            OpenPopup($"{GameConfig.SlotName(i)} - robot", names, sc.robot, k => sc.robot = k);
        }

        // AUTO: built-in routine or a saved planner file (the start position comes from the plan)
        bool hasPlan = !string.IsNullOrEmpty(sc.autoPlan);
        int pidx = hasPlan ? plans.IndexOf(sc.autoPlan) : -1;
        if (hasPlan && pidx < 0) { sc.autoPlan = ""; hasPlan = false; }
        if (Button(new Rect(r.x + 230, r.y + 90, 830, 60), (hasPlan ? "AUTO: " + sc.autoPlan : "AUTO: built-in") + "  ▼"))
        {
            var names = new List<string> { "Built-in" };
            names.AddRange(plans);
            OpenPopup($"{GameConfig.SlotName(i)} - AUTO", names, pidx + 1, k => sc.autoPlan = k == 0 ? "" : plans[k - 1]);
        }
    }

    // ------------------------------------------------------------------ pick-list popup
    List<string> popupItems;
    string popupTitle;
    int popupSelected;
    System.Action<int> popupPick;
    Vector2 popupScroll;

    void OpenPopup(string title, List<string> items, int selected, System.Action<int> pick)
    {
        popupTitle = title;
        popupItems = items;
        popupSelected = selected;
        popupPick = pick;
        popupScroll = Vector2.zero;
        UIKit.ResetFocus();
    }

    void ClosePopup()
    {
        popupItems = null;
        popupPick = null;
        UIKit.ResetFocus();
    }

    void DrawPopup()
    {
        Fill(new Rect(0, 0, W, H), new Color(0, 0, 0, 0.5f));
        float rowH = 60f, w = 620f;
        float listH = Mathf.Min(popupItems.Count * (rowH + 8), 600f);
        var r = new Rect((W - w) * 0.5f, (H - listH - 170) * 0.5f, w, listH + 170);
        Fill(r, new Color(0.03f, 0.03f, 0.05f, 0.98f));
        GUI.Label(new Rect(r.x + 16, r.y + 10, w - 32, 44), popupTitle, h2);
        popupScroll = GUI.BeginScrollView(new Rect(r.x + 10, r.y + 64, w - 20, listH), popupScroll, new Rect(0, 0, w - 44, popupItems.Count * (rowH + 8)));
        for (int k = 0; k < popupItems.Count; k++)
        {
            if (Button(new Rect(0, k * (rowH + 8), w - 44, rowH), popupItems[k], null, k == popupSelected))
            {
                var pick = popupPick;
                ClosePopup();
                pick?.Invoke(k);
                break;
            }
        }
        GUI.EndScrollView();
        if (popupItems != null && Button(new Rect(r.x + 10, r.yMax - 80, w - 20, 64), "CLOSE")) ClosePopup();
    }

    // ------------------------------------------------------------------ controls / rebinding
    void DrawControls()
    {
        GUI.Label(new Rect(80, 45, 1200, 90), "CONTROLS", title);

        float y = 160;
        GUI.Label(new Rect(90, y, 520, 30), "<b>ACTION</b>", body);
        GUI.Label(new Rect(700, y, 400, 30), "<b>KEYBOARD</b>", body);
        GUI.Label(new Rect(1160, y, 400, 30), "<b>CONTROLLER</b>", body);
        y += 42;
        for (int a = 0; a < Bindings.ActCount; a++)
        {
            if (a % 2 == 0) Fill(new Rect(80, y - 3, 1520, 50), new Color(1, 1, 1, 0.03f));
            GUI.Label(new Rect(90, y + 8, 580, 34), Bindings.ActNames[a], body);
            for (int col = 0; col < 2; col++)
            {
                bool waiting = rebindAct == a && rebindCol == col;
                string text = waiting ? "press..." : col == 0 ? Bindings.KeyName(Bindings.keys[a, 0]) : Bindings.PadName(Bindings.pads[a]);
                float bx = col == 0 ? 700 : 1160;
                if (Button(new Rect(bx, y, 420, 44), text, null, waiting) && rebindAct < 0)
                {
                    rebindAct = a;
                    rebindCol = col;
                    rebindStarted = Time.unscaledTime;
                }
            }
            y += 52;
        }

        if (Button(new Rect(1630, 160, 240, 56), "RESET")) { Bindings.ResetDefaults(); Bindings.Save(); }
        if (Button(new Rect(1630, 960, 240, 56), "BACK")) { Bindings.Save(); page = Page.Main; ResetFocus(); }
    }

    // ------------------------------------------------------------------ HUD
    void DrawHud(MatchManager mm)
    {
        var rs = mm.redScore; var bs = mm.blueScore;
        Fill(new Rect(560, 10, 800, 110), new Color(0, 0, 0, 0.72f));
        Fill(new Rect(560, 10, 290, 110), new Color(0.75f, 0.08f, 0.08f, 0.9f));
        Fill(new Rect(1070, 10, 290, 110), new Color(0.08f, 0.22f, 0.8f, 0.9f));
        GUI.Label(new Rect(560, 10, 290, 110), rs.Total.ToString(), big);
        GUI.Label(new Rect(1070, 10, 290, 110), bs.Total.ToString(), big);
        string clk = mm.period == Period.Practice ? "∞" : mm.period == Period.PreMatch ? Util.Clock(150) : Util.Clock(mm.MatchTimeLeft);
        GUI.Label(new Rect(850, 8, 220, 70), clk, clock);
        string per = mm.period == Period.PreMatch ? $"STARTS IN {Mathf.CeilToInt(3 - mm.t)}" : mm.period.ToString().ToUpper();
        GUI.Label(new Rect(850, 72, 220, 40), per, center);

        GUI.Label(new Rect(560, 122, 290, 30), $"TIPS {rs.tipCount}  ·  NECTAR {mm.Red.nectarInArea}{(mm.CanEnterNectar(Alliance.Red) ? " ▼" : "")}", smallCenter);
        GUI.Label(new Rect(1070, 122, 290, 30), $"TIPS {bs.tipCount}  ·  NECTAR {mm.Blue.nectarInArea}{(mm.CanEnterNectar(Alliance.Blue) ? " ▼" : "")}", smallCenter);

        float fx = 870;
        foreach (var f in Flower.All)
        {
            var res = f.Evaluate();
            Color c = res.owner == Alliance.None ? new Color(0.3f, 0.3f, 0.3f) : res.owner.Col();
            Fill(new Rect(fx, 118, 40, 30), c);
            GUI.Label(new Rect(fx, 118, 40, 30), res.count.ToString(), smallCenter);
            fx += 46;
        }

        string b = mm.CurrentBanner;
        if (!string.IsNullOrEmpty(b))
        {
            Fill(new Rect(460, 170, 1000, 60), new Color(0, 0, 0, 0.7f));
            GUI.Label(new Rect(460, 170, 1000, 60), b, center);
        }
    }

    static string ShortName(string n)
    {
        int i = n.IndexOf(" (");
        return i > 0 ? n.Substring(0, i) : n;
    }

    static string HeldString(Robot r)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var e in r.held) sb.Append(e.type == ElementType.Pollen ? "<color=#ffd81a>●</color>" : e.color == Alliance.Red ? "<color=#ff4040>●</color>" : "<color=#4a78ff>●</color>");
        return sb.ToString();
    }

    void DrawResults(MatchManager mm)
    {
        var r = new Rect(360, 240, 1200, 640);
        Fill(r, new Color(0.02f, 0.02f, 0.04f, 0.94f));
        GUI.Label(new Rect(r.x, r.y + 16, r.width, 60), "MATCH RESULTS", big);
        var rs = mm.redScore; var bs = mm.blueScore;
        string winner = rs.Total > bs.Total ? "RED WINS" : bs.Total > rs.Total ? "BLUE WINS" : "TIE";
        GUI.Label(new Rect(r.x, r.y + 88, r.width, 40), winner, center);
        string[] rows = { "LEAVE", "AUTO PARK", "AUTO HIVE TIPS", "TELEOP HIVE TIPS", "POLLEN/NECTAR in CELL", "Bottom NECTAR Bonus", "Owned FLOWER elements", "GARDEN", "TELEOP PARK", "FOULS (credited)", "TOTAL", "RANKING POINTS" };
        int[] rv = { rs.leave, rs.autoPark, rs.autoTips, rs.teleTips, rs.cell, rs.bottomNectar, rs.ownedFlower, rs.garden, rs.telePark, rs.fouls, rs.Total, rs.rp };
        int[] bv = { bs.leave, bs.autoPark, bs.autoTips, bs.teleTips, bs.cell, bs.bottomNectar, bs.ownedFlower, bs.garden, bs.telePark, bs.fouls, bs.Total, bs.rp };
        for (int i = 0; i < rows.Length; i++)
        {
            float y = r.y + 140 + i * 34;
            if (i >= 10) Fill(new Rect(r.x + 150, y, 900, 32), new Color(1, 1, 1, 0.06f));
            GUI.Label(new Rect(r.x + 160, y, 300, 32), rv[i].ToString(), smallCenter);
            GUI.Label(new Rect(r.x + 450, y, 300, 32), rows[i], smallCenter);
            GUI.Label(new Rect(r.x + 740, y, 300, 32), bv[i].ToString(), smallCenter);
        }
        GUI.Label(new Rect(r.x + 160, r.y + 552, 300, 30), $"{(rs.swarm ? "SWARM " : "")}{(rs.poll1 ? "POLLINATOR 1 " : "")}{(rs.poll2 ? "POLLINATOR 2" : "")}", smallCenter);
        GUI.Label(new Rect(r.x + 740, r.y + 552, 300, 30), $"{(bs.swarm ? "SWARM " : "")}{(bs.poll1 ? "POLLINATOR 1 " : "")}{(bs.poll2 ? "POLLINATOR 2" : "")}", smallCenter);
        if (Button(new Rect(r.x + 300, r.y + 580, 280, 50), "PLAY AGAIN")) Game.I.Restart();
        if (Button(new Rect(r.x + 620, r.y + 580, 280, 50), "MAIN MENU")) Game.I.BackToMenu();
    }

    void DrawPause()
    {
        Fill(new Rect(0, 0, W, H), new Color(0, 0, 0, 0.55f));
        GUI.Label(new Rect(0, 330, W, 90), "PAUSED", big);
        if (Button(new Rect(810, 450, 300, 60), "RESUME")) { paused = false; Time.timeScale = 1; }
        if (Button(new Rect(810, 525, 300, 60), "RESTART")) { SoundFx.StopCues(); paused = false; Time.timeScale = 1; Game.I.Restart(); }
        if (Button(new Rect(810, 600, 300, 60), "MAIN MENU")) { SoundFx.StopCues(); paused = false; Time.timeScale = 1; Game.I.BackToMenu(); }
    }
}
