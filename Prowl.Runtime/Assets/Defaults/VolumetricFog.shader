Shader "Default/VolumetricFog"
{
    Pass
    {
        Name "FogMarch"
        Tags { "RenderOrder" = "Opaque" }
        ZTest Disabled
        ZWrite Off
        Cull Off

        SLANGPROGRAM

        // ----------------------- VERTEX START ----------------------
        layout (location = 0) in float3 vertexPosition;
        layout (location = 1) in float2 vertexTexCoord;

        out float2 TexCoords;

        void main()
        {
            TexCoords = vertexTexCoord;
            gl_Position = float4(vertexPosition, 1.0);
        }

        // ----------------------- FRAGMENT START ----------------------
        import ProwlCG;
        import Lighting;

        layout(location = 0) out float4 OutputColor;

        in float2 TexCoords;

        uniform Sampler2D<float4> _MainTex;
        uniform float2 _Resolution;
        uniform float2 _LowResolution;
        uniform Sampler2D<float4> _CameraDepthTexture;

        // Global fog uniforms
        uniform float _FogDensity;
        uniform float _FogScattering;
        uniform float _FogExtinction;
        uniform float _FogMaxDistance;
        uniform int _FogSteps;
        uniform float4 _FogColorTint;
        uniform float _FogDithering;
        uniform float4 _FogAmbientColor;
        uniform float _FogAmbientIntensity;

        // Type-level enable / shadow toggles. All BVH lights of an enabled type contribute to
        // fog scattering; per-light overrides were removed alongside the legacy FogLight
        // component when the BVH refactor landed.
        uniform int _FogEnableDirectional;
        uniform int _FogEnableDirectionalShadows;
        uniform int _FogEnablePointLights;
        uniform int _FogEnablePointShadows;
        uniform int _FogEnableSpotLights;
        uniform int _FogEnableSpotShadows;

        // Fog volumes
        #define MAX_FOG_VOLUMES 16
        uniform int _FogVolumeCount;
        uniform int _FogVolumeShape[MAX_FOG_VOLUMES];      // 0=Global, 1=Box, 2=Sphere, 3=Cylinder, 4=Cone
        uniform float3 _FogVolumePosition[MAX_FOG_VOLUMES];
        uniform float3 _FogVolumeSize[MAX_FOG_VOLUMES];
        uniform float4x4 _FogVolumeWorldToLocal[MAX_FOG_VOLUMES];
        uniform float _FogVolumeDensity[MAX_FOG_VOLUMES];
        uniform float4 _FogVolumeColor[MAX_FOG_VOLUMES];
        uniform float _FogVolumeFalloff[MAX_FOG_VOLUMES];
        uniform float _FogVolumeConeAngle[MAX_FOG_VOLUMES];

        // ── Phase function ──
        float HenyeyGreenstein(float cosTheta, float g)
        {
            float g2 = g * g;
            float denom = 1.0 + g2 - 2.0 * g * cosTheta;
            return (1.0 - g2) / (4.0 * 3.14159265 * pow(max(denom, 1e-4), 1.5));
        }

        // ── Camera reconstruction ──
        float3 ReconstructWorldRay(float2 uv)
        {
            float4 clip = float4(uv * 2.0 - 1.0, 1.0, 1.0);
            float4 viewPos = PROWL_MATRIX_I_P * clip;
            viewPos /= viewPos.w;
            float3 worldDir = (PROWL_MATRIX_I_V * float4(viewPos.xyz, 0.0)).xyz;
            return normalize(worldDir);
        }

        float LinearEyeDepth(float rawDepth)
        {
            float zNear = _ProjectionParams.y;
            float zFar = _ProjectionParams.z;
            float z = rawDepth * 2.0 - 1.0;
            return (2.0 * zNear * zFar) / (zFar + zNear - z * (zFar - zNear));
        }

        // Returns world-space distance from camera to the geometry at this UV.
        // Different from LinearEyeDepth, which only gives the view-space Z component
        // (along camera forward). For off-axis pixels the actual world distance is
        // greater because the ray is at an angle.
        float WorldDistFromDepth(float2 uv, float rawDepth)
        {
            float4 clip = float4(uv * 2.0 - 1.0, rawDepth * 2.0 - 1.0, 1.0);
            float4 worldPos = PROWL_MATRIX_I_VP * clip;
            worldPos.xyz /= worldPos.w;
            return distance(worldPos.xyz, _WorldSpaceCameraPos.xyz);
        }

        // ── Volumetric shadow sampling (no slope bias, no normal bias) ──
        float VolDirShadow(float3 worldPos)
        {
            if (_CascadeCount == 0) return 0.0;

            float worldDistance = distance(worldPos, _WorldSpaceCameraPos.xyz) * 2.0;
            float4x4 cascadeMatrix;
            float4 cascadeParams;

            if (_CascadeCount >= 1 && worldDistance <= _CascadeAtlasParams0.w) {
                cascadeMatrix = _CascadeShadowMatrix0;
                cascadeParams = _CascadeAtlasParams0;
            } else if (_CascadeCount >= 2 && worldDistance <= _CascadeAtlasParams1.w) {
                cascadeMatrix = _CascadeShadowMatrix1;
                cascadeParams = _CascadeAtlasParams1;
            } else if (_CascadeCount >= 3 && worldDistance <= _CascadeAtlasParams2.w) {
                cascadeMatrix = _CascadeShadowMatrix2;
                cascadeParams = _CascadeAtlasParams2;
            } else if (_CascadeCount >= 4 && worldDistance <= _CascadeAtlasParams3.w) {
                cascadeMatrix = _CascadeShadowMatrix3;
                cascadeParams = _CascadeAtlasParams3;
            } else {
                if (_CascadeCount == 1)      { cascadeMatrix = _CascadeShadowMatrix0; cascadeParams = _CascadeAtlasParams0; }
                else if (_CascadeCount == 2) { cascadeMatrix = _CascadeShadowMatrix1; cascadeParams = _CascadeAtlasParams1; }
                else if (_CascadeCount == 3) { cascadeMatrix = _CascadeShadowMatrix2; cascadeParams = _CascadeAtlasParams2; }
                else                         { cascadeMatrix = _CascadeShadowMatrix3; cascadeParams = _CascadeAtlasParams3; }
            }

            if (cascadeParams.z <= 0.0) return 0.0;

            float4 lightSpacePos = cascadeMatrix * float4(worldPos, 1.0);
            float3 projCoords = lightSpacePos.xyz / lightSpacePos.w;
            projCoords = projCoords * 0.5 + 0.5;
            if (projCoords.z > 1.0 || projCoords.x < 0.0 || projCoords.x > 1.0 || projCoords.y < 0.0 || projCoords.y > 1.0)
                return 0.0;

            float2 atlasCoords, shadowMin, shadowMax;
            GetAtlasCoordinates(projCoords, cascadeParams, _ShadowAtlasSize.x, atlasCoords, shadowMin, shadowMax);

            float currentDepth = projCoords.z - max(_DirectionalLightShadowBias, 0.0005);
            float lit = _ShadowAtlas.Sample(float3(atlasCoords, currentDepth));
            return (1.0 - lit) * _DirectionalLightShadowStrength;
        }

        float VolPointShadow(LightSample L, int shadowSlot, float3 worldPos)
        {
            float3 lightToFrag = worldPos - L.Position;
            float3 absDir = abs(lightToFrag);
            int faceIndex = 0;
            if (absDir.x >= absDir.y && absDir.x >= absDir.z)      faceIndex = lightToFrag.x > 0.0 ? 0 : 1;
            else if (absDir.y >= absDir.x && absDir.y >= absDir.z) faceIndex = lightToFrag.y > 0.0 ? 2 : 3;
            else                                                   faceIndex = lightToFrag.z > 0.0 ? 4 : 5;

            int idx = shadowSlot * 6 + faceIndex;
            float4x4 shadowMatrix = _PointShadowMatrices[idx];
            float4 faceParams = _PointShadowFaceParams[idx];

            float4 lightSpacePos = shadowMatrix * float4(worldPos, 1.0);
            float3 projCoords = lightSpacePos.xyz / lightSpacePos.w;
            projCoords = projCoords * 0.5 + 0.5;
            if (projCoords.z > 1.0 || projCoords.x < 0.0 || projCoords.x > 1.0 || projCoords.y < 0.0 || projCoords.y > 1.0)
                return 0.0;

            float2 atlasCoords, shadowMin, shadowMax;
            GetAtlasCoordinates(projCoords, faceParams, _ShadowAtlasSize.x, atlasCoords, shadowMin, shadowMax);

            float currentDepth = projCoords.z - max(L.ShadowBias, 0.0005);
            float lit = _ShadowAtlas.Sample(float3(atlasCoords, currentDepth));
            return (1.0 - lit) * L.ShadowStrength;
        }

        float VolSpotShadow(LightSample L, int shadowSlot, float3 worldPos)
        {
            float4 atlasParams = _SpotShadowAtlasParams[shadowSlot];
            if (atlasParams.z <= 0.0) return 0.0;

            float4 lightSpacePos = _SpotShadowMatrices[shadowSlot] * float4(worldPos, 1.0);
            float3 projCoords = lightSpacePos.xyz / lightSpacePos.w;
            projCoords = projCoords * 0.5 + 0.5;
            if (projCoords.z > 1.0 || projCoords.x < 0.0 || projCoords.x > 1.0 || projCoords.y < 0.0 || projCoords.y > 1.0)
                return 0.0;

            float2 atlasCoords, shadowMin, shadowMax;
            GetAtlasCoordinates(projCoords, atlasParams, _ShadowAtlasSize.x, atlasCoords, shadowMin, shadowMax);

            float currentDepth = projCoords.z - max(L.ShadowBias, 0.0005);
            float lit = _ShadowAtlas.Sample(float3(atlasCoords, currentDepth));
            return (1.0 - lit) * L.ShadowStrength;
        }

        float SpotAttenuation(LightSample L, float3 lightDir)
        {
            // Cosines come pre-baked into the leaf data so we skip per-step trig here.
            float cosFromAxis = dot(-lightDir, normalize(L.Direction));
            return clamp((cosFromAxis - L.SpotCos) / max(L.InnerSpotCos - L.SpotCos, 1e-4), 0.0, 1.0);
        }

        // Physical inverse-square + smooth window cutoff. Matches Lighting.glsl exactly.
        float DistanceAttenuation(float dist, float range)
        {
            float dist2 = dist * dist;
            float invR2 = 1.0 / max(range * range, 1e-8);
            float factor = dist2 * invR2;
            float window = clamp(1.0 - factor * factor, 0.0, 1.0);
            window *= window;
            float invSqr = 1.0 / max(dist2, 0.01);
            return invSqr * window;
        }

        float Hash13(float3 p)
        {
            p = frac(p * 0.1031);
            p += dot(p, p.yzx + 33.33);
            return frac((p.x + p.y) * p.z);
        }

        // ── Fog volume density evaluation ──
        // Returns density and color contribution at worldPos. Globals add their density
        // unconditionally; shaped volumes use SDF-based smooth falloff at edges.
        void EvaluateVolumes(float3 worldPos, out float density, out float3 color)
        {
            density = 0.0;
            color = float3(0.0);
            float colorWeight = 0.0;

            int count = min(_FogVolumeCount, MAX_FOG_VOLUMES);
            for (int i = 0; i < count; i++)
            {
                int shape = _FogVolumeShape[i];
                float falloff = max(_FogVolumeFalloff[i], 0.001);
                float w = 0.0;

                if (shape == 0) {
                    // Global
                    w = 1.0;
                }
                else {
                    float3 lp = (_FogVolumeWorldToLocal[i] * float4(worldPos, 1.0)).xyz;
                    if (shape == 1) {
                        // Box: half-extents from world scale (already baked into local space)
                        float3 d = abs(lp) - float3(1.0);
                        float outside = length(max(d, 0.0));
                        float inside = min(max(d.x, max(d.y, d.z)), 0.0);
                        float sdf = outside + inside;
                        w = clamp(1.0 - sdf / falloff, 0.0, 1.0);
                    }
                    else if (shape == 2) {
                        // Sphere: radius 1 in local space
                        float r = length(lp);
                        w = clamp(1.0 - (r - 1.0) / falloff, 0.0, 1.0);
                    }
                    else if (shape == 3) {
                        // Cylinder: radius 1, height 1 (Y axis)
                        float2 d = float2(length(lp.xz) - 1.0, abs(lp.y) - 1.0);
                        float outside = length(max(d, 0.0));
                        float inside = min(max(d.x, d.y), 0.0);
                        float sdf = outside + inside;
                        w = clamp(1.0 - sdf / falloff, 0.0, 1.0);
                    }
                    else if (shape == 4) {
                        // Cone: apex at origin, axis +Y, height = 1, half-angle from uniform
                        float halfAngle = radians(_FogVolumeConeAngle[i]);
                        float cosA = cos(halfAngle);
                        float sinA = sin(halfAngle);
                        // distance from cone axis cap
                        float2 q = float2(length(lp.xz), lp.y);
                        // distance to slanted side
                        float side = q.x * cosA - q.y * sinA;
                        // distance to cap
                        float cap = q.y - 1.0;
                        // also need to be inside (apex pointing at origin, opening upward)
                        float sdf = max(side, max(cap, -lp.y));
                        w = clamp(1.0 - sdf / falloff, 0.0, 1.0);
                    }
                }

                float d = _FogVolumeDensity[i] * w;
                density += d;

                float3 c = _FogVolumeColor[i].rgb;
                color += c * max(d, 0.0);
                colorWeight += max(d, 0.0);
            }

            color = colorWeight > 1e-4 ? color / colorWeight : float3(1.0);
        }

        // Single local-light contribution (point or spot). Returns inscatter float3 already
        // multiplied by the volume tint.
        float3 ScatterFromLocalLight(LightSample L, float3 worldPos, float3 viewDir, float3 volColor)
        {
            float3 d = L.Position - worldPos;
            float dist = length(d);
            if (dist > L.Range) return float3(0.0);
            float3 toLight = d / max(dist, 1e-4);

            float att = DistanceAttenuation(dist, L.Range);
            if (L.Type == 2) att *= SpotAttenuation(L, toLight);
            if (att <= 0.0) return float3(0.0);

            // Type-level enable / shadow toggles let users dim a whole light class without
            // touching every light individually.
            bool isPoint = (L.Type == 1);
            int  enableType   = isPoint ? _FogEnablePointLights : _FogEnableSpotLights;
            int  enableShadow = isPoint ? _FogEnablePointShadows : _FogEnableSpotShadows;
            if (enableType == 0) return float3(0.0);

            float phase = HenyeyGreenstein(dot(viewDir, -toLight), _FogScattering);
            float3 contrib = L.Color * (L.Intensity * 8.0) * phase * att;

            if (L.ShadowEnabled != 0 && L.ShadowSlot >= 0 && enableShadow != 0) {
                if (isPoint) contrib *= (1.0 - VolPointShadow(L, L.ShadowSlot, worldPos));
                else         contrib *= (1.0 - VolSpotShadow(L, L.ShadowSlot, worldPos));
            }

            return contrib * volColor;
        }

        // ── Per-step inscatter accumulation ──
        float3 SampleScattering(float3 worldPos, float3 viewDir, float3 volColor)
        {
            float3 scatter = float3(0.0);

            // Directional. _DirectionalLightDirection already points surface->sun.
            if (_DirectionalLightEnabled != 0 && _FogEnableDirectional != 0) {
                float3 toLight = normalize(_DirectionalLightDirection);
                float phase = HenyeyGreenstein(dot(viewDir, -toLight), _FogScattering);
                float3 contrib = _DirectionalLightColor * (_DirectionalLightIntensity * 8.0) * phase;
                if (_DirectionalLightShadowEnabled != 0 && _FogEnableDirectionalShadows != 0)
                    contrib *= (1.0 - VolDirShadow(worldPos));
                scatter += contrib * volColor;
            }

            // Static BVH.
            if (_StaticLightRoot >= 0) {
                LBVH_Iter it;
                LBVH_Begin(it, _StaticLightRoot);
                int slot;
                while ((slot = LBVH_Next(it, _StaticLightNodes, _StaticNodeTexSize, _StaticNodeTexShift, worldPos)) >= 0) {
                    LightSample L = LBVH_FetchLight(_StaticLightData, _StaticLightTexSize, _StaticLightTexShift, slot);
                    scatter += ScatterFromLocalLight(L, worldPos, viewDir, volColor);
                }
            }

            // Dynamic BVH.
            if (_DynamicLightRoot >= 0) {
                LBVH_Iter it;
                LBVH_Begin(it, _DynamicLightRoot);
                int slot;
                while ((slot = LBVH_Next(it, _DynamicLightNodes, _DynamicNodeTexSize, _DynamicNodeTexShift, worldPos)) >= 0) {
                    LightSample L = LBVH_FetchLight(_DynamicLightData, _DynamicLightTexSize, _DynamicLightTexShift, slot);
                    scatter += ScatterFromLocalLight(L, worldPos, viewDir, volColor);
                }
            }

            return scatter;
        }

        void main()
        {
            float2 uv = TexCoords;

            float rawDepth = _CameraDepthTexture.Sample(uv).r;

            // World distance from camera to the actual geometry (NOT eye-space Z).
            // We march along viewDir in world space, so we need world distance using
            // LinearEyeDepth here makes off-axis pixels under/overshoot the geometry,
            // causing fog to appear past surfaces (e.g. through the ground at oblique angles).
            float sceneDist = (rawDepth >= 0.9999) ? _FogMaxDistance : WorldDistFromDepth(uv, rawDepth);

            float marchDist = min(sceneDist, _FogMaxDistance);
            if (marchDist < 0.01) {
                OutputColor = float4(0.0, 0.0, 0.0, 1.0);
                return;
            }

            float3 cameraPos = _WorldSpaceCameraPos.xyz;
            float3 viewDir = ReconstructWorldRay(uv);

            int steps = max(_FogSteps, 1);
            float stepSize = marchDist / float(steps);

            float jitter = InterleavedGradientNoise(gl_FragCoord.xy + _Time.y * 60.0);

            float3 accum = float3(0.0);
            float transmittance = 1.0;
            float t = stepSize * jitter;

            for (int i = 0; i < steps; i++)
            {
                float3 samplePos = cameraPos + viewDir * t;

                // Density at this point: global + volumes (clamped to 0 if negatives carved it out)
                float volumeDensity;
                float3 volColor;
                EvaluateVolumes(samplePos, volumeDensity, volColor);
                float density = max(_FogDensity + volumeDensity, 0.0);

                if (density > 0.0) {
                    float3 lightInscatter = SampleScattering(samplePos, viewDir, volColor);
                    // Ambient adds a constant base keeps fog visible in shadow
                    // (sky bounce / GI approximation, not physically accurate).
                    float3 ambient = _FogAmbientColor.rgb * _FogAmbientIntensity * volColor;
                    float3 inscatter = (lightInscatter + ambient) * density;
                    inscatter *= _FogColorTint.rgb;

                    float stepTransmittance = exp(-density * _FogExtinction * stepSize);
                    accum += transmittance * inscatter * stepSize;
                    transmittance *= stepTransmittance;

                    if (transmittance < 0.01) break;
                }

                t += stepSize;
            }

            // Color dithering small per-pixel random shift to break up banding bands.
            if (_FogDithering > 0.0) {
                float dither = (Hash13(float3(gl_FragCoord.xy, _Time.y)) - 0.5) * _FogDithering;
                accum += float3(dither);
            }

            OutputColor = float4(accum, transmittance);
        }

        ENDSLANG
    }

    Pass
    {
        Name "FogTemporal"
        Tags { "RenderOrder" = "Opaque" }
        ZTest Disabled
        ZWrite Off
        Cull Off

        SLANGPROGRAM

        // ----------------------- VERTEX START ----------------------
        layout (location = 0) in float3 vertexPosition;
        layout (location = 1) in float2 vertexTexCoord;

        out float2 TexCoords;

        void main()
        {
            TexCoords = vertexTexCoord;
            gl_Position = float4(vertexPosition, 1.0);
        }

        // ----------------------- FRAGMENT START ----------------------
        import ProwlCG;

        layout(location = 0) out float4 OutputColor;

        in float2 TexCoords;

        uniform Sampler2D<float4> _FogCurrentTex;   // this frame's low-res march result
        uniform Sampler2D<float4> _FogHistoryTex;   // previous frame's (already temporally blended) result
        uniform Sampler2D<float4> _CameraDepthTexture;
        uniform float2 _LowResolution;
        uniform float _FogHistoryValid;     // 0 on first frame / after resize, 1 otherwise
        uniform float _FogTemporalBlend;    // history weight (0..0.99)

        void main()
        {
            float2 uv = TexCoords;
            float4 current = _FogCurrentTex.Sample(uv);

            if (_FogHistoryValid < 0.5) {
                OutputColor = current;
                return;
            }

            // Reproject this pixel's world position into the previous frame's screen space.
            // Uses the scene depth at the current pixel (not the fog march distance) since the
            // fog volume effectively sits on the geometry behind each pixel.
            float depth = _CameraDepthTexture.Sample(uv).r;
            float3 prev = Reproject(uv, depth, PROWL_MATRIX_VP_PREVIOUS);
            float2 prevUV = prev.xy;

            if (prevUV.x < 0.0 || prevUV.x > 1.0 || prevUV.y < 0.0 || prevUV.y > 1.0) {
                OutputColor = current;
                return;
            }

            float4 history = _FogHistoryTex.Sample(prevUV);

            // Neighborhood clamp: limit history to the min/max of the 3x3 current
            // neighborhood. Handles disocclusion and fast-moving lights without
            // needing previous-frame depth / motion vectors.
            float2 texel = 1.0 / _LowResolution;
            float4 mn = current;
            float4 mx = current;
            for (int dx = -1; dx <= 1; dx++) {
                for (int dy = -1; dy <= 1; dy++) {
                    if (dx == 0 && dy == 0) continue;
                    float4 s = _FogCurrentTex.Sample(uv + float2(float(dx), float(dy)) * texel);
                    mn = min(mn, s);
                    mx = max(mx, s);
                }
            }
            history = clamp(history, mn, mx);

            float alpha = clamp(_FogTemporalBlend, 0.0, 0.99);
            OutputColor = lerp(current, history, alpha);
        }

        ENDSLANG
    }

    Pass
    {
        Name "FogComposite"
        Tags { "RenderOrder" = "Opaque" }
        ZTest Disabled
        ZWrite Off
        Cull Off

        SLANGPROGRAM

        // ----------------------- VERTEX START ----------------------
        layout (location = 0) in float3 vertexPosition;
        layout (location = 1) in float2 vertexTexCoord;

        out float2 TexCoords;

        void main()
        {
            TexCoords = vertexTexCoord;
            gl_Position = float4(vertexPosition, 1.0);
        }

        // ----------------------- FRAGMENT START ----------------------
        import ProwlCG;

        layout(location = 0) out float4 OutputColor;

        in float2 TexCoords;

        uniform Sampler2D<float4> _MainTex;
        uniform Sampler2D<float4> _FogTex;
        uniform Sampler2D<float4> _CameraDepthTexture;
        uniform float2 _Resolution;
        uniform float2 _LowResolution;
        uniform float _FogUpsampleThreshold;

        float LinearEyeDepthC(float rawDepth)
        {
            float zNear = _ProjectionParams.y;
            float zFar = _ProjectionParams.z;
            float z = rawDepth * 2.0 - 1.0;
            return (2.0 * zNear * zFar) / (zFar + zNear - z * (zFar - zNear));
        }

        // Depth-aware nearest-tap upsample. Picks the half-res tap whose depth is
        // closest to the full-res depth avoids bleeding fog across silhouettes
        // where bilinear blending would lerp fog from sky pixels into ground pixels.
        float4 BilateralUpsample(float2 uv, float refDepth)
        {
            float2 lowTexel = 1.0 / _LowResolution;
            float2 offsets[4] = float2[](
                float2(-0.5, -0.5),
                float2( 0.5, -0.5),
                float2(-0.5,  0.5),
                float2( 0.5,  0.5)
            );

            float bestDiff = 1e30;
            int bestIdx = 0;
            float depths[4];

            for (int i = 0; i < 4; i++)
            {
                float2 sampleUV = uv + offsets[i] * lowTexel;
                depths[i] = LinearEyeDepthC(_CameraDepthTexture.Sample(sampleUV).r);
                float diff = abs(depths[i] - refDepth);
                if (diff < bestDiff) { bestDiff = diff; bestIdx = i; }
            }

            float relDiff = bestDiff / max(refDepth, 1.0);

            // If the best match is within threshold, blend the matching neighbors bilinearly.
            // Otherwise fall back to point sampling the best tap to avoid edge bleed.
            if (relDiff < _FogUpsampleThreshold) {
                float4 sumColor = float4(0.0);
                float sumWeight = 0.0;
                for (int i = 0; i < 4; i++)
                {
                    float dDiff = abs(depths[i] - refDepth) / max(refDepth, 1.0);
                    float w = exp(-dDiff / max(_FogUpsampleThreshold, 1e-4));
                    float2 sampleUV = uv + offsets[i] * lowTexel;
                    sumColor += _FogTex.Sample(sampleUV) * w;
                    sumWeight += w;
                }
                return sumColor / max(sumWeight, 1e-4);
            }
            else {
                float2 sampleUV = uv + offsets[bestIdx] * lowTexel;
                return _FogTex.Sample(sampleUV);
            }
        }

        void main()
        {
            float3 sceneColor = _MainTex.Sample(TexCoords).rgb;
            float alpha = _MainTex.Sample(TexCoords).a;

            float refDepth = LinearEyeDepthC(_CameraDepthTexture.Sample(TexCoords).r);
            float4 fog = BilateralUpsample(TexCoords, refDepth);

            // fog.rgb = scattering, fog.a = transmittance
            float3 finalColor = sceneColor * fog.a + fog.rgb;
            OutputColor = float4(finalColor, alpha);
        }

        ENDSLANG
    }
}
