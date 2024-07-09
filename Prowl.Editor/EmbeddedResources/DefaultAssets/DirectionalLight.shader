Shader "Default/DirectionalLight"

Pass "DirectionalLight"
{
    Tags { "RenderOrder" = "Lighting" }

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
            Buffer LightingUniforms
            {
				_LightDirection Vector3
				_LightColor Vector3
				_LightIntensity Float
			}

            Buffer DefaultUniforms
            {
                Mat_V Matrix4x4
                Mat_P Matrix4x4
                Mat_ObjectToWorld Matrix4x4
                Mat_WorldToObject Matrix4x4
                Mat_MVP Matrix4x4
				Time Float
            }

			SampledTexture Camera_Albedo
			SampledTexture Camera_Position
			SampledTexture Camera_Normal
			SampledTexture Camera_Surface
			SampledTexture Camera_Depth
        }
	}

	PROGRAM VERTEX
		layout(location = 0) in vec3 vertexPosition;
		layout(location = 1) in vec2 vertexTexCoord;
		
		layout(location = 0) out vec2 TexCoords;
		
		void main() 
		{
			gl_Position = vec4(vertexPosition, 1.0);
			
			TexCoords = vertexTexCoord;
		}
	ENDPROGRAM

	PROGRAM FRAGMENT	
		layout(location = 0) in vec2 TexCoords;

		layout(location = 0) out vec4 result;

		layout(set = 0, binding = 0, std140) uniform LightingUniforms
		{
			vec3 _LightDirection;
			vec3 _LightColor;
			float _LightIntensity;
		};
		
		layout(set = 0, binding = 1, std140) uniform DefaultUniforms
		{
			mat4 Mat_V;
			mat4 Mat_P;
			mat4 Mat_ObjectToWorld;
			mat4 Mat_WorldToObject;
			mat4 Mat_MVP;
			float Time;
		};

		layout(set = 0, binding = 2) uniform texture2D Camera_Albedo;
		layout(set = 0, binding = 3) uniform sampler Camera_AlbedoSampler;

		layout(set = 0, binding = 4) uniform texture2D Camera_Position;
		layout(set = 0, binding = 5) uniform sampler Camera_PositionSampler;

		layout(set = 0, binding = 6) uniform texture2D Camera_Normal;
		layout(set = 0, binding = 7) uniform sampler Camera_NormalSampler;

		layout(set = 0, binding = 8) uniform texture2D Camera_Surface;
		layout(set = 0, binding = 9) uniform sampler Camera_SurfaceSampler;

		layout(set = 0, binding = 10) uniform texture2D Camera_Depth;
		layout(set = 0, binding = 11) uniform sampler Camera_DepthSampler;
		
		#include "PBR"

		void main()
		{
			float depth = texture(sampler2D(Camera_Depth, Camera_DepthSampler), TexCoords).r;
			if(depth >= 1.0) discard;

			vec3 gPos = texture(sampler2D(Camera_Position, Camera_PositionSampler), TexCoords).xyz;
			vec3 gAlbedo = texture(sampler2D(Camera_Albedo, Camera_AlbedoSampler), TexCoords).rgb;
			vec3 gNormal = texture(sampler2D(Camera_Normal, Camera_NormalSampler), TexCoords).xyz;
			vec3 gSurface = texture(sampler2D(Camera_Surface, Camera_SurfaceSampler), TexCoords).rgb; // AO, Roughness and Metallic
			float gMetallic = gSurface.g;
			float gRoughness = gSurface.b;

			// calculate reflectance at normal incidence; if dia-electric (like plastic) use F0 
			// of 0.04 and if it's a metal, use the albedo color as F0 (metallic workflow)    
			vec3 F0 = vec3(0.04); 
			F0 = mix(F0, gAlbedo, gMetallic);
			vec3 N = normalize(gNormal);
			vec3 V = normalize(-gPos);

			
			vec3 L = normalize(-(Mat_V * vec4(_LightDirection, 0.0)).xyz);
			vec3 H = normalize(V + L);

			vec3 radiance = _LightColor.rgb * _LightIntensity;    
			
			// cook-torrance brdf
			float NDF = DistributionGGX(N, H, gRoughness);        
			float G   = GeometrySmith(N, V, L, gRoughness);  
			vec3 F    = FresnelSchlick(max(dot(H, V), 0.0), F0);
			
			vec3 nominator    = NDF * G * F;
			float denominator = 4 * max(dot(N, V), 0.0) * max(dot(N, L), 0.0) + 0.001; 
			vec3 specular     = nominator / denominator;

			vec3 kS = F;
			vec3 kD = vec3(1.0) - kS;
			kD *= 1.0 - gMetallic;     

			// shadows
			//vec4 fragPosLightSpace = matCamViewInverse * vec4(gPos + (N * u_NormalBias), 1);
			//float shadow = ShadowCalculation(fragPosLightSpace.xyz, gPos, N, L);
			    
			// add to outgoing radiance Lo
			float NdotL = max(dot(N, L), 0.0);                
			//vec3 color = ((kD * gAlbedo) / PI + specular) * radiance * (1.0 - shadow) * NdotL;
			vec3 color = ((kD * gAlbedo) / PI + specular) * radiance * NdotL;

			result = vec4(color, 1.0);
		}
	ENDPROGRAM
}