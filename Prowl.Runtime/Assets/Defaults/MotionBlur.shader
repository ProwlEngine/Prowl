Shader "Default/MotionBlur"

Properties
{
}

Pass "MotionBlur"
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

        uniform sampler2D _MainTex;
        uniform sampler2D _MotionVectorsTex;
        uniform sampler2D _CameraDepthTexture;

        uniform vec2 _Resolution;
        uniform float _Intensity;
        uniform int _Samples;
        uniform float _MaxBlurRadius;    // Max blur in pixels

        // Interleaved Gradient Noise (Jimenez 2014) for per-pixel jitter
        float IGN(vec2 pixCoord)
        {
            const vec3 magic = vec3(0.06711056, 0.00583715, 52.9829189);
            return fract(magic.z * fract(dot(pixCoord, magic.xy)));
        }

        void main()
        {
            vec2 texelSize = 1.0 / _Resolution;
            vec3 centerColor = texture(_MainTex, TexCoords).rgb;

            // Sample motion vector
            vec2 motion = texture(_MotionVectorsTex, TexCoords).rg;

            // Scale by intensity and clamp to max blur radius (in UV space)
            motion *= _Intensity;
            float maxBlurUV = _MaxBlurRadius * max(texelSize.x, texelSize.y);
            float motionLen = length(motion);
            if (motionLen > maxBlurUV)
                motion = motion * (maxBlurUV / motionLen);

            // Skip blur for near-zero motion
            if (length(motion * _Resolution) < 0.5)
            {
                OutputColor = vec4(centerColor, 1.0);
                return;
            }

            // Per-pixel jitter to break banding
            float dither = IGN(gl_FragCoord.xy) - 0.5;

            int samples = max(_Samples, 1);
            vec3 acc = vec3(0.0);
            float totalWeight = 0.0;

            for (int i = 0; i < samples; i++)
            {
                // Distribute samples along the motion vector [-0.5, 0.5]
                float t = (float(i) + dither) / float(samples) - 0.5;
                vec2 sampleUV = TexCoords + motion * t;

                // Clamp to screen
                sampleUV = clamp(sampleUV, vec2(0.0), vec2(1.0));

                vec3 sampleColor = texture(_MainTex, sampleUV).rgb;

                // Depth-aware weighting: reduce ghosting from background bleeding
                // through foreground by weighting samples closer in depth higher
                float sampleDepth = texture(_CameraDepthTexture, sampleUV).r;
                float centerDepth = texture(_CameraDepthTexture, TexCoords).r;
                float depthDiff = abs(linearizeDepthFromProjection(sampleDepth)
                                    - linearizeDepthFromProjection(centerDepth));
                float depthWeight = 1.0 / (1.0 + depthDiff * 10.0);

                acc += sampleColor * depthWeight;
                totalWeight += depthWeight;
            }

            OutputColor = vec4(acc / max(totalWeight, 1e-5), 1.0);
        }
    }

    ENDGLSL
}
