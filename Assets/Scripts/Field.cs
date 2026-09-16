using UnityEngine;

// Axis-aligned floor zone (infinitely tall, as defined in 9.3).
public struct Zone
{
    public Vector2 min, max;   // metres, field XZ
    public Alliance alliance;
    public Zone(float x0In, float x1In, float z0In, float z1In, Alliance a)
    {
        min = new Vector2(Mathf.Min(x0In, x1In), Mathf.Min(z0In, z1In)) * Dims.IN;
        max = new Vector2(Mathf.Max(x0In, x1In), Mathf.Max(z0In, z1In)) * Dims.IN;
        alliance = a;
    }
    public Vector3 Center => new Vector3((min.x + max.x) * 0.5f, 0, (min.y + max.y) * 0.5f);
    public bool ContainsPartially(Vector3 p, float r) =>
        p.x + r >= min.x && p.x - r <= max.x && p.z + r >= min.y && p.z - r <= max.y;
}

// Builds the BIOBUZZ FIELD (Section 9). World frame: origin at FIELD centre on the TILE
// surface, +X towards the blue ALLIANCE AREA, +Z away from the audience, +Y up.
public static class Field
{
    public static Hive RedHive, BlueHive;
    public static Hive HiveOf(Alliance a) => a == Alliance.Red ? RedHive : BlueHive;

    // 9.3 / Figure 9-2 / Figure 9-3
    public static readonly Zone RedLoading = new Zone(-72f, -72f + 11f, 24.5f, 47.5f, Alliance.Red);
    public static readonly Zone BlueLoading = new Zone(72f, 72f - 11f, -24.5f, -47.5f, Alliance.Blue);
    public static readonly Zone RedGarden = new Zone(-72f, -49f, -72f, -70f, Alliance.Red);
    public static readonly Zone BlueGarden = new Zone(72f, 49f, 72f, 70f, Alliance.Blue);
    public static Zone LoadingOf(Alliance a) => a == Alliance.Red ? RedLoading : BlueLoading;
    public static Zone GardenOf(Alliance a) => a == Alliance.Red ? RedGarden : BlueGarden;

    static readonly Color TileA = new Color(0.36f, 0.36f, 0.38f);
    static readonly Color TileB = new Color(0.40f, 0.40f, 0.42f);
    static readonly Color WallC = new Color(0.16f, 0.16f, 0.18f);
    static readonly Color FrameC = new Color(0.62f, 0.64f, 0.66f);
    static readonly Color Panel = new Color(0.9f, 0.93f, 0.95f, 0.22f);

    public static void Build(Transform world, bool stageElements)
    {
        var root = new GameObject("FIELD").transform;
        root.SetParent(world, false);

        BuildFloorAndWalls(root);
        BuildMarkings(root);
        BuildHiveStructure(root);
        for (int i = 0; i < 4; i++) BuildFlower(root, i);

        if (stageElements) StageElements(world);
    }

    static void BuildFloorAndWalls(Transform root)
    {
        // Venue floor (catches elements that leave the FIELD).
        var floor = Util.Box(root, "VenueFloor", new Vector3(0, -0.5f - 0.02f, 0), new Vector3(14f, 1f, 12f), new Color(0.12f, 0.12f, 0.13f), true);
        floor.layer = Layers.Field;
        floor.GetComponent<Collider>().sharedMaterial = Util.FieldMat;
        var tileCol = Util.ColBox(root, "TileSurface", new Vector3(0, -0.25f, 0), new Vector3(Dims.FieldIn * Dims.IN, 0.5f, Dims.FieldIn * Dims.IN), null, Util.FieldMat);
        tileCol.layer = Layers.Field;

        // 36 TILES, 24 x 24 x 0.59 in (9.2).
        float t = 0.59f * Dims.IN;
        for (int ix = 0; ix < 6; ix++)
        for (int iz = 0; iz < 6; iz++)
        {
            var c = (ix + iz) % 2 == 0 ? TileA : TileB;
            var pos = new Vector3((-60f + 24f * ix) * Dims.IN, -t * 0.5f + 0.0005f, (-60f + 24f * iz) * Dims.IN);
            Util.Box(root, $"Tile_{(char)('A' + ix)}{iz + 1}", pos, new Vector3(23.85f * Dims.IN, t, 23.85f * Dims.IN), c, false);
        }

        // Perimeter (inside surface at +/-72 in).
        float h = Dims.WallHeight, w = Dims.WallThickness, L = Dims.FieldIn * Dims.IN + 2 * w;
        float o = Dims.Half + w * 0.5f;
        var walls = new[]
        {
            (new Vector3(0, h / 2, o), new Vector3(L, h, w)),
            (new Vector3(0, h / 2, -o), new Vector3(L, h, w)),
            (new Vector3(o, h / 2, 0), new Vector3(w, h, L)),
            (new Vector3(-o, h / 2, 0), new Vector3(w, h, L)),
        };
        foreach (var (p, s) in walls)
        {
            var wall = Util.Box(root, "Perimeter", p, s, WallC, true);
            wall.layer = Layers.Field;
            wall.GetComponent<Collider>().sharedMaterial = Util.FieldMat;
            Util.Box(wall.transform.parent, "PerimeterCap", p + Vector3.up * (h * 0.5f), new Vector3(s.x + 0.001f, 0.01f, s.z + 0.001f), new Color(0.5f, 0.5f, 0.52f), false);
        }
    }

    static void Tape(Transform root, float x0, float x1, float z0, float z1, Color c, float y = 0.0015f)
    {
        var p = new Vector3((x0 + x1) * 0.5f, y, (z0 + z1) * 0.5f) * 1f;
        p.x *= Dims.IN; p.z *= Dims.IN;
        Util.Box(root, "Tape", p, new Vector3(Mathf.Abs(x1 - x0) * Dims.IN, 0.002f, Mathf.Abs(z1 - z0) * Dims.IN), c, false);
    }

    static void BuildMarkings(Transform root)
    {
        var red = Alliance.Red.Col();
        var blue = Alliance.Blue.Col();
        float tp = 1f;

        // LOADING ZONES (Figure 9-3): U of tape against the ALLIANCE-side wall.
        Tape(root, -72, -61, 24.5f - tp, 24.5f, red);
        Tape(root, -72, -61, 47.5f, 47.5f + tp, red);
        Tape(root, -61 - tp, -61, 24.5f - tp, 47.5f + tp, red);
        Tape(root, 72, 61, -24.5f + tp, -24.5f, blue);
        Tape(root, 72, 61, -47.5f, -47.5f - tp, blue);
        Tape(root, 61 + tp, 61, -24.5f + tp, -47.5f - tp, blue);

        // GARDENS (Figure 9-3): two 1 in strips = 2 in deep, 23 in long, opposite corners.
        Tape(root, -72, -49, -72, -70, red);
        Tape(root, 72, 49, 72, 70, blue);

        // ALLIANCE AREAS (9.3): 97 x 54 in outside the FIELD, outlined in tape.
        float ha = 48.5f, d = 54f;
        foreach (var a in new[] { Alliance.Red, Alliance.Blue })
        {
            float s = a.Sign();
            float x0 = s * 73f, x1 = s * (73f + d);
            Color c = a.Col();
            Tape(root, x0, x1, ha, ha + 2, c);
            Tape(root, x0, x1, -ha, -ha - 2, c);
            Tape(root, x1, x1 + 2 * s, -ha - 2, ha + 2, c);
            Util.Box(root, "AllianceArea", new Vector3(s * (73f + d / 2) * Dims.IN, 0.001f, 0), new Vector3(d * Dims.IN, 0.001f, 97f * Dims.IN), new Color(c.r * 0.35f, c.g * 0.35f, c.b * 0.35f), false);
            // Driver station stand
            Util.Box(root, "OperatorTable", new Vector3(s * 80f * Dims.IN, 0.35f, 0), new Vector3(0.4f, 0.7f, 1.8f), new Color(0.2f, 0.2f, 0.22f), false);
        }
    }

    // 9.6 HIVE Structure ---------------------------------------------------------
    static void BuildHiveStructure(Transform root)
    {
        var hs = new GameObject("HIVE Structure").transform;
        hs.SetParent(root, false);

        float W = Dims.FrameWidth * 0.5f, D = Dims.FrameDepth * 0.5f, P = Dims.PivotHeight;
        float bar = 1.5f * Dims.IN;
        foreach (float sx in new[] { -1f, 1f })
        {
            float x = sx * W;
            // triangle legs
            foreach (float sz in new[] { -1f, 1f })
            {
                Vector3 a = new Vector3(x, 0, sz * D), b = new Vector3(x, P + bar, 0);
                Vector3 mid = (a + b) * 0.5f, dir = b - a;
                var leg = Util.Box(hs, "FrameLeg", mid, new Vector3(bar, bar, dir.magnitude), FrameC, true, Quaternion.LookRotation(dir));
                leg.layer = Layers.Field;
            }
            var rail = Util.Box(hs, "FrameBase", new Vector3(x, bar * 0.5f, 0), new Vector3(bar, bar, Dims.FrameDepth), new Color(0.35f, 0.35f, 0.37f), true);
            rail.layer = Layers.Field;
            Util.Box(hs, "FrameApex", new Vector3(x, P + bar, 0), new Vector3(bar * 1.6f, bar * 2f, bar * 2f), new Color(0.1f, 0.1f, 0.1f), false);
        }
        // under-TILE strips (visual only)
        foreach (float sz in new[] { -1f, 1f })
            Util.Box(hs, "UnderTileStrip", new Vector3(0, 0.0012f, sz * D), new Vector3(Dims.FrameWidth, 0.001f, bar), new Color(0.3f, 0.3f, 0.3f), false);
        Util.Box(hs, "Crossbar", new Vector3(0, P + bar * 1.5f, 0), new Vector3(Dims.FrameWidth, bar, bar), FrameC, true).layer = Layers.Field;
        Util.Box(hs, "BIOBUZZ Panel", new Vector3(0, P + 5.5f * Dims.IN, 0), new Vector3(30f * Dims.IN, 6f * Dims.IN, 0.25f * Dims.IN), new Color(0.98f, 0.86f, 0.25f), false);
        for (int i = 0; i < 7; i++)   // honeycomb accents on the panel
            Util.Cyl(hs, "Hex", new Vector3((-12f + 4f * i) * Dims.IN, P + 5.5f * Dims.IN, 0), 2.2f * Dims.IN, 0.35f * Dims.IN, new Color(0.15f, 0.12f, 0.05f), Vector3.forward);

        // Red HIVE on the red (-X) side, CELL toward the audience up (Figure 10-2).
        RedHive = BuildHive(hs, Alliance.Red, -Dims.HiveCenterToCenter * 0.5f, -1);
        BlueHive = BuildHive(hs, Alliance.Blue, Dims.HiveCenterToCenter * 0.5f, +1);
    }

    static Hive BuildHive(Transform parent, Alliance a, float x, int startUpEnd)
    {
        var go = new GameObject($"HIVE_{a}");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(x, Dims.PivotHeight, 0);
        go.layer = Layers.Hive;
        var tr = go.transform;

        float d = Dims.ArmOffset;
        float th = 0.25f * Dims.IN;
        float hw = Dims.CellWidth * 0.5f;
        float rect = Dims.CellRectHeight, apex = Dims.CellHeight;
        float uc = (Dims.CellInner + Dims.CellOuter) * 0.5f;
        Color edge = a.Col();

        // pivot hub, arm and dampers
        Util.Cyl(tr, "Pivot", Vector3.zero, 1.2f * Dims.IN, 3f * Dims.IN, new Color(0.15f, 0.15f, 0.15f), Vector3.right);
        Util.Box(tr, "Arm", new Vector3(0, -d - 0.6f * Dims.IN, 0), new Vector3(1.5f * Dims.IN, 0.75f * Dims.IN, Dims.HiveLength - 1f * Dims.IN), new Color(0.7f, 0.7f, 0.72f), false);
        foreach (int s in new[] { -1, 1 })
            Util.Box(tr, "Damper", new Vector3(0, -d - 1.6f * Dims.IN, s * 3.2f * Dims.IN), Vector3.one * 1.0f * Dims.IN, new Color(0.1f, 0.1f, 0.1f), false);

        foreach (int s in new[] { -1, 1 })
        {
            float zc = s * uc;
            // floor (base) of the CELL — carries the AprilTag Cluster underneath (9.9)
            Util.Box(tr, "CellBase", new Vector3(0, -d - th * 0.5f, zc), new Vector3(Dims.CellWidth, th, Dims.CellDepth), Panel, true, null, true);
            // straight sides
            foreach (int sx in new[] { -1, 1 })
                Util.Box(tr, "CellSide", new Vector3(sx * (hw + th * 0.5f), -d + rect * 0.5f, zc), new Vector3(th, rect, Dims.CellDepth), Panel, true, null, true);
            // roof
            foreach (int sx in new[] { -1, 1 })
            {
                Vector2 p0 = new Vector2(sx * hw, rect), p1 = new Vector2(0, apex);
                Vector2 dir = p1 - p0;
                float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                Vector2 mid = (p0 + p1) * 0.5f;
                Vector2 nrm = new Vector2(-dir.y, dir.x).normalized * (th * 0.5f);
                if (Vector2.Dot(nrm, mid - new Vector2(0, rect)) < 0) nrm = -nrm;
                Util.Box(tr, "CellRoof", new Vector3(mid.x + nrm.x, -d + mid.y + nrm.y, zc), new Vector3(dir.magnitude + th, th, Dims.CellDepth), Panel, true, Quaternion.Euler(0, 0, ang), true);
            }
            // closed pentagon wall on the pivot side; the outer pentagon is the opening.
            var pent = new[]
            {
                new Vector2(-hw, -d), new Vector2(-hw, -d + rect), new Vector2(0, -d + apex), new Vector2(hw, -d + rect), new Vector2(hw, -d)
            };
            Util.Prism(tr, "CellBack", pent, th, new Vector3(0, 0, s * (Dims.CellInner - th * 0.5f)), Quaternion.identity, Panel, true, true);
            PentagonOutline(tr, s * Dims.CellInner, d, edge);
            PentagonOutline(tr, s * Dims.CellOuter, d, edge);

            // AprilTag Cluster: 4 tags on the bottom face (Figure 9-15/9-16).
            for (int i = 0; i < 4; i++)
            {
                float tx = (i - 1.5f) * 4.2f * Dims.IN;
                Util.Box(tr, "AprilTag", new Vector3(tx, -d - th - 0.002f, zc), new Vector3(Dims.AprilTag, 0.002f, Dims.AprilTag), Color.white, false);
                Util.Box(tr, "AprilTagInner", new Vector3(tx, -d - th - 0.004f, zc), new Vector3(Dims.AprilTag * 0.7f, 0.002f, Dims.AprilTag * 0.7f), Color.black, false);
            }
        }
        foreach (var c in go.GetComponentsInChildren<Collider>()) c.gameObject.layer = Layers.Hive;

        var hive = go.AddComponent<Hive>();
        hive.Init(a, startUpEnd);
        return hive;
    }

    static void PentagonOutline(Transform tr, float z, float d, Color c)
    {
        float hw = Dims.CellWidth * 0.5f, rect = Dims.CellRectHeight, apex = Dims.CellHeight;
        var pts = new[] { new Vector2(-hw, 0), new Vector2(-hw, rect), new Vector2(0, apex), new Vector2(hw, rect), new Vector2(hw, 0) };
        float e = 0.6f * Dims.IN;
        for (int i = 0; i < pts.Length; i++)
        {
            Vector2 a = pts[i], b = pts[(i + 1) % pts.Length];
            Vector2 mid = (a + b) * 0.5f, dir = b - a;
            float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            Util.Box(tr, "CellFrame", new Vector3(mid.x, -d + mid.y, z), new Vector3(dir.magnitude + e, e, e), c, false, Quaternion.Euler(0, 0, ang));
        }
    }

    // 9.7 FLOWER (Figure 9-12) ---------------------------------------------------
    public static Vector3 FlowerPos(int i, out Vector3 inward)
    {
        float wallIn = 72f - Dims.FlowerFromWall / Dims.IN;
        float along = Dims.FlowerAlongWall / Dims.IN;
        switch (i)
        {
            case 0: inward = Vector3.back; return Dims.V(-along, 0, wallIn);     // rear wall, red half
            case 1: inward = Vector3.left; return Dims.V(wallIn, 0, along);      // blue wall
            case 2: inward = Vector3.forward; return Dims.V(along, 0, -wallIn);  // audience wall, blue half
            default: inward = Vector3.right; return Dims.V(-wallIn, 0, -along);  // red wall
        }
    }

    static void BuildFlower(Transform root, int i)
    {
        var pos = FlowerPos(i, out var inward);
        var go = new GameObject($"FLOWER_{i + 1}");
        go.transform.SetParent(root, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.LookRotation(inward);
        var tr = go.transform;
        go.layer = Layers.Field;

        Color black = new Color(0.08f, 0.08f, 0.08f), pipe = new Color(0.45f, 0.75f, 0.2f), gold = new Color(0.85f, 0.62f, 0.25f), purple = new Color(0.55f, 0.3f, 0.7f);
        float IN = Dims.IN;

        Util.Ring(tr, "LowerRing", new Vector3(0, Dims.LowerRingThick * 0.5f, 0), Dims.LowerRingHoleDia * 0.5f, 1.1f * IN, Dims.LowerRingThick, black, 24);
        float extH = Dims.MiddleRingBottom - Dims.LowerRingThick;
        Util.Box(tr, "SquareExtrusion", new Vector3(0, Dims.LowerRingThick + extH * 0.5f, -Dims.FlowerRingCenterFromBack), new Vector3(1f * IN, extH, 1f * IN), new Color(0.7f, 0.7f, 0.7f), true);
        Util.Ring(tr, "MiddleRing", new Vector3(0, Dims.MiddleRingBottom + Dims.MiddleRingThick * 0.5f, 0), Dims.MiddleRingHoleDia * 0.5f, 1.0f * IN, Dims.MiddleRingThick, black, 24);

        float pipeBottom = Dims.MiddleRingTop, pipeTop = Dims.FlowerTopHeight - 0.5f * IN;
        for (int k = 0; k < 4; k++)
        {
            var q = Quaternion.Euler(0, 45 + 90 * k, 0);
            var p = q * new Vector3(0, 0, 2.6f * IN);
            p.y = (pipeBottom + pipeTop) * 0.5f;
            Util.Cyl(tr, "HIPS Pipe", p, 1.0f * IN, pipeTop - pipeBottom, pipe, Vector3.up, true);
        }
        Util.Ring(tr, "TopRing", new Vector3(0, Dims.FlowerTopHeight - 0.25f * IN, 0), Dims.FlowerTopDia * 0.5f, 1.2f * IN, 0.5f * IN, gold, 24);
        Util.Ring(tr, "Backstop", new Vector3(0, Dims.FlowerTopHeight + Dims.FlowerBackstop * 0.5f, 0), 2.3f * IN, 0.3f * IN, Dims.FlowerBackstop, purple, 24, 110, 250);
        Util.Box(tr, "WallMount", new Vector3(0, Dims.FlowerTopHeight - 0.5f * IN, -(Dims.FlowerFromWall - 0.6f * IN)), new Vector3(4f * IN, 1f * IN, 1.2f * IN), gold, true);

        foreach (var c in go.GetComponentsInChildren<Collider>()) { c.gameObject.layer = Layers.Field; c.sharedMaterial = Util.FieldMat; }
        var f = go.AddComponent<Flower>();
        f.index = i;
        f.inward = inward;
    }

    // 10.3.1 SCORING ELEMENT staging (except ROBOT pre-loads and ALLIANCE AREA NECTAR).
    static void StageElements(Transform world)
    {
        float IN = Dims.IN;
        // 4 POLLEN in each FLOWER, stacked from the lower ring.
        foreach (var f in Flower.All)
            for (int k = 0; k < 4; k++)
                GameElement.Create(world, ElementType.Pollen, Alliance.None, f.transform.position + Vector3.up * ((1.42f + 2.82f * k) * IN));

        // 4 POLLEN in each GARDEN, in a line from the corner nearest the ALLIANCE AREA.
        for (int k = 0; k < 4; k++)
        {
            GameElement.Create(world, ElementType.Pollen, Alliance.None, Dims.V(-72f + 1.45f + 2.85f * k, 1.45f, -72f + 1.45f));
            GameElement.Create(world, ElementType.Pollen, Alliance.None, Dims.V(72f - 1.45f - 2.85f * k, 1.45f, 72f - 1.45f));
        }

        // 3 NECTAR in each upward CELL, against the back wall, lined up from the side
        // closest to the ALLIANCE AREA of that colour.
        foreach (var h in new[] { RedHive, BlueHive })
        {
            float side = h.alliance.Sign();
            for (int k = 0; k < 3; k++)
            {
                float lx = side * (8.0f - 3.7f * k) * IN;
                var local = new Vector3(lx, -Dims.ArmOffset + 2.0f * IN, h.upEnd * (Dims.CellInner + 2.0f * IN));
                GameElement.Create(world, ElementType.Nectar, h.alliance, h.transform.TransformPoint(local));
            }
        }
    }
}
