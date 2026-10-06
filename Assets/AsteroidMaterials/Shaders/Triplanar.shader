Shader "Asteroids/Realistic Triplanar"
{
 Properties
 {
  _BaseMap("Colour", 2D) = "white" {}
  _SurfaceMap("Metal / Height / AO / Smoothness", 2D) = "white" {}
  _EmissionMap("Emission", 2D) = "black" {}
  _BaseColor("Tint", Color) = (1,1,1,1)
  _TextureScale("Tiles per mesh", Range(.1,10)) = 2
  _Relief("Relief", Range(0,.08)) = .015
  _EmissionStrength("Emission strength", Range(0,5)) = 0
  _Flow("Surface flow", Range(0,.1)) = 0
  [HideInInspector] _MappingCentre("Mapping centre", Vector) = (0,0,0,0)
  [HideInInspector] _MappingSize("Mapping size", Vector) = (1,1,1,0)
  [HideInInspector] _Cull("Cull", Float) = 2
  [HideInInspector] _Surface("Surface", Float) = 0
  [HideInInspector] _ZWrite("ZWrite", Float) = 1
 }
 SubShader
 {
  Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
  Pass
  {
   Name "ForwardLit"
   Tags { "LightMode"="UniversalForwardOnly" }
   Cull Back
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex Vert
   #pragma fragment Frag
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
   #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
   #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
   #pragma multi_compile_fragment _ _SHADOWS_SOFT
   #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
   #pragma multi_compile_fog
   #pragma multi_compile_instancing
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
   TEXTURE2D(_SurfaceMap); SAMPLER(sampler_SurfaceMap);
   TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);
   CBUFFER_START(UnityPerMaterial)
    float4 _BaseColor, _MappingCentre, _MappingSize;
    float _TextureScale, _Relief, _EmissionStrength, _Flow, _Cull, _Surface, _ZWrite;
   CBUFFER_END
   struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
   struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; float3 normalWS:TEXCOORD1; float3 mapping:TEXCOORD2; float3 normalOS:TEXCOORD3; float fog:TEXCOORD4; UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO };
   Varyings Vert(Attributes v)
   {
    Varyings o=(Varyings)0;
    UNITY_SETUP_INSTANCE_ID(v); UNITY_TRANSFER_INSTANCE_ID(v,o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    VertexPositionInputs p=GetVertexPositionInputs(v.positionOS.xyz);
    o.positionCS=p.positionCS; o.positionWS=p.positionWS;
    o.normalWS=TransformObjectToWorldNormal(v.normalOS); o.normalOS=v.normalOS;
    o.mapping=(v.positionOS.xyz-_MappingCentre.xyz)/max(_MappingSize.x,.0001)*_TextureScale;
    o.fog=ComputeFogFactor(p.positionCS.z); return o;
   }
   half4 SampleTri(TEXTURE2D_PARAM(tex,samp),float3 p,half3 w)
   {
    return SAMPLE_TEXTURE2D(tex,samp,p.zy)*w.x + SAMPLE_TEXTURE2D(tex,samp,p.xz)*w.y + SAMPLE_TEXTURE2D(tex,samp,p.xy)*w.z;
   }
   half4 Frag(Varyings i):SV_Target
   {
    UNITY_SETUP_INSTANCE_ID(i); UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    half3 weights=pow(abs(normalize(i.normalOS)),4); weights/=max(dot(weights,half3(1,1,1)),.0001);
    float3 p=i.mapping; p+=_Flow*_Time.y*float3(.37,.19,.11);
    half3 albedo=SampleTri(TEXTURE2D_ARGS(_BaseMap,sampler_BaseMap),p,weights).rgb*_BaseColor.rgb;
    half4 mask=SampleTri(TEXTURE2D_ARGS(_SurfaceMap,sampler_SurfaceMap),p,weights);
    half3 emission=SampleTri(TEXTURE2D_ARGS(_EmissionMap,sampler_EmissionMap),p,weights).rgb*_EmissionStrength;
    // Surface-gradient bump: no mesh UVs or tangents are required.
    float3 n=normalize(i.normalWS), dx=ddx(i.positionWS), dy=ddy(i.positionWS);
    float3 rx=cross(dy,n), ry=cross(n,dx); float det=dot(dx,rx);
    float meshSize=length(TransformObjectToWorldDir(float3(_MappingSize.x,0,0),false));
    float h=mask.g*_Relief*meshSize;
    float3 bumped=normalize(abs(det)*n-sign(det)*(ddx(h)*rx+ddy(h)*ry));
    if(abs(det)>.00000001) n=bumped;
    InputData input=(InputData)0;
    input.positionWS=i.positionWS; input.normalWS=n;
    input.viewDirectionWS=GetWorldSpaceNormalizeViewDir(i.positionWS);
    input.shadowCoord=TransformWorldToShadowCoord(i.positionWS);
    input.bakedGI=SampleSH(n); input.shadowMask=half4(1,1,1,1);
    input.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.positionCS);
    input.fogCoord=i.fog; input.vertexLighting=VertexLighting(i.positionWS,n);
    SurfaceData surface=(SurfaceData)0;
    surface.albedo=albedo; surface.metallic=mask.r; surface.smoothness=mask.a;
    surface.occlusion=mask.b; surface.emission=emission; surface.alpha=1; surface.normalTS=half3(0,0,1);
    half4 result=UniversalFragmentPBR(input,surface); result.rgb=MixFog(result.rgb,i.fog); return result;
   }
   ENDHLSL
  }
  UsePass "Universal Render Pipeline/Lit/ShadowCaster"
  UsePass "Universal Render Pipeline/Lit/DepthOnly"
 }
 Fallback Off
}
