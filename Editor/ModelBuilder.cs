using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Pmx2Fbx.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Pmx2Fbx
{
    internal sealed class BuiltModel : IDisposable
    {
        public GameObject Root;
        public Mesh Mesh;
        public Material[] Materials;
        public Dictionary<HumanBodyBones, Transform> Human;
        public HumanDescription Description;
        public Avatar Avatar;
        public void Dispose()
        {
            if (Root) UnityEngine.Object.DestroyImmediate(Root);
            if (Mesh && !AssetDatabase.Contains(Mesh)) UnityEngine.Object.DestroyImmediate(Mesh);
            if (Avatar && !AssetDatabase.Contains(Avatar)) UnityEngine.Object.DestroyImmediate(Avatar);
            if (Materials != null) foreach (var m in Materials)
                if (m && !AssetDatabase.Contains(m)) UnityEngine.Object.DestroyImmediate(m);
        }
    }
    internal static class ModelBuilder
    {
        internal static Vector3 Convert(Float3 p, float scale) => new Vector3(-p.X, p.Y, -p.Z) * scale;
        private static Vector3 Convert(Float3 p, float scale, Float3 axis) => new Vector3(p.X * axis.X, p.Y * axis.Y, p.Z * axis.Z) * scale;

        public static BuiltModel Build(PmxModel source, string pmxPath, string folder, ConversionOptions options, ConversionReport report)
        {
            if (source.Vertices.Length == 0 || source.Indices.Length == 0) throw new InvalidDataException("模型没有可导出的网格。");
            float minY = source.Vertices.Min(v => v.Position.Y), maxY = source.Vertices.Max(v => v.Position.Y);
            float scale = options.VrChat && options.NormalizeHeight ? options.Height / (maxY - minY) : options.Scale;
            if (!(scale > 0) || float.IsInfinity(scale) || float.IsNaN(scale)) throw new InvalidDataException("尺寸或模型高度无效。");
            // Rotate MMD's -Z facing direction 180 degrees about Y into Unity's +Z convention.
            // This is a rotation, so triangle winding stays intact. Translate feet to Y=0.
            Vector3 offset = options.VrChat ? new Vector3(0, -minY * scale, 0) : Vector3.zero;
            var built = new BuiltModel();
            var axis = source.Axis;
            if (options.Rotate180) axis = new Float3(-axis.X, axis.Y, -axis.Z);
            try
            {
                built.Root = new GameObject("Avatar");
                var armature = new GameObject("Armature").transform; armature.SetParent(built.Root.transform);
                var bones = new Transform[Math.Max(1, source.Bones.Length)];
                for (int i = 0; i < bones.Length; i++)
                {
                    bones[i] = new GameObject("Bone_" + i.ToString("D5")).transform;
                    bones[i].SetParent(armature);
                    bones[i].position = source.Bones.Length > 0 ? Convert(source.Bones[i].Position, scale, axis) + offset : Vector3.zero;
                }
                for (int i = 0; i < source.Bones.Length; i++)
                    if (source.Bones[i].Parent >= 0) bones[i].SetParent(bones[source.Bones[i].Parent], true);
                if (options.VrChat)
                {
                    if (source.Bones.Length == 0) throw new InvalidDataException("VRChat 人形转换需要骨骼。");
                    built.Human = HumanoidRig.Map(source, bones, options);
                    HumanoidRig.Repair(built.Root, built.Human, report);
                }
                var mesh = built.Mesh = new Mesh { name = "Body", indexFormat = source.Vertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.vertices = source.Vertices.Select(v => Convert(v.Position, scale, axis) + offset).ToArray();
                mesh.normals = source.Vertices.Select(v => Convert(v.Normal, 1, axis).normalized).ToArray();
                mesh.colors32 = source.Vertices.Select(v => v.Color == null ? new Color32(255, 255, 255, 255) : new Color32(v.Color[0], v.Color[1], v.Color[2], v.Color[3])).ToArray();
                mesh.uv = source.Vertices.Select(v => new Vector2(v.U, 1 - v.V)).ToArray();
                int uvCount = source.Vertices[0].ExtraUvs.Length;
                for (int channel = 0; channel < uvCount; channel++)
                {
                    int c = channel;
                    mesh.SetUVs(c + 1, source.Vertices.Select(v => new Vector4(v.ExtraUvs[c][0], v.ExtraUvs[c][1], v.ExtraUvs[c][2], v.ExtraUvs[c][3])).ToList());
                }
                mesh.boneWeights = BuildWeights(source, bones.Length, report);
                mesh.bindposes = bones.Select(b => b.worldToLocalMatrix).ToArray();
                mesh.subMeshCount = source.Materials.Length;
                int faceCursor = 0;
                for (int i = 0; i < source.Materials.Length; i++)
                {
                    var triangles = new int[source.Materials[i].IndexCount];
                    Array.Copy(source.Indices, faceCursor, triangles, 0, triangles.Length);
                    mesh.SetTriangles(triangles, i); faceCursor += triangles.Length;
                }
                var body = new GameObject("Body"); body.transform.SetParent(built.Root.transform);
                var renderer = body.AddComponent<SkinnedMeshRenderer>(); renderer.sharedMesh = mesh; renderer.bones = bones;
                renderer.rootBone = options.VrChat ? built.Human[HumanBodyBones.Hips] : armature;
                renderer.quality = SkinQuality.Bone4;
                built.Materials = CreateMaterials(source, pmxPath, folder, report);
                renderer.sharedMaterials = built.Materials;
                Matrix4x4[] skin = null;
                if (options.VrChat && options.TPose)
                {
                    HumanoidRig.SetTPose(built.Human);
                    var originalBindPoses = mesh.bindposes;
                    skin = bones.Select((b, i) => b.localToWorldMatrix * originalBindPoses[i]).ToArray();
                    BakePose(mesh, skin);
                    mesh.bindposes = bones.Select(b => b.worldToLocalMatrix).ToArray();
                    report.Infos.Add("手臂已调整为 T Pose，并同步烘焙蒙皮网格与表情位移。");
                }
                if (options.IncludeMorphs) AddMorphs(mesh, source, scale, axis, skin, report);
                mesh.RecalculateBounds(); mesh.RecalculateTangents(); renderer.localBounds = mesh.bounds;
                if (options.VrChat)
                {
                    built.Description = HumanoidRig.Describe(built.Root, built.Human);
                    built.Avatar = AvatarBuilder.BuildHumanAvatar(built.Root, built.Description);
                    if (!built.Avatar || !built.Avatar.isValid || !built.Avatar.isHuman)
                        throw new InvalidDataException("Unity 无法构建有效 Humanoid Avatar。请检查骨骼映射与关节位置。");
                    built.Root.AddComponent<Animator>().avatar = built.Avatar;
                }
                report.HeightMeters = mesh.bounds.size.y; report.BlendShapes = mesh.blendShapeCount;
                return built;
            }
            catch { built.Dispose(); throw; }
        }
        private static BoneWeight[] BuildWeights(PmxModel source, int boneCount, ConversionReport report)
        {
            var result = new BoneWeight[source.Vertices.Length];
            var indices = new int[4]; var weights = new float[4];
            bool repaired = false;
            for (int v = 0; v < result.Length; v++)
            {
                Array.Clear(indices, 0, 4); Array.Clear(weights, 0, 4); int count = 0;
                var vertex = source.Vertices[v]; float sum = 0;
                for (int i = 0; i < 4; i++)
                {
                    if (vertex.Bones[i] < 0 || vertex.Bones[i] >= boneCount || vertex.Weights[i] <= 0) continue;
                    int match = 0;
                    while (match < count && indices[match] != vertex.Bones[i]) match++;
                    if (match == count) { indices[count] = vertex.Bones[i]; count++; }
                    weights[match] += vertex.Weights[i]; sum += vertex.Weights[i];
                }
                if (sum < 1e-6f) { repaired = true; result[v] = new BoneWeight { boneIndex0 = 0, weight0 = 1 }; continue; }
                for (int i = 1; i < count; i++)
                    for (int j = i; j > 0 && weights[j] > weights[j - 1]; j--)
                    {
                        float w = weights[j]; weights[j] = weights[j - 1]; weights[j - 1] = w;
                        int b = indices[j]; indices[j] = indices[j - 1]; indices[j - 1] = b;
                    }
                result[v] = new BoneWeight {
                    boneIndex0 = indices[0], boneIndex1 = indices[1], boneIndex2 = indices[2], boneIndex3 = indices[3],
                    weight0 = weights[0] / sum, weight1 = weights[1] / sum, weight2 = weights[2] / sum, weight3 = weights[3] / sum
                };
            }
            if (repaired) report.Warnings.Add("部分顶点没有有效蒙皮权重，已绑定到第一个骨骼。");
            return result;
        }
        private static Vector3 Skin(Vector3 v, BoneWeight w, Matrix4x4[] skin, bool point)
        {
            return Apply(skin[w.boneIndex0], v, point) * w.weight0 + Apply(skin[w.boneIndex1], v, point) * w.weight1
                + Apply(skin[w.boneIndex2], v, point) * w.weight2 + Apply(skin[w.boneIndex3], v, point) * w.weight3;
        }
        private static Vector3 Apply(Matrix4x4 matrix, Vector3 v, bool point) => point ? matrix.MultiplyPoint3x4(v) : matrix.MultiplyVector(v);
        private static void BakePose(Mesh mesh, Matrix4x4[] skin)
        {
            var positions = mesh.vertices; var normals = mesh.normals; var weights = mesh.boneWeights;
            for (int i = 0; i < positions.Length; i++)
            { positions[i] = Skin(positions[i], weights[i], skin, true); normals[i] = Skin(normals[i], weights[i], skin, false).normalized; }
            mesh.vertices = positions; mesh.normals = normals;
        }
        private static void AddMorphs(Mesh mesh, PmxModel source, float scale, Float3 axis, Matrix4x4[] skin, ConversionReport report)
        {
            var weights = mesh.boneWeights;
            for (int i = 0; i < source.Morphs.Length; i++)
            {
                if (source.Morphs[i].Type != 0 && source.Morphs[i].Type != 1) continue;
                var offsets = MorphResolver.Resolve(source, i, out bool complete);
                if (!complete) { report.Warnings.Add("组表情「" + source.Morphs[i].Name + "」包含不支持的类型，已整体跳过。"); continue; }
                if (offsets.Count == 0) continue;
                var delta = new Vector3[mesh.vertexCount];
                foreach (var pair in offsets)
                {
                    var v = Convert(pair.Value, scale, axis);
                    delta[pair.Key] = skin == null ? v : Skin(v, weights[pair.Key], skin, false);
                }
                // Stable ASCII channels avoid FBX importer renaming; human-readable mapping goes into the report.
                string name = "Morph_" + i.ToString("D5");
                mesh.AddBlendShapeFrame(name, 100, delta, null, null);
                report.Infos.Add(name + " = " + source.Morphs[i].Name);
            }
        }
        private static Material[] CreateMaterials(PmxModel source, string pmxPath, string folder, ConversionReport report)
        {
            Directory.CreateDirectory(folder + "/Textures");
            var loaded = new Dictionary<int, Texture2D>();
            for (int i = 0; i < source.Textures.Length; i++)
            {
                string name = source.Textures[i];
                if (string.IsNullOrWhiteSpace(name)) continue;
                string path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(pmxPath), name.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar)));
                if (!File.Exists(path) && source.Format == "XPS")
                {
                    string local = Path.Combine(Path.GetDirectoryName(pmxPath), Path.GetFileName(name.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar)));
                    if (File.Exists(local)) { path = Path.GetFullPath(local); report.Infos.Add("XPS 原纹理路径不可用，已使用模型旁的同名文件：" + Path.GetFileName(path)); }
                }
                if (!File.Exists(path)) { report.Warnings.Add("纹理缺失：" + name); continue; }
                string extension = Path.GetExtension(path).ToLowerInvariant();
                if (!new[] { ".png", ".jpg", ".jpeg", ".tga", ".bmp", ".psd", ".tif", ".tiff" }.Contains(extension))
                { report.Warnings.Add("Unity 不支持此纹理格式：" + name + "；请先转换为 PNG。"); continue; }
                string destination = folder + "/Textures/Texture_" + i.ToString("D5") + extension;
                File.Copy(path, destination, false); AssetDatabase.ImportAsset(destination, ImportAssetOptions.ForceSynchronousImport);
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(destination);
                if (texture) loaded[i] = texture; else report.Warnings.Add("纹理导入失败：" + name);
            }
            var materials = new Material[source.Materials.Length];
            bool vertexAlpha = source.Vertices.Any(v => v.Color != null && v.Color[3] < 255);
            for (int i = 0; i < materials.Length; i++)
            {
                var pmx = source.Materials[i]; bool transparent = pmx.Diffuse[3] < .999f || vertexAlpha;
                if (loaded.TryGetValue(pmx.Texture, out var texture))
                {
                    var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) as TextureImporter;
                    transparent |= importer && importer.DoesSourceTextureHaveAlpha();
                }
                var shader = Shader.Find(transparent ? "PMX2FBX/PC Toon Transparent" : "PMX2FBX/PC Toon");
                if (!shader) shader = Shader.Find("Standard");
                if (!shader) throw new InvalidOperationException("缺少材质 Shader；请使用 Built-in Render Pipeline。");
                var material = materials[i] = new Material(shader) { name = "Material_" + i.ToString("D5") };
                material.color = new Color(pmx.Diffuse[0], pmx.Diffuse[1], pmx.Diffuse[2], pmx.Diffuse[3]);
                material.mainTexture = texture;
                if (material.HasProperty("_Cull")) material.SetInt("_Cull", (pmx.Flags & 1) != 0 ? 0 : 2);
                if (transparent && shader.name == "Standard")
                {
                    material.SetFloat("_Mode", 3); material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                    material.SetInt("_ZWrite", 0); material.EnableKeyword("_ALPHABLEND_ON"); material.renderQueue = 3000;
                }
                if (pmx.SphereTexture >= 0) report.Warnings.Add("材质「" + pmx.Name + "」的球面贴图未转换；主纹理与颜色已保留。");
            }
            report.Warnings.Add(source.Format == "XPS" ? "XPS 材质使用基础 PC Toon，特殊渲染组、法线与高光需要手动配置。透明排序请在 Unity 中检查。" : "材质使用基础 PC Toon；MMD Toon / Sphere / 描边效果需要按需要手动调整。透明排序请在 Unity 中检查。");
            return materials;
        }
    }
}
