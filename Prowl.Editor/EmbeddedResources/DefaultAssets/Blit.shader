Shader "Default/Blit"

Pass "Blit"
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
			SampledTexture _Texture
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

		layout(set = 0, binding = 0) uniform texture2D _Texture;
		layout(set = 0, binding = 1) uniform sampler _TextureSampler;

		void main()
		{
			vec3 baseColor = texture(sampler2D(_Texture, _TextureSampler), TexCoords).rgb;
			OutputColor = vec4(baseColor, 1.0);
		}
	ENDPROGRAM
}