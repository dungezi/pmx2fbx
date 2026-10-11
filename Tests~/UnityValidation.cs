using System;
using System.IO;
using System.Linq;
using Pmx2Fbx;
using Pmx2Fbx.Core;
using Pmx2Fbx.Tests;
using UnityEditor;
using UnityEngine;

public static class UnityValidation
{
    public static void Run()
    {
        try
        {
            Directory.CreateDirectory("Assets/Fixtures");
            string source = "Assets/Fixtures/test.pmx";
            File.WriteAllBytes(source, PmxFixture.Create(humanoid: true));
            var texture = new Texture2D(2,2,TextureFormat.RGB24,false);
            texture.SetPixels(new[] { Color.red, Color.green, Color.blue, Color.white }); texture.Apply();
            File.WriteAllBytes("Assets/Fixtures/missing.png", texture.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.Refresh();
            var basic = PmxConverter.Convert(source, "Assets/Converted", new ConversionOptions { VrChat = false });
            Check(basic.ConversionSucceeded, "Generic round trip: " + string.Join("; ", basic.Errors));
            var humanoid = PmxConverter.Convert(source, "Assets/Converted", new ConversionOptions());
            Check(humanoid.ConversionSucceeded && humanoid.HumanoidValid, "Humanoid round trip: " + string.Join("; ", humanoid.Errors));
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(humanoid.Prefab);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var animator = instance.GetComponent<Animator>();
            var skin = instance.GetComponentInChildren<SkinnedMeshRenderer>();
            Check(animator && animator.avatar && animator.avatar.isHuman && animator.avatar.isValid, "Prefab avatar");
            Check(skin.sharedMesh.blendShapeCount == 2 && skin.sharedMesh.subMeshCount == 2, "Blend shapes/materials");
            Check(skin.sharedMaterials.All(m => m && m.mainTexture), "Material texture remap");
            Check((animator.GetBoneTransform(HumanBodyBones.LeftLowerArm).position - animator.GetBoneTransform(HumanBodyBones.LeftUpperArm).position).normalized.x < -.99f, "Left arm T pose");
            Check(Math.Abs(skin.sharedMesh.bounds.size.y - 1.65f) < .03f, "Meter scale");
            Check(skin.sharedMesh.isReadable, "VRChat readable mesh");
            var baked = new Mesh(); skin.BakeMesh(baked);
            Check((baked.bounds.size - skin.sharedMesh.bounds.size).magnitude < .01f, "Bind pose preserved after reimport");
            Check((baked.vertices.Zip(skin.sharedMesh.vertices, (a,b) => Vector3.Distance(a,b))).Max() < .001f, "All skinned vertices match exported rest pose");
            var neutralVertices = baked.vertices;
            skin.SetBlendShapeWeight(0,100); skin.BakeMesh(baked);
            Check(baked.vertices.Zip(neutralVertices, (a,b) => Vector3.Distance(a,b)).Max() > .01f, "Morph actually deforms imported skin");
            skin.SetBlendShapeWeight(0,0);
            var beforeArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm).localRotation;
            animator.GetBoneTransform(HumanBodyBones.LeftUpperArm).localRotation *= Quaternion.Euler(0,0,30);
            skin.BakeMesh(baked);
            Check(baked.vertices.Zip(neutralVertices, (a,b) => Vector3.Distance(a,b)).Max() > .05f, "Arm weight actually deforms imported skin");
            animator.GetBoneTransform(HumanBodyBones.LeftUpperArm).localRotation = beforeArm;
            UnityEngine.Object.DestroyImmediate(baked); UnityEngine.Object.DestroyImmediate(instance);
            var second = PmxConverter.Convert(source, "Assets/Converted", new ConversionOptions());
            Check(second.ConversionSucceeded && second.Fbx != humanoid.Fbx && File.Exists(humanoid.Fbx), "Non-destructive repeated export");
            File.WriteAllBytes(source, PmxFixture.Create(humanoid: true, missingChest: true));
            var repaired = PmxConverter.Convert(source, "Assets/Converted", new ConversionOptions());
            Check(repaired.ConversionSucceeded && repaired.Warnings.Any(w => w.Contains("补建 Chest")), "Synthetic chest repair");
            File.WriteAllBytes(source, PmxFixture.Create());
            var invalid = PmxConverter.Convert(source, "Assets/Converted", new ConversionOptions());
            Check(!invalid.ConversionSucceeded && invalid.Errors.Count > 0, "Reject non humanoid");
            foreach (int kind in new[] {1,2,3,4})
            {
                File.WriteAllBytes(source, PmxFixture.Create(skinning: kind));
                var skinResult = PmxConverter.Convert(source, "Assets/Converted", new ConversionOptions {VrChat=false});
                Check(skinResult.ConversionSucceeded, "Skinning type " + kind + ": " + string.Join("; ", skinResult.Errors));
            }
            foreach (int version in new[] { 0, 1, 2, 3, 4 })
            {
                string xpsPath = "Assets/Fixtures/xps" + version + (version == 4 ? ".mesh.ascii" : version == 0 ? ".mesh" : ".xps");
                File.WriteAllBytes(xpsPath, XpsFixture.Create(version, version == 4, humanoid:true, manyWeights:version >= 3));
                var xpsResult = XpsConverter.Convert(xpsPath, "Assets/Converted");
                Check(xpsResult.ConversionSucceeded && xpsResult.Format == "XPS", "XPS generic " + version + ": " + string.Join("; ", xpsResult.Errors));
                var xpsSkin = AssetDatabase.LoadAssetAtPath<GameObject>(xpsResult.Prefab).GetComponentInChildren<SkinnedMeshRenderer>();
                Check(xpsSkin.sharedMesh.subMeshCount == 2 && xpsSkin.sharedMaterials.All(m => m.mainTexture), "XPS material groups/textures");
                Check(xpsSkin.sharedMesh.colors32.Length == xpsSkin.sharedMesh.vertexCount && xpsSkin.sharedMesh.colors32.All(c => c.r == 120 && c.g == 200), "XPS FBX vertex colors");
                Check(xpsSkin.sharedMesh.uv.All(uv => Vector2.Distance(uv, new Vector2(.6f, .3f)) < .0001f) && xpsSkin.sharedMesh.uv2.All(uv => Vector2.Distance(uv, new Vector2(.2f, .7f)) < .0001f), "XPS multiple UVs/main texture layer");
                if (version == 3)
                {
                    File.WriteAllBytes(xpsPath, XpsFixture.Create(humanoid:true));
                    var avatarResult = PmxConverter.Convert(xpsPath, "Assets/Converted", new ConversionOptions { Scale=1 });
                    Check(avatarResult.ConversionSucceeded && avatarResult.HumanoidValid, "XPS humanoid aliases: " + string.Join("; ", avatarResult.Errors));
                    var xpsInstance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(avatarResult.Prefab));
                    var xpsAnimator = xpsInstance.GetComponent<Animator>(); var xpsRenderer = xpsInstance.GetComponentInChildren<SkinnedMeshRenderer>();
                    var xpsBaked = new Mesh(); xpsRenderer.BakeMesh(xpsBaked); var rest = xpsBaked.vertices;
                    Check(rest.Zip(xpsRenderer.sharedMesh.vertices, (a,b)=>Vector3.Distance(a,b)).Max() < .001f, "XPS T pose bind pose");
                    xpsAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm).localRotation *= Quaternion.Euler(0,0,30);
                    xpsRenderer.BakeMesh(xpsBaked); Check(xpsBaked.vertices.Zip(rest,(a,b)=>Vector3.Distance(a,b)).Max() > .05f, "XPS animated skin");
                    UnityEngine.Object.DestroyImmediate(xpsBaked); UnityEngine.Object.DestroyImmediate(xpsInstance);
                }
                File.WriteAllBytes(xpsPath, XpsFixture.Create(version, version == 4, noBones:true));
                var staticResult = PmxConverter.Convert(xpsPath, "Assets/Converted", new ConversionOptions { VrChat=false, Scale=1 });
                Check(staticResult.ConversionSucceeded, "XPS unrigged " + version + ": " + string.Join("; ", staticResult.Errors));
            }
            string damagedPath = "Assets/Fixtures/repaired-normals.xps";
            File.WriteAllBytes(damagedPath, XpsFixture.Create(badNormalKind:1).Concat(new byte[] { 1, 64, 1, 32, 0 }).ToArray());
            var repairedNormals = XpsConverter.Convert(damagedPath, "Assets/Converted");
            Check(repairedNormals.ConversionSucceeded && repairedNormals.Warnings.Any(w => w.Contains("法线")) && repairedNormals.Warnings.Any(w => w.Contains("末尾")), "Repair invalid normals and report footer");
            var repairedMesh = AssetDatabase.LoadAssetAtPath<GameObject>(repairedNormals.Prefab).GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh;
            Check(repairedMesh.normals.All(n => Finite(n.x) && Finite(n.y) && Finite(n.z) && n.sqrMagnitude > .9f), "Repaired FBX normals remain finite and normalized");
            CheckTriangleValidation();
            CheckXpsRigInference();
            CheckMaterialTransparency();
            var args = Environment.GetCommandLineArgs(); int externalFlag = Array.IndexOf(args, "-pmx2fbxRealXps");
            if (externalFlag >= 0)
            {
                if (externalFlag + 1 >= args.Length) throw new ArgumentException("Missing local XPS model path.");
                VerifyExternalXps(args[externalFlag + 1]);
            }
            string shaderFolder = AssetDatabase.IsValidFolder("Assets/PMX2FBX/Shaders") ? "Assets/PMX2FBX/Shaders/" : "Packages/com.pmx2fbx.converter/Shaders/";
            foreach (var path in new[] { shaderFolder + "PcToon.shader", shaderFolder + "PcToonTransparent.shader" })
            {
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                Check(shader && !ShaderUtil.ShaderHasError(shader), "Shader compilation: " + path);
            }
            File.WriteAllText("validation-result.json", "{\"passed\":true,\"unity\":\"" + Application.unityVersion + "\",\"checks\":\"PMX regression; XPS legacy/2.12/2.15/3.15/ASCII generic + unrigged roundtrip, humanoid, animated skin, vertex colors, multiple UVs, material textures; shaders\"}");
            Debug.Log("PMX2FBX_UNITY_VALIDATION_PASS"); EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            File.WriteAllText("validation-result.json", "{\"passed\":false,\"error\":\"" + e.Message.Replace("\"", "'") + "\"}");
            Debug.LogException(e); EditorApplication.Exit(1);
        }
    }
    private static void Check(bool condition, string label) { if (!condition) throw new Exception(label); }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static void CheckMaterialTransparency()
    {
        string path = "Assets/Fixtures/alpha-materials.xps";
        var texture = new Texture2D(2,2,TextureFormat.RGBA32,false);
        try
        {
            texture.SetPixels(Enumerable.Repeat(new Color(1,0,0,1),4).ToArray()); texture.Apply();
            File.WriteAllBytes("Assets/Fixtures/missing.png", texture.EncodeToPNG()); AssetDatabase.Refresh();
            File.WriteAllBytes(path, XpsFixture.Create(firstRenderGroup:5, secondRenderGroup:7, firstVertexAlpha:true));
            var result = XpsConverter.Convert(path, "Assets/Converted");
            Check(result.ConversionSucceeded, "Explicit XPS alpha modes: " + string.Join("; ",result.Errors));
            var materials = AssetDatabase.LoadAssetAtPath<GameObject>(result.Prefab).GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterials;
            Check(materials[0].shader.name == "PMX2FBX/PC Toon" && materials[1].shader.name == "PMX2FBX/PC Toon Transparent", "Authored render-group opacity overrides texture/vertex alpha");
            texture.SetPixel(0,0,new Color(1,0,0,0)); texture.Apply();
            File.WriteAllBytes("Assets/Fixtures/missing.png", texture.EncodeToPNG()); AssetDatabase.Refresh();
            result = XpsConverter.Convert(path, "Assets/Converted");
            Check(result.ConversionSucceeded, "XPS opacity with zero-alpha atlas pixels");
            materials = AssetDatabase.LoadAssetAtPath<GameObject>(result.Prefab).GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterials;
            Check(materials[0].renderQueue < 3000 && materials[1].renderQueue >= 3000, "Zero-alpha atlas does not turn opaque XPS group transparent");
            File.WriteAllBytes(path, XpsFixture.Create());
            result = XpsConverter.Convert(path, "Assets/Converted");
            Check(result.ConversionSucceeded, "Unknown XPS material alpha analysis");
            materials = AssetDatabase.LoadAssetAtPath<GameObject>(result.Prefab).GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterials;
            Check(materials.All(m => m.shader.name == "PMX2FBX/PC Toon Transparent"), "Unknown materials retain actual image transparency");
            texture.SetPixel(0,0,Color.red); texture.Apply();
            File.WriteAllBytes("Assets/Fixtures/missing.png", texture.EncodeToPNG()); AssetDatabase.Refresh();
            File.WriteAllBytes(path, XpsFixture.Create(firstVertexAlpha:true));
            result = XpsConverter.Convert(path, "Assets/Converted");
            Check(result.ConversionSucceeded, "Per-material vertex alpha analysis");
            materials = AssetDatabase.LoadAssetAtPath<GameObject>(result.Prefab).GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterials;
            Check(materials[0].shader.name == "PMX2FBX/PC Toon Transparent" && materials[1].shader.name == "PMX2FBX/PC Toon", "Transparent vertices in one material do not affect another; all-255 RGBA stays opaque");
            Check(materials.All(m => !(AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(m.mainTexture)) as TextureImporter).isReadable), "Texture readability restored after pixel analysis");
        }
        finally { UnityEngine.Object.DestroyImmediate(texture); }
    }
    private static void CheckXpsRigInference()
    {
        var bytes = XpsFixture.Create(humanoid:true, numberedRig:true);
        var source = XpsReader.Read(new MemoryStream(bytes));
        var map = HumanoidRig.AutoMap(source);
        Check(HumanoidRig.Required.All(h => map.ContainsKey(h)), "Numbered XPS limbs and unnamed torso map all required slots");
        Check(map[HumanBodyBones.Spine] == 1 && map[HumanBodyBones.Chest] == 2 && map[HumanBodyBones.Neck] == 3, "Infer torso from shared head/arms/hips chain");
        source.Bones = source.Bones.Concat(new[] {new PmxBone { Name="unused accessory arm left shoulder 2", Parent=0 }}).ToArray();
        Check(HumanoidRig.AutoMap(source)[HumanBodyBones.LeftUpperArm] == 6, "Accessory name suffix cannot override exact primary limb");
        string path = "Assets/Fixtures/numbered-rig.xps"; File.WriteAllBytes(path, bytes);
        var result = XpsConverter.Convert(path, "Assets/Converted", new ConversionOptions());
        Check(result.ConversionSucceeded && result.HumanoidValid && result.Warnings.Any(w => w.Contains("共同父子层级")), "Numbered XPS Humanoid export/reimport");
        source.Bones[9].Parent = -1;
        Check(!HumanoidRig.AutoMap(source).ContainsKey(HumanBodyBones.Spine), "Disconnected arm prevents torso inference");
        source = XpsReader.Read(new MemoryStream(bytes));
        int helper = source.Bones.Length;
        source.Bones = source.Bones.Concat(new[] {new PmxBone { Name="unnamed helper", Parent=0, Position=new Float3(0,.85f,0) }}).ToArray();
        source.Bones[1].Parent = helper;
        Check(!HumanoidRig.AutoMap(source).ContainsKey(HumanBodyBones.Spine), "Ambiguous spine chain remains unmapped");
        source = XpsReader.Read(new MemoryStream(bytes));
        source.Format = "PMX";
        Check(!HumanoidRig.AutoMap(source).ContainsKey(HumanBodyBones.Spine), "XPS inference does not change PMX mapping");
        var options = new ConversionOptions(); options.BoneOverrides.Add(new BoneOverride { Human=HumanBodyBones.Spine, PmxIndex=-1 });
        var overridden = XpsConverter.Convert(path, "Assets/Converted", options);
        Check(!overridden.ConversionSucceeded && overridden.Errors.Any(e => e.Contains("Spine")), "Manual unmapped override remains authoritative");
    }
    private static void CheckTriangleValidation()
    {
        var root = new GameObject("TriangleValidation"); var original = new Mesh(); var imported = new Mesh();
        try
        {
            original.vertices = imported.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            original.triangles = new[] { 0, 1, 2, 0, 0, 1 }; imported.triangles = new[] { 0, 1, 2 };
            imported.boneWeights = Enumerable.Repeat(new BoneWeight { weight0=1 }, 3).ToArray();
            var renderer = root.AddComponent<SkinnedMeshRenderer>(); renderer.sharedMesh = imported; renderer.bones = new[] {root.transform};
            var verify = typeof(PmxConverter).GetMethod("VerifyImported", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var report = new ConversionReport(); verify.Invoke(null, new object[] {root, original, report});
            Check(report.Triangles == 1 && report.Warnings.Any(w => w.Contains("退化")), "Accept only confirmed degenerate-face cleanup");
            original.triangles = new[] { 0, 1, 2, 0, 2, 1 };
            bool rejected = false;
            try { verify.Invoke(null, new object[] {root, original, new ConversionReport()}); }
            catch (System.Reflection.TargetInvocationException e) { rejected = e.InnerException is InvalidOperationException; }
            Check(rejected, "Reject removal of a valid triangle");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(original); UnityEngine.Object.DestroyImmediate(imported); }
    }
    private static void VerifyExternalXps(string sourcePath)
    {
        var source = XpsReader.Read(sourcePath);
        var result = XpsConverter.Convert(sourcePath, "Assets/ExternalValidation");
        Check(result.ConversionSucceeded, "Local XPS round trip: " + string.Join("; ", result.Errors));
        var skin = AssetDatabase.LoadAssetAtPath<GameObject>(result.Prefab).GetComponentInChildren<SkinnedMeshRenderer>();
        var mesh = skin.sharedMesh;
        Check(mesh.triangles.Length / 3 == result.Triangles && mesh.subMeshCount == source.Materials.Length && skin.bones.Length == source.Bones.Length, "Local XPS faces/material slots/bones verified");
        Check(mesh.normals.All(n => Finite(n.x) && Finite(n.y) && Finite(n.z) && n.sqrMagnitude > .9f), "Local XPS finite normals");
        Check(skin.sharedMaterials.All(m => m && m.mainTexture), "Local XPS texture references retained");
        for (int i=0; i<source.Materials.Length; i++)
            if (source.Materials[i].AlphaMode == MaterialAlphaMode.Opaque)
                Check(skin.sharedMaterials[i].shader.name == "PMX2FBX/PC Toon" && skin.sharedMaterials[i].renderQueue < 3000, "Local XPS opaque render group preserved: " + i);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(result.Prefab));
        var baked = new Mesh(); instance.GetComponentInChildren<SkinnedMeshRenderer>().BakeMesh(baked);
        Check(baked.vertices.Zip(mesh.vertices,(a,b)=>Vector3.Distance(a,b)).Max() < .001f, "Local XPS bind pose after reimport");
        UnityEngine.Object.DestroyImmediate(baked); UnityEngine.Object.DestroyImmediate(instance);
        File.WriteAllText("external-model-validation-result.json", result.ToJson());
        var map = HumanoidRig.AutoMap(source);
        var missing = HumanoidRig.Required.Where(h => !map.ContainsKey(h)).ToArray();
        Debug.Log("PMX2FBX_LOCAL_XPS_VALIDATION_PASS: " + result.Fbx + "; auto-mapping missing: " + string.Join(", ", missing));
        if (Environment.GetCommandLineArgs().Contains("-pmx2fbxRealXpsVrChat")) VerifyExternalXpsVrChat(sourcePath);
    }
    private static void VerifyExternalXpsVrChat(string sourcePath)
    {
        var report = XpsConverter.Convert(sourcePath, "Assets/ExternalVrChat", new ConversionOptions());
        File.WriteAllText("external-vrchat-validation-result.json", report.ToJson());
        Check(report.ConversionSucceeded && report.HumanoidValid, "Local XPS VRChat conversion: " + string.Join("; ",report.Errors));
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(report.Prefab));
        var baked = new Mesh();
        try
        {
            var animator = instance.GetComponent<Animator>(); var renderer = instance.GetComponentInChildren<SkinnedMeshRenderer>();
            var mesh = renderer.sharedMesh;
            Check(animator && animator.avatar && animator.avatar.isValid && animator.avatar.isHuman, "Local VRChat imported Avatar valid");
            Check(renderer.sharedMaterials.All(m => m && m.mainTexture), "Local VRChat material texture references");
            var source = XpsReader.Read(sourcePath);
            for (int i=0; i<source.Materials.Length; i++)
                if (source.Materials[i].AlphaMode == MaterialAlphaMode.Opaque)
                    Check(renderer.sharedMaterials[i].shader.name == "PMX2FBX/PC Toon" && renderer.sharedMaterials[i].renderQueue < 3000, "Local VRChat opaque render group preserved: " + i);
            Check(mesh.normals.All(n => Finite(n.x) && Finite(n.y) && Finite(n.z) && n.sqrMagnitude > .9f), "Local VRChat finite normals after T Pose");
            Check(Math.Abs(mesh.bounds.size.y - 1.65f) < .03f, "Local VRChat meter scale");
            renderer.BakeMesh(baked); var rest = baked.vertices;
            Check(rest.Zip(mesh.vertices,(a,b)=>Vector3.Distance(a,b)).Max() < .001f, "Local VRChat T Pose bind matrices match");
            foreach (var slot in new[] {HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm, HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg})
            {
                var joint = animator.GetBoneTransform(slot); var rotation = joint.localRotation;
                joint.localRotation *= Quaternion.Euler(0,0,20); renderer.BakeMesh(baked);
                Check(baked.vertices.Zip(rest,(a,b)=>Vector3.Distance(a,b)).Max() > .001f, "Local VRChat animated skin: " + slot);
                joint.localRotation = rotation;
            }
            Check((animator.GetBoneTransform(HumanBodyBones.LeftLowerArm).position - animator.GetBoneTransform(HumanBodyBones.LeftUpperArm).position).normalized.x < -.99f
                && (animator.GetBoneTransform(HumanBodyBones.RightLowerArm).position - animator.GetBoneTransform(HumanBodyBones.RightUpperArm).position).normalized.x > .99f, "Local VRChat bilateral T Pose");
        }
        finally { UnityEngine.Object.DestroyImmediate(baked); UnityEngine.Object.DestroyImmediate(instance); }
        Debug.Log("PMX2FBX_LOCAL_VRCHAT_VALIDATION_PASS: " + report.Prefab);
    }
}
