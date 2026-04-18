Shader "Default/VolumetricFog"

Properties
{
}

Pass "FogMarch"
{
    Tags { "RenderOrder" = "Opaque" }

    Blend Off
    ZTest Off
    ZWrite Off
    Cull Off

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
        #include "Lighting"

        layout(location = 0) out vec4 OutputColor;

        in vec2 TexCoords;

        uniform sampler2D _MainTex;
        uniform vec2 _Resolution;
        uniform vec2 _LowResolution;
        uniform sampler2D _CameraDepthTexture;

        // Global fog uniforms
        uniform float _FogDensity;
        uniform float _FogScattering;
        uniform float _FogExtinction;
        uniform float _FogMaxDistance;
        uniform int _FogSteps;
        uniform vec4 _FogColorTint;
        uniform float _FogDithering;
        uniform vec4 _FogAmbientColor;
        uniform float _FogAmbientIntensity;

        // Per-light fog config (parallel to _Light* arrays from Lighting.glsl)
        #define MAX_FOG_LIGHTS 5
        uniform int _FogLightCount;
        uniform int _FogLightEnabled[MAX_FOG_LIGHTS];
        uniform int _FogLightCastShadows[MAX_FOG_LIGHTS];
        uniform int _FogLightUseOverride[MAX_FOG_LIGHTS];
        uniform vec4 _FogLightOverrideColor[MAX_FOG_LIGHTS];
        uniform float _FogLightIntensity[MAX_FOG_LIGHTS];
        uniform float _FogLightScatBias[MAX_FOG_LIGHTS];

        // Fog volumes
        #define MAX_FOG_VOLUMES 16
        uniform int _FogVolumeCount;
        uniform int _FogVolumeShape[MAX_FOG_VOLUMES];      // 0=Global, 1=Box, 2=Sphere, 3=Cylinder, 4=Cone
        uniform vec3 _FogVolumePosition[MAX_FOG_VOLUMES];
        uniform vec3 _FogVolumeSize[MAX_FOG_VOLUMES];
        uniform mat4 _FogVolumeWorldToLocal[MAX_FOG_VOLUMES];
        uniform float _FogVolumeDensity[MAX_FOG_VOLUMES];
        uniform vec4 _FogVolumeColor[MAX_FOG_VOLUMES];
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
        vec3 ReconstructWorldRay(vec2 uv)
        {
            vec4 clip = vec4(uv * 2.0 - 1.0, 1.0, 1.0);
            vec4 viewPos = inverse(PROWL_MATRIX_P) * clip;
            viewPos /= viewPos.w;
            vec3 worldDir = (PROWL_MATRIX_I_V * vec4(viewPos.xyz, 0.0)).xyz;
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
        float WorldDistFromDepth(vec2 uv, float rawDepth)
        {
            vec4 clip = vec4(uv * 2.0 - 1.0, rawDepth * 2.0 - 1.0, 1.0);
            vec4 worldPos = inverse(PROWL_MATRIX_VP) * clip;
            worldPos.xyz /= worldPos.w;
            return distance(worldPos.xyz, _WorldSpaceCameraPos.xyz);
        }

        // ── Volumetric shadow sampling (no slope bias, no normal bias) ──
        float VolDirShadow(vec3 worldPos)
        {
            if (_CascadeCount == 0) return 0.0;

            float worldDistance = distance(worldPos, _WorldSpaceCameraPos.xyz) * 2.0;
            mat4 cascadeMatrix;
            vec4 cascadeParams;

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

            vec4 lightSpacePos = cascadeMatrix * vec4(worldPos, 1.0);
            vec3 projCoords = lightSpacePos.xyz / lightSpacePos.w;
            projCoords = projCoords * 0.5 + 0.5;
            if (projCoords.z > 1.0 || projCoords.x < 0.0 || projCoords.x > 1.0 || projCoords.y < 0.0 || projCoords.y > 1.0)
                return 0.0;

            vec2 atlasCoords, shadowMin, shadowMax;
            GetAtlasCoordinates(projCoords, cascadeParams, _ShadowAtlasSize.x, atlasCoords, shadowMin, shadowMax);

            float currentDepth = projCoords.z - max(_LightShadowBias[0], 0.0005);
            float closestDepth = texture(_ShadowAtlas, atlasCoords).r;
            return (currentDepth > closestDepth ? 1.0 : 0.0) * _LightShadowStrength[0];
        }

        float VolPointShadow(int lightIndex, int shadowSlot, vec3 worldPos)
        {
            vec3 lightToFrag = worldPos - _LightPositions[lightIndex];
            vec3 absDir = abs(lightToFrag);
            int faceIndex = 0;
            if (absDir.x >= absDir.y && absDir.x >= absDir.z)      faceIndex = lightToFrag.x > 0.0 ? 0 : 1;
            else if (absDir.y >= absDir.x && absDir.y >= absDir.z) faceIndex = lightToFrag.y > 0.0 ? 2 : 3;
            else                                                   faceIndex = lightToFrag.z > 0.0 ? 4 : 5;

            int idx = shadowSlot * 6 + faceIndex;
            mat4 shadowMatrix = _PointShadowMatrices[idx];
            vec4 faceParams = _PointShadowFaceParams[idx];

            vec4 lightSpacePos = shadowMatrix * vec4(worldPos, 1.0);
            vec3 projCoords = lightSpacePos.xyz / lightSpacePos.w;
            projCoords = projCoords * 0.5 + 0.5;
            if (projCoords.z > 1.0 || projCoords.x < 0.0 || projCoords.x > 1.0 || projCoords.y < 0.0 || projCoords.y > 1.0)
                return 0.0;

            vec2 atlasCoords, shadowMin, shadowMax;
            GetAtlasCoordinates(projCoords, faceParams, _ShadowAtlasSize.x, atlasCoords, shadowMin, shadowMax);

            float currentDepth = projCoords.z - max(_LightShadowBias[lightIndex], 0.0005);
            float closestDepth = texture(_ShadowAtlas, atlasCoords).r;
            return (currentDepth > closestDepth ? 1.0 : 0.0) * _LightShadowStrength[lightIndex];
        }

        float VolSpotShadow(int lightIndex, int shadowSlot, vec3 worldPos)
        {
            vec4 atlasParams = _SpotShadowAtlasParams[shadowSlot];
            if (atlasParams.z <= 0.0) return 0.0;

            vec4 lightSpacePos = _SpotShadowMatrices[shadowSlot] * vec4(worldPos, 1.0);
            vec3 projCoords = lightSpacePos.xyz / lightSpacePos.w;
            projCoords = projCoords * 0.5 + 0.5;
            if (projCoords.z > 1.0 || projCoords.x < 0.0 || projCoords.x > 1.0 || projCoords.y < 0.0 || projCoords.y > 1.0)
                return 0.0;

            vec2 atlasCoords, shadowMin, shadowMax;
            GetAtlasCoordinates(projCoords, atlasParams, _ShadowAtlasSize.x, atlasCoords, shadowMin, shadowMax);

            float currentDepth = projCoords.z - max(_LightShadowBias[lightIndex], 0.0005);
            float closestDepth = texture(_ShadowAtlas, atlasCoords).r;
            return (currentDepth > closestDepth ? 1.0 : 0.0) * _LightShadowStrength[lightIndex];
        }

        float SpotAttenuation(int i, vec3 lightDir)
        {
            float outerAngle = radians(_LightSpotAngles[i]);
            float innerAngle = radians(_LightInnerSpotAngles[i]);
            float cosOuter = cos(outerAngle * 0.5);
            float cosInner = cos(innerAngle * 0.5);
            float cosFromAxis = dot(-lightDir, normalize(_LightDirections[i]));
            return clamp((cosFromAxis - cosOuter) / max(cosInner - cosOuter, 1e-4), 0.0, 1.0);
        }

        float DistanceAttenuation(float dist, float range)
        {
            float att = 1.0 / max(dist * dist, 1e-4);
            float window = clamp(1.0 - pow(dist / max(range, 1e-4), 4.0), 0.0, 1.0);
            return att * window * window;
        }

        float Hash13(vec3 p)
        {
            p = fract(p * 0.1031);
            p += dot(p, p.yzx + 33.33);
            return fract((p.x + p.y) * p.z);
        }

        // ── Fog volume density evaluation ──
        // Returns density and color contribution at worldPos. Globals add their density
        // unconditionally; shaped volumes use SDF-based smooth falloff at edges.
        void EvaluateVolumes(vec3 worldPos, out float density, out vec3 color)
        {
            density = 0.0;
            color = vec3(0.0);
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
                    vec3 lp = (_FogVolumeWorldToLocal[i] * vec4(worldPos, 1.0)).xyz;
                    if (shape == 1) {
                        // Box: half-extents from world scale (already baked into local space)
                        vec3 d = abs(lp) - vec3(1.0);
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
                        vec2 d = vec2(length(lp.xz) - 1.0, abs(lp.y) - 1.0);
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
                        vec2 q = vec2(length(lp.xz), lp.y);
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

                vec3 c = _FogVolumeColor[i].rgb;
                color += c * max(d, 0.0);
                colorWeight += max(d, 0.0);
            }

            color = colorWeight > 1e-4 ? color / colorWeight : vec3(1.0);
        }

        // ── Per-step inscatter accumulation ──
        vec3 SampleScattering(vec3 worldPos, vec3 viewDir, vec3 volColor)
        {
            vec3 scatter = vec3(0.0);
            int count = min(_LightCount, MAX_FORWARD_LIGHTS);
            int fogCount = min(MAX_FOG_LIGHTS, count);

            for (int i = 0; i < fogCount; i++)
            {
                if (_FogLightEnabled[i] == 0) continue;

                vec3 lightColor = _FogLightUseOverride[i] != 0
                    ? _FogLightOverrideColor[i].rgb
                    : _LightColors[i];

                float gBias = clamp(_FogScattering + _FogLightScatBias[i], -0.99, 0.99);
                vec3 contrib = vec3(0.0);
                vec3 toLight;
                float att = 1.0;

                if (_LightType[i] == 0) {
                    toLight = -normalize(_LightDirections[i]);
                    float phase = HenyeyGreenstein(dot(viewDir, -toLight), gBias);
                    contrib = lightColor * _LightIntensities[i] * phase * _FogLightIntensity[i];

                    if (_FogLightCastShadows[i] != 0)
                        contrib *= (1.0 - VolDirShadow(worldPos));
                }
                else if (_LightType[i] == 1) {
                    vec3 d = _LightPositions[i] - worldPos;
                    float dist = length(d);
                    if (dist > _LightRanges[i]) continue;
                    toLight = d / max(dist, 1e-4);
                    att = DistanceAttenuation(dist, _LightRanges[i]);
                    float phase = HenyeyGreenstein(dot(viewDir, -toLight), gBias);
                    contrib = lightColor * _LightIntensities[i] * phase * att * _FogLightIntensity[i];

                    int slot = _LightShadowSlot[i];
                    if (_FogLightCastShadows[i] != 0 && slot >= 0)
                        contrib *= (1.0 - VolPointShadow(i, slot, worldPos));
                }
                else {
                    vec3 d = _LightPositions[i] - worldPos;
                    float dist = length(d);
                    if (dist > _LightRanges[i]) continue;
                    toLight = d / max(dist, 1e-4);
                    att = DistanceAttenuation(dist, _LightRanges[i]) * SpotAttenuation(i, toLight);
                    if (att <= 0.0) continue;
                    float phase = HenyeyGreenstein(dot(viewDir, -toLight), gBias);
                    contrib = lightColor * _LightIntensities[i] * phase * att * _FogLightIntensity[i];

                    int slot = _LightShadowSlot[i];
                    if (_FogLightCastShadows[i] != 0 && slot >= 0)
                        contrib *= (1.0 - VolSpotShadow(i, slot, worldPos));
                }

                scatter += contrib * volColor;
            }

            return scatter;
        }

        void main()
        {
            vec2 uv = TexCoords;

            float rawDepth = texture(_CameraDepthTexture, uv).r;

            // World distance from camera to the actual geometry (NOT eye-space Z).
            // We march along viewDir in world space, so we need world distance — using
            // LinearEyeDepth here makes off-axis pixels under/overshoot the geometry,
            // causing fog to appear past surfaces (e.g. through the ground at oblique angles).
            float sceneDist = (rawDepth >= 0.9999) ? _FogMaxDistance : WorldDistFromDepth(uv, rawDepth);

            float marchDist = min(sceneDist, _FogMaxDistance);
            if (marchDist < 0.01) {
                OutputColor = vec4(0.0, 0.0, 0.0, 1.0);
                return;
            }

            vec3 cameraPos = _WorldSpaceCameraPos.xyz;
            vec3 viewDir = ReconstructWorldRay(uv);

            int steps = max(_FogSteps, 1);
            float stepSize = marchDist / float(steps);

            float jitter = InterleavedGradientNoise(gl_FragCoord.xy + _Time.y * 60.0);

            vec3 accum = vec3(0.0);
            float transmittance = 1.0;
            float t = stepSize * jitter;

            for (int i = 0; i < steps; i++)
            {
                vec3 samplePos = cameraPos + viewDir * t;

                // Density at this point: global + volumes (clamped to 0 if negatives carved it out)
                float volumeDensity;
                vec3 volColor;
                EvaluateVolumes(samplePos, volumeDensity, volColor);
                float density = max(_FogDensity + volumeDensity, 0.0);

                if (density > 0.0) {
                    vec3 lightInscatter = SampleScattering(samplePos, viewDir, volColor);
                    // Ambient adds a constant base — keeps fog visible in shadow
                    // (sky bounce / GI approximation, not physically accurate).
                    vec3 ambient = _FogAmbientColor.rgb * _FogAmbientIntensity * volColor;
                    vec3 inscatter = (lightInscatter + ambient) * density;
                    inscatter *= _FogColorTint.rgb;

                    float stepTransmittance = exp(-density * _FogExtinction * stepSize);
                    accum += transmittance * inscatter * stepSize;
                    transmittance *= stepTransmittance;

                    if (transmittance < 0.01) break;
                }

                t += stepSize;
            }

            // Color dithering — small per-pixel random shift to break up banding bands.
            if (_FogDithering > 0.0) {
                float dither = (Hash13(vec3(gl_FragCoord.xy, _Time.y)) - 0.5) * _FogDithering;
                accum += vec3(dither);
            }

            OutputColor = vec4(accum, transmittance);
        }
    }

    ENDGLSL
}

Pass "FogComposite"
{
    Tags { "RenderOrder" = "Opaque" }

    Blend Off
    ZTest Off
    ZWrite Off
    Cull Off

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
        uniform sampler2D _FogTex;
        uniform sampler2D _CameraDepthTexture;
        uniform vec2 _Resolution;
        uniform vec2 _LowResolution;
        uniform float _FogUpsampleThreshold;

        float LinearEyeDepthC(float rawDepth)
        {
            float zNear = _ProjectionParams.y;
            float zFar = _ProjectionParams.z;
            float z = rawDepth * 2.0 - 1.0;
            return (2.0 * zNear * zFar) / (zFar + zNear - z * (zFar - zNear));
        }

        // Depth-aware nearest-tap upsample. Picks the half-res tap whose depth is
        // closest to the full-res depth — avoids bleeding fog across silhouettes
        // where bilinear blending would mix fog from sky pixels into ground pixels.
        vec4 BilateralUpsample(vec2 uv, float refDepth)
        {
            vec2 lowTexel = 1.0 / _LowResolution;
            vec2 offsets[4] = vec2[](
                vec2(-0.5, -0.5),
                vec2( 0.5, -0.5),
                vec2(-0.5,  0.5),
                vec2( 0.5,  0.5)
            );

            float bestDiff = 1e30;
            int bestIdx = 0;
            float depths[4];

            for (int i = 0; i < 4; i++)
            {
                vec2 sampleUV = uv + offsets[i] * lowTexel;
                depths[i] = LinearEyeDepthC(texture(_CameraDepthTexture, sampleUV).r);
                float diff = abs(depths[i] - refDepth);
                if (diff < bestDiff) { bestDiff = diff; bestIdx = i; }
            }

            float relDiff = bestDiff / max(refDepth, 1.0);

            // If the best match is within threshold, blend the matching neighbors bilinearly.
            // Otherwise fall back to point sampling the best tap to avoid edge bleed.
            if (relDiff < _FogUpsampleThreshold) {
                vec4 sumColor = vec4(0.0);
                float sumWeight = 0.0;
                for (int i = 0; i < 4; i++)
                {
                    float dDiff = abs(depths[i] - refDepth) / max(refDepth, 1.0);
                    float w = exp(-dDiff / max(_FogUpsampleThreshold, 1e-4));
                    vec2 sampleUV = uv + offsets[i] * lowTexel;
                    sumColor += texture(_FogTex, sampleUV) * w;
                    sumWeight += w;
                }
                return sumColor / max(sumWeight, 1e-4);
            }
            else {
                vec2 sampleUV = uv + offsets[bestIdx] * lowTexel;
                return texture(_FogTex, sampleUV);
            }
        }

        void main()
        {
            vec3 sceneColor = texture(_MainTex, TexCoords).rgb;
            float alpha = texture(_MainTex, TexCoords).a;

            float refDepth = LinearEyeDepthC(texture(_CameraDepthTexture, TexCoords).r);
            vec4 fog = BilateralUpsample(TexCoords, refDepth);

            // fog.rgb = scattering, fog.a = transmittance
            vec3 finalColor = sceneColor * fog.a + fog.rgb;
            OutputColor = vec4(finalColor, alpha);
        }
    }

    ENDGLSL
}
