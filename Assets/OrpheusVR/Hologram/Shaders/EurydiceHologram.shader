Shader "OrpheusVR/Eurydice Unlit Dissolve"
{
 Properties
 {
  _MainTex("Character PNG",2D)="white"{}
  _Tint("Tint",Color)=(1,1,1,1)
  _GlowOpacity("Cyan Glow Opacity",Range(0,1))=0.12
  _Opacity("Opacity",Range(0,1))=0.7
  _Dissolve("Dissolve",Range(0,1))=0
  _EdgeColor("Dissolve Edge",Color)=(0.15,1.5,1.25,1)
  _NoiseScale("Noise Scale",Float)=24
 }
 SubShader
 {
  Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent"}
  Pass
  {
   Name "HologramUnlit"
   Tags {"LightMode"="SRPDefaultUnlit"}
   Blend SrcAlpha OneMinusSrcAlpha
   ZWrite Off
   ZTest LEqual
   Cull Off
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile_instancing
   #pragma multi_compile _ STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
   CBUFFER_START(UnityPerMaterial)
    float4 _MainTex_ST, _Tint, _EdgeColor;
    float _Opacity, _Dissolve, _NoiseScale, _GlowOpacity;
   CBUFFER_END
   struct Attributes {float4 positionOS:POSITION;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
   struct Varyings {float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;float3 world:TEXCOORD1;UNITY_VERTEX_OUTPUT_STEREO};
   Varyings vert(Attributes v)
   {
    Varyings o;UNITY_SETUP_INSTANCE_ID(v);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    o.positionCS=TransformObjectToHClip(v.positionOS.xyz);o.world=TransformObjectToWorld(v.positionOS.xyz);o.uv=TRANSFORM_TEX(v.uv,_MainTex);return o;
   }
   float hash21(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
   float noise21(float2 p){float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash21(i),hash21(i+float2(1,0)),f.x),lerp(hash21(i+float2(0,1)),hash21(i+1),f.x),f.y);}
   half4 frag(Varyings i):SV_Target
   {
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    half4 c=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv);clip(c.a-0.035);
    float n=noise21(i.uv*_NoiseScale),threshold=lerp(-.065,1.065,_Dissolve);
    float mask=smoothstep(threshold-.03,threshold+.03,n);
    float edge=(1-smoothstep(.025,.07,abs(n-threshold)))*step(.001,_Dissolve)*step(_Dissolve,.999);
    float cyan=smoothstep(.02,.14,min(c.g,c.b)-c.r);
    float glowAlpha=lerp(1,_GlowOpacity,cyan);
    float scan=.96+.04*sin(i.world.y*180-_Time.y*4);
    return half4(c.rgb*_Tint.rgb*scan+_EdgeColor.rgb*edge,c.a*_Opacity*_Tint.a*mask*glowAlpha);
   }
   ENDHLSL
  }
 }
}
