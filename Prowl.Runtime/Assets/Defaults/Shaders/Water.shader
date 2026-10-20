Shader "Default/Water"
{
    Properties
    {
        _Heightmap("Terrain Heightmap", Texture2D) = "black"
        _WaterMap("Water Map", Texture2D) = "black"
        _WaterLevelMap("Water Levels", Texture2D) = "black"
        _HolesMap("Holes Map", Texture2D) = "white"
        _HasHoles("Has Holes", Integer) = 0
        _ShallowWaterColor("Shallow Color", Color) = (0.13, 0.55, 0.62, 1.0)
        _DeepWaterColor("Deep Color", Color) = (0.02, 0.16, 0.30, 1.0)
        _WaterVisibilityDepth("Visibility Depth", Float) = 2.0
        _RippleStrength("Ripple Strength", Float) = 0.35
        _RippleWavelength("Ripple Wavelength", Float) = 3.0
        _RippleSpeed("Ripple Speed", Float) = 0.25
        _TerrainSize("Terrain Size", Float) = 1024.0
        _TerrainHeight("Terrain Height", Float) = 100.0
    }

    Pass
    {
        Name "Forward"
        Tags { "LightMode" = "Forward" "RenderQueue" = "3000" }
        Cull Back
        ZWrite Off
        Blend SourceAlpha InverseSourceAlpha
        GLSLPROGRAM
        Vertex
        {
            #include "Prowl/TerrainHeight.glsl"
            uniform sampler2D _WaterLevelMap;
            out vec2 vTerrainUV;
            out vec2 vWorldXZ;

            void main()
            {
                vec3 local = terrainVertex(vTerrainUV);
                ivec2 last = textureSize(_WaterLevelMap, 0) - ivec2(1);
                ivec2 sampleUV = ivec2(round(clamp(vTerrainUV, vec2(0.0), vec2(1.0)) * vec2(last)));
                local.y = texelFetch(_WaterLevelMap, sampleUV, 0).r + 0.002;
                Graphite_SetPreviousPosition(local);
                vec3 relative = Graphite_ObjectToWorld(local);
                vWorldXZ = Graphite_AbsoluteFromRelative(relative).xz;
                gl_Position = Graphite_RelativeToClip(relative);
            }
        }
        Fragment
        {
            #include "Graphite/Core.glsl"
            #include "Prowl/TerrainHeight.glsl"
            uniform sampler2D _WaterMap;
            uniform sampler2D _WaterLevelMap;
            uniform vec4 _ShallowWaterColor;
            uniform vec4 _DeepWaterColor;
            uniform float _WaterVisibilityDepth;
            uniform float _RippleStrength;
            uniform float _RippleWavelength;
            uniform float _RippleSpeed;
            in vec2 vTerrainUV;
            in vec2 vWorldXZ;
            layout(location = 0) out vec4 fragColor;

            // Water values live on the height vertex grid, including both edges.
            // Interpolate those exact samples instead of point-sampling texel footprints.
            float waterCoverage(vec2 uv)
            {
                ivec2 last = textureSize(_WaterMap, 0) - ivec2(1);
                vec2 grid = clamp(uv, vec2(0.0), vec2(1.0)) * vec2(last);
                ivec2 low = ivec2(floor(grid));
                ivec2 high = min(low + ivec2(1), last);
                vec2 f = smoothstep(vec2(0.0), vec2(1.0), fract(grid));
                float a = texelFetch(_WaterMap, low, 0).r;
                float b = texelFetch(_WaterMap, ivec2(high.x, low.y), 0).r;
                float c = texelFetch(_WaterMap, ivec2(low.x, high.y), 0).r;
                float d = texelFetch(_WaterMap, high, 0).r;
                return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
            }

            // The brush writes to the height vertex grid. Soften that grid's
            // footprint on the water surface without changing the saved mask.
            float smoothWaterCoverage(vec2 uv)
            {
                vec2 stepUV = 1.0 / max(vec2(textureSize(_WaterMap, 0) - ivec2(1)), vec2(1.0));
                float center = waterCoverage(uv);
                float neighbors = waterCoverage(uv + vec2(stepUV.x, 0.0))
                                + waterCoverage(uv - vec2(stepUV.x, 0.0))
                                + waterCoverage(uv + vec2(0.0, stepUV.y))
                                + waterCoverage(uv - vec2(0.0, stepUV.y));
                return (center * 4.0 + neighbors) * 0.125;
            }

            void main()
            {
                float coverage = smoothWaterCoverage(vTerrainUV);
                if (coverage < 0.001) discard;
                ivec2 last = textureSize(_WaterLevelMap, 0) - ivec2(1);
                ivec2 sampleUV = ivec2(round(clamp(vTerrainUV, vec2(0.0), vec2(1.0)) * vec2(last)));
                float level = texelFetch(_WaterLevelMap, sampleUV, 0).r;
                float depth = max(level - terrainHeight(vTerrainUV), 0.0);
                float depthMix = smoothstep(0.0, max(_WaterVisibilityDepth, 0.01), depth);
                vec3 waterColor = mix(_ShallowWaterColor.rgb, _DeepWaterColor.rgb, depthMix);
                Graphite_ViewData view = Graphite_View();
                float time = view.time.x;
                float waveNumber = 6.2831853 / max(_RippleWavelength, 0.5);
                float phaseA = waveNumber * dot(vWorldXZ, vec2(0.87, 0.49)) - 6.2831853 * time * _RippleSpeed;
                float phaseB = waveNumber * 1.37 * dot(vWorldXZ, vec2(-0.37, 0.93)) - 6.2831853 * time * _RippleSpeed * 0.73;
                float ripple = 0.6 * sin(phaseA) + 0.4 * sin(phaseB);
                waterColor = clamp(waterColor * (1.0 + _RippleStrength * 0.16 * ripple)
                                   + vec3(0.06) * _RippleStrength * pow(max(ripple, 0.0), 3.0), 0.0, 1.0);
                float opacity = mix(0.12, 1.0, depthMix);
                float edge = smoothstep(0.001, 0.25, coverage);
                fragColor = vec4(waterColor, opacity * edge);
            }
        }
        ENDGLSL
    }
}
