Shader "PMX2FBX/PC Toon Transparent"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Texture", 2D) = "white" {}
        [Toggle] _DepthPrepass ("Occlusion Protection", Float) = 1
        _DepthCutoff ("Occlusion Alpha Threshold", Range(0.01,1)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Cull [_Cull]
        // Protect solid regions of soft alpha materials from transparent layers
        // drawn later (including another submesh of this same skinned renderer).
        // The color pass still blends the original alpha; soft edges remain soft.
        Pass
        {
            Name "OCCLUSION_DEPTH"
            Tags { "LightMode"="Always" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            CGPROGRAM
            #pragma vertex depthVert
            #pragma fragment depthFrag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _DepthPrepass, _DepthCutoff;
            struct DepthInput { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct DepthOutput { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed alpha : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            DepthOutput depthVert(DepthInput v)
            {
                DepthOutput o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(DepthOutput,o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos=UnityObjectToClipPos(v.vertex); o.uv=TRANSFORM_TEX(v.uv,_MainTex); o.alpha=v.color.a*_Color.a;
                return o;
            }
            fixed4 depthFrag(DepthOutput i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                clip(_DepthPrepass-.5);
                clip(tex2D(_MainTex,i.uv).a*i.alpha-_DepthCutoff);
                return 0;
            }
            ENDCG
        }
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
