using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public enum StepType { Shoot, Wait, CollectNearest, ScoreFlower, Outtake, Park }
public enum MarkerType { Intake, ShootOnMove, FlowerMech, Outtake }

// A waypoint: field position (metres), robot heading (degrees, 0 = +Z away from the audience),
// speed limit for the drive INTO this waypoint, and actions performed after arriving.
[Serializable]
public class PlanWaypoint
{
    public float x, z, heading;
    public float speed = 1.2f;      // m/s
    public bool stop = true;        // come to rest here (always true when it has steps)
    public bool reverse;            // tank drive: drive backwards into this waypoint
    public List<PlanStep> steps = new List<PlanStep>();
    public Vector3 Pos => new Vector3(x, 0, z);
}

[Serializable]
public class PlanStep
{
    public StepType type;
    public float duration = 1.5f;   // Shoot/Wait: time; Collect/Score: timeout after arrival
    public int flower = -1;         // ScoreFlower: FLOWER index, -1 = nearest

    public string Label
    {
        get
        {
            switch (type)
            {
                case StepType.Shoot: return $"Shoot {duration:0.0}s";
                case StepType.Wait: return $"Wait {duration:0.0}s";
                case StepType.CollectNearest: return $"Collect nearest (vision) ≤{duration:0.0}s";
                case StepType.ScoreFlower: return flower < 0 ? $"Score nearest FLOWER {duration:0.0}s" : $"Score FLOWER {flower + 1} {duration:0.0}s";
                case StepType.Outtake: return $"Outtake {duration:0.0}s";
                default: return "Park in LOADING ZONE";
            }
        }
    }
}

// Parallel action on the timeline (runs while the robot keeps moving).
[Serializable]
public class PlanMarker
{
    public MarkerType type;
    public float time, duration = 1.5f;
    public string Label => type == MarkerType.ShootOnMove ? "Fire on the move" : type == MarkerType.FlowerMech ? "FLOWER mech" : type.ToString();
}

[Serializable]
public class AutoPlan
{
    public string name = "New Auto";
    public int robot;
    public Alliance alliance = Alliance.Red;
    public float accelScale = 0.8f;   // fraction of the robot's acceleration used for planning
    public List<PlanWaypoint> waypoints = new List<PlanWaypoint>();
    public List<PlanMarker> markers = new List<PlanMarker>();

    public RobotSpec Spec => RobotLibrary.All[Mathf.Clamp(robot, 0, RobotLibrary.All.Count - 1)];

    public static string Folder
    {
        get
        {
            string d = Path.Combine(Application.persistentDataPath, "AutoPlans");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    static string FileFor(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return Path.Combine(Folder, name + ".json");
    }

    public void Save() => File.WriteAllText(FileFor(name), JsonUtility.ToJson(this, true));

    public static AutoPlan Load(string name)
    {
        try
        {
            string f = FileFor(name);
            return File.Exists(f) ? JsonUtility.FromJson<AutoPlan>(File.ReadAllText(f)) : null;
        }
        catch { return null; }
    }

    public static List<string> SavedNames()
    {
        var list = new List<string>();
        try { foreach (var f in Directory.GetFiles(Folder, "*.json")) list.Add(Path.GetFileNameWithoutExtension(f)); }
        catch { }
        list.Sort();
        return list;
    }

    public static void Delete(string name)
    {
        try { File.Delete(FileFor(name)); } catch { }
    }

    public AutoPlan Clone() => JsonUtility.FromJson<AutoPlan>(JsonUtility.ToJson(this));

    // The FIELD is 180-degree rotationally symmetric: mirror a plan to the other ALLIANCE.
    public AutoPlan ForAlliance(Alliance a)
    {
        if (a == alliance) return this;
        var c = Clone();
        c.alliance = a;
        foreach (var w in c.waypoints)
        {
            w.x = -w.x; w.z = -w.z;
            w.heading = Mathf.Repeat(w.heading + 180f, 360f);
            foreach (var s in w.steps) if (s.flower >= 0) s.flower = (s.flower + 2) % 4;
        }
        return c;
    }

    public int StepCount
    {
        get { int n = 0; foreach (var w in waypoints) n += w.steps.Count; return n; }
    }
}
