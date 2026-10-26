using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Pmx2Fbx.Core;
using UnityEngine;

namespace Pmx2Fbx
{
    public static class SourceModelReader
    {
        public static bool IsVrm(string path) => string.Equals(Path.GetExtension(path), ".vrm", StringComparison.OrdinalIgnoreCase);
        public static bool IsSupported(string path) => IsVrm(path) || ModelReader.IsSupported(path);
        public static PmxModel Read(string path) => IsVrm(path) ? VrmReader.Read(path) : ModelReader.Read(path);
    }

    // Independent GLB/VRM 0.x and 1.0 reader; no runtime components or UniVRM dependency.
    public sealed class VrmReader
    {
        private JsonValue root;
        private byte[] binary;
        private Matrix4x4[] world;
        private int[] parents;
        private bool[] active;
        private PmxModel model;
        private readonly Dictionary<int,float[][]> accessorCache = new Dictionary<int,float[][]>();
        public static PmxModel Read(string path)
        {
            using (var stream=File.OpenRead(path)) return Read(stream);
        }
        public static PmxModel Read(Stream input) => new VrmReader().Load(input);
        private static void Require(bool condition, string text) { if (!condition) throw new InvalidDataException("VRM："+text); }
        private PmxModel Load(Stream input)
        {
            using (var r=new BinaryReader(input,Encoding.UTF8,true))
            {
                Require(r.ReadUInt32()==0x46546c67 && r.ReadUInt32()==2,"需要 glTF 2.0 二进制文件。");
                uint size=r.ReadUInt32(); Require(input.CanSeek && size==input.Length && size<=512*1024*1024,"GLB 文件长度无效或超过 512 MB。");
                string json=null;
                while(input.Position<input.Length)
                {
                    Require(input.Length-input.Position>=8,"GLB 块头被截断。");
                    uint length=r.ReadUInt32(), type=r.ReadUInt32(); Require(length%4==0 && length<=input.Length-input.Position,"GLB 块长度无效。");
                    var data=r.ReadBytes((int)length);
                    if(type==0x4e4f534a) { Require(json==null && input.Position==20+length,"JSON 必须是首块且不能重复。"); json=new UTF8Encoding(false,true).GetString(data); }
                    else if(type==0x004e4942) { Require(binary==null,"BIN 块重复。"); binary=data; }
                }
                Require(json!=null && binary!=null,"缺少 JSON 或 BIN 块。"); root=JsonValue.Parse(json);
            }
            Require(root["asset"]["version"].Text()=="2.0","glTF 版本不受支持。");
            var vrm1=root["extensions"]["VRMC_vrm"]; var vrm0=root["extensions"]["VRM"];
            Require(vrm1.Exists || vrm0.Exists,"缺少 VRM 人形扩展。");
            if(vrm1.Exists) Require(vrm1["specVersion"].Text()=="1.0","只支持 VRM 1.0 正式规范。");
            foreach(var ext in Enumerable.Range(0,root["extensionsRequired"].Count).Select(i=>root["extensionsRequired"][i].Text()))
                Require(new[]{"VRMC_vrm","VRM","VRMC_materials_mtoon","KHR_materials_unlit","KHR_texture_transform"}.Contains(ext),"暂不支持必需扩展 "+ext+"；请使用未压缩的标准 VRM。");
            Require(root["buffers"].Count==1 && !root["buffers"][0]["uri"].Exists,"VRM 必须使用单个内嵌 BIN 缓冲。");
            int bufferLength=root["buffers"][0]["byteLength"].Int(); Require(bufferLength>0 && bufferLength<=binary.Length && binary.Length-bufferLength<=3,"缓冲长度不匹配。");
            if(bufferLength<binary.Length) Array.Resize(ref binary,bufferLength);
            model=new PmxModel { Format="VRM", Version=vrm1.Exists?1:0,
                Name=(vrm1.Exists?vrm1["meta"]["name"]:vrm0["meta"]["title"]).Text("VRM Avatar"),
                // VRM 0 faces glTF -Z; VRM 1 faces glTF +Z. Both become Unity +Z.
                Axis=vrm1.Exists?new Float3(-1,1,1):new Float3(1,1,-1) };
            BuildNodes(); ReadHuman(vrm1.Exists?vrm1:vrm0,vrm1.Exists); ReadImages(); ReadMaterials(); ReadMeshes();
            model.Warnings.Add("VRM 主贴图、颜色、透明模式、蒙皮和顶点表情已转换；MToon 特效、材质/骨骼表情、LookAt、约束、SpringBone、动画与授权元数据不能等价保存到 FBX。请保留原始 VRM 及其使用许可。");
            return model;
        }
        private void BuildNodes()
        {
            int count=root["nodes"].Count; Require(count>0 && count<=100000,"节点数量无效。");
            parents=Enumerable.Repeat(-1,count).ToArray(); world=new Matrix4x4[count]; active=new bool[count];
            for(int i=0;i<count;i++) for(int c=0;c<root["nodes"][i]["children"].Count;c++)
            { int child=root["nodes"][i]["children"][c].Int(); Require(child>=0 && child<count && child!=i && parents[child]==-1,"节点父子关系无效或重复。"); parents[child]=i; }
            var state=new byte[count]; for(int i=0;i<count;i++) NodeWorld(i,state,0);
            int scene=root["scene"].Int(0); Require(scene>=0 && scene<root["scenes"].Count,"默认场景无效。");
            for(int i=0;i<root["scenes"][scene]["nodes"].Count;i++) Activate(root["scenes"][scene]["nodes"][i].Int());
            model.Bones=Enumerable.Range(0,count).Select(i=>{
                var p=world[i].MultiplyPoint3x4(Vector3.zero); var q=world[i].rotation;
                return new PmxBone {Name=root["nodes"][i]["name"].Text("Node_"+i),EnglishName="",Parent=parents[i],Position=F(p),Rotation=new[]{q.x,q.y,q.z,q.w}};
            }).ToArray();
        }
        private Matrix4x4 NodeWorld(int index,byte[] state,int depth)
        {
            Require(depth<=512 && state[index]!=1,"节点循环或层级过深。"); if(state[index]==2)return world[index]; state[index]=1;
            var n=root["nodes"][index]; Matrix4x4 local;
            if(n["matrix"].Exists)
            {
                Require(n["matrix"].Count==16 && !n["translation"].Exists && !n["rotation"].Exists && !n["scale"].Exists,"节点矩阵与 TRS 冲突。");
                local=Matrix4x4.zero; for(int i=0;i<16;i++)local[i]=n["matrix"][i].Number();
            }
            else
            {
                var t=Vector(n["translation"],Vector3.zero); var s=Vector(n["scale"],Vector3.one); var a=n["rotation"];
                var q=a.Exists?new Quaternion(a[0].Number(),a[1].Number(),a[2].Number(),a[3].Number()):Quaternion.identity;
                Require(!a.Exists || a.Count==4 && Math.Abs(Quaternion.Dot(q,q)-1)<.001,"节点旋转不是单位四元数。"); local=Matrix4x4.TRS(t,q,s);
            }
            for(int i=0;i<16;i++) Require(Finite(local[i]),"节点变换含无效数值。");
            Require(Math.Abs(local.determinant)>1e-12,"节点缩放为零。");
            world[index]=parents[index]<0?local:NodeWorld(parents[index],state,depth+1)*local;
            for(int i=0;i<16;i++)Require(Finite(world[index][i]),"节点世界变换溢出。");
            state[index]=2; return world[index];
        }
        private void Activate(int i)
        {
            Require(i>=0 && i<active.Length,"场景节点越界。"); if(active[i])return; active[i]=true;
            for(int c=0;c<root["nodes"][i]["children"].Count;c++)Activate(root["nodes"][i]["children"][c].Int());
        }
        private void ReadHuman(JsonValue extension,bool v1)
        {
            var bones=extension["humanoid"]["humanBones"];
            var entries=v1?bones.Members:bonesArray(bones);
            var seen=new HashSet<int>();
            foreach(var entry in entries)
            {
                string name=entry.Key; int node=entry.Value["node"].Int();
                Require(node>=0 && node<world.Length && active[node] && seen.Add(node),"人形节点越界、重复或不在当前场景。");
                // VRM 1 renamed the three thumb joints; Unity retains the VRM 0 names.
                if(v1 && name.Contains("Thumb")) name=name.Replace("Proximal","Intermediate").Replace("Metacarpal","Proximal");
                Require(!model.HumanBones.ContainsKey(name),"人形插槽重复。"); model.HumanBones.Add(name,node);
            }
            Require(model.HumanBones.ContainsKey("hips") && model.HumanBones.ContainsKey("head"),"缺少人形骨骼定义。");
        }
        private static IEnumerable<KeyValuePair<string,JsonValue>> bonesArray(JsonValue a)
        { for(int i=0;i<a.Count;i++)yield return new KeyValuePair<string,JsonValue>(a[i]["bone"].Text(),a[i]); }
        private byte[] View(int index)
        {
            Require(index>=0 && index<root["bufferViews"].Count,"bufferView 越界。"); var v=root["bufferViews"][index];
            int offset=v["byteOffset"].Int(0),length=v["byteLength"].Int(); Require(v["buffer"].Int()==0 && offset>=0 && length>=0 && (long)offset+length<=binary.Length,"bufferView 范围无效。");
            var result=new byte[length]; Array.Copy(binary,offset,result,0,length); return result;
        }
        private void ReadImages()
        {
            var names=new List<string>();var images=new List<byte[]>();
            for(int i=0;i<root["images"].Count;i++)
            {
                var image=root["images"][i]; byte[] data; string mime=image["mimeType"].Text();
                if(image["bufferView"].Exists) data=View(image["bufferView"].Int());
                else
                {
                    string uri=image["uri"].Text(); int comma=uri.IndexOf(',');
                    Require(uri.StartsWith("data:image/",StringComparison.Ordinal) && comma>0 && uri.Substring(0,comma).EndsWith(";base64",StringComparison.Ordinal),"只支持内嵌 PNG/JPEG 图像。");
                    mime=uri.Substring(5,comma-12); data=System.Convert.FromBase64String(uri.Substring(comma+1));
                }
                Require(mime=="image/png"||mime=="image/jpeg","不支持的图像类型 "+mime);
                names.Add("Image_"+i+(mime=="image/png"?".png":".jpg")); images.Add(data);
            }
            var textures=new List<string>();
            for(int i=0;i<root["textures"].Count;i++)
            {
                var texture=root["textures"][i];int image=texture["source"].Int();Require(image>=0&&image<images.Count,"纹理图像越界。");
                textures.Add(names[image]);model.EmbeddedTextures[i]=images[image];
                int sampler=texture["sampler"].Int();Require(sampler>=-1&&sampler<root["samplers"].Count,"纹理采样器越界。");
                var s=root["samplers"][sampler];int u=s["wrapS"].Int(10497),v=s["wrapT"].Int(10497);
                Require(new[]{10497,33071,33648}.Contains(u)&&new[]{10497,33071,33648}.Contains(v),"纹理重复模式无效。");
                model.TextureSamplers[i]=new[]{u,v,s["magFilter"].Int(9729)};
            }
            model.Textures=textures.ToArray();
        }
        private void ReadMaterials()
        {
            var materials=new List<PmxMaterial>();
            for(int i=0;i<root["materials"].Count;i++)
            {
                var m=root["materials"][i]; var pbr=m["pbrMetallicRoughness"]; var color=pbr["baseColorFactor"];
                var mode=m["alphaMode"].Text("OPAQUE"); Require(new[]{"OPAQUE","BLEND","MASK"}.Contains(mode),"alphaMode 无效。");
                int tex=pbr["baseColorTexture"]["index"].Int(), image=-1;
                if(tex>=0) { Require(tex<model.Textures.Length,"材质纹理越界。"); image=tex; }
                var mat=new PmxMaterial { Name=m["name"].Text("Material_"+i),EnglishName="",Texture=image,SphereTexture=-1,
                    Diffuse=color.Exists?Enumerable.Range(0,4).Select(c=>color[c].Number()).ToArray():new[]{1f,1f,1f,1f},
                    Flags=(byte)(m["doubleSided"].Bool()?1:0),AlphaMode=mode=="MASK"?MaterialAlphaMode.Cutout:mode=="BLEND"?MaterialAlphaMode.Blend:MaterialAlphaMode.Opaque,
                    Cutoff=m["alphaCutoff"].Number(.5f) };
                Require(!color.Exists||color.Count==4,"基础颜色长度无效。");
                Require(mat.Diffuse.All(v=>Finite(v)&&v>=0&&v<=1)&&Finite(mat.Cutoff)&&mat.Cutoff>=0&&mat.Cutoff<=1,"基础颜色或镂空阈值无效。");
                materials.Add(mat);
            }
            materials.Add(new PmxMaterial {Name="Default",EnglishName="",Texture=-1,SphereTexture=-1,Diffuse=new[]{1f,1f,1f,1f},AlphaMode=MaterialAlphaMode.Opaque});
            model.Materials=materials.ToArray();
        }
        private float[][] Accessor(int index,int components)
        {
            Require(index>=0&&index<root["accessors"].Count,"accessor 越界。");
            var a=root["accessors"][index]; string type=a["type"].Text();
            int size=type=="SCALAR"?1:type=="VEC2"?2:type=="VEC3"?3:type=="VEC4"?4:type=="MAT4"?16:0;
            Require(size==components,"accessor 类型不匹配。"); if(accessorCache.TryGetValue(index,out var cached))return cached;
            int count=a["count"].Int(),ct=a["componentType"].Int(); Require(count>0&&(long)count*size<=16000000,"accessor 元素数无效或过大。");
            ComponentSize(ct); var data=new float[count][]; for(int i=0;i<count;i++)data[i]=new float[size];
            Require(!a["normalized"].Bool()||ct==5120||ct==5121||ct==5122||ct==5123,"normalized componentType 无效。");
            if(a["bufferView"].Exists)ReadElements(data,0,count,size,ct,a["normalized"].Bool(),a["bufferView"].Int(),a["byteOffset"].Int(0),null);
            else Require(!a["byteOffset"].Exists && a["sparse"].Exists,"accessor 缺少缓冲。");
            if(a["sparse"].Exists)
            {
                var s=a["sparse"];int n=s["count"].Int(),ict=s["indices"]["componentType"].Int();Require(n>0&&n<=count&&(ict==5121||ict==5123||ict==5125),"稀疏 accessor 索引无效。");
                var ids=new float[n][];for(int i=0;i<n;i++)ids[i]=new float[1];
                ReadElements(ids,0,n,1,ict,false,s["indices"]["bufferView"].Int(),s["indices"]["byteOffset"].Int(0),null);
                var remap=new int[n]; for(int i=0;i<n;i++){remap[i]=CheckedIndex(ids[i][0],count);Require(i==0||remap[i]>remap[i-1],"稀疏索引必须递增。");}
                ReadElements(data,0,n,size,ct,a["normalized"].Bool(),s["values"]["bufferView"].Int(),s["values"]["byteOffset"].Int(0),remap);
            }
            accessorCache[index]=data;return data;
        }
        private void ReadElements(float[][] output,int start,int count,int size,int ct,bool normalized,int view,int offset,int[] remap)
        {
            Require(view>=0&&view<root["bufferViews"].Count,"accessor bufferView 越界。"); var v=root["bufferViews"][view];int bytes=ComponentSize(ct);
            int stride=v["byteStride"].Int(size*bytes),begin=v["byteOffset"].Int(0),length=v["byteLength"].Int();
            Require(v["buffer"].Int()==0 && stride>=size*bytes && stride<=252 && stride%bytes==0 && offset>=0 && begin>=0 && offset%bytes==0
                && (long)offset+(long)(count-1)*stride+size*bytes<=length && (long)begin+length<=binary.Length,"accessor 范围或步长无效。");
            for(int i=0;i<count;i++)for(int c=0;c<size;c++)
            {
                int p=begin+offset+i*stride+c*bytes;double n=ct==5126?BitConverter.ToSingle(binary,p):ct==5125?BitConverter.ToUInt32(binary,p):ct==5123?BitConverter.ToUInt16(binary,p):ct==5122?BitConverter.ToInt16(binary,p):ct==5121?binary[p]:(sbyte)binary[p];
                if(normalized)n=ct==5120?Math.Max(n/127,-1):ct==5122?Math.Max(n/32767,-1):ct==5121?n/255:ct==5123?n/65535:n;
                Require(!double.IsNaN(n)&&!double.IsInfinity(n)&&Math.Abs(n)<=float.MaxValue,"accessor 包含非有限数值。");
                output[remap==null?start+i:remap[i]][c]=(float)n;
            }
        }
        private static int ComponentSize(int ct)
        { Require(new[]{5120,5121,5122,5123,5125,5126}.Contains(ct),"不支持的 accessor componentType。");return ct==5120||ct==5121?1:ct==5122||ct==5123?2:4; }
        private void ReadMeshes()
        {
            var vertices=new List<PmxVertex>();var faces=model.Materials.Select(_=>new List<int>()).ToArray();var morphs=new List<PmxMorph>();
            for(int node=0;node<world.Length;node++)
            {
                var n=root["nodes"][node]; if(!active[node]||!n["mesh"].Exists)continue;
                int meshIndex=n["mesh"].Int();Require(meshIndex>=0&&meshIndex<root["meshes"].Count,"mesh 越界。");var mesh=root["meshes"][meshIndex];
                int skin=n["skin"].Int();int[] joints=null;Matrix4x4[] matrices=null;
                if(skin>=0)
                {
                    Require(skin<root["skins"].Count,"skin 越界。");var s=root["skins"][skin];joints=Enumerable.Range(0,s["joints"].Count).Select(i=>s["joints"][i].Int()).ToArray();
                    Require(joints.Length>0&&joints.Distinct().Count()==joints.Length&&joints.All(j=>j>=0&&j<world.Length),"skin joints 无效。");
                    var ibm=s["inverseBindMatrices"].Exists?Accessor(s["inverseBindMatrices"].Int(),16):null;Require(ibm==null||ibm.Length==joints.Length,"逆绑定矩阵数量不匹配。");
                    matrices=new Matrix4x4[joints.Length];for(int j=0;j<joints.Length;j++){var b=Matrix4x4.identity;if(ibm!=null)for(int k=0;k<16;k++)b[k]=ibm[j][k];Require(Math.Abs(b.determinant)>1e-12,"逆绑定矩阵不可逆。");matrices[j]=world[joints[j]]*b;}
                }
                var localMorphs=new List<List<VertexOffset>>();
                for(int primitive=0;primitive<mesh["primitives"].Count;primitive++)
                {
                    var p=mesh["primitives"][primitive];Require(p["mode"].Int(4)==4,"只支持三角面 primitive。");var a=p["attributes"];
                    FloatAttribute(a["POSITION"].Int());if(a["NORMAL"].Exists)FloatAttribute(a["NORMAL"].Int());
                    var pos=Accessor(a["POSITION"].Int(),3); var norm=a["NORMAL"].Exists?Accessor(a["NORMAL"].Int(),3):null;
                    Require((long)vertices.Count+pos.Length<=2000000,"合并网格超过 200 万顶点的转换上限。");
                    Require(norm==null||norm.All(v=>V(v).sqrMagnitude>1e-12),"顶点法线长度为零。");
                    int material=p["material"].Int(model.Materials.Length-1);Require(material>=0&&material<model.Materials.Length,"primitive 材质越界。");
                    var textureInfo=root["materials"][material]["pbrMetallicRoughness"]["baseColorTexture"];var transform=textureInfo["extensions"]["KHR_texture_transform"];
                    int uvChannel=transform["texCoord"].Int(textureInfo["texCoord"].Int(0));Require(uvChannel>=0&&uvChannel<=7,"不支持的 UV 通道。");
                    var uv=a["TEXCOORD_"+uvChannel].Exists?Accessor(a["TEXCOORD_"+uvChannel].Int(),2):null;
                    if(textureInfo.Exists)Require(uv!=null,"贴图所需 UV 不存在。");
                    var color=a["COLOR_0"].Exists?Accessor(a["COLOR_0"].Int(),root["accessors"][a["COLOR_0"].Int()]["type"].Text()=="VEC3"?3:4):null;
                    var ji=a["JOINTS_0"].Exists?Accessor(a["JOINTS_0"].Int(),4):null;var weights=a["WEIGHTS_0"].Exists?Accessor(a["WEIGHTS_0"].Int(),4):null;
                    Require(joints==null||ji!=null&&weights!=null,"蒙皮缺少 JOINTS_0 或 WEIGHTS_0。");
                    if(ji!=null)UnsignedAttribute(a["JOINTS_0"].Int(),false);
                    var ji1=a["JOINTS_1"].Exists?Accessor(a["JOINTS_1"].Int(),4):null;var w1=a["WEIGHTS_1"].Exists?Accessor(a["WEIGHTS_1"].Int(),4):null;
                    Require((ji1==null)==(w1==null),"附加蒙皮索引/权重不成对。");
                    if(ji1!=null)UnsignedAttribute(a["JOINTS_1"].Int(),false);
                    foreach(var data in new[]{norm,uv,color,ji,weights,ji1,w1})Require(data==null||data.Length==pos.Length,"顶点属性数量不匹配。");
                    int start=vertices.Count;var vertexTransforms=new Matrix4x4[pos.Length];
                    for(int i=0;i<pos.Length;i++)
                    {
                        var v=new PmxVertex{ExtraUvs=Array.Empty<float[]>(),Color=new byte[]{255,255,255,255}};
                        Matrix4x4 matrix=world[node];
                        if(joints!=null)
                        {
                            var influence=new Dictionary<int,float>();for(int c=0;c<(ji1==null?4:8);c++)
                            {
                                float weight=c<4?weights[i][c]:w1[i][c-4];Require(weight>=0,"蒙皮权重为负。");if(weight==0)continue;
                                int j=CheckedIndex(c<4?ji[i][c]:ji1[i][c-4],joints.Length);influence.TryGetValue(j,out float old);influence[j]=old+weight;
                            }
                            var chosen=influence.OrderByDescending(k=>k.Value).ThenBy(k=>k.Key).Take(4).ToArray();float sum=chosen.Sum(k=>k.Value);Require(sum>1e-8,"顶点没有有效蒙皮权重。");
                            if(influence.Count>4&&!model.Warnings.Any(w=>w.Contains("四个权重")))model.Warnings.Add("部分 VRM 顶点超过四个权重，已保留最大的四个并归一化。");
                            matrix=Matrix4x4.zero;for(int k=0;k<chosen.Length;k++){v.Bones[k]=joints[chosen[k].Key];v.Weights[k]=chosen[k].Value/sum;var m=matrices[chosen[k].Key];for(int c=0;c<16;c++)matrix[c]+=m[c]*v.Weights[k];}
                        }
                        else {v.Bones[0]=node;v.Weights[0]=1;}
                        vertexTransforms[i]=matrix;v.Position=F(matrix.MultiplyPoint3x4(V(pos[i])));
                        v.Normal=norm==null?new Float3():F(matrix.inverse.transpose.MultiplyVector(V(norm[i])).normalized);
                        if(uv!=null)
                        {
                            var t=TextureUv(new Vector2(uv[i][0],uv[i][1]),transform);v.U=t.x;v.V=t.y;
                        }
                        if(color!=null)for(int c=0;c<color[i].Length;c++){Require(color[i][c]>=0&&color[i][c]<=1,"顶点颜色越界。");v.Color[c]=(byte)Mathf.RoundToInt(color[i][c]*255);}
                        vertices.Add(v);
                    }
                    if(p["indices"].Exists)UnsignedAttribute(p["indices"].Int(),true);
                    var ids=p["indices"].Exists?Accessor(p["indices"].Int(),1).Select(x=>CheckedIndex(x[0],pos.Length)).ToArray():Enumerable.Range(0,pos.Length).ToArray();Require(ids.Length%3==0,"三角索引数无效。");
                    // Shared axis conversion is a reflection for VRM, so reverse triangle winding here.
                    for(int i=0;i<ids.Length;i+=3){faces[material].Add(start+ids[i]);faces[material].Add(start+ids[i+2]);faces[material].Add(start+ids[i+1]);}
                    if(norm==null)RebuildNormals(vertices,start,ids);
                    for(int t=0;t<p["targets"].Count;t++)
                    {
                        while(localMorphs.Count<=t)localMorphs.Add(new List<VertexOffset>());
                        if(!p["targets"][t]["POSITION"].Exists)continue;FloatAttribute(p["targets"][t]["POSITION"].Int());var delta=Accessor(p["targets"][t]["POSITION"].Int(),3);Require(delta.Length==pos.Length,"表情顶点数量不匹配。");
                        for(int i=0;i<delta.Length;i++)localMorphs[t].Add(new VertexOffset{Vertex=start+i,Delta=F(vertexTransforms[i].MultiplyVector(V(delta[i])))});
                    }
                }
                for(int t=0;t<localMorphs.Count;t++)morphs.Add(new PmxMorph{Name=mesh["extras"]["targetNames"][t].Text("Mesh_"+meshIndex+"_Target_"+t),EnglishName="",Type=1,Vertices=localMorphs[t].ToArray()});
                if(mesh["weights"].Exists||n["weights"].Exists)model.Warnings.Add("VRM 默认表情权重已重置为零；表情通道仍保留。");
            }
            Require(vertices.Count>0&&faces.Any(f=>f.Count>0),"当前场景没有可转换的网格。");
            model.Vertices=vertices.ToArray();model.Indices=faces.SelectMany(f=>f).ToArray();model.Morphs=morphs.ToArray();for(int i=0;i<faces.Length;i++)model.Materials[i].IndexCount=faces[i].Count;
            model.Materials=model.Materials.Where(m=>m.IndexCount>0).ToArray();
        }
        private static Vector2 TextureUv(Vector2 uv,JsonValue transform)
        {
            if(!transform.Exists)return uv;float sx=transform["scale"].Exists?transform["scale"][0].Number():1,sy=transform["scale"].Exists?transform["scale"][1].Number():1;
            float r=transform["rotation"].Number(),c=Mathf.Cos(r),s=Mathf.Sin(r);uv=new Vector2(uv.x*sx,uv.y*sy);
            return new Vector2(c*uv.x-s*uv.y+transform["offset"][0].Number(),s*uv.x+c*uv.y+transform["offset"][1].Number());
        }
        private static void RebuildNormals(List<PmxVertex> vertices,int start,int[] indices)
        {
            var normals=new Vector3[vertices.Count-start];for(int i=0;i<indices.Length;i+=3)
            {
                var a=V(vertices[start+indices[i]].Position);var b=V(vertices[start+indices[i+1]].Position);var c=V(vertices[start+indices[i+2]].Position);var n=Vector3.Cross(b-a,c-a);
                normals[indices[i]]+=n;normals[indices[i+1]]+=n;normals[indices[i+2]]+=n;
            }
            for(int i=0;i<normals.Length;i++)vertices[start+i].Normal=F(normals[i].sqrMagnitude>1e-12?normals[i].normalized:Vector3.up);
        }
        private static int CheckedIndex(float n,int count){Require(n>=0&&n<count&&n==Mathf.Floor(n),"索引越界或不是整数。");return (int)n;}
        private void FloatAttribute(int index){Require(root["accessors"][index]["componentType"].Int()==5126&&!root["accessors"][index]["normalized"].Bool(),"位置/法线/位移必须是 FLOAT。");}
        private void UnsignedAttribute(int index,bool uintAllowed){int ct=root["accessors"][index]["componentType"].Int();Require((ct==5121||ct==5123||uintAllowed&&ct==5125)&&!root["accessors"][index]["normalized"].Bool(),"索引必须为未归一化的无符号整数。");}
        private static bool Finite(float n)=>!float.IsNaN(n)&&!float.IsInfinity(n);
        private static Vector3 Vector(JsonValue v,Vector3 fallback){if(!v.Exists)return fallback;Require(v.Count==3,"向量长度无效。");return new Vector3(v[0].Number(),v[1].Number(),v[2].Number());}
        private static Vector3 V(float[] v)=>new Vector3(v[0],v[1],v[2]);
        private static Vector3 V(Float3 v)=>new Vector3(v.X,v.Y,v.Z);
        private static Float3 F(Vector3 v)=>new Float3(v.x,v.y,v.z);
    }
}
