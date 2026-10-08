using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace Pmx2Fbx
{
    // Assets installations can compile before the exporter package is installed.
    public static class FbxDependency
    {
        private static AddRequest pending;
        public static bool IsInstalling => pending != null;
        public static string LastError { get; private set; }
        private static MethodInfo ExportMethod => AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("UnityEditor.Formats.Fbx.Exporter.ModelExporter", false))
            .Where(t => t != null)
            .Select(t => t.GetMethod("ExportObject", BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(string), typeof(UnityEngine.Object) }, null))
            .FirstOrDefault(m => m != null);
        public static bool IsReady => ExportMethod != null;

        [MenuItem("Tools/PMX to FBX/安装 FBX Exporter 依赖")]
        public static void Install()
        {
            if (IsReady || IsInstalling) return;
            LastError = null;
            try
            {
                pending = Client.Add("com.unity.formats.fbx@4.2.1");
                EditorApplication.update += Poll;
            }
            catch (Exception e) { LastError = e.Message; pending = null; Debug.LogError("PMX2FBX: " + LastError); }
        }
        private static void Poll()
        {
            if (pending == null || !pending.IsCompleted) return;
            EditorApplication.update -= Poll;
            if (pending.Status == StatusCode.Failure)
            {
                LastError = pending.Error?.message ?? "依赖安装失败，请检查 Unity Package Manager。";
                Debug.LogError("PMX2FBX: " + LastError);
            }
            else Debug.Log("PMX2FBX: FBX Exporter 安装完成，等待 Unity 编译后即可转换。");
            pending = null;
        }
        public static string Export(string absolutePath, GameObject root)
        {
            var method = ExportMethod;
            if (method == null) throw new InvalidOperationException("缺少 Unity FBX Exporter，请在转换窗口安装依赖。");
            try { return method.Invoke(null, new object[] { absolutePath, root }) as string; }
            catch (TargetInvocationException e) { throw new InvalidOperationException("FBX 导出失败：" + (e.InnerException?.Message ?? e.Message), e.InnerException ?? e); }
        }
    }
}
