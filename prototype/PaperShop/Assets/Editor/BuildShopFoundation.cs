using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using AnonTokyo.Shop;

public static class BuildShopFoundation
{
    const string ScenePath = "Assets/Scenes/ShopFoundation.unity";
    const string CatalogPath = "Assets/Resources/Shop/DefaultCatalog.asset";

    [MenuItem("Anon Tokyo/Open Foundation Scene")]
    public static void Prepare()
    {
        ShopSettingsChecks.ConfigureAudioResources();
        Directory.CreateDirectory("Assets/Resources/Shop");
        Directory.CreateDirectory("Assets/Scenes");
        var catalog = AssetDatabase.LoadAssetAtPath<ShopCatalog>(CatalogPath);
        if (catalog == null)
        {
            catalog = ShopCatalog.CreateDefault();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }
        ShopCatalog.EnsureStaffDefinitions(catalog);
        ShopUserAssetImport.Import(catalog);
        ShopProductAssetImport.Import(catalog);
        ShopEnvironmentImport.Import(catalog);
        EditorUtility.SetDirty(catalog);
        if (catalog.Find("fitting") == null)
        {
            // One-time reference-layout update. Keep any user-assigned model prefabs.
            var defaults = ShopCatalog.CreateDefault();
            foreach (var definition in defaults.facilities)
            {
                var existing = catalog.Find(definition.id);
                if (existing == null) catalog.facilities.Add(definition);
                else
                {
                    existing.width=definition.width; existing.depth=definition.depth;
                    existing.serviceOffset=definition.serviceOffset; existing.title=definition.title;
                    if(existing.id=="stage")existing.color=definition.color;
                }
            }
            UnityEngine.Object.DestroyImmediate(defaults);
            EditorUtility.SetDirty(catalog);
        }
        if (catalog.layoutDefinitionVersion < 4)
        {
            // Apply the geometry contract once. Keep the user's imported prefab and all other catalog content.
            var warehouse = catalog.Find("warehouse");
            if (warehouse == null) throw new InvalidOperationException("Warehouse catalog definition is missing.");
            warehouse.width = 3;
            warehouse.depth = 4;
            warehouse.serviceOffset = new Vector2Int(-1, 3);
            warehouse.title = "右后墙仓库";
            catalog.layoutDefinitionVersion = 4;
            EditorUtility.SetDirty(catalog);
        }
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) throw new InvalidOperationException("URP Lit is missing.");
        // Preserve the cutout variant used by both the map and the earlier visual sample.
        var reference = AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/Materials/CardReference.mat");
        if (reference == null) throw new InvalidOperationException("CardReference.mat is missing.");
        reference.SetFloat("_AlphaClip", 1);
        reference.SetFloat("_Cull", 0);
        reference.EnableKeyword("_ALPHATEST_ON");
        EditorUtility.SetDirty(reference);

        if (File.Exists(ScenePath)) EditorSceneManager.OpenScene(ScenePath);
        else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var app = UnityEngine.Object.FindFirstObjectByType<ShopApp>();
        if (app == null) app = new GameObject("Anon Tokyo - Shop Foundation").AddComponent<ShopApp>();
        app.catalog = catalog;
        app.litShader = shader;
        if (app.GetComponent<ShopVerification>() == null) app.gameObject.AddComponent<ShopVerification>();
        if (app.GetComponent<ShopCrowdVerification>() == null) app.gameObject.AddComponent<ShopCrowdVerification>();
        if (app.GetComponent<ShopMechanicsVerification>() == null) app.gameObject.AddComponent<ShopMechanicsVerification>();
        if (app.GetComponent<ShopOperationsVerification>() == null) app.gameObject.AddComponent<ShopOperationsVerification>();
        if (app.GetComponent<ShopRenovationVerification>() == null) app.gameObject.AddComponent<ShopRenovationVerification>();
        if (app.GetComponent<ShopSettingsVerification>() == null) app.gameObject.AddComponent<ShopSettingsVerification>();
        if (app.GetComponent<ShopOfflineVerification>() == null) app.gameObject.AddComponent<ShopOfflineVerification>();
        if (app.GetComponent<ShopGrowthVerification>() == null) app.gameObject.AddComponent<ShopGrowthVerification>();
        if (app.GetComponent<ShopSurfaceVerification>() == null) app.gameObject.AddComponent<ShopSurfaceVerification>();
        if (app.GetComponent<ShopPerformanceVerification>() == null) app.gameObject.AddComponent<ShopPerformanceVerification>();
        EditorUtility.SetDirty(app);
        EditorSceneManager.SaveScene(app.gameObject.scene, ScenePath);
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Anon Tokyo/Build Windows Foundation")]
    public static void Build()
    {
        Prepare();
        ShopReleaseSettings.Configure();
        var pipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
        if (pipeline == null) throw new InvalidOperationException("PC URP pipeline is missing.");
        var pipelineSettings = new SerializedObject(pipeline);
        pipelineSettings.FindProperty("m_MSAA").intValue = 4;
        pipelineSettings.FindProperty("m_MainLightShadowmapResolution").intValue = 4096;
        pipelineSettings.FindProperty("m_ShadowDistance").floatValue = 45;
        pipelineSettings.FindProperty("m_ShadowNormalBias").floatValue = .16f;
        pipelineSettings.FindProperty("m_ShadowDepthBias").floatValue = .08f;
        pipelineSettings.ApplyModifiedPropertiesWithoutUndo();
        var data = new SerializedObject(AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/Settings/PC_Renderer.asset"));
        data.FindProperty("m_RenderingMode").intValue = 0;
        data.ApplyModifiedPropertiesWithoutUndo();
        GraphicsSettings.defaultRenderPipeline = pipeline;
        QualitySettings.renderPipeline = pipeline;
        QualitySettings.vSyncCount = 1;
        QualitySettings.shadows = ShadowQuality.All;
        PlayerSettings.companyName = "AnonTokyoPrototype";
        PlayerSettings.productName = "AnonTokyo Shop Foundation";
        PlayerSettings.bundleVersion = "1.0";
        PlayerSettings.defaultScreenWidth = 1360;
        PlayerSettings.defaultScreenHeight = 850;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.resizableWindow = true;
        PlayerSettings.runInBackground = true;
        PlayerSettings.colorSpace = ColorSpace.Linear;
        var project = Directory.GetParent(Application.dataPath).FullName;
        var outputRoot = Path.GetFullPath(Path.Combine(project,".."));
        ShopSession.TestProfile = false;
        var ruleFailures=new System.Collections.Generic.List<string>();
        try { ShopBalanceChecks.Run(catalogForChecks(),Path.Combine(outputRoot,"MapPreview","BalanceChecks")); } catch(Exception ex) { ruleFailures.Add(ex.Message);Debug.LogError(ex.Message); }
        try { ShopUserAssetChecks.Run(catalogForChecks(),Path.Combine(outputRoot,"MapPreview","UserAssetChecks")); } catch(Exception ex) { ruleFailures.Add(ex.Message);Debug.LogError(ex.Message); }
        try { ShopLogicChecks.Run(AssetDatabase.LoadAssetAtPath<ShopCatalog>(CatalogPath),Path.Combine(outputRoot,"MapPreview","LogicChecks")); } catch(Exception ex) { ruleFailures.Add(ex.Message);Debug.LogError(ex.Message); }
        try { ShopEconomyChecks.Run(AssetDatabase.LoadAssetAtPath<ShopCatalog>(CatalogPath), Path.Combine(outputRoot,"MapPreview","EconomyChecks")); } catch(Exception ex) { ruleFailures.Add(ex.Message);Debug.LogError(ex.Message); }
        try { ShopSessionChecks.Run(AssetDatabase.LoadAssetAtPath<ShopCatalog>(CatalogPath), Path.Combine(outputRoot,"MapPreview","SessionChecks")); } catch(Exception ex) { ruleFailures.Add(ex.Message);Debug.LogError(ex.Message); }
        try { ShopTradingChecks.Run(AssetDatabase.LoadAssetAtPath<ShopCatalog>(CatalogPath), Path.Combine(outputRoot,"MapPreview","TradingChecks")); } catch(Exception ex) { ruleFailures.Add(ex.Message);Debug.LogError(ex.Message); }
        try { ShopUpgradeChecks.Run(AssetDatabase.LoadAssetAtPath<ShopCatalog>(CatalogPath), Path.Combine(outputRoot,"MapPreview","UpgradeChecks")); } catch(Exception ex) { ruleFailures.Add(ex.Message);Debug.LogError(ex.Message); }
        try { ShopSalesFlowChecks.Run(AssetDatabase.LoadAssetAtPath<ShopCatalog>(CatalogPath), Path.Combine(outputRoot,"MapPreview","SalesFlowChecks")); } catch(Exception ex) { ruleFailures.Add(ex.Message);Debug.LogError(ex.Message); }
        try { ShopMultiBasketChecks.Run(AssetDatabase.LoadAssetAtPath<ShopCatalog>(CatalogPath), Path.Combine(outputRoot,"MapPreview","MultiBasketChecks")); } catch(Exception ex) { ruleFailures.Add(ex.Message);Debug.LogError(ex.Message); }
        try { ShopBatchRestockChecks.Run(catalogForChecks(),Path.Combine(outputRoot,"MapPreview","BatchRestockChecks")); } catch(Exception ex) { ruleFailures.Add(ex.Message);Debug.LogError(ex.Message); }
        try { ShopCustomerFlowChecks.Run(catalogForChecks(),Path.Combine(outputRoot,"MapPreview","CustomerFlowChecks")); } catch(Exception ex) { ruleFailures.Add(ex.Message);Debug.LogError(ex.Message); }
        try { CheckoutPositionChecks.Run(catalogForChecks(),Path.Combine(outputRoot,"MapPreview","CheckoutPositionChecks")); } catch(Exception ex) { ruleFailures.Add(ex.Message);Debug.LogError(ex.Message); }
        try { ShopRestockSettingsChecks.Run(catalogForChecks(),Path.Combine(outputRoot,"MapPreview","RestockSettingsChecks")); } catch(Exception ex) { ruleFailures.Add(ex.Message);Debug.LogError(ex.Message); }
        try { ShopOrderChecks.Run(catalogForChecks(),Path.Combine(outputRoot,"MapPreview","OrderChecks")); } catch(Exception ex) { ruleFailures.Add(ex.Message);Debug.LogError(ex.Message); }
        try { ShopClockChecks.Run(Path.Combine(outputRoot,"MapPreview","ClockChecks")); } catch(Exception ex) { ruleFailures.Add(ex.Message);Debug.LogError(ex.Message); }
        try { ShopFixtureChecks.Run(catalogForChecks(),Path.Combine(outputRoot,"MapPreview","FixtureChecks")); } catch(Exception ex) { ruleFailures.Add(ex.Message);Debug.LogError(ex.Message); }
        try { ShopSettingsChecks.Run(Path.Combine(outputRoot,"MapPreview","SettingsChecks")); } catch(Exception ex) { ruleFailures.Add(ex.Message);Debug.LogError(ex.Message); }
        try { ShopOfflineChecks.Run(catalogForChecks(),Path.Combine(outputRoot,"MapPreview","OfflineChecks")); } catch(Exception ex) { ruleFailures.Add(ex.Message);Debug.LogError(ex.Message); }
        try { ShopExpansionChecks.Run(catalogForChecks(),Path.Combine(outputRoot,"MapPreview","ExpansionChecks")); } catch(Exception ex) { ruleFailures.Add(ex.Message);Debug.LogError(ex.Message); }
        try { ShopGrowthChecks.Run(catalogForChecks(),Path.Combine(outputRoot,"MapPreview","GrowthChecks")); } catch(Exception ex) { ruleFailures.Add(ex.Message);Debug.LogError(ex.Message); }
        try { ShopPerformanceChecks.Run(catalogForChecks(),Path.Combine(outputRoot,"MapPreview","PerformanceChecks")); } catch(Exception ex) { ruleFailures.Add(ex.Message);Debug.LogError(ex.Message); }
        try { ShopStaffExpansionChecks.Run(catalogForChecks(),Path.Combine(outputRoot,"MapPreview","StaffExpansionChecks")); } catch(Exception ex) { ruleFailures.Add(ex.Message);Debug.LogError(ex.Message); }
        try { ShopSurfaceCatalogChecks.Run(catalogForChecks(),Path.Combine(outputRoot,"MapPreview","SurfaceCatalogChecks")); } catch(Exception ex) { ruleFailures.Add(ex.Message);Debug.LogError(ex.Message); }
        try { ShopProfileArtChecks.Run(catalogForChecks(),Path.Combine(outputRoot,"MapPreview","ProfileArtChecks")); } catch(Exception ex) { ruleFailures.Add(ex.Message);Debug.LogError(ex.Message); }
        try { ShopEnvironmentChecks.Run(catalogForChecks(),Path.Combine(outputRoot,"MapPreview","EnvironmentChecks")); } catch(Exception ex) { ruleFailures.Add(ex.Message);Debug.LogError(ex.Message); }
        if(ruleFailures.Count>0)throw new InvalidOperationException(string.Join("\n",ruleFailures));
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath,true) };
        AssetDatabase.SaveAssets();
        var buildArgs=Environment.GetCommandLineArgs();
        int outputIndex=Array.IndexOf(buildArgs,"-shopOutputFolder");
        string folder=outputIndex>=0&&outputIndex+1<buildArgs.Length?buildArgs[outputIndex+1]:"ShopWindows";
        if(!System.Text.RegularExpressions.Regex.IsMatch(folder,"^[A-Za-z0-9_-]+$"))throw new InvalidOperationException("Invalid build folder");
        var output = Path.Combine(outputRoot,folder,"AnonTokyo.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath }, locationPathName = output,
            target = BuildTarget.StandaloneWindows64, options = BuildOptions.CompressWithLz4HC
        });
        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException("Foundation build failed: " + report.summary.totalErrors + " errors.");
        Debug.Log("SHOP_FOUNDATION_BUILD_OK " + output);
    }
    static ShopCatalog catalogForChecks()=>AssetDatabase.LoadAssetAtPath<ShopCatalog>(CatalogPath);
}
