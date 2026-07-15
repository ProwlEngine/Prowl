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
        #include "ProwlCG"

        layout(location = 0) out vec4 OutputColor;

        in vec2 TexCoords;

        uniform sampler2D _MainTex;          // Current frame (jittered)
        uniform sampler2D _HistoryTex;       // Previous frame (resolved with depth in alpha)
        uniform sampler2D _MotionVectorsTex; // Screen-space motion vectors
        uniform sampler2D _CameraDepthTexture;

        uniform vec2 _Resolution;
        uniform vec2 _Jitter;               // Subpixel jitter offset in pixels (-0.5 to 0.5)
        uniform float _HistoryValid;        // 0 or 1
        uniform float _BlendFactor;         // Feedback weight (0.9-0.97 typical)
        uniform float _VarianceGamma;       // Statistical box width in stddevs
        uniform float _MotionScale;         // UV-space motion scaling factor
        uniform float _NearPlane;           // Camera near plane
        uniform float _FarPlane;            // Camera far plane
        uniform float _Sharpness;           // Post-resolve sharpening

        // Linearizes depth (0=near, 1=far standard depth buffers)
        float LinearizeDepth(float d, float n, float f)
        {
            return (n * f) / (f - d * (f - n));
        }

        float Luminance(vec3 c)
        {
            return dot(c, vec3(0.2126, 0.7152, 0.0722));
        }

        // Transforms standard RGB to YCoCg space
        vec3 RGBToYCoCg(vec3 rgb)
        {
            return vec3(
                 0.25 * rgb.r + 0.5 * rgb.g + 0.25 * rgb.b,
                 0.5  * rgb.r                - 0.5  * rgb.b,
                -0.25 * rgb.r + 0.5 * rgb.g - 0.25 * rgb.b
            );
        }

        // Transforms YCoCg back to standard RGB
        vec3 YCoCgToRGB(vec3 ycocg)
        {
            float t = ycocg.x - ycocg.z;
            return vec3(t + ycocg.y, ycocg.x + ycocg.z, t - ycocg.y);
        }

        // Clamps history color directly towards the center of the variance box
        vec3 ClipToAABB(vec3 color, vec3 aabbMin, vec3 aabbMax)
        {
            vec3 center  = (aabbMax + aabbMin) * 0.5;
            vec3 extents = (aabbMax - aabbMin) * 0.5;
            vec3 shift   = color - center;
            vec3 absUnit = abs(shift / max(extents, vec3(1e-4)));
            float maxUnit = max(max(absUnit.x, absUnit.y), absUnit.z);
            return maxUnit > 1.0 ? center + (shift / maxUnit) : color;
        }

        // 5-tap Catmull-Rom (Karis) reconstruction filter
        vec3 SampleHistoryCatmullRom(sampler2D tex, vec2 uv, vec2 texSize)
        {
            vec2 samplePos = uv * texSize;
            vec2 tc1 = floor(samplePos - 0.5) + 0.5;
            vec2 f  = samplePos - tc1;
            vec2 w0 = f * (-0.5 + f * (1.0 - 0.5 * f));
            vec2 w1 = 1.0 + f * f * (-2.5 + 1.5 * f);
            vec2 w2 = f * (0.5 + f * (2.0 - 1.5 * f));
            vec2 w3 = f * f * (-0.5 + 0.5 * f);
            vec2 w12 = w1 + w2;
            vec2 tc0  = (tc1 - 1.0) / texSize;
            vec2 tc3  = (tc1 + 2.0) / texSize;
            vec2 tc12 = (tc1 + w2 / w12) / texSize;
            vec3 r = vec3(0.0);
            float wSum = 0.0;

            r += textureLod(tex, vec2(tc12.x, tc0.y),  0.0).rgb * (w12.x * w0.y);  wSum += w12.x * w0.y;
            r += textureLod(tex, vec2(tc0.x,  tc12.y), 0.0).rgb * (w0.x  * w12.y); wSum += w0.x  * w12.y;
            r += textureLod(tex, vec2(tc12.x, tc12.y), 0.0).rgb * (w12.x * w12.y); wSum += w12.x * w12.y;
            r += textureLod(tex, vec2(tc3.x,  tc12.y), 0.0).rgb * (w3.x  * w12.y); wSum += w3.x  * w12.y;
            r += textureLod(tex, vec2(tc12.x, tc3.y),  0.0).rgb * (w12.x * w3.y);  wSum += w12.x * w3.y;

            return max(r / max(wSum, 1e-5), vec3(0.0));
        }

        void main()
        {
            vec2 texelSize = 1.0 / _Resolution;

            // Convert pixel jitter to UV-space offset
            vec2 jitterUV = _Jitter / _Resolution;

            // Unjittered UV for sampling the current frame's center properly
            vec2 unjitteredTexCoords = TexCoords - jitterUV;

            vec3 current = texture(_MainTex, unjitteredTexCoords).rgb;

            // This pixel's surface depth (linear) is stored in the history alpha for the disocclusion check next frame
            float centerLin = LinearizeDepth(texture(_CameraDepthTexture, unjitteredTexCoords).r, _NearPlane, _FarPlane);

            // Closest depth in a 3x3 neighborhood -> stable motion-vector selection (reduces silhouette ghosting)
            float closestDepth = 1.0;
            vec2 closestUV = unjitteredTexCoords;
            for (int y = -1; y <= 1; ++y)
            {
                for (int x = -1; x <= 1; ++x)
                {
                    vec2 s = unjitteredTexCoords + vec2(float(x), float(y)) * texelSize;
                    float d = texture(_CameraDepthTexture, s).r;
                    if (d < closestDepth)
                    {
                        closestDepth = d;
                        closestUV = s;
                    }
                }
            }

            vec2 motion = texture(_MotionVectorsTex, closestUV).rg;
            vec2 historyUV = unjitteredTexCoords - motion;

            // Fallback if no valid history or coordinates are out of bounds
            if (_HistoryValid < 0.5 || any(lessThan(historyUV, vec2(0.0))) || any(greaterThan(historyUV, vec2(1.0))))
            {
                OutputColor = vec4(current, centerLin);
                return;
            }

            // Depth-based disocclusion reject: compares reprojected history's linear depth with current
            float linPrev = texture(_HistoryTex, historyUV).a;
            float motionPx = length(motion / texelSize); // motion-vector magnitude in pixels
            if (linPrev > 0.0 && motionPx > 0.5)
            {
                float linCur = LinearizeDepth(closestDepth, _NearPlane, _FarPlane);
                float relDiff = abs(linCur - linPrev) / max(min(linCur, linPrev), 0.001);
                if (relDiff > 0.03)
                {
                    OutputColor = vec4(current, centerLin);
                    return;
                }
            }

            // YCoCg neighborhood statistics: mean (m1) + mean-of-squares (m2) over 3x3 -> variance box
            vec3 m1 = vec3(0.0);
            vec3 m2 = vec3(0.0);
            for (int ny = -1; ny <= 1; ++ny)
            {
                for (int nx = -1; nx <= 1; ++nx)
                {
                    vec3 y = RGBToYCoCg(texture(_MainTex, unjitteredTexCoords + vec2(float(nx), float(ny)) * texelSize).rgb);
                    m1 += y;
                    m2 += y * y;
                }
            }

            m1 /= 9.0;
            m2 /= 9.0;
            vec3 sigma = sqrt(max(m2 - m1 * m1, vec3(0.0)));
            vec3 boxMin = m1 - _VarianceGamma * sigma;
            vec3 boxMax = m1 + _VarianceGamma * sigma;

            // Sample reprojected history with sharp Catmull-Rom filter and clip it to our variance box
            vec3 curY = RGBToYCoCg(current);
            vec3 histY = RGBToYCoCg(SampleHistoryCatmullRom(_HistoryTex, historyUV, _Resolution));
            histY = ClipToAABB(histY, boxMin, boxMax);

            // Blend: drop history as motion grows to avoid blurring fast motion
            float motionMag = clamp(length(motion) * _MotionScale, 0.0, 1.0);
            float blend = clamp(_BlendFactor * (1.0 - 0.5 * motionMag), 0.0, 0.99);

            vec3 result = YCoCgToRGB(mix(curY, histY, blend));
            result = max(result, vec3(0.0));

            // Post-resolve sharpening (negative-lobe)
            if (_Sharpness > 0.0)
            { 
                vec3 blur = vec3(0.0);
                blur += texture(_MainTex, unjitteredTexCoords + vec2(-texelSize.x, 0.0)).rgb;
                blur += texture(_MainTex, unjitteredTexCoords + vec2( texelSize.x, 0.0)).rgb;
                blur += texture(_MainTex, unjitteredTexCoords + vec2(0.0, -texelSize.y)).rgb;
                blur += texture(_MainTex, unjitteredTexCoords + vec2(0.0,  texelSize.y)).rgb;
                blur *= 0.25;

                result += (result - blur) * _Sharpness;
                result = max(result, vec3(0.0));
            }

            // Store RGB color and store linear depth in Alpha for next frame's disocclusion check
            OutputColor = vec4(result, centerLin);
        }
    }

    ENDGLSL
}
