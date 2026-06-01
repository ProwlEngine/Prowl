Shader "Default/Standard"

Properties
{
    _MainTex ("Albedo", Texture2D) = "grid"
    _MainColor ("Tint", Color) = (1.0, 1.0, 1.0, 1.0)
    _Tiling ("Tiling", Vector2) = (1.0, 1.0)
    _Offset ("Offset", Vector2) = (0.0, 0.0)

    _NormalTex ("Normal", Texture2D) = "normal"

    _SurfaceTex ("Surface (AO, Roughness, Metallicness)", Texture2D) = "surface"

    _EmissionTex ("Emission", Texture2D) = "emission"
    _EmissionIntensity ("Emission Intensity", Float) = 1.0

    _AlphaCutoff ("Alpha Cutoff", Float) = 0.5

    _ParallaxMap ("Height Map (G)", Texture2D) = "black"
    _Parallax ("Height Scale", Float) = 0.0
    _ParallaxSteps ("POM Steps", Int) = 16

    _TranslucencyMap ("Translucency (B) Occlusion (G)", Texture2D) = "white"
    _TranslucencyStrength ("Translucency Strength", Float) = 0.0
    _ScatteringPower ("Scattering Power", Float) = 0.0
    _ScatteringDistortion ("Scattering Distortion", Float) = 0.5
    _ScatteringScale ("Scattering Scale", Float) = 1.0
}

Pass "Standard"
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
			out vec3 vNormal;
			out vec3 vTangent;
			out vec3 vBitangent;
			out vec2 vLightmapUV2;

			uniform vec2 _Tiling;
			uniform vec2 _Offset;

			void main()
			{
				gl_Position = TransformClip(vertexPosition);
				texCoord0 = vertexTexCoord0 * _Tiling + _Offset;
				vLightmapUV2 = vertexTexCoord1; // raw UV2; scale/offset applied in the fragment
				worldPos = TransformPosition(vertexPosition);
				vColor = GetInstanceColor();
				vNormal = TransformDirection(vertexNormal);
#ifdef HAS_TANGENTS
				vTangent = TransformDirection(vertexTangent.xyz);
				vBitangent = cross(vTangent, vNormal) * vertexTangent.w;
				// Guard against degenerate tangent frames (parallel normal/tangent)
				if (dot(vBitangent, vBitangent) < 0.000001) {
					vTangent = abs(vNormal.y) < 0.999 ? normalize(cross(vNormal, vec3(0,1,0))) : normalize(cross(vNormal, vec3(1,0,0)));
					vBitangent = cross(vTangent, vNormal) * vertexTangent.w;
				}
#endif
			}
		}

		Fragment
		{
            #include "StandardSurface"

			layout (location = 0) out vec4 fragColor;

			in vec2 texCoord0;
			in vec3 worldPos;
			in vec4 vColor;
			in vec3 vNormal;
			in vec3 vTangent;
			in vec3 vBitangent;
			in vec2 vLightmapUV2;

			uniform sampler2D _MainTex;
			uniform sampler2D _NormalTex;
			uniform sampler2D _SurfaceTex;
			uniform sampler2D _EmissionTex;
			uniform float _EmissionIntensity;
			uniform vec4 _MainColor;
			uniform float _AlphaCutoff;

			uniform sampler2D _ParallaxMap;
			uniform float _Parallax;
			uniform int _ParallaxSteps;

			uniform sampler2D _TranslucencyMap;
			uniform float _TranslucencyStrength;
			uniform float _ScatteringPower;
			uniform float _ScatteringDistortion;
			uniform float _ScatteringScale;

			void main()
			{
				vec4 result = StandardSurface(texCoord0, worldPos, vColor,
				    vNormal, vTangent, vBitangent,
				    _MainTex, _NormalTex, _SurfaceTex, _EmissionTex,
				    _EmissionIntensity, _MainColor,
				    _ParallaxMap, _Parallax, _ParallaxSteps,
				    _TranslucencyMap, _TranslucencyStrength,
				    _ScatteringPower, _ScatteringDistortion, _ScatteringScale,
				    vLightmapUV2);

				// Alpha cutout discard below threshold, output fully opaque
				if (result.a < _AlphaCutoff)
				    discard;

				fragColor = vec4(result.rgb, 1.0);
			}
		}
	ENDGLSL
}

Pass "Prepass"
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
			out vec3 vTangent;
			out vec3 vBitangent;
			out vec2 texCoord0;
			out vec4 vCurrClipNJ;
			out vec4 vPrevClip;

			uniform vec2 _Tiling;
			uniform vec2 _Offset;

			void main()
			{
				gl_Position = TransformClip(vertexPosition); // jittered, for raster + depth
				vNormal = TransformDirection(vertexNormal);
#ifdef HAS_TANGENTS
				vTangent = TransformDirection(vertexTangent.xyz);
				vBitangent = cross(vTangent, vNormal) * vertexTangent.w;
				if (dot(vBitangent, vBitangent) < 0.000001) {
					vTangent = abs(vNormal.y) < 0.999 ? normalize(cross(vNormal, vec3(0,1,0))) : normalize(cross(vNormal, vec3(1,0,0)));
					vBitangent = cross(vTangent, vNormal) * vertexTangent.w;
				}
#endif
				texCoord0 = vertexTexCoord0 * _Tiling + _Offset;

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
			in vec3 vTangent;
			in vec3 vBitangent;
			in vec2 texCoord0;
			in vec4 vCurrClipNJ;
			in vec4 vPrevClip;

			uniform sampler2D _NormalTex;
			uniform sampler2D _MainTex;
			uniform sampler2D _SurfaceTex;
			uniform vec4 _MainColor;
			uniform float _AlphaCutoff;

			void main()
			{
				// Alpha cutoff for cutout mode
				if (_AlphaCutoff > 0.0)
				{
				    float alpha = texture(_MainTex, texCoord0).a * _MainColor.a;
				    if (alpha < _AlphaCutoff) discard;
				}

                vec3 worldNormal = ApplyNormalMap(_NormalTex, texCoord0, vNormal, vTangent, vBitangent);
				normalOut = EncodeViewNormal(worldNormal);

				// Motion vectors (jitter-free) + packed roughness/metallic (_SurfaceTex G/B).
				vec2 currNDC = (vCurrClipNJ.xy / vCurrClipNJ.w) * 0.5 + 0.5;
				vec2 prevNDC = (vPrevClip.xy / vPrevClip.w) * 0.5 + 0.5;
				vec4 surface = texture(_SurfaceTex, texCoord0);
				motionRM = vec4(currNDC - prevNDC, surface.g, surface.b);
			}
		}
	ENDGLSL
}

Pass "StandardShadow"
{
    Tags { "LightMode" = "ShadowCaster" }
    Cull Back

	GLSLPROGRAM

		Vertex
		{
            #include "ProwlCG"
            #include "VertexAttributes"

			out vec2 texCoord0;

			uniform vec2 _Tiling;
			uniform vec2 _Offset;

			void main()
			{
				gl_Position = TransformClip(vertexPosition);
				texCoord0 = vertexTexCoord0 * _Tiling + _Offset;
			}
		}

		Fragment
		{
            #include "ProwlCG"

			in vec2 texCoord0;
			uniform sampler2D _MainTex;
			uniform vec4 _MainColor;
			uniform float _AlphaCutoff;

			void main()
			{
				if (_AlphaCutoff > 0.0)
				{
				    float alpha = texture(_MainTex, texCoord0).a * _MainColor.a;
				    if (alpha < _AlphaCutoff) discard;
				}
                gl_FragDepth = gl_FragCoord.z;
			}
		}
	ENDGLSL
}
