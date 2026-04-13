Shader "Default/Unlit"

Properties
{
    _MainTex ("Texture", Texture2D) = "white"
    _MainColor ("Tint", Color) = (1.0, 1.0, 1.0, 1.0)
}

Pass "Unlit"
{
    Tags { "RenderOrder" = "Opaque" }
    Cull Back

	GLSLPROGRAM
		Vertex
		{
            #include "Fragment"
            #include "VertexAttributes"

			out vec2 texCoord0;
			out vec3 worldPos;
			out vec4 vColor;

			void main()
			{
				gl_Position = TransformClip(vertexPosition);
				texCoord0 = vertexTexCoord0;
				worldPos = TransformPosition(vertexPosition);
				vColor = GetInstanceColor();
			}
		}

		Fragment
		{
            #include "Fragment"
            #include "Lighting"

			layout (location = 0) out vec4 fragColor;

			in vec2 texCoord0;
			in vec3 worldPos;
			in vec4 vColor;

			uniform sampler2D _MainTex;
			uniform vec4 _MainColor;

			void main()
			{
				vec4 albedo = texture(_MainTex, texCoord0) * vColor * _MainColor;
				vec3 baseColor = gammaToLinearSpace(albedo.rgb);
				baseColor = ApplyFog(baseColor, worldPos);
				fragColor = vec4(baseColor, albedo.a);
			}
		}
	ENDGLSL
}

Pass "UnlitDepthNormals"
{
    Tags { "LightMode" = "DepthNormals" }
    Cull Back

	GLSLPROGRAM
		Vertex
		{
            #include "Fragment"
            #include "VertexAttributes"

			out vec3 vNormal;

			void main()
			{
				gl_Position = TransformClip(vertexPosition);
				vNormal = TransformDirection(vertexNormal);
			}
		}

		Fragment
		{
            #include "Fragment"

			layout (location = 0) out vec4 normalOut;
			in vec3 vNormal;

			void main()
			{
				normalOut = EncodeViewNormal(normalize(vNormal));
			}
		}
	ENDGLSL
}
