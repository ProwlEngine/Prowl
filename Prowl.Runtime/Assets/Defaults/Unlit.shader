Shader "Default/Unlit"

Properties
{
    _MainTex ("Texture", Texture2D) = "white"
    _MainColor ("Tint", Color) = (1.0, 1.0, 1.0, 1.0)
    _Tiling ("Tiling", Vector2) = (1.0, 1.0)
    _Offset ("Offset", Vector2) = (0.0, 0.0)
}

Pass "Unlit"
{
    Tags { "RenderOrder" = "Opaque" }
    Cull Back

	GLSLPROGRAM
		Vertex
		{
            #include "ProwlCG"
            #include "VertexAttributes"

			out vec2 texCoord0;
			out vec3 worldPos;
			out vec4 vColor;

			uniform vec2 _Tiling;
			uniform vec2 _Offset;

			void main()
			{
				gl_Position = TransformClip(vertexPosition);
				texCoord0 = vertexTexCoord0 * _Tiling + _Offset;
				worldPos = TransformPosition(vertexPosition);
				vColor = GetInstanceColor();
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

Pass "UnlitPrepass"
{
    Tags { "LightMode" = "Prepass" }
    Cull Back
    ZWrite On

	GLSLPROGRAM
		Vertex
		{
            #include "ProwlCG"
            #include "VertexAttributes"

			out vec3 vNormal;
			out vec4 vCurrClipNJ;
			out vec4 vPrevClip;

			void main()
			{
				gl_Position = TransformClip(vertexPosition); // jittered, for raster + depth
				vNormal = TransformDirection(vertexNormal);

				// Jitter-free current + previous clip positions for motion vectors.
				vec4 worldPos = GetModelMatrix() * vec4(vertexPosition, 1.0);
				vCurrClipNJ = PROWL_MATRIX_VP_NONJITTERED * worldPos;
				vec4 prevWorldPos = PROWL_MATRIX_M_PREVIOUS * vec4(vertexPosition, 1.0);
				vPrevClip = PROWL_MATRIX_VP_PREVIOUS * prevWorldPos;
			}
		}

		Fragment
		{
            #include "ProwlCG"

			layout (location = 0) out vec4 normalOut;
			layout (location = 1) out vec4 motionRM;
			in vec3 vNormal;
			in vec4 vCurrClipNJ;
			in vec4 vPrevClip;

			void main()
			{
				normalOut = EncodeViewNormal(normalize(vNormal));

				// Motion vectors (jitter-free). Unlit has no PBR material -> roughness/metallic 0.
				vec2 currNDC = (vCurrClipNJ.xy / vCurrClipNJ.w) * 0.5 + 0.5;
				vec2 prevNDC = (vPrevClip.xy / vPrevClip.w) * 0.5 + 0.5;
				motionRM = vec4(currNDC - prevNDC, 0.0, 0.0);
			}
		}
	ENDGLSL
}
