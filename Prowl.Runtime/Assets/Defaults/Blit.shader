Shader "Default/Gizmos"

Properties
{
}

Pass "Gizmos"
{
    Tags { "RenderOrder" = "Opaque" }

    // Rasterizer culling mode
    Cull None
    ZTest Off
    ZWrite Off

	GLSLPROGRAM

	Vertex
	{
		layout (location = 0) in vec3 vertexPosition;
		layout (location = 1) in vec2 vertexTexCoord;
		
		out vec2 TexCoords;
		
		void main()
		{
			TexCoords = vertexTexCoord;
		    gl_Position = vec4(vertexPosition, 1.0);
		}
	}

	Fragment
	{
		layout (location = 0) out vec4 finalColor;
		
		in vec2 TexCoords;

		uniform sampler2D _MainTex;

		void main()
		{
			finalColor = vec4(texture(_MainTex, TexCoords).rgb, 1.0);
		}
	}

	ENDGLSL
}