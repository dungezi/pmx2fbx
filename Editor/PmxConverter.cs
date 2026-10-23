using System;
using System.IO;
using System.Linq;
using Pmx2Fbx.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Pmx2Fbx
{
    public static class PmxConverter
    {
        public static ConversionReport Convert(string pmxPath, string outputFolder, ConversionOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (float.IsNaN(options.AlphaCutoff) || options.AlphaCutoff <= 0 || options.AlphaCutoff >= 1
                || float.IsNaN(options.OpaqueAlphaThreshold) || options.OpaqueAlphaThreshold < .9f || options.OpaqueAlphaThreshold > 1)
                throw new ArgumentException("镂空阈值须在 0 到 1 之间，近不透明阈值须在 0.9 到 1 之间。");
            if (!FbxDependency.IsReady) throw new InvalidOperationException("请先在转换窗口安装 Unity FBX Exporter。");
            if (!File.Exists(pmxPath)) throw new FileNotFoundException("找不到模型文件。", pmxPath);
            if (GraphicsSettings.currentRenderPipeline != null) throw new InvalidOperationException("当前版本面向 VRChat PC / Built-in Render Pipeline，请在 Built-in 项目中转换。");
            string assets = Path.GetFullPath(Application.dataPath);
            string output = Path.GetFullPath(outputFolder);
            if (!output.StartsWith(assets + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("输出目录必须位于当前 Unity 项目的 Assets 内。");
            if (!ModelReader.IsSupported(pmxPath)) throw new ArgumentException("请选择 .pmx、.xps、.mesh 或 .mesh.ascii 文件。");
            if (options.VrChat && options.NormalizeHeight && (options.Height < .2f || options.Height > 5)) throw new ArgumentException("目标身高应在 0.2 到 5 米之间。");
            string parent = "Assets" + output.Substring(assets.Length).Replace('\\', '/');
            Directory.CreateDirectory(parent);
            string modelName = Clean(Path.GetFileNameWithoutExtension(pmxPath));
            string folder = AssetDatabase.GenerateUniqueAssetPath(parent + "/" + modelName);
            // A fresh folder makes every conversion non-destructive, including repeated exports.
            Directory.CreateDirectory(folder); AssetDatabase.Refresh();
            var report = new ConversionReport { Source = Path.GetFullPath(pmxPath), VrChat = options.VrChat };
            try
            {
                var source = ModelReader.Read(pmxPath);
                report.Format = source.Format;
                report.Warnings.AddRange(source.Warnings);
                report.Vertices = source.Vertices.Length; report.Triangles = source.Indices.Length / 3;
                report.Materials = source.Materials.Length; report.Bones = source.Bones.Length;
                using (var built = ModelBuilder.Build(source, pmxPath, folder, options, report))
                {
                    Directory.CreateDirectory(folder + "/Materials");
                    for (int i = 0; i < built.Materials.Length; i++)
                        AssetDatabase.CreateAsset(built.Materials[i], folder + "/Materials/" + built.Materials[i].name + ".mat");
                    report.Fbx = folder + "/" + modelName + ".fbx";
                    EditorUtility.DisplayProgressBar(source.Format + " → FBX", "导出 FBX 并检查重新导入结果…", .6f);
                    if (string.IsNullOrEmpty(FbxDependency.Export(Path.GetFullPath(report.Fbx), built.Root)))
                        throw new InvalidOperationException("Unity FBX Exporter 导出失败。请查看 Console。");
                    AssetDatabase.ImportAsset(report.Fbx, ImportAssetOptions.ForceSynchronousImport);
                    var importer = AssetImporter.GetAtPath(report.Fbx) as ModelImporter;
                    if (!importer) throw new InvalidOperationException("FBX 未能被 Unity 识别。");
                    importer.isReadable = true; importer.importBlendShapes = options.IncludeMorphs;
                    importer.importAnimation = false; importer.meshCompression = ModelImporterMeshCompression.Off;
                    importer.importNormals = ModelImporterNormals.Import;
                    importer.importTangents = ModelImporterTangents.CalculateMikk;
                    importer.optimizeGameObjects = false;
                    importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                    if (options.VrChat)
                    {
                        importer.animationType = ModelImporterAnimationType.Human;
                        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                        importer.humanDescription = built.Description;
                    }
                    else importer.animationType = ModelImporterAnimationType.Generic;
                    foreach (var material in built.Materials)
                        importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), material.name), material);
                    importer.SaveAndReimport();
                    var asset = AssetDatabase.LoadAssetAtPath<GameObject>(report.Fbx);
                    if (!asset) throw new InvalidOperationException("FBX 重新导入失败。");
                    var avatar = AssetDatabase.LoadAllAssetsAtPath(report.Fbx).OfType<Avatar>().FirstOrDefault(a => a.isHuman);
                    report.HumanoidValid = avatar && avatar.isValid && avatar.isHuman;
                    if (options.VrChat && !report.HumanoidValid) throw new InvalidOperationException("导出后 Humanoid 校验失败，请检查骨骼配置。");
                    var prefabRoot = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                    try
                    {
                        VerifyImported(prefabRoot, built.Mesh, report);
                        var skin = prefabRoot.GetComponentInChildren<SkinnedMeshRenderer>();
                        if (skin) { skin.sharedMaterials = built.Materials; skin.quality = SkinQuality.Bone4; }
                        if (options.VrChat)
                        {
                            var animator = prefabRoot.GetComponent<Animator>() ?? prefabRoot.AddComponent<Animator>();
                            animator.avatar = avatar; animator.applyRootMotion = false;
                            if (options.AddDescriptor) VrChatBridge.Attach(prefabRoot, report, Mouth(source));
                        }
                        report.Prefab = folder + "/" + modelName + (options.VrChat ? "_VRChat.prefab" : ".prefab");
                        if (!PrefabUtility.SaveAsPrefabAsset(prefabRoot, report.Prefab)) throw new InvalidOperationException("Prefab 保存失败。");
                    }
                    finally { UnityEngine.Object.DestroyImmediate(prefabRoot); }
                    report.Bones = built.Root.GetComponentsInChildren<Transform>(true).Length - 3;
                    if (options.VrChat)
                    {
                        if (report.Triangles > 70000) report.Warnings.Add("三角面超过 70,000，PC 性能评级会受影响。需要单独减面。");
                        if (report.Materials > 8) report.Warnings.Add("材质槽超过 PC Good 参考值 8，建议合并材质 / 图集。");
                        if (report.Bones > 150) report.Warnings.Add("骨骼超过 PC Good 参考值 150，建议优化辅助骨骼。");
                        report.Infos.Add("性能阈值仅作提示，不代表完整 SDK 性能评级，也不是上传硬限制。");
                    }
                }
                report.ConversionSucceeded = true;
                AssetDatabase.SaveAssets();
            }
            catch (Exception e)
            {
                report.Errors.Add(e.Message);
                Debug.LogError("PMX2FBX: " + e);
                // Keep diagnostic artifacts for inspection, but never label a failed humanoid as compatible.
            }
            finally
            {
                File.WriteAllText(folder + "/conversion-report.json", report.ToJson());
                AssetDatabase.Refresh(); EditorUtility.ClearProgressBar();
            }
            return report;
        }
        private static void VerifyImported(GameObject root, Mesh original, ConversionReport report)
        {
            var renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers.Length != 1 || !renderers[0].sharedMesh) throw new InvalidOperationException("FBX 蒙皮网格丢失或数量异常。");
            var mesh = renderers[0].sharedMesh;
            if (mesh.subMeshCount != original.subMeshCount)
                throw new InvalidOperationException("FBX 材质槽在导出中发生变化。");
            int removed = 0;
            for (int s = 0; s < original.subMeshCount; s++)
            {
                var sourceIndices = original.GetTriangles(s); var importedIndices = mesh.GetTriangles(s);
                int difference = (sourceIndices.Length - importedIndices.Length) / 3;
                if (difference == 0) continue;
                // FBX/Unity can discard zero-area triangles. Verify each material group,
                // retaining the strict count check for every non-degenerate triangle.
                int sourceDegenerate = CountDegenerate(original.vertices, sourceIndices);
                int importedDegenerate = CountDegenerate(mesh.vertices, importedIndices);
                if (difference < 0 || difference > sourceDegenerate || sourceDegenerate - importedDegenerate != difference)
                    throw new InvalidOperationException("FBX 材质槽 " + s + " 的有效三角面数量发生变化，转换结果未通过检查。");
                removed += difference;
            }
            if (removed > 0) report.Warnings.Add("FBX 重新导入时移除了 " + removed + " 个零面积退化三角面；已按材质槽核对，其余有效面数保持一致。");
            report.Triangles = mesh.triangles.Length / 3;
            if (renderers[0].bones.Length == 0 || mesh.boneWeights.Length != mesh.vertexCount)
                throw new InvalidOperationException("FBX 蒙皮或骨骼丢失。");
            for (int i = 0; i < original.blendShapeCount; i++)
                if (mesh.GetBlendShapeIndex(original.GetBlendShapeName(i)) < 0)
                    throw new InvalidOperationException("FBX 表情丢失：" + original.GetBlendShapeName(i));
            report.Infos.Add("FBX 重新导入后网格面数、材质槽、蒙皮与 BlendShape 检查通过。");
        }
        private static int CountDegenerate(Vector3[] vertices, int[] indices)
        {
            int count = 0;
            for (int i = 0; i < indices.Length; i += 3)
            {
                var a = vertices[indices[i]]; var b = vertices[indices[i + 1]]; var c = vertices[indices[i + 2]];
                double ux = (double)b.x - a.x, uy = (double)b.y - a.y, uz = (double)b.z - a.z;
                double vx = (double)c.x - a.x, vy = (double)c.y - a.y, vz = (double)c.z - a.z;
                if (uy * vz - uz * vy == 0 && uz * vx - ux * vz == 0 && ux * vy - uy * vx == 0) count++;
            }
            return count;
        }
        private static string Mouth(PmxModel source)
        {
            int index = Array.FindIndex(source.Morphs, m => new[] { "あ", "あー", "mouthopen", "a", "aa" }.Contains((m.Name ?? "").ToLowerInvariant()) || new[] { "mouthopen", "a", "aa" }.Contains((m.EnglishName ?? "").ToLowerInvariant()));
            return index < 0 ? null : "Morph_" + index.ToString("D5");
        }
        private static string Clean(string name)
        {
            string clean = new string(name.Select(c => char.IsLetterOrDigit(c) && c < 128 || c == '_' || c == '-' ? c : '_').ToArray());
            return string.IsNullOrWhiteSpace(clean.Trim('_')) ? "PmxModel" : clean;
        }
    }
}
