using UnityEngine;

public class CameraRig : MonoBehaviour
{
    public enum Mode { DriverStation, DriverTrack, ThirdPerson, Follow, Overhead, Audience, Orbit, Planner }
    public Mode mode = Mode.DriverStation;
    public static readonly string[] Names = { "Driver Station", "Driver Station (tracking)", "Third Person (fixed angle)", "Chase", "Overhead", "Audience", "Orbit", "Auto Planner" };
    public static CameraRig I;

    // Planner: orthographic top-down view framed inside a screen rectangle (normalized).
    public Rect plannerViewport = new Rect(0, 0.2f, 0.75f, 0.75f);
    public float plannerFlip;   // 180 when planning for the blue alliance

    Camera cam;
    float orbit;
    float tpYaw;
    bool tpInit;
    Robot tpRobot;

    void Awake()
    {
        I = this;
        cam = GetComponent<Camera>();
    }

    public void Cycle()
    {
        mode = (Mode)(((int)mode + 1) % 6);
        tpInit = false;
    }

    // Horizontal "up the screen" direction, used for camera-relative driving.
    public static Vector3 ScreenForward()
    {
        var c = Camera.main;
        if (c == null) return Vector3.forward;
        Vector3 f = c.transform.forward;
        Vector3 flat = new Vector3(f.x, 0, f.z);
        if (flat.magnitude < 0.35f)   // looking (almost) straight down: use the camera's up vector
        {
            Vector3 u = c.transform.up;
            flat = new Vector3(u.x, 0, u.z);
        }
        return flat.sqrMagnitude < 1e-6f ? Vector3.forward : flat.normalized;
    }

    void LateUpdate()
    {
        var mm = MatchManager.I;
        Robot focus = mm != null ? mm.FocusRobot() : null;
        int focusSlot = focus != null ? focus.humanSlot : 0;
        bool planner = PlannerUI.Active != null;

        if (!planner)
        {
            if (Bindings.Down(Act.CameraCycle, 0) || Bindings.AnyPadDown(Bindings.pads[(int)Act.CameraCycle])) Cycle();
            if (mode == Mode.ThirdPerson && (Input.GetKeyDown(KeyCode.G) || Bindings.AnyPadDown(Pad.L3)))
                tpYaw += 180f;
        }

        Mode m = mode;
        if (planner) m = Mode.Planner;
        else if (mm == null || (mm.period == Period.Menu && !(Game.I != null && Game.I.Rebuilding))) m = Mode.Orbit;
        if (focus == null && (m == Mode.Follow || m == Mode.DriverStation || m == Mode.DriverTrack || m == Mode.ThirdPerson)) m = Mode.Audience;

        cam.orthographic = m == Mode.Planner;
        cam.rect = new Rect(0, 0, 1, 1);

        Vector3 pos; Vector3 look;
        Vector3 upHint = Vector3.up;
        float lerp = 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime);
        switch (m)
        {
            case Mode.DriverStation:
                {
                    // Standing in the ALLIANCE AREA behind the operator console.
                    float s = focus.alliance.Sign();
                    // 10.3.2 C: station 1 stages closest to the audience (-Z).
                    float z = focus.station == 1 ? -0.55f : 0.55f;
                    pos = new Vector3(s * (Dims.Half + 0.95f), 1.75f, z);
                    look = new Vector3(-s * 0.4f, 0.2f, z * 0.3f);
                    lerp = 1f;
                    break;
                }
            case Mode.DriverTrack:
                {
                    // Same spot as DriverStation, but as a person rather than a tripod: weight
                    // shifts side to side along the wall, the head rises and falls a little, and
                    // the eyes stay on the ROBOT wherever it drives.
                    float s = focus.alliance.Sign();
                    float z = focus.station == 1 ? -0.55f : 0.55f;
                    float t = Time.unscaledTime;
                    // two out-of-phase sines per axis so the motion never visibly repeats
                    float sway = Mathf.Sin(t * 0.52f) * 0.26f + Mathf.Sin(t * 0.21f + 1.3f) * 0.11f;
                    float bob = Mathf.Sin(t * 0.83f) * 0.055f + Mathf.Sin(t * 0.34f + 0.7f) * 0.028f;
                    float lean = Mathf.Sin(t * 0.29f + 2.1f) * 0.06f;   // drifting towards / away from the wall
                    pos = new Vector3(s * (Dims.Half + 0.95f + lean), 1.68f + bob, z + sway);
                    // A driver tracks the ROBOT but keeps their head up. Follow it exactly from
                    // side to side, and clamp how far the view may tilt down, so a ROBOT against
                    // the near wall does not leave the camera staring at the floor.
                    Vector3 aim = focus.transform.position + Vector3.up * 0.16f;
                    Vector3 d = aim - pos;
                    Vector3 flat = new Vector3(d.x, 0f, d.z);
                    float maxDrop = flat.magnitude * Mathf.Tan(34f * Mathf.Deg2Rad);
                    if (-d.y > maxDrop) d.y = -maxDrop;
                    look = pos + d;
                    // fast enough to keep the sway, slow enough that tracking looks human
                    lerp = 1f - Mathf.Exp(-9f * Time.unscaledDeltaTime);
                    break;
                }
            case Mode.ThirdPerson:
                {
                    // Behind the robot at a fixed world angle: turning the robot does not swing the camera.
                    if (!tpInit || tpRobot != focus) { tpYaw = focus.transform.eulerAngles.y; tpInit = true; tpRobot = focus; }
                    Quaternion q = Quaternion.Euler(0, tpYaw, 0);
                    Vector3 fwd = q * Vector3.forward;
                    pos = focus.transform.position - fwd * 1.45f + Vector3.up * 1.15f;
                    look = focus.transform.position + fwd * 0.9f + Vector3.up * 0.2f;
                    break;
                }
            case Mode.Follow:
                {
                    Vector3 back = -focus.transform.forward;
                    pos = focus.transform.position + back * 1.25f + Vector3.up * 0.95f;
                    look = focus.transform.position + focus.transform.forward * 0.6f + Vector3.up * 0.25f;
                    break;
                }
            case Mode.Overhead:
                pos = new Vector3(0, 5.6f, -0.01f);
                look = Vector3.zero;
                lerp = 1f;
                break;
            case Mode.Planner:
                {
                    pos = new Vector3(0, 12f, 0);
                    look = Vector3.zero;
                    upHint = Quaternion.Euler(0, plannerFlip, 0) * Vector3.forward;
                    // frame the field (plus a margin) in the free part of the screen
                    float fieldSize = Dims.Half * 2f + 0.5f;
                    Rect vp = plannerViewport;
                    float aspect = (vp.width * Screen.width) / Mathf.Max(1f, vp.height * Screen.height);
                    cam.orthographicSize = aspect >= 1f ? fieldSize * 0.5f : fieldSize * 0.5f / aspect;
                    cam.rect = vp;
                    lerp = 1f;
                    break;
                }
            case Mode.Orbit:
                orbit += Time.unscaledDeltaTime * 8f;
                pos = Quaternion.Euler(0, orbit, 0) * new Vector3(0, 2.6f, -4.2f);
                look = new Vector3(0, 0.5f, 0);
                lerp = 1f;
                break;
            default:
                pos = new Vector3(0, 2.7f, -Dims.Half - 2.4f);
                look = new Vector3(0, 0.3f, 0.2f);
                lerp = 1f;
                break;
        }
        transform.position = Vector3.Lerp(transform.position, pos, lerp);
        var rot = Quaternion.LookRotation(look - transform.position, m == Mode.Planner ? upHint : Vector3.up);
        if (m == Mode.Planner) rot = Quaternion.LookRotation(Vector3.down, upHint);
        transform.rotation = lerp >= 1f ? rot : Quaternion.Slerp(transform.rotation, rot, lerp);
        cam.fieldOfView = m == Mode.Overhead ? 50f : 60f;
    }
}
