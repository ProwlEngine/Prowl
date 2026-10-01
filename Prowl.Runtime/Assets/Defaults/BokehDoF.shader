Shader "Hidden/Post Process/Depth of Field"

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

        #include "ProwlCG"

        in vec2 TexCoords;

        uniform sampler2D _MainTex;
        uniform sampler2D _CameraDepthTexture;
        uniform vec2 _Resolution;
        uniform float _FocusStrength;
        uniform sampler2D _FocusTex;    // 1x1, the smoothed focus distance
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
            float normalizedDepthDiff = abs(depth - focusPoint) / max(focusPoint, 1e-3);
            float cocPixels = normalizedDepthDiff * _FocusStrength * 0.01 * _Resolution.y;
            float maxBlurPixels = _MaxBlurRadius * 0.01 * _Resolution.y;
            return min(cocPixels, maxBlurPixels);
        }

        void main()
        {
            float focusPoint = texture(_FocusTex, vec2(0.5)).r;

            float depth = linearizeDepthFromProjection(texture(_CameraDepthTexture, TexCoords).x);
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

        #include "ProwlCG"

        in vec2 TexCoords;

        uniform sampler2D _HorizR;
        uniform sampler2D _HorizG;
        uniform sampler2D _HorizB;
        uniform sampler2D _CameraDepthTexture;
        uniform vec2 _Resolution;
        uniform float _FocusStrength;
        uniform sampler2D _FocusTex;    // 1x1, the smoothed focus distance
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
            float normalizedDepthDiff = abs(depth - focusPoint) / max(focusPoint, 1e-3);
            float cocPixels = normalizedDepthDiff * _FocusStrength * 0.01 * _Resolution.y;
            float maxBlurPixels = _MaxBlurRadius * 0.01 * _Resolution.y;
            return min(cocPixels, maxBlurPixels);
        }

        void main()
        {
            float focusPoint = texture(_FocusTex, vec2(0.5)).r;

            float depth = linearizeDepthFromProjection(texture(_CameraDepthTexture, TexCoords).x);
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

        #include "ProwlCG"

        in vec2 TexCoords;

        uniform sampler2D _MainTex;
        uniform sampler2D _BlurredTex;
        uniform sampler2D _CameraDepthTexture;
        uniform float _FocusStrength;
        uniform sampler2D _FocusTex;    // 1x1, the smoothed focus distance
        uniform float _MaxBlurRadius;
        uniform vec2 _Resolution;

        float calculateCoC(float depth, float focusPoint)
        {
            float normalizedDepthDiff = abs(depth - focusPoint) / max(focusPoint, 1e-3);
            float cocPixels = normalizedDepthDiff * _FocusStrength * 0.01 * _Resolution.y;
            float maxBlurPixels = _MaxBlurRadius * 0.01 * _Resolution.y;
            return min(cocPixels, maxBlurPixels);
        }

        void main()
        {
            float focusPoint = texture(_FocusTex, vec2(0.5)).r;

            vec4 originalColor = texture(_MainTex, TexCoords);
            vec4 blurredColor = texture(_BlurredTex, TexCoords);

            float depth = linearizeDepthFromProjection(texture(_CameraDepthTexture, TexCoords).x);
            float coc = calculateCoC(depth, focusPoint);

            // Smooth blend based on CoC
            float maxBlurPixels = _MaxBlurRadius * 0.005 * _Resolution.y;
            float blendFactor = smoothstep(0.5, maxBlurPixels * 0.5, coc);

            OutputColor = vec4(mix(originalColor.rgb, blurredColor.rgb, blendFactor), originalColor.a);
        }
    }

    ENDGLSL
}

// Pass 3: Prefilter the scene down to the blur resolution, so quarter and eighth resolution blurs
// average every source pixel instead of picking one per texel and shimmering.
Pass "Prefilter"
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
        uniform float _PrefilterOffset;   // source texels from the center to each tap, a quarter of the downscale

        void main()
        {
            // Four bilinear taps spread across the block of source pixels this output pixel covers.
            vec2 offset = _PrefilterOffset / vec2(textureSize(_MainTex, 0));
            vec4 sum = texture(_MainTex, TexCoords + vec2(-offset.x, -offset.y));
            sum += texture(_MainTex, TexCoords + vec2( offset.x, -offset.y));
            sum += texture(_MainTex, TexCoords + vec2(-offset.x,  offset.y));
            sum += texture(_MainTex, TexCoords + vec2( offset.x,  offset.y));
            OutputColor = sum * 0.25;
        }
    }

    ENDGLSL
}

// Pass 4: Resolve the focus distance into a 1x1 target, easing toward the new value over time so
// autofocus glides instead of snapping as things pass the center of the screen.
Pass "Focus"
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

        #include "ProwlCG"

        in vec2 TexCoords;

        uniform sampler2D _CameraDepthTexture;
        uniform sampler2D _PrevFocusTex;
        uniform float _UseAutoFocus;
        uniform float _ManualFocusPoint;
        uniform float _FocusBlend;          // how far to move toward the new focus this frame
        uniform float _FocusHistoryValid;   // 0 = jump straight to the new focus

        void main()
        {
            float target = _ManualFocusPoint;
            if (_UseAutoFocus > 0.5)
            {
                // The nearest of a small cross around the center, so a thin object there still holds focus.
                vec2 taps[5] = vec2[](vec2(0.5, 0.5), vec2(0.48, 0.5), vec2(0.52, 0.5), vec2(0.5, 0.48), vec2(0.5, 0.52));
                target = 1e30;
                for (int i = 0; i < 5; i++)
                    target = min(target, linearizeDepthFromProjection(texture(_CameraDepthTexture, taps[i]).x));
            }

            float focus = _FocusHistoryValid > 0.5 ? mix(texture(_PrevFocusTex, vec2(0.5)).r, target, _FocusBlend) : target;
            OutputColor = vec4(focus, 0.0, 0.0, 1.0);
        }
    }

    ENDGLSL
}
