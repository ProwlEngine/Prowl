Shader "Default/EnhancedBokehdoF"

Properties
{
}

// Pass 0: Horizontal MRT (outputs to 3 render targets for R, G, B channels)
Pass "CircularHorizMRT"
{
    Tags { "RenderOrder" = "Opaque" }
    Blend Override
    Cull None
    ZTest Off
    ZWrite Off

    GLSLPROGRAM

    Vertex
    {
        layout (location = 0) in vec3 vertexPosition;
        layout (location = 1) in vec2 vertexTexCoord;

        out vec2 TexCoords;

        void main()
        {
            TexCoords = vertexTexCoord;
            gl_Position = vec4(vertexPosition, 1.0);
        }
    }

    Fragment
    {
        layout(location = 0) out vec4 OutputR;
        layout(location = 1) out vec4 OutputG;
        layout(location = 2) out vec4 OutputB;

        in vec2 TexCoords;

        uniform sampler2D _MainTex;
        uniform sampler2D _CameraDepthTexture;
        uniform vec2 _Resolution;
        uniform float _FocusStrength;
        uniform float _ManualFocusPoint;
        uniform float _MaxBlurRadius;

        // Kernel constants
        #define KERNEL_RADIUS 8
        #define KERNEL_COUNT 17

        // Final composition weights for both kernels
        const vec2 FinalWeights_Kernel0 = vec2(0.411259, -0.548794);
        const vec2 FinalWeights_Kernel1 = vec2(0.513282, 4.561110);

        // Combined kernel coefficients (xy: Kernel0, zw: Kernel1)
        const vec4 CombinedKernels[KERNEL_COUNT] = vec4[](
            vec4( 0.014096, -0.022658, 0.000115, 0.009116),
            vec4(-0.020612, -0.025574, 0.005324, 0.013416),
            vec4(-0.038708,  0.006957, 0.013753, 0.016519),
            vec4(-0.021449,  0.040468, 0.024700, 0.017215),
            vec4( 0.013015,  0.050223, 0.036693, 0.015064),
            vec4( 0.042178,  0.038585, 0.047976, 0.010684),
            vec4( 0.057972,  0.019812, 0.057015, 0.005570),
            vec4( 0.063647,  0.005252, 0.062782, 0.001529),
            vec4( 0.064754,  0.000000, 0.064754, 0.000000),
            vec4( 0.063647,  0.005252, 0.062782, 0.001529),
            vec4( 0.057972,  0.019812, 0.057015, 0.005570),
            vec4( 0.042178,  0.038585, 0.047976, 0.010684),
            vec4( 0.013015,  0.050223, 0.036693, 0.015064),
            vec4(-0.021449,  0.040468, 0.024700, 0.017215),
            vec4(-0.038708,  0.006957, 0.013753, 0.016519),
            vec4(-0.020612, -0.025574, 0.005324, 0.013416),
            vec4( 0.014096, -0.022658, 0.000115, 0.009116)
        );

        // Calculate Circle of Confusion
        float calculateCoC(float depth, float focusPoint)
        {
            float normalizedDepthDiff = abs(depth - focusPoint) / focusPoint;
            float cocPixels = normalizedDepthDiff * _FocusStrength * 0.01 * _Resolution.y;
            float maxBlurPixels = _MaxBlurRadius * 0.01 * _Resolution.y;
            return min(cocPixels, maxBlurPixels);
        }

        void main()
        {
        #ifdef AUTOFOCUS
            float focusPoint = texture(_CameraDepthTexture, vec2(0.5, 0.5)).x;
        #else
            float focusPoint = _ManualFocusPoint;
        #endif

            float depth = texture(_CameraDepthTexture, TexCoords).x;
            float coc = calculateCoC(depth, focusPoint);
            float radius = coc / _Resolution.x / float(KERNEL_RADIUS);

            vec4 rVal = vec4(0.0);
            vec4 gVal = vec4(0.0);
            vec4 bVal = vec4(0.0);

            for (int i = 0; i < KERNEL_COUNT; i++)
            {
                int offset = i - KERNEL_RADIUS;
                vec2 coords = TexCoords + vec2(offset * radius, 0.0);
                coords = clamp(coords, vec2(0.0), vec2(1.0));

                vec3 image = texture(_MainTex, coords).rgb;
                vec4 kernels = CombinedKernels[i];

                rVal += image.r * kernels;
                gVal += image.g * kernels;
                bVal += image.b * kernels;
            }

            OutputR = rVal;
            OutputG = gVal;
            OutputB = bVal;
        }
    }

    ENDGLSL
}

// Pass 1: Vertical Composite (reads from 3 inputs, outputs final result)
Pass "CircularVerticalComposite"
{
    Tags { "RenderOrder" = "Opaque" }
    Blend Override
    Cull None
    ZTest Off
    ZWrite Off

    GLSLPROGRAM

    Vertex
    {
        layout (location = 0) in vec3 vertexPosition;
        layout (location = 1) in vec2 vertexTexCoord;

        out vec2 TexCoords;

        void main()
        {
            TexCoords = vertexTexCoord;
            gl_Position = vec4(vertexPosition, 1.0);
        }
    }

    Fragment
    {
        layout(location = 0) out vec4 OutputColor;

        in vec2 TexCoords;

        uniform sampler2D _HorizR;
        uniform sampler2D _HorizG;
        uniform sampler2D _HorizB;
        uniform sampler2D _CameraDepthTexture;
        uniform vec2 _Resolution;
        uniform float _FocusStrength;
        uniform float _ManualFocusPoint;
        uniform float _MaxBlurRadius;

        // Kernel constants
        #define KERNEL_RADIUS 8
        #define KERNEL_COUNT 17

        // Final composition weights for both kernels
        const vec2 FinalWeights_Kernel0 = vec2(0.411259, -0.548794);
        const vec2 FinalWeights_Kernel1 = vec2(0.513282, 4.561110);

        // Combined kernel coefficients (xy: Kernel0, zw: Kernel1)
        const vec4 CombinedKernels[KERNEL_COUNT] = vec4[](
            vec4( 0.014096, -0.022658, 0.000115, 0.009116),
            vec4(-0.020612, -0.025574, 0.005324, 0.013416),
            vec4(-0.038708,  0.006957, 0.013753, 0.016519),
            vec4(-0.021449,  0.040468, 0.024700, 0.017215),
            vec4( 0.013015,  0.050223, 0.036693, 0.015064),
            vec4( 0.042178,  0.038585, 0.047976, 0.010684),
            vec4( 0.057972,  0.019812, 0.057015, 0.005570),
            vec4( 0.063647,  0.005252, 0.062782, 0.001529),
            vec4( 0.064754,  0.000000, 0.064754, 0.000000),
            vec4( 0.063647,  0.005252, 0.062782, 0.001529),
            vec4( 0.057972,  0.019812, 0.057015, 0.005570),
            vec4( 0.042178,  0.038585, 0.047976, 0.010684),
            vec4( 0.013015,  0.050223, 0.036693, 0.015064),
            vec4(-0.021449,  0.040468, 0.024700, 0.017215),
            vec4(-0.038708,  0.006957, 0.013753, 0.016519),
            vec4(-0.020612, -0.025574, 0.005324, 0.013416),
            vec4( 0.014096, -0.022658, 0.000115, 0.009116)
        );

        // Complex multiplication
        vec2 mulComplex(vec2 p, vec2 q)
        {
            return vec2(p.x * q.x - p.y * q.y, p.x * q.y + p.y * q.x);
        }

        // Calculate Circle of Confusion
        float calculateCoC(float depth, float focusPoint)
        {
            float normalizedDepthDiff = abs(depth - focusPoint) / focusPoint;
            float cocPixels = normalizedDepthDiff * _FocusStrength * 0.01 * _Resolution.y;
            float maxBlurPixels = _MaxBlurRadius * 0.01 * _Resolution.y;
            return min(cocPixels, maxBlurPixels);
        }

        void main()
        {
        #ifdef AUTOFOCUS
            float focusPoint = texture(_CameraDepthTexture, vec2(0.5, 0.5)).x;
        #else
            float focusPoint = _ManualFocusPoint;
        #endif

            float depth = texture(_CameraDepthTexture, TexCoords).x;
            float coc = calculateCoC(depth, focusPoint);
            float radius = coc / _Resolution.y / float(KERNEL_RADIUS);

            vec4 rAcc = vec4(0.0);
            vec4 gAcc = vec4(0.0);
            vec4 bAcc = vec4(0.0);

            for (int i = 0; i < KERNEL_COUNT; i++)
            {
                int offset = i - KERNEL_RADIUS;
                vec2 coords = TexCoords + vec2(0.0, offset * radius);
                coords = clamp(coords, vec2(0.0), vec2(1.0));

                vec4 rVal = texture(_HorizR, coords);
                vec4 gVal = texture(_HorizG, coords);
                vec4 bVal = texture(_HorizB, coords);

                vec4 kernels = CombinedKernels[i];

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

            OutputColor = vec4(r0 + r1, g0 + g1, b0 + b1, 1.0);
        }
    }

    ENDGLSL
}

// Pass 2: Final Combine with original image
Pass "DoFCombine"
{
    Tags { "RenderOrder" = "Opaque" }
    Blend Override
    Cull None
    ZTest Off
    ZWrite Off

    GLSLPROGRAM

    Vertex
    {
        layout (location = 0) in vec3 vertexPosition;
        layout (location = 1) in vec2 vertexTexCoord;

        out vec2 TexCoords;

        void main()
        {
            TexCoords = vertexTexCoord;
            gl_Position = vec4(vertexPosition, 1.0);
        }
    }

    Fragment
    {
        layout(location = 0) out vec4 OutputColor;

        in vec2 TexCoords;

        uniform sampler2D _MainTex;
        uniform sampler2D _BlurredTex;
        uniform sampler2D _CameraDepthTexture;
        uniform float _FocusStrength;
        uniform float _ManualFocusPoint;
        uniform float _MaxBlurRadius;
        uniform vec2 _Resolution;

        float calculateCoC(float depth, float focusPoint)
        {
            float normalizedDepthDiff = abs(depth - focusPoint) / focusPoint;
            float cocPixels = normalizedDepthDiff * _FocusStrength * 0.01 * _Resolution.y;
            float maxBlurPixels = _MaxBlurRadius * 0.01 * _Resolution.y;
            return min(cocPixels, maxBlurPixels);
        }

        void main()
        {
        #ifdef AUTOFOCUS
            float focusPoint = texture(_CameraDepthTexture, vec2(0.5, 0.5)).x;
        #else
            float focusPoint = _ManualFocusPoint;
        #endif

            vec4 originalColor = texture(_MainTex, TexCoords);
            vec4 blurredColor = texture(_BlurredTex, TexCoords);

            float depth = texture(_CameraDepthTexture, TexCoords).x;
            float coc = calculateCoC(depth, focusPoint);

            // Smooth blend based on CoC
            float maxBlurPixels = _MaxBlurRadius * 0.005 * _Resolution.y;
            float blendFactor = smoothstep(0.5, maxBlurPixels * 0.5, coc);

            OutputColor = mix(originalColor, blurredColor, blendFactor);
        }
    }

    ENDGLSL
}
