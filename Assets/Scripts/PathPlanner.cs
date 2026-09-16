using System.Collections.Generic;
using UnityEngine;

public struct TrajPoint
{
    public float t, s;
    public Vector2 p;        // field XZ (metres)
    public float heading;    // degrees
    public Vector2 v;        // m/s (field XZ)
    public float omega;      // deg/s, positive = heading increasing
}

public class Trajectory
{
    public readonly List<TrajPoint> pts = new List<TrajPoint>();
    public float Duration => pts.Count > 0 ? pts[pts.Count - 1].t : 0f;
    public float Length => pts.Count > 0 ? pts[pts.Count - 1].s : 0f;
    public TrajPoint End => pts[pts.Count - 1];

    public TrajPoint Sample(float t)
    {
        if (pts.Count == 0) return default;
        if (t <= 0) return pts[0];
        if (t >= Duration) { var e = End; e.v = Vector2.zero; e.omega = 0; return e; }
        int lo = 0, hi = pts.Count - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (pts[mid].t <= t) lo = mid; else hi = mid;
        }
        var a = pts[lo]; var b = pts[hi];
        float k = b.t > a.t ? (t - a.t) / (b.t - a.t) : 0f;
        return new TrajPoint
        {
            t = t,
            s = Mathf.Lerp(a.s, b.s, k),
            p = Vector2.Lerp(a.p, b.p, k),
            heading = Mathf.LerpAngle(a.heading, b.heading, k),
            v = Vector2.Lerp(a.v, b.v, k),
            omega = Mathf.Lerp(a.omega, b.omega, k),
        };
    }
}

// Obstacle-aware path generation (A* on a 4 cm grid + Hermite smoothing) and time
// parameterisation with the robot's acceleration, cornering and turn-rate limits.
public static class PathPlanner
{
    const float Cell = 0.04f;
    static int n;
    static float origin;
    static float[] clearance;   // distance to the nearest hard obstacle (HIVE frame, FLOWERS)
    static float[] wallDist;    // distance to the perimeter

    static void EnsureGrid()
    {
        if (clearance != null) return;
        origin = -Dims.Half;
        n = Mathf.CeilToInt(Dims.Half * 2f / Cell);
        clearance = new float[n * n];
        wallDist = new float[n * n];
        var flowers = new List<Vector2>();
        for (int i = 0; i < 4; i++) { var fp = Field.FlowerPos(i, out _); flowers.Add(new Vector2(fp.x, fp.z)); }
        float hd = Dims.FrameDepth * 0.5f, hw = Dims.FrameWidth * 0.5f;
        for (int ix = 0; ix < n; ix++)
            for (int iz = 0; iz < n; iz++)
            {
                Vector2 p = CellCenter(ix, iz);
                float c = float.MaxValue;
                foreach (float sx in new[] { -hw, hw })
                {
                    float dz = Mathf.Max(0, Mathf.Abs(p.y) - hd);
                    c = Mathf.Min(c, new Vector2(p.x - sx, dz).magnitude - 0.02f);
                }
                foreach (var f in flowers) c = Mathf.Min(c, Vector2.Distance(p, f) - 0.075f);
                clearance[ix * n + iz] = c;
                wallDist[ix * n + iz] = Dims.Half - Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y));
            }
    }

    static Vector2 CellCenter(int ix, int iz) => new Vector2(origin + (ix + 0.5f) * Cell, origin + (iz + 0.5f) * Cell);
    static int ToCell(float v) => Mathf.Clamp(Mathf.FloorToInt((v - origin) / Cell), 0, n - 1);

    public static float HardRadius(RobotSpec s) => Mathf.Sqrt(s.lengthIn * s.lengthIn + s.widthIn * s.widthIn) * 0.5f * Dims.IN * 0.9f;
    public static float WallRadius(RobotSpec s) => Mathf.Min(s.lengthIn, s.widthIn) * 0.5f * Dims.IN * 0.97f;

    public static bool Free(Vector2 p, RobotSpec s)
    {
        EnsureGrid();
        int i = ToCell(p.x) * n + ToCell(p.y);
        return clearance[i] >= HardRadius(s) && wallDist[i] >= WallRadius(s);
    }

    static float CellCost(int ix, int iz, float rh, float rw)
    {
        int i = ix * n + iz;
        float c = clearance[i], w = wallDist[i];
        if (c < 0.02f) return -1f;                         // inside the frame / FLOWER: impassable
        float cost = 1f;
        if (c < rh) cost += 60f;                           // would collide: only to escape a bad start
        else if (c < rh + 0.15f) cost += (rh + 0.15f - c) * 12f;
        if (w < rw) cost += 60f;
        else if (w < rw + 0.08f) cost += (rw + 0.08f - w) * 6f;
        return cost;
    }

    static bool LineFree(Vector2 a, Vector2 b, RobotSpec s)
    {
        float len = Vector2.Distance(a, b);
        int steps = Mathf.Max(1, Mathf.CeilToInt(len / 0.02f));
        for (int k = 1; k < steps; k++)
            if (!Free(Vector2.Lerp(a, b, (float)k / steps), s)) return false;
        return true;
    }

    // Shortest collision-free route (as few straight legs as possible).
    public static List<Vector2> Route(Vector2 a, Vector2 b, RobotSpec s)
    {
        EnsureGrid();
        var result = new List<Vector2> { a };
        if (LineFree(a, b, s)) { result.Add(b); return result; }

        float rh = HardRadius(s), rw = WallRadius(s);
        int start = ToCell(a.x) * n + ToCell(a.y), goal = ToCell(b.x) * n + ToCell(b.y);
        var g = new float[n * n];
        var from = new int[n * n];
        var closed = new bool[n * n];
        for (int i = 0; i < g.Length; i++) { g[i] = float.MaxValue; from[i] = -1; }
        var open = new SimpleHeap();
        g[start] = 0;
        open.Push(start, 0);
        int[] dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
        int[] dz = { 0, 0, 1, -1, 1, -1, 1, -1 };
        while (open.Count > 0)
        {
            int cur = open.Pop();
            if (closed[cur]) continue;
            closed[cur] = true;
            if (cur == goal) break;
            int cx = cur / n, cz = cur % n;
            for (int k = 0; k < 8; k++)
            {
                int nx = cx + dx[k], nz = cz + dz[k];
                if (nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
                int ni = nx * n + nz;
                if (closed[ni]) continue;
                float cc = CellCost(nx, nz, rh, rw);
                if (cc < 0 && ni != goal) continue;
                float step = (k < 4 ? 1f : 1.4142f) * Mathf.Max(1f, cc);
                float ng = g[cur] + step;
                if (ng < g[ni])
                {
                    g[ni] = ng;
                    from[ni] = cur;
                    Vector2 d = CellCenter(nx, nz) - b;
                    open.Push(ni, ng + d.magnitude / Cell);
                }
            }
        }
        if (from[goal] < 0) { result.Add(b); return result; }   // no route: go straight

        var cells = new List<Vector2>();
        for (int c = goal; c != -1 && c != start; c = from[c]) cells.Add(CellCenter(c / n, c % n));
        cells.Reverse();
        cells[cells.Count - 1] = b;

        // string pulling: skip to the farthest visible point
        Vector2 anchor = a;
        int idx = 0;
        while (idx < cells.Count)
        {
            int far = idx;
            for (int j = cells.Count - 1; j > idx; j--)
                if (LineFree(anchor, cells[j], s)) { far = j; break; }
            anchor = cells[far];
            result.Add(anchor);
            idx = far + 1;
        }
        return result;
    }

    class SimpleHeap
    {
        readonly List<(int id, float f)> items = new List<(int, float)>();
        public int Count => items.Count;
        public void Push(int id, float f)
        {
            items.Add((id, f));
            int i = items.Count - 1;
            while (i > 0)
            {
                int p = (i - 1) / 2;
                if (items[p].f <= items[i].f) break;
                (items[p], items[i]) = (items[i], items[p]);
                i = p;
            }
        }
        public int Pop()
        {
            var top = items[0].id;
            items[0] = items[items.Count - 1];
            items.RemoveAt(items.Count - 1);
            int i = 0;
            while (true)
            {
                int l = i * 2 + 1, r = l + 1, m = i;
                if (l < items.Count && items[l].f < items[m].f) m = l;
                if (r < items.Count && items[r].f < items[m].f) m = r;
                if (m == i) break;
                (items[m], items[i]) = (items[i], items[m]);
                i = m;
            }
            return top;
        }
    }

    static Vector2 Dir(float headingDeg) => new Vector2(Mathf.Sin(headingDeg * Mathf.Deg2Rad), Mathf.Cos(headingDeg * Mathf.Deg2Rad));
    static float Heading(Vector2 d) => Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;

    // Plan a drive from the current pose through a run of waypoints (the last one is a stop).
    public static Trajectory PlanRun(Vector2 start, float startHeading, IList<PlanWaypoint> run, RobotSpec spec, float accelScale, float speedCap = 99f)
    {
        bool tank = spec.drive == DriveType.Tank;
        float accel = Mathf.Max(0.5f, spec.accel * Mathf.Clamp(accelScale, 0.2f, 1f));
        float maxTurn = spec.maxTurnDeg * 0.7f;
        const float alpha = 720f;            // deg/s^2 for turning in place
        const float aLat = 2.5f;             // m/s^2 cornering

        // ---- geometry: dense samples with heading and leg index
        var samples = new List<Vector2>();
        var headings = new List<float>();
        var legOf = new List<int>();
        Vector2 cur = start;
        float curHeading = startHeading;
        for (int leg = 0; leg < run.Count; leg++)
        {
            var w = run[leg];
            Vector2 target = new Vector2(w.x, w.z);
            var route = Route(cur, target, spec);
            // Hermite tangents
            var tangents = new Vector2[route.Count];
            for (int i = 0; i < route.Count; i++)
            {
                Vector2 prev = i > 0 ? route[i - 1] : route[i];
                Vector2 next = i < route.Count - 1 ? route[i + 1] : route[i];
                Vector2 d = next - prev;
                tangents[i] = d.sqrMagnitude > 1e-8f ? d.normalized : Vector2.zero;
            }
            if (tank)
            {
                float sign = w.reverse ? -1f : 1f;
                if (leg == 0) tangents[0] = Dir(startHeading) * sign;
                tangents[route.Count - 1] = Dir(w.heading) * sign;
            }
            for (int i = 0; i < route.Count - 1; i++)
            {
                Vector2 p0 = route[i], p1 = route[i + 1];
                float L = Vector2.Distance(p0, p1);
                if (L < 1e-4f) continue;
                int steps = Mathf.Max(2, Mathf.CeilToInt(L / 0.01f));
                var span = new List<Vector2>(steps);
                bool ok = true;
                Vector2 m0 = tangents[i] * L, m1 = tangents[i + 1] * L;
                for (int k = 1; k <= steps; k++)
                {
                    float u = (float)k / steps, u2 = u * u, u3 = u2 * u;
                    Vector2 q = (2 * u3 - 3 * u2 + 1) * p0 + (u3 - 2 * u2 + u) * m0 + (-2 * u3 + 3 * u2) * p1 + (u3 - u2) * m1;
                    span.Add(q);
                    if (!Free(q, spec) && Free(p0, spec) && Free(p1, spec)) ok = false;
                }
                if (!ok)
                {
                    span.Clear();
                    for (int k = 1; k <= steps; k++) span.Add(Vector2.Lerp(p0, p1, (float)k / steps));
                }
                if (samples.Count == 0) { samples.Add(p0); headings.Add(curHeading); legOf.Add(leg); }
                foreach (var q in span) { samples.Add(q); headings.Add(0); legOf.Add(leg); }
            }
            if (samples.Count == 0) { samples.Add(cur); headings.Add(curHeading); legOf.Add(leg); }
            cur = target;
            curHeading = w.heading;
        }
        if (samples.Count < 2) { samples.Add(samples[0]); headings.Add(run.Count > 0 ? run[run.Count - 1].heading : startHeading); legOf.Add(Mathf.Max(0, run.Count - 1)); }

        int N = samples.Count;
        var s = new float[N];
        for (int i = 1; i < N; i++) s[i] = s[i - 1] + Vector2.Distance(samples[i - 1], samples[i]);

        // ---- headings
        if (tank)
        {
            for (int i = 0; i < N; i++)
            {
                Vector2 d = i < N - 1 ? samples[i + 1] - samples[i] : samples[i] - samples[i - 1];
                bool rev = run[Mathf.Clamp(legOf[i], 0, run.Count - 1)].reverse;
                headings[i] = d.sqrMagnitude > 1e-10f ? Heading(rev ? -d : d) : (i > 0 ? headings[i - 1] : startHeading);
            }
        }
        else
        {
            // holonomic: rotate smoothly from each leg's start heading to its waypoint heading
            float legStartHeading = startHeading;
            int legStartIdx = 0;
            for (int i = 0; i < N; i++)
            {
                int leg = legOf[i];
                bool lastOfLeg = i == N - 1 || legOf[i + 1] != leg;
                if (lastOfLeg)
                {
                    float h1 = run[leg].heading;
                    float s0 = s[legStartIdx], s1 = s[i];
                    for (int j = legStartIdx; j <= i; j++)
                    {
                        float u = s1 > s0 ? (s[j] - s0) / (s1 - s0) : 1f;
                        u = u * u * (3 - 2 * u);
                        headings[j] = legStartHeading + Mathf.DeltaAngle(legStartHeading, h1) * u;
                    }
                    legStartHeading = h1;
                    legStartIdx = i;
                }
            }
        }

        // ---- velocity limits
        var vmax = new float[N];
        for (int i = 0; i < N; i++)
        {
            float limit = Mathf.Min(run[Mathf.Clamp(legOf[i], 0, run.Count - 1)].speed, spec.maxSpeed * (tank ? 0.97f : 0.9f), speedCap);
            if (i > 0 && i < N - 1)
            {
                Vector2 a = samples[i] - samples[i - 1], b = samples[i + 1] - samples[i];
                float ds = (a.magnitude + b.magnitude) * 0.5f;
                if (ds > 1e-5f && a.sqrMagnitude > 1e-10f && b.sqrMagnitude > 1e-10f)
                {
                    float dAng = Mathf.Abs(Vector2.SignedAngle(a, b)) * Mathf.Deg2Rad;
                    float kappa = dAng / ds;
                    if (kappa > 1e-3f)
                    {
                        limit = Mathf.Min(limit, Mathf.Sqrt(aLat / kappa));
                        if (tank) limit = Mathf.Min(limit, maxTurn * Mathf.Deg2Rad / kappa);
                    }
                }
                if (!tank)
                {
                    float dh = Mathf.Abs(Mathf.DeltaAngle(headings[i - 1], headings[i + 1])) / Mathf.Max(1e-4f, s[i + 1] - s[i - 1]);
                    if (dh > 1e-3f) limit = Mathf.Min(limit, maxTurn / dh);
                }
            }
            vmax[i] = Mathf.Max(0.05f, limit);
        }
        vmax[0] = 0f;
        vmax[N - 1] = 0f;
        var v = new float[N];
        for (int i = 1; i < N; i++) v[i] = Mathf.Min(vmax[i], Mathf.Sqrt(v[i - 1] * v[i - 1] + 2 * accel * (s[i] - s[i - 1])));
        v[N - 1] = 0f;
        for (int i = N - 2; i >= 0; i--) v[i] = Mathf.Min(v[i], Mathf.Sqrt(v[i + 1] * v[i + 1] + 2 * accel * (s[i + 1] - s[i])));
        v[0] = 0f;

        // ---- assemble, with turn-in-place where the drive cannot rotate while moving
        var traj = new Trajectory();
        float t = 0;
        void AddTurn(Vector2 p, float from, float to, float sAt)
        {
            float delta = Mathf.DeltaAngle(from, to);
            float ang = Mathf.Abs(delta);
            if (ang < 2f) return;
            float wmax = maxTurn;
            float tAcc = wmax / alpha;
            float angAcc = 0.5f * alpha * tAcc * tAcc;
            float total = ang < 2 * angAcc ? 2 * Mathf.Sqrt(ang / alpha) : 2 * tAcc + (ang - 2 * angAcc) / wmax;
            int steps = Mathf.Max(4, Mathf.CeilToInt(total / 0.02f));
            for (int k = 1; k <= steps; k++)
            {
                float u = (float)k / steps;
                float e = u * u * (3 - 2 * u);
                traj.pts.Add(new TrajPoint { t = t + total * u, s = sAt, p = p, heading = from + delta * e, v = Vector2.zero, omega = Mathf.Sign(delta) * wmax * Mathf.Sin(u * Mathf.PI) });
            }
            t += total;
        }

        traj.pts.Add(new TrajPoint { t = 0, s = 0, p = samples[0], heading = startHeading });
        if (tank) AddTurn(samples[0], startHeading, headings[0], 0);
        for (int i = 1; i < N; i++)
        {
            float ds = s[i] - s[i - 1];
            float vAvg = Mathf.Max(0.03f, 0.5f * (v[i] + v[i - 1]));
            float dt = ds > 1e-6f ? ds / vAvg : 0f;
            t += dt;
            Vector2 d = samples[i] - samples[i - 1];
            Vector2 vel = d.sqrMagnitude > 1e-12f ? d.normalized * v[i] : Vector2.zero;
            float omega = dt > 1e-5f ? Mathf.DeltaAngle(headings[i - 1], headings[i]) / dt : 0f;
            traj.pts.Add(new TrajPoint { t = t, s = s[i], p = samples[i], heading = headings[i], v = vel, omega = omega });
        }
        // final heading (tank drives turn in place at the end; holonomic already interpolated)
        float endHeading = run.Count > 0 ? run[run.Count - 1].heading : headings[N - 1];
        AddTurn(samples[N - 1], traj.End.heading, endHeading, s[N - 1]);
        return traj;
    }

    // Straight-line-ish trajectory to a single pose (helper for dynamic actions).
    public static Trajectory PlanTo(Vector2 start, float startHeading, Vector2 goal, float goalHeading, RobotSpec spec, float accelScale, float speed, bool reverse = false)
    {
        var w = new PlanWaypoint { x = goal.x, z = goal.y, heading = goalHeading, speed = speed, stop = true, reverse = reverse };
        return PlanRun(start, startHeading, new List<PlanWaypoint> { w }, spec, accelScale);
    }
}
