using UnityEngine;

// 9.6 HIVE: a bi-stable pivoting pair of CELLS. LAUNCHING enough elements into the
// upward-facing CELL tips it (10.5.1). The manual does not state how many elements
// are "enough", so the threshold is a setting (weight units: POLLEN 1, NECTAR 1.6).
public class Hive : MonoBehaviour
{
    public static float TipThreshold = 8f;
    const float TipDuration = 0.55f;

    public Alliance alliance;
    public int upEnd;          // +1: CELL at +Z end is up, -1: CELL at -Z end is up
    public int tipsAuto, tipsTeleop;
    public int Tips => tipsAuto + tipsTeleop;

    Rigidbody rb;
    Quaternion baseRot;
    float angle, fromAngle, toAngle, t;
    bool tipping;
    float settleUntil;

    public static float AngleFor(int end) => end > 0 ? -Dims.HiveTilt : Dims.HiveTilt;

    public void Init(Alliance a, int startUpEnd)
    {
        alliance = a;
        upEnd = startUpEnd;
        baseRot = Quaternion.identity;
        angle = AngleFor(upEnd);
        transform.localRotation = Quaternion.Euler(angle, 0, 0);
        rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    // Centre of the upward CELL's opening (the outer pentagon face), used for aiming.
    public Vector3 AimPoint()
    {
        return transform.TransformPoint(new Vector3(0, -Dims.ArmOffset + 7.0f * Dims.IN, upEnd * (Dims.CellOuter - 0.5f * Dims.IN)));
    }

    // Horizontal direction the up CELL's opening faces (away from the pivot).
    public Vector3 OpeningDir()
    {
        var d = Util.Flat(transform.TransformDirection(new Vector3(0, 0, upEnd)));
        return d.sqrMagnitude < 1e-6 ? Vector3.forward : d.normalized;
    }

    public bool InCell(GameElement e, int end)
    {
        Vector3 p = transform.InverseTransformPoint(e.transform.position);
        float r = e.Radius;
        float u = p.z * end;
        float v = p.y + Dims.ArmOffset;
        return u > Dims.CellInner - r * 0.5f && u < Dims.CellOuter + r * 0.2f
            && Mathf.Abs(p.x) < Dims.CellWidth * 0.5f + r * 0.2f
            && v > -r && v < Dims.CellHeight + r;
    }

    public float WeightInCell(int end, bool requireSettled)
    {
        float w = 0;
        foreach (var e in GameElement.All)
        {
            if (!e.IsFree) continue;
            if (requireSettled && e.rb.velocity.sqrMagnitude > 2.5f) continue;
            if (InCell(e, end)) w += e.Weight;
        }
        return w;
    }

    public int CountInUpCell()
    {
        int n = 0;
        foreach (var e in GameElement.All) if (e.IsFree && InCell(e, upEnd)) n++;
        return n;
    }

    void FixedUpdate()
    {
        if (tipping)
        {
            t += Time.fixedDeltaTime / TipDuration;
            float k = Mathf.SmoothStep(0, 1, Mathf.Clamp01(t));
            angle = Mathf.Lerp(fromAngle, toAngle, k);
            rb.MoveRotation(transform.parent.rotation * baseRot * Quaternion.Euler(angle, 0, 0));
            if (t >= 1f)
            {
                tipping = false;
                settleUntil = Time.time + 0.5f;
                // 10.5.1 B: the damper that was not touching the frame now contacts it -> TIPPED.
                if (MatchManager.I != null) MatchManager.I.OnHiveTipped(this);
            }
            return;
        }
        if (Time.time < settleUntil) return;
        if (MatchManager.I != null && !MatchManager.I.FieldLive) return;
        if (WeightInCell(upEnd, true) >= TipThreshold) StartTip();
    }

    void StartTip()
    {
        tipping = true;
        fromAngle = angle;
        upEnd = -upEnd;
        toAngle = AngleFor(upEnd);
        t = 0;
    }
}
