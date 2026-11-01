using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CyclopsBuild
{
    const string ScenePath = "Assets/Scenes/Main.unity";

    [MenuItem("Cyclops/Build Scene")]
    public static void MakeScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        new GameObject("Game").AddComponent<CyclopsGame>();
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(.06f, .055f, .06f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = Color.black;
        RenderSettings.fogDensity = .045f;
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
    }

    public static void BuildMac()
    {
        MakeScene();
        PlayerSettings.productName = "Cyclops";
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.defaultIsNativeResolution = false;
        PlayerSettings.defaultScreenWidth = 450;
        PlayerSettings.defaultScreenHeight = 800;
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
        var report = BuildPipeline.BuildPlayer(new[] { ScenePath }, "Build/Cyclops.app", BuildTarget.StandaloneOSX, BuildOptions.None);
        Debug.Log("BUILD " + report.summary.result);
        EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
    }
}
