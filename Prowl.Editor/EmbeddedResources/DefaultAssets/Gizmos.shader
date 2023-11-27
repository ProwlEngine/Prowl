Shader "Default/Standard"

Properties
{
}

Pass 0
{
	RenderMode "Opaque"

	Vertex
	{
		layout (location = 0) in vec3 vertexPosition;
		layout (location = 1) in vec3 vertexColor;

		out vec3 VertColor;
		
		uniform mat4 mvp;

		void main()
		{
		    VertColor = vertexColor;

		    gl_Position = mvp * vec4(vertexPosition, 1.0);
		}
	}

	Fragment
	{
		in vec3 VertColor;

		out vec4 finalColor;

		void main()
		{
			finalColor = vec4(VertColor, 1.0);

		}
	}
}