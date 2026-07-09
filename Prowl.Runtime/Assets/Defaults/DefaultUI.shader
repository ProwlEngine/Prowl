Shader "Default/DefaultUI"

Properties
{
    _MainTex ("Texture", Texture2D) = "white"
    _MainColor ("Tint", Color) = (1.0, 1.0, 1.0, 1.0)
    _Tiling ("Tiling", Vector2) = (1.0, 1.0)
    _Offset ("Offset", Vector2) = (0.0, 0.0)
}

Pass "DefaultUI"
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

			uniform sampler2D _MainTex;
			uniform vec4 _MainColor;

			// Per-item rounded-rect clip (RectMask). _ClipToLocal maps the fragment's world position
			// into the mask's local space (so the clip follows rotation/scale); the fragment is tested
			// against _ClipRect with _ClipRadius rounded corners and a _ClipSoftness edge.
			uniform mat4 _ClipToLocal;
			uniform vec4 _ClipRect;      // minX, minY, maxX, maxY
			uniform float _ClipRadius;
			uniform float _ClipSoftness;
			uniform float _ClipEnable;

			float uiClipCoverage(vec3 worldPosition)
			{
				if (_ClipEnable < 0.5) return 1.0;
				vec2 p = (_ClipToLocal * vec4(worldPosition, 1.0)).xy;
				vec2 c = (_ClipRect.xy + _ClipRect.zw) * 0.5;
				vec2 e = (_ClipRect.zw - _ClipRect.xy) * 0.5 - vec2(_ClipRadius);
				vec2 d = abs(p - c) - e;
				float dist = length(max(d, vec2(0.0))) + min(max(d.x, d.y), 0.0) - _ClipRadius;
				float soft = max(_ClipSoftness, max(fwidth(dist), 1e-4));
				return clamp(0.5 - dist / soft, 0.0, 1.0);
			}

			void main()
			{
				vec4 albedo = texture(_MainTex, texCoord0) * vColor * _MainColor;
				fragColor = albedo * uiClipCoverage(worldPos);
			}
		}
	ENDGLSL
}
