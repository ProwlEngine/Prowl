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
#ifdef SKINNED
				vec4 skinnedPos = GetSkinnedPosition(vertexPosition);
				gl_Position = PROWL_MATRIX_MVP * skinnedPos;
				worldPos = (PROWL_MATRIX_M * skinnedPos).xyz;
#else
				gl_Position = PROWL_MATRIX_MVP * vec4(vertexPosition, 1.0);
				worldPos = (PROWL_MATRIX_M * vec4(vertexPosition, 1.0)).xyz;
#endif
				texCoord0 = vertexTexCoord0;
				vColor = vertexColor;
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
#ifdef SKINNED
				vec4 skinnedPos = GetSkinnedPosition(vertexPosition);
				vec3 skinnedNormal = GetSkinnedNormal(vertexNormal);
				gl_Position = PROWL_MATRIX_MVP * skinnedPos;
				vNormal = normalize(mat3(PROWL_MATRIX_M) * skinnedNormal);
#else
				gl_Position = PROWL_MATRIX_MVP * vec4(vertexPosition, 1.0);
				vNormal = normalize(mat3(PROWL_MATRIX_M) * vertexNormal);
#endif
			}
		}

		Fragment
		{
            #include "Fragment"

			layout (location = 0) out vec4 normalOut;
			in vec3 vNormal;

			void main()
			{
				vec3 viewNormal = normalize(mat3(PROWL_MATRIX_V) * normalize(vNormal));
				normalOut = vec4(viewNormal * 0.5 + 0.5, 1.0);
			}
		}
	ENDGLSL
}
