using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pmx2Fbx
{
    [Serializable]
    public sealed class BoneOverride
    {
        public HumanBodyBones Human;
        public int PmxIndex = -1;
    }
    [Serializable]
    public sealed class ConversionOptions
    {
        public bool VrChat = true;
        public bool NormalizeHeight = true;
        public float Height = 1.65f;
        public float Scale = 0.08f;
        public bool Rotate180;
        public bool TPose = true;
        public bool IncludeMorphs = true;
        public bool AddDescriptor = true;
        public List<BoneOverride> BoneOverrides = new List<BoneOverride>();
    }
    [Serializable]
    public sealed class ConversionReport
    {
        public string Source, Format, Fbx, Prefab;
        public bool VrChat, HumanoidValid, DescriptorAdded, ConversionSucceeded;
        public int Vertices, Triangles, Materials, Bones, BlendShapes;
        public float HeightMeters;
        public List<string> Infos = new List<string>();
        public List<string> Warnings = new List<string>();
        public List<string> Errors = new List<string>();
        public string ToJson() => JsonUtility.ToJson(this, true);
    }
}
