Shader "Default/Invalid"

Properties
{
}

Pass 0
{
	RenderMode "Opaque"

	Vertex
	{
		layout (location = 0) in vec3 vertexPosition;

		void main()
		{
		    gl_Position = mvp * vec4(vertexPosition, 1.0);
		}
	}

	Fragment
	{
		out vec4 fragColor;
		
		void main()
		{
			fragColor = vec4(1.0, 0.0, 1.0, 1.0);

		}
	}
}