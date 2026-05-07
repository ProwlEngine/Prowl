Shader "Default/TAA"

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
        #include "Fragment"

        layout(location = 0) out vec4 OutputColor;

        in vec2 TexCoords;

        uniform sampler2D _MainTex;          // Current frame (jittered)
        uniform sampler2D _HistoryTex;       // Previous frame (resolved)
        uniform sampler2D _MotionVectorsTex; // Screen-space motion vectors
        uniform sampler2D _CameraDepthTexture;

        uniform vec2 _Resolution;
        uniform vec2 _Jitter;               // Current frame jitter in pixels
        uniform float _HistoryValid;        // 0 or 1
        uniform float _BlendFactor;         // Feedback weight (0.9-0.97 typical)
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

        // YCoCg color space for better neighborhood clamping
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

        // Tonemap/inverse for stable blending in HDR
        vec3 Tonemap(vec3 c)
        {
            return c / (1.0 + luminance(c));
        }

        vec3 InverseTonemap(vec3 c)
        {
            return c / max(1.0 - luminance(c), 1e-6);
        }

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

            // Unjitter the current sample position
            vec2 unjitteredUV = TexCoords;

            // Sample current color (from jittered render)
            vec3 currentColor = texture(_MainTex, TexCoords).rgb;

            // If no valid history, just output current frame
            if (_HistoryValid < 0.5)
            {
                OutputColor = vec4(currentColor, 1.0);
                return;
            }

            // Get motion vector from closest depth neighbor (reduces edge artifacts)
            vec2 motionVector = GetClosestMotionVector(TexCoords, texelSize);

            // Reproject to find history UV
            vec2 historyUV = TexCoords - motionVector;

            // Check if reprojection is within screen bounds
            bool validReproject = historyUV.x >= 0.0 && historyUV.x <= 1.0 &&
                                  historyUV.y >= 0.0 && historyUV.y <= 1.0;

            if (!validReproject)
            {
                OutputColor = vec4(currentColor, 1.0);
                return;
            }

            // Sample history with Catmull-Rom for sharpness
            vec3 historyColor = SampleHistoryCatmullRom(_HistoryTex, historyUV, texelSize).rgb;

            // Neighborhood clamping in YCoCg space (variance clip)
            // Sample 3x3 neighborhood of current frame
            vec3 m1 = vec3(0.0);
            vec3 m2 = vec3(0.0);

            for (int y = -1; y <= 1; y++)
            {
                for (int x = -1; x <= 1; x++)
                {
                    vec3 s = RGBToYCoCg(Tonemap(texture(_MainTex, TexCoords + vec2(float(x), float(y)) * texelSize).rgb));
                    m1 += s;
                    m2 += s * s;
                }
            }

            // Variance-based AABB clip
            m1 /= 9.0;
            m2 /= 9.0;
            vec3 sigma = sqrt(max(m2 - m1 * m1, vec3(0.0)));

            // Tighter clamping when motion is detected
            float motionLength = length(motionVector * _Resolution);
            float gammaScale = mix(1.0, 0.5, saturate(motionLength * _MotionScale));

            vec3 aabbMin = m1 - gammaScale * sigma;
            vec3 aabbMax = m1 + gammaScale * sigma;

            // Clip history to AABB
            vec3 historyYCoCg = RGBToYCoCg(Tonemap(historyColor));
            vec3 clippedHistory = clamp(historyYCoCg, aabbMin, aabbMax);
            historyColor = InverseTonemap(YCoCgToRGB(clippedHistory));

            // Adaptive blend factor: reduce history weight with fast motion
            float blendFactor = _BlendFactor;
            blendFactor = mix(blendFactor, 0.0, saturate(motionLength * 0.1));

            // Blend in tonemapped space for HDR stability
            vec3 currentTM = Tonemap(currentColor);
            vec3 historyTM = Tonemap(historyColor);
            vec3 result = InverseTonemap(mix(currentTM, historyTM, blendFactor));

            // Optional sharpening (negative lobe)
            if (_Sharpness > 0.0)
            {
                vec3 blur = vec3(0.0);
                blur += texture(_MainTex, TexCoords + vec2(-texelSize.x, 0.0)).rgb;
                blur += texture(_MainTex, TexCoords + vec2( texelSize.x, 0.0)).rgb;
                blur += texture(_MainTex, TexCoords + vec2(0.0, -texelSize.y)).rgb;
                blur += texture(_MainTex, TexCoords + vec2(0.0,  texelSize.y)).rgb;
                blur *= 0.25;

                result += (result - blur) * _Sharpness;
                result = max(result, vec3(0.0));
            }

            OutputColor = vec4(result, 1.0);
        }
    }

    ENDGLSL
}
