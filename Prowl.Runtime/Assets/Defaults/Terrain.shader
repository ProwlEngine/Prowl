Shader "Default/Terrain"
{
    Properties
    {
        _Heightmap ("Heightmap", Texture2D) = "black" {}
        _Splatmap0 ("Splatmap 0 (Layers 0-3)", Texture2D) = "white" {}
        _Splatmap1 ("Splatmap 1 (Layers 4-7)", Texture2D) = "black" {}
        _HolesMap ("Holes Map", Texture2D) = "white" {}
        _HasHoles ("Has Holes", Integer) = 0
        _LayerCount ("Layer Count", Integer) = 4
        _Layer0 ("Layer 0 Albedo", Texture2D) = "white" {}
        _Layer0Normal ("Layer 0 Normal", Texture2D) = "normal" {}
        _Layer0Tiling ("Layer 0 Tiling", Float) = 10.0
        _Layer0Roughness ("Layer 0 Roughness", Float) = 1.0
        _Layer0Metallic ("Layer 0 Metallic", Float) = 0.0
        _Layer1 ("Layer 1 Albedo", Texture2D) = "white" {}
        _Layer1Normal ("Layer 1 Normal", Texture2D) = "normal" {}
        _Layer1Tiling ("Layer 1 Tiling", Float) = 10.0
        _Layer1Roughness ("Layer 1 Roughness", Float) = 1.0
        _Layer1Metallic ("Layer 1 Metallic", Float) = 0.0
        _Layer2 ("Layer 2 Albedo", Texture2D) = "white" {}
        _Layer2Normal ("Layer 2 Normal", Texture2D) = "normal" {}
        _Layer2Tiling ("Layer 2 Tiling", Float) = 10.0
        _Layer2Roughness ("Layer 2 Roughness", Float) = 1.0
        _Layer2Metallic ("Layer 2 Metallic", Float) = 0.0
        _Layer3 ("Layer 3 Albedo", Texture2D) = "white" {}
        _Layer3Normal ("Layer 3 Normal", Texture2D) = "normal" {}
        _Layer3Tiling ("Layer 3 Tiling", Float) = 10.0
        _Layer3Roughness ("Layer 3 Roughness", Float) = 1.0
        _Layer3Metallic ("Layer 3 Metallic", Float) = 0.0
        _Layer4 ("Layer 4 Albedo", Texture2D) = "white" {}
        _Layer4Normal ("Layer 4 Normal", Texture2D) = "normal" {}
        _Layer4Tiling ("Layer 4 Tiling", Float) = 10.0
        _Layer4Roughness ("Layer 4 Roughness", Float) = 1.0
        _Layer4Metallic ("Layer 4 Metallic", Float) = 0.0
        _Layer5 ("Layer 5 Albedo", Texture2D) = "white" {}
        _Layer5Normal ("Layer 5 Normal", Texture2D) = "normal" {}
        _Layer5Tiling ("Layer 5 Tiling", Float) = 10.0
        _Layer5Roughness ("Layer 5 Roughness", Float) = 1.0
        _Layer5Metallic ("Layer 5 Metallic", Float) = 0.0
        _Layer6 ("Layer 6 Albedo", Texture2D) = "white" {}
        _Layer6Normal ("Layer 6 Normal", Texture2D) = "normal" {}
        _Layer6Tiling ("Layer 6 Tiling", Float) = 10.0
        _Layer6Roughness ("Layer 6 Roughness", Float) = 1.0
        _Layer6Metallic ("Layer 6 Metallic", Float) = 0.0
        _Layer7 ("Layer 7 Albedo", Texture2D) = "white" {}
        _Layer7Normal ("Layer 7 Normal", Texture2D) = "normal" {}
        _Layer7Tiling ("Layer 7 Tiling", Float) = 10.0
        _Layer7Roughness ("Layer 7 Roughness", Float) = 1.0
        _Layer7Metallic ("Layer 7 Metallic", Float) = 0.0
        _TerrainSize ("Terrain Size", Float) = 1024.0
        _TerrainHeight ("Terrain Height", Float) = 100.0
        _BrushPosition ("Brush Position", Vector) = (0.0, 0.0, 0.0, 0.0)
        _BrushRadius ("Brush Radius", Float) = 0.0
        _BrushFalloff ("Brush Falloff", Float) = 0.5
        _BrushVisible ("Brush Visible", Float) = 0
    }

    Pass
    {
        Name "Terrain"
        Tags { "RenderOrder" = "Opaque" }
        Cull Back
        ZWrite On

        SLANGPROGRAM
        import ProwlCG;
        import Lighting;
        import VariantAttributes;

        [VariantAxis] extern static const bool TERRAIN_BICUBIC;
        [VariantAxis] extern static const bool GPU_INSTANCING;
        [VariantAxis] extern static const bool TERRAIN_8_LAYERS;

        struct MaterialData
        {
            Sampler2D<float4> _Heightmap;
            Sampler2D<float4> _Splatmap0;
            Sampler2D<float4> _Splatmap1;
            Sampler2D<float4> _HolesMap;
            int _HasHoles;
            int _LayerCount;
            Sampler2D<float4> _Layer0; Sampler2D<float4> _Layer0Normal; float _Layer0Tiling; float _Layer0Roughness; float _Layer0Metallic;
            Sampler2D<float4> _Layer1; Sampler2D<float4> _Layer1Normal; float _Layer1Tiling; float _Layer1Roughness; float _Layer1Metallic;
            Sampler2D<float4> _Layer2; Sampler2D<float4> _Layer2Normal; float _Layer2Tiling; float _Layer2Roughness; float _Layer2Metallic;
            Sampler2D<float4> _Layer3; Sampler2D<float4> _Layer3Normal; float _Layer3Tiling; float _Layer3Roughness; float _Layer3Metallic;
            Sampler2D<float4> _Layer4; Sampler2D<float4> _Layer4Normal; float _Layer4Tiling; float _Layer4Roughness; float _Layer4Metallic;
            Sampler2D<float4> _Layer5; Sampler2D<float4> _Layer5Normal; float _Layer5Tiling; float _Layer5Roughness; float _Layer5Metallic;
            Sampler2D<float4> _Layer6; Sampler2D<float4> _Layer6Normal; float _Layer6Tiling; float _Layer6Roughness; float _Layer6Metallic;
            Sampler2D<float4> _Layer7; Sampler2D<float4> _Layer7Normal; float _Layer7Tiling; float _Layer7Roughness; float _Layer7Metallic;
            float _TerrainSize;
            float _TerrainHeight;
            float4x4 _TerrainWorldToLocal;
            float4x4 _TerrainLocalToWorld;
            float2 _BrushPosition;
            float _BrushRadius;
            float _BrushFalloff;
            float _BrushVisible;
        }
        ParameterBlock<MaterialData> Mat;

        struct VertexInput
        {
            float3 position : POSITION0;
            float2 uv : TEXCOORD0;
            float4 instRow0 : TEXCOORD8;
            float4 instRow1 : TEXCOORD9;
            float4 instRow2 : TEXCOORD10;
            float4 instRow3 : TEXCOORD11;
        }
        struct Varyings
        {
            float4 position : SV_Position;
            float2 texCoord0 : TEXCOORD0;
            float3 worldPos : TEXCOORD1;
            float3 worldNormal : TEXCOORD2;
        }

        float2 hmSampleUV(float2 uv)
        {
            uint w, h; Mat._Heightmap.GetDimensions(w, h);
            float2 s = float2(w, h);
            return uv * (s - 1.0) / s + 0.5 / s;
        }

        float sampleHeightBicubic(float2 uv)
        {
            uint w, h; Mat._Heightmap.GetDimensions(w, h);
            float2 texSize = float2(w, h);
            float2 invTexSize = 1.0 / texSize;
            float2 coord = uv * texSize - 0.5;
            float2 f = frac(coord);
            coord -= f;
            float2 f2 = f * f; float2 f3 = f2 * f;
            float2 w0 = -0.5 * f3 + f2 - 0.5 * f;
            float2 w1 = 1.5 * f3 - 2.5 * f2 + 1.0;
            float2 w2 = -1.5 * f3 + 2.0 * f2 + 0.5 * f;
            float2 w3 = 0.5 * f3 - 0.5 * f2;
            float2 s0 = w0 + w1; float2 s1 = w2 + w3;
            float2 f0 = w1 / s0; float2 f1 = w3 / s1;
            float2 t0 = (coord - 0.5 + f0) * invTexSize + 0.5 * invTexSize;
            float2 t1 = (coord + 1.5 + f1) * invTexSize + 0.5 * invTexSize;
            float h00 = Mat._Heightmap.Sample(float2(t0.x, t0.y)).r;
            float h10 = Mat._Heightmap.Sample(float2(t1.x, t0.y)).r;
            float h01 = Mat._Heightmap.Sample(float2(t0.x, t1.y)).r;
            float h11 = Mat._Heightmap.Sample(float2(t1.x, t1.y)).r;
            float row0 = lerp(h00, h10, s1.x / (s0.x + s1.x));
            float row1 = lerp(h01, h11, s1.x / (s0.x + s1.x));
            return lerp(row0, row1, s1.y / (s0.y + s1.y));
        }

        float sampleHeight(float2 uv)
        {
            static if (TERRAIN_BICUBIC)
                return sampleHeightBicubic(uv) * Mat._TerrainHeight;
            else
                return Mat._Heightmap.Sample(hmSampleUV(uv)).r * Mat._TerrainHeight;
        }

        float4x4 InstanceModel(VertexInput input)
        {
            return transpose(float4x4(input.instRow0, input.instRow1, input.instRow2, input.instRow3));
        }

        float3 unpackNormal(float4 packednormal)
        {
            float3 normal;
            normal.xy = packednormal.rg * 2.0 - 1.0;
            normal.z = sqrt(max(0.0, 1.0 - dot(normal.xy, normal.xy)));
            return normal;
        }

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            Varyings output;
            static if (GPU_INSTANCING)
            {
                float4x4 instanceModel = InstanceModel(input);
                float4 worldPos4 = mul(instanceModel, float4(input.position, 1.0));
                float3 terrainLocal = mul(Mat._TerrainWorldToLocal, worldPos4).xyz;
                float2 terrainUV = terrainLocal.xz / Mat._TerrainSize;
                output.texCoord0 = terrainUV;

                float height = sampleHeight(terrainUV);
                float3 displacedLocal = float3(terrainLocal.x, height, terrainLocal.z);
                float3 worldPosition = mul(Mat._TerrainLocalToWorld, float4(displacedLocal, 1.0)).xyz;

                uint hmW, hmH; Mat._Heightmap.GetDimensions(hmW, hmH);
                float hmSize = float(hmW);
                float vertStep = hmSize > 1.0 ? (1.0 / (hmSize - 1.0)) : 0.001;

                float hR = sampleHeight(terrainUV + float2(vertStep, 0.0));
                float hL = sampleHeight(terrainUV - float2(vertStep, 0.0));
                float hU = sampleHeight(terrainUV + float2(0.0, vertStep));
                float hD = sampleHeight(terrainUV - float2(0.0, vertStep));

                float wStep = vertStep * Mat._TerrainSize;
                float slopeX = (hR - hL) / (wStep * 2.0);
                float slopeZ = (hU - hD) / (wStep * 2.0);

                float3 localNormal = normalize(float3(-slopeX, 1.0, -slopeZ));
                output.worldNormal = normalize(mul(Mat._TerrainLocalToWorld, float4(localNormal, 0.0)).xyz);

                output.worldPos = worldPosition;
                output.position = mul(Frame.prowl_MatVP, float4(worldPosition, 1.0));
            }
            else
            {
                output.position = mul(Object.mvp, float4(input.position, 1.0));
                output.texCoord0 = input.uv;
                output.worldPos = mul(Object.prowl_ObjectToWorld, float4(input.position, 1.0)).xyz;
                output.worldNormal = normalize(mul(Object.prowl_ObjectToWorld, float4(0.0, 1.0, 0.0, 0.0)).xyz);
            }
            return output;
        }

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
            if (Mat._HasHoles > 0 && Mat._HolesMap.Sample(input.texCoord0).r < 0.5)
                discard;

            float4 w0 = Mat._Splatmap0.Sample(input.texCoord0);

            float3 albedo = float3(0.0);
            float3 blendedNormalTS = float3(0.0);
            float roughness = 0.0;
            float metallic = 0.0;
            float totalWeight = 0.0;

            if (w0.r > 0.001) {
                float2 uv = input.texCoord0 * Mat._Layer0Tiling;
                albedo += Mat._Layer0.Sample(uv).rgb * w0.r;
                blendedNormalTS += unpackNormal(Mat._Layer0Normal.Sample(uv)) * w0.r;
                roughness += Mat._Layer0Roughness * w0.r;
                metallic += Mat._Layer0Metallic * w0.r;
                totalWeight += w0.r;
            }
            if (w0.g > 0.001) {
                float2 uv = input.texCoord0 * Mat._Layer1Tiling;
                albedo += Mat._Layer1.Sample(uv).rgb * w0.g;
                blendedNormalTS += unpackNormal(Mat._Layer1Normal.Sample(uv)) * w0.g;
                roughness += Mat._Layer1Roughness * w0.g;
                metallic += Mat._Layer1Metallic * w0.g;
                totalWeight += w0.g;
            }
            if (w0.b > 0.001) {
                float2 uv = input.texCoord0 * Mat._Layer2Tiling;
                albedo += Mat._Layer2.Sample(uv).rgb * w0.b;
                blendedNormalTS += unpackNormal(Mat._Layer2Normal.Sample(uv)) * w0.b;
                roughness += Mat._Layer2Roughness * w0.b;
                metallic += Mat._Layer2Metallic * w0.b;
                totalWeight += w0.b;
            }
            if (w0.a > 0.001) {
                float2 uv = input.texCoord0 * Mat._Layer3Tiling;
                albedo += Mat._Layer3.Sample(uv).rgb * w0.a;
                blendedNormalTS += unpackNormal(Mat._Layer3Normal.Sample(uv)) * w0.a;
                roughness += Mat._Layer3Roughness * w0.a;
                metallic += Mat._Layer3Metallic * w0.a;
                totalWeight += w0.a;
            }

            static if (TERRAIN_8_LAYERS)
            {
                float4 w1 = Mat._Splatmap1.Sample(input.texCoord0);

                if (w1.r > 0.001) {
                    float2 uv = input.texCoord0 * Mat._Layer4Tiling;
                    albedo += Mat._Layer4.Sample(uv).rgb * w1.r;
                    blendedNormalTS += unpackNormal(Mat._Layer4Normal.Sample(uv)) * w1.r;
                    roughness += Mat._Layer4Roughness * w1.r;
                    metallic += Mat._Layer4Metallic * w1.r;
                    totalWeight += w1.r;
                }
                if (w1.g > 0.001) {
                    float2 uv = input.texCoord0 * Mat._Layer5Tiling;
                    albedo += Mat._Layer5.Sample(uv).rgb * w1.g;
                    blendedNormalTS += unpackNormal(Mat._Layer5Normal.Sample(uv)) * w1.g;
                    roughness += Mat._Layer5Roughness * w1.g;
                    metallic += Mat._Layer5Metallic * w1.g;
                    totalWeight += w1.g;
                }
                if (w1.b > 0.001) {
                    float2 uv = input.texCoord0 * Mat._Layer6Tiling;
                    albedo += Mat._Layer6.Sample(uv).rgb * w1.b;
                    blendedNormalTS += unpackNormal(Mat._Layer6Normal.Sample(uv)) * w1.b;
                    roughness += Mat._Layer6Roughness * w1.b;
                    metallic += Mat._Layer6Metallic * w1.b;
                    totalWeight += w1.b;
                }
                if (w1.a > 0.001) {
                    float2 uv = input.texCoord0 * Mat._Layer7Tiling;
                    albedo += Mat._Layer7.Sample(uv).rgb * w1.a;
                    blendedNormalTS += unpackNormal(Mat._Layer7Normal.Sample(uv)) * w1.a;
                    roughness += Mat._Layer7Roughness * w1.a;
                    metallic += Mat._Layer7Metallic * w1.a;
                    totalWeight += w1.a;
                }
            }

            if (totalWeight > 0.0) {
                albedo /= totalWeight;
                roughness /= totalWeight;
                metallic /= totalWeight;
            }

            float3 baseColor = gammaToLinearSpace(albedo);
            blendedNormalTS = normalize(blendedNormalTS);

            float3 N = normalize(input.worldNormal);
            float3 T = normalize(cross(N, float3(0.0, 0.0, 1.0)));
            float3 B = cross(T, N);
            float3 finalWorldNormal = normalize(mul(blendedNormalTS, float3x3(T, B, N)));

            if (Mat._BrushVisible > 0.5 && Mat._BrushRadius > 0.0)
            {
                float dist = length(input.texCoord0 - Mat._BrushPosition);
                if (dist < Mat._BrushRadius)
                {
                    float t = dist / Mat._BrushRadius;
                    float alpha = 1.0 - smoothstep(1.0 - Mat._BrushFalloff, 1.0, t);
                    baseColor = lerp(baseColor, float3(0.2, 0.8, 0.6), alpha * 0.3);
                }
            }

            float3 viewDir = normalize(Frame._WorldSpaceCameraPos.xyz - input.worldPos);
            float3 lighting = CalculateForwardLighting(input.worldPos, finalWorldNormal, viewDir,
                baseColor, metallic, roughness, 1.0, input.position.xy);
            float3 ambientLight = CalculateAmbient(finalWorldNormal) * Lights._AmbientStrength;
            float3 diffuseColor = baseColor * (1.0 - metallic);
            float3 ambientDiffuse = ambientLight * diffuseColor;

            float3 F0 = lerp(float3(0.04), baseColor, metallic);
            float NdotV = max(dot(finalWorldNormal, viewDir), 0.0);
            float3 F = FresnelSchlickRoughness(NdotV, F0, roughness);
            float specOcclusion = 1.0 - roughness * roughness;
            float3 ambientSpecular = ambientLight * F * lerp(specOcclusion, 1.0, 0.25);

            float3 color = ambientDiffuse + ambientSpecular + lighting;
            color = ApplyFog(color, input.worldPos);

            return float4(color, 1.0);
        }
        ENDSLANG
    }

    Pass
    {
        Name "TerrainShadow"
        Tags { "LightMode" = "ShadowCaster" }
        Cull Back

        SLANGPROGRAM
        import ProwlCG;
        import VariantAttributes;

        extern static const bool TERRAIN_BICUBIC;
        extern static const bool GPU_INSTANCING;

        struct MaterialData
        {
            Sampler2D<float4> _Heightmap;
            Sampler2D<float4> _HolesMap;
            int _HasHoles;
            float _TerrainSize;
            float _TerrainHeight;
            float4x4 _TerrainWorldToLocal;
            float4x4 _TerrainLocalToWorld;
        }
        ParameterBlock<MaterialData> Mat;

        struct VertexInput
        {
            float3 position : POSITION0;
            float2 uv : TEXCOORD0;
            float4 instRow0 : TEXCOORD8;
            float4 instRow1 : TEXCOORD9;
            float4 instRow2 : TEXCOORD10;
            float4 instRow3 : TEXCOORD11;
        }
        struct Varyings
        {
            float4 position : SV_Position;
            float3 worldPos : TEXCOORD0;
            float2 texCoord0 : TEXCOORD1;
        }

        float2 hmSampleUV(float2 uv)
        {
            uint w, h; Mat._Heightmap.GetDimensions(w, h);
            float2 s = float2(w, h);
            return uv * (s - 1.0) / s + 0.5 / s;
        }

        float sampleHeightBicubic(float2 uv)
        {
            uint w, h; Mat._Heightmap.GetDimensions(w, h);
            float2 texSize = float2(w, h);
            float2 invTexSize = 1.0 / texSize;
            float2 coord = uv * texSize - 0.5;
            float2 f = frac(coord);
            coord -= f;
            float2 f2 = f * f; float2 f3 = f2 * f;
            float2 w0 = -0.5*f3 + f2 - 0.5*f;
            float2 w1 = 1.5*f3 - 2.5*f2 + 1.0;
            float2 w2 = -1.5*f3 + 2.0*f2 + 0.5*f;
            float2 w3 = 0.5*f3 - 0.5*f2;
            float2 s0 = w0+w1; float2 s1 = w2+w3;
            float2 f0 = w1/s0; float2 f1 = w3/s1;
            float2 t0 = (coord-0.5+f0)*invTexSize + 0.5*invTexSize;
            float2 t1 = (coord+1.5+f1)*invTexSize + 0.5*invTexSize;
            float h00 = Mat._Heightmap.Sample(float2(t0.x, t0.y)).r;
            float h10 = Mat._Heightmap.Sample(float2(t1.x, t0.y)).r;
            float h01 = Mat._Heightmap.Sample(float2(t0.x, t1.y)).r;
            float h11 = Mat._Heightmap.Sample(float2(t1.x, t1.y)).r;
            float row0 = lerp(h00, h10, s1.x/(s0.x+s1.x));
            float row1 = lerp(h01, h11, s1.x/(s0.x+s1.x));
            return lerp(row0, row1, s1.y/(s0.y+s1.y));
        }

        float sampleHeight(float2 uv)
        {
            static if (TERRAIN_BICUBIC)
                return sampleHeightBicubic(uv) * Mat._TerrainHeight;
            else
                return Mat._Heightmap.Sample(hmSampleUV(uv)).r * Mat._TerrainHeight;
        }

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            Varyings output;
            static if (GPU_INSTANCING)
            {
                float4x4 instanceModel = transpose(float4x4(input.instRow0, input.instRow1, input.instRow2, input.instRow3));
                float4 worldPos4 = mul(instanceModel, float4(input.position, 1.0));
                float3 terrainLocal = mul(Mat._TerrainWorldToLocal, worldPos4).xyz;
                float2 terrainUV = terrainLocal.xz / Mat._TerrainSize;
                output.texCoord0 = terrainUV;

                float height = sampleHeight(terrainUV);
                float3 displacedLocal = float3(terrainLocal.x, height, terrainLocal.z);
                float3 worldPosition = mul(Mat._TerrainLocalToWorld, float4(displacedLocal, 1.0)).xyz;

                output.worldPos = worldPosition;
                output.position = mul(Frame.prowl_MatVP, float4(worldPosition, 1.0));
            }
            else
            {
                output.position = mul(Object.mvp, float4(input.position, 1.0));
                output.worldPos = mul(Object.prowl_ObjectToWorld, float4(input.position, 1.0)).xyz;
                output.texCoord0 = input.uv;
            }
            return output;
        }

        [shader("fragment")]
        float Fragment(Varyings input) : SV_Depth
        {
            if (Mat._HasHoles > 0 && Mat._HolesMap.Sample(input.texCoord0).r < 0.5)
                discard;
            return input.position.z;
        }
        ENDSLANG
    }

    Pass
    {
        Name "TerrainPrepass"
        Tags { "LightMode" = "Prepass" }
        Cull Back
        ZWrite On

        SLANGPROGRAM
        import ProwlCG;
        import VariantAttributes;

        extern static const bool TERRAIN_BICUBIC;
        extern static const bool GPU_INSTANCING;

        struct MaterialData
        {
            Sampler2D<float4> _Heightmap;
            Sampler2D<float4> _HolesMap;
            int _HasHoles;
            float _TerrainSize;
            float _TerrainHeight;
            float4x4 _TerrainWorldToLocal;
            float4x4 _TerrainLocalToWorld;
        }
        ParameterBlock<MaterialData> Mat;

        struct VertexInput
        {
            float3 position : POSITION0;
            float2 uv : TEXCOORD0;
            float4 instRow0 : TEXCOORD8;
            float4 instRow1 : TEXCOORD9;
            float4 instRow2 : TEXCOORD10;
            float4 instRow3 : TEXCOORD11;
        }
        struct Varyings
        {
            float4 position : SV_Position;
            float3 worldNormal : TEXCOORD0;
            float2 texCoord0 : TEXCOORD1;
            float4 vCurrClipNJ : TEXCOORD2;
            float4 vPrevClip : TEXCOORD3;
        }
        struct FragOutput
        {
            float4 normalOut : SV_Target0;
            float4 motionRM : SV_Target1;
        }

        float2 hmSampleUV(float2 uv)
        {
            uint w, h; Mat._Heightmap.GetDimensions(w, h);
            float2 s = float2(w, h);
            return uv * (s - 1.0) / s + 0.5 / s;
        }

        float sampleHeightBicubic(float2 uv)
        {
            uint w, h; Mat._Heightmap.GetDimensions(w, h);
            float2 texSize = float2(w, h);
            float2 invTexSize = 1.0 / texSize;
            float2 coord = uv * texSize - 0.5;
            float2 f = frac(coord);
            coord -= f;
            float2 f2 = f * f; float2 f3 = f2 * f;
            float2 w0 = -0.5*f3 + f2 - 0.5*f;
            float2 w1 = 1.5*f3 - 2.5*f2 + 1.0;
            float2 w2 = -1.5*f3 + 2.0*f2 + 0.5*f;
            float2 w3 = 0.5*f3 - 0.5*f2;
            float2 s0 = w0+w1; float2 s1 = w2+w3;
            float2 f0 = w1/s0; float2 f1 = w3/s1;
            float2 t0 = (coord-0.5+f0)*invTexSize + 0.5*invTexSize;
            float2 t1 = (coord+1.5+f1)*invTexSize + 0.5*invTexSize;
            float h00 = Mat._Heightmap.Sample(float2(t0.x, t0.y)).r;
            float h10 = Mat._Heightmap.Sample(float2(t1.x, t0.y)).r;
            float h01 = Mat._Heightmap.Sample(float2(t0.x, t1.y)).r;
            float h11 = Mat._Heightmap.Sample(float2(t1.x, t1.y)).r;
            float row0 = lerp(h00, h10, s1.x/(s0.x+s1.x));
            float row1 = lerp(h01, h11, s1.x/(s0.x+s1.x));
            return lerp(row0, row1, s1.y/(s0.y+s1.y));
        }

        float sampleHeight(float2 uv)
        {
            static if (TERRAIN_BICUBIC)
                return sampleHeightBicubic(uv) * Mat._TerrainHeight;
            else
                return Mat._Heightmap.Sample(hmSampleUV(uv)).r * Mat._TerrainHeight;
        }

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            Varyings output;
            static if (GPU_INSTANCING)
            {
                float4x4 instanceModel = transpose(float4x4(input.instRow0, input.instRow1, input.instRow2, input.instRow3));
                float4 worldPos4 = mul(instanceModel, float4(input.position, 1.0));
                float3 terrainLocal = mul(Mat._TerrainWorldToLocal, worldPos4).xyz;
                float2 terrainUV = terrainLocal.xz / Mat._TerrainSize;
                output.texCoord0 = terrainUV;

                float height = sampleHeight(terrainUV);
                float3 displacedLocal = float3(terrainLocal.x, height, terrainLocal.z);
                float3 worldPosition = mul(Mat._TerrainLocalToWorld, float4(displacedLocal, 1.0)).xyz;

                uint hmW, hmH; Mat._Heightmap.GetDimensions(hmW, hmH);
                float hmSize = float(hmW);
                float vertStep = hmSize > 1.0 ? (1.0 / (hmSize - 1.0)) : 0.001;
                float hR = sampleHeight(terrainUV + float2(vertStep, 0.0));
                float hL = sampleHeight(terrainUV - float2(vertStep, 0.0));
                float hU = sampleHeight(terrainUV + float2(0.0, vertStep));
                float hD = sampleHeight(terrainUV - float2(0.0, vertStep));
                float wStep = vertStep * Mat._TerrainSize;
                float slopeX = (hR - hL) / (wStep * 2.0);
                float slopeZ = (hU - hD) / (wStep * 2.0);
                float3 localNormal = normalize(float3(-slopeX, 1.0, -slopeZ));
                output.worldNormal = normalize(mul(Mat._TerrainLocalToWorld, float4(localNormal, 0.0)).xyz);

                output.position = mul(Frame.prowl_MatVP, float4(worldPosition, 1.0));

                output.vCurrClipNJ = mul(Frame.prowl_MatVP_NonJittered, float4(worldPosition, 1.0));
                output.vPrevClip = mul(Frame.prowl_PrevViewProj, float4(worldPosition, 1.0));
            }
            else
            {
                output.position = mul(Object.mvp, float4(input.position, 1.0));
                output.worldNormal = normalize(mul(Object.prowl_ObjectToWorld, float4(0.0, 1.0, 0.0, 0.0)).xyz);
                output.texCoord0 = input.uv;

                float4 wp = mul(Object.prowl_ObjectToWorld, float4(input.position, 1.0));
                output.vCurrClipNJ = mul(Frame.prowl_MatVP_NonJittered, wp);
                output.vPrevClip = mul(Frame.prowl_PrevViewProj, mul(Object.prowl_PrevObjectToWorld, float4(input.position, 1.0)));
            }
            return output;
        }

        [shader("fragment")]
        FragOutput Fragment(Varyings input)
        {
            if (Mat._HasHoles > 0 && Mat._HolesMap.Sample(input.texCoord0).r < 0.5)
                discard;

            FragOutput o;
            o.normalOut = EncodeViewNormal(input.worldNormal);

            float2 currNDC = (input.vCurrClipNJ.xy / input.vCurrClipNJ.w) * 0.5 + 0.5;
            float2 prevNDC = (input.vPrevClip.xy / input.vPrevClip.w) * 0.5 + 0.5;
            o.motionRM = float4(currNDC - prevNDC, 1.0, 0.0);
            return o;
        }
        ENDSLANG
    }
}
