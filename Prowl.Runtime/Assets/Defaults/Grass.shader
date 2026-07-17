Shader "Default/Grass"
{
    Properties
    {
        _MainTex ("Grass Texture", Texture2D) = "white" {}
        _AlphaCutoff ("Alpha Cutoff", Float) = 0.5
        _WindStrength ("Wind Strength", Float) = 0.3
        _WindSpeed ("Wind Speed", Float) = 1.5
        _Billboard ("Billboard", Float) = 1.0
        _AlignToNormal ("Align To Normal", Float) = 0.0
        _Translucency ("Translucency", Float) = 15.0
        _ScatterPower ("Scattering Power", Float) = 0.0
        _ScatterDistortion ("Scattering Distortion", Float) = 0.5
        _ScatterScale ("Scattering Scale", Float) = 1.0
        _GrassDistance ("Grass Max Distance", Float) = 150.0
        _GrassFadeStart ("Grass Fade Start (world units)", Float) = 90.0
    }

    Pass
    {
        Name "Grass"
        Tags { "RenderOrder" = "Opaque" }
        Cull Off
        ZWrite On

        SLANGPROGRAM
        import ProwlCG;
        import Lighting;
        import VariantAttributes;

        [VariantAxis]
        extern static const bool GPU_INSTANCING;

        struct MaterialData
        {
            Sampler2D<float4> _MainTex;
            float _AlphaCutoff;
            float _WindStrength;
            float _WindSpeed;
            float _Billboard;
            float _AlignToNormal;
            float _Translucency;
            float _ScatterPower;
            float _ScatterDistortion;
            float _ScatterScale;
            float _GrassDistance;
            float _GrassFadeStart;
            float3 _TerrainUp;
            Sampler2D<float4> _Heightmap;
            float _TerrainSize;
            float _TerrainHeight;
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
            float4 instColor : TEXCOORD12;
            float4 instCustom : TEXCOORD13;
        }
        struct Varyings
        {
            float4 position : SV_Position;
            float2 texCoord0 : TEXCOORD0;
            float4 vColor : COLOR0;
            float3 worldPos : TEXCOORD1;
            float3 vNormal : TEXCOORD2;
        }

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            Varyings output;
            static if (GPU_INSTANCING)
            {
                float4x4 terrainToWorld = Mat._TerrainLocalToWorld;

                float3 localPosition = input.instRow3.xyz;
                float3 bladePosition = mul(terrainToWorld, float4(localPosition, 1.0)).xyz;
                float scaleX = length(input.instRow0.xyz);
                float scaleY = length(input.instRow1.xyz);

                float3 camFlat = float3(Frame._WorldSpaceCameraPos.x, bladePosition.y, Frame._WorldSpaceCameraPos.z);
                float camDist = length(bladePosition - camFlat);
                float fadeRange = max(0.0001, Mat._GrassDistance - Mat._GrassFadeStart);
                float fade = 1.0 - clamp((camDist - Mat._GrassFadeStart) / fadeRange, 0.0, 1.0);
                scaleX *= fade;
                scaleY *= fade;

                float2 terrainUV = localPosition.xz / Mat._TerrainSize;
                uint hmW, hmH;
                Mat._Heightmap.GetDimensions(hmW, hmH);
                float2 hmSize2 = float2(hmW, hmH);
                float hmSize = hmSize2.x;
                float vertStep = hmSize > 1.0 ? (1.0 / (hmSize - 1.0)) : 0.001;

                float2 baseUV = terrainUV * (hmSize2 - 1.0) / hmSize2 + 0.5 / hmSize2;
                float2 stepUV = float2(vertStep * (hmSize - 1.0) / hmSize, 0.0);
                float2 stepVV = float2(0.0, vertStep * (hmSize - 1.0) / hmSize);
                float hR = Mat._Heightmap.Sample(baseUV + stepUV).r * Mat._TerrainHeight;
                float hL = Mat._Heightmap.Sample(baseUV - stepUV).r * Mat._TerrainHeight;
                float hU = Mat._Heightmap.Sample(baseUV + stepVV).r * Mat._TerrainHeight;
                float hD = Mat._Heightmap.Sample(baseUV - stepVV).r * Mat._TerrainHeight;

                float wStep = vertStep * Mat._TerrainSize;
                float3 localNormal = normalize(float3(-(hR - hL) / (wStep * 2.0), 1.0, -(hU - hD) / (wStep * 2.0)));
                float3 terrainNormal = normalize(mul(terrainToWorld, float4(localNormal, 0.0)).xyz);

                float3 up = (Mat._AlignToNormal > 0.5) ? terrainNormal : Mat._TerrainUp;

                float3 quadRight;
                float3 localOffset;
                if (Mat._Billboard > 0.5)
                {
                    float3 cameraRight = Frame.prowl_MatV[0].xyz;
                    cameraRight = normalize(cameraRight - up * dot(cameraRight, up));
                    quadRight = cameraRight;
                    localOffset = cameraRight * input.position.x * scaleX + up * input.position.y * scaleY;
                }
                else
                {
                    float3 right = normalize(mul(terrainToWorld, float4(normalize(input.instRow0.xyz), 0.0)).xyz);
                    right = normalize(right - up * dot(right, up));
                    quadRight = right;
                    localOffset = right * input.position.x * scaleX + up * input.position.y * scaleY;
                }

                float3 quadNormal = normalize(cross(up, quadRight));

                float windPhase = input.instCustom.x;
                float bendFactor = input.instCustom.y;
                float windAmount = max(0.0, input.position.y);
                float wind = sin(Frame._Time.y * Mat._WindSpeed + bladePosition.x * 0.7 + bladePosition.z * 0.4 + windPhase) * Mat._WindStrength * bendFactor;
                localOffset.x += wind * windAmount;
                localOffset.z += wind * windAmount * 0.3;

                float3 worldPosition = bladePosition + localOffset;
                worldPosition += up * 0.01 * scaleY;
                output.worldPos = worldPosition;
                output.vNormal = quadNormal;
                output.vColor = input.instColor;

                output.position = mul(Frame.prowl_MatVP, float4(worldPosition, 1.0));
                output.texCoord0 = input.uv;
            }
            else
            {
                output.position = mul(Object.mvp, float4(input.position, 1.0));
                output.texCoord0 = input.uv;
                output.worldPos = mul(Object.prowl_ObjectToWorld, float4(input.position, 1.0)).xyz;
                output.vNormal = float3(0.0, 1.0, 0.0);
                output.vColor = float4(1.0);
            }
            return output;
        }

        [shader("fragment")]
        float4 Fragment(Varyings input, bool isFront : SV_IsFrontFace) : SV_Target
        {
            float4 texColor = Mat._MainTex.Sample(input.texCoord0);
            float4 finalColor = texColor * input.vColor;

            if (finalColor.a < Mat._AlphaCutoff)
                discard;

            float3 baseColor = gammaToLinearSpace(finalColor.rgb);
            float3 normal = normalize(input.vNormal) * (isFront ? 1.0 : -1.0);

            float3 viewDir = normalize(Frame._WorldSpaceCameraPos.xyz - input.worldPos);
            float3 lighting = CalculateForwardLighting(input.worldPos, normal, viewDir,
                baseColor, 0.0, 0.9, 1.0,
                Mat._Translucency, Mat._ScatterPower,
                Mat._ScatterDistortion, Mat._ScatterScale, input.position.xy);
            float3 ambient = CalculateAmbient(normal) * baseColor * Lights._AmbientStrength;

            float3 color = ambient + lighting;
            color = ApplyFog(color, input.worldPos);

            return float4(color, 1.0);
        }
        ENDSLANG
    }

    Pass
    {
        Name "GrassPrepass"
        Tags { "LightMode" = "Prepass" }
        Cull Off
        ZWrite On

        SLANGPROGRAM
        import ProwlCG;
        import VariantAttributes;

        extern static const bool GPU_INSTANCING;

        struct MaterialData
        {
            Sampler2D<float4> _MainTex;
            float _AlphaCutoff;
            float _WindStrength;
            float _WindSpeed;
            float _Billboard;
            float _AlignToNormal;
            float _GrassDistance;
            float _GrassFadeStart;
            float3 _TerrainUp;
            Sampler2D<float4> _Heightmap;
            float _TerrainSize;
            float _TerrainHeight;
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
            float4 instCustom : TEXCOORD13;
        }
        struct Varyings
        {
            float4 position : SV_Position;
            float3 vNormal : TEXCOORD0;
            float2 texCoord0 : TEXCOORD1;
        }
        struct FragOutput
        {
            float4 normalOut : SV_Target0;
            float4 motionRM : SV_Target1;
        }

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            Varyings output;
            static if (GPU_INSTANCING)
            {
                float4x4 terrainToWorld = Mat._TerrainLocalToWorld;
                float3 localPosition = input.instRow3.xyz;
                float3 bladePosition = mul(terrainToWorld, float4(localPosition, 1.0)).xyz;
                float scaleX = length(input.instRow0.xyz);
                float scaleY = length(input.instRow1.xyz);

                float3 camFlat = float3(Frame._WorldSpaceCameraPos.x, bladePosition.y, Frame._WorldSpaceCameraPos.z);
                float camDist = length(bladePosition - camFlat);
                float fadeRange = max(0.0001, Mat._GrassDistance - Mat._GrassFadeStart);
                float fade = 1.0 - clamp((camDist - Mat._GrassFadeStart) / fadeRange, 0.0, 1.0);
                scaleX *= fade;
                scaleY *= fade;

                float2 terrainUV = localPosition.xz / Mat._TerrainSize;
                uint hmW, hmH;
                Mat._Heightmap.GetDimensions(hmW, hmH);
                float2 hmSize2 = float2(hmW, hmH);
                float hmSize = hmSize2.x;
                float vertStep = hmSize > 1.0 ? (1.0 / (hmSize - 1.0)) : 0.001;

                float2 baseUV = terrainUV * (hmSize2 - 1.0) / hmSize2 + 0.5 / hmSize2;
                float2 stepUV = float2(vertStep * (hmSize - 1.0) / hmSize, 0.0);
                float2 stepVV = float2(0.0, vertStep * (hmSize - 1.0) / hmSize);
                float hR = Mat._Heightmap.Sample(baseUV + stepUV).r * Mat._TerrainHeight;
                float hL = Mat._Heightmap.Sample(baseUV - stepUV).r * Mat._TerrainHeight;
                float hU = Mat._Heightmap.Sample(baseUV + stepVV).r * Mat._TerrainHeight;
                float hD = Mat._Heightmap.Sample(baseUV - stepVV).r * Mat._TerrainHeight;

                float wStep = vertStep * Mat._TerrainSize;
                float3 localNormal = normalize(float3(-(hR - hL) / (wStep * 2.0), 1.0, -(hU - hD) / (wStep * 2.0)));
                float3 terrainNormal = normalize(mul(terrainToWorld, float4(localNormal, 0.0)).xyz);

                float3 up = (Mat._AlignToNormal > 0.5) ? terrainNormal : Mat._TerrainUp;
                float3 quadRight;
                float3 localOffset;
                if (Mat._Billboard > 0.5) {
                    float3 cameraRight = Frame.prowl_MatV[0].xyz;
                    cameraRight = normalize(cameraRight - up * dot(cameraRight, up));
                    quadRight = cameraRight;
                    localOffset = cameraRight * input.position.x * scaleX + up * input.position.y * scaleY;
                } else {
                    float3 right = normalize(mul(terrainToWorld, float4(normalize(input.instRow0.xyz), 0.0)).xyz);
                    right = normalize(right - up * dot(right, up));
                    quadRight = right;
                    localOffset = right * input.position.x * scaleX + up * input.position.y * scaleY;
                }

                float windPhase = input.instCustom.x;
                float bendFactor = input.instCustom.y;
                float windAmount = max(0.0, input.position.y);
                float wind = sin(Frame._Time.y * Mat._WindSpeed + bladePosition.x * 0.7 + bladePosition.z * 0.4 + windPhase) * Mat._WindStrength * bendFactor;
                localOffset.x += wind * windAmount;
                localOffset.z += wind * windAmount * 0.3;

                float3 worldPosition = bladePosition + localOffset;
                worldPosition += up * 0.01 * scaleY;
                output.vNormal = normalize(cross(up, quadRight));
                output.position = mul(Frame.prowl_MatVP, float4(worldPosition, 1.0));
                output.texCoord0 = input.uv;
            }
            else
            {
                output.position = mul(Object.mvp, float4(input.position, 1.0));
                output.vNormal = float3(0.0, 1.0, 0.0);
                output.texCoord0 = input.uv;
            }
            return output;
        }

        [shader("fragment")]
        FragOutput Fragment(Varyings input, bool isFront : SV_IsFrontFace)
        {
            float4 texColor = Mat._MainTex.Sample(input.texCoord0);
            if (texColor.a < Mat._AlphaCutoff)
                discard;

            float3 n = normalize(input.vNormal) * (isFront ? 1.0 : -1.0);

            FragOutput o;
            o.normalOut = EncodeViewNormal(n);
            o.motionRM = float4(0.0, 0.0, 1.0, 0.0);
            return o;
        }
        ENDSLANG
    }
}
