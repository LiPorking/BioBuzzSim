using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.DualShock;

public enum Act
{
    Fire, Feed, Intake, Outtake, FlowerMech, ToggleTarget, HumanPlayer, CameraCycle,
    MoveForward, MoveBack, MoveLeft, MoveRight, RotateLeft, RotateRight,
}

public enum Pad { None, A, B, X, Y, LB, RB, Back, Start, L3, R3, LT, RT, DUp, DDown, DLeft, DRight }

// Rebindable controls. Each action has two keyboard keys and one gamepad input.
// Slot 0 = keyboard/mouse, slots 1-4 = gamepads (XInput layout, legacy Input Manager).
public static class Bindings
{
    public static readonly int ActCount = Enum.GetValues(typeof(Act)).Length;
    const int PadCount = 17;

    public static KeyCode[,] keys = new KeyCode[ActCount, 2];
    public static Pad[] pads = new Pad[ActCount];

    public static float deadzone = 0.15f;
    public static bool invertLookY;

    // pad state per joystick (1..4) for edge detection of axis-based inputs
    static readonly bool[,] padNow = new bool[5, PadCount];
    static readonly bool[,] padPrev = new bool[5, PadCount];
    // presses latched in Update, consumed from FixedUpdate
    static readonly int[,] latched = new int[5, ActCount];
    // presses only count while a match is being played (not in menus, pause or restarts)
    public static bool latchEnabled;
    static int lastTickFrame = -1;
    static bool axesOk = true;

    public static readonly string[] ActNames =
    {
        "Shoot", "Feed (pass to your side)", "Intake", "Outtake", "FLOWER mechanism", "Target HIVE / FLOWER", "HUMAN PLAYER: enter NECTAR", "Cycle camera",
        "Move forward", "Move back", "Move left", "Move right", "Rotate left", "Rotate right",
    };

    static Bindings()
    {
        ResetDefaults();
        Load();
    }

    public static void ResetDefaults()
    {
        void B(Act a, KeyCode k1, KeyCode k2, Pad p) { keys[(int)a, 0] = k1; keys[(int)a, 1] = k2; pads[(int)a] = p; }
        B(Act.Fire, KeyCode.Space, KeyCode.None, Pad.RT);
        B(Act.Feed, KeyCode.B, KeyCode.None, Pad.Y);
        B(Act.Intake, KeyCode.LeftShift, KeyCode.None, Pad.LT);
        B(Act.Outtake, KeyCode.R, KeyCode.None, Pad.LB);
        B(Act.FlowerMech, KeyCode.F, KeyCode.None, Pad.RB);
        B(Act.ToggleTarget, KeyCode.Tab, KeyCode.None, Pad.B);
        B(Act.HumanPlayer, KeyCode.H, KeyCode.None, Pad.A);
        B(Act.CameraCycle, KeyCode.C, KeyCode.None, Pad.R3);
        B(Act.MoveForward, KeyCode.W, KeyCode.None, Pad.None);
        B(Act.MoveBack, KeyCode.S, KeyCode.None, Pad.None);
        B(Act.MoveLeft, KeyCode.A, KeyCode.None, Pad.None);
        B(Act.MoveRight, KeyCode.D, KeyCode.None, Pad.None);
        B(Act.RotateLeft, KeyCode.Q, KeyCode.None, Pad.DLeft);
        B(Act.RotateRight, KeyCode.E, KeyCode.None, Pad.DRight);
    }

    [Serializable]
    class Saved { public int[] k0, k1, p; public float dead; public bool inv; }

    public static void Save()
    {
        var s = new Saved { k0 = new int[ActCount], k1 = new int[ActCount], p = new int[ActCount], dead = deadzone, inv = invertLookY };
        for (int i = 0; i < ActCount; i++) { s.k0[i] = (int)keys[i, 0]; s.k1[i] = (int)keys[i, 1]; s.p[i] = (int)pads[i]; }
        try { PlayerPrefs.SetString("bindings_v2", JsonUtility.ToJson(s)); PlayerPrefs.Save(); } catch { }
    }

    public static void Load()
    {
        try
        {
            string json = PlayerPrefs.GetString("bindings_v2", "");
            if (string.IsNullOrEmpty(json)) return;
            var s = JsonUtility.FromJson<Saved>(json);
            if (s?.k0 == null || s.k0.Length != ActCount) return;
            for (int i = 0; i < ActCount; i++) { keys[i, 0] = (KeyCode)s.k0[i]; keys[i, 1] = (KeyCode)s.k1[i]; pads[i] = (Pad)s.p[i]; }
            deadzone = Mathf.Clamp(s.dead, 0.02f, 0.5f);
            invertLookY = s.inv;
        }
        catch { }
    }

    // ------------------------------------------------------------------ raw gamepad
    // Gamepads come from Unity's Input System (XInput/Xbox, DualShock 4 and DualSense over USB or
    // Bluetooth, Switch Pro, Steam virtual controllers...). Slot 1 = first connected gamepad.
    // If the Input System sees no gamepad, the legacy Input Manager XInput mapping is used.
    public static Gamepad GamepadFor(int joy)
    {
        var all = Gamepad.all;
        return joy >= 1 && joy <= all.Count ? all[joy - 1] : null;
    }

    public static float Axis(int joy, string name)
    {
        if (!axesOk || joy < 1) return 0;
        try { return Input.GetAxisRaw($"J{joy}_{name}"); }
        catch { axesOk = false; return 0; }
    }

    public static Vector2 Stick(int joy, bool right)
    {
        Vector2 v;
        var gp = GamepadFor(joy);
        if (gp != null) v = right ? gp.rightStick.ReadValue() : gp.leftStick.ReadValue();
        else if (Gamepad.all.Count == 0) v = right ? new Vector2(Axis(joy, "RX"), -Axis(joy, "RY")) : new Vector2(Axis(joy, "LX"), -Axis(joy, "LY"));
        else v = Vector2.zero;
        float m = v.magnitude;
        if (m < deadzone) return Vector2.zero;
        return v / m * Mathf.Clamp01((m - deadzone) / (1f - deadzone));
    }

    static KeyCode JoyButton(int joy, int b) => (KeyCode)((int)KeyCode.Joystick1Button0 + (joy - 1) * 20 + b);

    static bool PadRaw(int joy, Pad p)
    {
        var gp = GamepadFor(joy);
        if (gp != null)
        {
            switch (p)
            {
                case Pad.A: return gp.buttonSouth.isPressed;      // Cross on PlayStation
                case Pad.B: return gp.buttonEast.isPressed;       // Circle
                case Pad.X: return gp.buttonWest.isPressed;       // Square
                case Pad.Y: return gp.buttonNorth.isPressed;      // Triangle
                case Pad.LB: return gp.leftShoulder.isPressed;
                case Pad.RB: return gp.rightShoulder.isPressed;
                case Pad.Back: return gp.selectButton.isPressed;  // Share / View
                case Pad.Start: return gp.startButton.isPressed;  // Options / Menu
                case Pad.L3: return gp.leftStickButton.isPressed;
                case Pad.R3: return gp.rightStickButton.isPressed;
                case Pad.LT: return gp.leftTrigger.ReadValue() > 0.4f;
                case Pad.RT: return gp.rightTrigger.ReadValue() > 0.4f;
                case Pad.DUp: return gp.dpad.up.isPressed;
                case Pad.DDown: return gp.dpad.down.isPressed;
                case Pad.DLeft: return gp.dpad.left.isPressed;
                case Pad.DRight: return gp.dpad.right.isPressed;
                default: return false;
            }
        }
        if (Gamepad.all.Count > 0) return false;
        switch (p)
        {
            case Pad.A: return Input.GetKey(JoyButton(joy, 0));
            case Pad.B: return Input.GetKey(JoyButton(joy, 1));
            case Pad.X: return Input.GetKey(JoyButton(joy, 2));
            case Pad.Y: return Input.GetKey(JoyButton(joy, 3));
            case Pad.LB: return Input.GetKey(JoyButton(joy, 4));
            case Pad.RB: return Input.GetKey(JoyButton(joy, 5));
            case Pad.Back: return Input.GetKey(JoyButton(joy, 6));
            case Pad.Start: return Input.GetKey(JoyButton(joy, 7));
            case Pad.L3: return Input.GetKey(JoyButton(joy, 8));
            case Pad.R3: return Input.GetKey(JoyButton(joy, 9));
            case Pad.LT: return Axis(joy, "LT") > 0.4f;
            case Pad.RT: return Axis(joy, "RT") > 0.4f;
            case Pad.DUp: return Axis(joy, "DY") > 0.5f;
            case Pad.DDown: return Axis(joy, "DY") < -0.5f;
            case Pad.DLeft: return Axis(joy, "DX") < -0.5f;
            case Pad.DRight: return Axis(joy, "DX") > 0.5f;
            default: return false;
        }
    }

    public static bool IsPlayStation(int joy) => GamepadFor(joy) is DualShockGamepad;

    // Button name as printed on the controller (PlayStation or Xbox layout).
    public static string PadName(Pad p, int joy)
    {
        if (p == Pad.None) return "—";
        bool ps = joy >= 1 ? IsPlayStation(joy) : AnyPlayStation();
        if (!ps) return p.ToString();
        switch (p)
        {
            case Pad.A: return "Cross";
            case Pad.B: return "Circle";
            case Pad.X: return "Square";
            case Pad.Y: return "Triangle";
            case Pad.LB: return "L1";
            case Pad.RB: return "R1";
            case Pad.LT: return "L2";
            case Pad.RT: return "R2";
            case Pad.Back: return "Share";
            case Pad.Start: return "Options";
            default: return p.ToString();
        }
    }

    static bool AnyPlayStation()
    {
        foreach (var g in Gamepad.all) if (g is DualShockGamepad) return true;
        return false;
    }

    public static bool PadHeld(int joy, Pad p) => joy >= 1 && joy <= 4 && padNow[joy, (int)p];
    public static bool PadDown(int joy, Pad p) => joy >= 1 && joy <= 4 && padNow[joy, (int)p] && !padPrev[joy, (int)p];

    // ------------------------------------------------------------------ per-frame update
    public static void Tick()
    {
        if (Time.frameCount == lastTickFrame) return;
        lastTickFrame = Time.frameCount;
        for (int j = 1; j <= 4; j++)
            for (int p = 1; p < PadCount; p++)
            {
                padPrev[j, p] = padNow[j, p];
                padNow[j, p] = PadRaw(j, (Pad)p);
            }
        for (int slot = 0; slot <= 4; slot++)
            for (int a = 0; a < ActCount; a++)
                if (!latchEnabled) latched[slot, a] = 0;
                else if (Down((Act)a, slot)) latched[slot, a]++;
    }

    public static bool Held(Act a, int slot)
    {
        if (slot == 0)
        {
            var k0 = keys[(int)a, 0]; var k1 = keys[(int)a, 1];
            return (k0 != KeyCode.None && Input.GetKey(k0)) || (k1 != KeyCode.None && Input.GetKey(k1));
        }
        return PadHeld(slot, pads[(int)a]);
    }

    public static bool Down(Act a, int slot)
    {
        if (slot == 0)
        {
            var k0 = keys[(int)a, 0]; var k1 = keys[(int)a, 1];
            return (k0 != KeyCode.None && Input.GetKeyDown(k0)) || (k1 != KeyCode.None && Input.GetKeyDown(k1));
        }
        return PadDown(slot, pads[(int)a]);
    }

    // For FixedUpdate code: true once per press, never missed or doubled.
    public static bool Consume(Act a, int slot)
    {
        if (slot < 0 || slot > 4 || latched[slot, (int)a] <= 0) return false;
        latched[slot, (int)a] = 0;
        return true;
    }

    // Any joystick (for menus)
    public static bool AnyPadDown(Pad p)
    {
        for (int j = 1; j <= 4; j++) if (PadDown(j, p)) return true;
        return false;
    }

    public static Vector2 AnyStick()
    {
        Vector2 best = Vector2.zero;
        for (int j = 1; j <= 4; j++) { var s = Stick(j, false); if (s.sqrMagnitude > best.sqrMagnitude) best = s; }
        return best;
    }

    // ------------------------------------------------------------------ rebinding helpers
    public static bool CaptureKey(out KeyCode key)
    {
        key = KeyCode.None;
        if (!Input.anyKeyDown) return false;
        foreach (KeyCode k in Enum.GetValues(typeof(KeyCode)))
        {
            if (k >= KeyCode.JoystickButton0) continue; // keyboard + mouse only
            if (Input.GetKeyDown(k)) { key = k; return true; }
        }
        return false;
    }

    public static bool CapturePad(out Pad pad)
    {
        pad = Pad.None;
        for (int j = 1; j <= 4; j++)
            for (int p = 1; p < PadCount; p++)
                if ((Pad)p != Pad.Back && (Pad)p != Pad.Start && PadDown(j, (Pad)p)) { pad = (Pad)p; return true; }
        return false;
    }

    public static string KeyName(KeyCode k)
    {
        switch (k)
        {
            case KeyCode.None: return "—";
            case KeyCode.Mouse0: return "Left Mouse";
            case KeyCode.Mouse1: return "Right Mouse";
            case KeyCode.Mouse2: return "Middle Mouse";
            case KeyCode.UpArrow: return "↑";
            case KeyCode.DownArrow: return "↓";
            case KeyCode.LeftArrow: return "←";
            case KeyCode.RightArrow: return "→";
            case KeyCode.Equals: return "=";
            case KeyCode.Minus: return "-";
            default: return k.ToString();
        }
    }

    public static string PadName(Pad p) => PadName(p, 0);

    public static string Hint(Act a, int slot) => slot == 0 ? KeyName(keys[(int)a, 0]) : PadName(pads[(int)a], slot);

    // "<system name> 1", "<system name> 2" ... numbered per identical name.
    public static List<string> ControllerNames()
    {
        var raw = new List<string>();
        var all = Gamepad.all;
        if (all.Count > 0) foreach (var g in all) raw.Add(string.IsNullOrEmpty(g.displayName) ? g.layout : g.displayName);
        else
        {
            try { foreach (var n in Input.GetJoystickNames()) if (!string.IsNullOrEmpty(n)) raw.Add(n.Trim()); } catch { }
        }
        var result = new List<string>();
        var counts = new Dictionary<string, int>();
        foreach (var n in raw)
        {
            counts.TryGetValue(n, out int c);
            counts[n] = ++c;
            result.Add($"{n} {c}");
        }
        return result;
    }

    public static string[] ConnectedControllers()
    {
        var all = Gamepad.all;
        if (all.Count > 0)
        {
            var names = new string[all.Count];
            for (int i = 0; i < all.Count; i++) names[i] = $"{all[i].displayName} ({(all[i] is DualShockGamepad ? "PlayStation" : all[i].layout)})";
            return names;
        }
        try { return Input.GetJoystickNames(); } catch { return new string[0]; }
    }
}
