Shader "Default/TAA"
{
    Pass
    {
        Name "Resolve"
        Tags { "RenderOrder" = "Opaque" }

        Cull Off
        ZTest Disabled
        ZWrite Off

        SLANGPROGRAM
        import ProwlCG;

        struct MaterialData
        {
            Sampler2D<float4> _MainTex;          // Current frame (jittered)
            Sampler2D<float4> _HistoryTex;       // Previous frame (resolved)
            Sampler2D<float4> _MotionVectorsTex; // Screen-space motion vectors
            Sampler2D<float4> _CameraDepthTexture;
            float2 _Resolution;
            float2 _Jitter;
            float _HistoryValid;
            float _BlendFactor;
            float _MotionScale;
            float _Sharpness;
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

        // Catmull-Rom bicubic sampling for history (reduces blurriness)
        float4 SampleHistoryCatmullRom(Sampler2D<float4> tex, float2 uv, float2 texelSize)
        {
            float2 position = uv * Mat._Resolution;
            float2 center = floor(position - 0.5) + 0.5;
            float2 f = position - center;
            float2 f2 = f * f;
            float2 f3 = f2 * f;

            float2 w0 = f2 - 0.5 * (f3 + f);
            float2 w1 = 1.5 * f3 - 2.5 * f2 + 1.0;
            float2 w2 = -1.5 * f3 + 2.0 * f2 + 0.5 * f;
            float2 w3 = 0.5 * (f3 - f2);

            float2 w12 = w1 + w2;
            float2 tc12 = (center + w2 / w12) * texelSize;
            float2 tc0 = (center - 1.0) * texelSize;
            float2 tc3 = (center + 2.0) * texelSize;

            float4 result =
                (tex.Sample(float2(tc12.x, tc0.y))  * w12.x +
                 tex.Sample(float2(tc0.x,  tc0.y))  * w0.x  +
                 tex.Sample(float2(tc3.x,  tc0.y))  * w3.x) * w0.y  +
                (tex.Sample(float2(tc12.x, tc12.y)) * w12.x +
                 tex.Sample(float2(tc0.x,  tc12.y)) * w0.x  +
                 tex.Sample(float2(tc3.x,  tc12.y)) * w3.x) * w12.y +
                (tex.Sample(float2(tc12.x, tc3.y))  * w12.x +
                 tex.Sample(float2(tc0.x,  tc3.y))  * w0.x  +
                 tex.Sample(float2(tc3.x,  tc3.y))  * w3.x) * w3.y;

            return max(result, float4(0.0));
        }

        float3 RGBToYCoCg(float3 rgb)
        {
            return float3(
                 0.25 * rgb.r + 0.5 * rgb.g + 0.25 * rgb.b,
                 0.5  * rgb.r                - 0.5  * rgb.b,
                -0.25 * rgb.r + 0.5 * rgb.g - 0.25 * rgb.b
            );
        }

        float3 YCoCgToRGB(float3 ycocg)
        {
            return float3(
                ycocg.x + ycocg.y - ycocg.z,
                ycocg.x            + ycocg.z,
                ycocg.x - ycocg.y - ycocg.z
            );
        }

        float3 Tonemap(float3 c) { return c / (1.0 + luminance(c)); }
        float3 InverseTonemap(float3 c) { return c / max(1.0 - luminance(c), 1e-6); }

        float2 GetClosestMotionVector(float2 uv, float2 texelSize)
        {
            float closestDepth = 1.0;
            float2 closestUV = uv;

            for (int y = -1; y <= 1; y++)
            {
                for (int x = -1; x <= 1; x++)
                {
                    float2 sampleUV = uv + float2(float(x), float(y)) * texelSize;
                    float depth = Mat._CameraDepthTexture.Sample(sampleUV).r;
                    if (depth < closestDepth)
                    {
                        closestDepth = depth;
                        closestUV = sampleUV;
                    }
                }
            }

            return Mat._MotionVectorsTex.Sample(closestUV).rg;
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

            float3 currentColor = Mat._MainTex.Sample(texCoords).rgb;

            if (Mat._HistoryValid < 0.5)
                return float4(currentColor, 1.0);

            float2 motionVector = GetClosestMotionVector(texCoords, texelSize);

            float2 historyUV = texCoords - motionVector;

            bool validReproject = historyUV.x >= 0.0 && historyUV.x <= 1.0 &&
                                  historyUV.y >= 0.0 && historyUV.y <= 1.0;

            if (!validReproject)
                return float4(currentColor, 1.0);

            float3 historyColor = SampleHistoryCatmullRom(Mat._HistoryTex, historyUV, texelSize).rgb;

            float3 m1 = float3(0.0);
            float3 m2 = float3(0.0);

            for (int y = -1; y <= 1; y++)
            {
                for (int x = -1; x <= 1; x++)
                {
                    float3 s = RGBToYCoCg(Tonemap(Mat._MainTex.Sample(texCoords + float2(float(x), float(y)) * texelSize).rgb));
                    m1 += s;
                    m2 += s * s;
                }
            }

            m1 /= 9.0;
            m2 /= 9.0;
            float3 sigma = sqrt(max(m2 - m1 * m1, float3(0.0)));

            float motionLength = length(motionVector * Mat._Resolution);
            float gammaScale = lerp(1.0, 0.5, saturate(motionLength * Mat._MotionScale));

            float3 aabbMin = m1 - gammaScale * sigma;
            float3 aabbMax = m1 + gammaScale * sigma;

            float3 historyYCoCg = RGBToYCoCg(Tonemap(historyColor));
            float3 clippedHistory = clamp(historyYCoCg, aabbMin, aabbMax);
            historyColor = InverseTonemap(YCoCgToRGB(clippedHistory));

            float blendFactor = Mat._BlendFactor;
            blendFactor = lerp(blendFactor, 0.0, saturate(motionLength * 0.1));

            float3 currentTM = Tonemap(currentColor);
            float3 historyTM = Tonemap(historyColor);
            float3 result = InverseTonemap(lerp(currentTM, historyTM, blendFactor));

            if (Mat._Sharpness > 0.0)
            {
                float3 blur = float3(0.0);
                blur += Mat._MainTex.Sample(texCoords + float2(-texelSize.x, 0.0)).rgb;
                blur += Mat._MainTex.Sample(texCoords + float2( texelSize.x, 0.0)).rgb;
                blur += Mat._MainTex.Sample(texCoords + float2(0.0, -texelSize.y)).rgb;
                blur += Mat._MainTex.Sample(texCoords + float2(0.0,  texelSize.y)).rgb;
                blur *= 0.25;

                result += (result - blur) * Mat._Sharpness;
                result = max(result, float3(0.0));
            }

            return float4(result, 1.0);
        }
        ENDSLANG
    }
}
