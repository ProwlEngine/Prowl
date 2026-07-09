Shader "Default/SSR"

// Stochastic screen-space reflections.
//   RayCast   : per-pixel GGX importance-sampled reflection ray -> hit UV + pdf + mask
//   SceneBlur : separable Gaussian, used to build a convolved scene-colour pyramid
//   Resolve   : gather neighbour rays, weight by BRDF/pdf, pick a pyramid level by a roughness
//               cone (contact hardening + glossy blur), firefly suppression, screen-edge fade
//   Temporal  : reproject + neighbourhood-clamp the reflection against a history buffer
//   Reproject : reproject a buffer by motion (used to feed last frame's result back in -> 1 bounce)
//   Combine   : Fresnel-weighted blend over the scene (albedo approximated from the tonemapped scene)
// Material data (per-pixel roughness/metallic) and view normals come from the unified prepass.

//------------------------------------------------------------------------------------------------
Pass "RayCast"
{
    Tags { "RenderOrder" = "Opaque" }
    Blend Override
    ZTest Off
    ZWrite Off
    Cull Off

    GLSLPROGRAM

    Vertex
    {
        layout (location = 0) in vec3 vertexPosition;
        layout (location = 1) in vec2 vertexTexCoord;
        out vec2 TexCoords;
        void main() { gl_Position = vec4(vertexPosition, 1.0); TexCoords = vertexTexCoord; }
    }

    Fragment
    {
        #include "ProwlCG"

        uniform sampler2D _CameraDepthTexture;
        uniform sampler2D _CameraNormalsTexture;
        uniform sampler2D _CameraMotionVectorsTexture; // .b roughness
        uniform sampler2D _Noise;
        uniform vec4 _JitterSizeAndOffset; // xy = rayUVsize/noiseSize, zw = per-frame offset
        uniform float _NumSteps;
        uniform float _BRDFBias;

        in vec2 TexCoords;
        layout(location = 0) out vec4 rayData; // xy = hit UV, z = pdf, w = mask

        // GGX microfacet half-vector importance sample (Karis / UE4). Returns half-vector + pdf.
        vec4 importanceSampleGGX(vec2 Xi, float roughness)
        {
            float m = roughness * roughness;
            float m2 = m * m;
            float phi = PROWL_TWO_PI * Xi.x;
            float cosT = sqrt((1.0 - Xi.y) / (1.0 + (m2 - 1.0) * Xi.y));
            float sinT = sqrt(max(1e-5, 1.0 - cosT * cosT));
            vec3 h = vec3(sinT * cos(phi), sinT * sin(phi), cosT);
            float d = (cosT * m2 - cosT) * cosT + 1.0;
            float D = m2 / (PROWL_PI * d * d);
            return vec4(h, D * cosT);
        }

        vec3 tangentToView(vec3 n, vec3 h)
        {
            vec3 up = abs(n.z) < 0.999 ? vec3(0.0, 0.0, 1.0) : vec3(1.0, 0.0, 0.0);
            vec3 t = normalize(cross(up, n));
            vec3 b = cross(n, t);
            return t * h.x + b * h.y + n * h.z;
        }

        vec3 projectToScreen(vec3 viewPos)
        {
            vec4 clip = PROWL_MATRIX_P * vec4(viewPos, 1.0);
            clip.xyz /= clip.w;
            return vec3(clip.xy * 0.5 + 0.5, clip.z);
        }

        // March in screen space: step until the ray passes behind the depth buffer, then refine.
        bool trace(vec3 startScreen, vec3 reflViewDir, float jitter, out vec2 hitUV)
        {
            vec3 startView = getViewPos(startScreen.xy, startScreen.z);
            vec3 endScreen = projectToScreen(startView + reflViewDir * 100.0);
            vec3 delta = endScreen - startScreen;
            hitUV = startScreen.xy;
            if (length(delta.xy) < 1e-4) return false;

            float steps = min(max(abs(delta.x) * _ScreenParams.x, abs(delta.y) * _ScreenParams.y), _NumSteps);
            vec3 step = delta / max(steps, 1.0);
            vec3 cur = startScreen + step * (1.0 + jitter);

            bool crossed = false;
            for (float i = 1.0; i < steps; i += 1.0)
            {
                if (cur.x < 0.0 || cur.x > 1.0 || cur.y < 0.0 || cur.y > 1.0) return false;
                float sceneDepth = texture(_CameraDepthTexture, cur.xy).r;
                if (sceneDepth < 1.0 && cur.z > sceneDepth + 0.0001) { crossed = true; break; }
                cur += step;
            }
            if (!crossed) return false;

            vec3 lo = cur - step, hi = cur;
            for (int j = 0; j < 5; j++)
            {
                vec3 mid = (lo + hi) * 0.5;
                float d = texture(_CameraDepthTexture, mid.xy).r;
                if (mid.z > d) hi = mid; else lo = mid;
            }
            hitUV = hi.xy;
            return true;
        }

        void main()
        {
            float depth = texture(_CameraDepthTexture, TexCoords).r;
            if (depth >= 1.0) { rayData = vec4(0.0); return; }

            vec4 nd = texture(_CameraNormalsTexture, TexCoords);
            if (length(nd.xyz) < 0.01) { rayData = vec4(0.0); return; }
            vec3 viewNormal = normalize(nd.xyz * 2.0 - 1.0);
            float roughness = texture(_CameraMotionVectorsTexture, TexCoords).b;

            vec3 viewPos = getViewPos(TexCoords, depth);
            vec3 V = normalize(viewPos); // camera -> surface

            // Blue noise (R channel) for the stochastic sample, tiled + per-frame offset.
            vec2 Xi = texture(_Noise, (TexCoords + _JitterSizeAndOffset.zw) * _JitterSizeAndOffset.xy).rg;
            Xi.y = mix(Xi.y, 0.0, _BRDFBias); // bias toward the mirror direction

            vec4 H = importanceSampleGGX(Xi, roughness);
            vec3 h = tangentToView(viewNormal, H.xyz);
            vec3 reflDir = reflect(V, h);

            float jitter = (Xi.x + Xi.y) * (1.0 / max(_NumSteps, 1.0));

            vec2 hitUV;
            bool hit = trace(vec3(TexCoords, depth), reflDir, jitter, hitUV);

            rayData = hit ? vec4(hitUV, H.w, 1.0) : vec4(0.0);
        }
    }
    ENDGLSL
}

//------------------------------------------------------------------------------------------------
Pass "SceneBlur"
{
    Tags { "RenderOrder" = "Opaque" }
    Blend Off ZTest Off ZWrite Off Cull Off

    GLSLPROGRAM

    Vertex
    {
        layout (location = 0) in vec3 vertexPosition;
        layout (location = 1) in vec2 vertexTexCoord;
        out vec2 TexCoords;
        void main() { gl_Position = vec4(vertexPosition, 1.0); TexCoords = vertexTexCoord; }
    }

    Fragment
    {
        #include "ProwlCG"

        uniform sampler2D _MainTex;
        uniform vec2 _BlurDir; // (1,0) or (0,1)

        in vec2 TexCoords;
        layout(location = 0) out vec4 fragColor;

        void main()
        {
            // Separable 7-tap Gaussian over the source texel grid (downsamples on the V write).
            vec2 step = _BlurDir / vec2(textureSize(_MainTex, 0));
            vec4 c  = texture(_MainTex, TexCoords) * 0.474;
            c      += texture(_MainTex, TexCoords + step * 1.0) * 0.233;
            c      += texture(_MainTex, TexCoords - step * 1.0) * 0.233;
            c      += texture(_MainTex, TexCoords + step * 2.0) * 0.028;
            c      += texture(_MainTex, TexCoords - step * 2.0) * 0.028;
            c      += texture(_MainTex, TexCoords + step * 3.0) * 0.001;
            c      += texture(_MainTex, TexCoords - step * 3.0) * 0.001;
            fragColor = c;
        }
    }
    ENDGLSL
}

//------------------------------------------------------------------------------------------------
Pass "Resolve"
{
    Tags { "RenderOrder" = "Opaque" }
    Blend Off ZTest Off ZWrite Off Cull Off

    GLSLPROGRAM

    Vertex
    {
        layout (location = 0) in vec3 vertexPosition;
        layout (location = 1) in vec2 vertexTexCoord;
        out vec2 TexCoords;
        void main() { gl_Position = vec4(vertexPosition, 1.0); TexCoords = vertexTexCoord; }
    }

    Fragment
    {
        #include "ProwlCG"

        uniform sampler2D _RayCast;
        uniform sampler2D _CameraDepthTexture;
        uniform sampler2D _CameraNormalsTexture;
        uniform sampler2D _CameraMotionVectorsTexture; // .b roughness
        uniform sampler2D _Noise;
        uniform vec2 _NoiseSize;
        uniform vec4 _JitterSizeAndOffset;
        uniform vec2 _ResolveSize;
        uniform float _BRDFBias;
        uniform float _EdgeFactor;
        uniform float _MaxMipMap;
        uniform int _RayReuse;
        uniform int _UseNormalization;
        uniform int _Fireflies;

        // Convolved scene-colour pyramid (level 0 = sharp ... level 4 = blurriest).
        uniform sampler2D _Scene0;
        uniform sampler2D _Scene1;
        uniform sampler2D _Scene2;
        uniform sampler2D _Scene3;
        uniform sampler2D _Scene4;

        in vec2 TexCoords;
        layout(location = 0) out vec4 fragColor;

        const vec2 resolveOffset[4] = vec2[]( vec2(0,0), vec2(2,-2), vec2(-2,-2), vec2(0,2) );

        vec3 samplePyramid(vec2 uv, float mip)
        {
            vec3 c = mix(texture(_Scene0, uv).rgb, texture(_Scene1, uv).rgb, clamp(mip, 0.0, 1.0));
            c = mix(c, texture(_Scene2, uv).rgb, clamp(mip - 1.0, 0.0, 1.0));
            c = mix(c, texture(_Scene3, uv).rgb, clamp(mip - 2.0, 0.0, 1.0));
            c = mix(c, texture(_Scene4, uv).rgb, clamp(mip - 3.0, 0.0, 1.0));
            return c;
        }

        // Smith-GGX visibility * GGX NDF * PI/4 -> the resolve weight (matched with 1/pdf).
        float brdfWeight(vec3 V, vec3 L, vec3 N, float roughness)
        {
            vec3 H = normalize(L + V);
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

        float edgeFade(vec2 pos, float value)
        {
            float d = min(1.0 - max(pos.x, pos.y), min(pos.x, pos.y));
            return clamp(value > 0.0 ? d / value : 1.0, 0.0, 1.0);
        }

        void main()
        {
            vec3 viewNormal = normalize(texture(_CameraNormalsTexture, TexCoords).xyz * 2.0 - 1.0);
            float roughness = texture(_CameraMotionVectorsTexture, TexCoords).b;
            float depth = texture(_CameraDepthTexture, TexCoords).r;
            vec3 viewPos = getViewPos(TexCoords, depth);
            vec3 V = normalize(-viewPos); // surface -> camera

            // Per-pixel rotation of the reuse offsets from blue noise.
            vec2 bn = texture(_Noise, (TexCoords + _JitterSizeAndOffset.zw) * _ScreenParams.xy / _NoiseSize).rg * 2.0 - 1.0;
            mat2 rot = mat2(bn.x, bn.y, -bn.y, bn.x);

            int numResolve = _RayReuse == 1 ? 4 : 1;
            float NdotV = clamp(dot(viewNormal, V), 0.0, 1.0);
            float coneTangent = mix(0.0, roughness * (1.0 - _BRDFBias), NdotV * sqrt(roughness));
            float maxMip = _MaxMipMap - 1.0;

            vec4 result = vec4(0.0);
            float weightSum = 0.0;
            for (int i = 0; i < numResolve; i++)
            {
                vec2 offset = rot * (resolveOffset[i] / _ResolveSize);
                vec2 nUV = TexCoords + offset;

                vec4 rd = texture(_RayCast, nUV);
                vec2 hitUV = rd.xy;
                float pdf = rd.z;
                float mask = rd.w;
                if (mask <= 0.0) continue;

                float hitDepth = texture(_CameraDepthTexture, hitUV).r;
                vec3 hitViewPos = getViewPos(hitUV, hitDepth);

                float weight = 1.0;
                if (_UseNormalization == 1)
                    weight = brdfWeight(V, normalize(hitViewPos - viewPos), viewNormal, roughness) / max(1e-5, pdf);

                float coneRadius = coneTangent * length(hitUV - TexCoords);
                float mip = clamp(log2(max(1e-5, coneRadius) * max(_ResolveSize.x, _ResolveSize.y)), 0.0, maxMip);

                vec4 s;
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
            fragColor = max(vec4(1e-5), result);
        }
    }
    ENDGLSL
}

//------------------------------------------------------------------------------------------------
Pass "Temporal"
{
    Tags { "RenderOrder" = "Opaque" }
    Blend Off ZTest Off ZWrite Off Cull Off

    GLSLPROGRAM

    Vertex
    {
        layout (location = 0) in vec3 vertexPosition;
        layout (location = 1) in vec2 vertexTexCoord;
        out vec2 TexCoords;
        void main() { gl_Position = vec4(vertexPosition, 1.0); TexCoords = vertexTexCoord; }
    }

    Fragment
    {
        #include "ProwlCG"

        uniform sampler2D _MainTex;        // current resolved reflection
        uniform sampler2D _PreviousBuffer; // reflection history
        uniform sampler2D _RayCast;
        uniform sampler2D _CameraDepthTexture;
        uniform sampler2D _CameraMotionVectorsTexture; // .rg motion
        uniform int _ReflectionVelocity;  // 1 = derive velocity from reflection hit depth
        uniform float _TScale;
        uniform float _TResponse;

        in vec2 TexCoords;
        layout(location = 0) out vec4 fragColor;

        // Reproject this pixel through the previous view-projection to get screen-space motion.
        vec2 reflectionVelocity(vec2 uv)
        {
            // Use the reflection hit point's depth so the history follows the reflected geometry.
            float hitDepthW = texture(_RayCast, uv).w > 0.0 ? texture(_CameraDepthTexture, texture(_RayCast, uv).xy).r
                                                            : texture(_CameraDepthTexture, uv).r;
            vec4 clip = vec4(uv * 2.0 - 1.0, hitDepthW * 2.0 - 1.0, 1.0);
            vec4 world = PROWL_MATRIX_I_VP * clip;
            world /= world.w;
            vec4 prevClip = PROWL_MATRIX_VP_PREVIOUS * world;
            vec2 prevUV = (prevClip.xy / prevClip.w) * 0.5 + 0.5;
            return uv - prevUV;
        }

        void main()
        {
            vec2 velocity = _ReflectionVelocity == 1 ? reflectionVelocity(TexCoords)
                                                     : texture(_CameraMotionVectorsTexture, TexCoords).rg;
            vec4 current = texture(_MainTex, TexCoords);
            vec4 previous = texture(_PreviousBuffer, TexCoords - velocity);

            vec2 du = vec2(1.0 / _ScreenParams.x, 0.0);
            vec2 dv = vec2(0.0, 1.0 / _ScreenParams.y);
            vec4 mn = current, mx = current;
            for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
            {
                vec4 c = texture(_MainTex, TexCoords + du * float(x) + dv * float(y));
                mn = min(mn, c); mx = max(mx, c);
            }
            vec4 center = (mn + mx) * 0.5;
            mn = (mn - center) * _TScale + center;
            mx = (mx - center) * _TScale + center;
            previous = clamp(previous, mn, mx);

            fragColor = mix(current, previous, clamp(_TResponse * (1.0 - length(velocity) * 8.0), 0.0, 1.0));
        }
    }
    ENDGLSL
}

//------------------------------------------------------------------------------------------------
Pass "Reproject"
{
    Tags { "RenderOrder" = "Opaque" }
    Blend Off ZTest Off ZWrite Off Cull Off

    GLSLPROGRAM

    Vertex
    {
        layout (location = 0) in vec3 vertexPosition;
        layout (location = 1) in vec2 vertexTexCoord;
        out vec2 TexCoords;
        void main() { gl_Position = vec4(vertexPosition, 1.0); TexCoords = vertexTexCoord; }
    }

    Fragment
    {
        #include "ProwlCG"

        uniform sampler2D _MainTex;                    // buffer to reproject (previous combined)
        uniform sampler2D _CameraMotionVectorsTexture; // .rg motion

        in vec2 TexCoords;
        layout(location = 0) out vec4 fragColor;

        void main()
        {
            vec2 velocity = texture(_CameraMotionVectorsTexture, TexCoords).rg;
            fragColor = texture(_MainTex, TexCoords - velocity);
        }
    }
    ENDGLSL
}

//------------------------------------------------------------------------------------------------
Pass "Combine"
{
    Tags { "RenderOrder" = "Opaque" }
    Blend Off ZTest Off ZWrite Off Cull Off

    GLSLPROGRAM

    Vertex
    {
        layout (location = 0) in vec3 vertexPosition;
        layout (location = 1) in vec2 vertexTexCoord;
        out vec2 TexCoords;
        void main() { gl_Position = vec4(vertexPosition, 1.0); TexCoords = vertexTexCoord; }
    }

    Fragment
    {
        #include "ProwlCG"

        uniform sampler2D _MainTex;          // scene colour
        uniform sampler2D _ReflectionBuffer; // resolved (+temporal) reflection
        uniform sampler2D _CameraDepthTexture;
        uniform sampler2D _CameraNormalsTexture;
        uniform sampler2D _CameraMotionVectorsTexture; // .b roughness, .a metallic
        uniform int _UseFresnel;

        in vec2 TexCoords;
        layout(location = 0) out vec4 fragColor;

        // Environment BRDF approximation (Karis "mobile") for the specular reflection response.
        vec3 envBRDFApprox(vec3 F0, float roughness, float NdotV)
        {
            vec4 c0 = vec4(-1.0, -0.0275, -0.572, 0.022);
            vec4 c1 = vec4(1.0, 0.0425, 1.04, -0.04);
            vec4 r = roughness * c0 + c1;
            float a004 = min(r.x * r.x, exp2(-9.28 * NdotV)) * r.x + r.y;
            vec2 ab = vec2(-1.04, 1.04) * a004 + r.zw;
            return F0 * ab.x + ab.y;
        }

        void main()
        {
            vec3 sceneColor = texture(_MainTex, TexCoords).rgb;
            vec4 reflection = texture(_ReflectionBuffer, TexCoords);

            float roughness = texture(_CameraMotionVectorsTexture, TexCoords).b;
            float metallic = texture(_CameraMotionVectorsTexture, TexCoords).a;
            vec3 viewNormal = normalize(texture(_CameraNormalsTexture, TexCoords).xyz * 2.0 - 1.0);
            float depth = texture(_CameraDepthTexture, TexCoords).r;
            vec3 V = normalize(-getViewPos(TexCoords, depth));
            float NdotV = clamp(dot(viewNormal, V), 0.0, 1.0);

            // Forward SSR has no G-buffer albedo: approximate it from the tonemapped scene colour.
            vec3 approxAlbedo = sceneColor / (1.0 + sceneColor);
            vec3 F0 = mix(vec3(0.04), approxAlbedo, metallic);

            float mask = reflection.a * reflection.a;
            vec3 refl = reflection.rgb;
            if (_UseFresnel == 1)
                refl *= envBRDFApprox(F0, roughness, NdotV);
            // Guard against runaway HDR and the one-bounce feedback loop diverging when Fresnel
            // (which normally attenuates each bounce) is disabled.
            refl = min(refl, vec3(8.0));

            fragColor = vec4(sceneColor + refl * mask, 1.0);
        }
    }
    ENDGLSL
}
