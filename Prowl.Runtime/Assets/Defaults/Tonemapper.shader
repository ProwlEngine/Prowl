Shader "Default/Tonemapper"
{
    Pass
    {
        Name "Tonemapper"
        Tags { "RenderOrder" = "Opaque" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZTest Disabled
        ZWrite Off

        SLANGPROGRAM

        import ProwlCG;

        struct VertexInput { float3 position : POSITION0; float2 uv : TEXCOORD0; }
        struct Varyings { float4 position : SV_Position; float2 uv : TEXCOORD0; }

        struct Material
        {
            float Contrast;
            float Saturation;
            Sampler2D<float4> _MainTex;
        }
        ParameterBlock<Material> Mat;

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            Varyings output;
            output.uv = input.uv;
            output.position = float4(input.position, 1.0);
            return output;
        }

#ifdef TONEMAP_MELON
		// Shifts red to Yellow, green to Yellow, blue to Cyan,
		float3 HueShift(float3 In)
		{
			float A = max(In.x, In.y);
			return float3(A, max(A, In.z), In.z);
		}

		float Cmax(float3 In)
		{
			return max(max(In.r, In.g), In.b);
		}

		// Made by TripleMelon
		float3 MelonTonemap(float3 color)
		{
			// remaps the colors to [0-1] range
			// tested to be as close ti ACES contrast levels as possible
			color = pow(color, float3(1.56, 1.56, 1.56));
			color = color/(color + 0.84);

			// governs the transition to white for high color intensities
			float factor = Cmax(color) * 0.15; // multiply by 0.15 to get a similar look to ACES
			factor = factor / (factor + 1); // remaps the factor to [0-1] range
			factor *= factor; // smooths the transition to white

			// shift the hue for high intensities (for a more pleasing look).
			color = lerp(color, HueShift(color), factor); // can be removed for more neutral colors
			color = lerp(color, float3(1.0, 1.0, 1.0), factor); // shift to white for high intensities

		    return color;
		}
#endif

#ifdef TONEMAP_ACES
		// from https://github.com/TheRealMJP/BakingLab/blob/master/BakingLab/ACES.hlsl
		static const float3x3 ACESInputMat = float3x3
		(
		    0.59719, 0.35458, 0.04823,
		    0.07600, 0.90834, 0.01566,
		    0.02840, 0.13383, 0.83777
		);

		// ODT_SAT => XYZ => D60_2_D65 => sRGB
		static const float3x3 ACESOutputMat = float3x3
		(
		     1.60475, -0.53108, -0.07367,
		    -0.10208,  1.10813, -0.00605,
		    -0.00327, -0.07276,  1.07602
		);

		float3 RRTAndODTFit(float3 v)
		{
		    float3 a = v * (v + 0.0245786) - 0.000090537;
		    float3 b = v * (0.983729 * v + 0.4329510) + 0.238081;
		    return a / b;
		}

		float3 ACESFitted(float3 color)
		{
		    color = mul(ACESInputMat, color);
		    color = RRTAndODTFit(color);
		    color = mul(ACESOutputMat, color);
		    return color;
		}
#endif

#ifdef TONEMAP_ACES_SIMPLE
		float3 ACESSimple(float3 c)
		{
		    float a = 2.51;
		    float b = 0.03;
		    float y = 2.43;
		    float d = 0.59;
		    float e = 0.14;
		    return (c * (a * c + b)) / (c * (y * c + d) + e);
		}
#endif

#ifdef TONEMAP_AGX
		static const float3x3 agxTransform = float3x3(
		    0.842479062253094, 0.0423282422610123, 0.0423756549057051,
		    0.0784335999999992, 0.878468636469772,  0.0784336,
		    0.0792237451477643, 0.0791661274605434, 0.879142973793104);

		static const float3x3 agxTransformInverse = float3x3(
		     1.19687900512017,  -0.0528968517574562, -0.0529716355144438,
		    -0.0980208811401368, 1.15190312990417,   -0.0980434501171241,
		    -0.0990297440797205,-0.0989611768448433,  1.15107367264116);

		float3 agxDefaultContrastApproximation(float3 x)
		{
		    float3 x2 = x * x;
		    float3 x4 = x2 * x2;
		    return 15.5 * x4 * x2 - 40.14 * x4 * x + 31.96 * x4 - 6.868 * x2 * x + 0.4298 * x2 + 0.1191 * x - 0.00232;
		}

		float agxLuminance(float3 c)
		{
		    return dot(c, float3(0.2126729, 0.7151522, 0.0721750));
		}

		float3 AgX(float3 color)
		{
		    const float minEv = -12.47393;
		    const float maxEv = 4.026069;

		    color = mul(color, agxTransform);
		    color = clamp(log2(max(color, float3(1e-10))), minEv, maxEv);
		    color = (color - minEv) / (maxEv - minEv);
		    color = agxDefaultContrastApproximation(color);

		    // Punchy look
		    const float3 slope = float3(1.1);
		    const float3 power = float3(1.2);
		    const float sat = 1.3;
		    float luma = agxLuminance(color);
		    color = pow(max(color * slope, float3(0.0)), power);
		    color = max(luma + sat * (color - luma), float3(0.0));

		    // EOTF back to linear
		    color = mul(color, agxTransformInverse);
		    color = pow(max(float3(0.0), color), float3(2.2));
		    return color;
		}
#endif

#ifdef TONEMAP_REINHARD_SIMPLE
		float3 ReinhardSimple(float3 color)
		{
			float exposure = 1.5;
			return color * (exposure / (1.0 + color / exposure));
		}
#endif

#ifdef TONEMAP_REINHARD_LUMA
		float3 ReinhardLuma(float3 color)
		{
			float luma = dot(color, float3(0.2126, 0.7152, 0.0722));
			float toneMappedLuma = luma / (1.0 + luma);
			return color * (toneMappedLuma / max(luma, 1e-5));
		}
#endif

#ifdef TONEMAP_REINHARD_WHITE
		float3 ReinhardWhitePreserving(float3 color)
		{
			float white = 2.0;
			float luma = dot(color, float3(0.2126, 0.7152, 0.0722));
			float toneMappedLuma = luma * (1.0 + luma / (white * white)) / (1.0 + luma);
			return color * (toneMappedLuma / max(luma, 1e-5));
		}
#endif

#ifdef TONEMAP_ROMBINDAHOUSE
		float3 RomBinDaHouse(float3 color)
		{
			return exp(-1.0 / (2.72 * color + 0.15));
		}
#endif

#ifdef TONEMAP_UNCHARTED2
		float3 Uncharted2(float3 color)
		{
			float A = 0.15;
			float B = 0.50;
			float C = 0.10;
			float D = 0.20;
			float E = 0.02;
			float F = 0.30;
			float W = 11.2;
			float exposure = 2.0;
			color *= exposure;
			color = ((color * (A * color + C * B) + D * E) / (color * (A * color + B) + D * F)) - E / F;
			float white = ((W * (A * W + C * B) + D * E) / (W * (A * W + B) + D * F)) - E / F;
			return color / white;
		}
#endif

		float3 Tonemap(float3 color)
		{
#ifdef TONEMAP_MELON
			color = MelonTonemap(color);
#endif
#ifdef TONEMAP_ACES
			color = ACESFitted(color);
#endif
#ifdef TONEMAP_ACES_SIMPLE
			color = ACESSimple(color);
#endif
#ifdef TONEMAP_AGX
			color = AgX(color);
#endif
#ifdef TONEMAP_REINHARD_SIMPLE
			color = ReinhardSimple(color);
#endif
#ifdef TONEMAP_REINHARD_LUMA
			color = ReinhardLuma(color);
#endif
#ifdef TONEMAP_REINHARD_WHITE
			color = ReinhardWhitePreserving(color);
#endif
#ifdef TONEMAP_ROMBINDAHOUSE
			color = RomBinDaHouse(color);
#endif
#ifdef TONEMAP_UNCHARTED2
			color = Uncharted2(color);
#endif

		    // Clamp to [0, 1] (also acts as the None/Clamp tonemap when no keyword set)
		  	color = clamp(color, 0.0, 1.0);

			return color;
		}

		float4x4 contrastMatrix()
		{
			float t = (1.0 - Mat.Contrast) / 2.0;

		    return float4x4(Mat.Contrast, 0, 0, 0,
		                0, Mat.Contrast, 0, 0,
		                0, 0, Mat.Contrast, 0,
		                t, t, t, 1);

		}


		float4x4 saturationMatrix()
		{
		    float3 luminance = float3(0.3086, 0.6094, 0.0820);

		    float oneMinusSat = 1.0 - Mat.Saturation;

		    float3 red = float3(luminance.x * oneMinusSat);
		    red+= float3(Mat.Saturation, 0, 0);

		    float3 green = float3(luminance.y * oneMinusSat);
		    green += float3(0, Mat.Saturation, 0);

		    float3 blue = float3(luminance.z * oneMinusSat);
		    blue += float3(0, 0, Mat.Saturation);

		    return float4x4(float4(red, 0),
		                float4(green, 0),
		                float4(blue, 0),
		                float4(0, 0, 0, 1));
		}

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
            float4 base = Mat._MainTex.Sample(input.uv);

            float3 color = base.rgb;

            color = Tonemap(color);

            // Gamma Correct
            color = linearToGammaSpace(color);

            // GLSL `contrast * saturation * v` with literal matrices -> mul(mul(v, saturation), contrast).
            return mul(mul(float4(color, base.a), saturationMatrix()), contrastMatrix());
        }

        ENDSLANG
    }
}
