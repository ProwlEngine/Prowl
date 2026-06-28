Shader "Default/EnhancedBokehdoF"
{
    Pass
    {
        Name "CircularHorizMRT"
        Tags { "RenderOrder" = "Opaque" }
        Blend One Zero
        Cull Off
        ZTest Disabled
        ZWrite Off

        SLANGPROGRAM

        struct VertexInput { float3 position : POSITION0; float2 uv : TEXCOORD0; }
        struct Varyings { float4 position : SV_Position; float2 uv : TEXCOORD0; }

        struct Material
        {
            float2 _Resolution;
            float _FocusStrength;
            float _ManualFocusPoint;
            float _MaxBlurRadius;
            Sampler2D<float4> _MainTex;
            Sampler2D<float4> _CameraDepthTexture;
        }
        ParameterBlock<Material> Mat;

        struct FragOut
        {
            float4 R : SV_Target0;
            float4 G : SV_Target1;
            float4 B : SV_Target2;
        }

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            Varyings output;
            output.uv = input.uv;
            output.position = float4(input.position, 1.0);
            return output;
        }

        // Kernel constants
        #define KERNEL_RADIUS 8
        #define KERNEL_COUNT 17

        // Final composition weights for both kernels
        static const float2 FinalWeights_Kernel0 = float2(0.411259, -0.548794);
        static const float2 FinalWeights_Kernel1 = float2(0.513282, 4.561110);

        // Combined kernel coefficients (xy: Kernel0, zw: Kernel1)
        static const float4 CombinedKernels[KERNEL_COUNT] = {
            float4( 0.014096, -0.022658, 0.000115, 0.009116),
            float4(-0.020612, -0.025574, 0.005324, 0.013416),
            float4(-0.038708,  0.006957, 0.013753, 0.016519),
            float4(-0.021449,  0.040468, 0.024700, 0.017215),
            float4( 0.013015,  0.050223, 0.036693, 0.015064),
            float4( 0.042178,  0.038585, 0.047976, 0.010684),
            float4( 0.057972,  0.019812, 0.057015, 0.005570),
            float4( 0.063647,  0.005252, 0.062782, 0.001529),
            float4( 0.064754,  0.000000, 0.064754, 0.000000),
            float4( 0.063647,  0.005252, 0.062782, 0.001529),
            float4( 0.057972,  0.019812, 0.057015, 0.005570),
            float4( 0.042178,  0.038585, 0.047976, 0.010684),
            float4( 0.013015,  0.050223, 0.036693, 0.015064),
            float4(-0.021449,  0.040468, 0.024700, 0.017215),
            float4(-0.038708,  0.006957, 0.013753, 0.016519),
            float4(-0.020612, -0.025574, 0.005324, 0.013416),
            float4( 0.014096, -0.022658, 0.000115, 0.009116)
        };

        // Calculate Circle of Confusion
        float calculateCoC(float depth, float focusPoint)
        {
            float normalizedDepthDiff = abs(depth - focusPoint) / focusPoint;
            float cocPixels = normalizedDepthDiff * Mat._FocusStrength * 0.01 * Mat._Resolution.y;
            float maxBlurPixels = Mat._MaxBlurRadius * 0.01 * Mat._Resolution.y;
            return min(cocPixels, maxBlurPixels);
        }

        [shader("fragment")]
        FragOut Fragment(Varyings input)
        {
        #ifdef AUTOFOCUS
            float focusPoint = Mat._CameraDepthTexture.Sample(float2(0.5, 0.5)).x;
        #else
            float focusPoint = Mat._ManualFocusPoint;
        #endif

            float depth = Mat._CameraDepthTexture.Sample(input.uv).x;
            float coc = calculateCoC(depth, focusPoint);
            float radius = coc / Mat._Resolution.x / float(KERNEL_RADIUS);

            float4 rVal = float4(0.0);
            float4 gVal = float4(0.0);
            float4 bVal = float4(0.0);

            for (int i = 0; i < KERNEL_COUNT; i++)
            {
                int offset = i - KERNEL_RADIUS;
                float2 coords = input.uv + float2(offset * radius, 0.0);
                coords = clamp(coords, float2(0.0), float2(1.0));

                float3 image = Mat._MainTex.Sample(coords).rgb;
                float4 kernels = CombinedKernels[i];

                rVal += image.r * kernels;
                gVal += image.g * kernels;
                bVal += image.b * kernels;
            }

            FragOut o;
            o.R = rVal;
            o.G = gVal;
            o.B = bVal;
            return o;
        }

        ENDSLANG
    }

    Pass
    {
        Name "CircularVerticalComposite"
        Tags { "RenderOrder" = "Opaque" }
        Blend One Zero
        Cull Off
        ZTest Disabled
        ZWrite Off

        SLANGPROGRAM

        struct VertexInput { float3 position : POSITION0; float2 uv : TEXCOORD0; }
        struct Varyings { float4 position : SV_Position; float2 uv : TEXCOORD0; }

        struct Material
        {
            float2 _Resolution;
            float _FocusStrength;
            float _ManualFocusPoint;
            float _MaxBlurRadius;
            Sampler2D<float4> _HorizR;
            Sampler2D<float4> _HorizG;
            Sampler2D<float4> _HorizB;
            Sampler2D<float4> _CameraDepthTexture;
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

        // Kernel constants
        #define KERNEL_RADIUS 8
        #define KERNEL_COUNT 17

        // Final composition weights for both kernels
        static const float2 FinalWeights_Kernel0 = float2(0.411259, -0.548794);
        static const float2 FinalWeights_Kernel1 = float2(0.513282, 4.561110);

        // Combined kernel coefficients (xy: Kernel0, zw: Kernel1)
        static const float4 CombinedKernels[KERNEL_COUNT] = {
            float4( 0.014096, -0.022658, 0.000115, 0.009116),
            float4(-0.020612, -0.025574, 0.005324, 0.013416),
            float4(-0.038708,  0.006957, 0.013753, 0.016519),
            float4(-0.021449,  0.040468, 0.024700, 0.017215),
            float4( 0.013015,  0.050223, 0.036693, 0.015064),
            float4( 0.042178,  0.038585, 0.047976, 0.010684),
            float4( 0.057972,  0.019812, 0.057015, 0.005570),
            float4( 0.063647,  0.005252, 0.062782, 0.001529),
            float4( 0.064754,  0.000000, 0.064754, 0.000000),
            float4( 0.063647,  0.005252, 0.062782, 0.001529),
            float4( 0.057972,  0.019812, 0.057015, 0.005570),
            float4( 0.042178,  0.038585, 0.047976, 0.010684),
            float4( 0.013015,  0.050223, 0.036693, 0.015064),
            float4(-0.021449,  0.040468, 0.024700, 0.017215),
            float4(-0.038708,  0.006957, 0.013753, 0.016519),
            float4(-0.020612, -0.025574, 0.005324, 0.013416),
            float4( 0.014096, -0.022658, 0.000115, 0.009116)
        };

        // Complex multiplication
        float2 mulComplex(float2 p, float2 q)
        {
            return float2(p.x * q.x - p.y * q.y, p.x * q.y + p.y * q.x);
        }

        // Calculate Circle of Confusion
        float calculateCoC(float depth, float focusPoint)
        {
            float normalizedDepthDiff = abs(depth - focusPoint) / focusPoint;
            float cocPixels = normalizedDepthDiff * Mat._FocusStrength * 0.01 * Mat._Resolution.y;
            float maxBlurPixels = Mat._MaxBlurRadius * 0.01 * Mat._Resolution.y;
            return min(cocPixels, maxBlurPixels);
        }

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
        #ifdef AUTOFOCUS
            float focusPoint = Mat._CameraDepthTexture.Sample(float2(0.5, 0.5)).x;
        #else
            float focusPoint = Mat._ManualFocusPoint;
        #endif

            float depth = Mat._CameraDepthTexture.Sample(input.uv).x;
            float coc = calculateCoC(depth, focusPoint);
            float radius = coc / Mat._Resolution.y / float(KERNEL_RADIUS);

            float4 rAcc = float4(0.0);
            float4 gAcc = float4(0.0);
            float4 bAcc = float4(0.0);

            for (int i = 0; i < KERNEL_COUNT; i++)
            {
                int offset = i - KERNEL_RADIUS;
                float2 coords = input.uv + float2(0.0, offset * radius);
                coords = clamp(coords, float2(0.0), float2(1.0));

                float4 rVal = Mat._HorizR.Sample(coords);
                float4 gVal = Mat._HorizG.Sample(coords);
                float4 bVal = Mat._HorizB.Sample(coords);

                float4 kernels = CombinedKernels[i];

                rAcc.xy += mulComplex(rVal.xy, kernels.xy);
                rAcc.zw += mulComplex(rVal.zw, kernels.zw);

                gAcc.xy += mulComplex(gVal.xy, kernels.xy);
                gAcc.zw += mulComplex(gVal.zw, kernels.zw);

                bAcc.xy += mulComplex(bVal.xy, kernels.xy);
                bAcc.zw += mulComplex(bVal.zw, kernels.zw);
            }

            float r0 = dot(rAcc.xy, FinalWeights_Kernel0);
            float r1 = dot(rAcc.zw, FinalWeights_Kernel1);

            float g0 = dot(gAcc.xy, FinalWeights_Kernel0);
            float g1 = dot(gAcc.zw, FinalWeights_Kernel1);

            float b0 = dot(bAcc.xy, FinalWeights_Kernel0);
            float b1 = dot(bAcc.zw, FinalWeights_Kernel1);

            return float4(r0 + r1, g0 + g1, b0 + b1, 1.0);
        }

        ENDSLANG
    }

    Pass
    {
        Name "DoFCombine"
        Tags { "RenderOrder" = "Opaque" }
        Blend One Zero
        Cull Off
        ZTest Disabled
        ZWrite Off

        SLANGPROGRAM

        struct VertexInput { float3 position : POSITION0; float2 uv : TEXCOORD0; }
        struct Varyings { float4 position : SV_Position; float2 uv : TEXCOORD0; }

        struct Material
        {
            float _FocusStrength;
            float _ManualFocusPoint;
            float _MaxBlurRadius;
            float2 _Resolution;
            Sampler2D<float4> _MainTex;
            Sampler2D<float4> _BlurredTex;
            Sampler2D<float4> _CameraDepthTexture;
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

        float calculateCoC(float depth, float focusPoint)
        {
            float normalizedDepthDiff = abs(depth - focusPoint) / focusPoint;
            float cocPixels = normalizedDepthDiff * Mat._FocusStrength * 0.01 * Mat._Resolution.y;
            float maxBlurPixels = Mat._MaxBlurRadius * 0.01 * Mat._Resolution.y;
            return min(cocPixels, maxBlurPixels);
        }

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
        #ifdef AUTOFOCUS
            float focusPoint = Mat._CameraDepthTexture.Sample(float2(0.5, 0.5)).x;
        #else
            float focusPoint = Mat._ManualFocusPoint;
        #endif

            float4 originalColor = Mat._MainTex.Sample(input.uv);
            float4 blurredColor = Mat._BlurredTex.Sample(input.uv);

            float depth = Mat._CameraDepthTexture.Sample(input.uv).x;
            float coc = calculateCoC(depth, focusPoint);

            // Smooth blend based on CoC
            float maxBlurPixels = Mat._MaxBlurRadius * 0.005 * Mat._Resolution.y;
            float blendFactor = smoothstep(0.5, maxBlurPixels * 0.5, coc);

            return lerp(originalColor, blurredColor, blendFactor);
        }

        ENDSLANG
    }
}
