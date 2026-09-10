using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// 命令行构建入口：IL2CPP Windows x64（与部署版 MajdataViewX 一致）。
/// 用法：Unity.exe -batchmode -nographics -quit -projectPath <proj> -executeMethod BuildScript.BuildWindows64 -buildOutPath <dir> -logFile <log>
/// </summary>
public static class BuildScript
{
    public static void BuildWindows64()
    {
        var args = Environment.GetCommandLineArgs();
        var outPath = "Build/Win64";
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-buildOutPath")
                outPath = args[i + 1];
        }

        // 桌面身份（persistentDataPath 依赖它；Android 构建会改 productName/companyName，必须恢复）
        PlayerSettings.companyName = "bbben";
        PlayerSettings.productName = "MajdataViewX";

        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.IL2CPP);
        PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Standalone, ApiCompatibilityLevel.NET_Standard);

        var options = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/Game.unity" },
            locationPathName = outPath + "/MajdataViewX.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        };

        var report = BuildPipeline.BuildPlayer(options);
        Debug.Log("BUILD RESULT: " + report.summary.result + " size=" + report.summary.totalSize);
        if (report.summary.result != BuildResult.Succeeded)
            throw new Exception("Build failed: " + report.summary.result + " errors=" + report.summary.totalErrors);
    }

    /// <summary>
    /// 命令行构建入口：Android IL2CPP ARM64（移动端独立运行版）。
    /// 用法：Unity.exe -batchmode -nographics -quit -projectPath <proj> -executeMethod BuildScript.BuildAndroid -buildOutPath <dir> -logFile <log>
    /// </summary>
    public static void BuildAndroid()
    {
        var args = Environment.GetCommandLineArgs();
        var outPath = "Build/Android";
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-buildOutPath")
                outPath = args[i + 1];
        }

        EnsureBootstrapScene();

        // 外部工具路径（批量构建环境通过环境变量注入）
        var sdk = Environment.GetEnvironmentVariable("MAJDATA_ANDROID_SDK");
        var ndk = Environment.GetEnvironmentVariable("MAJDATA_ANDROID_NDK");
        var jdk = Environment.GetEnvironmentVariable("MAJDATA_ANDROID_JDK");
        if (!string.IsNullOrEmpty(sdk)) EditorPrefs.SetString("AndroidSdkRoot", sdk);
        if (!string.IsNullOrEmpty(ndk)) EditorPrefs.SetString("AndroidNdkRootR27C", ndk); // Unity 6000.3 需要 NDK r27c
        if (!string.IsNullOrEmpty(jdk)) EditorPrefs.SetString("Jdk17Path", jdk);          // JDK 17 专用键

        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        // ARM64 真机 + x86_64 模拟器双 ABI
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64 | AndroidArchitecture.X86_64;
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.majdata.viewx.mobile");
        PlayerSettings.productName = "MajdataX Mobile";
        PlayerSettings.companyName = "Majdata";
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
        PlayerSettings.allowedAutorotateToPortrait = true;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = true;
        PlayerSettings.allowedAutorotateToLandscapeLeft = false;
        PlayerSettings.allowedAutorotateToLandscapeRight = false;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
        PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Android, ApiCompatibilityLevel.NET_Standard);
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, true);
        // 自定义主 Manifest（Assets/Plugins/Android/AndroidManifest.xml：自定义 launcher 活动 + SAF 音频选择）
        // PlayerSettings.Android 没有公开该开关，直接写序列化属性
        var psSo = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
        var customManifest = psSo.FindProperty("useCustomMainManifest");
        if (customManifest is not null)
        {
            customManifest.boolValue = true;
            psSo.ApplyModifiedProperties();
        }
        // 生成符号包（symbols.zip），真机崩溃 tombstones 可用 addr2line 精确还原调用栈
        EditorUserBuildSettings.androidCreateSymbols = UnityEditor.AndroidCreateSymbols.Debugging;

        var options = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/Bootstrap.unity", "Assets/Scenes/Game.unity" },
            locationPathName = outPath + "/MajdataX-Mobile.apk",
            target = BuildTarget.Android,
            options = BuildOptions.None
        };

        var report = BuildPipeline.BuildPlayer(options);
        Debug.Log("BUILD RESULT: " + report.summary.result + " size=" + report.summary.totalSize);
        if (report.summary.result != BuildResult.Succeeded)
            throw new Exception("Build failed: " + report.summary.result + " errors=" + report.summary.totalErrors);
    }

    /// <summary>创建 Android 首启 Bootstrap 场景（空场景 + MobileBootstrap 组件，负责解压资源后进入 Game）。</summary>
    private static void EnsureBootstrapScene()
    {
        const string path = "Assets/Scenes/Bootstrap.unity";
        if (System.IO.File.Exists(path)) return;

        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
            UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
            UnityEditor.SceneManagement.NewSceneMode.Single);
        var go = new GameObject("MobileBootstrap");
        go.AddComponent<MajdataViewX.Mobile.MobileBootstrap>();
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, path);
        Debug.Log("Bootstrap scene created: " + path);
    }

    /// <summary>
    /// 桌面调试构建：注入 MAJDATA_MOBILE_DEBUG 定义，在 Windows 上复现移动端流程
    /// （分屏 + 编辑器 UI），便于本地定位崩溃。
    /// </summary>
    public static void BuildWindows64MobileDebug()
    {
        var args = Environment.GetCommandLineArgs();
        var outPath = "Build/Win64MobileDebug";
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-buildOutPath")
                outPath = args[i + 1];
        }

        PlayerSettings.companyName = "bbben";
        PlayerSettings.productName = "MajdataViewX";

        var defines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone);
        if (!defines.Contains("MAJDATA_MOBILE_DEBUG"))
        {
            defines = string.IsNullOrEmpty(defines) ? "MAJDATA_MOBILE_DEBUG" : defines + ";MAJDATA_MOBILE_DEBUG";
            PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Standalone, defines);
        }

        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.IL2CPP);
        PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Standalone, ApiCompatibilityLevel.NET_Standard);

        var options = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/Game.unity" },
            locationPathName = outPath + "/MajdataViewX.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        };

        var report = BuildPipeline.BuildPlayer(options);
        Debug.Log("BUILD RESULT: " + report.summary.result + " size=" + report.summary.totalSize);
        if (report.summary.result != BuildResult.Succeeded)
            throw new Exception("Build failed: " + report.summary.result + " errors=" + report.summary.totalErrors);
    }
}
