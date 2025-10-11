Shader "Default/Standard"

Properties
{
    _MainTex ("Albedo", Texture2D) = "grid"
    _MainColor ("Tint", Color) = (1.0, 1.0, 1.0, 1.0)

    _NormalTex ("Normal", Texture2D) = "normal"

    _SurfaceTex ("Surface (AO, Roughness, Metallicness)", Texture2D) = "surface"

    _EmissionTex ("Emission", Texture2D) = "emission"
    _EmissionIntensity ("Emission Intensity", Float) = 1.0

}

Pass "Standard"
{
    Tags { "RenderOrder" = "Opaque" }

    // Rasterizer culling mode
    Cull Back

	GLSLPROGRAM
		
		Vertex
		{
            #include "Fragment"
            #include "VertexAttributes"

			out vec2 texCoord0;
			out vec3 worldPos;
			out vec4 currentPos;
			out vec4 previousPos;
			out float fogCoord;

			out vec4 vColor;
			out vec3 vNormal;
			out vec3 vTangent;
			out vec3 vBitangent;

			void main()
			{
			    gl_Position = PROWL_MATRIX_MVP * vec4(vertexPosition, 1.0);
				fogCoord = gl_Position.z;
				currentPos = gl_Position; // Clip space
			    texCoord0 = vertexTexCoord0;
				
				// Previous position with current projection
				vec4 prevWorldPos = PROWL_MATRIX_M_PREVIOUS * vec4(vertexPosition, 1.0);
				previousPos = PROWL_MATRIX_VP_PREVIOUS * prevWorldPos;

				worldPos = (PROWL_MATRIX_M * vec4(vertexPosition, 1.0)).xyz;
				
				vColor = vertexColor;

				vNormal = normalize(mat3(PROWL_MATRIX_M) * vertexNormal);
#ifdef HAS_TANGENTS
				vTangent = normalize(mat3(PROWL_MATRIX_M) * vertexTangent.xyz);
				vBitangent = cross(vNormal, vTangent);
#endif
			}
		}

		Fragment
		{
            #include "Fragment"
            #include "PBR"

			//#define USEGENERATEDNORMALS

			layout (location = 0) out vec4 gAlbedo;
			layout (location = 1) out vec4 gMotionVector;
			layout (location = 2) out vec4 gNormal;
			layout (location = 3) out vec4 gSurface;
			
			in vec2 texCoord0;
			in vec3 worldPos;
			in vec4 currentPos;
			in vec4 previousPos;
			in float fogCoord;
			in vec4 vColor;
			in vec3 vNormal;
			in vec3 vTangent;
			in vec3 vBitangent;

			uniform sampler2D _MainTex; // diffuse
			uniform sampler2D _NormalTex; // normal
			uniform sampler2D _SurfaceTex; // surface - AO, roughness, metallic
			uniform sampler2D _EmissionTex; // emission
			uniform float _EmissionIntensity; // emission intensity
			
			uniform sampler2D _ShadowAtlas;

			uniform vec4 _MainColor;
			
			struct SunLightStruct {
			    vec3 direction;       // Maps to dirX, dirY, dirZ
			    vec3 color;           // Maps to colR, colG, colB
			    float intensity;      // Maps to intensity
				mat4 shadowMatrix;    // Maps to shadowMatrix
				float shadowBias;     // Maps to shadowBias
				float shadowNormalBias;     // Maps to shadowNormalBias
				float shadowStrength; // Maps to shadowStrength
				float shadowDistance; // Maps to shadowDistance

				float atlasX; // AtlasWidth
				float atlasY; // AtlasY,
				float atlasWidth; //  AtlasWidth
			};

			struct SpotLightStruct {
				vec3 position;
				vec3 direction;
				vec3 color;
				float intensity;
				float range;
				float innerAngle; // Cosine of inner cone half-angle
				float outerAngle; // Cosine of outer cone half-angle
				mat4 shadowMatrix;
				float shadowBias;
				float shadowNormalBias;
				float shadowStrength;
				float atlasX;
				float atlasY;
				float atlasWidth;
			};

			#define MAX_SPOT_LIGHTS 8

			uniform SunLightStruct _Sun;
			uniform SpotLightStruct _SpotLights[MAX_SPOT_LIGHTS];
			uniform int _SpotLightCount;

			float SampleShadow(SunLightStruct sun)
			{
				float BIAS_SCALE = 0.001;
				float NORMAL_BIAS_SCALE = 0.05;

                // Perform perspective divide to get NDC coordinates
				vec3 worldPosBiased = worldPos + (normalize(vNormal) * sun.shadowNormalBias * NORMAL_BIAS_SCALE);
                vec4 lightSpacePos = sun.shadowMatrix * vec4(worldPosBiased, 1.0);
                vec3 projCoords = lightSpacePos.xyz / lightSpacePos.w;
                
                // Transform to [0,1] range
                projCoords = projCoords * 0.5 + 0.5;
                
                // Early exit if beyond shadow distance or outside shadow map
                if (projCoords.z > 1.0 || 
                    projCoords.x < 0.0 || projCoords.x > 1.0 || 
                    projCoords.y < 0.0 || projCoords.y > 1.0) {
                    return 0.0;
                }
                
                // Get shadow atlas coordinates
                float AtlasX = sun.atlasX;
                float AtlasY = sun.atlasY;
                float AtlasWidth = sun.atlasWidth;
                
                vec2 atlasCoords;
                atlasCoords.x = AtlasX + (projCoords.x * AtlasWidth);
                atlasCoords.y = AtlasY + (projCoords.y * AtlasWidth);
				
                float atlasSize = prowl_ShadowAtlasSize.x;
                atlasCoords /= atlasSize;
                
                // Get depth from shadow map
                float closestDepth = texture(_ShadowAtlas, atlasCoords.xy).r;
                
                // Get current depth with bias
                float currentDepth = projCoords.z - (sun.shadowBias * BIAS_SCALE);
                
                // Check if fragment is in shadow
                float shadow = currentDepth > closestDepth ? 1.0 : 0.0;//0.0;
                
                //// PCF (Percentage Closer Filtering) for soft shadows
                //vec2 texelSize = vec2(1.0) / prowl_ShadowAtlasSize;
                //float pcfRadius = 1.0;
                //float pcfSamples = 0.0;
                //for(float x = -pcfRadius; x <= pcfRadius; x += 1.0) {
                //    for(float y = -pcfRadius; y <= pcfRadius; y += 1.0) {
                //        vec2 offset = vec2(x, y) * texelSize;
                //        float pcfDepth = texture(_ShadowAtlas, atlasCoords + offset).r; 
                //        shadow += currentDepth > pcfDepth ? 1.0 : 0.0;
				//		pcfSamples += 1.0;
                //    }
                //}
                //shadow /= pcfSamples;
                
                // Apply shadow strength
                shadow *= sun.shadowStrength;
                
                // Fade out shadows at shadow distance
                float distanceFade = 1.0 - clamp(length(worldPos) / sun.shadowDistance, 0.0, 1.0);
                shadow *= distanceFade;
                
                return shadow;
			}

			float SampleSpotLightShadow(SpotLightStruct light)
			{
				float BIAS_SCALE = 0.001;
				float NORMAL_BIAS_SCALE = 0.05;

				// Check if shadows are enabled for this light
				if (light.atlasX < 0.0 || light.shadowStrength <= 0.0) {
					return 0.0;
				}

				// Perform perspective divide to get NDC coordinates
				vec3 worldPosBiased = worldPos + (normalize(vNormal) * light.shadowNormalBias * NORMAL_BIAS_SCALE);
				vec4 lightSpacePos = light.shadowMatrix * vec4(worldPosBiased, 1.0);
				vec3 projCoords = lightSpacePos.xyz / lightSpacePos.w;

				// Transform to [0,1] range
				projCoords = projCoords * 0.5 + 0.5;

				// Early exit if outside shadow map
				if (projCoords.z > 1.0 ||
					projCoords.x < 0.0 || projCoords.x > 1.0 ||
					projCoords.y < 0.0 || projCoords.y > 1.0) {
					return 0.0;
				}

				// Get shadow atlas coordinates
				vec2 atlasCoords;
				atlasCoords.x = light.atlasX + (projCoords.x * light.atlasWidth);
				atlasCoords.y = light.atlasY + (projCoords.y * light.atlasWidth);

				float atlasSize = prowl_ShadowAtlasSize.x;
				atlasCoords /= atlasSize;

				// Get depth from shadow map
				float closestDepth = texture(_ShadowAtlas, atlasCoords.xy).r;

				// Get current depth with bias
				float currentDepth = projCoords.z - (light.shadowBias * BIAS_SCALE);

				// Check if fragment is in shadow
				float shadow = currentDepth > closestDepth ? 1.0 : 0.0;

				// Apply shadow strength
				shadow *= light.shadowStrength;

				return shadow;
			}

			vec3 CalculateDirectionalLight(vec3 normal, vec3 albedo, float metallic, float roughness, float ao)
			{
			    // Constants
			    vec3 lightDir = normalize(_Sun.direction); // Direction from surface to light
			    vec3 viewDir = normalize(-(worldPos - _WorldSpaceCameraPos.xyz));
			    //vec3 viewDir = normalize(-worldPos);
			    vec3 halfDir = normalize(lightDir + viewDir);
			    
			    // Calculate base reflectivity for metals vs non-metals
			    vec3 F0 = vec3(0.04); // Default reflectivity for non-metals at normal incidence
			    F0 = mix(F0, albedo, metallic); // For metals, base reflectivity is tinted by albedo
			    
			    // Calculate light radiance
			    vec3 radiance = _Sun.color * _Sun.intensity;
			    
			    // Cook-Torrance BRDF
			    float NDF = DistributionGGX(normal, halfDir, roughness);
			    float G = GeometrySmith(normal, viewDir, lightDir, roughness);
			    vec3 F = FresnelSchlick(max(dot(halfDir, viewDir), 0.0), F0);
			    
			    // Calculate specular and diffuse components
			    vec3 kS = F; // Energy of light that gets reflected
			    vec3 kD = vec3(1.0) - kS; // Energy of light that gets refracted
			    kD *= 1.0 - metallic; // Metals don't have diffuse lighting
			    
			    // Put it all together
			    float NdotL = max(dot(normal, lightDir), 0.0);
			    
			    // Specular term
			    vec3 numerator = NDF * G * F;
			    float denominator = 4.0 * max(dot(normal, viewDir), 0.0) * NdotL + 0.0001;
			    vec3 specular = numerator / denominator;
			    
                // Calculate shadow factor
                float shadow = SampleShadow(_Sun);
                float shadowFactor = 1.0 - shadow;
                
                // Final lighting contribution with shadow
                vec3 diffuse = kD * albedo / PI;
                return (diffuse + specular) * radiance * NdotL * shadowFactor * ao;
                //return vec3(shadowFactor);
			}

			vec3 CalculateSpotLight(SpotLightStruct light, vec3 normal, vec3 albedo, float metallic, float roughness, float ao)
			{
				// Calculate direction from surface to light
				vec3 lightDir = normalize(light.position - worldPos);
				vec3 viewDir = normalize(-(worldPos - _WorldSpaceCameraPos.xyz));
				vec3 halfDir = normalize(lightDir + viewDir);

				// Calculate distance attenuation
				float distance = length(light.position - worldPos);
				if (distance > light.range) {
					return vec3(0.0);
				}

				// Physical distance attenuation (inverse square law with smoothing)
				float attenuation = clamp(1.0 - pow(distance / light.range, 4.0), 0.0, 1.0);
				attenuation = (attenuation * attenuation) / (distance * distance + 1.0);

				// Calculate spot light cone attenuation
				float theta = dot(lightDir, normalize(-light.direction));
				float epsilon = light.innerAngle - light.outerAngle;
				float spotAttenuation = clamp((theta - light.outerAngle) / epsilon, 0.0, 1.0);

				// Early exit if outside cone
				if (spotAttenuation <= 0.0) {
					return vec3(0.0);
				}

				// Calculate base reflectivity
				vec3 F0 = vec3(0.04);
				F0 = mix(F0, albedo, metallic);

				// Calculate light radiance with attenuation
				vec3 radiance = light.color * light.intensity * attenuation * spotAttenuation;

				// Cook-Torrance BRDF
				float NDF = DistributionGGX(normal, halfDir, roughness);
				float G = GeometrySmith(normal, viewDir, lightDir, roughness);
				vec3 F = FresnelSchlick(max(dot(halfDir, viewDir), 0.0), F0);

				// Calculate specular and diffuse components
				vec3 kS = F;
				vec3 kD = vec3(1.0) - kS;
				kD *= 1.0 - metallic;

				// Put it all together
				float NdotL = max(dot(normal, lightDir), 0.0);

				// Specular term
				vec3 numerator = NDF * G * F;
				float denominator = 4.0 * max(dot(normal, viewDir), 0.0) * NdotL + 0.0001;
				vec3 specular = numerator / denominator;

				// Calculate shadow factor
				float shadow = SampleSpotLightShadow(light);
				float shadowFactor = 1.0 - shadow;

				// Final lighting contribution with shadow
				vec3 diffuse = kD * albedo / PI;
				return (diffuse + specular) * radiance * NdotL * shadowFactor * ao;
			}


            // Generated Normals implementation
            const float normalThreshold = 0.05;
            const float normalClamp = 0.5;
            
            float GetDif(float lOriginalAlbedo, vec2 offsetCoord) {
                float lNearbyAlbedo = length(texture(_MainTex, offsetCoord).rgb);
                
                float dif = lOriginalAlbedo - lNearbyAlbedo;
                
                if (dif > 0.0) dif = max(dif - normalThreshold, 0.0);
                else           dif = min(dif + normalThreshold, 0.0);
                
                return clamp(dif, -normalClamp, normalClamp);
            }
            
            vec3 GenerateNormals(vec3 color, mat3 TBN) {
                // Calculate texture dimensions
                vec2 texSize = vec2(textureSize(_MainTex, 0));
                vec2 texelSize = 1.0 / texSize;
                
                float lOriginalAlbedo = length(color.rgb);
                float normalMult = 1.0;
                
                vec3 normalMap = vec3(0.0, 0.0, 1.0);
                
                // Sample in four directions around current texel
                vec2 offsetCoord = texCoord0 + vec2(0.0, texelSize.y);
                normalMap.y += GetDif(lOriginalAlbedo, offsetCoord);
                
                offsetCoord = texCoord0 + vec2(texelSize.x, 0.0);
                normalMap.x += GetDif(lOriginalAlbedo, offsetCoord);
                
                offsetCoord = texCoord0 + vec2(0.0, -texelSize.y);
                normalMap.y -= GetDif(lOriginalAlbedo, offsetCoord);
                
                offsetCoord = texCoord0 + vec2(-texelSize.x, 0.0);
                normalMap.x -= GetDif(lOriginalAlbedo, offsetCoord);
                
                normalMap.xy *= normalMult;
                normalMap.xy = clamp(normalMap.xy, vec2(-1.0), vec2(1.0));
                
                if (normalMap.xy != vec2(0.0, 0.0)) {
                    return normalize(TBN * normalMap);
                }
                
                return normalize(vNormal);
            }

			void main()
			{
				// Calculate screen-space motion vector
				// Convert positions to NDC space [-1,1]
				vec2 curNDC = (currentPos.xy / currentPos.w) - _CameraJitter;
				vec2 prevNDC = (previousPos.xy / previousPos.w) - _CameraPreviousJitter;
			    gMotionVector = vec4((curNDC - prevNDC) * 0.5, 0.0, 1.0);

				// Albedo
				vec4 albedo = texture(_MainTex, texCoord0) * vColor;

				// Normals
                vec3 worldNormal;
#ifdef HAS_TANGENTS
				// Create tangent to world matrix
				mat3 TBN = mat3(normalize(vTangent), normalize(vBitangent), normalize(vNormal));
				
                
                // Normal mapping with fallback to generated normals
                #ifdef USEGENERATEDNORMALS
                    // Generate normals from albedo texture
                    worldNormal = GenerateNormals(albedo.rgb, TBN);
                #else
                    // Sample the normal map (original approach)
                    vec3 normalMapSample = texture(_NormalTex, texCoord0).rgb;
                    // Convert from [0,1] to [-1,1] range
                    vec3 normalTS = normalMapSample * 2.0 - 1.0;
                    // Transform normal from tangent space to world space
                    worldNormal = normalize(TBN * normalTS);
                #endif
#else
                worldNormal = vNormal;
#endif
                // Transform to view space
                vec3 viewNormal = normalize(mat3(PROWL_MATRIX_V) * worldNormal);
                
                // Output the normal
                gNormal = vec4(viewNormal, 1.0); // Add explicit alpha

				// AO, roughness, metallic
				vec4 surface = texture(_SurfaceTex, texCoord0);
				float ao = 1.0 - surface.r;
				float roughness = surface.g;
				float metallic = surface.b;
				gSurface = vec4(roughness, metallic, 0.0, 1.0); // Add explicit alpha

				// Emission
				vec4 emission = texture(_EmissionTex, texCoord0) * _EmissionIntensity;
    
				// Base color
				vec3 baseColor = albedo.rgb;
				baseColor.rgb = GammaToLinearSpace(baseColor.rgb);
				
				// Calculate lighting
				vec3 lighting = CalculateDirectionalLight(worldNormal, baseColor, metallic, roughness, ao);
				lighting += baseColor.rgb * CalculateAmbient(worldNormal);

				// Add spot lights
				for (int i = 0; i < _SpotLightCount && i < MAX_SPOT_LIGHTS; i++) {
					lighting += CalculateSpotLight(_SpotLights[i], worldNormal, baseColor, metallic, roughness, ao);
				}

				// Add emission
				lighting += emission.rgb * 1.0;
				
				// Final output
				gAlbedo = vec4(lighting, 1.0);
				
				// Apply fog
				gAlbedo.rgb = ApplyFog(fogCoord, gAlbedo.rgb);
			}
		}
	ENDGLSL
}

Pass "StandardMotionVector"
{
    Tags { "RenderOrder" = "DepthOnly" }

    // Rasterizer culling mode
    Cull Back

	GLSLPROGRAM
		
		Vertex
		{
            #include "Fragment"

			layout (location = 0) in vec3 vertexPosition;
			
			void main()
			{
				gl_Position = PROWL_MATRIX_MVP * vec4(vertexPosition, 1.0);
			}
		}

		Fragment
		{
            #include "Fragment"

			void main()
			{
			}
		}
	ENDGLSL
}

Pass "StandardShadow"
{
    Tags { "LightMode" = "ShadowCaster" }

    // Rasterizer culling mode
    Cull Back

	GLSLPROGRAM
		
		Vertex
		{
            #include "Fragment"

			layout (location = 0) in vec3 vertexPosition;
			
			void main()
			{
				gl_Position = PROWL_MATRIX_MVP * vec4(vertexPosition, 1.0);
			}
		}

		Fragment
		{
            #include "Fragment"

			void main()
			{
			}
		}
	ENDGLSL
}