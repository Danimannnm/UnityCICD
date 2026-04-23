using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public class BuildScript
{
    private static string GetArg(string name)
    {
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name)
                return args[i + 1];
        }
        return null;
    }

    private static string GetEditorLogPath()
    {
#if UNITY_EDITOR_WIN
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Unity", "Editor", "Editor.log");
#elif UNITY_EDITOR_OSX
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Library", "Logs", "Unity", "Editor.log");
#else
        // Linux / CI container
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), ".config", "unity3d", "Editor.log");
#endif
    }

    public static void BuildAndroid()
    {
        // Step 1: Log start banner
        Debug.Log("==== BuildAndroid start ====");
        Debug.Log($"Time: {DateTime.Now}");

        // Step 2: Resolve format
        // Priority order:
        //   1. ANDROID_EXPORT_TYPE env var (set by GameCI unity-builder@v4 when androidExportType input is provided)
        //   2. EditorUserBuildSettings.buildAppBundle (local / other invocations)
        string exportType = Environment.GetEnvironmentVariable("ANDROID_EXPORT_TYPE");
        bool buildAab;
        if (!string.IsNullOrEmpty(exportType))
        {
            buildAab = string.Equals(exportType, "androidAppBundle", StringComparison.OrdinalIgnoreCase);
            Debug.Log($"Format source: ANDROID_EXPORT_TYPE env var = '{exportType}'");
        }
        else
        {
            buildAab = EditorUserBuildSettings.buildAppBundle;
            Debug.Log($"Format source: EditorUserBuildSettings.buildAppBundle = {buildAab}");
        }

        // Apply resolved format to EditorUserBuildSettings so BuildPipeline picks it up
        EditorUserBuildSettings.buildAppBundle = buildAab;

        string extension = buildAab ? "aab" : "apk";
        Debug.Log($"Building {extension.ToUpper()}");

        // Step 3: Apply version overrides (optional)
        var customVersion = GetArg("-customBuildVersion");
        if (!string.IsNullOrEmpty(customVersion))
        {
            PlayerSettings.bundleVersion = customVersion;
            Debug.Log($"Version override: {customVersion}");
        }

        var versionCodeStr = GetArg("-androidVersionCode");
        if (!string.IsNullOrEmpty(versionCodeStr) && int.TryParse(versionCodeStr, out var versionCode))
        {
            PlayerSettings.Android.bundleVersionCode = versionCode;
            Debug.Log($"Version code override: {versionCode}");
        }

        // Step 4: Apply signing (only if all required envs are present)
        string keystoreName = Environment.GetEnvironmentVariable("ANDROID_KEYSTORE_NAME");
        string keystorePass = Environment.GetEnvironmentVariable("ANDROID_KEYSTORE_PASS");
        string keyaliasName = Environment.GetEnvironmentVariable("ANDROID_KEYALIAS_NAME");
        string keyaliasPass = Environment.GetEnvironmentVariable("ANDROID_KEYALIAS_PASS");

        if (!string.IsNullOrEmpty(keystoreName) && 
            !string.IsNullOrEmpty(keystorePass) && 
            !string.IsNullOrEmpty(keyaliasName) && 
            !string.IsNullOrEmpty(keyaliasPass))
        {
            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = keystoreName;
            PlayerSettings.Android.keystorePass = keystorePass;
            PlayerSettings.Android.keyaliasName = keyaliasName;
            PlayerSettings.Android.keyaliasPass = keyaliasPass;
            Debug.Log($"Signing: enabled (keystore={Path.GetFileName(keystoreName)})");
        }
        else
        {
            Debug.Log("Signing: skipped (no keystore env)");
        }

        // Step 5: Collect enabled scenes
        var scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => s.path)
            .ToArray();

        if (scenes.Length == 0)
        {
            Debug.LogError("No enabled scenes in Build Settings");
            EditorApplication.Exit(1);
            return;
        }

        Debug.Log($"Scenes to build: {string.Join(", ", scenes)}");

        // Step 6: Prepare output path
        string outDir = Path.Combine(Directory.GetCurrentDirectory(), "Builds", "Android");
        Directory.CreateDirectory(outDir);
        string outPath = Path.Combine(outDir, $"{PlayerSettings.productName}.{extension}");
        Debug.Log($"Output path: {outPath}");

        // Step 7: Configure build options
        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outPath,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = BuildOptions.None
        };

        // Step 8: Invoke build
        Debug.Log("Starting build...");
        BuildReport report = BuildPipeline.BuildPlayer(options);

        // Step 9: Assert success
        var summary = report.summary;
        Debug.Log($"Build {summary.result} — output: {summary.outputPath}, size: {summary.totalSize} bytes, duration: {summary.totalTime}");

        if (summary.result != BuildResult.Succeeded)
        {
            Debug.LogError($"Build failed with result: {summary.result}");
            
            // Log all errors from build steps
            foreach (var step in report.steps)
            {
                foreach (var message in step.messages)
                {
                    if (message.type == LogType.Error || message.type == LogType.Exception)
                    {
                        Debug.LogError($"[{step.name}] {message.content}");
                    }
                }
            }

            // Dump Editor.log to CI output for detailed diagnostics
            try
            {
                var editorLog = GetEditorLogPath();
                Console.WriteLine($"Editor.log path: {editorLog}");
                
                if (File.Exists(editorLog))
                {
                    var lines = File.ReadAllLines(editorLog);
                    int tail = Math.Min(300, lines.Length);
                    Console.WriteLine("===== BEGIN Editor.log (last 300 lines) =====");
                    for (int i = lines.Length - tail; i < lines.Length; i++)
                    {
                        Console.WriteLine(lines[i]);
                    }
                    Console.WriteLine("===== END Editor.log =====");
                }
                else
                {
                    Console.WriteLine("Editor.log not found.");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Failed to read Editor.log: {e}");
            }
            
            EditorApplication.Exit(1);
            return;
        }

        Debug.Log("==== BuildAndroid complete ====");
        EditorApplication.Exit(0);
    }
}
