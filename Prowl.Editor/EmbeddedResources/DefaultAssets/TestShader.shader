Shader "Default/TestShader"

Pass "TestShader"
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

			SampledTexture _MainTex
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
		
		void main() 
		{
			gl_Position = Mat_MVP * vec4(vertexPosition, 1.0);
			
			TexCoords = vertexTexCoord;
		}
	ENDPROGRAM

	PROGRAM FRAGMENT	
		layout(location = 0) in vec2 TexCoords;
		layout(location = 0) out vec4 OutputColor;

		layout(set = 0, binding = 1) uniform texture2D _MainTex;
		layout(set = 0, binding = 2) uniform sampler _MainTexSampler;

		void main()
		{
			OutputColor =  texture(sampler2D(_MainTex, _MainTexSampler), TexCoords);
		}
	ENDPROGRAM
}