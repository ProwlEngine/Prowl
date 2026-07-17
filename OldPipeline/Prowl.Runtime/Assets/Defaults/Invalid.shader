Shader "Default/Invalid"

Properties
{
}

Pass "Invalid"
{
	Tags { "RenderType" = "Opaque" }
	Cull None

	GLSLPROGRAM
	Vertex
	{
		#include "ProwlCG"
		#include "VertexAttributes"

		void main()
		{
			gl_Position = PROWL_MATRIX_VP * vec4(vertexPosition, 1.0);
		}
	}

	Fragment
	{
		layout (location = 0) out vec4 fragColor;

		void main()
		{
			fragColor = vec4(1.0, 0.0, 1.0, 1.0);
		}
	}
	ENDGLSL
}