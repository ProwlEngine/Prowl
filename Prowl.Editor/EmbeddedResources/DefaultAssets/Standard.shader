﻿Shader "Default/Standard"
Properties
{
    _AlbedoTex("Albedo Texture", Texture2D) = "white"
    _NormalTex("Normal Texture", Texture2D) = "normal"
    _SurfaceTex("Surface Texture", Texture2D) = "surface"
    _MainColor("Main Color", Color) = (1, 1, 1, 1)
    _AlphaClip("Alpha Clip", Float) = 0.5
}

Pass "Standard"
{
    Tags { "RenderOrder" = "Opaque" }
    Cull Back
    DepthStencil DepthLessEqual

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
            PROWL_FOG_COORDS(3)
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
        int _LightCount;
        float4 _MainColor;

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

		// Constants for shadow calculation
		static const float MIN_PENUMBRA_SIZE = 0.5;
		static const float BIAS_SCALE = 0.001;
		static const float NORMAL_BIAS_SCALE = 0.01;
		static const float _ShadowSoftness = 2.0;
		static const int _PCFSamples = 32;
		static const int _BlockerSearchSamples = 16;
		
		float2 VogelDiskSample(int sampleIndex, int samplesCount, float phi)
		{
			float GoldenAngle = 2.4f;
			
			float r = sqrt(sampleIndex + 0.5f) / sqrt(samplesCount);
			float theta = sampleIndex * GoldenAngle + phi;
			
			float sine, cosine;
			sincos(theta, sine, cosine);
			
			return float2(r * cosine, r * sine);
		}
		
		float InterleavedGradientNoise(float2 position_screen)
		{
			float3 magic = float3(0.06711056f, 0.00583715f, 52.9829189f);
			return frac(magic.z * frac(dot(position_screen, magic.xy)));
		}
		
		// Improved blocker search with early exit and better averaging
		float FindBlockerDistance(float2 uv, float2 lightPixelSize, float currentDepth, Light light, float2 screenPos)
		{
			float searchWidth = light.ShadowData.x * (currentDepth - light.ShadowData.z) / currentDepth;
			searchWidth = max(searchWidth, MIN_PENUMBRA_SIZE); // Ensure minimum search area
			
			float blockerSum = 0.0;
			float numBlockers = 0.0;
			float maxBlockerDistance = 0.0;
			
			int blockerSamples = int(light.PositionType.y); // THIS ONLY WORKS FOR DIRECTIONAL
			// Get rotated angle from screen position
			float phi = InterleavedGradientNoise(screenPos) * 2.0 * PI;
			
			[unroll]
			for(int i = 0; i < blockerSamples; i++)
			{
				float2 offset = VogelDiskSample(i, blockerSamples, phi) * searchWidth;
				float2 sampleUV = uv + offset * lightPixelSize;
				
				float shadowMapDepth = _ShadowAtlas.Sample(sampler_ShadowAtlas, sampleUV).r;
				
				if(shadowMapDepth < currentDepth - (light.ShadowData.z * BIAS_SCALE))
				{
					blockerSum += shadowMapDepth;
					maxBlockerDistance = max(maxBlockerDistance, currentDepth - shadowMapDepth);
					numBlockers++;
				}
			}
			
			if(numBlockers < 1.0)
				return -1.0;
				
			return blockerSum / numBlockers;
		}
		
		// Improved PCF filtering with depth-dependent kernel size
		float PCF_Filter(float2 uv, float2 lightPixelSize, float currentDepth, float filterRadius, Light light, float2 screenPos)
		{
			float sum = 0.0;
			float weightSum = 0.0;
			
			int qualitySamples = int(light.ShadowData.y);
			// Get rotated angle from screen position
			float phi = InterleavedGradientNoise(screenPos) * 2.0 * PI;
			
			[loop]
			for(int i = 0; i < qualitySamples; i++)
			{
				float2 offset = VogelDiskSample(i, qualitySamples, phi) * filterRadius;
				float2 sampleUV = uv + offset * lightPixelSize;
				
				// Calculate sample weight based on distance from center
				float weight = 1.0 - length(offset) / filterRadius;
				weight = max(0.0, weight * weight); // Quadratic falloff
				
				float shadowMapDepth = _ShadowAtlas.Sample(sampler_ShadowAtlas, sampleUV).r;
				
				sum += ((currentDepth - (light.ShadowData.z * BIAS_SCALE)) > shadowMapDepth ? 1.0 : 0.0) * weight;
				weightSum += weight;
			}
			
			return sum / weightSum;
		}
		
		float PCSS(float4 fragPosLightSpace, Light light, float2 screenPos)
		{
			float3 projCoords = fragPosLightSpace.xyz / fragPosLightSpace.w;
			projCoords = projCoords * 0.5 + 0.5;
			
			// Early exit for positions outside shadow map
			if (any(projCoords > 1.0) || any(projCoords < 0.0) || light.AtlasWidth <= 1.0)
				return 0.0;
			
			float AtlasX = (float)light.AtlasX;
			float AtlasY = (float)light.AtlasY;
			float AtlasWidth = (float)light.AtlasWidth;
			
			float2 atlasCoords;
			atlasCoords.x = AtlasX + (projCoords.x * AtlasWidth);
			atlasCoords.y = AtlasY + ((1.0 - projCoords.y) * AtlasWidth);

            float atlasSize = prowl_ShadowAtlasSize;
			atlasCoords /= atlasSize;
			atlasCoords.y = 1.0 - atlasCoords.y;
			
			float currentDepth = projCoords.z;
			float2 lightPixelSize = 1.0 / atlasSize;
			
			//// STEP 1: Blocker search
			//float blockerDistance = FindBlockerDistance(atlasCoords, lightPixelSize, currentDepth, light, screenPos);
			//if(blockerDistance < 0.0)
			//	return 0.0;
			//	
			//// STEP 2: Penumbra size estimate
			//float penumbraWidth = (currentDepth - blockerDistance) * light.ShadowData.x / blockerDistance;
			//penumbraWidth = max(penumbraWidth, MIN_PENUMBRA_SIZE);
			float penumbraWidth = light.ShadowData.x;
			
			// STEP 3: Filtering
			return PCF_Filter(atlasCoords, lightPixelSize, currentDepth, penumbraWidth * _ShadowSoftness, light, screenPos);
		}
		
		float ShadowCalculation(float4 fragPosLightSpace, Light light, float2 screenPos)
		{
			return PCSS(fragPosLightSpace, light, screenPos);
		}
		
		//This code is from "Rectangular Area Light" by Tsone,
		//used under CC BY 4.0 (http://creativecommons.org/licenses/by/4.0/).
		//Source: https://www.shadertoy.com/view/lsfGDN
		float RectLight_CalcWeight(float3 P, float3 R, float3 lightPos, float3 lightDir, 
								float3 lightRight, float3 lightUp, float2 lightSize, 
								float attenuation, float theta)
		{
			// Intersect ray and light plane
			float RoPlN = dot(R, lightDir);
			float d = dot(lightDir, lightPos - P) / RoPlN;
			
			if (d < 0.0 || RoPlN > 0.0) {
				return 0.0;
			}
			
			// Point on plane
			float3 PlC = P + d*R - lightPos;
			
			// UV coordinate on plane
			float2 PlUV = float2(dot(PlC, lightRight), dot(PlC, lightUp));
			
			// Radius of cone at distance d
			float r = d * tan(theta);
			
			// Rect size shifted by radius for weight 1 inside rect
			float2 s = max(lightSize - 0.5*r, 0.0);
			
			// Distance from rect on plane
			float h = length(max(abs(PlUV) - s, 0.0));
			
			// Steradians from sphere cap
			float sr = 2.0* 3.14159265359 * (1.0 - cos(theta));
			
			// Gaussian distribution with variance v^2 = 1/2
			float x = (3.0 * 0.70710678118 / 2.0) * (h/r);
			return exp(-(x * x)) / (attenuation + (d * d) * sr);
		}

        Varyings Vertex(Attributes input)
        {
            Varyings output = (Varyings)0;
            
            float4 viewPos = mul(PROWL_MATRIX_V, mul(PROWL_MATRIX_M, float4(input.position, 1.0)));
            output.fragPos = viewPos.xyz;
            output.vertPos = mul(PROWL_MATRIX_M, float4(input.position, 1.0)).xyz;
            
            output.position = mul(PROWL_MATRIX_MVP, float4(input.position, 1.0));
            output.uv = input.uv;
            output.vertColor = input.color;
            output.normal = input.normal;
    
			// Correctly transform normal and tangent to world space
			float3 worldNormal = normalize(mul((float3x3)PROWL_MATRIX_M, input.normal));
			float3 worldTangent = normalize(mul((float3x3)PROWL_MATRIX_M, input.tangent));
			
			// Ensure tangent is perpendicular to normal using Gram-Schmidt
			worldTangent = normalize(worldTangent - dot(worldTangent, worldNormal) * worldNormal);
			
			// Calculate bitangent and ensure proper handedness
			float3 worldBitangent = cross(worldNormal, worldTangent);
			
			// Construct TBN matrix - note the transpose here
			output.TBN = float3x3(
				worldTangent,
				worldBitangent,
				worldNormal
			);

            PROWL_TRANSFER_FOG(output, output.position);
            
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
            baseColor.rgb = GammaToLinearSpace(baseColor.rgb);

            // Normal
            float3 normal = _NormalTex.Sample(sampler_NormalTex, input.uv).rgb;
            normal = normal * 2.0 - 1.0;   
            normal = normalize(mul(normal, input.TBN));
            output.Normal = mul((float3x3)PROWL_MATRIX_V, normal);

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

            [loop]
            for(uint i = 0; i < _LightCount; i++)
            {
                Light light = _Lights[i];
                float3 lightColor = UnpackAndConvertRGBA(light.Color).rgb;
                float intensity = light.Intensity;

                if (light.PositionType.w == 0.0) // Directional Light
                {
                    float3 L = normalize(-(mul((float3x3)PROWL_MATRIX_V, light.DirectionRange.xyz)));
                    float3 H = normalize(V + L);

                    float3 kD;
                    float3 specular;
                    CookTorrance(N, H, L, V, F0, surface.g, surface.b, kD, specular);

                    float4 fragPosLightSpace = mul(light.ShadowMatrix, float4(input.vertPos + (input.normal * (light.ShadowData.w * NORMAL_BIAS_SCALE)), 1.0));
                    float shadow = ShadowCalculation(fragPosLightSpace, light, input.position.xy);

                    float3 radiance = lightColor * intensity;
                    float NdotL = max(dot(N, L), 0.0);
                    float3 color = ((kD * baseColor.rgb) / PI + specular) * radiance * (1.0 - shadow) * NdotL;

                    lighting += color;
                }
                else if(light.PositionType.w == 1.0) // Point Light
                {
                    float radius = light.DirectionRange.w;
                    
                    float3 lightPos = mul(PROWL_MATRIX_V, float4(light.PositionType.xyz, 1)).xyz;
                    float3 L = normalize(lightPos - input.fragPos);
                    float3 H = normalize(V + L);
                    
                    float3 kD;
                    float3 specular;
                    CookTorrance(N, H, L, V, F0, surface.g, surface.b, kD, specular);
                    
                    // attenuation
                    float distance = length(lightPos - input.fragPos);
                    float falloff = (saturate(1.0 - pow(distance / radius, 4)) * 
                                    saturate(1.0 - pow(distance / radius, 4))) / 
                                    (distance * distance + 1.0);
                    float3 radiance = lightColor * intensity * falloff;
                
                    // add to outgoing radiance Lo
                    float NdotL = max(dot(N, L), 0.0);                
                    float3 color = (kD * baseColor.rgb / PI + specular) * radiance * NdotL;
                    
                    lighting += color;
                }
                else if(light.PositionType.w == 2.0) // Area Light
                {
					// Unpack width and height
					uint packedSize = asuint(light.DirectionRange.w);
					float width = float(packedSize & 0xFFFF) / 512.0;
					float height = float(packedSize >> 16) / 512.0;
					float2 lightSize = float2(width, height);
					
					float3 lightPos = mul(PROWL_MATRIX_V, float4(light.PositionType.xyz, 1)).xyz;
					float3 lightDir = normalize(mul((float3x3)PROWL_MATRIX_V, light.DirectionRange.xyz));
					bool twoSided = light.SpotData.x > 0.5;
					
					// Calculate basis vectors for the light
					float3 lightRight = normalize(cross(lightDir, mul((float3x3)PROWL_MATRIX_V, float3(0, 1, 0))));
					float3 lightUp = normalize(cross(lightRight, lightDir));
					
					float3 N = normalize(output.Normal);
					float3 V = normalize(-input.fragPos);
					float3 R = reflect(-V, N);
					float NoR = max(dot(N, R), 0.0);
					
					// Schlick Fresnel
					float3 F = F0 + (1.0 - F0) * pow(1.0 - NoR, 5.0);
					
					// Calculate specular contribution
					float specTheta = lerp(PI * 0.003, PI/6.0, surface.g); // surface.g is roughness
					float Cs = RectLight_CalcWeight(input.fragPos, R, lightPos, lightDir, 
												lightRight, lightUp, lightSize, 1.0, specTheta);
					
					// Calculate wider specular tail for roughness
					float tailTheta = PI/3.0;
					float Cst = RectLight_CalcWeight(input.fragPos, R, lightPos, lightDir, 
												lightRight, lightUp, lightSize, 1.0, tailTheta);
					
					// Calculate diffuse contribution
					float3 W = normalize(N - lightDir);
					float Cd = RectLight_CalcWeight(input.fragPos, W, lightPos, lightDir, 
												lightRight, lightUp, lightSize, 1.0, PI/4.0);
					
					// Combine contributions
					float diffuseWeight = Cd * max(dot(N, W), 0.0);
					float specularWeight = lerp(Cs, Cst, surface.g * 0.5) * NoR;
					
					// Final light contribution
					float3 diffuse = (baseColor.rgb / PI) * diffuseWeight * (1.0 - F);
					float3 specular = specularWeight * F;
					
					//if (!twoSided && dot(-lightDir, N) <= 0.0) {
					//	diffuse = 0.0;
					//	specular = 0.0;
					//}
					
					float3 radiance = lightColor * intensity;
					lighting += (diffuse + specular) * radiance;
                }
                else // Spot Light
                {
                    float3 lightPos = mul(PROWL_MATRIX_V, float4(light.PositionType.xyz, 1)).xyz;
                    float3 L = normalize(lightPos - input.fragPos);
                    float3 H = normalize(V + L);
                    float theta = dot(L, normalize(-mul((float3x3)PROWL_MATRIX_V, light.DirectionRange.xyz)));
                
                    // attenuation
                    float radius = light.DirectionRange.w;
                    float lightAngle = light.SpotData.x;
                    float lightFalloff = light.SpotData.y;
                    
                    float distance = length(lightPos - input.fragPos);
                    float falloff = (saturate(1.0 - pow(distance / radius, 4)) * 
                                    saturate(1.0 - pow(distance / radius, 4))) / 
                                    (distance * distance + 1.0);
                    
                    // cone attenuation
                    float epsilon = lightAngle - lightFalloff;
                    float coneAttenuation = saturate((theta - lightFalloff) / epsilon);
                    
                    float3 radiance = lightColor * intensity * falloff * coneAttenuation;
                    
                    float3 kD;
                    float3 specular;
                    CookTorrance(N, H, L, V, F0, surface.g, surface.b, kD, specular);
                    specular *= coneAttenuation;
                    
                    // shadows
                    float4 fragPosLightSpace = mul(light.ShadowMatrix, float4(input.vertPos + (input.normal * (light.ShadowData.w * NORMAL_BIAS_SCALE)), 1.0));
                    float shadow = ShadowCalculation(fragPosLightSpace, light, input.position.xy);
                    
                    // add to outgoing radiance Lo
                    float NdotL = max(dot(N, L), 0.0);        
                    float3 color = ((kD * baseColor.rgb) / PI + specular) * radiance * (1.0 - shadow) * NdotL;
                    
                    lighting += color;
                }
            }
            
            lighting *= (1.0 - surface.r);
            PROWL_AMBIENT(output.Normal, baseColor);
            baseColor.rgb += lighting;

            PROWL_APPLY_FOG(input, baseColor);

            output.Albedo = float4(baseColor.rgb, 1.0);

            return output;
        }
    ENDHLSL
}

Pass "Shadow"
{
    Tags { "LightMode" = "ShadowCaster" }
    Cull Back

    HLSLPROGRAM
        #pragma vertex Vertex
        #pragma fragment Fragment

        #include "Prowl.hlsl"

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

        Texture2D<float4> _AlbedoTex;
        SamplerState sampler_AlbedoTex;

        Varyings Vertex(Attributes input)
        {
            Varyings output = (Varyings)0;
            output.position = mul(PROWL_MATRIX_MVP, float4(input.position, 1.0));
            output.vertPos = mul(PROWL_MATRIX_M, float4(input.position, 1.0)).xyz;
            output.uv = input.uv;
            return output;
        }

        float Fragment(Varyings input)
        {
            float4 fragPosLightSpace = mul(mul(PROWL_MATRIX_P, PROWL_MATRIX_V), float4(input.vertPos, 1.0));
            float3 projCoords = fragPosLightSpace.xyz / fragPosLightSpace.w;
            projCoords = projCoords * 0.5 + 0.5;
            return projCoords.z;
        }
    ENDHLSL
}

Pass "MotionVectors"
{
    Tags { "LightMode" = "MotionVectors" }
    
    HLSLPROGRAM
        #pragma vertex Vertex
        #pragma fragment Fragment

        #include "Prowl.hlsl"

        struct Attributes
        {
            float3 position : POSITION;
            float2 uv : TEXCOORD0;
            float3 normal : NORMAL;
        };

        struct Varyings
        {
            float4 position : SV_POSITION;
            float4 currentPos : TEXCOORD0;
            float4 previousPos : TEXCOORD1;
        };

        Varyings Vertex(Attributes input)
        {
            Varyings output = (Varyings)0;
            
            // Current frame positions
            float4 worldPos = mul(PROWL_MATRIX_M, float4(input.position, 1.0));
            output.position = mul(PROWL_MATRIX_MVP, float4(input.position, 1.0));
            output.currentPos = output.position;
            
            // Previous frame positions
            float4 prevWorldPos = mul(PROWL_MATRIX_M_PREVIOUS, float4(input.position, 1.0));
            output.previousPos = mul(PROWL_MATRIX_V_PREVIOUS, prevWorldPos);
            
            return output;
        }

        float2 Fragment(Varyings input) : SV_Target
        {
            // Convert positions to NDC space [-1,1]
            float2 currentPos = input.currentPos.xy / input.currentPos.w;
            float2 previousPos = input.previousPos.xy / input.previousPos.w;
            
            // Calculate screen-space motion vector
            // Scale from [-1,1] to [0,1] and then calculate difference
            float2 motionVector = (currentPos - previousPos) * 0.5f;
            
            return motionVector;
        }
    ENDHLSL
}
