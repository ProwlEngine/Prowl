Shader "Default/StandardTransparent"

Properties
{
    _MainTex ("Albedo", Texture2D) = "white"
    _MainColor ("Tint", Color) = (1.0, 1.0, 1.0, 1.0)
    _Tiling ("Tiling", Vector2) = (1.0, 1.0)
    _Offset ("Offset", Vector2) = (0.0, 0.0)

    _NormalTex ("Normal", Texture2D) = "normal"

    _SurfaceTex ("Surface (AO, Roughness, Metallicness)", Texture2D) = "surface"

    _EmissionTex ("Emission", Texture2D) = "emission"
    _EmissionIntensity ("Emission Intensity", Float) = 1.0

    _TranslucencyMap ("Translucency (B) Occlusion (G)", Texture2D) = "white"
    _TranslucencyStrength ("Translucency Strength", Float) = 0.0
    _ScatteringPower ("Scattering Power", Float) = 0.0
    _ScatteringDistortion ("Scattering Distortion", Float) = 0.5
    _ScatteringScale ("Scattering Scale", Float) = 1.0
}

Pass "StandardTransparent"
{
    Tags { "RenderOrder" = "Transparent" }
    Blend Alpha
    ZWrite Off
    Cull Back

	GLSLPROGRAM

		Vertex
		{
            #include "Fragment"
            #include "VertexAttributes"

			out vec2 texCoord0;
			out vec3 worldPos;
			out vec4 vColor;
			out vec3 vNormal;
			out vec3 vTangent;
			out vec3 vBitangent;

			uniform vec2 _Tiling;
			uniform vec2 _Offset;

			void main()
			{
				gl_Position = TransformClip(vertexPosition);
				texCoord0 = vertexTexCoord0 * _Tiling + _Offset;
				worldPos = TransformPosition(vertexPosition);
				vColor = GetInstanceColor();
				vNormal = TransformDirection(vertexNormal);
#ifdef HAS_TANGENTS
				vTangent = TransformDirection(vertexTangent.xyz);
				vBitangent = cross(vNormal, vTangent);
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

			uniform sampler2D _MainTex;
			uniform sampler2D _NormalTex;
			uniform sampler2D _SurfaceTex;
			uniform sampler2D _EmissionTex;
			uniform float _EmissionIntensity;
			uniform vec4 _MainColor;

			uniform sampler2D _TranslucencyMap;
			uniform float _TranslucencyStrength;
			uniform float _ScatteringPower;
			uniform float _ScatteringDistortion;
			uniform float _ScatteringScale;

			void main()
			{
				fragColor = StandardSurface(texCoord0, worldPos, vColor,
				    vNormal, vTangent, vBitangent,
				    _MainTex, _NormalTex, _SurfaceTex, _EmissionTex,
				    _EmissionIntensity, _MainColor,
				    _MainTex, 0.0, 0,
				    _TranslucencyMap, _TranslucencyStrength,
				    _ScatteringPower, _ScatteringDistortion, _ScatteringScale);
			}
		}
	ENDGLSL
}
