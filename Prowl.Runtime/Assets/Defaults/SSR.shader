Shader "Default/SSR"
{
    Pass
    {
        Name "RayCast"
        Tags { "RenderOrder" = "Opaque" }
        Blend One Zero
        ZTest Disabled
        ZWrite Off
        Cull Off

        SLANGPROGRAM

        // ----------------------- VERTEX START ----------------------
        layout (location = 0) in float3 vertexPosition;
        layout (location = 1) in float2 vertexTexCoord;
        out float2 TexCoords;
        void main() { gl_Position = float4(vertexPosition, 1.0); TexCoords = vertexTexCoord; }

        // ----------------------- FRAGMENT START ----------------------
        import ProwlCG;

        uniform Sampler2D<float4> _CameraDepthTexture;
        uniform Sampler2D<float4> _CameraNormalsTexture;
        uniform Sampler2D<float4> _CameraMotionVectorsTexture; // .b roughness
        uniform Sampler2D<float4> _Noise;
        uniform float4 _JitterSizeAndOffset; // xy = rayUVsize/noiseSize, zw = per-frame offset
        uniform float _NumSteps;
        uniform float _BRDFBias;

        in float2 TexCoords;
        layout(location = 0) out float4 rayData; // xy = hit UV, z = pdf, w = mask

        // GGX microfacet half-vector importance sample (Karis / UE4). Returns half-vector + pdf.
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
            float4 clip = PROWL_MATRIX_P * float4(viewPos, 1.0);
            clip.xyz /= clip.w;
            return float3(clip.xy * 0.5 + 0.5, clip.z);
        }

        // March in screen space: step until the ray passes behind the depth buffer, then refine.
        bool trace(float3 startScreen, float3 reflViewDir, float jitter, out float2 hitUV)
        {
            float3 startView = getViewPos(startScreen.xy, startScreen.z);
            float3 endScreen = projectToScreen(startView + reflViewDir * 100.0);
            float3 delta = endScreen - startScreen;
            hitUV = startScreen.xy;
            if (length(delta.xy) < 1e-4) return false;

            float steps = min(max(abs(delta.x) * _ScreenParams.x, abs(delta.y) * _ScreenParams.y), _NumSteps);
            float3 step = delta / max(steps, 1.0);
            float3 cur = startScreen + step * (1.0 + jitter);

            bool crossed = false;
            for (float i = 1.0; i < steps; i += 1.0)
            {
                if (cur.x < 0.0 || cur.x > 1.0 || cur.y < 0.0 || cur.y > 1.0) return false;
                float sceneDepth = _CameraDepthTexture.Sample(cur.xy).r;
                if (sceneDepth < 1.0 && cur.z > sceneDepth + 0.0001) { crossed = true; break; }
                cur += step;
            }
            if (!crossed) return false;

            float3 lo = cur - step, hi = cur;
            for (int j = 0; j < 5; j++)
            {
                float3 mid = (lo + hi) * 0.5;
                float d = _CameraDepthTexture.Sample(mid.xy).r;
                if (mid.z > d) hi = mid; else lo = mid;
            }
            hitUV = hi.xy;
            return true;
        }

        void main()
        {
            float depth = _CameraDepthTexture.Sample(TexCoords).r;
            if (depth >= 1.0) { rayData = float4(0.0); return; }

            float4 nd = _CameraNormalsTexture.Sample(TexCoords);
            if (length(nd.xyz) < 0.01) { rayData = float4(0.0); return; }
            float3 viewNormal = normalize(nd.xyz * 2.0 - 1.0);
            float roughness = _CameraMotionVectorsTexture.Sample(TexCoords).b;

            float3 viewPos = getViewPos(TexCoords, depth);
            float3 V = normalize(viewPos); // camera -> surface

            // Blue noise (R channel) for the stochastic sample, tiled + per-frame offset.
            float2 Xi = _Noise.Sample((TexCoords + _JitterSizeAndOffset.zw) * _JitterSizeAndOffset.xy).rg;
            Xi.y = lerp(Xi.y, 0.0, _BRDFBias); // bias toward the mirror direction

            float4 H = importanceSampleGGX(Xi, roughness);
            float3 h = tangentToView(viewNormal, H.xyz);
            float3 reflDir = reflect(V, h);

            float jitter = (Xi.x + Xi.y) * (1.0 / max(_NumSteps, 1.0));

            float2 hitUV;
            bool hit = trace(float3(TexCoords, depth), reflDir, jitter, hitUV);

            rayData = hit ? float4(hitUV, H.w, 1.0) : float4(0.0);
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

        // ----------------------- VERTEX START ----------------------
        layout (location = 0) in float3 vertexPosition;
        layout (location = 1) in float2 vertexTexCoord;
        out float2 TexCoords;
        void main() { gl_Position = float4(vertexPosition, 1.0); TexCoords = vertexTexCoord; }

        // ----------------------- FRAGMENT START ----------------------
        import ProwlCG;

        uniform Sampler2D<float4> _MainTex;
        uniform float2 _BlurDir; // (1,0) or (0,1)

        in float2 TexCoords;
        layout(location = 0) out float4 fragColor;

        void main()
        {
            // Separable 7-tap Gaussian over the source texel grid (downsamples on the V write).
            float2 step = _BlurDir / float2(textureSize(_MainTex, 0));
            float4 c  = _MainTex.Sample(TexCoords) * 0.474;
            c      += _MainTex.Sample(TexCoords + step * 1.0) * 0.233;
            c      += _MainTex.Sample(TexCoords - step * 1.0) * 0.233;
            c      += _MainTex.Sample(TexCoords + step * 2.0) * 0.028;
            c      += _MainTex.Sample(TexCoords - step * 2.0) * 0.028;
            c      += _MainTex.Sample(TexCoords + step * 3.0) * 0.001;
            c      += _MainTex.Sample(TexCoords - step * 3.0) * 0.001;
            fragColor = c;
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

        // ----------------------- VERTEX START ----------------------
        layout (location = 0) in float3 vertexPosition;
        layout (location = 1) in float2 vertexTexCoord;
        out float2 TexCoords;
        void main() { gl_Position = float4(vertexPosition, 1.0); TexCoords = vertexTexCoord; }

        // ----------------------- FRAGMENT START ----------------------
        import ProwlCG;

        uniform Sampler2D<float4> _RayCast;
        uniform Sampler2D<float4> _CameraDepthTexture;
        uniform Sampler2D<float4> _CameraNormalsTexture;
        uniform Sampler2D<float4> _CameraMotionVectorsTexture; // .b roughness
        uniform Sampler2D<float4> _Noise;
        uniform float2 _NoiseSize;
        uniform float4 _JitterSizeAndOffset;
        uniform float2 _ResolveSize;
        uniform float _BRDFBias;
        uniform float _EdgeFactor;
        uniform float _MaxMipMap;
        uniform int _RayReuse;
        uniform int _UseNormalization;
        uniform int _Fireflies;

        // Convolved scene-colour pyramid (level 0 = sharp ... level 4 = blurriest).
        uniform Sampler2D<float4> _Scene0;
        uniform Sampler2D<float4> _Scene1;
        uniform Sampler2D<float4> _Scene2;
        uniform Sampler2D<float4> _Scene3;
        uniform Sampler2D<float4> _Scene4;

        in float2 TexCoords;
        layout(location = 0) out float4 fragColor;

        const float2 resolveOffset[4] = float2[]( float2(0,0), float2(2,-2), float2(-2,-2), float2(0,2) );

        float3 samplePyramid(float2 uv, float mip)
        {
            float3 c = lerp(_Scene0.Sample(uv).rgb, _Scene1.Sample(uv).rgb, clamp(mip, 0.0, 1.0));
            c = lerp(c, _Scene2.Sample(uv).rgb, clamp(mip - 1.0, 0.0, 1.0));
            c = lerp(c, _Scene3.Sample(uv).rgb, clamp(mip - 2.0, 0.0, 1.0));
            c = lerp(c, _Scene4.Sample(uv).rgb, clamp(mip - 3.0, 0.0, 1.0));
            return c;
        }

        // Smith-GGX visibility * GGX NDF * PI/4 -> the resolve weight (matched with 1/pdf).
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

        void main()
        {
            float3 viewNormal = normalize(_CameraNormalsTexture.Sample(TexCoords).xyz * 2.0 - 1.0);
            float roughness = _CameraMotionVectorsTexture.Sample(TexCoords).b;
            float depth = _CameraDepthTexture.Sample(TexCoords).r;
            float3 viewPos = getViewPos(TexCoords, depth);
            float3 V = normalize(-viewPos); // surface -> camera

            // Per-pixel rotation of the reuse offsets from blue noise.
            float2 bn = _Noise.Sample((TexCoords + _JitterSizeAndOffset.zw) * _ScreenParams.xy / _NoiseSize).rg * 2.0 - 1.0;
            float2x2 rot = float2x2(bn.x, bn.y, -bn.y, bn.x);

            int numResolve = _RayReuse == 1 ? 4 : 1;
            float NdotV = clamp(dot(viewNormal, V), 0.0, 1.0);
            float coneTangent = lerp(0.0, roughness * (1.0 - _BRDFBias), NdotV * sqrt(roughness));
            float maxMip = _MaxMipMap - 1.0;

            float4 result = float4(0.0);
            float weightSum = 0.0;
            for (int i = 0; i < numResolve; i++)
            {
                float2 offset = rot * (resolveOffset[i] / _ResolveSize);
                float2 nUV = TexCoords + offset;

                float4 rd = _RayCast.Sample(nUV);
                float2 hitUV = rd.xy;
                float pdf = rd.z;
                float mask = rd.w;
                if (mask <= 0.0) continue;

                float hitDepth = _CameraDepthTexture.Sample(hitUV).r;
                float3 hitViewPos = getViewPos(hitUV, hitDepth);

                float weight = 1.0;
                if (_UseNormalization == 1)
                    weight = brdfWeight(V, normalize(hitViewPos - viewPos), viewNormal, roughness) / max(1e-5, pdf);

                float coneRadius = coneTangent * length(hitUV - TexCoords);
                float mip = clamp(log2(max(1e-5, coneRadius) * max(_ResolveSize.x, _ResolveSize.y)), 0.0, maxMip);

                float4 s;
                s.rgb = samplePyramid(hitUV, mip);
                s.a = edgeFade(hitUV, _EdgeFactor) * mask;
                if (_Fireflies == 1) s.rgb /= 1.0 + luminance(s.rgb);

                result += s * weight;
                weightSum += weight;
            }

            result /= max(1e-5, weightSum);
            if (_Fireflies == 1)
            {
                // Bounded inverse of the per-sample tonemap: cap luminance so a very bright
                // reflection can't divide by ~0 and explode to white.
                float lum = min(luminance(result.rgb), 0.95);
                result.rgb /= 1.0 - lum;
            }
            fragColor = max(float4(1e-5), result);
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

        // ----------------------- VERTEX START ----------------------
        layout (location = 0) in float3 vertexPosition;
        layout (location = 1) in float2 vertexTexCoord;
        out float2 TexCoords;
        void main() { gl_Position = float4(vertexPosition, 1.0); TexCoords = vertexTexCoord; }

        // ----------------------- FRAGMENT START ----------------------
        import ProwlCG;

        uniform Sampler2D<float4> _MainTex;        // current resolved reflection
        uniform Sampler2D<float4> _PreviousBuffer; // reflection history
        uniform Sampler2D<float4> _RayCast;
        uniform Sampler2D<float4> _CameraDepthTexture;
        uniform Sampler2D<float4> _CameraMotionVectorsTexture; // .rg motion
        uniform int _ReflectionVelocity;  // 1 = derive velocity from reflection hit depth
        uniform float _TScale;
        uniform float _TResponse;

        in float2 TexCoords;
        layout(location = 0) out float4 fragColor;

        // Reproject this pixel through the previous view-projection to get screen-space motion.
        float2 reflectionVelocity(float2 uv)
        {
            // Use the reflection hit point's depth so the history follows the reflected geometry.
            float hitDepthW = _RayCast.Sample(uv).w > 0.0 ? _CameraDepthTexture.Sample(_RayCast.Sample(uv).xy).r
                                                            : _CameraDepthTexture.Sample(uv).r;
            float4 clip = float4(uv * 2.0 - 1.0, hitDepthW * 2.0 - 1.0, 1.0);
            float4 world = PROWL_MATRIX_I_VP * clip;
            world /= world.w;
            float4 prevClip = PROWL_MATRIX_VP_PREVIOUS * world;
            float2 prevUV = (prevClip.xy / prevClip.w) * 0.5 + 0.5;
            return uv - prevUV;
        }

        void main()
        {
            float2 velocity = _ReflectionVelocity == 1 ? reflectionVelocity(TexCoords)
                                                     : _CameraMotionVectorsTexture.Sample(TexCoords).rg;
            float4 current = _MainTex.Sample(TexCoords);
            float4 previous = _PreviousBuffer.Sample(TexCoords - velocity);

            float2 du = float2(1.0 / _ScreenParams.x, 0.0);
            float2 dv = float2(0.0, 1.0 / _ScreenParams.y);
            float4 mn = current, mx = current;
            for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
            {
                float4 c = _MainTex.Sample(TexCoords + du * float(x) + dv * float(y));
                mn = min(mn, c); mx = max(mx, c);
            }
            float4 center = (mn + mx) * 0.5;
            mn = (mn - center) * _TScale + center;
            mx = (mx - center) * _TScale + center;
            previous = clamp(previous, mn, mx);

            fragColor = lerp(current, previous, clamp(_TResponse * (1.0 - length(velocity) * 8.0), 0.0, 1.0));
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

        // ----------------------- VERTEX START ----------------------
        layout (location = 0) in float3 vertexPosition;
        layout (location = 1) in float2 vertexTexCoord;
        out float2 TexCoords;
        void main() { gl_Position = float4(vertexPosition, 1.0); TexCoords = vertexTexCoord; }

        // ----------------------- FRAGMENT START ----------------------
        import ProwlCG;

        uniform Sampler2D<float4> _MainTex;                    // buffer to reproject (previous combined)
        uniform Sampler2D<float4> _CameraMotionVectorsTexture; // .rg motion

        in float2 TexCoords;
        layout(location = 0) out float4 fragColor;

        void main()
        {
            float2 velocity = _CameraMotionVectorsTexture.Sample(TexCoords).rg;
            fragColor = _MainTex.Sample(TexCoords - velocity);
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

        // ----------------------- VERTEX START ----------------------
        layout (location = 0) in float3 vertexPosition;
        layout (location = 1) in float2 vertexTexCoord;
        out float2 TexCoords;
        void main() { gl_Position = float4(vertexPosition, 1.0); TexCoords = vertexTexCoord; }

        // ----------------------- FRAGMENT START ----------------------
        import ProwlCG;

        uniform Sampler2D<float4> _MainTex;          // scene colour
        uniform Sampler2D<float4> _ReflectionBuffer; // resolved (+temporal) reflection
        uniform Sampler2D<float4> _CameraDepthTexture;
        uniform Sampler2D<float4> _CameraNormalsTexture;
        uniform Sampler2D<float4> _CameraMotionVectorsTexture; // .b roughness, .a metallic
        uniform int _UseFresnel;

        in float2 TexCoords;
        layout(location = 0) out float4 fragColor;

        // Environment BRDF approximation (Karis "mobile") for the specular reflection response.
        float3 envBRDFApprox(float3 F0, float roughness, float NdotV)
        {
            float4 c0 = float4(-1.0, -0.0275, -0.572, 0.022);
            float4 c1 = float4(1.0, 0.0425, 1.04, -0.04);
            float4 r = roughness * c0 + c1;
            float a004 = min(r.x * r.x, exp2(-9.28 * NdotV)) * r.x + r.y;
            float2 ab = float2(-1.04, 1.04) * a004 + r.zw;
            return F0 * ab.x + ab.y;
        }

        void main()
        {
            float3 sceneColor = _MainTex.Sample(TexCoords).rgb;
            float4 reflection = _ReflectionBuffer.Sample(TexCoords);

            float roughness = _CameraMotionVectorsTexture.Sample(TexCoords).b;
            float metallic = _CameraMotionVectorsTexture.Sample(TexCoords).a;
            float3 viewNormal = normalize(_CameraNormalsTexture.Sample(TexCoords).xyz * 2.0 - 1.0);
            float depth = _CameraDepthTexture.Sample(TexCoords).r;
            float3 V = normalize(-getViewPos(TexCoords, depth));
            float NdotV = clamp(dot(viewNormal, V), 0.0, 1.0);

            // Forward SSR has no G-buffer albedo: approximate it from the tonemapped scene colour.
            float3 approxAlbedo = sceneColor / (1.0 + sceneColor);
            float3 F0 = lerp(float3(0.04), approxAlbedo, metallic);

            float mask = reflection.a * reflection.a;
            float3 refl = reflection.rgb;
            if (_UseFresnel == 1)
                refl *= envBRDFApprox(F0, roughness, NdotV);
            // Guard against runaway HDR and the one-bounce feedback loop diverging when Fresnel
            // (which normally attenuates each bounce) is disabled.
            refl = min(refl, float3(8.0));

            fragColor = float4(sceneColor + refl * mask, 1.0);
        }

        ENDSLANG
    }
}
