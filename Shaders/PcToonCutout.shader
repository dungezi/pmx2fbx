Shader "PMX2FBX/PC Toon Cutout"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Texture", 2D) = "white" {}
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" }
        Cull [_Cull]
        ZWrite On
        CGPROGRAM
        #pragma surface surf Toon alphatest:_Cutoff addshadow fullforwardshadows
        #pragma target 3.0
        sampler2D _MainTex;
        fixed4 _Color;
        struct Input { float2 uv_MainTex; float4 color : COLOR; };
        half4 LightingToon(SurfaceOutput s, half3 lightDir, half atten)
        {
            half light = dot(s.Normal, lightDir) > 0.15 ? 1.0 : 0.45;
            return half4(s.Albedo * _LightColor0.rgb * light * atten, s.Alpha);
        }
        void surf(Input IN, inout SurfaceOutput o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color * IN.color;
            o.Albedo = c.rgb; o.Alpha = c.a;
        }
        ENDCG
    }
    Fallback "Transparent/Cutout/Diffuse"
}
