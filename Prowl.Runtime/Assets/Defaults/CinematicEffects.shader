Shader "Default/CinematicEffects"
{
    Pass
    {
        Name "CinematicEffects"
        Tags { "RenderOrder" = "Opaque" }

        ZTest Disabled
        ZWrite Off
        Cull Off

        SLANGPROGRAM
        import ProwlCG;
        import VariantAttributes;

        [VariantAxis] extern static const bool VIGNETTE;
        [VariantAxis] extern static const bool CHROMATIC_ABERRATION;
        [VariantAxis] extern static const bool FILM_GRAIN;
        [VariantAxis] extern static const bool COLOR_GRADING;
        [VariantAxis] extern static const bool LUT;
        [VariantAxis] extern static const bool SHARPEN;
        [VariantAxis] extern static const bool EDGE_DETECTION;
        [VariantAxis] extern static const bool PIXELATION;
        [VariantAxis] extern static const bool GOD_RAYS;

        struct MaterialData
        {
            Sampler2D<float4> _MainTex;
            float2 _Resolution;

            float _VignetteIntensity;
            float _VignetteSmoothness;
            float _VignetteRoundness;

            float _ChromaticIntensity;
            float _ChromaticDistortion;

            float _GrainIntensity;
            float _GrainResponse;

            float _PostExposure;
            float _Contrast;
            float _Saturation;
            float _Temperature;
            float4 _Lift;
            float4 _Gamma;
            float4 _Gain;

            Sampler2D<float4> _LUTTex;
            float _LUTContribution;
            float _LUTSize;

            float _SharpenAmount;
            float _SharpenRadius;

            float _EdgeIntensity;
            float4 _EdgeColor;
            float _EdgeBackgroundFade;

            float _PixelSize;

            float _GodRayIntensity;
            float _GodRayDecay;
            float _GodRayDensity;
            float _GodRayWeight;
            int _GodRaySamples;
            float _GodRayThreshold;
            float2 _SunScreenPos;
            Sampler2D<float4> _CameraDepthTexture;
        }
        ParameterBlock<MaterialData> Mat;

        struct VertexInput { float3 position : POSITION0; float2 uv : TEXCOORD0; }
        struct Varyings { float4 position : SV_Position; float2 uv : TEXCOORD0; }

        float getLuminance(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }

        float hash12(float2 p) {
            float3 p3 = frac(float3(p.xyx) * 0.1031);
            p3 += dot(p3, p3.yzx + 33.33);
            return frac((p3.x + p3.y) * p3.z);
        }

        float2 applyPixelation(float2 uv) {
            float2 pixelCount = Mat._Resolution / Mat._PixelSize;
            return floor(uv * pixelCount) / pixelCount;
        }

        float3 applyChromaticAberration(float2 uv) {
            float2 center = uv - 0.5;
            float dist2 = dot(center, center);

            float intensity = Mat._ChromaticIntensity / Mat._Resolution.x;
            float distortion = Mat._ChromaticDistortion;

            float2 offsetR = center * (1.0 + dist2 * distortion) * intensity;
            float2 offsetB = center * (1.0 - dist2 * distortion) * intensity;

            float r = Mat._MainTex.Sample(uv + offsetR).r;
            float g = Mat._MainTex.Sample(uv).g;
            float b = Mat._MainTex.Sample(uv - offsetB).b;
            return float3(r, g, b);
        }

        // Contrast Adaptive Sharpening (CAS)
        float3 applyContrastAdaptiveSharpening(int2 texcoord) {
            int r = int(Mat._SharpenRadius);
            float3 a = Mat._MainTex.Load(int3(texcoord + int2( 0, -1) * r, 0)).rgb;
            float3 b = Mat._MainTex.Load(int3(texcoord + int2(-1,  0) * r, 0)).rgb;
            float3 c = Mat._MainTex.Load(int3(texcoord, 0)).rgb;
            float3 d = Mat._MainTex.Load(int3(texcoord + int2( 1,  0) * r, 0)).rgb;
            float3 e = Mat._MainTex.Load(int3(texcoord + int2( 0,  1) * r, 0)).rgb;

            float mn = min(a.g, min(b.g, min(c.g, min(d.g, e.g))));
            float mx = max(a.g, max(b.g, max(c.g, max(d.g, e.g))));

            float w = sqrt(min(1.0 - mx, mn) / max(mx, 0.001)) * lerp(-0.125, -0.2, Mat._SharpenAmount);
            return (w * (a + b + d + e) + c) / (4.0 * w + 1.0);
        }

        float sobelEdge(float2 uv) {
            float2 texel = 1.0 / Mat._Resolution;

            float tl = getLuminance(Mat._MainTex.Sample(uv + float2(-texel.x,  texel.y)).rgb);
            float t  = getLuminance(Mat._MainTex.Sample(uv + float2(     0.0,  texel.y)).rgb);
            float tr = getLuminance(Mat._MainTex.Sample(uv + float2( texel.x,  texel.y)).rgb);
            float l  = getLuminance(Mat._MainTex.Sample(uv + float2(-texel.x,      0.0)).rgb);
            float r  = getLuminance(Mat._MainTex.Sample(uv + float2( texel.x,      0.0)).rgb);
            float bl = getLuminance(Mat._MainTex.Sample(uv + float2(-texel.x, -texel.y)).rgb);
            float b  = getLuminance(Mat._MainTex.Sample(uv + float2(     0.0, -texel.y)).rgb);
            float br = getLuminance(Mat._MainTex.Sample(uv + float2( texel.x, -texel.y)).rgb);

            float gx = -tl - 2.0*l - bl + tr + 2.0*r + br;
            float gy = -tl - 2.0*t - tr + bl + 2.0*b + br;

            return sqrt(gx*gx + gy*gy);
        }

        float3 applyColorGrading(float3 color) {
            color *= exp2(Mat._PostExposure);
            color = lerp(float3(0.18), color, 1.0 + Mat._Contrast);

            color.r *= 1.0 + Mat._Temperature * 0.1;
            color.b *= 1.0 - Mat._Temperature * 0.1;

            float lum = getLuminance(color);
            color = lerp(float3(lum), color, 1.0 + Mat._Saturation);

            float3 lift = Mat._Lift.rgb;
            float3 gamma = Mat._Gamma.rgb;
            float3 gain = Mat._Gain.rgb;

            color *= (float3(1.0) + gain);
            color += lift * (1.0 - color);

            float3 gammaExp = 1.0 / max(float3(1.0) + gamma, float3(0.01));
            color = pow(max(color, float3(0.0)), gammaExp);

            return max(color, float3(0.0));
        }

        float3 applyLUT(float3 color) {
            float size = Mat._LUTSize;
            float invSize = 1.0 / size;
            float sliceSize = 1.0 / size;

            color = clamp(color, 0.0, 1.0);

            float blueScaled = color.b * (size - 1.0);
            float sliceLow = floor(blueScaled);
            float sliceHigh = min(sliceLow + 1.0, size - 1.0);
            float sliceFrac = blueScaled - sliceLow;

            float halfTexel = 0.5 * invSize;

            float2 innerUV = float2(
                halfTexel + color.r * (1.0 - invSize),
                halfTexel + color.g * (1.0 - invSize)
            );

            float2 uv1 = float2(innerUV.x * sliceSize + sliceLow * sliceSize, innerUV.y);
            float2 uv2 = float2(innerUV.x * sliceSize + sliceHigh * sliceSize, innerUV.y);

            float3 graded1 = Mat._LUTTex.Sample(uv1).rgb;
            float3 graded2 = Mat._LUTTex.Sample(uv2).rgb;

            float3 graded = lerp(graded1, graded2, sliceFrac);
            return lerp(color, graded, Mat._LUTContribution);
        }

        float3 applyFilmGrain(float3 color, float2 uv) {
            float noise = hash12(uv * Mat._Resolution + frac(Frame._Time.y) * 1000.0) * 2.0 - 1.0;
            float lum = getLuminance(color);
            float response = lerp(1.0, lum, Mat._GrainResponse);
            color += noise * Mat._GrainIntensity * response;
            return max(color, float3(0.0));
        }

        float3 applyVignette(float3 color, float2 uv) {
            float2 d = abs(uv - 0.5) * 2.0;
            d = lerp(d, float2(length(d)), Mat._VignetteRoundness);
            float vfactor = pow(clamp(1.0 - dot(d, d) * Mat._VignetteIntensity, 0.0, 1.0), Mat._VignetteSmoothness + 0.01);
            return color * vfactor;
        }

        float3 applyGodRays(float3 color, float2 uv) {
            float2 deltaUV = (uv - Mat._SunScreenPos) * Mat._GodRayDensity / float(Mat._GodRaySamples);
            float2 sampleUV = uv;
            float illumination = 0.0;
            float decay = 1.0;

            int sampleCount = min(Mat._GodRaySamples, 128);
            for (int i = 0; i < sampleCount; i++) {
                sampleUV -= deltaUV;
                float2 clampedUV = clamp(sampleUV, 0.001, 0.999);

                float depth = Mat._CameraDepthTexture.Sample(clampedUV).r;
                float sky = step(0.9999, depth);

                float3 sampleColor = Mat._MainTex.Sample(clampedUV).rgb;
                float brightness = getLuminance(sampleColor);
                float bright = sky * step(Mat._GodRayThreshold, brightness);

                illumination += bright * decay * Mat._GodRayWeight;
                decay *= Mat._GodRayDecay;
            }

            return color + illumination * Mat._GodRayIntensity * float3(1.0, 0.95, 0.85);
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
            float2 uv = input.uv;

            static if (PIXELATION)
                uv = applyPixelation(uv);

            float3 color;
            static if (CHROMATIC_ABERRATION)
                color = applyChromaticAberration(uv);
            else
                color = Mat._MainTex.Sample(uv).rgb;

            float alpha = Mat._MainTex.Sample(input.uv).a;

            static if (SHARPEN)
                color = applyContrastAdaptiveSharpening(int2(uv * Mat._Resolution));

            static if (EDGE_DETECTION)
            {
                float edge = sobelEdge(uv) * Mat._EdgeIntensity;
                edge = clamp(edge, 0.0, 1.0);
                color = lerp(color * Mat._EdgeBackgroundFade, Mat._EdgeColor.rgb, edge);
            }

            static if (COLOR_GRADING)
                color = applyColorGrading(color);

            static if (LUT)
                color = applyLUT(color);

            static if (GOD_RAYS)
                color = applyGodRays(color, input.uv);

            static if (FILM_GRAIN)
                color = applyFilmGrain(color, uv);

            static if (VIGNETTE)
                color = applyVignette(color, uv);

            return float4(color, alpha);
        }
        ENDSLANG
    }
}
