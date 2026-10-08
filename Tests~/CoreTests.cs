using System;
using System.IO;
using System.Linq;
using Pmx2Fbx.Core;
using Pmx2Fbx.Tests;

public static class CoreTests
{
    private static int passed;
    private static void Expect(bool condition, string label)
    { if (!condition) throw new Exception(label); passed++; }
    private static void Reject(byte[] data, string label)
    {
        try { PmxReader.Read(new MemoryStream(data)); throw new Exception("Accepted: " + label); }
        catch (InvalidDataException) { passed++; }
    }
    public static int Main()
    {
        foreach (bool utf16 in new[] { false, true })
            foreach (int width in new[] { 1, 2, 4 })
                foreach (int skinning in new[] { 0, 1, 2, 3, 4 })
                {
                    var bytes = PmxFixture.Create(utf16, width, skinning);
                    var model = PmxReader.Read(new MemoryStream(bytes));
                    Expect(model.Name == "合成テスト" && model.Vertices.Length == 6 && model.Materials.Length == 2 && model.Morphs.Length == 3, "encoding, indices, skinning " + skinning);
                    var offsets = MorphResolver.Resolve(model, 1, out bool complete);
                    Expect(complete && Math.Abs(offsets[5].Y - .01f) < 1e-6f, "group morph");
                    Expect(model.Vertices[0].ExtraUvs.Length == 1, "extra UV");
                }
        var good = PmxFixture.Create();
        for (int len = 0; len < good.Length; len += 7)
        { var truncated = new byte[len]; Array.Copy(good, truncated, len); Reject(truncated, "truncated " + len); }
        var signature = (byte[])good.Clone(); signature[0] = 0; Reject(signature, "signature");
        var header = (byte[])good.Clone(); header[11] = 3; Reject(header, "index width");
        Reject(PmxFixture.Create(cycle: true), "cyclic skeleton");
        var morphModel = PmxReader.Read(new MemoryStream(good));
        morphModel.Morphs[1].References[0] = new MorphReference { Morph = 1, Weight = 1 };
        try { MorphResolver.Resolve(morphModel, 1, out _); throw new Exception("Accepted morph cycle"); }
        catch (InvalidDataException) { passed++; }
        Console.WriteLine("PMX PASS: " + passed + " assertions");
        foreach (int version in new[] { 0, 1, 2, 3, 4 })
        {
            bool ascii = version == 4;
            var data = XpsFixture.Create(version, ascii, humanoid:true, manyWeights:version >= 3);
            var xps = XpsReader.Read(new MemoryStream(data), ascii);
            Expect(xps.Format == "XPS" && xps.Bones.Length == 19 && xps.Vertices.Length == 12 && xps.Indices.Length == 12 && xps.Materials.Length == 2, "XPS mesh/rig " + version);
            Expect(xps.Vertices[0].Color[0] == 120 && xps.Vertices[0].U == .6f && xps.Vertices[0].ExtraUvs[0][1] == .7f, "XPS colors/texture UV selection " + version);
            Expect(xps.Materials[0].Name.Length > 128 && xps.Materials[1].Texture == 2 && xps.Indices[6] == 6, "XPS strings/mesh offsets " + version);
            if (version >= 3) Expect(xps.Vertices[0].Bones[0] == 0 && Math.Abs(xps.Vertices[0].Weights[0] - .375f) < 1e-5f && xps.Warnings.Exists(w => w.Contains("4 个")), "XPS merge/prune/normalize");
            var unrigged = XpsReader.Read(new MemoryStream(XpsFixture.Create(version, ascii, noBones:true)), ascii);
            Expect(unrigged.Bones.Length == 0 && unrigged.Vertices.Length == 6, "XPS static layout " + version);
            RejectXps(XpsFixture.Create(version, ascii, badParent:true), ascii, "bone cycle");
            RejectXps(XpsFixture.Create(version, ascii, badFace:true), ascii, "face index");
            RejectXps(XpsFixture.Create(version, ascii, badWeight:true), ascii, "weight index");
            for (int len = 0; len < data.Length - (ascii ? 40 : 0); len += 47) RejectXps(new ArraySegment<byte>(data, 0, len).ToArray(), ascii, "truncation " + version + "/" + len);
        }
        Expect(ModelReader.IsSupported("x.mesh.ascii") && ModelReader.IsSupported("x.XPS") && !ModelReader.IsSupported("x.obj"), "Format dispatch");
        string plain = "0\n1\nplain\n0\n0\n3\n0 0 0\n0 0 1\n255 255 255 255\n1 0 0\n0 0 1\n255 255 255 255\n0 1 0\n0 0 1\n255 255 255 255\n1\n0 1 2\n";
        var plainModel = XpsReader.Read(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(plain)), true);
        Expect(plainModel.Vertices.Length == 3 && plainModel.Vertices[0].ExtraUvs.Length == 0 && plainModel.Materials[0].Texture == -1, "Independent untextured ASCII fixture without UVs");
        Console.WriteLine("PASS: " + passed + " PMX + XPS core assertions");
        return 0;
    }
    private static void RejectXps(byte[] data, bool ascii, string label)
    {
        try { XpsReader.Read(new MemoryStream(data), ascii); throw new Exception("XPS accepted: " + label); }
        catch (InvalidDataException) { passed++; }
    }
}
