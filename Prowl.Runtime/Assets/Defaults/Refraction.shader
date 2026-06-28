Shader "Default/Refraction"
{
    Properties
    {
        _RefractionStrength("Refraction Strength", Float) = 0.1
        _NoiseScale("Noise Scale", Float) = 1.0
        _Tint("Tint", Color) = (1.0, 1.0, 1.0, 1.0)
        _BlurRadius("Blur Radius", Float) = 0.02
        _BlurMIP("Blur MIP Bias", Float) = 2.0
        _BlurSteps("Blur Steps", Integer) = 4
    }

    Pass
    {
        Name "Refraction"
        GrabTexture
        _GrabTexture
        Tags { "RenderOrder" = "Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        SLANGPROGRAM

        // ----------------------- VERTEX START ----------------------
            import ProwlCG;
            import VertexAttributes;

			out float2 texCoord0;
			out float3 worldPos;
			out float4 screenPos;
			out float3 vNormal;

			void main()
			{
				gl_Position = PROWL_MATRIX_MVP * float4(vertexPosition, 1.0);
				texCoord0 = vertexTexCoord0;
				worldPos = (PROWL_MATRIX_M * float4(vertexPosition, 1.0)).xyz;
				screenPos = gl_Position;
				vNormal = normalize(float3x3(PROWL_MATRIX_M) * vertexNormal);
			}

        // ----------------------- FRAGMENT START ----------------------
            import ProwlCG;

			layout (location = 0) out float4 fragColor;

			in float2 texCoord0;
			in float3 worldPos;
			in float4 screenPos;
			in float3 vNormal;

			uniform Sampler2D<float4> _GrabTexture;
			uniform float _RefractionStrength;
			uniform float _NoiseScale;
			uniform float4 _Tint;
			uniform float _BlurRadius;
			uniform float _BlurMIP;
			uniform int _BlurSteps;

			void main()
			{
				// Screen UV from clip space
				float2 screenUV = (screenPos.xy / screenPos.w) * 0.5 + 0.5;

				// Noise-based refraction offset
				float3 noiseInput = worldPos * _NoiseScale + float3(_Time * 0.1);
				float2 refractionOffset = float2(
					noise(noiseInput + float3(0.0, 0.0, 0.0)),
					noise(noiseInput + float3(5.2, 1.3, 0.0))
				);
				refractionOffset = (refractionOffset * 2.0 - 1.0) * _RefractionStrength;
				float2 refractedUV = clamp(screenUV + refractionOffset, 0.0, 1.0);

				// One-step mip blur (Mirza Beig technique)
				float ign = InterleavedGradientNoise(gl_FragCoord.xy, int(_Time.w) % 60);
				float angle = ign * 6.28318530718;
				float2 dir = float2(cos(angle), sin(angle));
				float2 aspect = float2(min(1.0, _ScreenParams.y / _ScreenParams.x),
				                   min(1.0, _ScreenParams.x / _ScreenParams.y));

				int steps = clamp(_BlurSteps, 1, 4);
				float4 acc = float4(0.0);
				for (int i = 0; i < steps; i++)
				{
					float2 offset = dir * _BlurRadius * aspect;
					acc += _GrabTexture.SampleLevel(refractedUV + offset, _BlurMIP);
					dir = float2(-dir.y, dir.x); // Rotate 90 degrees
				}
				float4 refractedColor = acc / float(steps);

				fragColor = float4(refractedColor.rgb * _Tint.rgb, _Tint.a);
			}

        ENDSLANG
    }
}
