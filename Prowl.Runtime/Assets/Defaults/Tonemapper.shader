Shader "Default/Tonemapper"
{
    Pass
    {
        Name "Tonemapper"
        Tags { "RenderOrder" = "Opaque" }

        Blend SourceAlpha InverseSourceAlpha
        Cull Off
        ZTest Disabled
        ZWrite Off

        SLANGPROGRAM
        import ProwlCG;
        import VariantAttributes;

        // Mutually-exclusive tonemap operator selection.
        static const int TONEMAP_NONE            = 0;
        static const int TONEMAP_MELON           = 1;
        static const int TONEMAP_ACES            = 2;
        static const int TONEMAP_ACES_SIMPLE     = 3;
        static const int TONEMAP_AGX             = 4;
        static const int TONEMAP_REINHARD_SIMPLE = 5;
        static const int TONEMAP_REINHARD_LUMA   = 6;
        static const int TONEMAP_REINHARD_WHITE  = 7;
        static const int TONEMAP_ROMBINDAHOUSE   = 8;
        static const int TONEMAP_UNCHARTED2      = 9;

        [variant("0") variant("1") variant("2") variant("3") variant("4") variant("5") variant("6") variant("7") variant("8") variant("9")]
        extern static const int TonemapMode;

        struct MaterialData
        {
            float Contrast;
            float Saturation;
            Sampler2D<float4> _MainTex;
        }
        ParameterBlock<MaterialData> Mat;

        struct VertexInput
        {
            float3 position : POSITION0;
            float2 uv : TEXCOORD0;
        }
        struct Varyings
        {
            float4 position : SV_Position;
            float2 uv : TEXCOORD0;
        }

        // --- Melon (TripleMelon) ---
        float3 HueShift(float3 In)
        {
            float A = max(In.x, In.y);
            return float3(A, max(A, In.z), In.z);
        }
        float Cmax(float3 In) { return max(max(In.r, In.g), In.b); }
        float3 MelonTonemap(float3 color)
        {
            color = pow(color, float3(1.56, 1.56, 1.56));
            color = color / (color + 0.84);
            float factor = Cmax(color) * 0.15;
            factor = factor / (factor + 1);
            factor *= factor;
            color = lerp(color, HueShift(color), factor);
            color = lerp(color, float3(1.0, 1.0, 1.0), factor);
            return color;
        }

        // --- ACES (TheRealMJP / BakingLab) ---
        static const float3x3 ACESInputMat = float3x3(
            0.59719, 0.35458, 0.04823,
            0.07600, 0.90834, 0.01566,
            0.02840, 0.13383, 0.83777);
        static const float3x3 ACESOutputMat = float3x3(
             1.60475, -0.53108, -0.07367,
            -0.10208,  1.10813, -0.00605,
            -0.00327, -0.07276,  1.07602);
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

        // --- ACES simple ---
        float3 ACESSimple(float3 c)
        {
            float a = 2.51;
            float b = 0.03;
            float y = 2.43;
            float d = 0.59;
            float e = 0.14;
            return (c * (a * c + b)) / (c * (y * c + d) + e);
        }

        // --- AgX ---
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
        float agxLuminance(float3 c) { return dot(c, float3(0.2126729, 0.7151522, 0.0721750)); }
        float3 AgX(float3 color)
        {
            const float minEv = -12.47393;
            const float maxEv = 4.026069;

            color = mul(color, agxTransform);
            color = clamp(log2(max(color, float3(1e-10))), minEv, maxEv);
            color = (color - minEv) / (maxEv - minEv);
            color = agxDefaultContrastApproximation(color);

            const float3 slope = float3(1.1);
            const float3 power = float3(1.2);
            const float sat = 1.3;
            float luma = agxLuminance(color);
            color = pow(max(color * slope, float3(0.0)), power);
            color = max(luma + sat * (color - luma), float3(0.0));

            color = mul(color, agxTransformInverse);
            color = pow(max(float3(0.0), color), float3(2.2));
            return color;
        }

        // --- Reinhard family ---
        float3 ReinhardSimple(float3 color)
        {
            float exposure = 1.5;
            return color * (exposure / (1.0 + color / exposure));
        }
        float3 ReinhardLuma(float3 color)
        {
            float luma = dot(color, float3(0.2126, 0.7152, 0.0722));
            float toneMappedLuma = luma / (1.0 + luma);
            return color * (toneMappedLuma / max(luma, 1e-5));
        }
        float3 ReinhardWhitePreserving(float3 color)
        {
            float white = 2.0;
            float luma = dot(color, float3(0.2126, 0.7152, 0.0722));
            float toneMappedLuma = luma * (1.0 + luma / (white * white)) / (1.0 + luma);
            return color * (toneMappedLuma / max(luma, 1e-5));
        }

        // --- Misc ---
        float3 RomBinDaHouse(float3 color) { return exp(-1.0 / (2.72 * color + 0.15)); }
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

        float3 Tonemap(float3 color)
        {
            switch (TonemapMode)
            {
                case TONEMAP_MELON:           color = MelonTonemap(color); break;
                case TONEMAP_ACES:            color = ACESFitted(color); break;
                case TONEMAP_ACES_SIMPLE:     color = ACESSimple(color); break;
                case TONEMAP_AGX:             color = AgX(color); break;
                case TONEMAP_REINHARD_SIMPLE: color = ReinhardSimple(color); break;
                case TONEMAP_REINHARD_LUMA:   color = ReinhardLuma(color); break;
                case TONEMAP_REINHARD_WHITE:  color = ReinhardWhitePreserving(color); break;
                case TONEMAP_ROMBINDAHOUSE:   color = RomBinDaHouse(color); break;
                case TONEMAP_UNCHARTED2:      color = Uncharted2(color); break;
                default: break; // None / Clamp
            }

            color = clamp(color, 0.0, 1.0);
            return color;
        }

        // Contrast as a row-major float4x4 applied with mul(M, v).
        float4x4 contrastMatrix()
        {
            float t = (1.0 - Mat.Contrast) / 2.0;
            return float4x4(
                Mat.Contrast, 0, 0, t,
                0, Mat.Contrast, 0, t,
                0, 0, Mat.Contrast, t,
                0, 0, 0, 1);
        }

        float4x4 saturationMatrix()
        {
            float3 lum = float3(0.3086, 0.6094, 0.0820);
            float oneMinusSat = 1.0 - Mat.Saturation;

            float3 red = float3(lum.x * oneMinusSat) + float3(Mat.Saturation, 0, 0);
            float3 green = float3(lum.y * oneMinusSat) + float3(0, Mat.Saturation, 0);
            float3 blue = float3(lum.z * oneMinusSat) + float3(0, 0, Mat.Saturation);

            return float4x4(
                red.x, green.x, blue.x, 0,
                red.y, green.y, blue.y, 0,
                red.z, green.z, blue.z, 0,
                0, 0, 0, 1);
        }

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            Varyings output;
            output.uv = input.uv;
            output.position = float4(input.position, 1.0);
            return output;
        }

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
            float4 base = Mat._MainTex.Sample(input.uv);
            float3 color = base.rgb;
            color = Tonemap(color);
            color = linearToGammaSpace(color);
            return mul(contrastMatrix(), mul(saturationMatrix(), float4(color, base.a)));
        }
        ENDSLANG
    }
}
