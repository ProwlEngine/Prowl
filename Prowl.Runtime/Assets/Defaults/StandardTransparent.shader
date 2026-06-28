Shader "Default/StandardTransparent"
{
    Properties
    {
        _MainTex("Albedo", Texture2D) = "white" {}
        _MainColor("Tint", Color) = (1.0, 1.0, 1.0, 1.0)
        _Tiling("Tiling", Vector) = (1.0, 1.0, 0, 0)
        _Offset("Offset", Vector) = (0.0, 0.0, 0, 0)
        _NormalTex("Normal", Texture2D) = "normal" {}
        _SurfaceTex("Surface (AO, Roughness, Metallicness)", Texture2D) = "surface" {}
        _EmissionTex("Emission", Texture2D) = "emission" {}
        _EmissionIntensity("Emission Intensity", Float) = 1.0
        _TranslucencyMap("Translucency (B) Occlusion (G)", Texture2D) = "white" {}
        _TranslucencyStrength("Translucency Strength", Float) = 0.0
        _ScatteringPower("Scattering Power", Float) = 0.0
        _ScatteringDistortion("Scattering Distortion", Float) = 0.5
        _ScatteringScale("Scattering Scale", Float) = 1.0
    }

    Pass
    {
        Name "StandardTransparent"
        Tags { "RenderOrder" = "Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        SLANGPROGRAM

        import ProwlCG;
        import VertexAttributes;
        import StandardSurface;

        struct VertexInput
        {
            float3 position : POSITION0;
            float2 uv0 : TEXCOORD0;
            float3 normal : NORMAL0;
            float4 tangent : TANGENT0;
            float4 color : COLOR0;
            uint vid : SV_VertexID;
        }

        struct Varyings
        {
            float4 position : SV_Position;
            float2 texCoord0 : TEXCOORD0;
            float3 worldPos : TEXCOORD1;
            float4 vColor : COLOR0;
            float3 vNormal : NORMAL0;
            float3 vTangent : TANGENT0;
            float3 vBitangent : TEXCOORD2;
        }

        struct Material
        {
            float2 _Tiling;
            float2 _Offset;
            float _EmissionIntensity;
            float4 _MainColor;
            float _TranslucencyStrength;
            float _ScatteringPower;
            float _ScatteringDistortion;
            float _ScatteringScale;
            Sampler2D<float4> _MainTex;
            Sampler2D<float4> _NormalTex;
            Sampler2D<float4> _SurfaceTex;
            Sampler2D<float4> _EmissionTex;
            Sampler2D<float4> _TranslucencyMap;
        }
        ParameterBlock<Material> Mat;

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            Varyings o;
            o.position = TransformClip(input.position, input.vid);
            o.texCoord0 = input.uv0 * Mat._Tiling + Mat._Offset;
            o.worldPos = TransformPosition(input.position, input.vid);
            o.vColor = GetInstanceColor(input.color);
            o.vNormal = TransformDirection(input.normal);
            o.vTangent = float3(0.0);
            o.vBitangent = float3(0.0);
#ifdef HAS_TANGENTS
            o.vTangent = TransformDirection(input.tangent.xyz);
            o.vBitangent = cross(o.vTangent, o.vNormal) * input.tangent.w;
#endif
            return o;
        }

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
            return StandardSurface(input.texCoord0, input.worldPos, input.vColor,
                input.vNormal, input.vTangent, input.vBitangent,
                Mat._MainTex, Mat._NormalTex, Mat._SurfaceTex, Mat._EmissionTex,
                Mat._EmissionIntensity, Mat._MainColor,
                Mat._MainTex, 0.0, 0,
                Mat._TranslucencyMap, Mat._TranslucencyStrength,
                Mat._ScatteringPower, Mat._ScatteringDistortion, Mat._ScatteringScale,
                float2(0.0), input.position.xy); // transparent objects use realtime ambient / SH
        }

        ENDSLANG
    }
}
