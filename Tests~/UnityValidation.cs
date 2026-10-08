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
}
