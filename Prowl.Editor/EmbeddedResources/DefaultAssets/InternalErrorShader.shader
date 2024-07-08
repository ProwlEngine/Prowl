Shader "Hidden/InternalError"

Pass "InternalError"
{
	Blend Override

    // Stencil state
    DepthStencil
    {
        // Depth write
        DepthWrite On
        
        // Comparison kind
        DepthTest LessEqual
    }

    // Rasterizer culling mode
    Cull None

	Inputs
	{
		VertexInput 
        {
            Position // Input location 0
        }

        // Set 0
        Set
        {
            // Binding 0
            Buffer MVPBuffer
            {
				Mat_MVP Matrix4x4
            }
        }
	}

	PROGRAM VERTEX
		layout(location = 0) in vec3 vertexPosition;
		
		layout(set = 0, binding = 0, std140) uniform MVPBuffer
		{
			mat4 Mat_MVP;
		};

		void main() 
		{
			gl_Position = Mat_MVP * vec4(vertexPosition, 1.0);
		}
	ENDPROGRAM

	PROGRAM FRAGMENT
		layout(location = 0) out vec4 OutputColor;

		void main()
		{
			OutputColor = vec4(1.0, 0.0, 1.0, 1.0);
		}
	ENDPROGRAM
}