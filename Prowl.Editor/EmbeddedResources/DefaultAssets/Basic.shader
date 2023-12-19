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

		void main() 
		{
			gl_Position =vec4(vertexPosition, 1.0);
		}
	}

	Fragment
	{
		in vec2 fragTexCoord;
		uniform vec2 ScreenResolution;
		uniform sampler2D texture0;
		
		out vec4 finalColor;
		
		void main()
		{
			vec2 texCoords = gl_FragCoord.xy / ScreenResolution;
		    finalColor = texture(texture0, texCoords);
		}
	}
}