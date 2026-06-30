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
        import ProwlCG;
        import Lighting;

        static const int MAX_FOG_VOLUMES = 16;

        struct MaterialData
        {
            Sampler2D<float4> _CameraDepthTexture;

            float _FogDensity;
            float _FogScattering;
            float _FogExtinction;
            float _FogMaxDistance;
            int _FogSteps;
            float4 _FogColorTint;
            float _FogDithering;
            float4 _FogAmbientColor;
            float _FogAmbientIntensity;

            int _FogEnableDirectional;
            int _FogEnableDirectionalShadows;
            int _FogEnablePointLights;
            int _FogEnablePointShadows;
            int _FogEnableSpotLights;
            int _FogEnableSpotShadows;

            int _FogVolumeCount;
            int _FogVolumeShape[MAX_FOG_VOLUMES];
            float3 _FogVolumePosition[MAX_FOG_VOLUMES];
            float3 _FogVolumeSize[MAX_FOG_VOLUMES];
            float4x4 _FogVolumeWorldToLocal[MAX_FOG_VOLUMES];
            float _FogVolumeDensity[MAX_FOG_VOLUMES];
            float4 _FogVolumeColor[MAX_FOG_VOLUMES];
            float _FogVolumeFalloff[MAX_FOG_VOLUMES];
            float _FogVolumeConeAngle[MAX_FOG_VOLUMES];
        }
        ParameterBlock<MaterialData> Mat;

        struct VertexInput { float3 position : POSITION0; float2 uv : TEXCOORD0; }
        struct Varyings { float4 position : SV_Position; float2 uv : TEXCOORD0; }

        float HenyeyGreenstein(float cosTheta, float g)
        {
            float g2 = g * g;
            float denom = 1.0 + g2 - 2.0 * g * cosTheta;
            return (1.0 - g2) / (4.0 * 3.14159265 * pow(max(denom, 1e-4), 1.5));
        }

        float3 ReconstructWorldRay(float2 uv)
        {
            float4 clip = float4(uv * 2.0 - 1.0, 1.0, 1.0);
            float4 viewPos = mul(Frame.prowl_MatIP, clip);
            viewPos /= viewPos.w;
            float3 worldDir = mul(Frame.prowl_MatIV, float4(viewPos.xyz, 0.0)).xyz;
            return normalize(worldDir);
        }

        float LinearEyeDepth(float rawDepth)
        {
            float zNear = Frame._ProjectionParams.y;
            float zFar = Frame._ProjectionParams.z;
            float z = rawDepth * 2.0 - 1.0;
            return (2.0 * zNear * zFar) / (zFar + zNear - z * (zFar - zNear));
        }

        float WorldDistFromDepth(float2 uv, float rawDepth)
        {
            float4 clip = float4(uv * 2.0 - 1.0, rawDepth * 2.0 - 1.0, 1.0);
            float4 worldPos = mul(Frame.prowl_MatIVP, clip);
            worldPos.xyz /= worldPos.w;
            return distance(worldPos.xyz, Frame._WorldSpaceCameraPos.xyz);
        }

        float VolDirShadow(float3 worldPos)
        {
            if (Lights._CascadeCount == 0) return 0.0;

            float worldDistance = distance(worldPos, Frame._WorldSpaceCameraPos.xyz) * 2.0;
            float4x4 cascadeMatrix;
            float4 cascadeParams;

            if (Lights._CascadeCount >= 1 && worldDistance <= Lights._CascadeAtlasParams0.w) {
                cascadeMatrix = Lights._CascadeShadowMatrix0; cascadeParams = Lights._CascadeAtlasParams0;
            } else if (Lights._CascadeCount >= 2 && worldDistance <= Lights._CascadeAtlasParams1.w) {
                cascadeMatrix = Lights._CascadeShadowMatrix1; cascadeParams = Lights._CascadeAtlasParams1;
            } else if (Lights._CascadeCount >= 3 && worldDistance <= Lights._CascadeAtlasParams2.w) {
                cascadeMatrix = Lights._CascadeShadowMatrix2; cascadeParams = Lights._CascadeAtlasParams2;
            } else if (Lights._CascadeCount >= 4 && worldDistance <= Lights._CascadeAtlasParams3.w) {
                cascadeMatrix = Lights._CascadeShadowMatrix3; cascadeParams = Lights._CascadeAtlasParams3;
            } else {
                if (Lights._CascadeCount == 1)      { cascadeMatrix = Lights._CascadeShadowMatrix0; cascadeParams = Lights._CascadeAtlasParams0; }
                else if (Lights._CascadeCount == 2) { cascadeMatrix = Lights._CascadeShadowMatrix1; cascadeParams = Lights._CascadeAtlasParams1; }
                else if (Lights._CascadeCount == 3) { cascadeMatrix = Lights._CascadeShadowMatrix2; cascadeParams = Lights._CascadeAtlasParams2; }
                else                                { cascadeMatrix = Lights._CascadeShadowMatrix3; cascadeParams = Lights._CascadeAtlasParams3; }
            }

            if (cascadeParams.z <= 0.0) return 0.0;

            float4 lightSpacePos = mul(cascadeMatrix, float4(worldPos, 1.0));
            float3 projCoords = lightSpacePos.xyz / lightSpacePos.w;
            projCoords = projCoords * 0.5 + 0.5;
            if (projCoords.z > 1.0 || projCoords.x < 0.0 || projCoords.x > 1.0 || projCoords.y < 0.0 || projCoords.y > 1.0)
                return 0.0;

            float2 atlasCoords, shadowMin, shadowMax;
            GetAtlasCoordinates(projCoords, cascadeParams, Lights._ShadowAtlasSize.x, atlasCoords, shadowMin, shadowMax);

            float currentDepth = projCoords.z - max(Lights._DirectionalLightShadowBias, 0.0005);
            float lit = Lights._ShadowAtlas.SampleCmp(atlasCoords, currentDepth);
            return (1.0 - lit) * Lights._DirectionalLightShadowStrength;
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
            float4x4 shadowMatrix = Lights._PointShadowMatrices[idx];
            float4 faceParams = Lights._PointShadowFaceParams[idx];

            float4 lightSpacePos = mul(shadowMatrix, float4(worldPos, 1.0));
            float3 projCoords = lightSpacePos.xyz / lightSpacePos.w;
            projCoords = projCoords * 0.5 + 0.5;
            if (projCoords.z > 1.0 || projCoords.x < 0.0 || projCoords.x > 1.0 || projCoords.y < 0.0 || projCoords.y > 1.0)
                return 0.0;

            float2 atlasCoords, shadowMin, shadowMax;
            GetAtlasCoordinates(projCoords, faceParams, Lights._ShadowAtlasSize.x, atlasCoords, shadowMin, shadowMax);

            float currentDepth = projCoords.z - max(L.ShadowBias, 0.0005);
            float lit = Lights._ShadowAtlas.SampleCmp(atlasCoords, currentDepth);
            return (1.0 - lit) * L.ShadowStrength;
        }

        float VolSpotShadow(LightSample L, int shadowSlot, float3 worldPos)
        {
            float4 atlasParams = Lights._SpotShadowAtlasParams[shadowSlot];
            if (atlasParams.z <= 0.0) return 0.0;

            float4 lightSpacePos = mul(Lights._SpotShadowMatrices[shadowSlot], float4(worldPos, 1.0));
            float3 projCoords = lightSpacePos.xyz / lightSpacePos.w;
            projCoords = projCoords * 0.5 + 0.5;
            if (projCoords.z > 1.0 || projCoords.x < 0.0 || projCoords.x > 1.0 || projCoords.y < 0.0 || projCoords.y > 1.0)
                return 0.0;

            float2 atlasCoords, shadowMin, shadowMax;
            GetAtlasCoordinates(projCoords, atlasParams, Lights._ShadowAtlasSize.x, atlasCoords, shadowMin, shadowMax);

            float currentDepth = projCoords.z - max(L.ShadowBias, 0.0005);
            float lit = Lights._ShadowAtlas.SampleCmp(atlasCoords, currentDepth);
            return (1.0 - lit) * L.ShadowStrength;
        }

        float SpotAttenuation(LightSample L, float3 lightDir)
        {
            float cosFromAxis = dot(-lightDir, normalize(L.Direction));
            return clamp((cosFromAxis - L.SpotCos) / max(L.InnerSpotCos - L.SpotCos, 1e-4), 0.0, 1.0);
        }

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

        void EvaluateVolumes(float3 worldPos, out float density, out float3 color)
        {
            density = 0.0;
            color = float3(0.0);
            float colorWeight = 0.0;

            int count = min(Mat._FogVolumeCount, MAX_FOG_VOLUMES);
            for (int i = 0; i < count; i++)
            {
                int shape = Mat._FogVolumeShape[i];
                float falloff = max(Mat._FogVolumeFalloff[i], 0.001);
                float w = 0.0;

                if (shape == 0) {
                    w = 1.0;
                }
                else {
                    float3 lp = mul(Mat._FogVolumeWorldToLocal[i], float4(worldPos, 1.0)).xyz;
                    if (shape == 1) {
                        float3 d = abs(lp) - float3(1.0);
                        float outside = length(max(d, 0.0));
                        float inside = min(max(d.x, max(d.y, d.z)), 0.0);
                        float sdf = outside + inside;
                        w = clamp(1.0 - sdf / falloff, 0.0, 1.0);
                    }
                    else if (shape == 2) {
                        float r = length(lp);
                        w = clamp(1.0 - (r - 1.0) / falloff, 0.0, 1.0);
                    }
                    else if (shape == 3) {
                        float2 d = float2(length(lp.xz) - 1.0, abs(lp.y) - 1.0);
                        float outside = length(max(d, 0.0));
                        float inside = min(max(d.x, d.y), 0.0);
                        float sdf = outside + inside;
                        w = clamp(1.0 - sdf / falloff, 0.0, 1.0);
                    }
                    else if (shape == 4) {
                        float halfAngle = radians(Mat._FogVolumeConeAngle[i]);
                        float cosA = cos(halfAngle);
                        float sinA = sin(halfAngle);
                        float2 q = float2(length(lp.xz), lp.y);
                        float side = q.x * cosA - q.y * sinA;
                        float cap = q.y - 1.0;
                        float sdf = max(side, max(cap, -lp.y));
                        w = clamp(1.0 - sdf / falloff, 0.0, 1.0);
                    }
                }

                float d = Mat._FogVolumeDensity[i] * w;
                density += d;

                float3 c = Mat._FogVolumeColor[i].rgb;
                color += c * max(d, 0.0);
                colorWeight += max(d, 0.0);
            }

            color = colorWeight > 1e-4 ? color / colorWeight : float3(1.0);
        }

        float3 ScatterFromLocalLight(LightSample L, float3 worldPos, float3 viewDir, float3 volColor)
        {
            float3 d = L.Position - worldPos;
            float dist = length(d);
            if (dist > L.Range) return float3(0.0);
            float3 toLight = d / max(dist, 1e-4);

            float att = DistanceAttenuation(dist, L.Range);
            if (L.Type == 2) att *= SpotAttenuation(L, toLight);
            if (att <= 0.0) return float3(0.0);

            bool isPoint = (L.Type == 1);
            int enableType   = isPoint ? Mat._FogEnablePointLights : Mat._FogEnableSpotLights;
            int enableShadow = isPoint ? Mat._FogEnablePointShadows : Mat._FogEnableSpotShadows;
            if (enableType == 0) return float3(0.0);

            float phase = HenyeyGreenstein(dot(viewDir, -toLight), Mat._FogScattering);
            float3 contrib = L.Color * (L.Intensity * 8.0) * phase * att;

            if (L.ShadowEnabled != 0 && L.ShadowSlot >= 0 && enableShadow != 0) {
                if (isPoint) contrib *= (1.0 - VolPointShadow(L, L.ShadowSlot, worldPos));
                else         contrib *= (1.0 - VolSpotShadow(L, L.ShadowSlot, worldPos));
            }

            return contrib * volColor;
        }

        float3 SampleScattering(float3 worldPos, float3 viewDir, float3 volColor)
        {
            float3 scatter = float3(0.0);

            if (Lights._DirectionalLightEnabled != 0 && Mat._FogEnableDirectional != 0) {
                float3 toLight = normalize(Lights._DirectionalLightDirection);
                float phase = HenyeyGreenstein(dot(viewDir, -toLight), Mat._FogScattering);
                float3 contrib = Lights._DirectionalLightColor * (Lights._DirectionalLightIntensity * 8.0) * phase;
                if (Lights._DirectionalLightShadowEnabled != 0 && Mat._FogEnableDirectionalShadows != 0)
                    contrib *= (1.0 - VolDirShadow(worldPos));
                scatter += contrib * volColor;
            }

            if (Lights._StaticLightRoot >= 0) {
                LBVH_Iter it;
                LBVH_Begin(it, Lights._StaticLightRoot);
                int slot;
                while ((slot = LBVH_Next(it, Lights._StaticLightNodes, Lights._StaticNodeTexSize, Lights._StaticNodeTexShift, worldPos)) >= 0) {
                    LightSample L = LBVH_FetchLight(Lights._StaticLightData, Lights._StaticLightTexSize, Lights._StaticLightTexShift, slot);
                    scatter += ScatterFromLocalLight(L, worldPos, viewDir, volColor);
                }
            }

            if (Lights._DynamicLightRoot >= 0) {
                LBVH_Iter it;
                LBVH_Begin(it, Lights._DynamicLightRoot);
                int slot;
                while ((slot = LBVH_Next(it, Lights._DynamicLightNodes, Lights._DynamicNodeTexSize, Lights._DynamicNodeTexShift, worldPos)) >= 0) {
                    LightSample L = LBVH_FetchLight(Lights._DynamicLightData, Lights._DynamicLightTexSize, Lights._DynamicLightTexShift, slot);
                    scatter += ScatterFromLocalLight(L, worldPos, viewDir, volColor);
                }
            }

            return scatter;
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
            float2 uv = input.uv;

            float rawDepth = Mat._CameraDepthTexture.Sample(uv).r;

            float sceneDist = (rawDepth >= 0.9999) ? Mat._FogMaxDistance : WorldDistFromDepth(uv, rawDepth);

            float marchDist = min(sceneDist, Mat._FogMaxDistance);
            if (marchDist < 0.01)
                return float4(0.0, 0.0, 0.0, 1.0);

            float3 cameraPos = Frame._WorldSpaceCameraPos.xyz;
            float3 viewDir = ReconstructWorldRay(uv);

            int steps = max(Mat._FogSteps, 1);
            float stepSize = marchDist / float(steps);

            float jitter = InterleavedGradientNoise(input.position.xy + Frame._Time.y * 60.0);

            float3 accum = float3(0.0);
            float transmittance = 1.0;
            float t = stepSize * jitter;

            for (int i = 0; i < steps; i++)
            {
                float3 samplePos = cameraPos + viewDir * t;

                float volumeDensity;
                float3 volColor;
                EvaluateVolumes(samplePos, volumeDensity, volColor);
                float density = max(Mat._FogDensity + volumeDensity, 0.0);

                if (density > 0.0) {
                    float3 lightInscatter = SampleScattering(samplePos, viewDir, volColor);
                    float3 ambient = Mat._FogAmbientColor.rgb * Mat._FogAmbientIntensity * volColor;
                    float3 inscatter = (lightInscatter + ambient) * density;
                    inscatter *= Mat._FogColorTint.rgb;

                    float stepTransmittance = exp(-density * Mat._FogExtinction * stepSize);
                    accum += transmittance * inscatter * stepSize;
                    transmittance *= stepTransmittance;

                    if (transmittance < 0.01) break;
                }

                t += stepSize;
            }

            if (Mat._FogDithering > 0.0) {
                float dither = (Hash13(float3(input.position.xy, Frame._Time.y)) - 0.5) * Mat._FogDithering;
                accum += float3(dither);
            }

            return float4(accum, transmittance);
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
        import ProwlCG;

        struct MaterialData
        {
            Sampler2D<float4> _FogCurrentTex;
            Sampler2D<float4> _FogHistoryTex;
            Sampler2D<float4> _CameraDepthTexture;
            float2 _LowResolution;
            float _FogHistoryValid;
            float _FogTemporalBlend;
        }
        ParameterBlock<MaterialData> Mat;

        struct VertexInput { float3 position : POSITION0; float2 uv : TEXCOORD0; }
        struct Varyings { float4 position : SV_Position; float2 uv : TEXCOORD0; }

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
            float2 uv = input.uv;
            float4 current = Mat._FogCurrentTex.Sample(uv);

            if (Mat._FogHistoryValid < 0.5)
                return current;

            float depth = Mat._CameraDepthTexture.Sample(uv).r;
            float3 prev = Reproject(uv, depth, Frame.prowl_PrevViewProj);
            float2 prevUV = prev.xy;

            if (prevUV.x < 0.0 || prevUV.x > 1.0 || prevUV.y < 0.0 || prevUV.y > 1.0)
                return current;

            float4 history = Mat._FogHistoryTex.Sample(prevUV);

            float2 texel = 1.0 / Mat._LowResolution;
            float4 mn = current;
            float4 mx = current;
            for (int dx = -1; dx <= 1; dx++) {
                for (int dy = -1; dy <= 1; dy++) {
                    if (dx == 0 && dy == 0) continue;
                    float4 s = Mat._FogCurrentTex.Sample(uv + float2(float(dx), float(dy)) * texel);
                    mn = min(mn, s);
                    mx = max(mx, s);
                }
            }
            history = clamp(history, mn, mx);

            float alpha = clamp(Mat._FogTemporalBlend, 0.0, 0.99);
            return lerp(current, history, alpha);
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
        import ProwlCG;

        struct MaterialData
        {
            Sampler2D<float4> _MainTex;
            Sampler2D<float4> _FogTex;
            Sampler2D<float4> _CameraDepthTexture;
            float2 _Resolution;
            float2 _LowResolution;
            float _FogUpsampleThreshold;
        }
        ParameterBlock<MaterialData> Mat;

        struct VertexInput { float3 position : POSITION0; float2 uv : TEXCOORD0; }
        struct Varyings { float4 position : SV_Position; float2 uv : TEXCOORD0; }

        float LinearEyeDepthC(float rawDepth)
        {
            float zNear = Frame._ProjectionParams.y;
            float zFar = Frame._ProjectionParams.z;
            float z = rawDepth * 2.0 - 1.0;
            return (2.0 * zNear * zFar) / (zFar + zNear - z * (zFar - zNear));
        }

        float4 BilateralUpsample(float2 uv, float refDepth)
        {
            float2 lowTexel = 1.0 / Mat._LowResolution;
            float2 offsets[4] = {
                float2(-0.5, -0.5),
                float2( 0.5, -0.5),
                float2(-0.5,  0.5),
                float2( 0.5,  0.5)
            };

            float bestDiff = 1e30;
            int bestIdx = 0;
            float depths[4];

            for (int i = 0; i < 4; i++)
            {
                float2 sampleUV = uv + offsets[i] * lowTexel;
                depths[i] = LinearEyeDepthC(Mat._CameraDepthTexture.Sample(sampleUV).r);
                float diff = abs(depths[i] - refDepth);
                if (diff < bestDiff) { bestDiff = diff; bestIdx = i; }
            }

            float relDiff = bestDiff / max(refDepth, 1.0);

            if (relDiff < Mat._FogUpsampleThreshold) {
                float4 sumColor = float4(0.0);
                float sumWeight = 0.0;
                for (int i = 0; i < 4; i++)
                {
                    float dDiff = abs(depths[i] - refDepth) / max(refDepth, 1.0);
                    float w = exp(-dDiff / max(Mat._FogUpsampleThreshold, 1e-4));
                    float2 sampleUV = uv + offsets[i] * lowTexel;
                    sumColor += Mat._FogTex.Sample(sampleUV) * w;
                    sumWeight += w;
                }
                return sumColor / max(sumWeight, 1e-4);
            }
            else {
                float2 sampleUV = uv + offsets[bestIdx] * lowTexel;
                return Mat._FogTex.Sample(sampleUV);
            }
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
            float3 sceneColor = Mat._MainTex.Sample(input.uv).rgb;
            float alpha = Mat._MainTex.Sample(input.uv).a;

            float refDepth = LinearEyeDepthC(Mat._CameraDepthTexture.Sample(input.uv).r);
            float4 fog = BilateralUpsample(input.uv, refDepth);

            float3 finalColor = sceneColor * fog.a + fog.rgb;
            return float4(finalColor, alpha);
        }
        ENDSLANG
    }
}
