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
		uniform sampler2D gEmission; // Emission
		
		layout(location = 0) out vec4 OutputColor;
		
		void main()
		{
			vec2 texCoords = gl_FragCoord.xy / Resolution;
			vec4 albedoAO = texture(gAlbedoAO, texCoords);
			//vec3 diffuseColor = albedoAO.rgb * 0.01;
			vec3 lightingColor = texture(gLighting, texCoords).rgb;
			vec3 emissionColor = texture(gEmission, texCoords).rgb;
			// Apply AO onto the lightingColor
			// AO comes in as 0-1, 0 being no AO, 1 being full AO
			lightingColor *= (1.0 - albedoAO.w);
		
			//vec3 color = diffuseColor + (lightingColor);
			vec3 color = lightingColor + (emissionColor * 2.0);

			OutputColor = vec4(color, 1.0);
		}
	}
}