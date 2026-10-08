using System;
using System.IO;
using System.Text;

namespace Pmx2Fbx.Core
{
    public sealed class PmxReader : IDisposable
    {
        private readonly BinaryReader reader;
        private Encoding encoding;
        private byte extraUv, vertexIndex, textureIndex, materialIndex, boneIndex, morphIndex, rigidIndex;
        private readonly PmxModel model = new PmxModel();

        private PmxReader(Stream stream) { reader = new BinaryReader(stream, Encoding.UTF8, true); }
        public static PmxModel Read(string path)
        {
            using (var stream = File.OpenRead(path)) return Read(stream);
        }
        public static PmxModel Read(Stream stream)
        {
            if (!stream.CanSeek) throw new ArgumentException("PMX 读取需要可定位的数据流。");
            using (var parser = new PmxReader(stream))
            {
                try { return parser.Parse(); }
                catch (EndOfStreamException e) { throw new InvalidDataException("PMX 文件被截断。", e); }
                catch (DecoderFallbackException e) { throw new InvalidDataException("PMX 文本编码无效。", e); }
            }
        }
        public void Dispose() { reader.Dispose(); }
        private long Remaining => reader.BaseStream.Length - reader.BaseStream.Position;
        private void Skip(long bytes)
        {
            if (bytes < 0 || bytes > Remaining) throw new EndOfStreamException();
            reader.BaseStream.Position += bytes;
        }
        private int Count(string field, int minBytes = 1, int max = 5000000)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > max || (long)count * minBytes > Remaining)
                throw new InvalidDataException(field + " 数量无效或超过可读取范围。");
            return count;
        }
        private float Float()
        {
            float value = reader.ReadSingle();
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidDataException("PMX 含非有限浮点数。");
            return value;
        }
        private Float3 Vector() => new Float3(Float(), Float(), Float());
        private float[] Floats(int count)
        {
            var result = new float[count];
            for (int i = 0; i < count; i++) result[i] = Float();
            return result;
        }
        private string Text()
        {
            int length = Count("文本字节", 1, 16 * 1024 * 1024);
            if (encoding.CodePage == 1200 && length % 2 != 0) throw new InvalidDataException("UTF-16 字节长度必须为偶数。");
            return encoding.GetString(reader.ReadBytes(length));
        }
        private int Index(byte size, bool unsigned = false)
        {
            if (size == 1) return unsigned ? (int)reader.ReadByte() : reader.ReadSByte();
            if (size == 2) return unsigned ? (int)reader.ReadUInt16() : reader.ReadInt16();
            return reader.ReadInt32();
        }
        private void Check(int index, int length, string label, bool optional = false)
        {
            if (index < (optional ? -1 : 0) || index >= length) throw new InvalidDataException(label + " 索引越界：" + index);
        }
        private PmxModel Parse()
        {
            if (reader.ReadUInt32() != 0x20584d50) throw new InvalidDataException("文件不是 PMX（缺少 PMX 签名）。");
            model.Version = Float();
            if (Math.Abs(model.Version - 2.0f) > .001f && Math.Abs(model.Version - 2.1f) > .001f)
                throw new InvalidDataException("仅支持 PMX 2.0 / 2.1。");
            byte headerSize = reader.ReadByte();
            if (headerSize < 8) throw new InvalidDataException("PMX 头部长度无效。");
            byte textEncoding = reader.ReadByte();
            if (textEncoding > 1) throw new InvalidDataException("不支持的 PMX 文本编码。");
            encoding = textEncoding == 0 ? (Encoding)new UnicodeEncoding(false, false, true) : new UTF8Encoding(false, true);
            extraUv = reader.ReadByte();
            if (extraUv > 4) throw new InvalidDataException("PMX 额外 UV 数量超过 4。");
            vertexIndex = reader.ReadByte(); textureIndex = reader.ReadByte(); materialIndex = reader.ReadByte();
            boneIndex = reader.ReadByte(); morphIndex = reader.ReadByte(); rigidIndex = reader.ReadByte();
            foreach (byte size in new[] { vertexIndex, textureIndex, materialIndex, boneIndex, morphIndex, rigidIndex })
                if (size != 1 && size != 2 && size != 4) throw new InvalidDataException("PMX 索引宽度无效。");
            Skip(headerSize - 8);
            model.Name = Text(); model.EnglishName = Text(); Text(); Text();
            ReadVertices();
            int count = Count("面索引", vertexIndex, 15000000);
            if (count % 3 != 0) throw new InvalidDataException("面索引数量必须为 3 的倍数。");
            model.Indices = new int[count];
            for (int i = 0; i < count; i++)
            {
                model.Indices[i] = Index(vertexIndex, true);
                Check(model.Indices[i], model.Vertices.Length, "顶点");
            }
            model.Textures = new string[Count("纹理", 4, 100000)];
            for (int i = 0; i < model.Textures.Length; i++) model.Textures[i] = Text();
            ReadMaterials(); ReadBones(); ReadMorphs(); ReadFooter(); Validate();
            return model;
        }
        private void ReadVertices()
        {
            model.Vertices = new PmxVertex[Count("顶点", 37 + boneIndex + extraUv * 16)];
            bool approximated = false;
            for (int i = 0; i < model.Vertices.Length; i++)
            {
                var v = new PmxVertex { Position = Vector(), Normal = Vector(), U = Float(), V = Float(), ExtraUvs = new float[extraUv][] };
                for (int j = 0; j < extraUv; j++) v.ExtraUvs[j] = Floats(4);
                v.Skinning = reader.ReadByte();
                switch (v.Skinning)
                {
                    case 0: v.Bones[0] = Index(boneIndex); v.Weights[0] = 1; break;
                    case 1:
                    case 3:
                        v.Bones[0] = Index(boneIndex); v.Bones[1] = Index(boneIndex);
                        v.Weights[0] = Float(); v.Weights[1] = 1 - v.Weights[0];
                        if (v.Skinning == 3) { Floats(9); approximated = true; }
                        break;
                    case 2:
                    case 4:
                        if (v.Skinning == 4 && model.Version < 2.05f) throw new InvalidDataException("PMX 2.0 不能使用 QDEF。");
                        for (int j = 0; j < 4; j++) v.Bones[j] = Index(boneIndex);
                        v.Weights = Floats(4); approximated |= v.Skinning == 4;
                        break;
                    default: throw new InvalidDataException("未知蒙皮类型：" + v.Skinning);
                }
                Float(); model.Vertices[i] = v;
            }
            if (approximated) model.Warnings.Add("SDEF / QDEF 转为四权重线性蒙皮，关节变形可能与 MMD 不同。");
        }
        private void ReadMaterials()
        {
            model.Materials = new PmxMaterial[Count("材质", 80, 100000)];
            long assigned = 0;
            for (int i = 0; i < model.Materials.Length; i++)
            {
                var m = new PmxMaterial { Name = Text(), EnglishName = Text(), Diffuse = Floats(4) };
                Floats(7); m.Flags = reader.ReadByte(); Floats(5);
                m.Texture = Index(textureIndex); m.SphereTexture = Index(textureIndex);
                reader.ReadByte(); byte shared = reader.ReadByte();
                if (shared == 0) Index(textureIndex);
                else if (shared == 1) reader.ReadByte();
                else throw new InvalidDataException("Toon 共享标记无效。");
                Text(); m.IndexCount = Count("材质面索引", 0, model.Indices.Length);
                if (m.IndexCount % 3 != 0) throw new InvalidDataException("材质面索引数量必须为 3 的倍数。");
                assigned += m.IndexCount;
                Check(m.Texture, model.Textures.Length, "主纹理", true);
                Check(m.SphereTexture, model.Textures.Length, "球面纹理", true);
                model.Materials[i] = m;
            }
            if (assigned != model.Indices.Length) throw new InvalidDataException("材质分配的面数与模型总面数不一致。");
        }
        private void ReadBones()
        {
            model.Bones = new PmxBone[Count("骨骼", 26, 100000)];
            bool constraints = false;
            for (int i = 0; i < model.Bones.Length; i++)
            {
                var b = new PmxBone { Name = Text(), EnglishName = Text(), Position = Vector(), Parent = Index(boneIndex) };
                reader.ReadInt32(); b.Flags = reader.ReadUInt16();
                if ((b.Flags & 1) != 0) Index(boneIndex); else Vector();
                if ((b.Flags & 0x300) != 0) { Index(boneIndex); Float(); constraints = true; }
                if ((b.Flags & 0x400) != 0) Vector();
                if ((b.Flags & 0x800) != 0) { Vector(); Vector(); }
                if ((b.Flags & 0x2000) != 0) reader.ReadInt32();
                if ((b.Flags & 0x20) != 0)
                {
                    constraints = true; Index(boneIndex); reader.ReadInt32(); Float();
                    int links = Count("IK 链", boneIndex + 1, 100000);
                    for (int j = 0; j < links; j++)
                    {
                        Index(boneIndex); byte limited = reader.ReadByte();
                        if (limited > 1) throw new InvalidDataException("IK 限制标记无效。");
                        if (limited == 1) { Vector(); Vector(); }
                    }
                }
                model.Bones[i] = b;
            }
            if (constraints) model.Warnings.Add("PMX IK 与追加骨骼约束不导出；VRChat 使用 Humanoid IK，辅助骨骼仍保留。");
        }
        private void ReadMorphs()
        {
            model.Morphs = new PmxMorph[Count("表情", 14, 100000)];
            int unsupported = 0;
            for (int i = 0; i < model.Morphs.Length; i++)
            {
                var m = new PmxMorph { Name = Text(), EnglishName = Text() };
                reader.ReadByte(); m.Type = reader.ReadByte();
                int count = Count("表情偏移", 1);
                if (m.Type == 1)
                {
                    m.Vertices = new VertexOffset[count];
                    for (int j = 0; j < count; j++)
                    {
                        int vertex = Index(vertexIndex, true); Check(vertex, model.Vertices.Length, "表情顶点");
                        m.Vertices[j] = new VertexOffset { Vertex = vertex, Delta = Vector() };
                    }
                }
                else if (m.Type == 0 || m.Type == 9)
                {
                    m.References = new MorphReference[count];
                    for (int j = 0; j < count; j++) m.References[j] = new MorphReference { Morph = Index(morphIndex), Weight = Float() };
                    if (m.Type == 9) unsupported++;
                }
                else
                {
                    unsupported++;
                    int bytes;
                    switch (m.Type)
                    {
                        case 2: bytes = boneIndex + 28; break;
                        case 3: case 4: case 5: case 6: case 7: bytes = vertexIndex + 16; break;
                        case 8: bytes = materialIndex + 113; break;
                        case 10: bytes = rigidIndex + 25; break;
                        default: throw new InvalidDataException("未知表情类型：" + m.Type);
                    }
                    Skip((long)bytes * count);
                }
                model.Morphs[i] = m;
            }
            foreach (var morph in model.Morphs)
                foreach (var child in morph.References) Check(child.Morph, model.Morphs.Length, "组表情");
            if (unsupported > 0) model.Warnings.Add(unsupported + " 个骨骼 / UV / 材质 / Flip / 冲量表情不支持 FBX BlendShape，已跳过。");
        }
        private void ReadFooter()
        {
            int frames = Count("显示框", 13, 100000);
            for (int i = 0; i < frames; i++)
            {
                Text(); Text(); reader.ReadByte(); int items = Count("显示项", 2);
                for (int j = 0; j < items; j++)
                {
                    byte type = reader.ReadByte();
                    if (type > 1) throw new InvalidDataException("显示项类型无效。");
                    Index(type == 0 ? boneIndex : morphIndex);
                }
            }
            model.RigidBodyCount = Count("刚体", 8 + boneIndex + 61, 100000);
            for (int i = 0; i < model.RigidBodyCount; i++) { Text(); Text(); Skip(boneIndex + 61); }
            model.JointCount = Count("关节", 9 + rigidIndex * 2 + 96, 100000);
            for (int i = 0; i < model.JointCount; i++) { Text(); Text(); Skip(1 + rigidIndex * 2 + 96); }
            if (model.Version > 2.05f)
            {
                model.SoftBodyCount = Count("软体", 8 + materialIndex + 133, 100000);
                for (int i = 0; i < model.SoftBodyCount; i++)
                {
                    Text(); Text(); Skip(1 + materialIndex + 3 + 1 + 12 + 4 + 48 + 24 + 16 + 12);
                    int anchors = Count("软体锚点", rigidIndex + vertexIndex + 1);
                    Skip((long)anchors * (rigidIndex + vertexIndex + 1));
                    int pins = Count("软体固定顶点", vertexIndex);
                    Skip((long)pins * vertexIndex);
                }
            }
            if (model.RigidBodyCount + model.JointCount + model.SoftBodyCount > 0)
                model.Warnings.Add("PMX 刚体、关节与软体不转换。头发 / 衣服物理需要在 VRChat 中手动配置 PhysBones。");
            if (Remaining != 0) model.Warnings.Add("文件末尾有扩展数据，已忽略。");
        }
        private void Validate()
        {
            var state = new byte[model.Bones.Length];
            for (int i = 0; i < model.Bones.Length; i++) Check(model.Bones[i].Parent, model.Bones.Length, "父骨骼", true);
            for (int i = 0; i < model.Bones.Length; i++)
            {
                int cursor = i;
                while (cursor >= 0 && state[cursor] == 0) { state[cursor] = 1; cursor = model.Bones[cursor].Parent; }
                if (cursor >= 0 && state[cursor] == 1) throw new InvalidDataException("骨骼父子关系存在循环。");
                cursor = i;
                while (cursor >= 0 && state[cursor] == 1) { state[cursor] = 2; cursor = model.Bones[cursor].Parent; }
            }
            foreach (var vertex in model.Vertices)
                for (int j = 0; j < 4; j++)
                {
                    if (vertex.Bones[j] != -1) Check(vertex.Bones[j], model.Bones.Length, "蒙皮骨骼");
                    if (vertex.Weights[j] < 0 || vertex.Weights[j] > 1) throw new InvalidDataException("蒙皮权重必须在 0 到 1 之间。");
                }
        }
    }
}
