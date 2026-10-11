using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Pmx2Fbx.Core
{
    // Independent implementation of the XNALara binary/ASCII data layout.
    public sealed class XpsReader : IDisposable
    {
        private readonly BinaryReader binary;
        private readonly StreamReader ascii;
        private readonly Stream stream;
        private bool tangents, variableWeights;
        private readonly PmxModel model = new PmxModel { Format = "XPS", Axis = new Float3(1, 1, 1), Morphs = Array.Empty<PmxMorph>() };
        private readonly List<PmxVertex> vertices = new List<PmxVertex>();
        private readonly List<int> faces = new List<int>();
        private readonly List<PmxMaterial> materials = new List<PmxMaterial>();
        private readonly List<string> textures = new List<string>();
        private int maxUvLayers = 1;
        private bool reducedWeights;
        private int lineNumber;
        private XpsReader(Stream input, bool text)
        {
            stream = input;
            if (text) ascii = new StreamReader(input, new UTF8Encoding(false, true), true, 4096, true);
            else binary = new BinaryReader(input, new UTF8Encoding(false, true), true);
        }
        public static PmxModel Read(string path)
        {
            using (var input = File.OpenRead(path))
            {
                var result = Read(input, string.Equals(Path.GetExtension(path), ".ascii", StringComparison.OrdinalIgnoreCase));
                result.Name = Path.GetFileNameWithoutExtension(path); result.EnglishName = result.Name;
                return result;
            }
        }
        public static PmxModel Read(Stream input, bool text = false)
        {
            if (!input.CanSeek) throw new ArgumentException("XPS 读取需要可定位的数据流。");
            using (var reader = new XpsReader(input, text))
            {
                try { return reader.Parse(); }
                catch (EndOfStreamException e) { throw new InvalidDataException("XPS 文件被截断。", e); }
                catch (DecoderFallbackException e) { throw new InvalidDataException("XPS 文本不是有效 UTF-8。", e); }
            }
        }
        public void Dispose() { binary?.Dispose(); ascii?.Dispose(); }
        private long Remaining => stream.Length - stream.Position;
        private string Line()
        {
            string value = ascii.ReadLine(); lineNumber++;
            if (value == null) throw new EndOfStreamException();
            if (value.Length > 1024 * 1024) throw new InvalidDataException("XPS 文本行过长。");
            int comment = value.IndexOf('#');
            return (comment >= 0 ? value.Substring(0, comment) : value).Trim();
        }
        private string[] Values() => Line().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        private int Integer(string value)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                throw new InvalidDataException("XPS 第 " + lineNumber + " 行整数无效。");
            return number;
        }
        private int Int() => ascii != null ? Integer(Line()) : binary.ReadInt32();
        private int Short() => ascii != null ? Int() : binary.ReadInt16();
        private int Count(string label, int max = 5000000, int minBytes = 1)
        {
            int value = Int();
            if (value < 0 || value > max || (ascii == null && (long)value * minBytes > Remaining))
                throw new InvalidDataException("XPS " + label + " 数量无效。");
            return value;
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private float Number(string value, bool allowNonFinite = false)
        {
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float number)
                || !allowNonFinite && !Finite(number)) throw new InvalidDataException("XPS 数值无效。");
            return number;
        }
        private float Float(bool allowNonFinite = false)
        {
            float value = binary.ReadSingle();
            if (!allowNonFinite && !Finite(value)) throw new InvalidDataException("XPS 含非有限数值（偏移 " + (stream.Position - 4) + " 字节）。");
            return value;
        }
        private float[] Floats(int length, bool allowNonFinite = false)
        {
            if (ascii != null)
            {
                var values = Values();
                if (values.Length != length) throw new InvalidDataException("XPS 第 " + lineNumber + " 行应有 " + length + " 个数值。");
                return values.Select(v => Number(v, allowNonFinite)).ToArray();
            }
            var result = new float[length];
            for (int i = 0; i < length; i++) result[i] = Float(allowNonFinite);
            return result;
        }
        private Float3 Vector(bool allowNonFinite = false)
        {
            var values = Floats(3, allowNonFinite); return new Float3(values[0], values[1], values[2]);
        }
        private void Skip(long bytes)
        {
            if (bytes < 0 || bytes > Remaining) throw new EndOfStreamException();
            stream.Position += bytes;
        }
        private string Text()
        {
            if (ascii != null) return Line();
            int first = binary.ReadByte();
            int length = (first & 127) + (first >= 128 ? binary.ReadByte() * 128 : 0);
            if (length > Remaining) throw new EndOfStreamException();
            return new UTF8Encoding(false, true).GetString(binary.ReadBytes(length)).TrimStart('\uFEFF');
        }
        private PmxModel Parse()
        {
            if (ascii == null)
            {
                long start = stream.Position;
                int magic = binary.ReadInt32(); stream.Position = start;
                if (magic == 323232)
                {
                    binary.ReadInt32(); int major = binary.ReadUInt16(), minor = binary.ReadUInt16();
                    if (major < 1 || major > 3) throw new InvalidDataException("不支持的 XPS 版本：" + major + "." + minor);
                    model.Version = major + minor / 100f;
                    Text(); int settings = Count("头部设置", 16 * 1024 * 1024, 0);
                    Text(); Text(); Text(); Skip((long)settings * 4);
                    tangents = major <= 2 && minor <= 12; variableWeights = major >= 3;
                    if (settings > 0) model.Warnings.Add("XPS 头部姿态与渲染设置未应用，使用模型的绑定姿态。");
                }
                else tangents = true; // Headerless legacy .mesh uses four weights and per-UV tangents.
            }
            int boneCount = Count("骨骼", 32768, 15);
            model.Bones = new PmxBone[boneCount];
            for (int i = 0; i < boneCount; i++)
                model.Bones[i] = new PmxBone { Name = Text(), EnglishName = "", Parent = Short(), Position = Vector() };
            ValidateBones();
            int meshCount = Count("网格", 100000, 13);
            for (int mesh = 0; mesh < meshCount; mesh++) ReadMesh(boneCount);
            foreach (var vertex in vertices)
            {
                var original = vertex.ExtraUvs;
                vertex.ExtraUvs = new float[maxUvLayers - 1][];
                for (int i = 0; i < vertex.ExtraUvs.Length; i++) vertex.ExtraUvs[i] = i < original.Length ? original[i] : new float[4];
            }
            model.Vertices = vertices.ToArray(); model.Indices = faces.ToArray();
            model.Materials = materials.ToArray(); model.Textures = textures.ToArray();
            if (reducedWeights) model.Warnings.Add("部分 XPS 顶点超过 4 个骨骼影响，已合并重复权重并保留最高 4 项，随后归一化。");
            if (ascii == null && Remaining != 0)
                model.Warnings.Add("XPS 网格已完整读取；忽略文件末尾 " + Remaining + " 字节的附加数据，附加信息不会导出。");
            if (ascii != null)
            {
                string extra;
                while ((extra = ascii.ReadLine()) != null)
                    if (!string.IsNullOrWhiteSpace(extra.Split('#')[0])) throw new InvalidDataException("XPS ASCII 有未解析的尾部数据。");
            }
            return model;
        }
        private void ValidateBones()
        {
            var state = new byte[model.Bones.Length];
            foreach (var b in model.Bones)
                if (b.Parent < -1 || b.Parent >= model.Bones.Length) throw new InvalidDataException("XPS 父骨骼索引越界。");
            for (int i = 0; i < state.Length; i++)
            {
                int cursor = i;
                while (cursor >= 0 && state[cursor] == 0) { state[cursor] = 1; cursor = model.Bones[cursor].Parent; }
                if (cursor >= 0 && state[cursor] == 1) throw new InvalidDataException("XPS 骨骼层级存在循环。");
                cursor = i;
                while (cursor >= 0 && state[cursor] == 1) { state[cursor] = 2; cursor = model.Bones[cursor].Parent; }
            }
        }
        private void ReadMesh(int boneCount)
        {
            string name = Text(); int uvCount = Count("UV 层", 8, 0);
            maxUvLayers = Math.Max(maxUvLayers, uvCount);
            int textureCount = Count("纹理", 10000, 5), mainTexture = -1, mainUv = 0;
            for (int t = 0; t < textureCount; t++)
            {
                if (uvCount == 0) throw new InvalidDataException("XPS 有纹理的网格缺少 UV 层。");
                string path = Text(); int uv = Count("纹理 UV 索引", uvCount - 1, 0);
                if (t == 0) { mainTexture = textures.Count; mainUv = uv; }
                textures.Add(path);
            }
            if (textureCount > 1) model.Warnings.Add("XPS 网格「" + name + "」只绑定第一张主贴图；其他贴图已复制，需要手动设置法线 / 高光 / 特殊材质。");
            int count = Count("顶点", 5000000, 28 + uvCount * (tangents ? 24 : 8));
            if ((long)vertices.Count + count > 5000000) throw new InvalidDataException("XPS 合并后的顶点数量过大。");
            int offset = vertices.Count;
            var invalidNormals = new List<int>();
            for (int i = 0; i < count; i++)
            {
                var v = new PmxVertex { Position = Vector(), Normal = Vector(true), Color = ReadColor(), ExtraUvs = new float[Math.Max(0, uvCount - 1)][] };
                var normal = v.Normal;
                double lengthSquared = (double)normal.X * normal.X + (double)normal.Y * normal.Y + (double)normal.Z * normal.Z;
                if (!Finite(normal.X) || !Finite(normal.Y) || !Finite(normal.Z) || lengthSquared < 1e-20)
                { invalidNormals.Add(i); v.Normal = new Float3(); }
                var uvLayers = new float[uvCount][];
                for (int uv = 0; uv < uvCount; uv++) { uvLayers[uv] = Floats(2); if (tangents) Skip(16); }
                if (uvCount > 0) { v.U = uvLayers[mainUv][0]; v.V = uvLayers[mainUv][1]; }
                int extra = 0;
                for (int uv = 0; uv < uvCount; uv++)
                    if (uv != mainUv) v.ExtraUvs[extra++] = new[] { uvLayers[uv][0], 1 - uvLayers[uv][1], 0f, 0f };
                if (boneCount > 0) ReadWeights(v, boneCount);
                vertices.Add(v);
            }
            int triangleCount = Count("三角面", 5000000, 12);
            int firstFace = faces.Count;
            if ((long)faces.Count + triangleCount * 3L > 15000000) throw new InvalidDataException("XPS 合并后的面数过大。");
            for (int t = 0; t < triangleCount; t++)
            {
                int[] triangle = ascii != null ? Values().Select(Integer).ToArray() : new[] { Int(), Int(), Int() };
                if (triangle.Length != 3) throw new InvalidDataException("XPS 三角面必须有 3 个索引。");
                foreach (int vertex in triangle)
                {
                    if (vertex < 0 || vertex >= count) throw new InvalidDataException("XPS 三角面顶点索引越界。");
                    faces.Add(offset + vertex);
                }
            }
            RepairNormals(offset, count, firstFace, invalidNormals);
            materials.Add(new PmxMaterial { Name = name, EnglishName = "", Diffuse = new[] { 1f, 1f, 1f, 1f }, Flags = 1,
                Texture = mainTexture, SphereTexture = -1, IndexCount = triangleCount * 3, AlphaMode = ReadAlphaMode(name) });
        }
        private static MaterialAlphaMode ReadAlphaMode(string name)
        {
            int separator = name.IndexOf('_');
            if (separator <= 0 || !int.TryParse(name.Substring(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out int group))
                return MaterialAlphaMode.Auto;
            // XNALara render-group metadata controls use of diffuse alpha. Other group
            // effects remain outside this converter; unknown groups use pixel analysis.
            switch (group)
            {
                case 1: case 2: case 3: case 4: case 5: case 10: case 11: case 13: case 14: case 16: case 17:
                case 22: case 24: case 26: case 28: case 30: case 32: case 36: case 38: case 40: case 42:
                    return MaterialAlphaMode.Opaque;
                case 6: case 7: case 8: case 9: case 12: case 15: case 18: case 19: case 20: case 21:
                case 23: case 25: case 27: case 29: case 31: case 33: case 37: case 39: case 41: case 43:
                    return MaterialAlphaMode.Blend;
                default: return MaterialAlphaMode.Auto;
            }
        }
        private void RepairNormals(int offset, int count, int firstFace, List<int> invalid)
        {
            if (invalid.Count == 0) return;
            var x = new double[count]; var y = new double[count]; var z = new double[count];
            var needed = new bool[count]; foreach (int index in invalid) needed[index] = true;
            for (int f = firstFace; f < faces.Count; f += 3)
            {
                int a = faces[f] - offset, b = faces[f + 1] - offset, c = faces[f + 2] - offset;
                var p = vertices[offset + a].Position; var q = vertices[offset + b].Position; var r = vertices[offset + c].Position;
                double ux = (double)q.X - p.X, uy = (double)q.Y - p.Y, uz = (double)q.Z - p.Z;
                double vx = (double)r.X - p.X, vy = (double)r.Y - p.Y, vz = (double)r.Z - p.Z;
                double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                // Orient reconstructed normals to valid neighboring normals when available.
                // Keep all authored normals intact, including hard edges on duplicated vertices.
                double alignment = 0;
                for (int corner = 0; corner < 3; corner++)
                {
                    int index = corner == 0 ? a : corner == 1 ? b : c;
                    if (!needed[index]) { var n = vertices[offset + index].Normal; alignment += nx * n.X + ny * n.Y + nz * n.Z; }
                }
                if (alignment < 0) { nx = -nx; ny = -ny; nz = -nz; }
                for (int corner = 0; corner < 3; corner++)
                {
                    int index = corner == 0 ? a : corner == 1 ? b : c;
                    if (needed[index]) { x[index] += nx; y[index] += ny; z[index] += nz; }
                }
            }
            int fallback = 0;
            foreach (int index in invalid)
            {
                double length = Math.Sqrt(x[index] * x[index] + y[index] * y[index] + z[index] * z[index]);
                if (length > 1e-20) vertices[offset + index].Normal = new Float3((float)(x[index] / length), (float)(y[index] / length), (float)(z[index] / length));
                else { vertices[offset + index].Normal = new Float3(0, 1, 0); fallback++; }
            }
            model.Warnings.Add("XPS 网格有 " + invalid.Count + " 个无效或零长度法线，已按相邻三角面重建并保留其余原法线。" +
                (fallback > 0 ? "其中 " + fallback + " 个顶点没有可用的相邻面，使用向上法线；请检查退化面。" : ""));
        }
        private byte[] ReadColor()
        {
            if (ascii == null) return new[] { binary.ReadByte(), binary.ReadByte(), binary.ReadByte(), binary.ReadByte() };
            var values = Values().Select(Integer).ToArray();
            if (values.Length != 4 || values.Any(c => c < 0 || c > 255)) throw new InvalidDataException("XPS 顶点颜色必须为 4 个 0–255 整数。");
            return values.Select(c => (byte)c).ToArray();
        }
        private void ReadWeights(PmxVertex v, int boneCount)
        {
            int[] bones; float[] weights;
            if (ascii != null)
            {
                bones = Values().Select(Integer).ToArray(); weights = Values().Select(v => Number(v)).ToArray();
                if (bones.Length != weights.Length || bones.Length > 1024) throw new InvalidDataException("XPS 骨骼索引与权重数量不一致或过多。");
            }
            else
            {
                int count = variableWeights ? binary.ReadInt16() : 4;
                if (count < 0 || count > 1024 || (long)count * 6 > Remaining) throw new InvalidDataException("XPS 骨骼影响数量无效。");
                bones = new int[count]; weights = new float[count];
                for (int i = 0; i < count; i++) bones[i] = binary.ReadInt16();
                for (int i = 0; i < count; i++) weights[i] = Float();
            }
            var merged = new Dictionary<int, float>();
            for (int i = 0; i < bones.Length; i++)
            {
                if (weights[i] < 0) throw new InvalidDataException("XPS 权重不能为负数。");
                if (weights[i] == 0) continue;
                if (bones[i] < 0 || bones[i] >= boneCount) throw new InvalidDataException("XPS 蒙皮骨骼索引越界。");
                merged.TryGetValue(bones[i], out float previous); merged[bones[i]] = previous + weights[i];
            }
            reducedWeights |= merged.Count > 4;
            var sorted = merged.OrderByDescending(p => p.Value).Take(4).ToArray();
            float sum = sorted.Sum(p => p.Value);
            if (float.IsInfinity(sum)) throw new InvalidDataException("XPS 权重总和无效。");
            for (int i = 0; i < sorted.Length; i++) { v.Bones[i] = sorted[i].Key; v.Weights[i] = sum > 0 ? sorted[i].Value / sum : 0; }
        }
    }
}
