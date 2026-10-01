Shader "Default/Line"

Properties
{
    _MainTex ("Texture", Texture2D) = "white"
    _StartColor ("Start Color", Color) = (1.0, 1.0, 1.0, 1.0)
    _EndColor ("End Color", Color) = (1.0, 1.0, 1.0, 1.0)
}

Pass "Line"
{
    Tags { "RenderOrder" = "Transparent" }
    Blend Alpha
    ZWrite Off
    Cull Off

	GLSLPROGRAM
		Vertex
		{
            #include "ProwlCG"
            #include "VertexAttributes"

			out vec2 texCoord0;
			out vec3 worldPos;
			out vec4 vColor;

			void main()
			{
				gl_Position = TransformClip(vertexPosition);
				texCoord0 = vertexTexCoord0;
				worldPos = TransformPosition(vertexPosition);
				vColor = vertexColor;
			}
		}

		Fragment
		{
            #include "ProwlCG"
            #include "Lighting"

			layout (location = 0) out vec4 fragColor;

			in vec2 texCoord0;
			in vec3 worldPos;
			in vec4 vColor;

			uniform sampler2D _MainTex;

			void main()
			{
				vec4 texel = texture(_MainTex, texCoord0);
				vec3 baseColor = gammaToLinearSpace(texel.rgb) * vColor.rgb;
				baseColor = ApplyFog(baseColor, worldPos);
				fragColor = vec4(baseColor, texel.a * vColor.a);
			}
		}
	ENDGLSL
}
