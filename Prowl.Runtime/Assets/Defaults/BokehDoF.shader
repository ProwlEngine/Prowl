Shader "Hidden/Post Process/Depth of Field"

Properties
{
}

// Bokeh depth of field with a separate near and far field.
//   Pass 0 CoC        full res   signed circle of confusion in full res pixels (negative = in front of focus)
//   Pass 1 Prefilter  blur res   color and the strongest nearby CoC; in-focus pixels drop out of the blur
//   Pass 2 Bokeh      blur res   disk gather; far samples only spread as far as both they and the center
//                                allow, so sharp foreground can't halo into the blurred background, while
//                                near samples spread over what is behind them and carry a coverage alpha
//   Pass 3 Postfilter blur res   small tent to smooth the sample pattern
//   Pass 4 Combine    full res   far blur by the pixel's own CoC, near blur over everything by its coverage
//   Pass 5 Focus      1x1        focus distance, eased toward its target over time

Pass "CoC"
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
        layout(location = 0) out vec4 OutputCoC;

        #include "ProwlCG"

        in vec2 TexCoords;

        uniform sampler2D _CameraDepthTexture;
        uniform sampler2D _FocusTex;    // 1x1, the smoothed focus distance
        uniform float _FocusStrength;
        uniform float _MaxBlurRadius;   // percent of the screen height
        uniform vec2 _Resolution;       // full resolution

        void main()
        {
            float focus = max(texture(_FocusTex, vec2(0.5)).r, 1e-3);
            float depth = linearizeDepthFromProjection(texture(_CameraDepthTexture, TexCoords).x);
            float maxCoC = _MaxBlurRadius * 0.01 * _Resolution.y;
            float coc = (depth - focus) / focus * _FocusStrength * 0.01 * _Resolution.y;
            OutputCoC = vec4(clamp(coc, -maxCoC, maxCoC), 0.0, 0.0, 1.0);
        }
    }

    ENDGLSL
}

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

        uniform sampler2D _MainTex;     // full res scene color
        uniform sampler2D _CoCTex;      // full res CoC
        uniform float _Downscale;       // full res pixels per blur pixel

        float MaxComponent(vec3 c) { return max(c.r, max(c.g, c.b)); }

        void main()
        {
            // Four taps spread across the block of full res pixels this blur pixel covers.
            vec2 fullTexel = 1.0 / vec2(textureSize(_MainTex, 0));
            vec2 spread = fullTexel * max(_Downscale * 0.25, 0.5);
            vec2 offsets[4] = vec2[](vec2(-1.0, -1.0), vec2(1.0, -1.0), vec2(-1.0, 1.0), vec2(1.0, 1.0));

            vec3 color = vec3(0.0);
            float weightSum = 0.0;
            float cocMin = 0.0;
            float cocMax = 0.0;
            for (int i = 0; i < 4; i++)
            {
                vec2 uv = TexCoords + offsets[i] * spread;
                vec3 c = texture(_MainTex, uv).rgb;
                // Point sampled CoC: blending a near and a far value would invent one in between.
                ivec2 cocSize = textureSize(_CoCTex, 0);
                ivec2 cocPixel = clamp(ivec2(uv * vec2(cocSize)), ivec2(0), cocSize - 1);
                float coc = texelFetch(_CoCTex, cocPixel, 0).r;
                cocMin = min(cocMin, coc);
                cocMax = max(cocMax, coc);

                // Dim the brightest taps so a single hot pixel can't flicker the bokeh.
                float w = 1.0 / (MaxComponent(c) + 1.0);
                color += c * w;
                weightSum += w;
            }
            color /= weightSum;

            // The strongest blur of the block, in blur res pixels.
            float coc = (-cocMin > cocMax ? cocMin : cocMax) / _Downscale;

            // In-focus pixels drop out of the blurred image; the combine takes them sharp from the source.
            color *= smoothstep(0.0, 2.0, abs(coc));

            OutputColor = vec4(color, coc);
        }
    }

    ENDGLSL
}

Pass "Bokeh"
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

        #define MAX_KERNEL 71

        uniform sampler2D _MainTex;         // prefiltered: rgb, a = CoC in blur res pixels
        uniform vec2 _Kernel[MAX_KERNEL];   // unit disk sample offsets
        uniform int _KernelCount;
        uniform float _MaxCoC;              // largest CoC in blur res pixels

        void main()
        {
            vec2 texel = 1.0 / vec2(textureSize(_MainTex, 0));
            vec4 center = texture(_MainTex, TexCoords);

            // A soft margin on each sample's reach so the bokeh edge isn't a hard step.
            const float margin = 2.0;

            vec4 far = vec4(0.0);
            vec4 near = vec4(0.0);
            int count = min(_KernelCount, MAX_KERNEL);
            for (int i = 0; i < count; i++)
            {
                vec2 disp = _Kernel[i] * _MaxCoC;
                float dist = length(disp);
                vec4 s = texture(_MainTex, TexCoords + disp * texel);

                // Far field: a sample reaches only as far as the smaller of its CoC and the center's, so a
                // sharp pixel nearby can't be pulled into this pixel's blur.
                float farCoC = max(min(center.a, s.a), 0.0);
                float farWeight = clamp((farCoC - dist + margin) / margin, 0.0, 1.0);

                // Near field: a blurred foreground sample spreads over whatever is behind it.
                float nearWeight = clamp((-s.a - dist + margin) / margin, 0.0, 1.0);
                // In-focus samples were dimmed by the prefilter, so keep them out of the near field.
                nearWeight *= step(1.0, -s.a);

                far += vec4(s.rgb, 1.0) * farWeight;
                near += vec4(s.rgb, 1.0) * nearWeight;
            }

            far.rgb /= far.a + (far.a == 0.0 ? 1.0 : 0.0);
            near.rgb /= near.a + (near.a == 0.0 ? 1.0 : 0.0);

            // How much of this pixel the near field covers: a sample count turned into an area fraction.
            float nearAlpha = clamp(near.a * 3.14159265 / float(max(count, 1)), 0.0, 1.0);

            OutputColor = vec4(mix(far.rgb, near.rgb, nearAlpha), nearAlpha);
        }
    }

    ENDGLSL
}

Pass "Postfilter"
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

        void main()
        {
            // Four bilinear taps half a texel out: a 3x3 tent that smooths the gather's sample pattern.
            vec2 h = 0.5 / vec2(textureSize(_MainTex, 0));
            vec4 sum = texture(_MainTex, TexCoords + vec2(-h.x, -h.y));
            sum += texture(_MainTex, TexCoords + vec2( h.x, -h.y));
            sum += texture(_MainTex, TexCoords + vec2(-h.x,  h.y));
            sum += texture(_MainTex, TexCoords + vec2( h.x,  h.y));
            OutputColor = sum * 0.25;
        }
    }

    ENDGLSL
}

Pass "Combine"
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

        uniform sampler2D _MainTex;     // full res scene color
        uniform sampler2D _BlurredTex;  // rgb = blurred, a = near field coverage
        uniform sampler2D _CoCTex;      // full res CoC
        uniform float _Downscale;

        void main()
        {
            vec4 original = texture(_MainTex, TexCoords);
            vec4 dof = texture(_BlurredTex, TexCoords);
            float coc = texture(_CoCTex, TexCoords).r;

            // Far field shows where this pixel itself is behind focus; the near field covers everything.
            float farAlpha = smoothstep(_Downscale, _Downscale * 2.0, coc);
            float alpha = farAlpha + dof.a - farAlpha * dof.a;

            OutputColor = vec4(mix(original.rgb, dof.rgb, alpha), original.a);
        }
    }

    ENDGLSL
}

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
