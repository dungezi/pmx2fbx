using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Pmx2Fbx
{
    public static class VrChatBridge
    {
        public static Type DescriptorType => AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor", false)).FirstOrDefault(t => t != null);

        public static void Attach(GameObject root, ConversionReport report, string mouthShape)
        {
            var type = DescriptorType;
            if (type == null)
            {
                report.Warnings.Add("未安装 VRChat Avatars SDK：已生成人形 FBX / Prefab，安装 SDK 后再使用菜单添加 Descriptor。");
                return;
            }
            var animator = root.GetComponent<Animator>();
            if (!animator || !animator.avatar || !animator.avatar.isHuman || !animator.avatar.isValid)
                throw new InvalidOperationException("添加 VRChat Descriptor 需要有效 Humanoid Animator。");
            var descriptor = root.GetComponent(type) ?? root.AddComponent(type);
            var serialized = new SerializedObject(descriptor);
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            var leftEye = animator.GetBoneTransform(HumanBodyBones.LeftEye);
            var rightEye = animator.GetBoneTransform(HumanBodyBones.RightEye);
            Vector3 view = leftEye && rightEye ? (leftEye.position + rightEye.position) / 2 : head.position + root.transform.up * .08f + root.transform.forward * .08f;
            var viewProperty = serialized.FindProperty("ViewPosition");
            if (viewProperty != null) viewProperty.vector3Value = root.transform.InverseTransformPoint(view);
            var skin = root.GetComponentInChildren<SkinnedMeshRenderer>();
            var mode = serialized.FindProperty("lipSync");
            if (mode != null && skin && skin.sharedMesh && !string.IsNullOrEmpty(mouthShape) && skin.sharedMesh.GetBlendShapeIndex(mouthShape) >= 0)
            {
                int enumIndex = Array.IndexOf(mode.enumNames, "JawFlapBlendShape");
                var face = serialized.FindProperty("VisemeSkinnedMesh");
                var shape = serialized.FindProperty("MouthOpenBlendShapeName");
                if (enumIndex >= 0 && face != null && shape != null)
                {
                    mode.enumValueIndex = enumIndex; face.objectReferenceValue = skin; shape.stringValue = mouthShape;
                    report.Infos.Add("已配置张嘴表情驱动的基础语音口型（JawFlapBlendShape）。");
                }
                else report.Warnings.Add("SDK 的口型属性与预期不同，请手动设置 LipSync。");
            }
            else report.Warnings.Add("没有找到张嘴表情，请在 Descriptor 手动设置 LipSync。");
            serialized.ApplyModifiedPropertiesWithoutUndo();
            report.DescriptorAdded = true;
            report.Warnings.Add("请确认 Descriptor 视点、眨眼 / Eye Look 与 SDK Builder 校验；本工具未运行上传构建。");
        }
        [MenuItem("Tools/PMX to FBX/为选中头像添加 VRChat Descriptor")]
        private static void AttachSelected()
        {
            if (!Selection.activeGameObject) return;
            var root = Selection.activeGameObject;
            Undo.RegisterFullObjectHierarchyUndo(root, "Add VRChat Descriptor");
            var report = new ConversionReport();
            try { Attach(root, report, null); Debug.Log(report.ToJson(), root); }
            catch (Exception e) { Debug.LogError(e.Message, root); }
        }
    }
}
