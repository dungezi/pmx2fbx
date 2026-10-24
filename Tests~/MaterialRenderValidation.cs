using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Run with a graphics device (omit -nographics). Synthetic geometry only.
public static class MaterialRenderValidation
{
    public static void Run()
    {
        try { Check(); CheckTransparentSubmeshes(); Debug.Log("PMX2FBX_MATERIAL_DEPTH_RENDER_PASS"); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
    public static void Check()
    {
        var root=new GameObject("MaterialRenderingTest");
        var camera=new GameObject("Camera").AddComponent<Camera>(); camera.transform.SetParent(root.transform);
        var light=new GameObject("Light").AddComponent<Light>(); light.transform.SetParent(root.transform); light.type=LightType.Directional;
        var texture=new Texture2D(32,32,TextureFormat.RGBA32,false);
        var front=new Material(Shader.Find("PMX2FBX/PC Toon Cutout"));
        var back=new Material(Shader.Find("Unlit/Color")); back.color=Color.green;
        var blend=new Material(Shader.Find("PMX2FBX/PC Toon Transparent")); blend.color=new Color(0,0,1,.5f);
        var target=RenderTexture.GetTemporary(256,256,24,RenderTextureFormat.ARGB32);
        var pixels=new Texture2D(256,256,TextureFormat.RGBA32,false); var previous=RenderTexture.active;
        try
        {
            light.intensity=1; RenderSettings.ambientMode=AmbientMode.Flat; RenderSettings.ambientLight=Color.gray;
            camera.orthographic=true; camera.orthographicSize=1; camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=Color.blue;
            camera.transform.position=new Vector3(0,0,-3); camera.transform.LookAt(Vector3.zero); camera.targetTexture=target;
            for(int y=0;y<32;y++) for(int x=0;x<32;x++) texture.SetPixel(x,y,new Color(1,0,0,x<16 ? 0 : 1));
            texture.Apply(); front.mainTexture=texture; front.SetInt("_Cull",0); blend.SetInt("_Cull",0);
            Quad(root.transform,front,0); Quad(root.transform,back,.5f);
            Capture(camera,target,pixels,"cutout-mask.png");
            Expect(pixels.GetPixel(96,128).g>.7f && pixels.GetPixel(160,128).r>.3f,"Cutout hole reveals background while solid mask remains visible");
            Quad(root.transform,blend,.25f);
            Capture(camera,target,pixels,"cutout-depth.png");
            var hole=pixels.GetPixel(96,128); var solid=pixels.GetPixel(160,128);
            Expect(hole.g>.2f && hole.b>.2f && solid.r>.3f && solid.b<.15f,"Cutout writes depth and occludes later transparent objects only in solid pixels");
            for(int y=0;y<32;y++) for(int x=0;x<32;x++) texture.SetPixel(x,y,new Color(1,0,0,.6f));
            texture.Apply(); front.SetFloat("_Cutoff",.75f);
            Capture(camera,target,pixels,"cutout-threshold.png");
            Expect(pixels.GetPixel(160,128).r<.15f,"Adjustable alpha cutoff clips pixels below the chosen threshold");
        }
        finally
        {
            camera.targetTexture=null; RenderTexture.active=previous; RenderTexture.ReleaseTemporary(target);
            UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(front); UnityEngine.Object.DestroyImmediate(back);
            UnityEngine.Object.DestroyImmediate(blend); UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(pixels);
        }
    }
    private static void Quad(Transform parent,Material material,float z)
    {
        var quad=GameObject.CreatePrimitive(PrimitiveType.Quad); quad.transform.SetParent(parent); quad.transform.position=new Vector3(0,0,z);
        quad.GetComponent<Renderer>().sharedMaterial=material;
    }
    public static void CheckTransparentSubmeshes()
    {
        var root=new GameObject("TransparentSubmeshTest");
        var camera=new GameObject("Camera").AddComponent<Camera>(); camera.transform.SetParent(root.transform);
        var light=new GameObject("Light").AddComponent<Light>(); light.transform.SetParent(root.transform); light.type=LightType.Directional; light.intensity=1;
        var hair=new Material(Shader.Find("PMX2FBX/PC Toon Transparent")); hair.color=new Color(1,0,0,.8f); hair.SetInt("_Cull",0);
        var shadow=new Material(hair); shadow.color=new Color(0,0,0,.8f); shadow.SetFloat("_DepthPrepass",0);
        var back=new Material(Shader.Find("Unlit/Color")); back.color=Color.green;
        var target=RenderTexture.GetTemporary(256,256,24,RenderTextureFormat.ARGB32);
        var pixels=new Texture2D(256,256,TextureFormat.RGBA32,false); var previous=RenderTexture.active;
        var mesh=new Mesh();
        try
        {
            camera.orthographic=true; camera.orthographicSize=1; camera.transform.position=new Vector3(0,0,-3); camera.transform.LookAt(Vector3.zero);
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=Color.blue; camera.targetTexture=target;
            var vertices=new Vector3[8]; var normals=new Vector3[8]; var colors=new Color32[8];
            for(int s=0;s<2;s++) for(int i=0;i<4;i++)
            {
                vertices[s*4+i]=new Vector3(i==0 || i==3 ? -.5f : .5f,i<2 ? -.5f : .5f,s*.25f);
                normals[s*4+i]=Vector3.back; colors[s*4+i]=new Color32(255,255,255,255);
            }
            mesh.vertices=vertices; mesh.normals=normals; mesh.colors32=colors; mesh.subMeshCount=2;
            mesh.SetTriangles(new[]{0,2,1,0,3,2},0); mesh.SetTriangles(new[]{4,6,5,4,7,6},1);
            mesh.boneWeights=new BoneWeight[8]; var weights=mesh.boneWeights;
            for(int i=0;i<weights.Length;i++) weights[i]=new BoneWeight{boneIndex0=0,weight0=1};
            mesh.boneWeights=weights; mesh.bindposes=new[]{Matrix4x4.identity};
            var renderer=root.AddComponent<SkinnedMeshRenderer>(); renderer.sharedMesh=mesh; renderer.bones=new[]{root.transform};
            renderer.rootBone=root.transform; renderer.sharedMaterials=new[]{hair,shadow}; renderer.localBounds=new Bounds(Vector3.zero,Vector3.one*2);
            Quad(root.transform,back,.5f);
            hair.SetFloat("_DepthPrepass",0); Capture(camera,target,pixels,"blend-submeshes-unprotected.png"); var before=pixels.GetPixel(128,128);
            hair.SetFloat("_DepthPrepass",1); hair.SetFloat("_DepthCutoff",.5f);
            Capture(camera,target,pixels,"blend-submeshes-protected.png"); var after=pixels.GetPixel(128,128);
            Expect(after.r>before.r+.25f,"A later fake-shadow submesh must not draw over protected soft-alpha foreground");
            hair.color=new Color(1,0,0,.2f);
            Capture(camera,target,pixels,"blend-soft-edge-protected.png"); after=pixels.GetPixel(128,128);
            hair.SetFloat("_DepthPrepass",0); Capture(camera,target,pixels,"blend-soft-edge-unprotected.png"); before=pixels.GetPixel(128,128);
            Expect(Math.Abs(after.r-before.r)<.03f && Math.Abs(after.g-before.g)<.03f,"Low alpha edges preserve blending without writing occlusion depth");
            hair.color=new Color(1,0,0,.8f); hair.SetFloat("_DepthPrepass",1); hair.SetFloat("_DepthCutoff",.9f);
            Capture(camera,target,pixels,"blend-occlusion-threshold.png");
            Expect(pixels.GetPixel(128,128).r<.3f,"Occlusion threshold can opt partial alpha out of depth protection");
        }
        finally
        {
            camera.targetTexture=null; RenderTexture.active=previous; RenderTexture.ReleaseTemporary(target);
            UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(hair); UnityEngine.Object.DestroyImmediate(shadow);
            UnityEngine.Object.DestroyImmediate(back); UnityEngine.Object.DestroyImmediate(mesh); UnityEngine.Object.DestroyImmediate(pixels);
        }
    }
    private static void Capture(Camera camera,RenderTexture target,Texture2D pixels,string file)
    {
        camera.Render(); RenderTexture.active=target; pixels.ReadPixels(new Rect(0,0,256,256),0,0); pixels.Apply();
        Directory.CreateDirectory("MaterialDepthPreview"); File.WriteAllBytes("MaterialDepthPreview/"+file,pixels.EncodeToPNG());
    }
    private static void Expect(bool condition,string message) { if(!condition) throw new Exception(message); }
}
