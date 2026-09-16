using UnityEngine;

// Shared IMGUI styles plus gamepad navigation: D-pad / left stick move the focus between
// buttons (in drawing order), A activates, B backs out. The mouse still works as usual.
public static class UIKit
{
    public const float W = 1920f, H = 1080f;
    public static GUIStyle title, h1, h2, body, small, big, center, clock, btn, btnSmall, smallCenter, field, tiny, banner;
    public static float scale = 1f;
    public static Vector2 offset;

    static int navCount, lastNavCount, navIndex;
    static bool activate, back;
    public static bool padMode;
    static Vector3 lastMouse;
    static float repeatAt;
    public static bool suppressNav;   // e.g. while capturing a gamepad binding

    public static Texture2D Tex(Color c)
    {
        var t = new Texture2D(1, 1);
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }

    static void EnsureStyles()
    {
        if (title != null) return;
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        GUIStyle S(int size, FontStyle fs, TextAnchor a, Color c)
        {
            var s = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = fs, alignment = a, wordWrap = true, richText = true };
            if (font) s.font = font;
            s.normal.textColor = c;
            return s;
        }
        title = S(64, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.2f));
        h1 = S(34, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
        h2 = S(24, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.9f, 0.9f, 0.9f));
        body = S(21, FontStyle.Normal, TextAnchor.UpperLeft, new Color(0.88f, 0.88f, 0.9f));
        small = S(17, FontStyle.Normal, TextAnchor.UpperLeft, new Color(0.75f, 0.75f, 0.78f));
        tiny = S(14, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.85f, 0.85f, 0.88f));
        big = S(72, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
        center = S(26, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
        clock = S(58, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
        smallCenter = S(17, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
        btn = new GUIStyle(GUI.skin.button) { fontSize = 20, fontStyle = FontStyle.Bold, wordWrap = false };
        if (font) btn.font = font;
        btn.normal.background = Tex(new Color(0.22f, 0.23f, 0.27f));
        btn.hover.background = Tex(new Color(0.34f, 0.35f, 0.4f));
        btn.active.background = Tex(new Color(0.9f, 0.7f, 0.1f));
        btn.normal.textColor = btn.hover.textColor = Color.white;
        btn.active.textColor = Color.black;
        btnSmall = new GUIStyle(btn) { fontSize = 15 };
        banner = S(20, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
        field = new GUIStyle(GUI.skin.textField) { fontSize = 20 };
        if (font) field.font = font;
        field.normal.background = Tex(new Color(0.1f, 0.1f, 0.12f));
        field.focused.background = Tex(new Color(0.15f, 0.15f, 0.2f));
        field.normal.textColor = field.focused.textColor = Color.white;
    }

    // Call at the start of every OnGUI.
    public static void Begin()
    {
        EnsureStyles();
        scale = Mathf.Min(Screen.width / W, Screen.height / H);
        offset = new Vector2((Screen.width - W * scale) * 0.5f, (Screen.height - H * scale) * 0.5f);
        GUI.matrix = Matrix4x4.TRS(new Vector3(offset.x, offset.y, 0), Quaternion.identity, Vector3.one * scale);
        if (Event.current.type == EventType.Layout) { lastNavCount = navCount; }
        navCount = 0;
    }

    // Call once per frame from an Update().
    public static void NavUpdate()
    {
        if (Vector3.Distance(Input.mousePosition, lastMouse) > 2f) { padMode = false; lastMouse = Input.mousePosition; }
        if (suppressNav) return;
        int dir = 0;
        if (Bindings.AnyPadDown(Pad.DDown) || Bindings.AnyPadDown(Pad.DRight)) dir = 1;
        if (Bindings.AnyPadDown(Pad.DUp) || Bindings.AnyPadDown(Pad.DLeft)) dir = -1;
        var stick = Bindings.AnyStick();
        if (dir == 0 && stick.magnitude > 0.6f && Time.unscaledTime > repeatAt)
        {
            dir = (Mathf.Abs(stick.y) > Mathf.Abs(stick.x) ? -stick.y : stick.x) > 0 ? 1 : -1;
            repeatAt = Time.unscaledTime + 0.22f;
        }
        if (stick.magnitude < 0.3f) repeatAt = 0f;
        if (dir != 0)
        {
            if (!padMode) padMode = true;
            else navIndex = (navIndex + dir + Mathf.Max(1, lastNavCount)) % Mathf.Max(1, lastNavCount);
        }
        if (Bindings.AnyPadDown(Pad.A)) { if (padMode) activate = true; padMode = true; }
        if (Bindings.AnyPadDown(Pad.B)) back = true;
    }

    public static bool ConsumeBack()
    {
        bool b = back;
        back = false;
        return b;
    }

    public static void ResetFocus() { navIndex = 0; }

    public static bool Button(Rect r, string text, GUIStyle style = null, bool selected = false)
    {
        if (!GUI.enabled)
        {
            GUI.Button(r, text, style ?? btn);
            return false;
        }
        int id = navCount++;
        bool focused = padMode && id == navIndex;
        if (selected) Fill(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), new Color(1f, 0.8f, 0.1f, 0.6f));
        bool clicked = GUI.Button(r, text, style ?? btn);
        if (focused)
        {
            Outline(r, new Color(1f, 0.85f, 0.2f), 3);
            if (activate && Event.current.type == EventType.Repaint) { activate = false; clicked = true; }
        }
        return clicked;
    }

    public static bool Toggle(Rect r, bool v, string label)
    {
        if (Button(new Rect(r.x, r.y, 34, 34), v ? "X" : "")) v = !v;
        GUI.Label(new Rect(r.x + 46, r.y, r.width - 46, r.height), label, body);
        return v;
    }

    // -/+ stepper that works with both mouse and gamepad
    public static float Stepper(Rect r, string label, float value, float step, float min, float max, string fmt = "0.0")
    {
        GUI.Label(new Rect(r.x, r.y, r.width - 150, r.height), $"{label}: <b>{value.ToString(fmt)}</b>", body);
        if (Button(new Rect(r.x + r.width - 140, r.y, 64, r.height), "-")) value = Mathf.Max(min, value - step);
        if (Button(new Rect(r.x + r.width - 68, r.y, 64, r.height), "+")) value = Mathf.Min(max, value + step);
        return value;
    }

    public static void Fill(Rect r, Color c)
    {
        var old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = old;
    }

    public static void Outline(Rect r, Color c, float t)
    {
        Fill(new Rect(r.x, r.y, r.width, t), c);
        Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
        Fill(new Rect(r.x, r.y, t, r.height), c);
        Fill(new Rect(r.xMax - t, r.y, t, r.height), c);
    }

    // Mouse position in 1920x1080 GUI coordinates.
    public static Vector2 MouseGui()
    {
        Vector3 m = Input.mousePosition;
        return new Vector2((m.x - offset.x) / scale, (Screen.height - m.y - offset.y) / scale);
    }

    // GUI rect (1920x1080 space) to normalized viewport rect (for cameras).
    public static Rect GuiToViewport(Rect r)
    {
        float x = (r.x * scale + offset.x) / Screen.width;
        float w = r.width * scale / Screen.width;
        float h = r.height * scale / Screen.height;
        float y = 1f - (r.y * scale + offset.y) / Screen.height - h;
        return new Rect(x, y, w, h);
    }
}
