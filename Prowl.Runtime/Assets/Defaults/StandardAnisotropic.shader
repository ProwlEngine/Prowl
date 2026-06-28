Shader "Default/StandardAnisotropic"
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
        _AlphaCutoff("Alpha Cutoff", Float) = 0.5
        _Anisotropy("Anisotropy", Float) = 0.5
        _AnisoDirectionMap("Anisotropy Direction (RG)", Texture2D) = "normal" {}
    }

    Pass
    {
        Name "StandardAniso"
        Tags { "RenderOrder" = "Opaque" }
        Cull Back

        SLANGPROGRAM

        import ProwlCG;
        import VertexAttributes;
        import Lighting;

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
            float _AlphaCutoff;
            float _Anisotropy;
            Sampler2D<float4> _MainTex;
            Sampler2D<float4> _NormalTex;
            Sampler2D<float4> _SurfaceTex;
            Sampler2D<float4> _EmissionTex;
            Sampler2D<float4> _AnisoDirectionMap;
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
            // Albedo
            float4 albedo = Mat._MainTex.Sample(input.texCoord0) * input.vColor * Mat._MainColor;
            float3 baseColor = gammaToLinearSpace(albedo.rgb);

            // Alpha cutout
            if (albedo.a < Mat._AlphaCutoff)
                discard;

            // Normal mapping
            float3 worldNormal = ApplyNormalMap(Mat._NormalTex, input.texCoord0, input.vNormal, input.vTangent, input.vBitangent);

            // Surface: R = AO, G = Roughness, B = Metallic
            float4 surface = Mat._SurfaceTex.Sample(input.texCoord0);
            float ao = 1.0 - surface.r;
            float roughness = surface.g;
            float metallic = surface.b;

            // Tangent frame for anisotropic lighting
            float3 T = normalize(input.vTangent);
            float3 B = normalize(input.vBitangent);
            float3 N = normalize(worldNormal);

            // Optionally rotate tangent direction by aniso direction map
            float2 anisoDir = Mat._AnisoDirectionMap.Sample(input.texCoord0).rg * 2.0 - 1.0;
            float anisoDirLen = length(anisoDir);
            float3 anisoTangent, anisoBitangent;
            if (anisoDirLen > 0.01)
            {
                anisoDir /= anisoDirLen;
                anisoTangent = normalize(T * anisoDir.x + B * anisoDir.y);
                anisoBitangent = normalize(cross(N, anisoTangent));
            }
            else
            {
                anisoTangent = T;
                anisoBitangent = B;
            }

            // Emission
            float3 emission = Mat._EmissionTex.Sample(input.texCoord0).rgb * Mat._EmissionIntensity;

            // Anisotropic PBR lighting
            float3 viewDir = normalize(Frame._WorldSpaceCameraPos.xyz - input.worldPos);
            float3 lighting = CalculateForwardLightingAniso(input.worldPos, N, viewDir,
                anisoTangent, anisoBitangent,
                baseColor, metallic, roughness, Mat._Anisotropy, ao, input.position.xy);

            // Ambient + fog (energy conserved for metals)
            float3 ambientLight = CalculateAmbient(N) * ao * Light._AmbientStrength;
            float3 diffuseColor = baseColor * (1.0 - metallic);
            float3 ambientDiffuse = ambientLight * diffuseColor;

            float3 F0 = lerp(float3(0.04), baseColor, metallic);
            float NdotV = max(dot(N, viewDir), 0.0);
            float3 F = FresnelSchlickRoughness(NdotV, F0, roughness);
            float specOcclusion = 1.0 - roughness * roughness;
            float3 ambientSpecular = ambientLight * F * lerp(specOcclusion, 1.0, 0.25);

            float3 ambient = ambientDiffuse + ambientSpecular;
            float3 color = ApplyFog(ambient + lighting + emission, input.worldPos);

            return float4(color, 1.0);
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
            o.vNormal = TransformDirection(input.normal);
            o.vTangent = float3(0.0);
            o.vBitangent = float3(0.0);
#ifdef HAS_TANGENTS
            o.vTangent = TransformDirection(input.tangent.xyz);
            o.vBitangent = cross(o.vTangent, o.vNormal) * input.tangent.w;
#endif
            o.texCoord0 = input.uv0 * Mat._Tiling + Mat._Offset;

            float4 worldPos = mul(GetModelMatrix(), float4(input.position, 1.0));
            o.vCurrClipNJ = mul(Frame.prowl_MatVP_NonJittered, worldPos);
            float4 prevWorldPos = mul(Object.prowl_PrevObjectToWorld, float4(input.position, 1.0));
            o.vPrevClip = mul(Frame.prowl_PrevViewProj, prevWorldPos);
            return o;
        }

        [shader("fragment")]
        FragOut Fragment(Varyings input)
        {
            if (Mat._MainTex.Sample(input.texCoord0).a < Mat._AlphaCutoff)
                discard;

            float3 worldNormal = ApplyNormalMap(Mat._NormalTex, input.texCoord0, input.vNormal, input.vTangent, input.vBitangent);

            FragOut o;
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
        Name "ShadowCaster"
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
            if (Mat._MainTex.Sample(input.texCoord0).a < Mat._AlphaCutoff)
                discard;
            return input.position.z;
        }

        ENDSLANG
    }
}
