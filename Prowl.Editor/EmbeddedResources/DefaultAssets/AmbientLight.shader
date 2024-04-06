Shader "Default/AmbientLight"

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
		in vec3 vertexPosition;
		in vec2 vertexTexCoord;
		
		out vec2 TexCoords;

		void main() 
		{
			gl_Position =vec4(vertexPosition, 1.0);
			TexCoords = vertexTexCoord;
		}
	}

	Fragment
	{
		layout(location = 0) out vec4 gBuffer_lighting;
		
		uniform mat4 matView;
		
		in vec2 TexCoords;
		
		uniform vec4 SkyColor;
		uniform vec4 GroundColor;
		uniform float SkyIntensity;
		uniform float GroundIntensity;
		
		uniform sampler2D gAlbedoAO; // Albedo & Roughness
		uniform sampler2D gNormalMetallic; // Normal & Metalness
		uniform sampler2D gPositionRoughness; // Depth
		
		// ----------------------------------------------------------------------------

		void main()
		{
			vec4 gPosRough = textureLod(gPositionRoughness, TexCoords, 0);
			if(gPosRough.rgb == vec3(0, 0, 0)) discard;
		
			vec3 gAlbedo = textureLod(gAlbedoAO, TexCoords, 0).rgb;

			vec4 gNormalMetal = textureLod(gNormalMetallic, TexCoords, 0);
			vec3 gNormal = gNormalMetal.rgb; // in View space
			
			// Obtain the local up vector in view space
			vec3 upVector = (matView * vec4(0.0, 1.0, 0.0, 0.0)).xyz;

			// Calculate hemisphere/ambient lighting
			float NdotUp = max(0.0, dot(gNormal, upVector));

			// Interpolate between SkyColor and GroundColor based on NdotUp
			vec3 ambientColor = mix(GroundColor.rgb * GroundIntensity, SkyColor.rgb * SkyIntensity, NdotUp);

			gBuffer_lighting = vec4(gAlbedo * ambientColor, 1.0);
		}

	}
}