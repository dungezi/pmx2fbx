using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Pmx2Fbx.Core;
using UnityEditor;
using UnityEngine;

namespace Pmx2Fbx
{
    public sealed class ConverterWindow : EditorWindow
    {
        [SerializeField] private string sourcePath = "", outputPath = "Assets/PMXConverted";
        [SerializeField] private ConversionOptions options = new ConversionOptions();
        private PmxModel model;
        private ConversionReport report;
        private Vector2 scroll;
        private bool mapping;
        [SerializeField] private int format;
        [MenuItem("Tools/PMX to FBX/转换 PMX 模型")]
        public static void Open() => GetWindow<ConverterWindow>("模型 → FBX").SetFormat(0);
        [MenuItem("Tools/PMX to FBX/转换 XPS 模型（附属功能）")]
        public static void OpenXps() => GetWindow<ConverterWindow>("模型 → FBX").SetFormat(1);
        private void SetFormat(int value)
        {
            if (value == format) return;
            format = value; model = null; report = null; sourcePath = "";
            outputPath = value == 0 ? "Assets/PMXConverted" : "Assets/XPSConverted";
            options = new ConversionOptions { Scale = value == 0 ? .08f : 1f, VrChat = value == 0 };
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            SetFormat(EditorGUILayout.Popup("源模型格式", format, new[] { "PMX → FBX", "XPS → FBX（附属功能）" }));
            if (!FbxDependency.IsReady)
            {
                EditorGUILayout.HelpBox("首次使用需要 Unity 官方 FBX Exporter。点击下方按钮安装，完成后可直接转换。", MessageType.Info);
                using (new EditorGUI.DisabledScope(FbxDependency.IsInstalling))
                    if (GUILayout.Button(FbxDependency.IsInstalling ? "正在安装 FBX Exporter…" : "安装 FBX Exporter")) FbxDependency.Install();
                if (!string.IsNullOrEmpty(FbxDependency.LastError)) EditorGUILayout.HelpBox(FbxDependency.LastError, MessageType.Error);
                if (FbxDependency.IsInstalling) Repaint();
            }
            EditorGUILayout.HelpBox(format == 0 ? "转换 PMX 2.0 / 2.1；保留骨骼蒙皮、主纹理与顶点 / 组表情。VRChat 模式面向 PC 人形头像。" : "支持 .xps、.mesh 和 .mesh.ascii；保留骨骼蒙皮、顶点颜色、UV 和主贴图。XPS 不提供 BlendShape；可选 VRChat PC 人形适配。", MessageType.Info);
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck(); sourcePath = EditorGUILayout.TextField("模型文件", sourcePath);
            if (EditorGUI.EndChangeCheck()) { model = null; options.BoneOverrides.Clear(); }
            if (GUILayout.Button("浏览", GUILayout.Width(55)))
            {
                string selected = EditorUtility.OpenFilePanelWithFilters("选择模型", "", format == 0 ? new[] { "PMX", "pmx" } : new[] { "XPS / XNALara", "xps,mesh,ascii" });
                if (!string.IsNullOrEmpty(selected)) { sourcePath = selected; model = null; options.BoneOverrides.Clear(); }
            }
            EditorGUILayout.EndHorizontal();
            var drag = Event.current;
            if ((drag.type == EventType.DragUpdated || drag.type == EventType.DragPerform) && DragAndDrop.paths.Any(ModelReader.IsSupported))
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                if (drag.type == EventType.DragPerform)
                { DragAndDrop.AcceptDrag(); string selected = DragAndDrop.paths.First(ModelReader.IsSupported); SetFormat(ModelReader.IsXps(selected) ? 1 : 0); sourcePath = selected; model = null; options.BoneOverrides.Clear(); }
                drag.Use();
            }
            outputPath = EditorGUILayout.TextField("输出目录", outputPath);
            options.VrChat = EditorGUILayout.ToggleLeft("VRChat PC 适配（Humanoid + 头像 Prefab）", options.VrChat);
            if (options.VrChat)
            {
                options.NormalizeHeight = EditorGUILayout.Toggle("自动调整身高", options.NormalizeHeight);
                if (options.NormalizeHeight) options.Height = EditorGUILayout.Slider("目标身高（米）", options.Height, .2f, 3f);
                else options.Scale = EditorGUILayout.FloatField("模型单位 → 米", options.Scale);
                options.TPose = EditorGUILayout.Toggle("转换为 T Pose", options.TPose);
                options.AddDescriptor = EditorGUILayout.Toggle("添加 Avatar Descriptor", options.AddDescriptor);
                EditorGUILayout.LabelField("VRChat Avatars SDK", VrChatBridge.DescriptorType == null ? "未安装（仍可导出 FBX）" : "已检测到");
                EditorGUILayout.HelpBox("骨骼缺失或 Humanoid 验证失败会明确报错。模型减面、PhysBones、完整音素口型与眨眼需要另行配置。", MessageType.None);
            }
            else options.Scale = EditorGUILayout.FloatField("模型单位 → 米", options.Scale);
            options.Rotate180 = EditorGUILayout.Toggle("额外旋转 180°（修正朝向）", options.Rotate180);
            if (format == 0) options.IncludeMorphs = EditorGUILayout.Toggle("导出 BlendShape 表情", options.IncludeMorphs);
            using (new EditorGUI.DisabledScope(!File.Exists(sourcePath)))
            {
                if (GUILayout.Button("读取模型 / 查看骨骼映射")) Read();
                if (model != null)
                {
                    EditorGUILayout.LabelField(model.Name + "  ·  " + model.Vertices.Length + " 顶点  ·  " + model.Indices.Length / 3 + " 三角面");
                    if (options.VrChat)
                    {
                        mapping = EditorGUILayout.Foldout(mapping, "骨骼映射（自动匹配，可手动修改）", true);
                        if (mapping) DrawMappings();
                    }
                }
                GUILayout.Space(8);
                using (new EditorGUI.DisabledScope(!FbxDependency.IsReady))
                if (GUILayout.Button("转换并导出", GUILayout.Height(36)))
                {
                    try { report = PmxConverter.Convert(sourcePath, outputPath, options); }
                    catch (Exception e) { report = new ConversionReport(); report.Errors.Add(e.Message); }
                    if (report.ConversionSucceeded) { Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(report.Prefab); EditorGUIUtility.PingObject(Selection.activeObject); }
                }
            }
            if (report != null)
            {
                GUILayout.Space(10);
                EditorGUILayout.HelpBox(report.ConversionSucceeded ? "转换完成：" + report.Fbx : "转换失败，请按下方错误修正后重试。", report.ConversionSucceeded ? MessageType.Info : MessageType.Error);
                foreach (string e in report.Errors) EditorGUILayout.HelpBox(e, MessageType.Error);
                foreach (string w in report.Warnings) EditorGUILayout.HelpBox(w, MessageType.Warning);
                if (report.ConversionSucceeded && GUILayout.Button("选中头像 Prefab")) Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(report.Prefab);
            }
            EditorGUILayout.EndScrollView();
        }
        private void Read()
        {
            try { model = ModelReader.Read(sourcePath); mapping = true; }
            catch (Exception e) { model = null; report = new ConversionReport(); report.Errors.Add(e.Message); }
        }
        private void DrawMappings()
        {
            var automatic = HumanoidRig.AutoMap(model);
            var labels = new[] { "不映射 / 必要时自动补建" }.Concat(model.Bones.Select((b, i) => i + ": " + b.Name + " / " + b.EnglishName)).ToArray();
            foreach (HumanBodyBones human in Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (human == HumanBodyBones.LastBone) continue;
                var custom = options.BoneOverrides.FirstOrDefault(x => x.Human == human);
                int current = custom != null ? custom.PmxIndex : automatic.TryGetValue(human, out int found) ? found : -1;
                EditorGUI.BeginChangeCheck();
                int selected = EditorGUILayout.Popup(human.ToString(), current + 1, labels) - 1;
                if (EditorGUI.EndChangeCheck())
                {
                    if (custom == null) { custom = new BoneOverride { Human = human }; options.BoneOverrides.Add(custom); }
                    custom.PmxIndex = selected;
                }
            }
            if (GUILayout.Button("恢复自动映射")) options.BoneOverrides.Clear();
        }
    }
}
