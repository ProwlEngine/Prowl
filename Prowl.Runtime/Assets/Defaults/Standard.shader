Shader "Default/Standard"
{
    Properties
    {
        _MainTex("Albedo", Texture2D) = "grid" {}
        _MainColor("Tint", Color) = (1.0, 1.0, 1.0, 1.0)
        _Tiling("Tiling", Vector) = (1.0, 1.0, 0, 0)
        _Offset("Offset", Vector) = (0.0, 0.0, 0, 0)
        _NormalTex("Normal", Texture2D) = "normal" {}
        _SurfaceTex("Surface (AO, Roughness, Metallicness)", Texture2D) = "surface" {}
        _EmissionTex("Emission", Texture2D) = "emission" {}
        _EmissionIntensity("Emission Intensity", Float) = 1.0
        _AlphaCutoff("Alpha Cutoff", Float) = 0.5
        _ParallaxMap("Height Map (G)", Texture2D) = "black" {}
        _Parallax("Height Scale", Float) = 0.0
        _ParallaxSteps("POM Steps", Integer) = 16
        _TranslucencyMap("Translucency (B) Occlusion (G)", Texture2D) = "white" {}
        _TranslucencyStrength("Translucency Strength", Float) = 0.0
        _ScatteringPower("Scattering Power", Float) = 0.0
        _ScatteringDistortion("Scattering Distortion", Float) = 0.5
        _ScatteringScale("Scattering Scale", Float) = 1.0
    }

    Pass
    {
        Name "Standard"
        Tags { "RenderOrder" = "Opaque" }
        Cull Back

        SLANGPROGRAM

        import ProwlCG;
        import VertexAttributes;
        import StandardSurface;

        struct VertexInput
        {
            float3 position : POSITION0;
            float2 uv0 : TEXCOORD0;
            float2 uv1 : TEXCOORD1;
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
            float2 vLightmapUV2 : TEXCOORD3;
        }

        struct Material
        {
            float2 _Tiling;
            float2 _Offset;
            float _EmissionIntensity;
            float4 _MainColor;
            float _AlphaCutoff;
            float _Parallax;
            int _ParallaxSteps;
            float _TranslucencyStrength;
            float _ScatteringPower;
            float _ScatteringDistortion;
            float _ScatteringScale;
            Sampler2D<float4> _MainTex;
            Sampler2D<float4> _NormalTex;
            Sampler2D<float4> _SurfaceTex;
            Sampler2D<float4> _EmissionTex;
            Sampler2D<float4> _ParallaxMap;
            Sampler2D<float4> _TranslucencyMap;
        }
        ParameterBlock<Material> Mat;

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            Varyings o;
            o.position = TransformClip(input.position, input.vid);
            o.texCoord0 = input.uv0 * Mat._Tiling + Mat._Offset;
            o.vLightmapUV2 = input.uv1; // raw UV2; scale/offset applied in the fragment
            o.worldPos = TransformPosition(input.position, input.vid);
            o.vColor = GetInstanceColor(input.color);
            o.vNormal = TransformDirection(GetMorphedNormal(input.normal, input.vid));
            o.vTangent = float3(0.0);
            o.vBitangent = float3(0.0);
#ifdef HAS_TANGENTS
            o.vTangent = TransformDirection(GetMorphedTangent(input.tangent.xyz, input.vid));
            o.vBitangent = cross(o.vTangent, o.vNormal) * input.tangent.w;
            // Guard against degenerate tangent frames (parallel normal/tangent)
            if (dot(o.vBitangent, o.vBitangent) < 0.000001) {
                o.vTangent = abs(o.vNormal.y) < 0.999 ? normalize(cross(o.vNormal, float3(0,1,0))) : normalize(cross(o.vNormal, float3(1,0,0)));
                o.vBitangent = cross(o.vTangent, o.vNormal) * input.tangent.w;
            }
#endif
            return o;
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
                input.vLightmapUV2, input.position.xy);

            // Alpha cutout discard below threshold, output fully opaque
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

        struct VertexInput
        {
            float3 position : POSITION0;
            float2 uv0 : TEXCOORD0;
            float3 normal : NORMAL0;
            float4 tangent : TANGENT0;
            uint vid : SV_VertexID;
        }

        struct Varyings
        {
            float4 position : SV_Position;
            float3 vNormal : NORMAL0;
            float3 vTangent : TANGENT0;
            float3 vBitangent : TEXCOORD0;
            float2 texCoord0 : TEXCOORD1;
            float4 vCurrClipNJ : TEXCOORD2;
            float4 vPrevClip : TEXCOORD3;
        }

        struct Material
        {
            float2 _Tiling;
            float2 _Offset;
            float4 _MainColor;
            float _AlphaCutoff;
            Sampler2D<float4> _NormalTex;
            Sampler2D<float4> _MainTex;
            Sampler2D<float4> _SurfaceTex;
        }
        ParameterBlock<Material> Mat;

        struct FragOut
        {
            float4 normalOut : SV_Target0;
            float4 motionRM : SV_Target1;
        }

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            Varyings o;
            o.position = TransformClip(input.position, input.vid); // jittered, for raster + depth
            o.vNormal = TransformDirection(GetMorphedNormal(input.normal, input.vid));
            o.vTangent = float3(0.0);
            o.vBitangent = float3(0.0);
#ifdef HAS_TANGENTS
            o.vTangent = TransformDirection(GetMorphedTangent(input.tangent.xyz, input.vid));
            o.vBitangent = cross(o.vTangent, o.vNormal) * input.tangent.w;
            if (dot(o.vBitangent, o.vBitangent) < 0.000001) {
                o.vTangent = abs(o.vNormal.y) < 0.999 ? normalize(cross(o.vNormal, float3(0,1,0))) : normalize(cross(o.vNormal, float3(1,0,0)));
                o.vBitangent = cross(o.vTangent, o.vNormal) * input.tangent.w;
            }
#endif
            o.texCoord0 = input.uv0 * Mat._Tiling + Mat._Offset;

            // Jitter-free current + previous clip positions for motion vectors.
            float4 worldPos = mul(GetModelMatrix(), float4(input.position, 1.0));
            o.vCurrClipNJ = mul(Frame.prowl_MatVP_NonJittered, worldPos);
            float4 prevWorldPos = mul(Object.prowl_PrevObjectToWorld, float4(input.position, 1.0));
            o.vPrevClip = mul(Frame.prowl_PrevViewProj, prevWorldPos);
            return o;
        }

        [shader("fragment")]
        FragOut Fragment(Varyings input)
        {
            // Alpha cutoff for cutout mode
            if (Mat._AlphaCutoff > 0.0)
            {
                float alpha = Mat._MainTex.Sample(input.texCoord0).a * Mat._MainColor.a;
                if (alpha < Mat._AlphaCutoff) discard;
            }

            float3 worldNormal = ApplyNormalMap(Mat._NormalTex, input.texCoord0, input.vNormal, input.vTangent, input.vBitangent);

            FragOut o;
            o.normalOut = EncodeViewNormal(worldNormal);

            // Motion vectors (jitter-free) + packed roughness/metallic (_SurfaceTex G/B).
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

        struct VertexInput
        {
            float3 position : POSITION0;
            float2 uv0 : TEXCOORD0;
            uint vid : SV_VertexID;
        }

        struct Varyings
        {
            float4 position : SV_Position;
            float2 texCoord0 : TEXCOORD0;
        }

        struct Material
        {
            float2 _Tiling;
            float2 _Offset;
            float4 _MainColor;
            float _AlphaCutoff;
            Sampler2D<float4> _MainTex;
        }
        ParameterBlock<Material> Mat;

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            Varyings o;
            o.position = TransformClip(input.position, input.vid);
            o.texCoord0 = input.uv0 * Mat._Tiling + Mat._Offset;
            return o;
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
