Shader "Default/Screen"

Pass "Screen"
{
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
			SampledTexture _AlbedoTex
			SampledTexture _LightTex
			SampledTexture Camera_Surface
			SampledTexture Camera_Normal
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
		layout(location = 0) out vec4 OutputColor;

		layout(set = 0, binding = 0) uniform texture2D _AlbedoTex;
		layout(set = 0, binding = 1) uniform sampler _AlbedoTexSampler;

		layout(set = 0, binding = 2) uniform texture2D _LightTex;
		layout(set = 0, binding = 3) uniform sampler _LightTexSampler;

		layout(set = 0, binding = 4) uniform texture2D Camera_Surface;
		layout(set = 0, binding = 5) uniform sampler Camera_SurfaceSampler;

		void main()
		{
			vec3 baseColor = texture(sampler2D(_AlbedoTex, _AlbedoTexSampler), TexCoords).rgb * 0.01;
			float ao = texture(sampler2D(Camera_Surface, Camera_SurfaceSampler), TexCoords).r; // AO, Roughness and Metallic
			vec3 lightingColor = texture(sampler2D(_LightTex, _LightTexSampler), TexCoords).rgb;

			// Apply AO onto the lightingColor
			// AO comes in as 0-1, 0 being no AO, 1 being full AO
			lightingColor *= (1.0 - ao);
			
			vec3 color = baseColor + (lightingColor);
			
			color.rgb = pow(color.xyz, vec3(1.0/2.2));

			OutputColor = vec4(color, 1.0);
		}
	ENDPROGRAM
}