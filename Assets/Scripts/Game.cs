using System.Collections;
using UnityEngine;

// Entry point: sets up physics, lighting, camera and managers, and (re)builds the world.
public class Game : MonoBehaviour
{
    public static Game I;
    public static Transform World;
    public CameraRig rig;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindFirstObjectByType<Game>() == null) new GameObject("Game").AddComponent<Game>();
    }

    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;
        Application.targetFrameRate = 120;
        WindowTitle.Apply();

        // Small, fast balls need a fine physics step and a small contact offset.
        Time.fixedDeltaTime = 1f / 200f;
        Time.maximumDeltaTime = 1f / 20f;
        Physics.defaultContactOffset = 0.002f;
        Physics.defaultSolverIterations = 12;
        Physics.defaultSolverVelocityIterations = 4;
        Physics.bounceThreshold = 0.25f;
        Physics.gravity = new Vector3(0, -9.81f, 0);
        Physics.IgnoreLayerCollision(Layers.Hive, Layers.Field, true);
        Physics.IgnoreLayerCollision(Layers.Hive, Layers.Robot, false);

        foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None)) Destroy(c.gameObject);
        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) Destroy(l.gameObject);

        // clears the whole screen (the planner renders the field into part of it)
        var bg = new GameObject("Background Camera").AddComponent<Camera>();
        bg.clearFlags = CameraClearFlags.SolidColor;
        bg.backgroundColor = new Color(0.035f, 0.04f, 0.055f);
        bg.cullingMask = 0;
        bg.depth = -10;

        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.06f, 0.07f, 0.09f);
        cam.nearClipPlane = 0.03f;
        cam.farClipPlane = 60f;
        camGo.AddComponent<AudioListener>();
        rig = camGo.AddComponent<CameraRig>();
        camGo.transform.position = new Vector3(0, 3, -5);

        var sun = new GameObject("Key Light").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 1.05f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.6f;
        sun.transform.rotation = Quaternion.Euler(58, -35, 0);
        var fill = new GameObject("Fill Light").AddComponent<Light>();
        fill.type = LightType.Directional;
        fill.intensity = 0.35f;
        fill.shadows = LightShadows.None;
        fill.transform.rotation = Quaternion.Euler(35, 150, 0);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.42f, 0.43f, 0.46f);
        QualitySettings.shadowDistance = 12f;

        gameObject.AddComponent<SoundFx>();
        gameObject.AddComponent<MatchManager>();
        gameObject.AddComponent<GameUI>();
        gameObject.AddComponent<PlannerUI>().enabled = false;

        BuildWorld(false);
        AutoTest.TryStart(this);
    }

    void BuildWorld(bool withRobots)
    {
        if (World != null)
        {
            World.gameObject.SetActive(false);   // unregisters elements/flowers immediately
            Destroy(World.gameObject);
        }
        World = new GameObject("World").transform;
        Field.Build(World, true);
        if (withRobots) MatchManager.I.SpawnRobots(World);
        else MatchManager.I.robots.Clear();
    }

    public void RebuildWorldNow(bool withRobots) => BuildWorld(withRobots);

    public void StartPlanner()
    {
        StopAllCoroutines();
        Time.timeScale = 1f;
        GetComponent<PlannerUI>().Open();
    }

    public void StartGame(bool practice)
    {
        GameConfig.practice = practice;
        StartCoroutine(Rebuild(practice));
    }

    public void Restart() => StartGame(GameConfig.practice);

    // true while a match is being (re)built: nothing from the menu is shown in between
    public bool Rebuilding { get; private set; }

    IEnumerator Rebuild(bool practice)
    {
        Rebuilding = true;
        var mm = MatchManager.I;
        mm.period = Period.Menu;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        yield return null;
        mm.Red.nectarInArea = 5; mm.Red.nectarGrants = 0; mm.Red.nectarEntered = 0; mm.Red.foulPointsReceived = 0;
        mm.Blue.nectarInArea = 5; mm.Blue.nectarGrants = 0; mm.Blue.nectarEntered = 0; mm.Blue.foulPointsReceived = 0;
        BuildWorld(true);
        yield return null;
        mm.Begin(practice);
        if (rig.mode == CameraRig.Mode.Orbit) rig.mode = CameraRig.Mode.DriverStation;
        Rebuilding = false;
    }

    public void BackToMenu()
    {
        StopAllCoroutines();
        Rebuilding = false;
        var planner = GetComponent<PlannerUI>();
        if (PlannerUI.Active == planner && planner) planner.Close();
        Physics.simulationMode = SimulationMode.FixedUpdate;
        Time.timeScale = 1f;
        MatchManager.I.period = Period.Menu;
        BuildWorld(false);
    }
}
