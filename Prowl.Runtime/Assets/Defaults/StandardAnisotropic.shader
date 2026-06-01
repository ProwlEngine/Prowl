Shader "Default/StandardAnisotropic"

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

    _AlphaCutoff ("Alpha Cutoff", Float) = 0.5

    _Anisotropy ("Anisotropy", Float) = 0.5
    _AnisoDirectionMap ("Anisotropy Direction (RG)", Texture2D) = "normal"
}

Pass "StandardAniso"
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
				vBitangent = cross(vTangent, vNormal) * vertexTangent.w;
				if (dot(vBitangent, vBitangent) < 0.000001) {
					vTangent = abs(vNormal.y) < 0.999 ? normalize(cross(vNormal, vec3(0,1,0))) : normalize(cross(vNormal, vec3(1,0,0)));
					vBitangent = cross(vTangent, vNormal) * vertexTangent.w;
				}
#endif
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
			in vec3 vNormal;
			in vec3 vTangent;
			in vec3 vBitangent;

			uniform sampler2D _MainTex;
			uniform sampler2D _NormalTex;
			uniform sampler2D _SurfaceTex;
			uniform sampler2D _EmissionTex;
			uniform float _EmissionIntensity;
			uniform vec4 _MainColor;
			uniform float _AlphaCutoff;

			uniform float _Anisotropy;
			uniform sampler2D _AnisoDirectionMap;

			void main()
			{
				// Albedo
				vec4 albedo = texture(_MainTex, texCoord0) * vColor * _MainColor;
				vec3 baseColor = gammaToLinearSpace(albedo.rgb);

				// Alpha cutout
				if (albedo.a < _AlphaCutoff)
				    discard;

				// Normal mapping
				vec3 worldNormal = ApplyNormalMap(_NormalTex, texCoord0, vNormal, vTangent, vBitangent);

				// Surface: R = AO, G = Roughness, B = Metallic
				vec4 surface = texture(_SurfaceTex, texCoord0);
				float ao = 1.0 - surface.r;
				float roughness = surface.g;
				float metallic = surface.b;

				// Tangent frame for anisotropic lighting
				vec3 T = normalize(vTangent);
				vec3 B = normalize(vBitangent);
				vec3 N = normalize(worldNormal);

				// Optionally rotate tangent direction by aniso direction map
				// RG encodes direction in tangent plane, default (0.5, 0.5) = use mesh tangent as-is
				vec2 anisoDir = texture(_AnisoDirectionMap, texCoord0).rg * 2.0 - 1.0;
				float anisoDirLen = length(anisoDir);
				vec3 anisoTangent, anisoBitangent;
				if (anisoDirLen > 0.01)
				{
				    anisoDir /= anisoDirLen;
				    anisoTangent = normalize(T * anisoDir.x + B * anisoDir.y);
				    anisoBitangent = normalize(cross(N, anisoTangent));
				}
				else
				{
				    // No direction map or neutral use mesh tangent directly
				    anisoTangent = T;
				    anisoBitangent = B;
				}

				// Emission
				vec3 emission = texture(_EmissionTex, texCoord0).rgb * _EmissionIntensity;

				// Anisotropic PBR lighting
				vec3 viewDir = normalize(_WorldSpaceCameraPos.xyz - worldPos);
				vec3 lighting = CalculateForwardLightingAniso(worldPos, N, viewDir,
				    anisoTangent, anisoBitangent,
				    baseColor, metallic, roughness, _Anisotropy, ao);

				// Ambient + fog (energy conserved for metals)
				vec3 ambientLight = CalculateAmbient(N) * ao * _AmbientStrength;
				vec3 diffuseColor = baseColor * (1.0 - metallic);
				vec3 ambientDiffuse = ambientLight * diffuseColor;

				vec3 F0 = mix(vec3(0.04), baseColor, metallic);
				float NdotV = max(dot(N, viewDir), 0.0);
				vec3 F = FresnelSchlickRoughness(NdotV, F0, roughness);
				float specOcclusion = 1.0 - roughness * roughness;
				vec3 ambientSpecular = ambientLight * F * mix(specOcclusion, 1.0, 0.25);

				vec3 ambient = ambientDiffuse + ambientSpecular;
				vec3 color = ApplyFog(ambient + lighting + emission, worldPos);

				fragColor = vec4(color, 1.0);
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
			uniform float _AlphaCutoff;

			void main()
			{
				if (texture(_MainTex, texCoord0).a < _AlphaCutoff)
				    discard;

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

Pass "ShadowCaster"
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
			uniform float _AlphaCutoff;

			void main()
			{
				if (texture(_MainTex, texCoord0).a < _AlphaCutoff)
				    discard;
                gl_FragDepth = gl_FragCoord.z;
			}
		}
	ENDGLSL
}
