Shader "PMX2FBX/PC Toon Transparent"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Texture", 2D) = "white" {}
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Cull [_Cull]
        CGPROGRAM
        #pragma surface surf Toon alpha:fade fullforwardshadows
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
    Fallback "Transparent/Diffuse"
}
