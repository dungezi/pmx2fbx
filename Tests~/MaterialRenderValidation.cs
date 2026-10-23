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
        try { Check(); Debug.Log("PMX2FBX_MATERIAL_DEPTH_RENDER_PASS"); EditorApplication.Exit(0); }
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
    private static void Capture(Camera camera,RenderTexture target,Texture2D pixels,string file)
    {
        camera.Render(); RenderTexture.active=target; pixels.ReadPixels(new Rect(0,0,256,256),0,0); pixels.Apply();
        Directory.CreateDirectory("MaterialDepthPreview"); File.WriteAllBytes("MaterialDepthPreview/"+file,pixels.EncodeToPNG());
    }
    private static void Expect(bool condition,string message) { if(!condition) throw new Exception(message); }
}
