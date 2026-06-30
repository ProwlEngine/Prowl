Shader "Default/SSR"
{
    Pass
    {
        Name "RayCast"
        Tags { "RenderOrder" = "Opaque" }
        ZTest Disabled
        ZWrite Off
        Cull Off

        SLANGPROGRAM
        import ProwlCG;

        struct MaterialData
        {
            Sampler2D<float4> _CameraDepthTexture;
            Sampler2D<float4> _CameraNormalsTexture;
            Sampler2D<float4> _CameraMotionVectorsTexture;
            Sampler2D<float4> _Noise;
            float4 _JitterSizeAndOffset;
            float _NumSteps;
            float _BRDFBias;
        }
        ParameterBlock<MaterialData> Mat;

        struct VertexInput { float3 position : POSITION0; float2 uv : TEXCOORD0; }
        struct Varyings { float4 position : SV_Position; float2 uv : TEXCOORD0; }

        float4 importanceSampleGGX(float2 Xi, float roughness)
        {
            float m = roughness * roughness;
            float m2 = m * m;
            float phi = PROWL_TWO_PI * Xi.x;
            float cosT = sqrt((1.0 - Xi.y) / (1.0 + (m2 - 1.0) * Xi.y));
            float sinT = sqrt(max(1e-5, 1.0 - cosT * cosT));
            float3 h = float3(sinT * cos(phi), sinT * sin(phi), cosT);
            float d = (cosT * m2 - cosT) * cosT + 1.0;
            float D = m2 / (PROWL_PI * d * d);
            return float4(h, D * cosT);
        }

        float3 tangentToView(float3 n, float3 h)
        {
            float3 up = abs(n.z) < 0.999 ? float3(0.0, 0.0, 1.0) : float3(1.0, 0.0, 0.0);
            float3 t = normalize(cross(up, n));
            float3 b = cross(n, t);
            return t * h.x + b * h.y + n * h.z;
        }

        float3 projectToScreen(float3 viewPos)
        {
            float4 clip = mul(Frame.prowl_MatP, float4(viewPos, 1.0));
            clip.xyz /= clip.w;
            return float3(clip.xy * 0.5 + 0.5, clip.z);
        }

        bool trace(float3 startScreen, float3 reflViewDir, float jitter, out float2 hitUV)
        {
            float3 startView = getViewPos(startScreen.xy, startScreen.z);
            float3 endScreen = projectToScreen(startView + reflViewDir * 100.0);
            float3 delta = endScreen - startScreen;
            hitUV = startScreen.xy;
            if (length(delta.xy) < 1e-4) return false;

            float steps = min(max(abs(delta.x) * Frame._ScreenParams.x, abs(delta.y) * Frame._ScreenParams.y), Mat._NumSteps);
            float3 stepVec = delta / max(steps, 1.0);
            float3 cur = startScreen + stepVec * (1.0 + jitter);

            bool crossed = false;
            for (float i = 1.0; i < steps; i += 1.0)
            {
                if (cur.x < 0.0 || cur.x > 1.0 || cur.y < 0.0 || cur.y > 1.0) return false;
                float sceneDepth = Mat._CameraDepthTexture.Sample(cur.xy).r;
                if (sceneDepth < 1.0 && cur.z > sceneDepth + 0.0001) { crossed = true; break; }
                cur += stepVec;
            }
            if (!crossed) return false;

            float3 lo = cur - stepVec, hi = cur;
            for (int j = 0; j < 5; j++)
            {
                float3 mid = (lo + hi) * 0.5;
                float d = Mat._CameraDepthTexture.Sample(mid.xy).r;
                if (mid.z > d) hi = mid; else lo = mid;
            }
            hitUV = hi.xy;
            return true;
        }

        [shader("vertex")]
        Varyings Vertex(VertexInput input) { Varyings o; o.position = float4(input.position, 1.0); o.uv = input.uv; return o; }

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
            float depth = Mat._CameraDepthTexture.Sample(input.uv).r;
            if (depth >= 1.0) return float4(0.0);

            float4 nd = Mat._CameraNormalsTexture.Sample(input.uv);
            if (length(nd.xyz) < 0.01) return float4(0.0);
            float3 viewNormal = normalize(nd.xyz * 2.0 - 1.0);
            float roughness = Mat._CameraMotionVectorsTexture.Sample(input.uv).b;

            float3 viewPos = getViewPos(input.uv, depth);
            float3 V = normalize(viewPos);

            float2 Xi = Mat._Noise.Sample((input.uv + Mat._JitterSizeAndOffset.zw) * Mat._JitterSizeAndOffset.xy).rg;
            Xi.y = lerp(Xi.y, 0.0, Mat._BRDFBias);

            float4 H = importanceSampleGGX(Xi, roughness);
            float3 h = tangentToView(viewNormal, H.xyz);
            float3 reflDir = reflect(V, h);

            float jitter = (Xi.x + Xi.y) * (1.0 / max(Mat._NumSteps, 1.0));

            float2 hitUV;
            bool hit = trace(float3(input.uv, depth), reflDir, jitter, hitUV);

            return hit ? float4(hitUV, H.w, 1.0) : float4(0.0);
        }
        ENDSLANG
    }

    Pass
    {
        Name "SceneBlur"
        Tags { "RenderOrder" = "Opaque" }
        ZTest Disabled
        ZWrite Off
        Cull Off

        SLANGPROGRAM
        import ProwlCG;

        struct MaterialData { Sampler2D<float4> _MainTex; float2 _BlurDir; }
        ParameterBlock<MaterialData> Mat;

        struct VertexInput { float3 position : POSITION0; float2 uv : TEXCOORD0; }
        struct Varyings { float4 position : SV_Position; float2 uv : TEXCOORD0; }

        [shader("vertex")]
        Varyings Vertex(VertexInput input) { Varyings o; o.position = float4(input.position, 1.0); o.uv = input.uv; return o; }

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
            uint w, h;
            Mat._MainTex.GetDimensions(w, h);
            float2 stepVec = Mat._BlurDir / float2(w, h);
            float4 c  = Mat._MainTex.Sample(input.uv) * 0.474;
            c += Mat._MainTex.Sample(input.uv + stepVec * 1.0) * 0.233;
            c += Mat._MainTex.Sample(input.uv - stepVec * 1.0) * 0.233;
            c += Mat._MainTex.Sample(input.uv + stepVec * 2.0) * 0.028;
            c += Mat._MainTex.Sample(input.uv - stepVec * 2.0) * 0.028;
            c += Mat._MainTex.Sample(input.uv + stepVec * 3.0) * 0.001;
            c += Mat._MainTex.Sample(input.uv - stepVec * 3.0) * 0.001;
            return c;
        }
        ENDSLANG
    }

    Pass
    {
        Name "Resolve"
        Tags { "RenderOrder" = "Opaque" }
        ZTest Disabled
        ZWrite Off
        Cull Off

        SLANGPROGRAM
        import ProwlCG;

        struct MaterialData
        {
            Sampler2D<float4> _RayCast;
            Sampler2D<float4> _CameraDepthTexture;
            Sampler2D<float4> _CameraNormalsTexture;
            Sampler2D<float4> _CameraMotionVectorsTexture;
            Sampler2D<float4> _Noise;
            float2 _NoiseSize;
            float4 _JitterSizeAndOffset;
            float2 _ResolveSize;
            float _BRDFBias;
            float _EdgeFactor;
            float _MaxMipMap;
            int _RayReuse;
            int _UseNormalization;
            int _Fireflies;
            Sampler2D<float4> _Scene0;
            Sampler2D<float4> _Scene1;
            Sampler2D<float4> _Scene2;
            Sampler2D<float4> _Scene3;
            Sampler2D<float4> _Scene4;
        }
        ParameterBlock<MaterialData> Mat;

        struct VertexInput { float3 position : POSITION0; float2 uv : TEXCOORD0; }
        struct Varyings { float4 position : SV_Position; float2 uv : TEXCOORD0; }

        static const float2 resolveOffset[4] = { float2(0,0), float2(2,-2), float2(-2,-2), float2(0,2) };

        float3 samplePyramid(float2 uv, float mip)
        {
            float3 c = lerp(Mat._Scene0.Sample(uv).rgb, Mat._Scene1.Sample(uv).rgb, clamp(mip, 0.0, 1.0));
            c = lerp(c, Mat._Scene2.Sample(uv).rgb, clamp(mip - 1.0, 0.0, 1.0));
            c = lerp(c, Mat._Scene3.Sample(uv).rgb, clamp(mip - 2.0, 0.0, 1.0));
            c = lerp(c, Mat._Scene4.Sample(uv).rgb, clamp(mip - 3.0, 0.0, 1.0));
            return c;
        }

        float brdfWeight(float3 V, float3 L, float3 N, float roughness)
        {
            float3 H = normalize(L + V);
            float NdotH = clamp(dot(N, H), 0.0, 1.0);
            float NdotL = clamp(dot(N, L), 0.0, 1.0);
            float NdotV = clamp(dot(N, V), 0.0, 1.0);
            float m = roughness * roughness;
            float m2 = m * m;
            float d = (NdotH * m2 - NdotH) * NdotH + 1.0;
            float D = m2 / (PROWL_PI * d * d);
            float visL = NdotV * sqrt((NdotL - NdotL * m2) * NdotL + m2);
            float visV = NdotL * sqrt((NdotV - NdotV * m2) * NdotV + m2);
            float Vis = 0.5 / max(1e-5, visL + visV);
            return D * Vis * (PROWL_PI / 4.0);
        }

        float edgeFade(float2 pos, float value)
        {
            float d = min(1.0 - max(pos.x, pos.y), min(pos.x, pos.y));
            return clamp(value > 0.0 ? d / value : 1.0, 0.0, 1.0);
        }

        [shader("vertex")]
        Varyings Vertex(VertexInput input) { Varyings o; o.position = float4(input.position, 1.0); o.uv = input.uv; return o; }

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
            float3 viewNormal = normalize(Mat._CameraNormalsTexture.Sample(input.uv).xyz * 2.0 - 1.0);
            float roughness = Mat._CameraMotionVectorsTexture.Sample(input.uv).b;
            float depth = Mat._CameraDepthTexture.Sample(input.uv).r;
            float3 viewPos = getViewPos(input.uv, depth);
            float3 V = normalize(-viewPos);

            float2 bn = Mat._Noise.Sample((input.uv + Mat._JitterSizeAndOffset.zw) * Frame._ScreenParams.xy / Mat._NoiseSize).rg * 2.0 - 1.0;
            float2x2 rot = float2x2(bn.x, bn.y, -bn.y, bn.x);

            int numResolve = Mat._RayReuse == 1 ? 4 : 1;
            float NdotV = clamp(dot(viewNormal, V), 0.0, 1.0);
            float coneTangent = lerp(0.0, roughness * (1.0 - Mat._BRDFBias), NdotV * sqrt(roughness));
            float maxMip = Mat._MaxMipMap - 1.0;

            float4 result = float4(0.0);
            float weightSum = 0.0;
            for (int i = 0; i < numResolve; i++)
            {
                float2 offset = mul(resolveOffset[i] / Mat._ResolveSize, rot);
                float2 nUV = input.uv + offset;

                float4 rd = Mat._RayCast.Sample(nUV);
                float2 hitUV = rd.xy;
                float pdf = rd.z;
                float mask = rd.w;
                if (mask <= 0.0) continue;

                float hitDepth = Mat._CameraDepthTexture.Sample(hitUV).r;
                float3 hitViewPos = getViewPos(hitUV, hitDepth);

                float weight = 1.0;
                if (Mat._UseNormalization == 1)
                    weight = brdfWeight(V, normalize(hitViewPos - viewPos), viewNormal, roughness) / max(1e-5, pdf);

                float coneRadius = coneTangent * length(hitUV - input.uv);
                float mip = clamp(log2(max(1e-5, coneRadius) * max(Mat._ResolveSize.x, Mat._ResolveSize.y)), 0.0, maxMip);

                float4 s;
                s.rgb = samplePyramid(hitUV, mip);
                s.a = edgeFade(hitUV, Mat._EdgeFactor) * mask;
                if (Mat._Fireflies == 1) s.rgb /= 1.0 + luminance(s.rgb);

                result += s * weight;
                weightSum += weight;
            }

            result /= max(1e-5, weightSum);
            if (Mat._Fireflies == 1)
            {
                float lum = min(luminance(result.rgb), 0.95);
                result.rgb /= 1.0 - lum;
            }
            return max(float4(1e-5), result);
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

        struct MaterialData
        {
            Sampler2D<float4> _MainTex;
            Sampler2D<float4> _PreviousBuffer;
            Sampler2D<float4> _RayCast;
            Sampler2D<float4> _CameraDepthTexture;
            Sampler2D<float4> _CameraMotionVectorsTexture;
            int _ReflectionVelocity;
            float _TScale;
            float _TResponse;
        }
        ParameterBlock<MaterialData> Mat;

        struct VertexInput { float3 position : POSITION0; float2 uv : TEXCOORD0; }
        struct Varyings { float4 position : SV_Position; float2 uv : TEXCOORD0; }

        float2 reflectionVelocity(float2 uv)
        {
            float hitDepthW = Mat._RayCast.Sample(uv).w > 0.0 ? Mat._CameraDepthTexture.Sample(Mat._RayCast.Sample(uv).xy).r
                                                              : Mat._CameraDepthTexture.Sample(uv).r;
            float4 clip = float4(uv * 2.0 - 1.0, hitDepthW * 2.0 - 1.0, 1.0);
            float4 world = mul(Frame.prowl_MatIVP, clip);
            world /= world.w;
            float4 prevClip = mul(Frame.prowl_PrevViewProj, world);
            float2 prevUV = (prevClip.xy / prevClip.w) * 0.5 + 0.5;
            return uv - prevUV;
        }

        [shader("vertex")]
        Varyings Vertex(VertexInput input) { Varyings o; o.position = float4(input.position, 1.0); o.uv = input.uv; return o; }

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
            float2 velocity = Mat._ReflectionVelocity == 1 ? reflectionVelocity(input.uv)
                                                           : Mat._CameraMotionVectorsTexture.Sample(input.uv).rg;
            float4 current = Mat._MainTex.Sample(input.uv);
            float4 previous = Mat._PreviousBuffer.Sample(input.uv - velocity);

            float2 du = float2(1.0 / Frame._ScreenParams.x, 0.0);
            float2 dv = float2(0.0, 1.0 / Frame._ScreenParams.y);
            float4 mn = current, mx = current;
            for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
            {
                float4 c = Mat._MainTex.Sample(input.uv + du * float(x) + dv * float(y));
                mn = min(mn, c); mx = max(mx, c);
            }
            float4 center = (mn + mx) * 0.5;
            mn = (mn - center) * Mat._TScale + center;
            mx = (mx - center) * Mat._TScale + center;
            previous = clamp(previous, mn, mx);

            return lerp(current, previous, clamp(Mat._TResponse * (1.0 - length(velocity) * 8.0), 0.0, 1.0));
        }
        ENDSLANG
    }

    Pass
    {
        Name "Reproject"
        Tags { "RenderOrder" = "Opaque" }
        ZTest Disabled
        ZWrite Off
        Cull Off

        SLANGPROGRAM
        import ProwlCG;

        struct MaterialData
        {
            Sampler2D<float4> _MainTex;
            Sampler2D<float4> _CameraMotionVectorsTexture;
        }
        ParameterBlock<MaterialData> Mat;

        struct VertexInput { float3 position : POSITION0; float2 uv : TEXCOORD0; }
        struct Varyings { float4 position : SV_Position; float2 uv : TEXCOORD0; }

        [shader("vertex")]
        Varyings Vertex(VertexInput input) { Varyings o; o.position = float4(input.position, 1.0); o.uv = input.uv; return o; }

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
            float2 velocity = Mat._CameraMotionVectorsTexture.Sample(input.uv).rg;
            return Mat._MainTex.Sample(input.uv - velocity);
        }
        ENDSLANG
    }

    Pass
    {
        Name "Combine"
        Tags { "RenderOrder" = "Opaque" }
        ZTest Disabled
        ZWrite Off
        Cull Off

        SLANGPROGRAM
        import ProwlCG;

        struct MaterialData
        {
            Sampler2D<float4> _MainTex;
            Sampler2D<float4> _ReflectionBuffer;
            Sampler2D<float4> _CameraDepthTexture;
            Sampler2D<float4> _CameraNormalsTexture;
            Sampler2D<float4> _CameraMotionVectorsTexture;
            int _UseFresnel;
        }
        ParameterBlock<MaterialData> Mat;

        struct VertexInput { float3 position : POSITION0; float2 uv : TEXCOORD0; }
        struct Varyings { float4 position : SV_Position; float2 uv : TEXCOORD0; }

        float3 envBRDFApprox(float3 F0, float roughness, float NdotV)
        {
            float4 c0 = float4(-1.0, -0.0275, -0.572, 0.022);
            float4 c1 = float4(1.0, 0.0425, 1.04, -0.04);
            float4 r = roughness * c0 + c1;
            float a004 = min(r.x * r.x, exp2(-9.28 * NdotV)) * r.x + r.y;
            float2 ab = float2(-1.04, 1.04) * a004 + r.zw;
            return F0 * ab.x + ab.y;
        }

        [shader("vertex")]
        Varyings Vertex(VertexInput input) { Varyings o; o.position = float4(input.position, 1.0); o.uv = input.uv; return o; }

        [shader("fragment")]
        float4 Fragment(Varyings input) : SV_Target
        {
            float3 sceneColor = Mat._MainTex.Sample(input.uv).rgb;
            float4 reflection = Mat._ReflectionBuffer.Sample(input.uv);

            float roughness = Mat._CameraMotionVectorsTexture.Sample(input.uv).b;
            float metallic = Mat._CameraMotionVectorsTexture.Sample(input.uv).a;
            float3 viewNormal = normalize(Mat._CameraNormalsTexture.Sample(input.uv).xyz * 2.0 - 1.0);
            float depth = Mat._CameraDepthTexture.Sample(input.uv).r;
            float3 V = normalize(-getViewPos(input.uv, depth));
            float NdotV = clamp(dot(viewNormal, V), 0.0, 1.0);

            float3 approxAlbedo = sceneColor / (1.0 + sceneColor);
            float3 F0 = lerp(float3(0.04), approxAlbedo, metallic);

            float mask = reflection.a * reflection.a;
            float3 refl = reflection.rgb;
            if (Mat._UseFresnel == 1)
                refl *= envBRDFApprox(F0, roughness, NdotV);
            refl = min(refl, float3(8.0));

            return float4(sceneColor + refl * mask, 1.0);
        }
        ENDSLANG
    }
}
