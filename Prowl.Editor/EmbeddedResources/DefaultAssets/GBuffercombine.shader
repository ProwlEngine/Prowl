Shader "Default/GBuffer"

Pass 0
{
	Vertex
	{
		in vec3 vertexPosition;

		void main() 
		{
			gl_Position =vec4(vertexPosition, 1.0);
		}
	}

	Fragment
	{
		uniform vec2 Resolution;

		uniform sampler2D gAlbedoAO; // Diffuse
		uniform sampler2D gLighting; // Lighting
		
		layout(location = 0) out vec4 OutputColor;
		
		void main()
		{
			vec2 texCoords = gl_FragCoord.xy / Resolution;
			vec4 albedoAO = texture(gAlbedoAO, texCoords);
			float AO = albedoAO.w;
			vec3 diffuseColor = albedoAO.rgb * 0.01;
			vec3 lightingColor = texture(gLighting, texCoords).rgb;
		
			vec3 color = diffuseColor + (lightingColor * AO);

			OutputColor = vec4(color, 1.0);
		}
	}
}