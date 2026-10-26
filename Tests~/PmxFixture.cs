using System;
using System.IO;
using System.Text;

namespace Pmx2Fbx.Tests
{
    // Entirely synthetic geometry and rig; no third-party model is redistributed.
    public static class PmxFixture
    {
        public static byte[] Create(bool utf16 = false, int width = 1, int skinning = 0, bool humanoid = false, bool missingChest = false, bool cycle = false,
            bool atlasUvs = false, float firstAlpha = 1, float secondAlpha = 1, bool withTwist = false)
        {
            using (var stream = new MemoryStream())
            using (var w = new BinaryWriter(stream))
            {
                Encoding encoding = utf16 ? Encoding.Unicode : Encoding.UTF8;
                Action<string> text = s => { var bytes = encoding.GetBytes(s); w.Write(bytes.Length); w.Write(bytes); };
                Action<int> index = v => { if (width == 1) w.Write((byte)v); else if (width == 2) w.Write((short)v); else w.Write(v); };
                Action<float, float, float> vector = (x, y, z) => { w.Write(x); w.Write(y); w.Write(z); };
                string[] names = humanoid ? new[] { "下半身", "上半身", "上半身2", "首", "頭", "左肩", "左腕", "左ひじ", "左手首", "右肩", "右腕", "右ひじ", "右手首", "左足", "左ひざ", "左足首", "右足", "右ひざ", "右足首" } : new[] { "root" };
                float[][] locations = {
                    new[] {0f,1f,0f}, new[] {0f,1.1f,0f}, new[] {0f,1.35f,0f}, new[] {0f,1.5f,0f}, new[] {0f,1.6f,0f},
                    new[] {.08f,1.4f,0f}, new[] {.18f,1.4f,0f}, new[] {.4f,1.25f,0f}, new[] {.6f,1.1f,0f},
                    new[] {-.08f,1.4f,0f}, new[] {-.18f,1.4f,0f}, new[] {-.4f,1.25f,0f}, new[] {-.6f,1.1f,0f},
                    new[] {.09f,.95f,0f}, new[] {.09f,.5f,0f}, new[] {.09f,.08f,0f},
                    new[] {-.09f,.95f,0f}, new[] {-.09f,.5f,0f}, new[] {-.09f,.08f,0f}
                };
                int[] parents = { -1, -1, 1, 2, 3, 2, 5, 6, 7, 2, 9, 10, 11, 0, 13, 14, 0, 16, 17 };
                if (humanoid && withTwist)
                {
                    Array.Resize(ref names,20); names[19]="左腕捩";
                    Array.Resize(ref locations,20); locations[19]=new[]{.3f,1.32f,0f};
                    Array.Resize(ref parents,20); parents[19]=6; parents[7]=19;
                }
                w.Write(Encoding.ASCII.GetBytes("PMX ")); w.Write(skinning == 4 ? 2.1f : 2f);
                w.Write((byte)8); w.Write((byte)(utf16 ? 0 : 1)); w.Write((byte)1);
                for (int i = 0; i < 6; i++) w.Write((byte)width);
                text("合成テスト"); text("Synthetic"); text(""); text("");
                int vertexCount = humanoid ? 12 : 6;
                w.Write(vertexCount);
                for (int i = 0; i < vertexCount; i++)
                {
                    int bone = i < 6 ? i >= 3 ? 4 : 0 : i < 9 ? i : i + 1;
                    if (humanoid && withTwist && i==7) bone=19;
                    if (i >= 6) { var armPosition = locations[bone]; vector(armPosition[0],armPosition[1],-.02f * (i % 3)); }
                    else vector(i % 3 == 0 ? -.1f : .1f, i < 3 ? 0 : 1.75f, -.02f * (i % 3));
                    vector(0,0,-1);
                    if (atlasUvs)
                    {
                        float u = (i<3 ? .1f : .65f) + (i%3==1 ? .3f : 0);
                        float v = i%3==2 ? .7f : .1f;
                        w.Write(u); w.Write(1-v);
                    }
                    else { w.Write(i % 2 == 0 ? 0f : 1f); w.Write(i < 3 ? 0f : 1f); }
                    for (int j = 0; j < 4; j++) w.Write((float)j / 4);
                    w.Write((byte)skinning);
                    if (skinning == 0) index(humanoid ? bone : 0);
                    else if (skinning == 1 || skinning == 3)
                    { index(0); index(0); w.Write(.3f); if (skinning == 3) for (int j = 0; j < 9; j++) w.Write(0f); }
                    else { for (int j = 0; j < 4; j++) index(0); for (int j = 0; j < 4; j++) w.Write(.25f); }
                    w.Write(1f);
                }
                w.Write(vertexCount); for (int i = 0; i < vertexCount; i++) index(i);
                w.Write(1); text("missing.png");
                w.Write(2);
                for (int material = 0; material < 2; material++)
                {
                    text("材質" + material); text("mat" + material);
                    for (int j = 0; j < 3; j++) w.Write(1f); w.Write(material==0 ? firstAlpha : secondAlpha);
                    for (int j = 0; j < 7; j++) w.Write(0f);
                    w.Write((byte)1); for (int j = 0; j < 5; j++) w.Write(0f);
                    index(0); index(-1); w.Write((byte)0); w.Write((byte)1); w.Write((byte)0); text(""); w.Write(vertexCount / 2);
                }
                w.Write(names.Length);
                for (int i = 0; i < names.Length; i++)
                {
                    text(missingChest && i == 2 ? "unmapped-chest" : names[i]); text("");
                    var p = humanoid ? locations[i] : locations[0]; vector(p[0],p[1],p[2]);
                    index(cycle && i == 0 ? 0 : humanoid ? parents[i] : -1); w.Write(0); w.Write((ushort)0); vector(0,.1f,0);
                }
                w.Write(3);
                text("あ"); text("a"); w.Write((byte)3); w.Write((byte)1); w.Write(humanoid ? 2 : 1); index(5); vector(0,.02f,-.01f);
                if (humanoid) { index(8); vector(0,.02f,-.01f); }
                text("group"); text(""); w.Write((byte)3); w.Write((byte)0); w.Write(1); index(0); w.Write(.5f);
                text("material-change"); text(""); w.Write((byte)3); w.Write((byte)8); w.Write(1); index(-1); w.Write((byte)0); for (int j = 0; j < 28; j++) w.Write(0f);
                w.Write(0); w.Write(0); w.Write(0);
                if (skinning == 4) w.Write(0);
                return stream.ToArray();
            }
        }
    }
}
