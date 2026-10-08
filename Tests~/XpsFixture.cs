using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Pmx2Fbx.Core;

namespace Pmx2Fbx.Tests
{
    public static class XpsFixture
    {
        public static byte[] Create(int version = 3, bool ascii = false, bool humanoid = false, bool noBones = false,
            bool badParent = false, bool badFace = false, bool badWeight = false, bool manyWeights = false)
        {
            var model = PmxReader.Read(new MemoryStream(PmxFixture.Create(humanoid: humanoid)));
            string[] names = { "root hips", "spine lower", "spine upper", "head neck lower", "head neck upper",
                "arm left clavicle", "arm left shoulder", "arm left elbow", "arm left wrist",
                "arm right clavicle", "arm right shoulder", "arm right elbow", "arm right wrist",
                "leg left thigh", "leg left knee", "leg left ankle", "leg right thigh", "leg right knee", "leg right ankle" };
            using (var stream = new MemoryStream())
            using (var binary = new BinaryWriter(stream, Encoding.UTF8, true))
            using (var text = new StreamWriter(stream, new UTF8Encoding(false), 4096, true))
            {
                Action<int> integer = n => { if (ascii) text.WriteLine(n); else binary.Write(n); };
                Action<string> str = s => {
                    if (ascii) text.WriteLine(s);
                    else { var bytes = Encoding.UTF8.GetBytes(s); int n = bytes.Length;
                        binary.Write((byte)(n < 128 ? n : (n % 128) | 128)); if (n >= 128) binary.Write((byte)(n / 128)); binary.Write(bytes); }
                };
                Action<float[]> floats = values => {
                    if (ascii) text.WriteLine(string.Join(" ", values.Select(v => v.ToString("R", CultureInfo.InvariantCulture))));
                    else foreach (float value in values) binary.Write(value);
                };
                Action<Float3> vector = v => floats(new[] { -v.X, v.Y, -v.Z });
                if (!ascii && version > 0)
                {
                    binary.Write(323232); binary.Write((ushort)(version == 1 ? 2 : version)); binary.Write((ushort)(version == 1 ? 12 : 15));
                    str("XNAaraL"); integer(1); str("fixture"); str("test"); str(""); binary.Write(0);
                }
                integer(noBones ? 0 : model.Bones.Length);
                if (!noBones) for (int i = 0; i < model.Bones.Length; i++)
                {
                    str(humanoid ? names[i] : "root"); int parent = badParent && i == 0 ? 0 : model.Bones[i].Parent;
                    if (ascii) integer(parent); else binary.Write((short)parent); vector(model.Bones[i].Position);
                }
                integer(2);
                for (int mesh = 0; mesh < 2; mesh++)
                {
                    str(mesh == 0 ? "测试 " + new string('m', 140) : "mesh2"); integer(2); integer(2);
                    str("missing.png"); integer(1); str("missing.png"); integer(0);
                    int count = model.Vertices.Length / 2; integer(count);
                    for (int i = 0; i < count; i++)
                    {
                        var v = model.Vertices[mesh * count + i]; vector(v.Position); vector(v.Normal);
                        if (ascii) text.WriteLine("120 200 255 255"); else binary.Write(new byte[] { 120, 200, 255, 255 });
                        floats(new[] { .2f, .3f }); if (!ascii && version <= 1) floats(new[] { 1f, 0f, 0f, 1f });
                        floats(new[] { .6f, .7f }); if (!ascii && version <= 1) floats(new[] { 1f, 0f, 0f, 1f });
                        if (!noBones)
                        {
                            int[] bones = manyWeights ? new[] { 0, 1, 2, 3, 4, 5, 0 } : new[] { badWeight ? 32000 : v.Bones[0], -1, -1, -1 };
                            float[] weights = manyWeights ? new[] { .1f, .2f, .3f, .4f, .5f, .6f, .8f } : new[] { 1f, 0f, 0f, 0f };
                            if (ascii) { text.WriteLine(string.Join(" ", bones)); floats(weights); }
                            else { if (version >= 3) binary.Write((short)bones.Length); foreach (int bone in bones) binary.Write((short)bone); floats(weights); }
                        }
                    }
                    integer(count / 3);
                    for (int i = 0; i < count; i += 3)
                        if (ascii) text.WriteLine((badFace ? count : i) + " " + (i + 1) + " " + (i + 2) + " # triangle");
                        else { integer(badFace ? count : i); integer(i + 1); integer(i + 2); }
                }
                text.Flush(); binary.Flush(); return stream.ToArray();
            }
        }
    }
}
