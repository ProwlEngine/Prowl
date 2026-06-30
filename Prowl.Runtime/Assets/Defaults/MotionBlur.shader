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

        struct MaterialData
        {
            Sampler2D<float4> _MainTex;
            Sampler2D<float4> _MotionVectorsTex;
            Sampler2D<float4> _CameraDepthTexture;
            float2 _Resolution;
            float _Intensity;
            int _Samples;
            float _MaxBlurRadius;
        }
        ParameterBlock<MaterialData> Mat;

        struct VertexInput
        {
            float3 position : POSITION0;
            float2 uv : TEXCOORD0;
        }
        struct Varyings
        {
            float4 position : SV_Position;
            float2 uv : TEXCOORD0;
        }

        // Interleaved Gradient Noise (Jimenez 2014) for per-pixel jitter
        float IGN(float2 pixCoord)
        {
            const float3 magic = float3(0.06711056, 0.00583715, 52.9829189);
            return frac(magic.z * frac(dot(pixCoord, magic.xy)));
        }

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            Varyings output;
            output.uv = input.uv;
            output.position = float4(input.position, 1.0);
            return output;
        }

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
            float2 texCoords = input.uv;
            float2 texelSize = 1.0 / Mat._Resolution;
            float3 centerColor = Mat._MainTex.Sample(texCoords).rgb;

            float2 motion = Mat._MotionVectorsTex.Sample(texCoords).rg;

            motion *= Mat._Intensity;
            float maxBlurUV = Mat._MaxBlurRadius * max(texelSize.x, texelSize.y);
            float motionLen = length(motion);
            if (motionLen > maxBlurUV)
                motion = motion * (maxBlurUV / motionLen);

            if (length(motion * Mat._Resolution) < 0.5)
                return float4(centerColor, 1.0);

            float dither = IGN(input.position.xy) - 0.5;

            int samples = max(Mat._Samples, 1);
            float3 acc = float3(0.0);
            float totalWeight = 0.0;

            for (int i = 0; i < samples; i++)
            {
                float t = (float(i) + dither) / float(samples) - 0.5;
                float2 sampleUV = texCoords + motion * t;

                sampleUV = clamp(sampleUV, float2(0.0), float2(1.0));

                float3 sampleColor = Mat._MainTex.Sample(sampleUV).rgb;

                float sampleDepth = Mat._CameraDepthTexture.Sample(sampleUV).r;
                float centerDepth = Mat._CameraDepthTexture.Sample(texCoords).r;
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
