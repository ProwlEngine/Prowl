Shader "Default/Gizmos"

Properties
{
}

Pass "Gizmos"
{
    Tags { "RenderOrder" = "Opaque" }

    // Rasterizer culling mode
    Blend Alpha
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
			finalColor = texture(_MainTex, TexCoords).rgba;
		}
	}

	ENDGLSL
}

// Same as above but Blend Off, so the source replaces the destination instead of
// alpha-blending against it. Used to copy into a multisampled target, where the
// destination's existing samples must not survive and HDR scene color carries no
// alpha=1 guarantee. ZWrite Off keeps the multisampled depth buffer intact.
Pass "Copy"
{
    Tags { "RenderOrder" = "Opaque" }

    Blend Off
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
			finalColor = texture(_MainTex, TexCoords).rgba;
		}
	}

	ENDGLSL
}
