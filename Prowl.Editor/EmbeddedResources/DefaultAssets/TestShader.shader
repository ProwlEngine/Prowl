Shader "Default/TestShader"

Pass "TestShader"
{
    Tags { "RenderOrder" = "Opaque" }

    // Rasterizer culling mode
    Cull None

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
		
		void main() 
		{
		 	vec4 viewPos = Mat_V * Mat_ObjectToWorld * vec4(vertexPosition, 1.0);
		    FragPos = viewPos.xyz; 

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
			vec4 PositionType; // 4 float - 16 bytes
			vec4 DirectionRange; // 4 float - 16 bytes - 32 bytes
			uint Color; // 1 uint - 4 bytes - 36 bytes
			float Intensity; // 1 float - 4 bytes - 40 bytes
			vec2 SpotData; // 2 float - 8 bytes - 48 bytes
			vec4 ShadowData; // 4 float - 16 bytes - 64 bytes
		};
		
		layout(set = 0, binding = 8, std140) buffer _Lights
		{
			gpuLight allLights[];
		};
		
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
					//vec4 fragPosLightSpace = matCamViewInverse * vec4(FragPos + (N * u_NormalBias), 1);
					//float shadow = ShadowCalculation(fragPosLightSpace.xyz, FragPos, N, L);
						
					// add to outgoing radiance Lo
					vec3 radiance = lightColor * intensity;   
					float NdotL = max(dot(N, L), 0.0);                
					//vec3 color = ((kD * baseColor.rgb) / PI + specular) * radiance * (1.0 - shadow) * NdotL;
					vec3 color = ((kD * baseColor.rgb) / PI + specular) * radiance * NdotL;
					
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
					
					// add to outgoing radiance Lo
					float NdotL = max(dot(N, L), 0.0);                
					vec3 color = (kD * baseColor.rgb / PI + specular) * radiance * NdotL;
					
					lighting += color;
				}
			}
			
			lighting *= (1.0 - surface.r);
			
			baseColor.rgb *= ambientStrength;
			baseColor.rgb += lighting;
			
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
    Cull Front

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

            Buffer StandardUniforms
            {
				_AlphaClip Float
            }
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
		
		void main() 
		{
		 	vec4 viewPos = Mat_V * Mat_ObjectToWorld * vec4(vertexPosition, 1.0);
		    FragPos = viewPos.xyz; 

			gl_Position = Mat_MVP * vec4(vertexPosition, 1.0);
			
			TexCoords = vertexTexCoord;
			VertColor = vertexColors;

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
		
		layout (location = 0) out float fragmentdepth;
		
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
		
		
		layout(set = 0, binding = 3, std140) uniform StandardUniforms
		{
			float _AlphaClip;
		};
		
		void main()
		{
			// Albedo & Cutout
			vec4 baseColor = texture(sampler2D(_AlbedoTex, _AlbedoTexSampler), TexCoords);// * _MainColor.rgb;
			if(baseColor.w < _AlphaClip) discard;
		}
	ENDPROGRAM
}