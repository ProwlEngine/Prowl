Shader "Default/Water"
{
    Properties
    {
        _Heightmap("Terrain Heightmap", Texture2D) = "black"
        _WaterMap("Water Map", Texture2D) = "black"
        _HolesMap("Holes Map", Texture2D) = "white"
        _HasHoles("Has Holes", Integer) = 0
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
            uniform sampler2D _WaterMap;
            out vec2 vTerrainUV;

            void main()
            {
                vec3 local = terrainVertex(vTerrainUV);
                local.y += 0.08;
                Graphite_SetPreviousPosition(local);
                gl_Position = Graphite_RelativeToClip(Graphite_ObjectToWorld(local));
            }
        }
        Fragment
        {
            uniform sampler2D _WaterMap;
            in vec2 vTerrainUV;
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

            void main()
            {
                float coverage = waterCoverage(vTerrainUV);
                if (coverage < 0.005) discard;
                float alpha = 0.7 * smoothstep(0.005, 0.35, coverage);
                fragColor = vec4(0.06, 0.32, 0.50, alpha);
            }
        }
        ENDGLSL
    }
}
