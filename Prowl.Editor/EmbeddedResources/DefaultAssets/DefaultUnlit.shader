Shader "Default/TestShader"
Properties
{
    _AlbedoTex("Albedo Texture", Texture2D) = "white"
    _NormalTex("Normal Texture", Texture2D) = "normal"
    _SurfaceTex("Surface Texture", Texture2D) = "surface"
    _MainColor("Main Color", Color) = (1, 1, 1, 1)
    _AlphaClip("Alpha Clip", Float) = 0.5
}

Pass "TestShader"
{
    Tags { "RenderOrder" = "Opaque" }
    Cull Back

    HLSLPROGRAM
        #pragma vertex Vertex
        #pragma fragment Fragment

        #include "Prowl.hlsl"
        #include "PBR.hlsl"

        struct Attributes
        {
            float3 position : POSITION;
            float2 uv : TEXCOORD0;
            float3 normal : NORMAL;
            float3 tangent : TANGENT;
            float4 color : COLOR;
        };

        struct Varyings
        {
            float4 position : SV_POSITION;
            float2 uv : TEXCOORD0;
            float4 vertColor : COLOR;
            float3 fragPos : TEXCOORD1;
            float3x3 TBN : TEXCOORD2;
            float3 normal : NORMAL;
            float3 vertPos : TEXCOORD5;
        };

        struct Light 
        {
            float4 PositionType;
            float4 DirectionRange;
            uint Color;
            float Intensity;
            float2 SpotData;
            float4 ShadowData;
            float4x4 ShadowMatrix;
            int AtlasX;
            int AtlasY;
            int AtlasWidth;
            int Padding;
        };

        // Textures and samplers
        Texture2D<float4> _AlbedoTex;
        SamplerState sampler_AlbedoTex;
        Texture2D<float4> _NormalTex;
        SamplerState sampler_NormalTex;
        Texture2D<float4> _SurfaceTex;
        SamplerState sampler_SurfaceTex;
        Texture2D<float4> _ShadowAtlas;
        SamplerState sampler_ShadowAtlas;

        float _AlphaClip;
        float _LightCount;
        float4 _MainColor;

        // Default uniforms buffer
        cbuffer _PerDraw
        {
            float4x4 Mat_V;
            float4x4 Mat_P;
            float4x4 Mat_ObjectToWorld;
            float4x4 Mat_WorldToObject;
            float4x4 Mat_MVP;

            int _ObjectID;
        }

        // Structured buffer for lights
        StructuredBuffer<Light> _Lights;

        float4 UnpackAndConvertRGBA(uint packed)
        {
            uint4 color;
            color.r = packed & 0xFF;
            color.g = (packed >> 8) & 0xFF;
            color.b = (packed >> 16) & 0xFF;
            color.a = (packed >> 24) & 0xFF;
            return float4(color) / 255.0;
        }

        float ShadowCalculation(float4 fragPosLightSpace, Light light)
        {
            float3 projCoords = fragPosLightSpace.xyz / fragPosLightSpace.w;
            projCoords = projCoords * 0.5 + 0.5;
            
            if (any(projCoords > 1.0) || any(projCoords < 0.0))
                return 0.0;
            
            float AtlasX = (float)light.AtlasX;
            float AtlasY = (float)light.AtlasY;
            float AtlasWidth = (float)light.AtlasWidth;
            
            float2 atlasCoords;
            atlasCoords.x = AtlasX + (projCoords.x * AtlasWidth);
            atlasCoords.y = AtlasY + ((1.0 - projCoords.y) * AtlasWidth);
            
            atlasCoords /= float2(4096.0, 4096.0);
            atlasCoords.y = 1.0 - atlasCoords.y;
            
            float closestDepth = _ShadowAtlas.Sample(sampler_ShadowAtlas, atlasCoords).r;
            float currentDepth = projCoords.z;
            
            return (currentDepth - light.ShadowData.z) > closestDepth ? 1.0 : 0.0;
        }

        Varyings Vertex(Attributes input)
        {
            Varyings output = (Varyings)0;
            
            float4 viewPos = mul(Mat_V, mul(Mat_ObjectToWorld, float4(input.position, 1.0)));
            output.fragPos = viewPos.xyz;
            output.vertPos = mul(Mat_ObjectToWorld, float4(input.position, 1.0)).xyz;
            
            output.position = mul(Mat_MVP, float4(input.position, 1.0));
            output.uv = input.uv;
            output.vertColor = input.color;
            output.normal = input.normal;
            
            float3x3 normalMatrix = transpose((float3x3)Mat_WorldToObject);
            
            float3 T = normalize(mul((float3x3)Mat_ObjectToWorld, input.tangent));
            float3 B = normalize(mul((float3x3)Mat_ObjectToWorld, cross(input.normal, input.tangent)));
            float3 N = normalize(mul((float3x3)Mat_ObjectToWorld, input.normal));
            output.TBN = float3x3(T, B, N);
            
            return output;
        }

        struct PSOutput
        {
            float4 Albedo : SV_Target0;
            float3 Normal : SV_Target1;
            float3 AoRoughnessMetallic : SV_Target2;
            uint ObjectID : SV_Target3;
        };

        PSOutput Fragment(Varyings input)
        {
            PSOutput output = (PSOutput)0;

            // Albedo & Cutout
            float4 baseColor = _AlbedoTex.Sample(sampler_AlbedoTex, input.uv);
            clip(baseColor.a - _AlphaClip);
            baseColor.rgb = pow(baseColor.rgb, 2.2);

            // Normal
            float3 normal = _NormalTex.Sample(sampler_NormalTex, input.uv).rgb;
            normal = normal * 2.0 - 1.0;   
            normal = normalize(mul(input.TBN, normal));
            output.Normal = mul((float3x3)Mat_V, normal);

            // AO, Roughness, Metallic
            float3 surface = _SurfaceTex.Sample(sampler_SurfaceTex, input.uv).rgb;
            output.AoRoughnessMetallic = surface;

            // Object ID
            output.ObjectID = (uint)_ObjectID;

            // Lighting calculation
            float roughness2 = surface.g * surface.g;
            float limiterStrength = 0.2;
            float limiterClamp = 0.18;
            float3 dndu = ddx(output.Normal), dndv = ddy(output.Normal);
            float variance = limiterStrength * (dot(dndu, dndu) + dot(dndv, dndv));
            float kernelRoughness2 = min(2.0 * variance, limiterClamp);
            float filteredRoughness2 = min(1.0, roughness2 + kernelRoughness2);
            surface.g = sqrt(filteredRoughness2);

            float3 F0 = float3(0.04, 0.04, 0.04);
            F0 = lerp(F0, baseColor.rgb, surface.b);
            float3 N = normalize(output.Normal);
            float3 V = normalize(-input.fragPos);

            float3 lighting = float3(0, 0, 0);
            float ambientStrength = 0.0;

            [loop]
            for(uint i = 0; i < _LightCount; i++)
            {
                Light light = _Lights[i];
                float3 lightColor = UnpackAndConvertRGBA(light.Color).rgb;
                float intensity = light.Intensity;

                if (light.PositionType.w == 0.0) // Directional Light
                {
                    float3 L = normalize(-(mul((float3x3)Mat_V, light.DirectionRange.xyz)));
                    float3 H = normalize(V + L);

                    float3 kD;
                    float3 specular;
                    CookTorrance(N, H, L, V, F0, surface.g, surface.b, kD, specular);

                    float4 fragPosLightSpace = mul(light.ShadowMatrix, float4(input.vertPos + (normal * light.ShadowData.w), 1.0));
                    float shadow = ShadowCalculation(fragPosLightSpace, light);

                    float3 radiance = lightColor * intensity;
                    float NdotL = max(dot(N, L), 0.0);
                    float3 color = ((kD * baseColor.rgb) / PI + specular) * radiance * (1.0 - shadow) * NdotL;

                    ambientStrength += light.SpotData.x;
                    lighting += color;
                }
            }

            //lighting *= (1.0 - surface.r);
            //baseColor.rgb *= ambientStrength;
            //baseColor.rgb += lighting;

            output.Albedo = float4(baseColor.rgb, 1.0);
            return output;
        }
    ENDHLSL
}

Pass "Shadow"
{
    Tags { "RenderOrder" = "Shadow" }
    Cull None

    HLSLPROGRAM
        #pragma vertex Vertex
        #pragma fragment Fragment

        struct Attributes
        {
            float3 position : POSITION;
            float2 uv : TEXCOORD0;
        };

        struct Varyings
        {
            float4 position : SV_POSITION;
            float2 uv : TEXCOORD0;
            float3 vertPos : TEXCOORD1;
        };

        cbuffer DefaultUniforms
        {
            float4x4 Mat_V;
            float4x4 Mat_P;
            float4x4 Mat_ObjectToWorld;
            float4x4 Mat_WorldToObject;
            float4x4 Mat_MVP;
        }

        Texture2D<float4> _AlbedoTex;
        SamplerState sampler_AlbedoTex;

        Varyings Vertex(Attributes input)
        {
            Varyings output = (Varyings)0;
            output.position = mul(Mat_MVP, float4(input.position, 1.0));
            output.vertPos = mul(Mat_ObjectToWorld, float4(input.position, 1.0)).xyz;
            output.uv = input.uv;
            return output;
        }

        float Fragment(Varyings input) : SV_DEPTH
        {
            float4 fragPosLightSpace = mul(mul(Mat_P, Mat_V), float4(input.vertPos, 1.0));
            float3 projCoords = fragPosLightSpace.xyz / fragPosLightSpace.w;
            projCoords = projCoords * 0.5 + 0.5;
            return projCoords.z;
        }
    ENDHLSL
}
