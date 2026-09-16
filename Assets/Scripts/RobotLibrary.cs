using System;
using System.Collections.Generic;
using UnityEngine;

public enum DriveType { Tank, Mecanum }
public enum LauncherType { None, Flywheel, Catapult }
public enum FlowerMechType { None, Intake, Side }

// A robot definition. Dimensions in inches in robot space: +X right, +Y up, +Z forward
// (intake side), origin on the floor at the footprint centre.
public class RobotSpec
{
    public string name, maker, source;
    public string drivetrain, intake, storage, launcherDesc, flowerDesc, notes;
    public float lengthIn, widthIn, heightIn;
    public float massKg = 11f;

    public DriveType drive;
    public float maxSpeed;          // m/s, from motor ratio x wheel size in the BOM
    public float accel = 5f;        // m/s^2
    public float maxTurnDeg = 270f; // deg/s

    public LauncherType launcher;
    public float launchAngle = 55f; // degrees above horizontal
    public float launchMin = 3f, launchMax = 9f, launchFixed = 6f;
    public float feedInterval = 0.25f;
    public float spreadDeg = 1f, speedSpread = 0.015f;
    public Vector3 launchPoint;       // turret robots: relative to the turret pivot, turret facing forward
    public bool shootsBackward;
    public int barrels = 1;           // twin cannons fire two elements per volley
    public float barrelSpacing = 6f;
    public bool turret, turretAlwaysTracks, shootOnMove;
    public Vector3 turretPivot;
    public float turretRange = 180f;  // +/- degrees from forward
    public float turretSpeed = 540f;  // deg/s
    public float hoodMin, hoodMax;    // adjustable hood range (degrees); equal = fixed launchAngle
    public bool concept;              // not a published build: inspired design

    public Vector3 intakeCenter, intakeSize;
    public FlowerMechType flowerMech;
    public Vector3 flowerMechPoint;
    public float flowerMechRadius = 2.5f;

    public Vector3[] slots;
    public float chassisTopIn = 5f;
    public Vector3 upperCenter, upperSize; // secondary collider for the mechanism tower

    public Action<RobotParts, RobotSpec> build;
}

// Handles the builder collects while making visuals (for animation).
public class RobotParts
{
    public Transform root;
    public Alliance alliance;
    public readonly List<Transform> intakeSpinners = new List<Transform>();
    public readonly List<Transform> launcherSpinners = new List<Transform>();
    public readonly List<Transform> wheelSpinners = new List<Transform>();
    public Transform catapultArm, flowerMech, turret, hood;
    public Vector3 flowerMechRest, flowerMechActive;
}

public static class RobotLibrary
{
    public static readonly List<RobotSpec> All = new List<RobotSpec>();
    const float IN = Dims.IN;

    static readonly Color Alu = new Color(0.72f, 0.73f, 0.75f);
    static readonly Color DarkAlu = new Color(0.35f, 0.36f, 0.38f);
    static readonly Color Black = new Color(0.07f, 0.07f, 0.08f);
    static readonly Color Yellow = new Color(0.98f, 0.78f, 0.1f);
    static readonly Color Poly = new Color(0.85f, 0.9f, 0.95f, 0.28f);
    static readonly Color StudicaBlue = new Color(0.1f, 0.3f, 0.75f);
    static readonly Color RevGrey = new Color(0.25f, 0.25f, 0.27f);
    static readonly Color Green = new Color(0.2f, 0.85f, 0.25f);

    static RobotLibrary()
    {
        All.Add(GoBildaStarterBot(false));
        All.Add(RevDuoStarterBot());
        All.Add(RobitsStarterBot(false));
        All.Add(StudicaStarterBot());
        // Additional robots with open CAD / instructions
        // FTC-sized concept robots inspired by FRC 2026 REBUILT designs
        All.Add(TurretBot());
        All.Add(TwinCannonBot());
        // every robot drives with mecanum wheels
        foreach (var s in All)
        {
            if (s.drive != DriveType.Mecanum) s.maxSpeed *= 0.95f;
            s.drive = DriveType.Mecanum;
        }
    }

    // ---------------------------------------------------------------- helpers
    static Vector3 V(float x, float y, float z) => new Vector3(x, y, z) * IN;
    static GameObject B(RobotParts p, string n, float x, float y, float z, float sx, float sy, float sz, Color c, Quaternion? r = null, bool t = false)
        => Util.Box(p.root, n, V(x, y, z), V(sx, sy, sz), c, false, r, t);
    static GameObject Wheel(RobotParts p, string n, float x, float y, float z, float dia, float width, Color tire, Color hub)
    {
        if (!n.Contains("Flywheel"))
        {
            if (Mathf.Abs(z) < 0.01f) return new GameObject("unusedMiddleWheel");
            return Mecanum(p, x, y, z, dia, width);
        }
        var w = Util.Cyl(p.root, n, V(x, y, z), dia * IN, width * IN, tire, Vector3.right);
        Util.Cyl(w.transform, "hub", new Vector3(0, 0.51f, 0), 0.5f, 0.02f, hub, Vector3.up);
        Util.Box(w.transform, "spoke", new Vector3(0, 0.52f, 0), new Vector3(0.9f, 0.01f, 0.12f), hub, false);
        p.wheelSpinners.Add(w.transform);
        return w;
    }
    static GameObject Mecanum(RobotParts p, float x, float y, float z, float dia, float width)
    {
        var w = Util.Cyl(p.root, "MecanumWheel", V(x, y, z), dia * IN, width * IN, Black, Vector3.right);
        for (int i = 0; i < 10; i++)
        {
            // rollers around the rim (drawn straight; 45 deg tilt is implied by the colour stripes)
            var roller = Util.Box(w.transform, "roller", Quaternion.Euler(0, i * 36f, 0) * new Vector3(0, 0, 0.46f), new Vector3(0.16f, 0.95f, 0.12f), DarkAlu, false);
            roller.transform.localRotation = Quaternion.Euler(0, i * 36f, 0);
        }
        p.wheelSpinners.Add(w.transform);
        return w;
    }
    static void Channel(RobotParts p, float x, float y, float z, float sx, float sy, float sz, Color c)
    {
        B(p, "Channel", x, y, z, sx, sy, sz, c);
        // hole pattern hint
        float len = Mathf.Max(sx, sz);
        int holes = Mathf.Max(1, Mathf.RoundToInt(len / 0.95f));
        bool alongZ = sz >= sx;
        for (int i = 0; i < holes && sy >= 1.2f; i++)
        {
            float o = -len / 2 + (i + 0.5f) * len / holes;
            float side = alongZ ? (x >= 0 ? 1 : -1) : (z >= 0 ? 1 : -1);
            if (alongZ) B(p, "hole", x + side * (sx / 2 + 0.005f), y, z + o, 0.02f, 0.35f, 0.35f, Black);
            else B(p, "hole", x + o, y, z + side * (sz / 2 + 0.005f), 0.35f, 0.35f, 0.02f, Black);
        }
    }
    static GameObject Roller(RobotParts p, string n, float x, float y, float z, float dia, float len, Color c, int fins, Color finColor, float finLen)
    {
        var r = Util.Cyl(p.root, n, V(x, y, z), dia * IN, len * IN, c, Vector3.right);
        for (int i = 0; i < fins; i++)
        {
            float a = i * 360f / fins;
            var fin = Util.Box(r.transform, "fin", Quaternion.Euler(0, a, 0) * new Vector3(0, 0, 0.5f + finLen / dia * 0.5f),
                new Vector3(0.12f, 1.9f, finLen / dia), finColor, false);
            fin.transform.localRotation = Quaternion.Euler(0, a, 0);
        }
        p.intakeSpinners.Add(r.transform);
        return r;
    }
    static void SignPlates(RobotParts p, float halfWidth, float y, float len, float h)
    {
        // ROBOT SIGNS in ALLIANCE colour (R402 / G303.D)
        foreach (int s in new[] { -1, 1 })
            B(p, "RobotSign", s * (halfWidth + 0.06f), y, 0, 0.1f, h, len, p.alliance.Col());
    }

    // ------------------------------------------------------------ goBILDA
    // Source: goBILDA FTC StarterBot Resource Guide (2026-2027), assembly instructions
    // 3200-2627-0003 (37 pages) and the StarterBot Base instructions (Steps 1-40).
    static RobotSpec GoBildaStarterBot(bool mecanum)
    {
        var s = new RobotSpec
        {
            name = mecanum ? "goBILDA StarterBot (Mecanum)" : "goBILDA StarterBot",
            maker = "goBILDA",
            source = mecanum ? "gobilda.com - FTC StarterBot with Mecanum Wheels Resource Guide (2026-2027)"
                             : "gobilda.com/ftc-starter-bot-resource-guide-2026-2027-season (assembly instructions 3200-2627-0003, STEP file)",
            drivetrain = mecanum ? "4x mecanum wheels, 312 RPM Yellow Jacket motors" : "Drop-center 6WD: 4x 96mm Hogback + 2x 96mm Omni, 2x 19.2:1 Yellow Jacket, 30T:30T gears + 10T sprockets",
            intake = "Front roller of 48mm GripForce Gecko wheels + two servo-driven 72mm Gecko corner wheels",
            storage = "Polycarbonate gridplate ramp, carries up to 4 POLLEN",
            launcherDesc = "Flywheel: 96mm Hogback wheel on a 1:1 Yellow Jacket, Windmill servo feed, gridplate hood",
            flowerDesc = "Pulls POLLEN out of the FLOWER bottom directly into the intake",
            notes = "Launch angle/exit point estimated from the instruction renders.",
            lengthIn = 17.2f, widthIn = 17.1f, heightIn = 17.5f, massKg = 11.5f,
            drive = mecanum ? DriveType.Mecanum : DriveType.Tank,
            maxSpeed = mecanum ? 1.5f : 1.57f,   // 312 rpm x 96 mm wheel
            maxTurnDeg = 280f,
            launcher = LauncherType.Flywheel,
            launchAngle = 58f, launchMin = 2.5f, launchMax = 9f, feedInterval = 0.22f,
            spreadDeg = 0.9f, speedSpread = 0.012f,
            launchPoint = new Vector3(0, 14.5f, -7.6f),
            shootsBackward = true,
            intakeCenter = new Vector3(0, 1.4f, 8.8f), intakeSize = new Vector3(12.5f, 3.0f, 3.4f),
            flowerMech = FlowerMechType.Intake,
            flowerMechPoint = new Vector3(0, 1.4f, 9.4f), flowerMechRadius = 2.4f,
            slots = new[] { new Vector3(0, 3.4f, 4.6f), new Vector3(0, 4.9f, 1.8f), new Vector3(0, 6.5f, -0.9f), new Vector3(0, 8.3f, -3.4f) },
            chassisTopIn = 5.2f,
            upperCenter = new Vector3(0, 11f, -3.5f), upperSize = new Vector3(10f, 11f, 8f),
        };
        s.build = (p, spec) =>
        {
            Channel(p, -6.6f, 2.5f, 0, 1.9f, 1.9f, 15.1f, Alu);
            Channel(p, 6.6f, 2.5f, 0, 1.9f, 1.9f, 15.1f, Alu);
            Channel(p, 0, 2.5f, -6.6f, 11.3f, 1.9f, 1.9f, Alu);
            Channel(p, 0, 4.4f, 6.8f, 13.2f, 1.9f, 1.9f, Alu);
            if (mecanum)
            {
                foreach (float x in new[] { -8.2f, 8.2f })
                foreach (float z in new[] { -5.6f, 5.6f })
                    Mecanum(p, x, 2.05f, z, 4.09f, 1.5f);
            }
            else
            {
                foreach (float x in new[] { -8.1f, 8.1f })
                {
                    Wheel(p, "HogbackWheel", x, 1.9f, -5.9f, 3.78f, 1.4f, Black, Yellow);
                    Wheel(p, "HogbackWheel", x, 1.9f, 0f, 3.78f, 1.4f, Black, Yellow);
                    Wheel(p, "OmniWheel", x, 1.9f, 5.9f, 3.78f, 1.4f, Black, DarkAlu);
                }
            }
            foreach (float x in new[] { -3.2f, 3.2f })
                Util.Cyl(p.root, "YellowJacket", V(x, 2.5f, -3f), 1.45f * IN, 4.6f * IN, Yellow, Vector3.right);
            B(p, "Battery", -3.4f, 3.6f, 2.2f, 2.3f, 2.9f, 4.4f, Black);
            B(p, "ControlHub", 3.4f, 3.0f, 2.0f, 4.0f, 0.9f, 4.4f, Black);
            // front intake: 7x 48mm Gecko wheels on a 240mm shaft
            Roller(p, "GeckoIntake", 0, 1.35f, 8.4f, 1.9f, 11.2f, Black, 7, Black, 0.35f);
            foreach (int sx in new[] { -1, 1 })
            {
                B(p, "IntakePlate", sx * 6.1f, 3.0f, 8.2f, 0.25f, 4.2f, 3.6f, Alu);
                var corner = Util.Cyl(p.root, "CornerGecko72", V(sx * 7.6f, 0.95f, 9.0f), 2.83f * IN, 1.1f * IN, Black, Vector3.up);
                p.intakeSpinners.Add(corner.transform);
                B(p, "SpeedServo", sx * 7.6f, 2.4f, 9.0f, 1.6f, 1.5f, 0.8f, Black);
            }
            // gridplate ramp to the shooter
            B(p, "GridplateRamp", 0, 5.5f, 1.0f, 8.4f, 0.15f, 11.5f, Poly, Quaternion.Euler(-24, 0, 0), true);
            foreach (int sx in new[] { -1, 1 })
            {
                B(p, "GridplateWall", sx * 4.3f, 7.0f, 0.5f, 0.15f, 5.0f, 10f, Poly, null, true);
                Channel(p, sx * 5.0f, 9.6f, -4.3f, 1.9f, 9.4f, 1.9f, Alu);
            }
            Channel(p, 0, 14.4f, -4.3f, 11.9f, 1.9f, 1.9f, Alu);
            var fly = Wheel(p, "FlywheelHogback", 0, 12.2f, -5.6f, 3.78f, 1.4f, Black, Yellow);
            p.wheelSpinners.Remove(fly.transform);
            p.launcherSpinners.Add(fly.transform);
            Util.Cyl(p.root, "FlywheelMotor_1to1", V(2.9f, 12.2f, -5.6f), 1.45f * IN, 4.0f * IN, Yellow, Vector3.right);
            B(p, "WindmillServo", -2.6f, 9.4f, -2.8f, 1.6f, 1.6f, 0.9f, Black);
            // Large Gridplate H hood
            B(p, "GridplateHood", 0, 15.9f, -5.2f, 9.4f, 0.15f, 7.8f, Poly, Quaternion.Euler(38, 0, 0), true);
            SignPlates(p, 6.6f + 0.95f, 5.3f, 9f, 2.2f);
        };
        return s;
    }

    // ------------------------------------------------------------ REV
    // Source: 2026-27 REV DUO FTC Starter Bot Build Guide (126 pages), BOM and Onshape CAD.
    static RobotSpec RevDuoStarterBot()
    {
        var s = new RobotSpec
        {
            name = "REV DUO FTC Starter Bot",
            maker = "REV Robotics",
            source = "docs.revrobotics.com/ftc-kickoff-concepts (Build Guide PDF, Onshape CAD)",
            drivetrain = "REV Channel drivetrain, 90mm omni / grip / traction wheels, HD Hex + UltraPlanetary 5:1+4:1, #25 chain",
            intake = "Flap wheels (Intake Flap 40A) on two 308mm hex shafts, polycarbonate floor/lid",
            storage = "Polycarbonate launcher floor feeding the flywheel",
            launcherDesc = "Flywheel: 2x 90mm grip wheels on a 252mm shaft, HD Hex 1:1 through 90T gears, feeder wheels on a servo",
            flowerDesc = "Servo-powered POLLEN poker on the side of the robot",
            notes = "Launch direction/angle estimated from the build guide renders.",
            lengthIn = 17.0f, widthIn = 15.6f, heightIn = 17.0f, massKg = 12f,
            drive = DriveType.Tank,
            maxSpeed = 1.41f,  // 6000 rpm / 20 x 90 mm wheel
            maxTurnDeg = 260f,
            launcher = LauncherType.Flywheel,
            launchAngle = 52f, launchMin = 2.5f, launchMax = 9f, feedInterval = 0.3f,
            spreadDeg = 1.1f, speedSpread = 0.015f,
            launchPoint = new Vector3(0, 15.4f, 3.4f),
            shootsBackward = false,
            intakeCenter = new Vector3(0, 1.5f, 9.1f), intakeSize = new Vector3(11.5f, 3.0f, 3.2f),
            flowerMech = FlowerMechType.Side,
            flowerMechPoint = new Vector3(8.9f, 1.4f, 1.5f), flowerMechRadius = 2.6f,
            slots = new[] { new Vector3(0, 3.2f, 5.0f), new Vector3(0, 4.8f, 2.4f), new Vector3(0, 6.6f, 0f), new Vector3(0, 8.6f, -2.4f) },
            chassisTopIn = 4.5f,
            upperCenter = new Vector3(0, 10.5f, -0.5f), upperSize = new Vector3(11f, 12f, 11f),
        };
        s.build = (p, spec) =>
        {
            foreach (int sx in new[] { -1, 1 })
            {
                Channel(p, sx * 6.0f, 2.4f, 0, 1.7f, 1.9f, 16.5f, RevGrey);
                Wheel(p, "OmniWheel90", sx * 7.5f, 1.77f, 6.2f, 3.54f, 1.0f, Black, DarkAlu);
                Wheel(p, "GripWheel90", sx * 7.5f, 1.77f, 0f, 3.54f, 1.0f, Black, Green);
                Wheel(p, "TractionWheel90", sx * 7.5f, 1.77f, -6.2f, 3.54f, 1.0f, Black, DarkAlu);
                foreach (float z in new[] { -4.2f, 3.2f })
                    B(p, "Extrusion15", sx * 5.0f, 9.5f, z, 0.6f, 12.7f, 0.6f, RevGrey);
                B(p, "LauncherRail", sx * 5.0f, 15.7f, -0.5f, 0.6f, 0.6f, 12.7f, RevGrey);
            }
            B(p, "Extrusion248_front", 0, 2.4f, 7.6f, 10.4f, 0.6f, 0.6f, RevGrey);
            B(p, "Extrusion248_back", 0, 2.4f, -7.6f, 10.4f, 0.6f, 0.6f, RevGrey);
            B(p, "LauncherCross", 0, 15.7f, -4.2f, 10.4f, 0.6f, 0.6f, RevGrey);
            Util.Cyl(p.root, "HDHexDrive", V(-3.0f, 2.6f, -3.5f), 1.45f * IN, 5.5f * IN, Black, Vector3.right);
            Util.Cyl(p.root, "HDHexDrive", V(3.0f, 2.6f, -3.5f), 1.45f * IN, 5.5f * IN, Black, Vector3.right);
            B(p, "SlimBattery", -2.8f, 3.6f, -6.0f, 4.4f, 1.6f, 2.6f, Black);
            B(p, "ControlHub", 2.8f, 4.0f, -5.8f, 4.4f, 1.0f, 3.9f, Black);
            // intake
            Roller(p, "FlapShaftLow", 0, 1.7f, 8.7f, 0.4f, 12.1f, DarkAlu, 8, RevGrey, 1.4f);
            Roller(p, "FlapShaftHigh", 0, 4.6f, 8.0f, 0.4f, 12.1f, DarkAlu, 8, RevGrey, 1.4f);
            B(p, "IntakeFloor", 0, 2.0f, 5.5f, 9.8f, 0.1f, 4.4f, Poly, Quaternion.Euler(-12, 0, 0), true);
            B(p, "IntakeLid", 0, 6.0f, 6.0f, 9.8f, 0.1f, 2.5f, Poly, Quaternion.Euler(20, 0, 0), true);
            B(p, "IntakeSide", 5.2f, 4.0f, 6.5f, 0.1f, 5.0f, 2.8f, Poly, null, true);
            // launcher floor (bent polycarbonate) and flywheel
            for (int i = 0; i < 6; i++)
            {
                float a = -80f + i * 24f;
                var off = Quaternion.Euler(a, 0, 0) * new Vector3(0, -3.2f, 0);
                B(p, "LauncherFloor", 0, 13.0f + off.y, -0.5f + off.z, 8.2f, 0.1f, 1.45f, Poly, Quaternion.Euler(a, 0, 0), true);
            }
            foreach (float x in new[] { -1.6f, 1.6f })
            {
                var fw = Wheel(p, "GripWheel90_Flywheel", x, 13.0f, -0.5f, 3.54f, 1.0f, Black, Green);
                p.wheelSpinners.Remove(fw.transform);
                p.launcherSpinners.Add(fw.transform);
            }
            Util.Cyl(p.root, "HDHexLauncher", V(5.9f, 13.0f, -0.5f), 1.45f * IN, 3.5f * IN, Black, Vector3.right);
            Util.Cyl(p.root, "Gear90T", V(4.4f, 13.0f, -0.5f), 3.6f * IN, 0.3f * IN, new Color(0.2f, 0.2f, 0.2f), Vector3.right);
            var feeder = Roller(p, "FeederWheels", 0, 7.4f, -4.1f, 2.3f, 6.5f, Black, 0, Black, 0.1f);
            p.intakeSpinners.Add(feeder.transform);
            // side POLLEN poker
            B(p, "PokerServo", 7.4f, 4.2f, 1.5f, 0.9f, 1.6f, 1.6f, Black);
            var arm = new GameObject("PokerPivot").transform;
            arm.SetParent(p.root, false);
            arm.localPosition = V(8.0f, 4.2f, 1.5f);
            Util.Box(arm, "PokerArm", V(0.5f, -1.4f, 0), V(0.5f, 3.2f, 0.9f), new Color(0.75f, 0.75f, 0.78f), false);
            p.flowerMech = arm;
            p.flowerMechRest = Vector3.zero;
            p.flowerMechActive = new Vector3(0, 0, 55);
            SignPlates(p, 6.85f, 6.0f, 10f, 3.0f);
        };
        return s;
    }

    // ------------------------------------------------------------ AndyMark
    // Source: AndyMark Robits BIOBUZZ StarterBot Assembly Guide (42 pages), Robot Manual
    // (22 pages) and STEP CAD (standard, alternate flower mechanism, mecanum).
    static RobotSpec RobitsStarterBot(bool mecanum)
    {
        var s = new RobotSpec
        {
            name = mecanum ? "AndyMark Robits StarterBot (Mecanum)" : "AndyMark Robits StarterBot",
            maker = "AndyMark",
            source = mecanum ? "andymark.com/pages/2026-2027-robits-starterbot-base (CAD: Robits BIOBUZZ Robot (Mecanum).STEP)"
                             : "andymark.com/pages/2026-2027-robits-starterbot-base (Assembly Guide, Robot Manual, STEP)",
            drivetrain = mecanum ? "Robits chassis converted to mecanum (per Robot Manual p.20)" : "Skid-steer, belt driven, powered 3in Stealth + 3in Omni wheels",
            intake = "Servo-powered rubber band rollers, full width, adjustable backstop",
            storage = "Passive gravity-fed angled hopper",
            launcherDesc = "Coaxial flinger catapult: one motor winds and fires, rubber band powered, curved hood, 4in channel",
            flowerDesc = "Roller version: trimmed compliant wheel on a continuous servo",
            notes = "Fixed-power catapult: distance is set by driving to the right spot (Robot Manual p.17).",
            lengthIn = 16.8f, widthIn = 16.4f, heightIn = 17.5f, massKg = 10f,
            drive = mecanum ? DriveType.Mecanum : DriveType.Tank,
            maxSpeed = mecanum ? 1.15f : 1.24f,
            maxTurnDeg = 250f,
            launcher = LauncherType.Catapult,
            launchAngle = 55f, launchFixed = 6.5f, feedInterval = 0.75f,
            spreadDeg = 2.2f, speedSpread = 0.035f,
            launchPoint = new Vector3(3.0f, 16.2f, 3.2f),
            shootsBackward = false,
            intakeCenter = new Vector3(0, 1.6f, 9.0f), intakeSize = new Vector3(13f, 3.2f, 3.2f),
            flowerMech = FlowerMechType.Side,
            flowerMechPoint = new Vector3(-5.6f, 1.4f, -9.4f), flowerMechRadius = 2.5f,
            slots = new[] { new Vector3(3f, 3.4f, 5.0f), new Vector3(3f, 3.0f, 2.3f), new Vector3(3f, 2.8f, -0.4f), new Vector3(3f, 8.5f, -2.0f) },
            chassisTopIn = 4.5f,
            upperCenter = new Vector3(2.8f, 11f, -0.5f), upperSize = new Vector3(9f, 12f, 10f),
        };
        s.build = (p, spec) =>
        {
            foreach (int sx in new[] { -1, 1 })
            {
                Channel(p, sx * 6.2f, 2.1f, 0, 1f, 1f, 15.5f, Alu);
                if (mecanum)
                {
                    Mecanum(p, sx * 7.4f, 1.55f, 5.2f, 3.1f, 1.1f);
                    Mecanum(p, sx * 7.4f, 1.55f, -5.2f, 3.1f, 1.1f);
                }
                else
                {
                    Wheel(p, "StealthWheel3in", sx * 7.3f, 1.5f, 5.3f, 3f, 1f, new Color(0.45f, 0.45f, 0.47f), DarkAlu);
                    Wheel(p, "OmniWheel3in", sx * 7.3f, 1.5f, -5.3f, 3f, 1f, Black, DarkAlu);
                    B(p, "HTDBelt", sx * 6.85f, 1.5f, 0, 0.3f, 0.2f, 10.6f, Black);
                }
            }
            Channel(p, 0, 2.1f, -7.2f, 11.4f, 1f, 1f, Alu);
            Channel(p, 0, 2.1f, 7.2f, 11.4f, 1f, 1f, Alu);
            Util.Cyl(p.root, "DriveMotor", V(-3.3f, 3.1f, -3.0f), 1.5f * IN, 5f * IN, DarkAlu, Vector3.right);
            Util.Cyl(p.root, "DriveMotor", V(3.3f, 3.1f, -3.0f), 1.5f * IN, 5f * IN, DarkAlu, Vector3.right);
            B(p, "Battery", -4.2f, 4.2f, -4.6f, 2.3f, 3.0f, 4.3f, Black);
            // intake rollers (rubber bands between star rollers)
            var r = Roller(p, "RubberBandRoller", 0, 2.3f, 8.3f, 2.6f, 12.8f, new Color(0.2f, 0.2f, 0.2f), 6, Black, 0.3f);
            foreach (int sx in new[] { -1, 1 })
                Util.Cyl(r.transform, "IntakeGear", new Vector3(sx * 0.46f, 0, 0), 1.25f, 0.02f, DarkAlu, Vector3.up);
            B(p, "IntakeBackstop", 0, 5.4f, 6.4f, 11.5f, 0.1f, 4.0f, Poly, Quaternion.Euler(35, 0, 0), true);
            B(p, "IntakeServo", 6.9f, 3.9f, 8.0f, 0.9f, 1.6f, 1.6f, Black);
            // hopper
            B(p, "HopperFloor", 3.0f, 3.3f, 2.0f, 4.4f, 0.12f, 9.5f, Poly, Quaternion.Euler(-8, 0, 0), true);
            // launcher tower (1/2in ROBITS tubes)
            foreach (float x in new[] { 0.5f, 5.5f })
            foreach (float z in new[] { -3.8f, 2.6f })
                B(p, "RobitsTube", x, 9.6f, z, 0.5f, 15f, 0.5f, Alu);
            B(p, "LauncherTube1x1", 3.0f, 12.0f, -3.8f, 7f, 1f, 1f, Alu);
            Util.Cyl(p.root, "LauncherMotor", V(3.0f, 12.0f, -5.3f), 1.5f * IN, 3.5f * IN, DarkAlu, Vector3.forward);
            var pivot = new GameObject("CatapultPivot").transform;
            pivot.SetParent(p.root, false);
            pivot.localPosition = V(3.0f, 12.0f, -2.0f);
            Util.Box(pivot, "CatapultArm", V(0, 0, 3.0f), V(0.9f, 0.5f, 6.5f), Black, false);
            Util.Box(pivot, "Cradle", V(0, 0.6f, 6.0f), V(2.6f, 0.3f, 2.0f), Black, false);
            p.catapultArm = pivot;
            // hood + protective cover (curved perforated polycarbonate)
            for (int i = 0; i < 7; i++)
            {
                float a = -10f + i * 22f;
                var off = Quaternion.Euler(a, 0, 0) * new Vector3(0, 5.0f, 0);
                B(p, "Hood", 3.0f, 12.0f + off.y, -1.0f + off.z, 4.2f, 0.1f, 2.1f, Poly, Quaternion.Euler(a, 0, 0), true);
            }
            B(p, "LauncherInnerWall", 0.9f, 13.5f, 0.5f, 0.1f, 3f, 6f, Poly, null, true);
            B(p, "LauncherOuterWall", 5.1f, 13.5f, 0.5f, 0.1f, 3f, 9.5f, Poly, null, true);
            // FLOWER mechanism (roller version) at the rear-left corner
            B(p, "FlowerServo", -5.6f, 3.2f, -8.3f, 1.6f, 1.6f, 0.9f, Black);
            var fm = Util.Cyl(p.root, "CompliantWheel", V(-5.6f, 1.3f, -8.6f), 2f * IN, 1f * IN, Green, Vector3.up);
            p.intakeSpinners.Add(fm.transform);
            p.flowerMech = fm.transform;
            SignPlates(p, 6.7f, 4.0f, 8f, 2.5f);
        };
        return s;
    }

    // ------------------------------------------------------------ Studica
    // Source: Studica Starter Bot 2026/2027 Build Guide (pre-kickoff release, 34 pages) and
    // STEP files. Studica's published guide covers the drive base and intake rollers only;
    // it has no launcher, so this robot scores by pushing/ejecting into GARDENS and PARKING.
    static RobotSpec StudicaStarterBot()
    {
        var s = new RobotSpec
        {
            name = "Studica Starter Bot",
            maker = "Studica Robotics",
            source = "studica.com/ftc-starter-bot-resource-guide-2026-2027 (2026-27 Build Guide PDF, STEP ZIP)",
            drivetrain = "336mm U-Channel base, 2x Maverick 50.9:1 Clover gearbox w/ 36T bevel gears, drive wheels + omni wheels",
            intake = "Two roller shafts with silicone tubing hubs, 2x Multi Mode Smart Servos",
            storage = "Rides on the chassis (up to 4 POLLEN)",
            launcherDesc = "None in the published guide",
            flowerDesc = "None",
            notes = "Pre-kickoff design: no launcher. Speed estimated from the 50.9:1 gearbox.",
            lengthIn = 15.0f, widthIn = 14.6f, heightIn = 8.5f, massKg = 8f,
            drive = DriveType.Tank,
            maxSpeed = 0.9f,
            maxTurnDeg = 200f,
            launcher = LauncherType.None,
            intakeCenter = new Vector3(0, 1.6f, 8.6f), intakeSize = new Vector3(11f, 3.2f, 3.0f),
            flowerMech = FlowerMechType.None,
            slots = new[] { new Vector3(-2.2f, 3.9f, 2.0f), new Vector3(2.2f, 3.9f, 2.0f), new Vector3(-2.2f, 3.9f, -1.8f), new Vector3(2.2f, 3.9f, -1.8f) },
            chassisTopIn = 3.2f,
            upperCenter = new Vector3(0, 5.5f, 6.0f), upperSize = new Vector3(12.5f, 4.5f, 2f),
        };
        s.build = (p, spec) =>
        {
            foreach (int sx in new[] { -1, 1 })
            {
                Channel(p, sx * 5.9f, 2.2f, 0, 1.5f, 1.5f, 13.2f, StudicaBlue);
                Wheel(p, "DriveWheel", sx * 7.0f, 1.95f, -4.4f, 3.9f, 1.0f, Black, StudicaBlue);
                Wheel(p, "OmniWheel", sx * 7.0f, 1.95f, 4.4f, 3.9f, 1.0f, Black, DarkAlu);
                Util.Cyl(p.root, "MaverickMotor", V(sx * 2.8f, 2.2f, -4.4f), 1.4f * IN, 5.0f * IN, new Color(0.15f, 0.15f, 0.18f), Vector3.right);
                B(p, "U-Channel_Upright", sx * 5.9f, 4.4f, 6.1f, 1.5f, 5.5f, 1.5f, StudicaBlue);
                B(p, "SmartServo", sx * 6.9f, 1.8f, 7.6f, 0.9f, 1.6f, 1.6f, Black);
                B(p, "SmartServo", sx * 6.9f, 5.0f, 7.6f, 0.9f, 1.6f, 1.6f, Black);
            }
            Channel(p, 0, 2.2f, 6.4f, 10.3f, 1.5f, 1.5f, StudicaBlue);
            Channel(p, 0, 2.2f, -6.4f, 10.3f, 1.5f, 1.5f, StudicaBlue);
            B(p, "LowProfileChannel288", 0, 7.4f, 6.1f, 11.3f, 0.8f, 1.5f, StudicaBlue);
            B(p, "PolycarbonateDeck", 0, 3.0f, 0, 10.3f, 0.1f, 11.5f, Poly, null, true);
            B(p, "Battery", 0, 3.8f, -3.9f, 4.3f, 1.5f, 2.3f, Black);
            B(p, "ControlHub", 0, 5.1f, -3.9f, 4.3f, 0.9f, 3.9f, Black);
            Roller(p, "SiliconeTubingRollerLow", 0, 1.8f, 7.7f, 0.9f, 11.2f, DarkAlu, 8, new Color(0.3f, 0.55f, 0.95f), 1.4f);
            Roller(p, "SiliconeTubingRollerHigh", 0, 5.0f, 7.7f, 0.9f, 11.2f, DarkAlu, 8, new Color(0.3f, 0.55f, 0.95f), 1.4f);
            SignPlates(p, 6.7f, 3.9f, 7f, 1.6f);
        };
        return s;
    }

    // ------------------------------------------------------------ Turret concept
    // Inspired by FRC 1690 Orbit's 2026 REBUILT robot "KEPLER" (turret shooter that shoots on
    // the move, very fast roller-floor intake). Scaled into the FTC 18 in cube; not a published FTC build.
    static RobotSpec TurretBot()
    {
        var s = new RobotSpec
        {
            name = "Turret Bot",
            maker = "Concept",
            source = "Inspired by FRC 1690 Orbit 'KEPLER' (2026 REBUILT) - chiefdelphi.com/t/orbit-1690-2026-robot-reveal-kepler",
            drivetrain = "Mecanum, 104mm wheels, 312 RPM",
            intake = "Full-width roller intake feeding a roller floor",
            storage = "Roller-floor hopper into a single-channel feeder (4 max, G407)",
            launcherDesc = "360-degree turret: dual flywheels, adjustable hood 40-70 deg, velocity-compensated shooting on the move",
            flowerDesc = "Intake pulls POLLEN from the FLOWER bottom",
            notes = "Concept robot. Turret tracks the target continuously; FIRE releases whenever it is locked.",
            concept = true,
            lengthIn = 17.6f, widthIn = 17.6f, heightIn = 17.2f, massKg = 13f,
            drive = DriveType.Mecanum, maxSpeed = 1.6f, accel = 5.5f, maxTurnDeg = 300f,
            launcher = LauncherType.Flywheel,
            launchAngle = 55f, hoodMin = 40f, hoodMax = 70f, launchMin = 2.5f, launchMax = 9.5f,
            feedInterval = 0.14f, spreadDeg = 0.8f, speedSpread = 0.01f,
            turret = true, turretAlwaysTracks = true, shootOnMove = true,
            turretPivot = new Vector3(0, 12.5f, -2.5f), turretRange = 180f, turretSpeed = 720f,
            launchPoint = new Vector3(0, 3.2f, 3.8f),
            intakeCenter = new Vector3(0, 1.5f, 9.2f), intakeSize = new Vector3(15f, 3.2f, 3.4f),
            flowerMech = FlowerMechType.Intake,
            flowerMechPoint = new Vector3(0, 1.4f, 9.6f), flowerMechRadius = 2.4f,
            slots = new[] { new Vector3(-2.5f, 3.4f, 4.0f), new Vector3(2.5f, 3.4f, 4.0f), new Vector3(0, 3.6f, 0.5f), new Vector3(0, 8.0f, -2.5f) },
            chassisTopIn = 5.0f,
            upperCenter = new Vector3(0, 11.5f, -2.5f), upperSize = new Vector3(10f, 11f, 10f),
        };
        s.build = (p, spec) =>
        {
            Color frame = new Color(0.2f, 0.22f, 0.26f), accent = new Color(0.05f, 0.45f, 0.95f);
            foreach (int sx in new[] { -1, 1 })
            {
                Channel(p, sx * 6.9f, 2.4f, 0, 1.4f, 2.0f, 15.5f, frame);
                Mecanum(p, sx * 8.3f, 2.05f, 5.6f, 4.09f, 1.3f);
                Mecanum(p, sx * 8.3f, 2.05f, -5.6f, 4.09f, 1.3f);
            }
            B(p, "BellyPan", 0, 1.1f, 0, 12.4f, 0.2f, 15.5f, frame);
            // full-width intake + roller floor
            Roller(p, "IntakeRoller", 0, 1.5f, 8.6f, 1.8f, 15f, Black, 10, new Color(0.9f, 0.4f, 0.1f), 0.5f);
            for (int i = 0; i < 5; i++)
                Roller(p, "RollerFloor", 0, 2.3f + i * 0.35f, 5.8f - i * 2.1f, 1.0f, 11.5f, new Color(0.25f, 0.25f, 0.25f), 0, Black, 0.1f);
            foreach (int sx in new[] { -1, 1 })
                B(p, "HopperWall", sx * 5.9f, 5.5f, 1.5f, 0.12f, 6f, 12f, Poly, null, true);
            B(p, "Feeder", 0, 8.0f, -2.5f, 3.4f, 7f, 3.4f, frame);
            Util.Cyl(p.root, "TurretRing", V(0, 11.9f, -2.5f), 9.5f * IN, 0.6f * IN, accent, Vector3.up);
            B(p, "ControlHub", -4.2f, 4.2f, -6.0f, 4.0f, 1.0f, 3.9f, Black);
            B(p, "Battery", 4.2f, 4.2f, -6.0f, 2.3f, 3.0f, 4.3f, Black);

            var turret = new GameObject("Turret").transform;
            turret.SetParent(p.root, false);
            turret.localPosition = V(0, 12.5f, -2.5f);
            p.turret = turret;
            foreach (int sx in new[] { -1, 1 })
                Util.Box(turret, "TurretPlate", V(sx * 2.6f, 2.4f, 0.5f), V(0.2f, 5.2f, 8f), accent, false);
            Util.Box(turret, "TurretBase", V(0, 0.2f, 0), V(5.4f, 0.4f, 8f), frame, false);
            var fw1 = Util.Cyl(turret, "Flywheel", V(0, 2.2f, 1.5f), 3.0f * IN, 4.4f * IN, Black, Vector3.right);
            p.launcherSpinners.Add(fw1.transform);
            Util.Cyl(turret, "FlywheelMotor", V(3.6f, 2.2f, 1.5f), 1.4f * IN, 2.5f * IN, new Color(0.1f, 0.1f, 0.1f), Vector3.right);
            Util.Box(turret, "Limelight", V(0, 5.3f, -1.5f), V(2.2f, 1.2f, 1.6f), Black, false);
            var hood = new GameObject("Hood").transform;
            hood.SetParent(turret, false);
            hood.localPosition = V(0, 2.2f, -0.5f);
            p.hood = hood;
            for (int i = 0; i < 5; i++)
            {
                float a = -60f + i * 28f;
                var off = Quaternion.Euler(a, 0, 0) * new Vector3(0, 2.6f, 0);
                Util.Box(hood, "HoodPlate", V(0, off.y, off.z), V(4.8f, 0.12f, 1.4f), Poly, false, Quaternion.Euler(a, 0, 0), true);
            }
            SignPlates(p, 7.5f, 4.6f, 9f, 2.0f);
        };
        return s;
    }

    // ------------------------------------------------------------ Twin cannon concept
    // Inspired by FRC 5614 Team Sycamore's 2026 robot "Scyther" (dual shooter). FTC-scale concept.
    static RobotSpec TwinCannonBot()
    {
        var s = new RobotSpec
        {
            name = "Double Cannon Bot",
            maker = "Concept",
            source = "Inspired by FRC 5614 Team Sycamore 'Scyther' (2026 REBUILT) - chiefdelphi.com/t/5614-team-sycamore-2026-robot-reveal",
            drivetrain = "Drop-center 6WD, 96mm wheels, 312 RPM",
            intake = "Full-width roller with a V-splitter into two lanes",
            storage = "Two 2-ball lanes (4 max, G407)",
            launcherDesc = "Two fixed flywheel cannons side by side, 55 deg; each volley fires both lanes",
            flowerDesc = "Intake pulls POLLEN from the FLOWER bottom",
            notes = "Concept robot. Fires two elements per volley, 6 in apart - the HIVE CELL opening is 20 in wide.",
            concept = true,
            lengthIn = 17.4f, widthIn = 17.2f, heightIn = 16.5f, massKg = 12.5f,
            drive = DriveType.Tank, maxSpeed = 1.55f, accel = 5f, maxTurnDeg = 280f,
            launcher = LauncherType.Flywheel,
            launchAngle = 55f, launchMin = 2.5f, launchMax = 9f,
            feedInterval = 0.45f, spreadDeg = 1.0f, speedSpread = 0.015f,
            barrels = 2, barrelSpacing = 6f,
            launchPoint = new Vector3(0, 14.8f, 3.0f),
            intakeCenter = new Vector3(0, 1.5f, 9.0f), intakeSize = new Vector3(14.5f, 3.2f, 3.4f),
            flowerMech = FlowerMechType.Intake,
            flowerMechPoint = new Vector3(0, 1.4f, 9.4f), flowerMechRadius = 2.4f,
            slots = new[] { new Vector3(-3f, 5.5f, 1.5f), new Vector3(3f, 5.5f, 1.5f), new Vector3(-3f, 4.0f, 4.4f), new Vector3(3f, 4.0f, 4.4f) },
            chassisTopIn = 5.0f,
            upperCenter = new Vector3(0, 10.5f, -0.5f), upperSize = new Vector3(13f, 11f, 9f),
        };
        s.build = (p, spec) =>
        {
            Color frame = new Color(0.12f, 0.35f, 0.18f), metal = new Color(0.75f, 0.76f, 0.78f);
            foreach (int sx in new[] { -1, 1 })
            {
                Channel(p, sx * 6.6f, 2.5f, 0, 1.9f, 1.9f, 15.1f, metal);
                Wheel(p, "Wheel96", sx * 8.1f, 1.9f, -5.9f, 3.78f, 1.3f, Black, frame);
                Wheel(p, "Wheel96", sx * 8.1f, 1.9f, 0f, 3.78f, 1.3f, Black, frame);
                Wheel(p, "Omni96", sx * 8.1f, 1.9f, 5.9f, 3.78f, 1.3f, Black, metal);
                B(p, "Lane", sx * 3f, 4.3f, 2.5f, 3.2f, 0.12f, 9f, Poly, Quaternion.Euler(-12, 0, 0), true);
            }
            Roller(p, "IntakeRoller", 0, 1.5f, 8.5f, 1.8f, 14.5f, Black, 8, new Color(0.9f, 0.8f, 0.1f), 0.5f);
            B(p, "VSplitter", 0, 3.5f, 6.3f, 0.2f, 3f, 3f, frame, Quaternion.Euler(0, 45, 0));
            B(p, "ControlHub", 0, 3.4f, -5.8f, 4.0f, 1.0f, 3.9f, Black);
            B(p, "Battery", 0, 5.0f, -3.5f, 4.3f, 2.3f, 3.0f, Black);
            foreach (int sx in new[] { -1, 1 })
            {
                B(p, "Tower", sx * 6.4f, 9.5f, -1.5f, 1.2f, 12f, 1.2f, metal);
                // cannon barrel at 55 deg
                var barrel = Util.Cyl(p.root, "CannonBarrel", V(sx * 3f, 12.2f, 0.8f), 3.4f * IN, 7.5f * IN, frame, Quaternion.Euler(-55f + 90f, 0, 0) * Vector3.up, false);
                Util.Cyl(p.root, "CannonMuzzle", V(sx * 3f, 14.9f, 2.7f), 3.8f * IN, 0.5f * IN, metal, Quaternion.Euler(35f, 0, 0) * Vector3.up);
                var fly = Util.Cyl(p.root, "CannonFlywheel", V(sx * 3f, 10.0f, -1.2f), 3.0f * IN, 1.2f * IN, Black, Vector3.right);
                p.launcherSpinners.Add(fly.transform);
                Util.Cyl(p.root, "CannonMotor", V(sx * 5.4f, 10.0f, -1.2f), 1.4f * IN, 2.6f * IN, new Color(0.1f, 0.1f, 0.1f), Vector3.right);
            }
            B(p, "CannonBridge", 0, 15.8f, -1.5f, 13.6f, 1.0f, 1.0f, metal);
            SignPlates(p, 7.55f, 5.0f, 9f, 2.0f);
        };
        return s;
    }
}
