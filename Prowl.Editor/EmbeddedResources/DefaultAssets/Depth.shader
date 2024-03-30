Shader "Default/Depth"

Properties
{
}

Pass 0
{
	RenderMode "Opaque"

	Vertex
	{
		layout (location = 0) in vec3 vertexPosition;

		uniform mat4 mvp;
		void main()
		{
		    gl_Position =  mvp * vec4(vertexPosition, 1.0);
		}
	}

	Fragment
	{
		layout (location = 0) out float fragmentdepth;
		//uniform sampler _MainTex; // diffuse

		void main()
		{
			//if(texture(_MainTex, TexCoords).a < 0.5) discard;
			// Not really needed, OpenGL does it anyway
			//fragmentdepth = gl_FragCoord.z;
		}
	}
}