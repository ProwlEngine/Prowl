Shader "Default/DefaultText"

Properties
{
    _MainTex ("SDF Atlas", Texture2D) = "white"
    _MainColor ("Tint", Color) = (1.0, 1.0, 1.0, 1.0)
    _Tiling ("Tiling", Vector2) = (1.0, 1.0)
    _Offset ("Offset", Vector2) = (0.0, 0.0)
}

Pass "DefaultText"
{
    Tags { "RenderOrder" = "UI" }

    Blend {
        Src SrcAlpha
        Dst OneMinusSrcAlpha
        Mode Add
    }
    ZTest Off
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

			uniform sampler2D _MainTex;   // single-channel SDF replicated across RGB(A)
			uniform vec4 _MainColor;

			// Reconstruct sharp, resolution-independent coverage from the distance field.
			const float sdfPxRange = 4.0;
			float sdfScreenPxRange(vec2 uv) {
				vec2 unitRange = vec2(sdfPxRange) / vec2(textureSize(_MainTex, 0));
				vec2 screenTexSize = vec2(1.0) / fwidth(uv);
				return max(0.5 * dot(unitRange, screenTexSize), 1.0);
			}

			void main()
			{
				float sd = texture(_MainTex, texCoord0).r;
				float screenPxDistance = sdfScreenPxRange(texCoord0) * (sd - 0.5);
				float coverage = clamp(screenPxDistance + 0.5, 0.0, 1.0);
				fragColor = vColor * _MainColor * coverage;
			}
		}
	ENDGLSL
}
