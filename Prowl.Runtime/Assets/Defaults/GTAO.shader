Shader "Default/GTAO"
{
    Pass
    {
        Name "CalculateGTAO"
        Tags { "RenderOrder" = "Opaque" }
        ZTest Disabled
        ZWrite Off
        Cull Off

        SLANGPROGRAM

        import ProwlCG;

        struct VertexInput { float3 position : POSITION0; float2 uv : TEXCOORD0; }
        struct Varyings { float4 position : SV_Position; float2 uv : TEXCOORD0; }

        struct Material
        {
            int _Slices;
            int _DirectionSamples;
            float _Radius;
            float _Intensity;
            float2 _NoiseScale;   // ao-res / noise-res, tiles the blue noise 1:1 per pixel
            float2 _JitterOffset; // per-frame Halton offset so temporal accumulation converges
            Sampler2D<float4> _CameraDepthTexture;
            Sampler2D<float4> _CameraNormalsTexture; // View-space normals from depth pre-pass
            Sampler2D<float4> _Noise;
        }
        ParameterBlock<Material> Mat;

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            Varyings output;
            output.position = float4(input.position, 1.0);
            output.uv = input.uv;
            return output;
        }

        void SampleHorizonCos(float2 coord, float2 offset, float3 viewPos, float3 viewDir, float2 falloff, inout float cHorizonCos) {
            float2 sTexCoord = coord + offset;

            // Check bounds
            if (sTexCoord.x < 0.0 || sTexCoord.x > 1.0 || sTexCoord.y < 0.0 || sTexCoord.y > 1.0)
                return;

            float sDepth = Mat._CameraDepthTexture.Sample(sTexCoord).r;
            if (sDepth >= 1.0) return;

            float3 sHorizonV = getViewPos(sTexCoord, sDepth) - viewPos;

            float sLenV = sdot(sHorizonV);
            // Reject samples that reconstruct to essentially the same view position as the center.
            float minLen = 0.0015 * abs(viewPos.z);
            if (sLenV < minLen * minLen) return;
            float sNormV = rsqrt(sLenV);

            float sHorizonCos = dot(sHorizonV, viewDir) * sNormV;
            sHorizonCos = lerp(sHorizonCos, cHorizonCos, linearstep(falloff.x, falloff.y, sLenV));
            cHorizonCos = max(sHorizonCos, cHorizonCos);
        }

        float CalculateGTAO(float2 coord, float3 viewPos, float3 normal, float2 dither) {
            float viewDistance = sdot(viewPos);
            float norm = rsqrt(viewDistance);
            viewDistance *= norm;

            float3 viewDir = viewPos * -norm;

            int sliceCount = Mat._Slices;
            float rSliceCount = 1.0 / float(sliceCount);

            int sampleCount = Mat._DirectionSamples;
            float rSampleCount = 1.0 / float(sampleCount);

            float radius = Mat._Radius * saturate(0.25 + viewDistance * rcp(64.0));
            float2 sRadius = rSampleCount * radius * norm * diagonal2(Frame.prowl_MatP);
            // Floor the per-sample step so consecutive samples are at least ~1.5 texels apart.
            sRadius = max(sRadius, float2(1.5) / Frame._ScreenParams.xy);
            float2 falloff = sqr(radius * float2(1.0, 4.0));

            float visibility = 0.0;

            for (int slice = 0; slice < sliceCount; ++slice) {
                float slicePhi = (float(slice) + dither.x) * (PROWL_PI * rSliceCount);

                float3 directionV = float3(cos(slicePhi), sin(slicePhi), 0.0);
                float3 orthoDirectionV = directionV - dot(directionV, viewDir) * viewDir;
                float3 axisV = cross(directionV, viewDir);
                float3 projNormalV = normal - axisV * dot(normal, axisV);

                float lenV = sdot(projNormalV);
                float normV = rsqrt(lenV);
                lenV *= normV;

                float sgnN = fastSign(dot(orthoDirectionV, projNormalV));
                float cosN = saturate(dot(projNormalV, viewDir) * normV);
                float n = sgnN * fastAcos(cosN);

                float2 cHorizonCos = float2(-1.0);

                for (int samp = 0; samp < sampleCount; ++samp) {
                    float2 stepDir = directionV.xy * sRadius;
                    float2 offset = (float(samp) + dither.y) * stepDir;

                    SampleHorizonCos(coord, offset, viewPos, viewDir, falloff, cHorizonCos.x);
                    SampleHorizonCos(coord, -offset, viewPos, viewDir, falloff, cHorizonCos.y);
                }

                float2 h = n + clamp(float2(fastAcos(cHorizonCos.x), -fastAcos(cHorizonCos.y)) - n, -PROWL_HALF_PI, PROWL_HALF_PI);
                h = cosN + 2.0 * h * sin(n) - cos(2.0 * h - n);

                visibility += lenV * (h.x + h.y);
            }

            return 0.25 * rSliceCount * visibility;
        }

        float3 ApproxMultiBounce(float ao, float3 albedo) {
            float3 a = 2.0404 * albedo - 0.3324;
            float3 b = 4.7951 * albedo - 0.6417;
            float3 c = 2.7552 * albedo + 0.6903;

            return max(float3(ao), ((ao * a - b) * ao + c) * ao);
        }

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
            float depth = Mat._CameraDepthTexture.Sample(input.uv).r;

            // Sky
            if (depth >= 1.0) {
                return float4(1.0);
            }

            // Get view space data
            float3 viewPos = getViewPos(input.uv, depth);

            // Get view space normal from GBuffer
            float4 normalData = Mat._CameraNormalsTexture.Sample(input.uv);
            float3 viewNormal = normalize(normalData.xyz * 2.0 - 1.0);

            // Blue-noise dither scrolled each frame by a Halton offset.
            float2 noise = Mat._Noise.Sample(input.uv * Mat._NoiseScale + Mat._JitterOffset).rg;

            // Calculate GTAO
            float ao = CalculateGTAO(input.uv, viewPos, viewNormal, noise);

            // Apply intensity
            ao = pow(saturate(ao), Mat._Intensity);

            return float4(ao, ao, ao, 1.0);
        }

        ENDSLANG
    }

    Pass
    {
        Name "Blur"
        Tags { "RenderOrder" = "Opaque" }
        ZTest Disabled
        ZWrite Off
        Cull Off

        SLANGPROGRAM

        import ProwlCG;

        struct VertexInput { float3 position : POSITION0; float2 uv : TEXCOORD0; }
        struct Varyings { float4 position : SV_Position; float2 uv : TEXCOORD0; }

        struct Material
        {
            float2 _BlurDirection;
            float _BlurRadius;
            Sampler2D<float4> _MainTex;
            Sampler2D<float4> _CameraDepthTexture;
        }
        ParameterBlock<Material> Mat;

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            Varyings output;
            output.position = float4(input.position, 1.0);
            output.uv = input.uv;
            return output;
        }

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
            float2 texelSize = 1.0 / Frame._ScreenParams.xy;
            float centerDepth = Mat._CameraDepthTexture.Sample(input.uv).r;

            float4 result = Mat._MainTex.Sample(input.uv);
            float totalWeight = 1.0;

            // Depth-aware bilateral blur
            for (int i = -2; i <= 2; i++) {
                if (i == 0) continue;

                float offset = float(i) * Mat._BlurRadius;
                float2 sampleUV = input.uv + Mat._BlurDirection * texelSize * offset;

                float sampleDepth = Mat._CameraDepthTexture.Sample(sampleUV).r;
                float depthDiff = abs(centerDepth - sampleDepth);

                // Weight based on depth similarity
                float weight = exp(-depthDiff * 100.0) * exp(-0.5 * float(i * i) / 2.0);

                result += Mat._MainTex.Sample(sampleUV) * weight;
                totalWeight += weight;
            }

            return result / totalWeight;
        }

        ENDSLANG
    }

    Pass
    {
        Name "Composite"
        Tags { "RenderOrder" = "Opaque" }
        ZTest Disabled
        ZWrite Off
        Cull Off

        SLANGPROGRAM

        import ProwlCG;

        struct VertexInput { float3 position : POSITION0; float2 uv : TEXCOORD0; }
        struct Varyings { float4 position : SV_Position; float2 uv : TEXCOORD0; }

        struct Material
        {
            float _Intensity;
            Sampler2D<float4> _MainTex;
            Sampler2D<float4> _AOTex;
        }
        ParameterBlock<Material> Mat;

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            Varyings output;
            output.position = float4(input.position, 1.0);
            output.uv = input.uv;
            return output;
        }

        float3 ApproxMultiBounce(float ao, float3 albedo) {
            float3 a = 2.0404 * albedo - 0.3324;
            float3 b = 4.7951 * albedo - 0.6417;
            float3 c = 2.7552 * albedo + 0.6903;

            return max(float3(ao), ((ao * a - b) * ao + c) * ao);
        }

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
            float4 sceneColor = Mat._MainTex.Sample(input.uv);
            float ao = Mat._AOTex.Sample(input.uv).r;

            float3 finalColor = sceneColor.rgb;

            finalColor *= ao;

            return float4(finalColor, sceneColor.a);
        }

        ENDSLANG
    }

    Pass
    {
        Name "Temporal"
        Tags { "RenderOrder" = "Opaque" }
        ZTest Disabled
        ZWrite Off
        Cull Off

        SLANGPROGRAM

        import ProwlCG;

        struct VertexInput { float3 position : POSITION0; float2 uv : TEXCOORD0; }
        struct Varyings { float4 position : SV_Position; float2 uv : TEXCOORD0; }

        struct Material
        {
            float _TResponse;
            Sampler2D<float4> _MainTex;        // current-frame AO
            Sampler2D<float4> _PreviousBuffer; // accumulated AO history
            Sampler2D<float4> _CameraMotionVectorsTexture; // .rg motion
        }
        ParameterBlock<Material> Mat;

        [shader("vertex")]
        Varyings Vertex(VertexInput input)
        {
            Varyings output;
            output.position = float4(input.position, 1.0);
            output.uv = input.uv;
            return output;
        }

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
            float current = Mat._MainTex.Sample(input.uv).r;

            float2 velocity = Mat._CameraMotionVectorsTexture.Sample(input.uv).rg;
            float2 prevUV = input.uv - velocity;

            // Neighbourhood clamp: bound the history to the 3x3 range of the current AO.
            uint texW, texH;
            Mat._MainTex.GetDimensions(texW, texH);
            float2 texel = 1.0 / float2(texW, texH);
            float mn = current, mx = current;
            for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
            {
                float s = Mat._MainTex.Sample(input.uv + texel * float2(float(x), float(y))).r;
                mn = min(mn, s); mx = max(mx, s);
            }

            float previous = clamp(Mat._PreviousBuffer.Sample(prevUV).r, mn, mx);

            // Drop history on disocclusion (reprojected off-screen).
            float response = (prevUV.x < 0.0 || prevUV.x > 1.0 || prevUV.y < 0.0 || prevUV.y > 1.0) ? 0.0 : Mat._TResponse;

            float ao = lerp(current, previous, response);
            return float4(float3(ao), 1.0);
        }

        ENDSLANG
    }
}
