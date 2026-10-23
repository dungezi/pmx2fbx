using System;
using System.Collections;

namespace Pmx2Fbx.Core
{
    public sealed class AlphaCoverageResult
    {
        public MaterialAlphaMode Mode;
        public int Samples, Zero, Opaque, Partial;
        public int InteriorPartial;
        public bool UsedFallback;
    }
    // Texture rows use Unity's bottom-to-top ordering. No model names or material
    // name heuristics participate in this analysis.
    public sealed class AlphaCoverage
    {
        private readonly int width, height;
        private readonly byte[] pixels;
        private readonly bool opaque;
        private readonly byte opaqueThreshold;
        private sbyte[] edgeCache;
        public AlphaCoverage(int width, int height, byte[] pixels, byte opaqueThreshold = 250)
        {
            if (width <= 0 || height <= 0 || pixels == null || (long)width * height != pixels.Length)
                throw new ArgumentException("Invalid alpha texture dimensions.");
            this.width = width; this.height = height; this.pixels = pixels;
            this.opaqueThreshold = opaqueThreshold;
            opaque = Array.TrueForAll(pixels, a => a >= opaqueThreshold);
        }
        private bool IsEdgePartial(int x, int y)
        {
            if (edgeCache == null) edgeCache = new sbyte[pixels.Length];
            int index = y*width+x;
            if (edgeCache[index] != 0) return edgeCache[index] == 1;
            // Antialiased hair/lace edges contain intermediate alpha too. Distinguish
            // a narrow edge next to a clear/solid pixel from a broad translucent area.
            for (int dy=-2; dy<=2; dy++) for (int dx=-2; dx<=2; dx++)
            {
                int px=(x+dx+width)%width, py=(y+dy+height)%height;
                byte a=pixels[py*width+px];
                if (a==0 || a>=opaqueThreshold) { edgeCache[index]=1; return true; }
            }
            edgeCache[index]=2; return false;
        }
        public AlphaCoverageResult Analyze(PmxModel model, int firstIndex, int indexCount)
        {
            if (firstIndex < 0 || indexCount < 0 || indexCount % 3 != 0 || firstIndex > model.Indices.Length - indexCount)
                throw new ArgumentOutOfRangeException(nameof(firstIndex));
            if (opaque) return new AlphaCoverageResult { Mode = MaterialAlphaMode.Opaque };
            var scan = new Scan(this);
            for (int i = firstIndex; i < firstIndex + indexCount && !scan.Full; i += 3)
            {
                var a = model.Vertices[model.Indices[i]]; var b = model.Vertices[model.Indices[i+1]]; var c = model.Vertices[model.Indices[i+2]];
                scan.Triangle(a.U, 1d-a.V, b.U, 1d-b.V, c.U, 1d-c.V);
            }
            return scan.Finish();
        }
        private sealed class Scan
        {
            private readonly AlphaCoverage map;
            private readonly BitArray visited;
            private readonly AlphaCoverageResult result = new AlphaCoverageResult();
            private long work;
            public bool Full => result.Samples == map.pixels.Length || result.UsedFallback;
            public Scan(AlphaCoverage map) { this.map = map; visited = new BitArray(map.pixels.Length); }
            private void Mark(int x, int y)
            {
                int index = y * map.width + x;
                if (visited[index]) return;
                visited[index] = true; result.Samples++;
                byte alpha = map.pixels[index];
                if (alpha == 0) result.Zero++;
                else if (alpha >= map.opaqueThreshold) result.Opaque++;
                else { result.Partial++; if (!map.IsEdgePartial(x,y)) result.InteriorPartial++; }
            }
            private void Point(double u, double v)
            {
                u -= Math.Floor(u); v -= Math.Floor(v);
                Mark(Math.Min(map.width-1, (int)(u*map.width)), Math.Min(map.height-1, (int)(v*map.height)));
            }
            private void Edge(double u0, double v0, double u1, double v1)
            {
                int steps = Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(u1-u0)*map.width, Math.Abs(v1-v0)*map.height)));
                for (int i=0; i<=steps; i++) Point(u0+(u1-u0)*i/steps, v0+(v1-v0)*i/steps);
                work += steps;
            }
            private void Fallback()
            {
                result.UsedFallback = true;
                for (int y=0; y<map.height; y++) for (int x=0; x<map.width; x++) Mark(x,y);
            }
            public void Triangle(double au, double av, double bu, double bv, double cu, double cv)
            {
                double minU=Math.Min(au,Math.Min(bu,cu)), maxU=Math.Max(au,Math.Max(bu,cu));
                double minV=Math.Min(av,Math.Min(bv,cv)), maxV=Math.Max(av,Math.Max(bv,cv));
                if (double.IsNaN(minU+maxU+minV+maxV) || double.IsInfinity(minU+maxU+minV+maxV)
                    || Math.Abs(minU)>1000000 || Math.Abs(maxU)>1000000 || Math.Abs(minV)>1000000 || Math.Abs(maxV)>1000000
                    || (Math.Floor(maxU)-Math.Floor(minU)+1)*(Math.Floor(maxV)-Math.Floor(minV)+1)>64 || work>16000000)
                { Fallback(); return; }
                Edge(au,av,bu,bv); Edge(bu,bv,cu,cv); Edge(cu,cv,au,av);
                // Scan actual UV triangle interiors, including repeat-wrapped tiles.
                // A rectangle or vertex-only test would misclassify atlas padding or
                // miss a transparent hole entirely inside a polygon.
                var intersections = new double[6];
                for (int ty=(int)Math.Floor(minV); ty<=Math.Floor(maxV); ty++)
                for (int tx=(int)Math.Floor(minU); tx<=Math.Floor(maxU); tx++)
                {
                    int y0=Math.Max(0,(int)Math.Ceiling((minV-ty)*map.height-.5));
                    int y1=Math.Min(map.height-1,(int)Math.Floor((maxV-ty)*map.height-.5));
                    for (int y=y0; y<=y1; y++)
                    {
                        double v=ty+(y+.5)/map.height; int count=0;
                        Intersect(au,av,bu,bv,v,intersections,ref count);
                        Intersect(bu,bv,cu,cv,v,intersections,ref count);
                        Intersect(cu,cv,au,av,v,intersections,ref count);
                        if (count==0) continue;
                        double lo=intersections[0], hi=lo;
                        for (int k=1;k<count;k++) { lo=Math.Min(lo,intersections[k]); hi=Math.Max(hi,intersections[k]); }
                        int x0=Math.Max(0,(int)Math.Ceiling((lo-tx)*map.width-.5));
                        int x1=Math.Min(map.width-1,(int)Math.Floor((hi-tx)*map.width-.5));
                        work += Math.Max(0,x1-x0+1);
                        if (work>16000000) { Fallback(); return; }
                        for (int x=x0;x<=x1;x++) Mark(x,y);
                    }
                }
            }
            private static void Intersect(double u0,double v0,double u1,double v1,double v,double[] output,ref int count)
            {
                if (v<Math.Min(v0,v1) || v>Math.Max(v0,v1)) return;
                if (Math.Abs(v1-v0)<1e-15) { output[count++]=u0; output[count++]=u1; }
                else output[count++]=u0+(u1-u0)*(v-v0)/(v1-v0);
            }
            public AlphaCoverageResult Finish()
            {
                result.Mode = result.Zero+result.Partial==0 ? MaterialAlphaMode.Opaque
                    : result.Partial==0 || result.Zero>0 && result.Opaque>0 && result.InteriorPartial<=result.Partial*.05
                        ? MaterialAlphaMode.Cutout : MaterialAlphaMode.Blend;
                return result;
            }
        }
    }
}
