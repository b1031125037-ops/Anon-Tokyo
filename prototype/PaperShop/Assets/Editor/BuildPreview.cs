using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using AnonTokyo.Preview;

public static class BuildPreview
{
    [MenuItem("Paper Shop/Build Windows Preview")]
    public static void Build()
    {
        Directory.CreateDirectory("Assets/Scenes");
        foreach (var name in new[] { "idle", "work" })
        {
            var path = "Assets/Resources/CardTextures/" + name + ".png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Missing card texture: " + path);
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 1024;
            importer.SaveAndReimport();
        }

        var pipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
        if (pipeline == null) throw new InvalidOperationException("URP PC pipeline asset missing.");
        var pipelineSettings = new SerializedObject(pipeline);
        pipelineSettings.FindProperty("m_MSAA").intValue = 4;
        pipelineSettings.FindProperty("m_MainLightShadowmapResolution").intValue = 4096;
        pipelineSettings.FindProperty("m_ShadowDistance").floatValue = 20;
        pipelineSettings.FindProperty("m_ShadowNormalBias").floatValue = 0.16f;
        pipelineSettings.FindProperty("m_ShadowDepthBias").floatValue = 0.08f;
        pipelineSettings.ApplyModifiedPropertiesWithoutUndo();
        var rendererData = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/Settings/PC_Renderer.asset");
        var rendererSettings = new SerializedObject(rendererData);
        rendererSettings.FindProperty("m_RenderingMode").intValue = 0;
        rendererSettings.ApplyModifiedPropertiesWithoutUndo();
        GraphicsSettings.defaultRenderPipeline = pipeline;
        QualitySettings.renderPipeline = pipeline;
        QualitySettings.vSyncCount = 1;
        QualitySettings.shadows = ShadowQuality.All;
        PlayerSettings.companyName = "AnonTokyoPrototype";
        PlayerSettings.productName = "Paper Shop - 2.5D Study";
        PlayerSettings.defaultScreenWidth = 1360;
        PlayerSettings.defaultScreenHeight = 850;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.resizableWindow = true;
        PlayerSettings.runInBackground = true;
        PlayerSettings.colorSpace = ColorSpace.Linear;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var root = new GameObject("Paper Shop Preview");
        var demo = root.AddComponent<PaperShopDemo>();
        demo.litShader = Shader.Find("Universal Render Pipeline/Lit");
        if (demo.litShader == null) throw new InvalidOperationException("URP Lit shader missing.");
        Directory.CreateDirectory("Assets/Resources/Materials");
        var referencePath = "Assets/Resources/Materials/CardReference.mat";
        var reference = AssetDatabase.LoadAssetAtPath<Material>(referencePath);
        if (reference == null)
        {
            reference = new Material(demo.litShader);
            AssetDatabase.CreateAsset(reference, referencePath);
        }
        reference.SetFloat("_AlphaClip", 1);
        reference.SetFloat("_Cutoff", 0.4f);
        reference.SetFloat("_Cull", 0);
        reference.EnableKeyword("_ALPHATEST_ON");
        reference.renderQueue = (int)RenderQueue.AlphaTest;
        reference.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/CardTextures/idle.png");
        EditorUtility.SetDirty(reference);
        root.AddComponent<PreviewVerification>();
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/PaperShop.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/PaperShop.unity", true) };
        AssetDatabase.SaveAssets();

        var project = Directory.GetParent(Application.dataPath).FullName;
        var output = Path.GetFullPath(Path.Combine(project, "..", "Windows", "PaperShop.exe"));
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/PaperShop.unity" },
            locationPathName = output,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        });
        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException("Build failed: " + report.summary.result + "; errors=" + report.summary.totalErrors);
        Debug.Log("PAPER_SHOP_BUILD_OK " + output);
    }
}
