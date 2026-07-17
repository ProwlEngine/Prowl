Shader "Default/Standard"
{
    Properties
    {
        _MainTex ("Albedo", Texture2D) = "grid" {}
        _MainColor ("Tint", Color) = (1.0, 1.0, 1.0, 1.0)
        _Tiling ("Tiling", Vector) = (1.0, 1.0, 0.0, 0.0)
        _Offset ("Offset", Vector) = (0.0, 0.0, 0.0, 0.0)

        _NormalTex ("Normal", Texture2D) = "normal" {}

        _SurfaceTex ("Surface (AO, Roughness, Metallicness)", Texture2D) = "surface" {}

        _EmissionTex ("Emission", Texture2D) = "emission" {}
        _EmissionIntensity ("Emission Intensity", Float) = 1.0

        _AlphaCutoff ("Alpha Cutoff", Float) = 0.5

        _ParallaxMap ("Height Map (G)", Texture2D) = "black" {}
        _Parallax ("Height Scale", Float) = 0.0
        _ParallaxSteps ("POM Steps", Integer) = 16

        _TranslucencyMap ("Translucency (B) Occlusion (G)", Texture2D) = "white" {}
        _TranslucencyStrength ("Translucency Strength", Float) = 0.0
        _ScatteringPower ("Scattering Power", Float) = 0.0
        _ScatteringDistortion ("Scattering Distortion", Float) = 0.5
        _ScatteringScale ("Scattering Scale", Float) = 1.0
    }

    Pass
    {
        Name "Standard"
        Tags { "RenderOrder" = "Opaque" }
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
            float _AlphaCutoff;
            Sampler2D<float4> _ParallaxMap;
            float _Parallax;
            int _ParallaxSteps;
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
            float2 uv2 : TEXCOORD1;
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
            float2 vLightmapUV2 : TEXCOORD5;
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
            output.vLightmapUV2 = input.uv2;
            output.worldPos = TransformPosition(v);
            output.vColor = GetInstanceColor(v);

            float3 nrm = input.normal;
            nrm = GetMorphedNormal(nrm, input.vid);
            output.vNormal = TransformDirection(v, nrm);

            output.vTangent = float3(1, 0, 0);
            output.vBitangent = float3(0, 1, 0);
            static if (HAS_TANGENTS)
            {
                float3 tan = input.tangent.xyz;
                tan = GetMorphedTangent(tan, input.vid);
                output.vTangent = TransformDirection(v, tan);
                output.vBitangent = cross(output.vTangent, output.vNormal) * input.tangent.w;
                if (dot(output.vBitangent, output.vBitangent) < 0.000001) {
                    output.vTangent = abs(output.vNormal.y) < 0.999 ? normalize(cross(output.vNormal, float3(0,1,0))) : normalize(cross(output.vNormal, float3(1,0,0)));
                    output.vBitangent = cross(output.vTangent, output.vNormal) * input.tangent.w;
                }
            }
            return output;
        }

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
            float4 result = StandardSurface(input.texCoord0, input.worldPos, input.vColor,
                input.vNormal, input.vTangent, input.vBitangent,
                Mat._MainTex, Mat._NormalTex, Mat._SurfaceTex, Mat._EmissionTex,
                Mat._EmissionIntensity, Mat._MainColor,
                Mat._ParallaxMap, Mat._Parallax, Mat._ParallaxSteps,
                Mat._TranslucencyMap, Mat._TranslucencyStrength,
                Mat._ScatteringPower, Mat._ScatteringDistortion, Mat._ScatteringScale,
                input.vLightmapUV2, HAS_TANGENTS, input.position.xy);

            if (result.a < Mat._AlphaCutoff)
                discard;

            return float4(result.rgb, 1.0);
        }
        ENDSLANG
    }

    Pass
    {
        Name "Prepass"
        Tags { "LightMode" = "Prepass" }
        Cull Back
        ZWrite On

        SLANGPROGRAM
        import ProwlCG;
        import VertexAttributes;
        import VariantAttributes;

        extern static const bool HAS_TANGENTS;

        struct MaterialData
        {
            Sampler2D<float4> _NormalTex;
            Sampler2D<float4> _MainTex;
            Sampler2D<float4> _SurfaceTex;
            float4 _MainColor;
            float _AlphaCutoff;
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
            float4 boneIndices : BLENDINDICES0;
            float4 boneWeights : BLENDWEIGHT0;
            float4 instRow0 : TEXCOORD8;
            float4 instRow1 : TEXCOORD9;
            float4 instRow2 : TEXCOORD10;
            float4 instRow3 : TEXCOORD11;
            uint vid : SV_VertexID;
        }
        struct Varyings
        {
            float4 position : SV_Position;
            float3 vNormal : TEXCOORD0;
            float3 vTangent : TEXCOORD1;
            float3 vBitangent : TEXCOORD2;
            float2 texCoord0 : TEXCOORD3;
            float4 vCurrClipNJ : TEXCOORD4;
            float4 vPrevClip : TEXCOORD5;
        }
        struct FragOutput
        {
            float4 normalOut : SV_Target0;
            float4 motionRM : SV_Target1;
        }

        MeshVertex MakeVertex(VertexInput input)
        {
            MeshVertex v;
            v.position = input.position;
            v.normal = input.normal;
            v.tangent = input.tangent;
            v.color = float4(1, 1, 1, 1);
            v.vid = input.vid;
            v.boneIndices = input.boneIndices;
            v.boneWeights = input.boneWeights;
            v.instanceRow0 = input.instRow0;
            v.instanceRow1 = input.instRow1;
            v.instanceRow2 = input.instRow2;
            v.instanceRow3 = input.instRow3;
            v.instanceColor = float4(1, 1, 1, 1);
            v.instanceCustomData = float4(0);
            return v;
        }

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            MeshVertex v = MakeVertex(input);
            Varyings output;
            output.position = TransformClip(v);

            float3 nrm = input.normal;
            nrm = GetMorphedNormal(nrm, input.vid);
            output.vNormal = TransformDirection(v, nrm);

            output.vTangent = float3(1, 0, 0);
            output.vBitangent = float3(0, 1, 0);
            static if (HAS_TANGENTS)
            {
                float3 tan = input.tangent.xyz;
                tan = GetMorphedTangent(tan, input.vid);
                output.vTangent = TransformDirection(v, tan);
                output.vBitangent = cross(output.vTangent, output.vNormal) * input.tangent.w;
                if (dot(output.vBitangent, output.vBitangent) < 0.000001) {
                    output.vTangent = abs(output.vNormal.y) < 0.999 ? normalize(cross(output.vNormal, float3(0,1,0))) : normalize(cross(output.vNormal, float3(1,0,0)));
                    output.vBitangent = cross(output.vTangent, output.vNormal) * input.tangent.w;
                }
            }
            output.texCoord0 = input.uv * Mat._Tiling + Mat._Offset;

            float4 worldPos = mul(GetModelMatrix(v), float4(input.position, 1.0));
            output.vCurrClipNJ = mul(Frame.prowl_MatVP_NonJittered, worldPos);
            float4 prevWorldPos = mul(Object.prowl_PrevObjectToWorld, float4(input.position, 1.0));
            output.vPrevClip = mul(Frame.prowl_PrevViewProj, prevWorldPos);
            return output;
        }

        [shader("fragment")]
        FragOutput Fragment(Varyings input)
        {
            if (Mat._AlphaCutoff > 0.0)
            {
                float alpha = Mat._MainTex.Sample(input.texCoord0).a * Mat._MainColor.a;
                if (alpha < Mat._AlphaCutoff) discard;
            }

            float3 worldNormal;
            static if (HAS_TANGENTS)
                worldNormal = ApplyNormalMap(Mat._NormalTex, input.texCoord0, input.vNormal, input.vTangent, input.vBitangent);
            else
                worldNormal = normalize(input.vNormal);

            FragOutput o;
            o.normalOut = EncodeViewNormal(worldNormal);

            float2 currNDC = (input.vCurrClipNJ.xy / input.vCurrClipNJ.w) * 0.5 + 0.5;
            float2 prevNDC = (input.vPrevClip.xy / input.vPrevClip.w) * 0.5 + 0.5;
            float4 surface = Mat._SurfaceTex.Sample(input.texCoord0);
            o.motionRM = float4(currNDC - prevNDC, surface.g, surface.b);
            return o;
        }
        ENDSLANG
    }

    Pass
    {
        Name "StandardShadow"
        Tags { "LightMode" = "ShadowCaster" }
        Cull Back

        SLANGPROGRAM
        import ProwlCG;
        import VertexAttributes;

        struct MaterialData
        {
            Sampler2D<float4> _MainTex;
            float4 _MainColor;
            float _AlphaCutoff;
            float2 _Tiling;
            float2 _Offset;
        }
        ParameterBlock<MaterialData> Mat;

        struct VertexInput
        {
            float3 position : POSITION0;
            float2 uv : TEXCOORD0;
            float4 boneIndices : BLENDINDICES0;
            float4 boneWeights : BLENDWEIGHT0;
            float4 instRow0 : TEXCOORD8;
            float4 instRow1 : TEXCOORD9;
            float4 instRow2 : TEXCOORD10;
            float4 instRow3 : TEXCOORD11;
            uint vid : SV_VertexID;
        }
        struct Varyings
        {
            float4 position : SV_Position;
            float2 texCoord0 : TEXCOORD0;
        }

        MeshVertex MakeVertex(VertexInput input)
        {
            MeshVertex v;
            v.position = input.position;
            v.normal = float3(0, 1, 0);
            v.tangent = float4(1, 0, 0, 1);
            v.color = float4(1, 1, 1, 1);
            v.vid = input.vid;
            v.boneIndices = input.boneIndices;
            v.boneWeights = input.boneWeights;
            v.instanceRow0 = input.instRow0;
            v.instanceRow1 = input.instRow1;
            v.instanceRow2 = input.instRow2;
            v.instanceRow3 = input.instRow3;
            v.instanceColor = float4(1, 1, 1, 1);
            v.instanceCustomData = float4(0);
            return v;
        }

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            MeshVertex v = MakeVertex(input);
            Varyings output;
            output.position = TransformClip(v);
            output.texCoord0 = input.uv * Mat._Tiling + Mat._Offset;
            return output;
        }

        [shader("fragment")]
        float Fragment(Varyings input) : SV_Depth
        {
            if (Mat._AlphaCutoff > 0.0)
            {
                float alpha = Mat._MainTex.Sample(input.texCoord0).a * Mat._MainColor.a;
                if (alpha < Mat._AlphaCutoff) discard;
            }
            return input.position.z;
        }
        ENDSLANG
    }
}
