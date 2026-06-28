Shader "Default/Grass"
{
    Properties
    {
        _MainTex("Grass Texture", Texture2D) = "white" {}
        _AlphaCutoff("Alpha Cutoff", Float) = 0.5
        _WindStrength("Wind Strength", Float) = 0.3
        _WindSpeed("Wind Speed", Float) = 1.5
        _Billboard("Billboard", Float) = 1.0
        _AlignToNormal("Align To Normal", Float) = 0.0
        _Translucency("Translucency", Float) = 15.0
        _ScatterPower("Scattering Power", Float) = 0.0
        _ScatterDistortion("Scattering Distortion", Float) = 0.5
        _ScatterScale("Scattering Scale", Float) = 1.0
        _GrassDistance("Grass Max Distance", Float) = 150.0
        _GrassFadeStart("Grass Fade Start (world units)", Float) = 90.0
    }

    Pass
    {
        Name "Grass"
        Tags { "RenderOrder" = "Opaque" }
        Cull Off
        ZWrite On

        SLANGPROGRAM

        import ProwlCG;
        import VertexAttributes;
        import Lighting;

        struct VertexInput
        {
            float3 position : POSITION0;
            float2 uv0 : TEXCOORD0;
#ifdef GPU_INSTANCING
            float4 instanceModelRow0 : TEXCOORD8;
            float4 instanceModelRow1 : TEXCOORD9;
            float4 instanceModelRow2 : TEXCOORD10;
            float4 instanceModelRow3 : TEXCOORD11;
            float4 instanceColor : TEXCOORD12;
            float4 instanceCustomData : TEXCOORD13;
#endif
        }

        struct Varyings
        {
            float4 position : SV_Position;
            float2 texCoord0 : TEXCOORD0;
            float4 vColor : COLOR0;
            float3 worldPos : TEXCOORD1;
            float3 vNormal : NORMAL0;
        }

        struct Material
        {
            float _WindStrength;
            float _WindSpeed;
            float _Billboard;
            float _AlignToNormal;
            float _GrassDistance;
            float _GrassFadeStart;
            float3 _TerrainUp;
            float _TerrainSize;
            float _TerrainHeight;
            float4x4 _TerrainWorldToLocal;
            float4x4 _TerrainLocalToWorld;
            float _AlphaCutoff;
            float _Translucency;
            float _ScatterPower;
            float _ScatterDistortion;
            float _ScatterScale;
            Sampler2D<float4> _Heightmap;
            Sampler2D<float4> _MainTex;
        }
        ParameterBlock<Material> Mat;

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            Varyings o;
#ifdef GPU_INSTANCING
            // Instance matrix is in terrain-local space; transform to world
            float4x4 terrainToWorld = Mat._TerrainLocalToWorld;

            float3 localPosition = input.instanceModelRow3.xyz;
            float3 bladePosition = mul(terrainToWorld, float4(localPosition, 1.0)).xyz;
            float scaleX = length(input.instanceModelRow0.xyz); // width
            float scaleY = length(input.instanceModelRow1.xyz); // height

            // Distance fade
            float3 camFlat = float3(Frame._WorldSpaceCameraPos.x, bladePosition.y, Frame._WorldSpaceCameraPos.z);
            float camDist = length(bladePosition - camFlat);
            float fadeRange = max(0.0001, Mat._GrassDistance - Mat._GrassFadeStart);
            float fade = 1.0 - clamp((camDist - Mat._GrassFadeStart) / fadeRange, 0.0, 1.0);
            scaleX *= fade;
            scaleY *= fade;

            // Terrain surface normal from heightmap for blade orientation.
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
                // Camera right is row 0 of the view matrix (GLSL V[i][0] -> Slang V[0]).
                float3 cameraRight = Frame.prowl_MatV[0].xyz;
                cameraRight = normalize(cameraRight - up * dot(cameraRight, up));
                quadRight = cameraRight;
                localOffset = cameraRight * input.position.x * scaleX
                             + up * input.position.y * scaleY;
            }
            else
            {
                float3 right = normalize(mul(terrainToWorld, float4(normalize(input.instanceModelRow0.xyz), 0.0)).xyz);
                right = normalize(right - up * dot(right, up));
                quadRight = right;
                localOffset = right * input.position.x * scaleX
                            + up * input.position.y * scaleY;
            }

            float3 quadNormal = normalize(cross(up, quadRight));

            // Wind sway - only affects top vertices (y > 0)
            float windPhase = input.instanceCustomData.x;
            float bendFactor = input.instanceCustomData.y;
            float windAmount = max(0.0, input.position.y);
            float wind = sin(Frame._Time.y * Mat._WindSpeed + bladePosition.x * 0.7 + bladePosition.z * 0.4 + windPhase) * Mat._WindStrength * bendFactor;
            localOffset.x += wind * windAmount;
            localOffset.z += wind * windAmount * 0.3;

            float3 worldPosition = bladePosition + localOffset;
            worldPosition += up * 0.01 * scaleY; // Minimal offset to reduce ground clipping
            o.worldPos = worldPosition;
            o.vNormal = quadNormal;
            o.vColor = input.instanceColor;

            o.position = mul(Frame.prowl_MatVP, float4(worldPosition, 1.0));
            o.texCoord0 = input.uv0;
#else
            o.position = mul(Object.mvp, float4(input.position, 1.0));
            o.texCoord0 = input.uv0;
            o.worldPos = mul(Object.prowl_ObjectToWorld, float4(input.position, 1.0)).xyz;
            o.vNormal = float3(0.0, 1.0, 0.0);
            o.vColor = float4(1.0);
#endif
            return o;
        }

        [shader("fragment")]
        float4 Fragment(Varyings input, bool isFront : SV_IsFrontFace) : SV_Target
        {
            float4 texColor = Mat._MainTex.Sample(input.texCoord0);
            float4 finalColor = texColor * input.vColor;

            if (finalColor.a < Mat._AlphaCutoff)
                discard;

            float3 baseColor = gammaToLinearSpace(finalColor.rgb);
            // Flip normal for back faces since grass is double-sided
            float3 normal = normalize(input.vNormal) * (isFront ? 1.0 : -1.0);

            // Unified PBR + translucency in a single light loop
            float3 viewDir = normalize(Frame._WorldSpaceCameraPos.xyz - input.worldPos);
            float3 lighting = CalculateForwardLighting(input.worldPos, normal, viewDir,
                                                     baseColor, 0.0, 0.9, 1.0,
                                                     Mat._Translucency, Mat._ScatterPower,
                                                     Mat._ScatterDistortion, Mat._ScatterScale, input.position.xy);
            float3 ambient = CalculateAmbient(normal) * baseColor * Light._AmbientStrength;

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
        import VertexAttributes;

        struct VertexInput
        {
            float3 position : POSITION0;
            float2 uv0 : TEXCOORD0;
#ifdef GPU_INSTANCING
            float4 instanceModelRow0 : TEXCOORD8;
            float4 instanceModelRow1 : TEXCOORD9;
            float4 instanceModelRow2 : TEXCOORD10;
            float4 instanceModelRow3 : TEXCOORD11;
            float4 instanceColor : TEXCOORD12;
            float4 instanceCustomData : TEXCOORD13;
#endif
        }

        struct Varyings
        {
            float4 position : SV_Position;
            float3 vNormal : NORMAL0;
            float2 texCoord0 : TEXCOORD0;
        }

        struct Material
        {
            float _WindStrength;
            float _WindSpeed;
            float _Billboard;
            float _AlignToNormal;
            float _GrassDistance;
            float _GrassFadeStart;
            float3 _TerrainUp;
            float _TerrainSize;
            float _TerrainHeight;
            float4x4 _TerrainWorldToLocal;
            float4x4 _TerrainLocalToWorld;
            float _AlphaCutoff;
            Sampler2D<float4> _Heightmap;
            Sampler2D<float4> _MainTex;
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
#ifdef GPU_INSTANCING
            float4x4 terrainToWorld = Mat._TerrainLocalToWorld;
            float3 localPosition = input.instanceModelRow3.xyz;
            float3 bladePosition = mul(terrainToWorld, float4(localPosition, 1.0)).xyz;
            float scaleX = length(input.instanceModelRow0.xyz);
            float scaleY = length(input.instanceModelRow1.xyz);

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
                float3 right = normalize(mul(terrainToWorld, float4(normalize(input.instanceModelRow0.xyz), 0.0)).xyz);
                right = normalize(right - up * dot(right, up));
                quadRight = right;
                localOffset = right * input.position.x * scaleX + up * input.position.y * scaleY;
            }

            float windPhase = input.instanceCustomData.x;
            float bendFactor = input.instanceCustomData.y;
            float windAmount = max(0.0, input.position.y);
            float wind = sin(Frame._Time.y * Mat._WindSpeed + bladePosition.x * 0.7 + bladePosition.z * 0.4 + windPhase) * Mat._WindStrength * bendFactor;
            localOffset.x += wind * windAmount;
            localOffset.z += wind * windAmount * 0.3;

            float3 worldPosition = bladePosition + localOffset;
            worldPosition += up * 0.01 * scaleY; // Must match main Grass pass offset
            o.vNormal = normalize(cross(up, quadRight));
            o.position = mul(Frame.prowl_MatVP, float4(worldPosition, 1.0));
            o.texCoord0 = input.uv0;
#else
            o.position = mul(Object.mvp, float4(input.position, 1.0));
            o.vNormal = float3(0.0, 1.0, 0.0);
            o.texCoord0 = input.uv0;
#endif
            return o;
        }

        [shader("fragment")]
        FragOut Fragment(Varyings input, bool isFront : SV_IsFrontFace)
        {
            float4 texColor = Mat._MainTex.Sample(input.texCoord0);
            if (texColor.a < Mat._AlphaCutoff)
                discard;

            float3 n = normalize(input.vNormal) * (isFront ? 1.0 : -1.0);

            FragOut o;
            o.normalOut = EncodeViewNormal(n);
            // Grass is procedurally wind-animated with no stable previous position, so motion stays zero.
            o.motionRM = float4(0.0, 0.0, 1.0, 0.0);
            return o;
        }

        ENDSLANG
    }
}
