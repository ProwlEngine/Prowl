Shader "Default/StandardTransparent"
{
    Properties
    {
        _MainTex ("Albedo", Texture2D) = "white" {}
        _MainColor ("Tint", Color) = (1.0, 1.0, 1.0, 1.0)
        _Tiling ("Tiling", Vector) = (1.0, 1.0, 0.0, 0.0)
        _Offset ("Offset", Vector) = (0.0, 0.0, 0.0, 0.0)

        _NormalTex ("Normal", Texture2D) = "normal" {}

        _SurfaceTex ("Surface (AO, Roughness, Metallicness)", Texture2D) = "surface" {}

        _EmissionTex ("Emission", Texture2D) = "emission" {}
        _EmissionIntensity ("Emission Intensity", Float) = 1.0

        _TranslucencyMap ("Translucency (B) Occlusion (G)", Texture2D) = "white" {}
        _TranslucencyStrength ("Translucency Strength", Float) = 0.0
        _ScatteringPower ("Scattering Power", Float) = 0.0
        _ScatteringDistortion ("Scattering Distortion", Float) = 0.5
        _ScatteringScale ("Scattering Scale", Float) = 1.0
    }

    Pass
    {
        Name "StandardTransparent"
        Tags { "RenderOrder" = "Transparent" }
        Blend SourceAlpha InverseSourceAlpha
        ZWrite Off
        Cull Back

        SLANGPROGRAM
        import StandardSurface;
        import VertexAttributes;
        import VariantAttributes;

        [VariantAxis]
        extern static const bool HAS_TANGENTS;

        struct MaterialData
        {
            Sampler2D<float4> _MainTex;
            Sampler2D<float4> _NormalTex;
            Sampler2D<float4> _SurfaceTex;
            Sampler2D<float4> _EmissionTex;
            float _EmissionIntensity;
            float4 _MainColor;
            Sampler2D<float4> _TranslucencyMap;
            float _TranslucencyStrength;
            float _ScatteringPower;
            float _ScatteringDistortion;
            float _ScatteringScale;
            float2 _Tiling;
            float2 _Offset;
        }
        ParameterBlock<MaterialData> Mat;

        struct VertexInput
        {
            float3 position : POSITION0;
            float2 uv : TEXCOORD0;
            float3 normal : NORMAL0;
            float4 tangent : TANGENT0;
            float4 color : COLOR0;
            float4 boneIndices : BLENDINDICES0;
            float4 boneWeights : BLENDWEIGHT0;
            float4 instRow0 : TEXCOORD8;
            float4 instRow1 : TEXCOORD9;
            float4 instRow2 : TEXCOORD10;
            float4 instRow3 : TEXCOORD11;
            float4 instColor : TEXCOORD12;
            float4 instCustom : TEXCOORD13;
            uint vid : SV_VertexID;
        }
        struct Varyings
        {
            float4 position : SV_Position;
            float2 texCoord0 : TEXCOORD0;
            float3 worldPos : TEXCOORD1;
            float4 vColor : COLOR0;
            float3 vNormal : TEXCOORD2;
            float3 vTangent : TEXCOORD3;
            float3 vBitangent : TEXCOORD4;
        }

        MeshVertex MakeVertex(VertexInput input)
        {
            MeshVertex v;
            v.position = input.position;
            v.normal = input.normal;
            v.tangent = input.tangent;
            v.color = input.color;
            v.vid = input.vid;
            v.boneIndices = input.boneIndices;
            v.boneWeights = input.boneWeights;
            v.instanceRow0 = input.instRow0;
            v.instanceRow1 = input.instRow1;
            v.instanceRow2 = input.instRow2;
            v.instanceRow3 = input.instRow3;
            v.instanceColor = input.instColor;
            v.instanceCustomData = input.instCustom;
            return v;
        }

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            MeshVertex v = MakeVertex(input);
            Varyings output;
            output.position = TransformClip(v);
            output.texCoord0 = input.uv * Mat._Tiling + Mat._Offset;
            output.worldPos = TransformPosition(v);
            output.vColor = GetInstanceColor(v);
            output.vNormal = TransformDirection(v, input.normal);

            output.vTangent = float3(1, 0, 0);
            output.vBitangent = float3(0, 1, 0);
            static if (HAS_TANGENTS)
            {
                output.vTangent = TransformDirection(v, input.tangent.xyz);
                output.vBitangent = cross(output.vTangent, output.vNormal) * input.tangent.w;
            }
            return output;
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
                float2(0.0), HAS_TANGENTS, input.position.xy);
        }
        ENDSLANG
    }
}
