Shader "Hidden/Post Process/TAA"

Properties
{
}

Pass "Resolve"
{
    Tags { "RenderOrder" = "Opaque" }

    Blend Off
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
        #include "ProwlCG"

        layout(location = 0) out vec4 OutputColor;

        in vec2 TexCoords;

        uniform sampler2D _MainTex;          // Current frame (jittered)
        uniform sampler2D _HistoryTex;       // Previous frame (resolved)
        uniform sampler2D _MotionVectorsTex; // Screen-space motion vectors
        uniform sampler2D _CameraDepthTexture;

        uniform vec2 _Resolution;
        uniform float _HistoryValid;        // 0 or 1
        uniform float _BlendFactor;         // Feedback weight (0.9-0.97 typical)
        uniform float _MotionBlendFactor;   // Feedback weight for fast moving pixels
        uniform float _MotionScale;         // Scale for motion-based rejection
        uniform float _Sharpness;           // Sharpening amount (0-1)

        // Catmull-Rom bicubic sampling for history (reduces blurriness)
        vec4 SampleHistoryCatmullRom(sampler2D tex, vec2 uv, vec2 texelSize)
        {
            vec2 position = uv * _Resolution;
            vec2 center = floor(position - 0.5) + 0.5;
            vec2 f = position - center;
            vec2 f2 = f * f;
            vec2 f3 = f2 * f;

            // Catmull-Rom weights
            vec2 w0 = f2 - 0.5 * (f3 + f);
            vec2 w1 = 1.5 * f3 - 2.5 * f2 + 1.0;
            vec2 w2 = -1.5 * f3 + 2.0 * f2 + 0.5 * f;
            vec2 w3 = 0.5 * (f3 - f2);

            // Optimized to 4 bilinear taps by grouping pairs
            vec2 w12 = w1 + w2;
            vec2 tc12 = (center + w2 / w12) * texelSize;
            vec2 tc0 = (center - 1.0) * texelSize;
            vec2 tc3 = (center + 2.0) * texelSize;

            vec4 result =
                (texture(tex, vec2(tc12.x, tc0.y))  * w12.x +
                 texture(tex, vec2(tc0.x,  tc0.y))  * w0.x  +
                 texture(tex, vec2(tc3.x,  tc0.y))  * w3.x) * w0.y  +
                (texture(tex, vec2(tc12.x, tc12.y)) * w12.x +
                 texture(tex, vec2(tc0.x,  tc12.y)) * w0.x  +
                 texture(tex, vec2(tc3.x,  tc12.y)) * w3.x) * w12.y +
                (texture(tex, vec2(tc12.x, tc3.y))  * w12.x +
                 texture(tex, vec2(tc0.x,  tc3.y))  * w0.x  +
                 texture(tex, vec2(tc3.x,  tc3.y))  * w3.x) * w3.y;

            return max(result, vec4(0.0));
        }

        // YCoCg color space for better neighborhood clipping
        vec3 RGBToYCoCg(vec3 rgb)
        {
            return vec3(
                 0.25 * rgb.r + 0.5 * rgb.g + 0.25 * rgb.b,
                 0.5  * rgb.r                - 0.5  * rgb.b,
                -0.25 * rgb.r + 0.5 * rgb.g - 0.25 * rgb.b
            );
        }

        vec3 YCoCgToRGB(vec3 ycocg)
        {
            return vec3(
                ycocg.x + ycocg.y - ycocg.z,
                ycocg.x            + ycocg.z,
                ycocg.x - ycocg.y - ycocg.z
            );
        }

        float MaxChannel(vec3 c) { return max(c.r, max(c.g, c.b)); }

        // Dividing by the largest channel keeps every channel below 1, so bright HDR pixels can't
        // dominate the blend and the inverse below stays finite.
        vec3 Tonemap(vec3 c) { return c / (1.0 + MaxChannel(c)); }
        vec3 InverseTonemap(vec3 c) { return c / max(1.0 - MaxChannel(c), 1e-4); }

        // NaN or infinite input would otherwise live forever in the history.
        vec3 Sanitize(vec3 c)
        {
            if (any(isnan(c)) || any(isinf(c))) return vec3(0.0);
            return max(c, vec3(0.0));
        }

        vec3 SampleCurrent(vec2 uv) { return RGBToYCoCg(Tonemap(Sanitize(texture(_MainTex, uv).rgb))); }

        // Find closest depth in 3x3 neighborhood for motion vector sampling
        vec2 GetClosestMotionVector(vec2 uv, vec2 texelSize)
        {
            float closestDepth = 1.0;
            vec2 closestUV = uv;

            for (int y = -1; y <= 1; y++)
            {
                for (int x = -1; x <= 1; x++)
                {
                    vec2 sampleUV = uv + vec2(float(x), float(y)) * texelSize;
                    float depth = texture(_CameraDepthTexture, sampleUV).r;
                    if (depth < closestDepth)
                    {
                        closestDepth = depth;
                        closestUV = sampleUV;
                    }
                }
            }

            return texture(_MotionVectorsTex, closestUV).rg;
        }

        void main()
        {
            vec2 texelSize = 1.0 / _Resolution;

            // Everything below works on tonemapped YCoCg and converts back once at the end.
            vec3 m1 = vec3(0.0);
            vec3 m2 = vec3(0.0);
            vec3 boxMin = vec3(1e9);
            vec3 boxMax = vec3(-1e9);
            vec3 current = vec3(0.0);
            for (int y = -1; y <= 1; y++)
            {
                for (int x = -1; x <= 1; x++)
                {
                    vec3 s = SampleCurrent(TexCoords + vec2(float(x), float(y)) * texelSize);
                    if (x == 0 && y == 0) current = s;
                    m1 += s;
                    m2 += s * s;
                    boxMin = min(boxMin, s);
                    boxMax = max(boxMax, s);
                }
            }

            if (_HistoryValid < 0.5)
            {
                OutputColor = vec4(InverseTonemap(max(YCoCgToRGB(current), vec3(0.0))), 1.0);
                return;
            }

            // Get motion vector from closest depth neighbor (reduces edge artifacts)
            vec2 motionVector = GetClosestMotionVector(TexCoords, texelSize);
            vec2 historyUV = TexCoords - motionVector;

            if (historyUV.x < 0.0 || historyUV.x > 1.0 || historyUV.y < 0.0 || historyUV.y > 1.0)
            {
                OutputColor = vec4(InverseTonemap(max(YCoCgToRGB(current), vec3(0.0))), 1.0);
                return;
            }

            // Catmull-Rom keeps the history sharp; its overshoot is clamped away below.
            vec3 history = RGBToYCoCg(Tonemap(Sanitize(SampleHistoryCatmullRom(_HistoryTex, historyUV, texelSize).rgb)));

            m1 /= 9.0;
            m2 /= 9.0;
            vec3 sigma = sqrt(max(m2 - m1 * m1, vec3(0.0)));
            float motionLength = length(motionVector * _Resolution);
            float gamma = mix(1.25, 0.75, saturate(motionLength * _MotionScale));
            vec3 clampMin = m1 - gamma * sigma;
            vec3 clampMax = m1 + gamma * sigma;
            clampMin.yz = max(clampMin.yz, boxMin.yz);
            clampMax.yz = min(clampMax.yz, boxMax.yz);
            history = clamp(history, clampMin, clampMax);

            // Ease toward the motion weight as the pixel moves faster. History never drops out entirely:
            // the current frame is jittered, so without any history a fast pan shows the raw jitter.
            float blendFactor = mix(_BlendFactor, _MotionBlendFactor, saturate(motionLength * 0.1));

            float lumaDifference = abs(current.x - history.x) / max(max(current.x, history.x), 0.2);
            blendFactor = mix(blendFactor, max(blendFactor, 0.98), (1.0 - lumaDifference) * (1.0 - lumaDifference));

            // Blending in tonemapped space is what makes edges of very bright surfaces visibly smooth. A linear
            // average there stays far past white and the edge looks as aliased as without TAA.
            vec3 result = YCoCgToRGB(mix(current, history, blendFactor));
            OutputColor = vec4(InverseTonemap(max(result, vec3(0.0))), 1.0);
        }
    }

    ENDGLSL
}

Pass "Sharpen"
{
    Tags { "RenderOrder" = "Opaque" }

    Blend Off
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

        uniform sampler2D _MainTex;   // Resolved frame
        uniform vec2 _Resolution;
        uniform float _Sharpness;

        float MaxChannel(vec3 c) { return max(c.r, max(c.g, c.b)); }
        vec3 Tonemap(vec3 c) { return c / (1.0 + MaxChannel(c)); }
        vec3 InverseTonemap(vec3 c) { return c / max(1.0 - MaxChannel(c), 1e-4); }
        float Luma(vec3 c) { return dot(c, vec3(0.299, 0.587, 0.114)); }

        // Sharpens brightness only, in tonemapped space, after the resolve was stored as history. Working
        // on linear HDR color let one very bright neighbor pull a single channel down and tint the edge.
        void main()
        {
            vec2 texel = 1.0 / _Resolution;
            vec3 center = Tonemap(texture(_MainTex, TexCoords).rgb);
            float blur = (Luma(Tonemap(texture(_MainTex, TexCoords + vec2(-texel.x, 0.0)).rgb)) +
                          Luma(Tonemap(texture(_MainTex, TexCoords + vec2( texel.x, 0.0)).rgb)) +
                          Luma(Tonemap(texture(_MainTex, TexCoords + vec2(0.0, -texel.y)).rgb)) +
                          Luma(Tonemap(texture(_MainTex, TexCoords + vec2(0.0,  texel.y)).rgb))) * 0.25;
            float luma = Luma(center);
            float sharpened = max(luma + (luma - blur) * _Sharpness, 0.0);
            vec3 result = luma > 1e-5 ? center * (sharpened / luma) : center;
            OutputColor = vec4(InverseTonemap(min(result, vec3(0.999))), 1.0);
        }
    }

    ENDGLSL
}
