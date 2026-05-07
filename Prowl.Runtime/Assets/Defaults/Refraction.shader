Shader "Default/Refraction"

Properties
{
    _RefractionStrength ("Refraction Strength", Float) = 0.1
    _NoiseScale ("Noise Scale", Float) = 1.0
    _Tint ("Tint", Color) = (1.0, 1.0, 1.0, 1.0)
    _BlurRadius ("Blur Radius", Float) = 0.02
    _BlurMIP ("Blur MIP Bias", Float) = 2.0
    _BlurSteps ("Blur Steps", Int) = 4
}

Pass "Refraction"
{
    GrabTexture "_GrabTexture"
    Tags { "RenderOrder" = "Transparent" }
    Blend Alpha
    ZWrite Off
    Cull Back

	GLSLPROGRAM
		Shared
		{
			// Simple 3D noise function
			float hash(vec3 p)
			{
				p = fract(p * 0.3183099 + 0.1);
				p *= 17.0;
				return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
			}

			float noise(vec3 x)
			{
				vec3 i = floor(x);
				vec3 f = fract(x);
				f = f * f * (3.0 - 2.0 * f);

				return mix(mix(mix(hash(i + vec3(0.0, 0.0, 0.0)),
								   hash(i + vec3(1.0, 0.0, 0.0)), f.x),
							   mix(hash(i + vec3(0.0, 1.0, 0.0)),
								   hash(i + vec3(1.0, 1.0, 0.0)), f.x), f.y),
						   mix(mix(hash(i + vec3(0.0, 0.0, 1.0)),
								   hash(i + vec3(1.0, 0.0, 1.0)), f.x),
							   mix(hash(i + vec3(0.0, 1.0, 1.0)),
								   hash(i + vec3(1.0, 1.0, 1.0)), f.x), f.y), f.z);
			}

			// Interleaved Gradient Noise (Jimenez 2014)
			float InterleavedGradientNoise(vec2 pixCoord, int frameCount)
			{
				const vec3 magic = vec3(0.06711056, 0.00583715, 52.9829189);
				vec2 frameMagicScale = vec2(2.083, 4.867);
				pixCoord += float(frameCount) * frameMagicScale;
				return fract(magic.z * fract(dot(pixCoord, magic.xy)));
			}
		}

		Vertex
		{
            #include "Fragment"
            #include "VertexAttributes"

			out vec2 texCoord0;
			out vec3 worldPos;
			out vec4 screenPos;
			out vec3 vNormal;

			void main()
			{
				gl_Position = PROWL_MATRIX_MVP * vec4(vertexPosition, 1.0);
				texCoord0 = vertexTexCoord0;
				worldPos = (PROWL_MATRIX_M * vec4(vertexPosition, 1.0)).xyz;
				screenPos = gl_Position;
				vNormal = normalize(mat3(PROWL_MATRIX_M) * vertexNormal);
			}
		}

		Fragment
		{
            #include "Fragment"

			layout (location = 0) out vec4 fragColor;

			in vec2 texCoord0;
			in vec3 worldPos;
			in vec4 screenPos;
			in vec3 vNormal;

			uniform sampler2D _GrabTexture;
			uniform float _RefractionStrength;
			uniform float _NoiseScale;
			uniform vec4 _Tint;
			uniform float _BlurRadius;
			uniform float _BlurMIP;
			uniform int _BlurSteps;

			void main()
			{
				// Screen UV from clip space
				vec2 screenUV = (screenPos.xy / screenPos.w) * 0.5 + 0.5;

				// Noise-based refraction offset
				vec3 noiseInput = worldPos * _NoiseScale + vec3(_Time * 0.1);
				vec2 refractionOffset = vec2(
					noise(noiseInput + vec3(0.0, 0.0, 0.0)),
					noise(noiseInput + vec3(5.2, 1.3, 0.0))
				);
				refractionOffset = (refractionOffset * 2.0 - 1.0) * _RefractionStrength;
				vec2 refractedUV = clamp(screenUV + refractionOffset, 0.0, 1.0);

				// One-step mip blur (Mirza Beig technique)
				float ign = InterleavedGradientNoise(gl_FragCoord.xy, int(_Time.w) % 60);
				float angle = ign * 6.28318530718;
				vec2 dir = vec2(cos(angle), sin(angle));
				vec2 aspect = vec2(min(1.0, _ScreenParams.y / _ScreenParams.x),
				                   min(1.0, _ScreenParams.x / _ScreenParams.y));

				int steps = clamp(_BlurSteps, 1, 4);
				vec4 acc = vec4(0.0);
				for (int i = 0; i < steps; i++)
				{
					vec2 offset = dir * _BlurRadius * aspect;
					acc += textureLod(_GrabTexture, refractedUV + offset, _BlurMIP);
					dir = vec2(-dir.y, dir.x); // Rotate 90 degrees
				}
				vec4 refractedColor = acc / float(steps);

				fragColor = vec4(refractedColor.rgb * _Tint.rgb, _Tint.a);
			}
		}
	ENDGLSL
}
