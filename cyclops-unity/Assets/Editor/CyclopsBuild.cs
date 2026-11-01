using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CyclopsBuild
{
    const string ScenePath = "Assets/Scenes/Main.unity";

    [MenuItem("Cyclops/Build Scene")]
    public static void MakeScene()
    {
        // 코드로만 쓰는 셰이더가 빌드에서 빠지지 않도록 재질을 Resources에 둔다
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/Base.mat"))
        {
            var mat = new Material(Shader.Find("Standard"));
            mat.SetFloat("_Glossiness", .15f);
            AssetDatabase.CreateAsset(mat, "Assets/Resources/Base.mat");
        }
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

    public static void BuildWeb()
    {
        MakeScene();
        PlayerSettings.productName = "Cyclops";
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
        PlayerSettings.WebGL.decompressionFallback = true;
        // 장면을 코드로 만들므로 씬에 없는 엔진 기능도 빌드에 남겨야 한다
        PlayerSettings.stripEngineCode = false;
        var report = BuildPipeline.BuildPlayer(new[] { ScenePath }, "Build/Web", BuildTarget.WebGL, BuildOptions.None);
        Debug.Log("BUILD " + report.summary.result);
        EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
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
