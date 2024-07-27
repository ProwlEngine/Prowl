Shader "Default/TestShader"

Pass "TestShader"
{
    Tags { "RenderOrder" = "Opaque" }

    // Rasterizer culling mode
    Cull Back

	Inputs
	{
		VertexInput 
        {
            Position // Input location 0
            UV0 // Input location 1
            Normals // Input location 2
            Tangents // Input location 3
            Colors // Input location 4
        }
        
        // Set 0
        Set
        {
            // Binding 0
            Buffer DefaultUniforms
            {
                Mat_V Matrix4x4
                Mat_P Matrix4x4
                Mat_ObjectToWorld Matrix4x4
                Mat_WorldToObject Matrix4x4
                Mat_MVP Matrix4x4
				Time Float
            }

			SampledTexture _AlbedoTex
			SampledTexture _NormalTex
			SampledTexture _SurfaceTex

            Buffer StandardUniforms
            {
				_MainColor Vector4 // color
				_AlphaClip Float
				_ObjectID Float
				
				_LightCount Float
            }
			
			StructuredBuffer _Lights
			
			SampledTexture _ShadowAtlas
        }
	}

	PROGRAM VERTEX
		layout(location = 0) in vec3 vertexPosition;
		layout(location = 1) in vec2 vertexTexCoord;
		layout(location = 2) in vec3 vertexNormal;
		layout(location = 3) in vec3 vertexTangent;
		layout(location = 4) in vec4 vertexColors;
		
		layout(set = 0, binding = 0, std140) uniform DefaultUniforms
		{
			mat4 Mat_V;
			mat4 Mat_P;
			mat4 Mat_ObjectToWorld;
			mat4 Mat_WorldToObject;
			mat4 Mat_MVP;
			float Time;
		};

		layout(location = 0) out vec2 TexCoords;
		layout(location = 1) out vec4 VertColor;
		layout(location = 2) out vec3 FragPos;
		layout(location = 3) out mat3 TBN;
		layout(location = 6) out vec3 vNorm;
		layout(location = 7) out vec3 VertPos;
		
		void main() 
		{
		 	vec4 viewPos = Mat_V * Mat_ObjectToWorld * vec4(vertexPosition, 1.0);
		    FragPos = viewPos.xyz; 
			VertPos = (Mat_ObjectToWorld * vec4(vertexPosition, 1.0)).xyz;

			gl_Position = Mat_MVP * vec4(vertexPosition, 1.0);
			
			TexCoords = vertexTexCoord;
			VertColor = vertexColors;
			vNorm = vertexNormal;
			

			mat3 normalMatrix = transpose(inverse(mat3(Mat_ObjectToWorld)));
			
			vec3 T = normalize(vec3(Mat_ObjectToWorld * vec4(vertexTangent, 0.0)));
			vec3 B = normalize(vec3(Mat_ObjectToWorld * vec4(cross(vertexNormal, vertexTangent), 0.0)));
			vec3 N = normalize(vec3(Mat_ObjectToWorld * vec4(vertexNormal, 0.0)));
		    TBN = mat3(T, B, N);
		}
	ENDPROGRAM

	PROGRAM FRAGMENT	
		layout(location = 0) in vec2 TexCoords;
		layout(location = 1) in vec4 VertColor;
		layout(location = 2) in vec3 FragPos;
		layout(location = 3) in mat3 TBN;
		layout(location = 6) in vec3 vNorm;
		layout(location = 7) in vec3 VertPos;

		layout(location = 0) out vec4 Albedo;
		layout(location = 1) out vec3 Normal;
		layout(location = 2) out vec3 AoRoughnessMetallic;
		layout(location = 3) out uint ObjectID;
		
		layout(set = 0, binding = 0, std140) uniform DefaultUniforms
		{
			mat4 Mat_V;
			mat4 Mat_P;
			mat4 Mat_ObjectToWorld;
			mat4 Mat_WorldToObject;
			mat4 Mat_MVP;
			float Time;
		};

		layout(set = 0, binding = 1) uniform texture2D _AlbedoTex;
		layout(set = 0, binding = 2) uniform sampler _AlbedoTexSampler;

		layout(set = 0, binding = 3) uniform texture2D _NormalTex;
		layout(set = 0, binding = 4) uniform sampler _NormalTexSampler;

		layout(set = 0, binding = 5) uniform texture2D _SurfaceTex;
		layout(set = 0, binding = 6) uniform sampler _SurfaceTexSampler;
		
		
		layout(set = 0, binding = 7, std140) uniform StandardUniforms
		{
			vec4 _MainColor; // color
			float _AlphaClip;
			float _ObjectID;
			float _LightCount;
		};
		
		struct gpuLight 
		{
			vec4 PositionType;
			vec4 DirectionRange;
			uint Color;
			float Intensity;
			vec2 SpotData;
			vec4 ShadowData;
			mat4 ShadowMatrix;
			int AtlasX;
			int AtlasY;
			int AtlasWidth;
			int Padding;
		};
		
		layout(set = 0, binding = 8, std140) buffer _Lights
		{
			gpuLight allLights[];
		};

		layout(set = 0, binding = 9) uniform texture2D _ShadowAtlas;
		layout(set = 0, binding = 10) uniform sampler _ShadowAtlasSampler;
		
		vec4 unpackAndConvertRGBA(uint packed)
		{
			uvec4 color;
			color.r = packed & 0xFF;
			color.g = (packed >> 8) & 0xFF;
			color.b = (packed >> 16) & 0xFF;
			color.a = (packed >> 24) & 0xFF;
			
			// Convert to float and normalize to [0, 1] range
			vec4 normalizedColor = vec4(color) / 255.0;
			return normalizedColor;
		}
		
		#include "Prowl"
		#include "PBR"
		
		void CookTorrance(vec3 N, vec3 H, vec3 L, vec3 V, vec3 F0, float roughness, float metallic, out vec3 kD, out vec3 specular)
		{
			float NDF = DistributionGGX(N, H, roughness);        
			float G   = GeometrySmith(N, V, L, roughness);  
			vec3 F    = FresnelSchlick(max(dot(H, V), 0.0), F0);
			
			vec3 nominator    = NDF * G * F;
			float denominator = 4 * max(dot(N, V), 0.0) * max(dot(N, L), 0.0) + 0.001; 
			specular = nominator / denominator;
			
			vec3 kS = F;
			kD = vec3(1.0) - kS;
			kD *= 1.0 - metallic;  
		}


		float ShadowCalculation(vec4 fragPosLightSpace, gpuLight light)
		{
			// perform perspective divide
			vec3 projCoords = fragPosLightSpace.xyz / fragPosLightSpace.w;
			// transform to [0,1] range
			projCoords = projCoords * 0.5 + 0.5;
			
			if (projCoords.x > 1.0 || projCoords.y > 1.0 || projCoords.z > 1.0 || projCoords.x < 0.0 || projCoords.y < 0.0 || projCoords.z < 0.0)
			    return 0.0;
			
			float AtlasX = float(light.AtlasX);
			float AtlasY = float(light.AtlasY);
			float AtlasWidth = float(light.AtlasWidth);
			
			// convert projCoords.xy to atlas coordinates
			vec2 atlasCoords;
			atlasCoords.x = AtlasX + (projCoords.x * AtlasWidth);
			atlasCoords.y = AtlasY + ((1.0 - projCoords.y) * AtlasWidth);
			
			// normalize the atlas coordinates to [0,1] range for the shadow atlas texture lookup
			atlasCoords /= vec2(4096.0);
			
			atlasCoords.y = 1.0 - atlasCoords.y;
			
			// get closest depth value from light's perspective (using [0,1] range fragPosLight as coords)
			float closestDepth = texture(sampler2D(_ShadowAtlas, _ShadowAtlasSampler), atlasCoords.xy).r; 
			// get depth of current fragment from light's perspective
			float currentDepth = projCoords.z;
			// check whether current frag pos is in shadow
			
			float shadow = (currentDepth - light.ShadowData.z) > closestDepth  ? 1.0 : 0.0;
		
			return shadow;
		}  
		
		void main()
		{
			// Albedo & Cutout
			vec4 baseColor = texture(sampler2D(_AlbedoTex, _AlbedoTexSampler), TexCoords);// * _MainColor.rgb;
			if(baseColor.w < _AlphaClip) discard;
			baseColor.rgb = pow(baseColor.xyz, vec3(2.2));

			// Normal
			vec3 normal = texture(sampler2D(_NormalTex, _NormalTexSampler), TexCoords).rgb;
			normal = normal * 2.0 - 1.0;   
			normal = normalize(TBN * normal); 
			Normal = (Mat_V * vec4(normal, 0)).rgb;

			// AO, Roughness, Metallic
			vec3 surface = texture(sampler2D(_SurfaceTex, _SurfaceTexSampler), TexCoords).rgb;
			AoRoughnessMetallic = vec3(surface.r, surface.g, surface.b);

			// Object ID
			ObjectID = uint(_ObjectID);
			
			
			// Calculate Lighting
			// AO: surface.r
			// Roughness: surface.g
			// Metallic: surface.b
			
			//http://www.jp.square-enix.com/tech/library/pdf/ImprovedGeometricSpecularAA.pdf - based on Godot's implementation
			float roughness2 = surface.g * surface.g;
			float limiterStrength = 0.2;
			float limiterClamp = 0.18;
			vec3 dndu = dFdx(Normal), dndv = dFdx(Normal);
			float variance = limiterStrength * (dot(dndu, dndu) + dot(dndv, dndv));
			float kernelRoughness2 = min(2.0 * variance, limiterClamp); //limit effect
			float filteredRoughness2 = min(1.0, roughness2 + kernelRoughness2);
			surface.g = sqrt(filteredRoughness2);
			
			// calculate reflectance at normal incidence; if dia-electric (like plastic) use F0 
			// of 0.04 and if it's a metal, use the albedo color as F0 (metallic workflow)    
			vec3 F0 = vec3(0.04); 
			F0 = mix(F0, baseColor.rgb, surface.b);
			vec3 N = normalize(Normal);
			vec3 V = normalize(-FragPos);
			
			vec3 lighting = vec3(0.0, 0.0, 0.0);
			float ambientStrength = 0.0;
			for(int i = 0; i < _LightCount; i++)
			{
				gpuLight light = allLights[i];
				vec3 lightColor = unpackAndConvertRGBA(light.Color).rgb;
				float intensity = light.Intensity;
					
				if(light.PositionType.w == 0.0) // Directional Light
				{
					vec3 L = normalize(-(Mat_V * vec4(light.DirectionRange.xyz, 0)).rgb);
					vec3 H = normalize(V + L);
					
					vec3 kD;
					vec3 specular;
					CookTorrance(N, H, L, V, F0, surface.g, surface.b, kD, specular); 
					
					// shadows
					vec4 fragPosLightSpace = light.ShadowMatrix * vec4(VertPos + (normal * light.ShadowData.w), 1.0);
					float shadow = ShadowCalculation(fragPosLightSpace, light);
					
					// add to outgoing radiance Lo
					vec3 radiance = lightColor * intensity;   
					float NdotL = max(dot(N, L), 0.0);                
					vec3 color = ((kD * baseColor.rgb) / PI + specular) * radiance * (1.0 - shadow) * NdotL;
					//vec3 color = ((kD * baseColor.rgb) / PI + specular) * radiance * NdotL;
					
					// Ambient Lighting
					ambientStrength += light.SpotData.x;
					
					lighting += color;
				}
				else if(light.PositionType.w == 1.0) // Point Light
				{
					float radius = light.DirectionRange.w;
					
					vec3 lightPos = (Mat_V * vec4(light.PositionType.xyz, 1)).xyz;
					vec3 L = normalize(lightPos - FragPos);
					vec3 H = normalize(V + L);
					
					vec3 kD;
					vec3 specular;
					CookTorrance(N, H, L, V, F0, surface.g, surface.b, kD, specular); 
					
					// attenuation
					float distance = length(lightPos - FragPos);
					float falloff  = (clamp(1.0 - pow(distance / radius, 4), 0.0, 1.0) * clamp(1.0 - pow(distance / radius, 4), 0.0, 1.0)) / (distance * distance + 1.0);
					vec3 radiance  = lightColor * intensity * falloff;
			    
					// add to outgoing radiance Lo
					float NdotL = max(dot(N, L), 0.0);                
					vec3 color = (kD * baseColor.rgb / PI + specular) * radiance * NdotL;
					
					lighting += color; 
				}
				else // Spot Light
				{
					vec3 lightPos = (Mat_V * vec4(light.PositionType.xyz, 1)).xyz;
					vec3 L = normalize(lightPos - FragPos);
					vec3 H = normalize(V + L);
					float theta = dot(L, normalize(-(Mat_V * vec4(light.DirectionRange.xyz, 0)).rgb));
			
					// attenuation
					float radius = light.DirectionRange.w;
					float lightAngle = light.SpotData.x;
					float lightFalloff = light.SpotData.y;
					
					float distance = length(lightPos - FragPos);
					float falloff  = (clamp(1.0 - pow(distance / radius, 4), 0.0, 1.0) * clamp(1.0 - pow(distance / radius, 4), 0.0, 1.0)) / (distance * distance + 1.0);
					
					// cone attenuation
					float epsilon   = lightAngle - lightFalloff;
					float coneAttenuation = clamp((theta - lightFalloff) / epsilon, 0.0, 1.0);  
					
					vec3 radiance = lightColor * intensity * falloff * coneAttenuation;
					
					vec3 kD;
					vec3 specular;
					CookTorrance(N, H, L, V, F0, surface.g, surface.b, kD, specular); 
					specular *= vec3(coneAttenuation);
					
					// shadows
					vec4 fragPosLightSpace = light.ShadowMatrix * vec4(VertPos + (normal * light.ShadowData.w), 1.0);
					float shadow = ShadowCalculation(fragPosLightSpace, light);

					// add to outgoing radiance Lo
					float NdotL = max(dot(N, L), 0.0);        
					vec3 color = ((kD * baseColor.rgb) / PI + specular) * radiance * (1.0 - shadow) * NdotL;        
					//vec3 color = (kD * baseColor.rgb / PI + specular) * radiance * NdotL;
					
					lighting += color;
				}
			}
			
			lighting *= (1.0 - surface.r);
			
			baseColor.rgb *= ambientStrength;
			baseColor.rgb += lighting;
			//baseColor.rgb = lighting;
			
			//baseColor.rgb = pow(baseColor.xyz, vec3(1.0/2.2));
			
			Albedo.rgb = baseColor.xyz;
			Albedo.a = 1.0;
			
		}
	ENDPROGRAM
}

Pass "Shadow"
{
    Tags { "RenderOrder" = "Shadow" }

    // Rasterizer culling mode
    Cull None

	Inputs
	{
		VertexInput 
        {
            Position // Input location 0
            UV0 // Input location 1
        }
        
        // Set 0
        Set
        {
            // Binding 0
            Buffer DefaultUniforms
            {
                Mat_V Matrix4x4
                Mat_P Matrix4x4
                Mat_ObjectToWorld Matrix4x4
                Mat_WorldToObject Matrix4x4
                Mat_MVP Matrix4x4
				Time Float
            }

			SampledTexture _AlbedoTex
        }
	}

	PROGRAM VERTEX
		layout(location = 0) in vec3 vertexPosition;
		layout(location = 1) in vec2 vertexTexCoord;
		
		layout(set = 0, binding = 0, std140) uniform DefaultUniforms
		{
			mat4 Mat_V;
			mat4 Mat_P;
			mat4 Mat_ObjectToWorld;
			mat4 Mat_WorldToObject;
			mat4 Mat_MVP;
			float Time;
		};

		layout(location = 0) out vec2 TexCoords;
		layout(location = 1) out vec3 VertPos;
		
		void main() 
		{
			gl_Position = Mat_MVP * vec4(vertexPosition, 1.0);
			
			VertPos = (Mat_ObjectToWorld * vec4(vertexPosition, 1.0)).xyz;
			
			TexCoords = vertexTexCoord;
		}
	ENDPROGRAM

	PROGRAM FRAGMENT	
		layout(location = 0) in vec2 TexCoords;
		layout(location = 1) in vec3 VertPos;
		
		layout(set = 0, binding = 0, std140) uniform DefaultUniforms
		{
			mat4 Mat_V;
			mat4 Mat_P;
			mat4 Mat_ObjectToWorld;
			mat4 Mat_WorldToObject;
			mat4 Mat_MVP;
			float Time;
		};
		
		layout(set = 0, binding = 1) uniform texture2D _AlbedoTex;
		layout(set = 0, binding = 2) uniform sampler _AlbedoTexSampler;
		
		void main()
		{
			// Albedo & Cutout
			//vec4 baseColor = texture(sampler2D(_AlbedoTex, _AlbedoTexSampler), TexCoords);// * _MainColor.rgb;
			//if(baseColor.w < 0.8) discard;
			
			vec4 fragPosLightSpace = (Mat_P * Mat_V) * vec4(VertPos, 1.0);
			vec3 projCoords = fragPosLightSpace.xyz / fragPosLightSpace.w;
			projCoords = projCoords * 0.5 + 0.5;
			gl_FragDepth = projCoords.z;
		}
	ENDPROGRAM
}