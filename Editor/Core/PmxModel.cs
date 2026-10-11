using System;
using System.Collections.Generic;

namespace Pmx2Fbx.Core
{
    public struct Float3
    {
        public float X, Y, Z;
        public Float3(float x, float y, float z) { X = x; Y = y; Z = z; }
        public static Float3 operator +(Float3 a, Float3 b) => new Float3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Float3 operator *(Float3 a, float b) => new Float3(a.X * b, a.Y * b, a.Z * b);
    }

    public sealed class PmxVertex
    {
        public Float3 Position, Normal;
        public float U, V;
        public float[][] ExtraUvs;
        public byte[] Color;
        public byte Skinning;
        public int[] Bones = { -1, -1, -1, -1 };
        public float[] Weights = new float[4];
    }
    public enum MaterialAlphaMode { Auto, Opaque, Blend }
    public sealed class PmxMaterial
    {
        public string Name, EnglishName;
        public float[] Diffuse;
        public byte Flags;
        public int Texture, SphereTexture, IndexCount;
        public MaterialAlphaMode AlphaMode;
    }
    public sealed class PmxBone
    {
        public string Name, EnglishName;
        public Float3 Position;
        public int Parent;
        public ushort Flags;
    }
    public struct VertexOffset
    {
        public int Vertex;
        public Float3 Delta;
    }
    public struct MorphReference
    {
        public int Morph;
        public float Weight;
    }
    public sealed class PmxMorph
    {
        public string Name, EnglishName;
        public byte Type;
        public VertexOffset[] Vertices = Array.Empty<VertexOffset>();
        public MorphReference[] References = Array.Empty<MorphReference>();
    }
    public sealed class PmxModel
    {
        // Shared conversion data. PMX and XPS readers populate the same mesh/rig representation.
        public string Format = "PMX";
        public Float3 Axis = new Float3(-1, 1, -1);
        public string Name, EnglishName;
        public float Version;
        public PmxVertex[] Vertices;
        public int[] Indices;
        public string[] Textures;
        public PmxMaterial[] Materials;
        public PmxBone[] Bones;
        public PmxMorph[] Morphs;
        public int RigidBodyCount, JointCount, SoftBodyCount;
        public readonly List<string> Warnings = new List<string>();
    }

    public static class MorphResolver
    {
        // FBX blend shapes can represent vertex deltas; other morph semantics cannot be preserved.
        public static Dictionary<int, Float3> Resolve(PmxModel model, int morphIndex, out bool complete)
        {
            var output = new Dictionary<int, Float3>();
            complete = Visit(model, morphIndex, 1, new HashSet<int>(), output, 0);
            return output;
        }

        private static bool Visit(PmxModel model, int index, float weight, HashSet<int> path,
            Dictionary<int, Float3> output, int depth)
        {
            if (index < 0 || index >= model.Morphs.Length || depth > 64 || !path.Add(index))
                throw new System.IO.InvalidDataException("表情引用无效、循环或嵌套过深。");
            var morph = model.Morphs[index];
            bool complete = true;
            if (morph.Type == 1)
            {
                foreach (var offset in morph.Vertices)
                {
                    output.TryGetValue(offset.Vertex, out var previous);
                    output[offset.Vertex] = previous + offset.Delta * weight;
                }
            }
            else if (morph.Type == 0)
            {
                foreach (var child in morph.References)
                    complete &= Visit(model, child.Morph, weight * child.Weight, path, output, depth + 1);
            }
            else complete = false;
            path.Remove(index);
            return complete;
        }
    }
}
