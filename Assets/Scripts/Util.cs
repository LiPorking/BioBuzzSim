using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public static class Layers
{
    public const int Field = 10;
    public const int Ball = 11;
    public const int Robot = 12;
    public const int Hive = 13;
    public static int BallMask => 1 << Ball;
    public static int RobotMask => 1 << Robot;
}

// Procedural geometry helpers. Everything in the game is built from primitives at runtime.
public static class Util
{
    static Material baseMat, transMat, lineMat;
    static readonly Dictionary<Color, Material> opaque = new Dictionary<Color, Material>();
    static readonly Dictionary<Color, Material> clear = new Dictionary<Color, Material>();
    static PhysicMaterial frictionless, ballMat, fieldMat;

    public static Material Mat(Color c)
    {
        if (opaque.TryGetValue(c, out var m) && m) return m;
        if (!baseMat)
        {
            baseMat = Resources.Load<Material>("BioBuzz/Base");
            if (!baseMat) baseMat = new Material(Shader.Find("Standard"));
        }
        m = new Material(baseMat) { color = c };
        m.SetFloat("_Glossiness", 0.25f);
        opaque[c] = m;
        return m;
    }

    public static Material TMat(Color c)
    {
        if (clear.TryGetValue(c, out var m) && m) return m;
        if (!transMat)
        {
            transMat = Resources.Load<Material>("BioBuzz/Transparent");
            if (!transMat) transMat = MakeTransparent(new Material(Shader.Find("Standard")));
        }
        m = new Material(transMat) { color = c };
        clear[c] = m;
        return m;
    }

    public static Material MakeTransparent(Material m)
    {
        m.SetFloat("_Mode", 3);
        m.SetInt("_SrcBlend", (int)BlendMode.One);
        m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.DisableKeyword("_ALPHATEST_ON");
        m.DisableKeyword("_ALPHABLEND_ON");
        m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
        m.renderQueue = 3000;
        return m;
    }

    public static Material LineMat()
    {
        if (lineMat) return lineMat;
        lineMat = Resources.Load<Material>("BioBuzz/Line");
        if (!lineMat)
        {
            var sh = Shader.Find("Sprites/Default");
            lineMat = sh ? new Material(sh) : Mat(Color.white);
        }
        return lineMat;
    }

    public static PhysicMaterial Frictionless => frictionless ??= new PhysicMaterial("Frictionless")
    { dynamicFriction = 0, staticFriction = 0, bounciness = 0, frictionCombine = PhysicMaterialCombine.Minimum, bounceCombine = PhysicMaterialCombine.Minimum };

    // Gopher ResisDent polyethylene balls (9.8): moderate bounce.
    public static PhysicMaterial BallMat => ballMat ??= new PhysicMaterial("Ball")
    { dynamicFriction = 0.5f, staticFriction = 0.6f, bounciness = 0.35f, frictionCombine = PhysicMaterialCombine.Average, bounceCombine = PhysicMaterialCombine.Average };

    public static PhysicMaterial FieldMat => fieldMat ??= new PhysicMaterial("Field")
    { dynamicFriction = 0.6f, staticFriction = 0.7f, bounciness = 0.2f, frictionCombine = PhysicMaterialCombine.Average, bounceCombine = PhysicMaterialCombine.Average };

    public static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;

    public static void SetLayer(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform t in go.transform) SetLayer(t.gameObject, layer);
    }

    static void StripCollider(GameObject go)
    {
        var c = go.GetComponent<Collider>();
        if (c) Object.DestroyImmediate(c);
    }

    public static GameObject Box(Transform parent, string name, Vector3 localPos, Vector3 size, Color c,
        bool collider = true, Quaternion? localRot = null, bool transparent = false)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = localRot ?? Quaternion.identity;
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = transparent ? TMat(c) : Mat(c);
        if (!collider) StripCollider(go);
        return go;
    }

    // Collider-only box (invisible).
    public static GameObject ColBox(Transform parent, string name, Vector3 localPos, Vector3 size, Quaternion? localRot = null, PhysicMaterial pm = null)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = localRot ?? Quaternion.identity;
        var bc = go.AddComponent<BoxCollider>();
        bc.size = size;
        if (pm) bc.sharedMaterial = pm;
        return go;
    }

    public static GameObject Cyl(Transform parent, string name, Vector3 localPos, float diameter, float length, Color c,
        Vector3 axis, bool collider = false, bool transparent = false)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, axis.normalized);
        go.transform.localScale = new Vector3(diameter, length * 0.5f, diameter);
        go.GetComponent<MeshRenderer>().sharedMaterial = transparent ? TMat(c) : Mat(c);
        if (!collider) StripCollider(go);
        return go;
    }

    public static GameObject Ball(Transform parent, string name, Vector3 localPos, float diameter, Color c, bool collider = false)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = Vector3.one * diameter;
        go.GetComponent<MeshRenderer>().sharedMaterial = Mat(c);
        if (!collider) StripCollider(go);
        return go;
    }

    // A flat ring (annulus) made of box segments, lying in the local XZ plane.
    public static GameObject Ring(Transform parent, string name, Vector3 center, float innerR, float radialT, float height,
        Color c, int segments = 20, float arcFrom = 0, float arcTo = 360, bool visual = true)
    {
        var root = new GameObject(name);
        root.transform.SetParent(parent, false);
        root.transform.localPosition = center;
        float span = arcTo - arcFrom;
        int n = Mathf.Max(1, Mathf.RoundToInt(segments * span / 360f));
        float step = span / n;
        float outer = innerR + radialT;
        float tangential = 2f * outer * Mathf.Tan(Mathf.Deg2Rad * step * 0.5f) * 1.02f;
        for (int i = 0; i < n; i++)
        {
            float a = arcFrom + step * (i + 0.5f);
            var rot = Quaternion.Euler(0, a, 0);
            var pos = rot * new Vector3(0, 0, innerR + radialT * 0.5f);
            var size = new Vector3(tangential, height, radialT);
            if (visual) Box(root.transform, "seg", pos, size, c, true, rot);
            else ColBox(root.transform, "seg", pos, size, rot);
        }
        return root;
    }

    // Convex polygon (XY plane) extruded along local Z by thickness, centred.
    public static Mesh PrismMesh(Vector2[] poly, float thickness)
    {
        int n = poly.Length;
        var verts = new List<Vector3>();
        var tris = new List<int>();
        float h = thickness * 0.5f;
        // caps
        for (int side = 0; side < 2; side++)
        {
            int start = verts.Count;
            float z = side == 0 ? -h : h;
            foreach (var p in poly) verts.Add(new Vector3(p.x, p.y, z));
            for (int i = 1; i < n - 1; i++)
            {
                if (side == 0) { tris.Add(start); tris.Add(start + i); tris.Add(start + i + 1); }
                else { tris.Add(start); tris.Add(start + i + 1); tris.Add(start + i); }
            }
        }
        // sides
        for (int i = 0; i < n; i++)
        {
            var a = poly[i]; var b = poly[(i + 1) % n];
            int s = verts.Count;
            verts.Add(new Vector3(a.x, a.y, -h)); verts.Add(new Vector3(b.x, b.y, -h));
            verts.Add(new Vector3(b.x, b.y, h)); verts.Add(new Vector3(a.x, a.y, h));
            tris.Add(s); tris.Add(s + 2); tris.Add(s + 1);
            tris.Add(s); tris.Add(s + 3); tris.Add(s + 2);
        }
        var m = new Mesh();
        m.SetVertices(verts);
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    public static GameObject Prism(Transform parent, string name, Vector2[] poly, float thickness, Vector3 localPos,
        Quaternion localRot, Color c, bool collider, bool transparent = false)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = localRot;
        var mesh = PrismMesh(poly, thickness);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = transparent ? TMat(c) : Mat(c);
        if (collider)
        {
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
            mc.convex = true;
        }
        return go;
    }

    public static TextMesh Label(Transform parent, string text, Vector3 localPos, Quaternion localRot, float charSize, Color c)
    {
        var go = new GameObject("Label");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = localRot;
        var tm = go.AddComponent<TextMesh>();
        tm.text = text;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.fontSize = 64;
        tm.characterSize = charSize;
        tm.color = c;
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font)
        {
            tm.font = font;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        }
        return tm;
    }

    // Projectile: speed required to pass through a target at horizontal distance d and
    // height difference h when launched at pitch theta (radians). Returns NaN if impossible.
    public static float LaunchSpeed(float d, float h, float theta)
    {
        float g = -Physics.gravity.y;
        float c = Mathf.Cos(theta);
        float denom = 2f * c * c * (d * Mathf.Tan(theta) - h);
        if (denom <= 0.0001f) return float.NaN;
        return Mathf.Sqrt(g * d * d / denom);
    }

    // Horizontal distance at which a shot of speed v, pitch theta reaches height h (descending root first).
    public static float RangeFor(float v, float h, float theta, bool descending = true)
    {
        float g = -Physics.gravity.y;
        float vx = v * Mathf.Cos(theta), vy = v * Mathf.Sin(theta);
        float disc = vy * vy - 2f * g * h;
        if (disc < 0) return float.NaN;
        float t = descending ? (vy + Mathf.Sqrt(disc)) / g : (vy - Mathf.Sqrt(disc)) / g;
        return vx * t;
    }

    public static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

    public static string Clock(float seconds)
    {
        seconds = Mathf.Max(0, seconds);
        int s = Mathf.CeilToInt(seconds);
        return $"{s / 60}:{s % 60:00}";
    }
}
