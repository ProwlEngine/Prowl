Shader "Default/MotionBlur"
{
    Pass
    {
        Name "MotionBlur"
        Tags { "RenderOrder" = "Opaque" }
        Cull Off
        ZTest Disabled
        ZWrite Off

        SLANGPROGRAM

        import ProwlCG;

        struct VertexInput { float3 position : POSITION0; float2 uv : TEXCOORD0; }
        struct Varyings { float4 position : SV_Position; float2 uv : TEXCOORD0; }

        struct Material
        {
            float2 _Resolution;
            float _Intensity;
            int _Samples;
            float _MaxBlurRadius;    // Max blur in pixels
            Sampler2D<float4> _MainTex;
            Sampler2D<float4> _MotionVectorsTex;
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

        // Interleaved Gradient Noise (Jimenez 2014) for per-pixel jitter
        float IGN(float2 pixCoord)
        {
            const float3 magic = float3(0.06711056, 0.00583715, 52.9829189);
            return frac(magic.z * frac(dot(pixCoord, magic.xy)));
        }

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
            float2 texCoord = input.uv;
            float2 texelSize = 1.0 / Mat._Resolution;
            float3 centerColor = Mat._MainTex.Sample(texCoord).rgb;

            // Sample motion vector
            float2 motion = Mat._MotionVectorsTex.Sample(texCoord).rg;

            // Scale by intensity and clamp to max blur radius (in UV space)
            motion *= Mat._Intensity;
            float maxBlurUV = Mat._MaxBlurRadius * max(texelSize.x, texelSize.y);
            float motionLen = length(motion);
            if (motionLen > maxBlurUV)
                motion = motion * (maxBlurUV / motionLen);

            // Skip blur for near-zero motion
            if (length(motion * Mat._Resolution) < 0.5)
            {
                return float4(centerColor, 1.0);
            }

            // Per-pixel jitter to break banding
            float dither = IGN(input.position.xy) - 0.5;

            int samples = max(Mat._Samples, 1);
            float3 acc = float3(0.0);
            float totalWeight = 0.0;

            for (int i = 0; i < samples; i++)
            {
                // Distribute samples along the motion vector [-0.5, 0.5]
                float t = (float(i) + dither) / float(samples) - 0.5;
                float2 sampleUV = texCoord + motion * t;

                // Clamp to screen
                sampleUV = clamp(sampleUV, float2(0.0), float2(1.0));

                float3 sampleColor = Mat._MainTex.Sample(sampleUV).rgb;

                // Depth-aware weighting: reduce ghosting from background bleeding
                // through foreground by weighting samples closer in depth higher
                float sampleDepth = Mat._CameraDepthTexture.Sample(sampleUV).r;
                float centerDepth = Mat._CameraDepthTexture.Sample(texCoord).r;
                float depthDiff = abs(linearizeDepthFromProjection(sampleDepth)
                                    - linearizeDepthFromProjection(centerDepth));
                float depthWeight = 1.0 / (1.0 + depthDiff * 10.0);

                acc += sampleColor * depthWeight;
                totalWeight += depthWeight;
            }

            return float4(acc / max(totalWeight, 1e-5), 1.0);
        }

        ENDSLANG
    }
}
