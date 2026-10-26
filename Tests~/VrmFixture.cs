using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Pmx2Fbx.Core;
using UnityEngine;

namespace Pmx2Fbx.Tests
{
    // Synthetic GLB, with deliberately unrecognizable joint names: only VRM metadata can map it.
    public static class VrmFixture
    {
        public static byte[] Create(bool v1, bool sparse = false, bool brokenJoint = false, bool duplicateHuman = false, bool interleaved = false)
        {
            var source=PmxReader.Read(new MemoryStream(PmxFixture.Create(humanoid:true)));
            string[] slots={"hips","spine","chest","neck","head","leftShoulder","leftUpperArm","leftLowerArm","leftHand","rightShoulder","rightUpperArm","rightLowerArm","rightHand","leftUpperLeg","leftLowerLeg","leftFoot","rightUpperLeg","rightLowerLeg","rightFoot"};
            using(var bytes=new MemoryStream())using(var b=new BinaryWriter(bytes))
            {
                var views=new List<string>();var accessors=new List<string>();
                Func<byte[],int,int,int> view=(data,stride,target)=>{while(bytes.Position%4!=0)b.Write((byte)0);int offset=(int)bytes.Position;b.Write(data);views.Add("{\"buffer\":0,\"byteOffset\":"+offset+",\"byteLength\":"+data.Length+(stride>0?",\"byteStride\":"+stride:"")+"}");return views.Count-1;};
                Func<float[][],string,int> floats=(data,type)=>{using(var s=new MemoryStream())using(var w=new BinaryWriter(s)){foreach(var row in data)foreach(var f in row)w.Write(f);int v=view(s.ToArray(),0,0);accessors.Add("{\"bufferView\":"+v+",\"componentType\":5126,\"count\":"+data.Length+",\"type\":\""+type+"\"}");return accessors.Count-1;}};
                var positions=source.Vertices.Select(v=>new[]{v.Position.X,v.Position.Y,v.Position.Z}).ToArray();int position;
                if(interleaved)
                {
                    using(var s=new MemoryStream())using(var w=new BinaryWriter(s)){foreach(var p in positions){foreach(float f in p)w.Write(f);w.Write(123f);}int v=view(s.ToArray(),16,0);accessors.Add("{\"bufferView\":"+v+",\"componentType\":5126,\"count\":"+positions.Length+",\"type\":\"VEC3\"}");position=accessors.Count-1;}
                }
                else position=floats(positions,"VEC3");
                int normal=floats(source.Vertices.Select(v=>new[]{0f,0f,1f}).ToArray(),"VEC3"),uv=floats(source.Vertices.Select(v=>new[]{.25f,.75f}).ToArray(),"VEC2");
                var jointData=source.Vertices.SelectMany(v=>new[]{(byte)(brokenJoint?250:v.Bones[0]),(byte)0,(byte)0,(byte)0}).ToArray();int jv=view(jointData,0,0);
                accessors.Add("{\"bufferView\":"+jv+",\"componentType\":5121,\"count\":"+positions.Length+",\"type\":\"VEC4\"}");int joints=accessors.Count-1;
                int wv=view(Enumerable.Range(0,positions.Length).SelectMany(i=>new byte[]{255,0,0,0}).ToArray(),0,0);
                accessors.Add("{\"bufferView\":"+wv+",\"componentType\":5121,\"normalized\":true,\"count\":"+positions.Length+",\"type\":\"VEC4\"}");int weights=accessors.Count-1;
                int iv=view(Enumerable.Range(0,positions.Length).SelectMany(i=>BitConverter.GetBytes((ushort)i)).ToArray(),0,0);
                accessors.Add("{\"bufferView\":"+iv+",\"componentType\":5123,\"count\":"+positions.Length+",\"type\":\"SCALAR\"}");int indices=accessors.Count-1;
                var inverse=source.Bones.Select((bone,i)=>{var p=bone.Position;var m=Matrix4x4.TRS(new Vector3(p.X,p.Y,p.Z),i==8?Quaternion.Euler(0,35,0):Quaternion.identity,Vector3.one).inverse;return Enumerable.Range(0,16).Select(k=>m[k]).ToArray();}).ToArray();int ibm=floats(inverse,"MAT4");
                int morph;
                if(sparse)
                {
                    int ids=view(new byte[]{5},0,0),values=view(new[]{0f,.03f,0f}.SelectMany(BitConverter.GetBytes).ToArray(),0,0);
                    accessors.Add("{\"componentType\":5126,\"count\":"+positions.Length+",\"type\":\"VEC3\",\"sparse\":{\"count\":1,\"indices\":{\"bufferView\":"+ids+",\"componentType\":5121},\"values\":{\"bufferView\":"+values+"}}}");morph=accessors.Count-1;
                }
                else morph=floats(positions.Select((p,i)=>new[]{0f,i==5?.03f:0f,0f}).ToArray(),"VEC3");
                var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);texture.SetPixels(new[]{Color.red,Color.green,Color.blue,Color.white});texture.Apply();int image=view(texture.EncodeToPNG(),0,0);UnityEngine.Object.DestroyImmediate(texture);
                Func<float,string> num=f=>f.ToString("R",CultureInfo.InvariantCulture);var nodes=new List<string>();
                for(int i=0;i<source.Bones.Length;i++)
                {
                    var p=source.Bones[i].Position;int parent=i==1?0:source.Bones[i].Parent;
                    if(parent>=0){var q=source.Bones[parent].Position;p=new Float3(p.X-q.X,p.Y-q.Y,p.Z-q.Z);}
                    var children=Enumerable.Range(0,source.Bones.Length).Where(c=>(c==1?0:source.Bones[c].Parent)==i);
                    var rotation=Quaternion.Euler(0,35,0);
                    nodes.Add("{\"name\":\"joint_"+i+"\",\"translation\":["+num(p.X)+","+num(p.Y)+","+num(p.Z)+"],"+(i==8?"\"rotation\":[0,"+num(rotation.y)+",0,"+num(rotation.w)+"],":"")+"\"children\":["+string.Join(",",children)+"]}");
                }
                nodes.Add("{\"name\":\"MeshNode\",\"mesh\":0,\"skin\":0,\"translation\":[0,0,10]}");
                var human=v1?"{\"specVersion\":\"1.0\",\"meta\":{\"name\":\"Synthetic VRM 1\"},\"humanoid\":{\"humanBones\":{"+string.Join(",",slots.Select((s,i)=>"\""+s+"\":{\"node\":"+(duplicateHuman&&i==4?3:i)+"}"))+"}}}":
                    "{\"specVersion\":\"0.0\",\"meta\":{\"title\":\"Synthetic VRM 0\"},\"humanoid\":{\"humanBones\":["+string.Join(",",slots.Select((s,i)=>"{\"bone\":\""+s+"\",\"node\":"+(duplicateHuman&&i==4?3:i)+"}"))+"]}}";
                int actualLength=(int)bytes.Length;
                string json="{\"asset\":{\"version\":\"2.0\"},\"extensions\":{\""+(v1?"VRMC_vrm":"VRM")+"\":"+human+"},\"scene\":0,\"scenes\":[{\"nodes\":[0,19]}],\"nodes\":["+string.Join(",",nodes)+"],\"skins\":[{\"joints\":["+string.Join(",",Enumerable.Range(0,19))+"],\"inverseBindMatrices\":"+ibm+"}],\"buffers\":[{\"byteLength\":"+actualLength+"}],\"bufferViews\":["+string.Join(",",views)+"],\"accessors\":["+string.Join(",",accessors)+"],\"images\":[{\"bufferView\":"+image+",\"mimeType\":\"image/png\"}],\"textures\":[{\"source\":0}],\"materials\":[{\"name\":\"Mask\",\"alphaMode\":\"MASK\",\"alphaCutoff\":0.37,\"doubleSided\":true,\"pbrMetallicRoughness\":{\"baseColorTexture\":{\"index\":0}}}],\"meshes\":[{\"extras\":{\"targetNames\":[\"Smile\"]},\"primitives\":[{\"attributes\":{\"POSITION\":"+position+",\"NORMAL\":"+normal+",\"TEXCOORD_0\":"+uv+",\"JOINTS_0\":"+joints+",\"WEIGHTS_0\":"+weights+"},\"indices\":"+indices+",\"material\":0,\"targets\":[{\"POSITION\":"+morph+"}]}]}]}";
                while(bytes.Length%4!=0)b.Write((byte)0);var bin=bytes.ToArray();var j=Encoding.UTF8.GetBytes(json);int jp=(j.Length+3)/4*4;
                using(var result=new MemoryStream())using(var w=new BinaryWriter(result)){w.Write(0x46546c67u);w.Write(2u);w.Write((uint)(12+8+jp+8+bin.Length));w.Write(jp);w.Write(0x4e4f534au);w.Write(j);for(int i=j.Length;i<jp;i++)w.Write((byte)32);w.Write(bin.Length);w.Write(0x004e4942u);w.Write(bin);return result.ToArray();}
            }
        }
    }
}
