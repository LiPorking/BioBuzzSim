using System.Collections.Generic;
using UnityEngine;

// A SCORING ELEMENT (9.8): POLLEN (2.8 in yellow) or NECTAR (3.6 in red/blue).
public class GameElement : MonoBehaviour
{
    public static readonly List<GameElement> All = new List<GameElement>();

    public ElementType type;
    public Alliance color = Alliance.None;   // NECTAR colour
    public Rigidbody rb;
    public SphereCollider col;
    public Robot holder;
    public Alliance lastTouch = Alliance.None;
    public bool inFlowerVolume;               // used for G410 detection
    public bool removed;                      // NECTAR returned to its ALLIANCE AREA

    Robot ignoredRobot;
    float ignoreUntil;
    float outSince = -1;
    bool interpolateNext;

    public float Radius => (type == ElementType.Pollen ? Dims.PollenDia : Dims.NectarDia) * 0.5f;
    public bool IsFree => holder == null && !removed && gameObject.activeInHierarchy;
    public float Weight => type == ElementType.Pollen ? 1f : 1.6f;

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    public static GameElement Create(Transform parent, ElementType type, Alliance color, Vector3 worldPos)
    {
        float dia = type == ElementType.Pollen ? Dims.PollenDia : Dims.NectarDia;
        Color c = type == ElementType.Pollen ? new Color(1f, 0.85f, 0.05f) : color.Col();
        var go = Util.Ball(parent, type == ElementType.Pollen ? "POLLEN" : $"NECTAR_{color}", Vector3.zero, dia, c, true);
        go.transform.position = worldPos;
        go.layer = Layers.Ball;
        var e = go.AddComponent<GameElement>();
        e.type = type;
        e.color = color;
        e.col = go.GetComponent<SphereCollider>();
        e.col.sharedMaterial = Util.BallMat;
        e.rb = go.AddComponent<Rigidbody>();
        // Mass is not listed in the manual; values approximate hollow polyethylene balls.
        e.rb.mass = type == ElementType.Pollen ? 0.025f : 0.045f;
        e.rb.drag = 0f;
        e.rb.angularDrag = 1.0f;
        e.rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        e.rb.interpolation = RigidbodyInterpolation.Interpolate;
        return e;
    }

    public void Hold(Robot r, Transform slot)
    {
        holder = r;
        lastTouch = r.alliance;
        rb.interpolation = RigidbodyInterpolation.None;
        rb.isKinematic = true;
        col.enabled = false;
        transform.SetParent(slot, false);
        transform.localPosition = Vector3.zero;
    }

    // Teleport safely: an interpolated rigidbody would otherwise snap back to its old pose.
    public void Place(Vector3 pos)
    {
        rb.interpolation = RigidbodyInterpolation.None;
        transform.position = pos;
        rb.position = pos;
        outSince = -1;
    }

    public void Release(Vector3 pos, Vector3 velocity, Robot from)
    {
        transform.SetParent(Game.World, true);
        Place(pos);
        holder = null;
        col.enabled = true;
        rb.isKinematic = false;
        rb.velocity = velocity;
        interpolateNext = true;
        rb.angularVelocity = Vector3.zero;
        if (from != null)
        {
            lastTouch = from.alliance;
            IgnoreRobot(from, 0.35f);
        }
    }

    public void IgnoreRobot(Robot r, float seconds)
    {
        if (ignoredRobot != null && ignoredRobot != r) SetIgnore(ignoredRobot, false);
        ignoredRobot = r;
        ignoreUntil = Time.time + seconds;
        SetIgnore(r, true);
    }

    void SetIgnore(Robot r, bool ignore)
    {
        if (r == null) return;
        foreach (var c in r.Colliders) if (c) Physics.IgnoreCollision(col, c, ignore);
    }

    public void ReturnToAllianceArea()
    {
        removed = true;
        if (holder != null) holder.Drop(this);
        gameObject.SetActive(false);
    }

    void FixedUpdate()
    {
        if (interpolateNext && holder == null) { interpolateNext = false; rb.interpolation = RigidbodyInterpolation.Interpolate; }
        if (ignoredRobot != null && Time.time > ignoreUntil)
        {
            SetIgnore(ignoredRobot, false);
            ignoredRobot = null;
        }
        if (holder != null || rb.isKinematic) return;

        // Foam TILES have a lot of rolling resistance; PhysX has none, so add some.
        Vector3 p = transform.position;
        if (p.y < Radius + 0.004f)
        {
            Vector3 v = rb.velocity;
            float k = Mathf.Clamp01(1.4f * Time.fixedDeltaTime);
            v.x *= 1 - k; v.z *= 1 - k;
            if (new Vector2(v.x, v.z).sqrMagnitude < 0.0004f) { v.x = 0; v.z = 0; }
            rb.velocity = v;
        }

        // 10.8.2: elements that exit the FIELD.
        bool outside = Mathf.Abs(p.x) > Dims.Half + 0.02f || Mathf.Abs(p.z) > Dims.Half + 0.02f || p.y < -0.5f;
        if (outside)
        {
            if (outSince < 0) outSince = Time.time;
            if (Time.time - outSince > 1.5f && (p.y < 0.3f || p.y < -0.5f))
            {
                outSince = -1;
                if (type == ElementType.Nectar && MatchManager.I != null)
                {
                    MatchManager.I.NectarExited(this);
                }
                else
                {
                    // POLLEN is reintroduced by FIELD STAFF at the nearest convenient location.
                    float m = Dims.Half - 0.08f;
                    var np = new Vector3(Mathf.Clamp(p.x, -m, m), 0.08f, Mathf.Clamp(p.z, -m, m));
                    Place(np);
                    rb.velocity = Vector3.zero;
                    interpolateNext = true;
                }
            }
        }
        else outSince = -1;
    }
}
