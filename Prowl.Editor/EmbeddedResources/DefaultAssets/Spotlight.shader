Shader "Default/SpotLight"

Pass 0
{
	DepthTest Off
	DepthWrite Off
	// DepthMode Less
	Blend On
	BlendSrc SrcAlpha
	BlendDst One
	BlendEquation FuncAdd
	Cull Off
	// Winding CW
 
	Vertex
	{
		// Input vertex attributes
		in vec3 vertexPosition;

		// Input uniform values
		uniform mat4 mvp;

		// ----------------------------------------------------------------------------
		void main()
		{
			gl_Position = mvp * vec4(vertexPosition, 1.0);
		}
	}

	Fragment
	{
		layout(location = 0) out vec4 gBuffer_lighting;
		
		//uniform mat4 viewMatrixInv;
		uniform mat4 matProjection;
		
		uniform vec2 Resolution;

		uniform vec3 LightPosition;
		uniform vec3 LightDirection;

		uniform float LightDistance;
		uniform float Angle;
		uniform float Falloff;

		uniform vec4 LightColor;
		uniform float LightIntensity;
		
		uniform sampler2D gAlbedoAO; // Albedo & Roughness
		uniform sampler2D gNormalMetallic; // Normal & Metalness
		uniform sampler2D gPositionRoughness; // Depth
		
		#include "PBR"
		
		// ----------------------------------------------------------------------------
		void main()
		{
			vec2 TexCoords = gl_FragCoord.xy / Resolution;
		
			vec4 gPosRough = textureLod(gPositionRoughness, TexCoords, 0);
			vec3 gPos = gPosRough.rgb;
			if(gPos == vec3(0, 0, 0)) discard;
		
			vec3 gAlbedo = textureLod(gAlbedoAO, TexCoords, 0).rgb;
			vec4 gNormalMetal = textureLod(gNormalMetallic, TexCoords, 0);
			vec3 gNormal = gNormalMetal.rgb;
			float gMetallic = gNormalMetal.a;
			float gRoughness = gPosRough.a;

			// calculate reflectance at normal incidence; if dia-electric (like plastic) use F0 
			// of 0.04 and if it's a metal, use the albedo color as F0 (metallic workflow)    
			vec3 F0 = vec3(0.04); 
			F0 = mix(F0, gAlbedo, gMetallic);
			vec3 N = normalize(gNormal);
			vec3 V = normalize(-gPos);

			vec3 L = normalize(LightPosition - gPos);
			vec3 H = normalize(V + L);
			float theta = dot(L, normalize(-LightDirection));
			
			// attenuation
			float distance = length(LightPosition - gPos);
			float falloff  = (clamp(1.0 - pow(distance / LightDistance, 4), 0.0, 1.0) * clamp(1.0 - pow(distance / LightDistance, 4), 0.0, 1.0)) / (distance * distance + 1.0);
			
			// cone attenuation
			float epsilon   = Angle - Falloff;
			float coneAttenuation = clamp((theta - Falloff) / epsilon, 0.0, 1.0);  

 			vec3 radiance = LightColor.rgb * LightIntensity * falloff * coneAttenuation;

			// cook-torrance brdf
			float NDF = DistributionGGX(N, H, gRoughness);        
			float G   = GeometrySmith(N, V, L, gRoughness);      
			vec3 F    = FresnelSchlick(max(dot(H, V), 0.0), F0);
			
			vec3 nominator    = NDF * G * F;
			float denominator = 4 * max(dot(N, V), 0.0) * max(dot(N, L), 0.0) + 0.001; 
			vec3 specular     = (nominator / denominator) * coneAttenuation;

			vec3 kS = F;
			vec3 kD = vec3(1.0) - kS;
			kD *= 1.0 - gMetallic;     
			    
			// add to outgoing radiance Lo
			float NdotL = max(dot(N, L), 0.0);                
			vec3 color = (kD * gAlbedo / PI + specular) * radiance * NdotL;

			gBuffer_lighting = vec4(color, 1.0);

			vec4 depth = matProjection * vec4(gPos, 1.0);
			gl_FragDepth = depth.z / depth.w;
		}
	}
}