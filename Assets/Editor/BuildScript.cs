using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Creates the runtime materials (kept in Resources so their shaders ship in builds),
// the bootstrap scene, and builds the Windows player.
[InitializeOnLoad]
public static class BuildScript
{
    const string MatDir = "Assets/Resources/BioBuzz";
    const string ScenePath = "Assets/Scenes/Main.unity";

    static BuildScript()
    {
        EditorApplication.delayCall += () =>
        {
            if (!File.Exists(ScenePath) || !File.Exists(MatDir + "/Base.mat")) Setup();
        };
    }

    [MenuItem("BIOBUZZ/Setup Project")]
    public static void Setup()
    {
        Directory.CreateDirectory(MatDir);
        Directory.CreateDirectory("Assets/Scenes");

        if (!File.Exists(MatDir + "/Base.mat"))
        {
            var m = new Material(Shader.Find("Standard")) { color = Color.white };
            AssetDatabase.CreateAsset(m, MatDir + "/Base.mat");
        }
        if (!File.Exists(MatDir + "/Transparent.mat"))
        {
            var m = new Material(Shader.Find("Standard")) { color = new Color(1, 1, 1, 0.3f) };
            Util.MakeTransparent(m);
            AssetDatabase.CreateAsset(m, MatDir + "/Transparent.mat");
        }
        if (!File.Exists(MatDir + "/Line.mat"))
        {
            var m = new Material(Shader.Find("Sprites/Default"));
            AssetDatabase.CreateAsset(m, MatDir + "/Line.mat");
        }
        AssetDatabase.SaveAssets();

        if (!File.Exists(ScenePath))
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Game").AddComponent<Game>();
            EditorSceneManager.SaveScene(scene, ScenePath);
        }
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

        PlayerSettings.companyName = "BIOBUZZ Sim";
        PlayerSettings.productName = "BIOBUZZ Simulator";
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.defaultScreenWidth = 1600;
        PlayerSettings.defaultScreenHeight = 900;
        PlayerSettings.resizableWindow = true;
        PlayerSettings.runInBackground = true;
        PlayerSettings.colorSpace = ColorSpace.Linear;
        AssetDatabase.Refresh();
    }

    [MenuItem("BIOBUZZ/Build Windows")]
    public static void BuildWindows()
    {
        Setup();
        var opts = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = "Build/BioBuzzSim.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        };
        var report = BuildPipeline.BuildPlayer(opts);
        Debug.Log($"BUILD RESULT: {report.summary.result} errors={report.summary.totalErrors} size={report.summary.totalSize}");
        if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
    }
}
