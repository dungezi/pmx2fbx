using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class UnityPackageValidation
{
    private const string PendingImport = "PMX2FBX.PackageImportPending";
    private static Type Dependency => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Pmx2Fbx.FbxDependency", false)).FirstOrDefault(t => t != null);
    private static bool Ready => Dependency != null && (bool)Dependency.GetProperty("IsReady").GetValue(null);
    private static string Archive
    {
        get
        {
            var args = Environment.GetCommandLineArgs();
            int flag = Array.IndexOf(args, "-pmx2fbxPackageOutput");
            if (flag < 0 || flag + 1 >= args.Length) throw new ArgumentException("Missing unitypackage output path.");
            return args[flag + 1];
        }
    }
    public static void Export()
    {
        try
        {
            if (!AssetDatabase.IsValidFolder("Assets/PMX2FBX")) throw new Exception("Missing staged Assets installation.");
            if (!Ready) throw new Exception("FBX dependency unavailable in export project.");
            Directory.CreateDirectory(Path.GetDirectoryName(Archive));
            AssetDatabase.ExportPackage("Assets/PMX2FBX", Archive, ExportPackageOptions.Recurse);
            if (!File.Exists(Archive) || new FileInfo(Archive).Length == 0) throw new Exception("Archive export failed.");
            Debug.Log("PMX2FBX_UNITYPACKAGE_EXPORT_PASS: " + Archive);
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
    public static void VerifyWithoutDependency()
    {
        try
        {
            if (Dependency == null) throw new Exception("Imported assembly not loaded.");
            if (Dependency.Assembly.GetType("Pmx2Fbx.Core.XpsReader") == null || Dependency.Assembly.GetType("Pmx2Fbx.XpsConverter") == null) throw new Exception("XPS extension missing in imported package.");
            if (Ready) throw new Exception("Clean import test unexpectedly includes FBX Exporter.");
            if (!AssetDatabase.IsValidFolder("Assets/PMX2FBX/Editor")) throw new Exception("Imported Editor files missing.");
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/PMX2FBX/Shaders/PcToon.shader");
            if (!shader || ShaderUtil.ShaderHasError(shader)) throw new Exception("Imported shader invalid.");
            var windowType = Dependency.Assembly.GetType("Pmx2Fbx.ConverterWindow", true);
            var window = EditorWindow.GetWindow(windowType);
            window.Close();
            windowType.GetMethod("OpenXps").Invoke(null, null);
            EditorWindow.GetWindow(windowType).Close();
            File.WriteAllText("unitypackage-import-result.json", "{\"passed\":true,\"dependencyAbsent\":true,\"editorWindowOpened\":true,\"xpsExtensionLoaded\":true,\"unity\":\"" + Application.unityVersion + "\"}");
            Debug.Log("PMX2FBX_UNITYPACKAGE_CLEAN_IMPORT_PASS");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
    public static void Import()
    {
        SessionState.SetBool(PendingImport, true);
        SessionState.SetString(PendingImport + ".start", DateTime.UtcNow.Ticks.ToString());
        EditorApplication.update += FinishImport;
        AssetDatabase.ImportPackage(Archive, false);
    }
    [InitializeOnLoadMethod]
    private static void ResumeImport()
    {
        if (SessionState.GetBool(PendingImport, false)) EditorApplication.update += FinishImport;
    }
    private static void FinishImport()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (Dependency != null && Dependency.Assembly.GetType("Pmx2Fbx.XpsConverter") != null && AssetDatabase.IsValidFolder("Assets/PMX2FBX/Editor"))
        {
            EditorApplication.update -= FinishImport;
            SessionState.SetBool(PendingImport, false);
            VerifyWithoutDependency();
        }
        else if (long.TryParse(SessionState.GetString(PendingImport + ".start", ""), out long start)
            && DateTime.UtcNow.Ticks - start > TimeSpan.FromMinutes(2).Ticks)
        {
            SessionState.SetBool(PendingImport, false);
            Debug.LogError("Unitypackage import timed out before assembly reload.");
            EditorApplication.Exit(1);
        }
    }
}
