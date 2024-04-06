Shader "Default/Gizmos"

Properties
{
}

Pass 0
{
	// Default Raster state
	Blend On
	BlendSrc SrcAlpha
	BlendDst One

	Vertex
	{
		layout (location = 0) in vec3 vertexPosition;
		layout (location = 1) in vec4 vertexColor;

		out vec4 VertColor;
		
		uniform mat4 mvp;

		void main()
		{
		    VertColor = vertexColor;

		    gl_Position = mvp * vec4(vertexPosition, 1.0);
		}
	}

	Fragment
	{
		in vec4 VertColor;

		out vec4 finalColor;

		void main()
		{
			finalColor = VertColor;

		}
	}
}