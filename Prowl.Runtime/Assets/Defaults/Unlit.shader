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
            #include "Fragment"
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

Pass "UnlitMotionVectors"
{
    Tags { "LightMode" = "MotionVectors" }

    Blend Off
    Cull Back
    ZTest LEqual
    ZWrite Off

    GLSLPROGRAM
        Vertex
        {
            #include "Fragment"
            #include "VertexAttributes"

            out vec4 vClipPos;
            out vec4 vPrevClipPos;

            void main()
            {
                vec4 worldPos = GetModelMatrix() * vec4(vertexPosition, 1.0);
                vClipPos = PROWL_MATRIX_VP * worldPos;
                gl_Position = vClipPos;

                vec4 prevWorldPos = PROWL_MATRIX_M_PREVIOUS * vec4(vertexPosition, 1.0);
                vPrevClipPos = PROWL_MATRIX_VP_PREVIOUS * prevWorldPos;
            }
        }

        Fragment
        {
            #include "Fragment"

            layout(location = 0) out vec4 OutputColor;

            in vec4 vClipPos;
            in vec4 vPrevClipPos;

            void main()
            {
                vec2 currentNDC = (vClipPos.xy / vClipPos.w) * 0.5 + 0.5;
                vec2 previousNDC = (vPrevClipPos.xy / vPrevClipPos.w) * 0.5 + 0.5;
                vec2 motion = currentNDC - previousNDC;

                OutputColor = vec4(motion, 0.0, 1.0);
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
