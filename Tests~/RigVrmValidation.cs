using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Pmx2Fbx;
using Pmx2Fbx.Core;
using Pmx2Fbx.Tests;
using UnityEditor;
using UnityEngine;

public static class RigVrmValidation
{
    public static void RunStandalone()
    {
        try { Run(); MaterialRenderValidation.Run(); }
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
    private static int checks;
    private static void Check(bool ok,string message){checks++;if(!ok)throw new Exception("Rig/VRM: "+message);}
    private static PmxModel Rig()=>PmxReader.Read(new MemoryStream(PmxFixture.Create(humanoid:true)));
    public static void Run()
    {
        CheckRig();CheckVrm();Debug.Log("PMX2FBX_RIG_VRM_VALIDATION_PASS checks="+checks);
    }
    private static void CheckRig()
    {
        string[] mixamo={"Hips","Spine","Spine1","Neck","Head","LeftShoulder","LeftArm","LeftForeArm","LeftHand","RightShoulder","RightArm","RightForeArm","RightHand","LeftUpLeg","LeftLeg","LeftFoot","RightUpLeg","RightLeg","RightFoot"};
        foreach(string prefix in new[]{"mixamorig:","Character|mixamorig:","mixamorig"})
        {
            var rig=Rig();for(int i=0;i<rig.Bones.Length;i++)rig.Bones[i].Name=prefix+mixamo[i];
            // Mixamo's LeftLeg/RightLeg mean lower leg, while MMD aliases formerly used LeftLeg for upper leg.
            var map=HumanoidRig.AutoMap(rig);
            Check(HumanoidRig.Required.All(map.ContainsKey),"namespaced Mixamo maps full body "+prefix);
            Check(map[HumanBodyBones.LeftLowerLeg]==14&&map[HumanBodyBones.LeftUpperLeg]==13,"Mixamo leg chain");
        }
        foreach(bool vroid in new[]{true,false})
        {
            var rig=Rig();var initial=HumanoidRig.AutoMap(rig);
            foreach(var pair in initial)
            {
                string name=pair.Key.ToString();bool left=name.StartsWith("Left"),right=name.StartsWith("Right");
                rig.Bones[pair.Value].Name=vroid ? "J_Bip_"+(left?"L_":right?"R_":"C_")+(left?name.Substring(4):right?name.Substring(5):name)
                    : left||right ? "ValveBiped.Bip01_"+(left?"L_":"R_")+(name.Contains("UpperLeg")?"Thigh":name.Contains("LowerLeg")?"Calf":name.Contains("Shoulder")?"Clavicle":name.Contains("LowerArm")?"Forearm":left?name.Substring(4):name.Substring(5)) : "ValveBiped.Bip01_"+name;
            }
            var list=rig.Bones.ToList();int parent=8;
            for(int segment=0;segment<3;segment++)
            {
                list.Add(new PmxBone{Name=vroid?"J_Bip_L_Index"+(segment+1):"ValveBiped.Bip01_L_Finger1"+(segment==0?"":segment.ToString()),EnglishName="",Parent=parent,Position=new Float3(.62f+segment*.03f,1.1f,0)});parent=list.Count-1;
            }
            rig.Bones=list.ToArray();var mapped=HumanoidRig.AutoMap(rig);
            Check(HumanoidRig.Required.All(mapped.ContainsKey),"VRoid/Bip full body "+vroid);
            Check(mapped[HumanBodyBones.LeftIndexProximal]==19&&mapped[HumanBodyBones.LeftIndexIntermediate]==20&&mapped[HumanBodyBones.LeftIndexDistal]==21,"VRoid/Bip finger numbering "+vroid);
        }
        var source=Rig();source.Bones=source.Bones.Concat(new[]{new PmxBone{Name="左腕",EnglishName="",Parent=-1,Position=new Float3(9,9,9)}}).ToArray();
        Check(HumanoidRig.AutoMap(source)[HumanBodyBones.LeftUpperArm]==6,"duplicate arm disambiguated by chain");
        source=Rig();source.Bones=source.Bones.Concat(new[]{new PmxBone{Name="左手首",EnglishName="",Parent=7,Position=source.Bones[8].Position}}).ToArray();
        Check(!HumanoidRig.AutoMap(source).ContainsKey(HumanBodyBones.LeftHand),"equivalent duplicate hands stay unmapped");
        source=Rig();source.Bones[7].Name="左腕捩";source.Bones[7].EnglishName="";
        Check(!HumanoidRig.AutoMap(source).ContainsKey(HumanBodyBones.LeftLowerArm),"twist names cannot masquerade as elbow");
        foreach(int first in new[]{0,1})
        {
            source=Rig();var list=source.Bones.ToList();
            foreach(string finger in new[]{"親指","人差指"})
            {
                int parent=8;for(int segment=0;segment<3;segment++)
                {
                    int number=finger=="親指"?first+segment:1+segment;
                    list.Add(new PmxBone{Name="左"+finger+(char)('０'+number),EnglishName="",Parent=parent,Position=new Float3(.62f+segment*.03f,1.1f,finger=="親指"?.03f:0)});parent=list.Count-1;
                }
            }
            source.Bones=list.ToArray();var map=HumanoidRig.AutoMap(source);
            Check(map[HumanBodyBones.LeftThumbProximal]==19&&map[HumanBodyBones.LeftThumbIntermediate]==20&&map[HumanBodyBones.LeftThumbDistal]==21,"MMD thumb numbering "+first);
            Check(map[HumanBodyBones.LeftIndexProximal]==22&&map[HumanBodyBones.LeftIndexIntermediate]==23&&map[HumanBodyBones.LeftIndexDistal]==24,"full width Japanese index fingers");
        }
        source=Rig();int twist=source.Bones.Length;source.Bones=source.Bones.Concat(new[]{new PmxBone{Name="左腕捩",EnglishName="",Parent=6,Position=new Float3(.3f,1.32f,0)}}).ToArray();source.Bones[7].Parent=twist;
        var root=new GameObject("HelperChainRegression");
        try
        {
            var bones=source.Bones.Select(b=>{var t=new GameObject("bone").transform;t.SetParent(root.transform);t.position=new Vector3(b.Position.X,b.Position.Y,b.Position.Z);return t;}).ToArray();
            for(int i=0;i<bones.Length;i++)if(source.Bones[i].Parent>=0)bones[i].SetParent(bones[source.Bones[i].Parent],true);
            var map=HumanoidRig.Map(source,bones,new ConversionOptions(),new ConversionReport());HumanoidRig.Repair(root,map,new ConversionReport());
            Check(bones[7].parent==bones[twist]&&bones[twist].parent==bones[6],"repair retains twist chain");
            var before=bones[twist].position;HumanoidRig.SetTPose(map);Check(Vector3.Distance(before,bones[twist].position)>.01f,"twist helper follows upper arm in T pose");
            var options=new ConversionOptions();options.BoneOverrides.Add(new BoneOverride{Human=HumanBodyBones.LeftLowerArm,PmxIndex=10});
            bool rejected=false;try{HumanoidRig.Map(source,bones,options);}catch(InvalidDataException){rejected=true;}Check(rejected,"crossed manual limb is rejected before repair");
        }
        finally{UnityEngine.Object.DestroyImmediate(root);}
        string twistPath="Assets/Fixtures/twist-chain.pmx";File.WriteAllBytes(twistPath,PmxFixture.Create(humanoid:true,withTwist:true));
        var roundtrip=PmxConverter.Convert(twistPath,"Assets/Converted",new ConversionOptions());
        Check(roundtrip.ConversionSucceeded&&roundtrip.HumanoidValid,"helper chain FBX roundtrip: "+string.Join(";",roundtrip.Errors));
        var avatar=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(roundtrip.Prefab));
        try
        {
            var animator=avatar.GetComponent<Animator>();var lower=animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);var upper=animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            Check(lower.parent!=upper&&lower.IsChildOf(upper)&&lower.parent.name.Contains("00019"),"exported twist remains between upper/lower arm");
            var skin=avatar.GetComponentInChildren<SkinnedMeshRenderer>();var baked=new Mesh();skin.BakeMesh(baked);var rest=baked.vertices;
            Check(rest.Zip(skin.sharedMesh.vertices,Vector3.Distance).Max()<.001,"twist-weighted vertex bind pose roundtrip");
            var position=lower.parent.position;upper.localRotation*=Quaternion.Euler(0,0,30);skin.BakeMesh(baked);
            Check(Vector3.Distance(position,lower.parent.position)>.01&&rest.Zip(baked.vertices,Vector3.Distance).Max()>.03,"exported helper and weighted mesh follow arm motion");
            UnityEngine.Object.DestroyImmediate(baked);
        }
        finally{UnityEngine.Object.DestroyImmediate(avatar);}
    }
    private static void CheckVrm()
    {
        for(int version=0;version<=1;version++)
        {
            string path="Assets/Fixtures/synthetic"+version+".vrm";var bytes=VrmFixture.Create(version==1,sparse:version==1,interleaved:version==0);File.WriteAllBytes(path,bytes);
            var source=VrmReader.Read(new MemoryStream(bytes));var map=HumanoidRig.AutoMap(source);
            Check(source.Format=="VRM"&&source.Vertices.Length==12&&source.Indices.Length==12,"VRM geometry "+version);
            Check(version==0 ? source.Axis.Z==-1&&source.Axis.X==1 : source.Axis.Z==1&&source.Axis.X==-1,"version-specific coordinate convention "+version);
            Check(map.Count==19&&map[HumanBodyBones.Head]==4,"authoritative humanoid mapping with opaque names "+version);
            var q=source.Bones[8].Rotation;Check(Quaternion.Angle(new Quaternion(q[0],q[1],q[2],q[3]),Quaternion.Euler(0,35,0))<.01,"non-identity source joint orientation "+version);
            Check(source.Materials.Length==1&&source.Materials[0].AlphaMode==MaterialAlphaMode.Cutout&&Math.Abs(source.Materials[0].Cutoff-.37f)<.001,"VRM mask mode/cutoff "+version);
            Check(source.EmbeddedTextures.Count==1&&source.Morphs.Length==1&&Math.Abs(MorphResolver.Resolve(source,0,out _)[5].Y-.03f)<.001,"embedded image and morph "+version);
            Check(Math.Abs(source.Vertices[0].Position.Z)<.01,"skinned mesh node transform not applied twice "+version);
            foreach(bool humanoid in new[]{false,true})
            {
                var result=VrmConverter.Convert(path,"Assets/Converted",new ConversionOptions{VrChat=humanoid,Scale=1});
                Check(result.ConversionSucceeded&&(!humanoid||result.HumanoidValid),"VRM FBX roundtrip "+version+" humanoid="+humanoid+": "+string.Join(";",result.Errors));
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(result.Prefab);var instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab);
                try
                {
                    var skin=instance.GetComponentInChildren<SkinnedMeshRenderer>();var baked=new Mesh();skin.BakeMesh(baked);
                    Check(skin.sharedMaterials[0].mainTexture&&Math.Abs(skin.sharedMaterials[0].GetFloat("_Cutoff")-.37f)<.001,"embedded texture and authored cutoff survive FBX "+version);
                    Check(baked.vertices.Zip(skin.sharedMesh.vertices,Vector3.Distance).Max()<.001,"VRM bind pose survives FBX "+version);
                    var rest=baked.vertices;skin.SetBlendShapeWeight(0,100);skin.BakeMesh(baked);Check(rest.Zip(baked.vertices,Vector3.Distance).Max()>.01,"VRM morph deforms skin "+version);skin.SetBlendShapeWeight(0,0);
                    if(humanoid){instance.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.LeftUpperArm).localRotation*=Quaternion.Euler(0,0,30);skin.BakeMesh(baked);Check(rest.Zip(baked.vertices,Vector3.Distance).Max()>.05,"VRM arm deforms skin "+version);}
                    UnityEngine.Object.DestroyImmediate(baked);
                }
                finally{UnityEngine.Object.DestroyImmediate(instance);}
            }
        }
        foreach(var bytes in new[]{VrmFixture.Create(true,brokenJoint:true),VrmFixture.Create(false,duplicateHuman:true),VrmFixture.Create(true).Take(40).ToArray()})
        {bool rejected=false;try{VrmReader.Read(new MemoryStream(bytes));}catch(InvalidDataException){rejected=true;}catch(EndOfStreamException){rejected=true;}Check(rejected,"malformed VRM is rejected");}
        var metadata=Rig();metadata.HumanBones.Add("hips",0);metadata.HumanBones.Add("head",4);Check(HumanoidRig.AutoMap(metadata).Count==2,"VRM optional bones are never guessed from names");
        // Thumb metadata semantics differ between VRM 1 and Unity.
        string thumbJson="{\"x\":1,\"x\":2}";bool badJson=false;try{JsonValue.Parse(thumbJson);}catch(InvalidDataException){badJson=true;}Check(badJson,"duplicate JSON keys rejected");
    }
}
