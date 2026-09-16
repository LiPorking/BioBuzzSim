using System.Collections.Generic;
using UnityEngine;

// 9.7 FLOWER: elements go in the top ring; POLLEN can be removed from the bottom.
public class Flower : MonoBehaviour
{
    public static readonly List<Flower> All = new List<Flower>();
    public int index;
    public Vector3 inward;   // horizontal unit vector from the wall into the FIELD

    // 10.5.2 scoring volume: between the top ring and the middle ring.
    public static float VolBottom => Dims.MiddleRingTop;
    public static float VolTop => Dims.FlowerTopHeight;
    const float VolRadius = 3.2f * Dims.IN;

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    // aim point: centre of the top ring, a little above its top face
    public Vector3 TopCenter => transform.position + Vector3.up * (Dims.FlowerTopHeight + 0.6f * Dims.IN);

    public float HorizDist(Vector3 p)
    {
        Vector3 d = p - transform.position;
        return new Vector2(d.x, d.z).magnitude;
    }

    public bool InVolume(GameElement e)
    {
        if (!e.IsFree) return false;
        Vector3 p = e.transform.position;
        float y = p.y - transform.position.y;
        return HorizDist(p) < VolRadius && y + e.Radius > VolBottom && y - e.Radius < VolTop;
    }

    // POLLEN sitting in the lower ring, reachable through the Retrieval Opening.
    public GameElement BottomPollen()
    {
        foreach (var e in GameElement.All)
        {
            if (!e.IsFree || e.type != ElementType.Pollen) continue;
            Vector3 p = e.transform.position;
            if (HorizDist(p) < 1.6f * Dims.IN && p.y - transform.position.y < Dims.MiddleRingBottom) return e;
        }
        return null;
    }

    public struct Result
    {
        public Alliance owner, bottomNectar;
        public int count;
    }

    // Owner = ALLIANCE with the top-most NECTAR of its colour in the volume.
    // Bottom NECTAR Bonus = ALLIANCE with the bottom-most NECTAR in the volume.
    public Result Evaluate()
    {
        var r = new Result { owner = Alliance.None, bottomNectar = Alliance.None };
        float top = float.MinValue, bottom = float.MaxValue;
        foreach (var e in GameElement.All)
        {
            if (!InVolume(e)) continue;
            r.count++;
            if (e.type != ElementType.Nectar) continue;
            float y = e.transform.position.y;
            if (y > top) { top = y; r.owner = e.color; }
            if (y < bottom) { bottom = y; r.bottomNectar = e.color; }
        }
        return r;
    }
}
