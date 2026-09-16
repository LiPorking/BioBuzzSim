using UnityEngine;

// All geometry comes from the BIOBUZZ presented by RTX Competition Manual V1
// (FIRST Tech Challenge 2026-2027). Section / figure is noted next to each value.
// Values are authored in inches (as in the manual) and converted to metres.
public static class Dims
{
    public const float IN = 0.0254f;

    // ---- 9.2 FIELD --------------------------------------------------------
    // "approximately 144 in. by 144 in. ... 36 interlocking soft foam TILES ... 24 in. by 24 in."
    public const float FieldIn = 144f;
    public const float Half = 72f * IN;
    public const float Tile = 24f * IN;
    // Perimeter wall height is NOT given in the manual (it references the AndyMark
    // am-0481 perimeter kit). 12 in is that kit's nominal height.
    public const float WallHeight = 12f * IN;
    public const float WallThickness = 1f * IN;

    // ---- 9.3 Areas, Zones & Markings -------------------------------------
    public const float AllianceAreaWidth = 97f * IN;   // "approximately 97 in. wide"
    public const float AllianceAreaDepth = 54f * IN;   // "by 54 in. deep"
    public const float LoadingZoneWidth = 23f * IN;    // "23 in. wide"
    public const float LoadingZoneDepth = 11f * IN;    // "by 11 in. deep"
    public const float GardenLength = 23f * IN;        // "23 in."
    public const float GardenDepth = 2f * IN;          // "by 2 in. wide"
    public const float Tape = 1f * IN;                 // 1 in. gaffer tape

    // ---- 9.6 HIVE Structure ------------------------------------------------
    public const float FrameWidth = 49.46f * IN;       // 9.6.1 (along the crossbar, field X)
    public const float FrameDepth = 38.95f * IN;       // 9.6.1 (triangle base, field Z)
    public const float PivotHeight = 43.95f * IN;      // 9.6.1 pivot axis above TILES
    public const float CellGap = 18.84f * IN;          // Fig 9-9 inner distance between CELLS
    public const float CellDepth = 12.04f * IN;        // Fig 9-9 / 9.6.2 "12 in. deep"
    public const float HiveLength = 42.91f * IN;       // Fig 9-9 overall
    public const float CellWidth = 20f * IN;           // 9.6.2 opening "20 in. wide"
    public const float CellHeight = 14f * IN;          // 9.6.2 opening "14 in tall"
    public const float CellRectHeight = 7.61f * IN;    // Fig 9-11 straight side of the pentagon
    public const float HiveCenterToCenter = 25.5f * IN;// Fig 9-10
    public const float HiveTilt = 30f;                 // Fig 9-10 (degrees)
    public const float HiveOpeningTop = 65.6f * IN;    // Fig 9-10
    public const float HiveOpeningBottom = 53.5f * IN; // Fig 9-10
    // Distance of the CELL mounting line below the pivot. Derived so that a 30 deg tilt
    // reproduces Fig 9-10's 53.5 in / 65.6 in opening heights exactly.
    public const float ArmOffset = 1.36f * IN;
    public static float CellInner => CellGap * 0.5f;              // 9.42 in from pivot
    public static float CellOuter => CellGap * 0.5f + CellDepth;  // 21.46 in from pivot

    // ---- 9.7 FLOWER --------------------------------------------------------
    public const float FlowerTopDia = 4.0f * IN;       // "opening ... approximately 4 in."
    public const float FlowerTopHeight = 21.5f * IN;   // "approximately 21.5 in. above the TILES"
    public const float FlowerBackstop = 1.25f * IN;    // backstop height
    public const float RetrievalHeight = 3.55f * IN;   // Retrieval Opening height
    public const float RetrievalDepth = 3.57f * IN;    // Retrieval Opening depth
    public const float LowerRingThick = 0.43f * IN;    // Fig 9-12 bottom ring thickness
    public const float LowerRingHoleDia = 2.79f * IN;  // "hole ... approximately 2.79 in."
    public const float FlowerRingCenterFromBack = 2.40f * IN; // Fig 9-12
    // Middle ring: the manual says POLLEN (2.8 in) can be removed from the bottom but
    // NECTAR (3.6 in) cannot, so its opening lies between the two. 3.2 in is used.
    public const float MiddleRingHoleDia = 3.2f * IN;
    public const float MiddleRingThick = 0.75f * IN;
    public static float MiddleRingBottom => LowerRingThick + RetrievalHeight;
    public static float MiddleRingTop => MiddleRingBottom + MiddleRingThick;
    // Figure 9-2: FLOWER centres sit on the 48 in tile seam from each corner.
    public const float FlowerAlongWall = 24f * IN;     // offset from wall centre line
    public const float FlowerFromWall = 3.0f * IN;

    // ---- 9.8 SCORING ELEMENTS ------------------------------------------------
    public const float PollenDia = 2.8f * IN;          // "approximately 2.8 in."
    public const float NectarDia = 3.6f * IN;          // "approximately 3.6 in."
    public const int PollenTotal = 40;
    public const int NectarPerAlliance = 8;

    // ---- 9.9 AprilTags -----------------------------------------------------
    public const float AprilTag = 3.25f * IN;

    // ---- 12 ROBOT (R102/R105) ------------------------------------------------
    public const float StartCube = 18f * IN;

    // ---- 10.4 / Table 9-1 MATCH timing --------------------------------------
    public const float AutoTime = 30f;
    public const float TransitionTime = 8f;
    public const float TeleopTime = 120f;
    public const float FlowerUnlock = 60f;   // G410 / G426

    // ---- Table 10-2 point values ---------------------------------------------
    public const int PtsLeave = 3;
    public const int PtsPark = 5;
    public const int PtsTip = 20;
    public const int PtsCell = 2;
    public const int PtsBottomNectar = 5;
    public const int PtsOwnedFlower = 2;
    public const int PtsGarden = 1;
    public const int PtsMinor = 5;   // Table 10-4
    public const int PtsMajor = 20;
    // Table 10-3 RP thresholds ("All Other Events")
    public const int SwarmThreshold = 16;
    public const int Pollinator1Tips = 4;
    public const int Pollinator2Tips = 7;

    public static Vector3 V(float xIn, float yIn, float zIn) => new Vector3(xIn * IN, yIn * IN, zIn * IN);
}

public enum Alliance { Red, Blue, None }
public enum ElementType { Pollen, Nectar }

public static class AllianceExt
{
    public static Alliance Other(this Alliance a) => a == Alliance.Red ? Alliance.Blue : a == Alliance.Blue ? Alliance.Red : Alliance.None;
    public static Color Col(this Alliance a) => a == Alliance.Red ? new Color(0.85f, 0.1f, 0.1f) : a == Alliance.Blue ? new Color(0.1f, 0.3f, 0.95f) : Color.gray;
    // Red side of the field is -X (left from the audience, 9.5). Blue is +X.
    public static float Sign(this Alliance a) => a == Alliance.Red ? -1f : 1f;
}
