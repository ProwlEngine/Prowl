Shader "Default/Basic"

Properties
{
}

Pass 0
{
	RenderMode "Opaque"

	Vertex
	{
		in vec3 vertexPosition;
		in vec2 vertexTexCoord;
		
		out vec2 TexCoords;

		void main() 
		{
			gl_Position =vec4(vertexPosition, 1.0);
			TexCoords = vertexTexCoord;
		}
	}

	Fragment
	{
		in vec2 TexCoords;
		uniform sampler2D texture0;
		
		out vec4 finalColor;
		
		void main()
		{
		    finalColor = vec4(texture(texture0, TexCoords).xyz, 1.0);
		}
	}
}