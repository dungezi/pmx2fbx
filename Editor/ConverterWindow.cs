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
        [SerializeField] private int settingsVersion;
        private PmxModel model;
        private Dictionary<HumanBodyBones, int> autoMapping;
        private ConversionReport report;
        private Vector2 scroll;
        private bool mapping;
        private bool materialSettings;
        private static readonly string[] AlphaLabels = { "自动（按材质实际 UV）", "不透明", "半透明", "镂空（保留遮挡）" };
        [SerializeField] private int format;
        [MenuItem("Tools/PMX to FBX/转换 PMX 模型")]
        public static void Open() => GetWindow<ConverterWindow>("模型 → FBX").SetFormat(0);
        [MenuItem("Tools/PMX to FBX/转换 XPS 模型（附属功能）")]
        public static void OpenXps() => GetWindow<ConverterWindow>("模型 → FBX").SetFormat(1);
        [MenuItem("Tools/PMX to FBX/转换 VRM 模型（附属功能）")]
        public static void OpenVrm() => GetWindow<ConverterWindow>("模型 → FBX").SetFormat(2);
        private void OnEnable()
        {
            if (options == null) options = new ConversionOptions();
            // Older serialized windows do not contain the new protection fields.
            // Initialize them once, preserving explicit choices after migration.
            if (settingsVersion < 1)
            {
                options.TransparentOcclusion = true; options.OcclusionCutoff = .5f;
                settingsVersion = 1;
            }
        }
        private void SetFormat(int value)
        {
            if (value == format) return;
            format = value; model = null; report = null; sourcePath = "";
            outputPath = value == 0 ? "Assets/PMXConverted" : value == 1 ? "Assets/XPSConverted" : "Assets/VRMConverted";
            options = new ConversionOptions { Scale = value == 0 ? .08f : 1f, VrChat = value == 0 };
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            SetFormat(EditorGUILayout.Popup("源模型格式", format, new[] { "PMX → FBX", "XPS → FBX（附属功能）", "VRM → FBX（附属功能）" }));
            if (!FbxDependency.IsReady)
            {
                EditorGUILayout.HelpBox("首次使用需要 Unity 官方 FBX Exporter。点击下方按钮安装，完成后可直接转换。", MessageType.Info);
                using (new EditorGUI.DisabledScope(FbxDependency.IsInstalling))
                    if (GUILayout.Button(FbxDependency.IsInstalling ? "正在安装 FBX Exporter…" : "安装 FBX Exporter")) FbxDependency.Install();
                if (!string.IsNullOrEmpty(FbxDependency.LastError)) EditorGUILayout.HelpBox(FbxDependency.LastError, MessageType.Error);
                if (FbxDependency.IsInstalling) Repaint();
            }
            EditorGUILayout.HelpBox(format == 0 ? "转换 PMX 2.0 / 2.1；保留骨骼蒙皮、主纹理与顶点 / 组表情。VRChat 模式面向 PC 人形头像。" : format == 1 ? "支持 .xps、.mesh 和 .mesh.ascii；保留骨骼蒙皮、顶点颜色、UV 和主贴图。XPS 不提供 BlendShape；可选 VRChat PC 人形适配。" : "支持 VRM 0.x / 1.0，直接读取内嵌网格、贴图、蒙皮、顶点表情及人形骨骼定义，无需安装 UniVRM。MToon、SpringBone、LookAt 等需要另行配置。", MessageType.Info);
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck(); sourcePath = EditorGUILayout.TextField("模型文件", sourcePath);
            if (EditorGUI.EndChangeCheck()) { model = null; options.BoneOverrides.Clear(); options.MaterialOverrides.Clear(); }
            if (GUILayout.Button("浏览", GUILayout.Width(55)))
            {
                string selected = EditorUtility.OpenFilePanelWithFilters("选择模型", "", format == 0 ? new[] { "PMX", "pmx" } : format == 1 ? new[] { "XPS / XNALara", "xps,mesh,ascii" } : new[] { "VRM", "vrm" });
                if (!string.IsNullOrEmpty(selected)) { sourcePath = selected; model = null; options.BoneOverrides.Clear(); options.MaterialOverrides.Clear(); }
            }
            EditorGUILayout.EndHorizontal();
            var drag = Event.current;
            if ((drag.type == EventType.DragUpdated || drag.type == EventType.DragPerform) && DragAndDrop.paths.Any(SourceModelReader.IsSupported))
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                if (drag.type == EventType.DragPerform)
                { DragAndDrop.AcceptDrag(); string selected = DragAndDrop.paths.First(SourceModelReader.IsSupported); SetFormat(SourceModelReader.IsVrm(selected) ? 2 : ModelReader.IsXps(selected) ? 1 : 0); sourcePath = selected; model = null; options.BoneOverrides.Clear(); options.MaterialOverrides.Clear(); }
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
            if (format != 1) options.IncludeMorphs = EditorGUILayout.Toggle("导出 BlendShape 表情", options.IncludeMorphs);
            options.Transparency = (MaterialAlphaMode)EditorGUILayout.Popup("透明处理", (int)options.Transparency, AlphaLabels);
            options.AlphaCutoff = EditorGUILayout.Slider("镂空透明阈值", options.AlphaCutoff, .01f, .99f);
            options.TransparentOcclusion = EditorGUILayout.Toggle("半透明遮挡保护", options.TransparentOcclusion);
            if (options.TransparentOcclusion) options.OcclusionCutoff = EditorGUILayout.Slider("遮挡保护阈值", options.OcclusionCutoff, .01f, 1);
            using (new EditorGUI.DisabledScope(!File.Exists(sourcePath)))
            {
                if (GUILayout.Button("读取模型 / 查看骨骼映射")) Read();
                if (model != null)
                {
                    EditorGUILayout.LabelField(model.Name + "  ·  " + model.Vertices.Length + " 顶点  ·  " + model.Indices.Length / 3 + " 三角面");
                    materialSettings = EditorGUILayout.Foldout(materialSettings, "材质透明设置（可逐个调整）", true);
                    if (materialSettings) DrawMaterials();
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
            try { model = SourceModelReader.Read(sourcePath); autoMapping = null; mapping = true; }
            catch (Exception e) { model = null; report = new ConversionReport(); report.Errors.Add(e.Message); }
        }
        private void DrawMappings()
        {
            var automatic = autoMapping ?? (autoMapping = HumanoidRig.AutoMap(model));
            EditorGUILayout.HelpBox("自动映射结合名称及父子链。存在同名歧义时保持未映射，请确认关节；VRM 优先读取文件内的人形定义。", MessageType.Info);
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
        private void DrawMaterials()
        {
            EditorGUILayout.HelpBox("自动模式仅检查当前材质实际使用的贴图区域。黑白透明遮罩使用镂空并写入深度，连续透明度使用半透明。特殊材质可在下方单独指定；材质名称不参与自动判断。", MessageType.Info);
            options.OpaqueAlphaThreshold = EditorGUILayout.Slider("近不透明阈值", options.OpaqueAlphaThreshold, .9f, 1);
            EditorGUILayout.HelpBox("默认将 Alpha ≥ 0.98 视为不透明，避免 0.99 的皮肤等材质出现深度排序穿透。需要精确保留微弱透明度时，可将该材质指定为半透明或把阈值设为 1。", MessageType.None);
            EditorGUILayout.HelpBox("半透明遮挡保护让刘海、衣物的实体区域遮住后绘制的阴影层，同时保留边缘透明度。真正需要层层透视的玻璃等材质，可单独关闭保护。", MessageType.Info);
            var labels = new[] { "跟随全局 / 自动", "不透明", "半透明", "镂空（保留遮挡）" };
            for (int i=0; i<model.Materials.Length; i++)
            {
                var custom = options.MaterialOverrides.FirstOrDefault(m => m.MaterialIndex == i);
                int current = custom == null ? 0 : (int)custom.Mode;
                EditorGUI.BeginChangeCheck();
                int selected = EditorGUILayout.Popup(i + ": " + model.Materials[i].Name, current, labels);
                if (EditorGUI.EndChangeCheck())
                {
                    if (custom == null) { custom = new MaterialOverride { MaterialIndex=i }; options.MaterialOverrides.Add(custom); }
                    custom.Mode = (MaterialAlphaMode)selected;
                }
                EditorGUI.BeginChangeCheck();
                int occlusion = EditorGUILayout.Popup("    遮挡保护", custom == null ? 0 : (int)custom.Occlusion, new[] { "跟随全局", "开启", "关闭（玻璃等）" });
                if (EditorGUI.EndChangeCheck())
                {
                    if (custom == null) { custom=new MaterialOverride { MaterialIndex=i }; options.MaterialOverrides.Add(custom); }
                    custom.Occlusion=(OcclusionOverride)occlusion;
                }
            }
            if (GUILayout.Button("恢复材质自动判断"))
            {
                options.MaterialOverrides.Clear(); options.Transparency=MaterialAlphaMode.Auto;
                options.TransparentOcclusion=true; options.OcclusionCutoff=.5f;
            }
        }
    }
}
