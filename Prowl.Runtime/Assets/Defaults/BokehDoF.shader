Shader "Hidden/Post Process/Depth of Field"

Properties
{
}

// Bokeh depth of field with a separate near and far field.
//   Pass 0 CoC        full res   signed circle of confusion in full res pixels (negative = in front of focus)
//   Pass 1 Prefilter  blur res   color of the taps that share the block's strongest CoC, and that CoC
//   Pass 2 Bokeh      blur res   far: disk scaled to the pixel's own CoC, taking only samples that reach
//                                back, so sharp objects can't halo into the blurred background;
//                                near: disk at the max CoC, each blurred foreground sample spreading over
//                                what is behind it, weighted so full coverage adds up to 1
//   Pass 3 Postfilter blur res   small tent over both fields to smooth the sample pattern
//   Pass 4 Combine    full res   sharp, then far by the pixel's own CoC, then near by its coverage
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
        uniform float _FocusStrength;   // blur at infinity, percent of the screen height
        uniform float _MaxBlurRadius;   // percent of the screen height
        uniform vec2 _Resolution;       // full resolution

        void main()
        {
            float focus = max(texture(_FocusTex, vec2(0.5)).r, 1e-3);
            float depth = max(linearizeDepthFromProjection(texture(_CameraDepthTexture, TexCoords).x), 1e-4);

            // Lens style: grows without bound toward the camera and levels off at _FocusStrength far away.
            float maxCoC = _MaxBlurRadius * 0.01 * _Resolution.y;
            float coc = (depth - focus) / depth * _FocusStrength * 0.01 * _Resolution.y;
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

        #define MAX_GRID 4

        void main()
        {
            // A grid of bilinear taps, each on the corner of a 2x2 block of source pixels, together
            // covering every source pixel this blur pixel stands for (one exact tap at full res).
            vec2 fullTexel = 1.0 / vec2(textureSize(_MainTex, 0));
            ivec2 cocSize = textureSize(_CoCTex, 0);
            int n = clamp(int(floor(_Downscale * 0.5 + 0.5)), 1, MAX_GRID);

            vec3 colors[MAX_GRID * MAX_GRID];
            float cocs[MAX_GRID * MAX_GRID];
            int taps = 0;
            float cocMin = 0.0;
            float cocMax = 0.0;
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    vec2 uv = TexCoords + (vec2(float(x), float(y)) * 2.0 + 1.0 - float(n)) * fullTexel;
                    vec3 c = texture(_MainTex, uv).rgb;
                    // A NaN or infinite pixel would spread through the whole gather.
                    if (any(isnan(c)) || any(isinf(c))) c = vec3(0.0);
                    colors[taps] = max(c, vec3(0.0));

                    // Point sampled CoC: blending a near and a far value would invent one in between.
                    ivec2 cocPixel = clamp(ivec2(uv * vec2(cocSize)), ivec2(0), cocSize - 1);
                    cocs[taps] = texelFetch(_CoCTex, cocPixel, 0).r;
                    cocMin = min(cocMin, cocs[taps]);
                    cocMax = max(cocMax, cocs[taps]);
                    taps++;
                }
            }

            // The strongest blur of the block, and only the taps sharing it contribute color, so an in focus
            // object on the block's edge doesn't tint a blurred background (or the other way round).
            float chosen = -cocMin > cocMax ? cocMin : cocMax;
            vec3 color = vec3(0.0);
            float weightSum = 0.0;
            for (int i = 0; i < taps; i++)
            {
                float w = 1.0 - clamp(abs(cocs[i] - chosen) / max(abs(chosen), 1.0), 0.0, 1.0);
                color += colors[i] * w;
                weightSum += w;
            }

            OutputColor = vec4(color / max(weightSum, 1e-4), chosen / _Downscale);
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
        layout(location = 0) out vec4 OutputFar;    // rgb = far field
        layout(location = 1) out vec4 OutputNear;   // rgb = near field, a = how much of the pixel it covers

        in vec2 TexCoords;

        #define MAX_KERNEL 71

        uniform sampler2D _MainTex;         // prefiltered: rgb, a = CoC in blur res pixels
        uniform vec2 _Kernel[MAX_KERNEL];   // unit disk sample offsets, center first, evenly spread by area
        uniform int _KernelCount;
        uniform float _MaxCoC;              // largest CoC in blur res pixels

        void main()
        {
            vec2 texel = 1.0 / vec2(textureSize(_MainTex, 0));
            vec4 center = texture(_MainTex, TexCoords);
            int count = clamp(_KernelCount, 1, MAX_KERNEL);

            // A soft edge on each sample's reach so the bokeh edge isn't a hard step.
            const float margin = 1.0;

            // Far field: a disk the size of this pixel's own blur. A sample counts only when its own far
            // blur reaches back here, which keeps sharp and near samples out of it.
            vec4 far = vec4(center.rgb, 1.0);
            float farRadius = max(center.a, 0.0);
            if (farRadius >= 1.0)
            {
                for (int i = 1; i < count; i++)
                {
                    vec2 disp = _Kernel[i] * farRadius;
                    vec4 s = texture(_MainTex, TexCoords + disp * texel);
                    float w = clamp((s.a - length(disp) + margin) / margin, 0.0, 1.0);
                    far += vec4(s.rgb, 1.0) * w;
                }
            }
            far.rgb /= far.a;

            // Near field: a disk the size of the largest blur, since a blurred foreground pixel that far
            // away can still spread over this one. Each sample stands for 1/count of the disk's area and
            // spreads over pi * c^2, so weighting by MaxCoC^2 / (count * c^2) makes full coverage sum to 1.
            float area = _MaxCoC * _MaxCoC / float(count);
            vec4 near = vec4(0.0);
            for (int i = 0; i < count; i++)
            {
                vec2 disp = _Kernel[i] * _MaxCoC;
                vec4 s = texture(_MainTex, TexCoords + disp * texel);
                float c = -s.a;
                float reach = clamp((c - length(disp) + margin) / margin, 0.0, 1.0);
                // Fades in over the first blurred pixels so the edge of focus doesn't pop.
                float blurred = smoothstep(0.5, 2.0, c);
                float w = reach * blurred * area / max(c * c, 1.0);
                near += vec4(s.rgb, 1.0) * w;
            }
            near.rgb /= near.a + (near.a == 0.0 ? 1.0 : 0.0);

            OutputFar = vec4(far.rgb, 1.0);
            OutputNear = vec4(near.rgb, clamp(near.a, 0.0, 1.0));
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
        layout(location = 0) out vec4 OutputFar;
        layout(location = 1) out vec4 OutputNear;

        in vec2 TexCoords;

        uniform sampler2D _FarTex;
        uniform sampler2D _NearTex;

        // Four bilinear taps half a texel out: a 3x3 tent that smooths the gather's sample pattern.
        vec4 Tent(sampler2D tex)
        {
            vec2 h = 0.5 / vec2(textureSize(tex, 0));
            vec4 sum = texture(tex, TexCoords + vec2(-h.x, -h.y));
            sum += texture(tex, TexCoords + vec2( h.x, -h.y));
            sum += texture(tex, TexCoords + vec2(-h.x,  h.y));
            sum += texture(tex, TexCoords + vec2( h.x,  h.y));
            return sum * 0.25;
        }

        void main()
        {
            OutputFar = Tent(_FarTex);
            OutputNear = Tent(_NearTex);
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
        uniform sampler2D _FarTex;      // rgb = far field
        uniform sampler2D _NearTex;     // rgb = near field, a = coverage
        uniform sampler2D _CoCTex;      // full res CoC
        uniform float _Downscale;

        void main()
        {
            vec4 original = texture(_MainTex, TexCoords);
            vec3 far = texture(_FarTex, TexCoords).rgb;
            vec4 near = texture(_NearTex, TexCoords);
            float coc = texture(_CoCTex, TexCoords).r;

            // Sharp, then the far field where this pixel itself is behind focus, then the near field over
            // everything by how much of the pixel it covers.
            float farAlpha = smoothstep(_Downscale, _Downscale * 2.0, coc);
            vec3 color = mix(original.rgb, far, farAlpha);
            color = mix(color, near.rgb, near.a);

            OutputColor = vec4(color, original.a);
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
                // Read unfiltered: a filtered depth on an edge lands between foreground and background.
                ivec2 size = textureSize(_CameraDepthTexture, 0);
                vec2 taps[5] = vec2[](vec2(0.5, 0.5), vec2(0.48, 0.5), vec2(0.52, 0.5), vec2(0.5, 0.48), vec2(0.5, 0.52));
                target = 1e30;
                for (int i = 0; i < 5; i++)
                {
                    ivec2 pixel = clamp(ivec2(taps[i] * vec2(size)), ivec2(0), size - 1);
                    target = min(target, linearizeDepthFromProjection(texelFetch(_CameraDepthTexture, pixel, 0).x));
                }
            }

            float focus = _FocusHistoryValid > 0.5 ? mix(texture(_PrevFocusTex, vec2(0.5)).r, target, _FocusBlend) : target;
            OutputColor = vec4(focus, 0.0, 0.0, 1.0);
        }
    }

    ENDGLSL
}
