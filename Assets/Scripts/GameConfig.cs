using UnityEngine;

public enum ControlType { Empty, AI, Keyboard, Gamepad1, Gamepad2, Gamepad3, Gamepad4 }

public class SlotConfig
{
    public ControlType control;
    public int robot;
    public int start;
    public string autoPlan = "";   // "" = built-in AUTO, otherwise a saved AUTO PLANNER plan
    public SlotConfig(ControlType c, int robot, int start) { control = c; this.robot = robot; this.start = start; }
}

public static class GameConfig
{
    public static bool practice;
    // Red 1, Red 2, Blue 1, Blue 2
    public static readonly SlotConfig[] slots =
    {
        new SlotConfig(ControlType.Keyboard, 0, 0),
        new SlotConfig(ControlType.AI, 1, 1),
        new SlotConfig(ControlType.AI, 2, 0),
        new SlotConfig(ControlType.AI, 3, 1),
    };
    public static bool trajectory = true;
    public const bool autoHumanPlayer = true;   // the HUMAN PLAYER always enters NECTAR automatically
    public static bool driverInAuto;

    public static Alliance SlotAlliance(int i) => i < 2 ? Alliance.Red : Alliance.Blue;
    public static int SlotStation(int i) => i % 2 + 1;
    public static string SlotName(int i) => $"{SlotAlliance(i)} {SlotStation(i)}";

    public static readonly string[] StartNames = { "Alliance wall", "Audience-side wall", "Rear wall" };

    public static int ControlSlot(ControlType c) => c == ControlType.Keyboard ? 0 : c >= ControlType.Gamepad1 ? (int)c - (int)ControlType.Gamepad1 + 1 : -1;

    // G304 legal starting spots on the red half (columns A-C), touching the perimeter,
    // outside the LOADING ZONE and FLOWERS. Blue positions are the 180 deg rotation.
    public static void StartPose(int slot, RobotSpec spec, out Vector3 pos, out float yaw)
    {
        var a = SlotAlliance(slot);
        var plan = string.IsNullOrEmpty(slots[slot].autoPlan) ? null : AutoPlan.Load(slots[slot].autoPlan);
        if (plan != null && plan.waypoints.Count > 0)
        {
            var w = plan.ForAlliance(a).waypoints[0];
            pos = new Vector3(w.x, 0, w.z);
            yaw = w.heading;
            return;
        }
        float half = spec.lengthIn * 0.5f + 0.02f;   // G304.C: touching the FIELD perimeter
        float x, z;
        switch (slots[slot].start)
        {
            case 1: x = -36f; z = -72f + half; yaw = 0; break;
            case 2: x = -50f; z = 72f - half; yaw = 180; break;
            default: x = -72f + half; z = 0f; yaw = 90; break;
        }
        pos = Dims.V(x, 0f, z);
        if (a == Alliance.Blue)
        {
            pos = new Vector3(-pos.x, pos.y, -pos.z);
            yaw += 180f;
        }
    }
}
