Shader "Default/Grass"
{
    Properties
    {
        _MainTex("Grass Texture", Texture2D) = "white"
        _AlphaCutoff("Alpha Cutoff", Float) = 0.5
        _WindStrength("Wind Strength", Float) = 0.3
        _WindSpeed("Wind Speed", Float) = 1.5
        _Billboard("Billboard", Float) = 1.0
        _AlignToNormal("Align To Normal", Float) = 0.0
        _Translucency("Translucency", Float) = 15.0
        _ScatterPower("Scattering Power", Float) = 0.0
        _ScatterDistortion("Scattering Distortion", Float) = 0.5
        _ScatterScale("Scattering Scale", Float) = 1.0

        _Heightmap("Heightmap", Texture2D) = "black"
        _TerrainSize("Terrain Size", Float) = 1024.0
        _TerrainHeight("Terrain Height", Float) = 100.0
        _HolesMap("Holes Map", Texture2D) = "white"
        _HasHoles("Has Holes", Integer) = 0
        _DetailMap("Detail Map", Texture2D) = "black"
        _DetailChannel("Detail Channel", Integer) = 0
        _ScatterCellSize("Scatter Cell Size", Float) = 0.5
        _ScatterMaxLevel("Scatter Max Level", Integer) = 0

        _ProtoSize("Prototype Size", Vector) = (1.0, 2.0, 1.0, 2.0)
        _ProtoHealthyColor("Healthy Color", Color) = (0.26, 0.97, 0.16, 1.0)
        _ProtoDryColor("Dry Color", Color) = (0.80, 0.73, 0.10, 1.0)
        _ProtoNoiseSpread("Noise Spread", Float) = 0.1
        _ProtoBendFactor("Bend Factor", Float) = 0.5

        _WindZoneCount("Wind Zone Count", Integer) = 0
        _WindZoneSphere0("Wind Zone 0 Sphere", Vector) = (0.0, 0.0, 0.0, 0.0)
        _WindZoneSphere1("Wind Zone 1 Sphere", Vector) = (0.0, 0.0, 0.0, 0.0)
        _WindZoneSphere2("Wind Zone 2 Sphere", Vector) = (0.0, 0.0, 0.0, 0.0)
        _WindZoneSphere3("Wind Zone 3 Sphere", Vector) = (0.0, 0.0, 0.0, 0.0)
        _WindZoneParams0("Wind Zone 0 Params", Vector) = (0.0, 0.0, 0.0, 0.0)
        _WindZoneParams1("Wind Zone 1 Params", Vector) = (0.0, 0.0, 0.0, 0.0)
        _WindZoneParams2("Wind Zone 2 Params", Vector) = (0.0, 0.0, 0.0, 0.0)
        _WindZoneParams3("Wind Zone 3 Params", Vector) = (0.0, 0.0, 0.0, 0.0)
    }

    Pass
    {
        Name "Forward"
        Tags { "LightMode" = "Forward" }
        Cull Off
        GLSLPROGRAM
        Vertex
        {
            #include "Prowl/TerrainScatter.glsl"

            out vec2 vUV;
            out vec4 vColor;
            out vec3 vWorldPos;
            out vec3 vNormal;

            void main()
            {
                vUV = vertexUV0;
                if (!grassVertex(vWorldPos, vNormal, vColor))
                {
                    // Every vertex of an absent blade lands on one point, so it rasterizes nothing
                    gl_Position = vec4(2.0, 2.0, 2.0, 1.0);
                    return;
                }
                gl_Position = Graphite_RelativeToClip(vWorldPos);
            }
        }
        Fragment
        {
            #include "Prowl/Common.glsl"
            #include "Graphite/Lighting.glsl"
            #include "Graphite/Passes.glsl"
            #include "Graphite/Debug.glsl"

            layout(location = 0) out vec4 fragColor;

            in vec2 vUV;
            in vec4 vColor;
            in vec3 vWorldPos;
            in vec3 vNormal;

            uniform sampler2D _MainTex;
            uniform float _AlphaCutoff;
            uniform float _Translucency;
            uniform float _ScatterPower;
            uniform float _ScatterDistortion;
            uniform float _ScatterScale;

            void main()
            {
                vec4 finalColor = texture(_MainTex, vUV) * vColor;
                if (finalColor.a < _AlphaCutoff)
                    discard;

                vec3 baseColor = texture(_MainTex, vUV).rgb * gammaToLinearSpace(vColor.rgb);
                // Grass is double sided, back faces shade with the flipped normal
                vec3 normal = normalize(vNormal) * (gl_FrontFacing ? 1.0 : -1.0);
                float metallic = 0.0;
                Graphite_DebugInputs(baseColor, metallic);

                vec3 viewDir = GetWorldViewDir(vWorldPos);
                vec3 lighting = CalculateForwardLighting(vWorldPos, normal, viewDir, baseColor, 0.0, 0.9, 1.0,
                                                         _Translucency, _ScatterPower, _ScatterDistortion, _ScatterScale);
                vec3 ambient = Graphite_IndirectDiffuse(normal, vec2(0.0)) * baseColor;

                vec3 color = ApplyFog(ambient + lighting, vWorldPos);
                fragColor = vec4(Graphite_DebugView(color, baseColor, normal, 0.9, 0.0, 1.0, vec3(0.0), vWorldPos, vec2(0.0)), 1.0);
                Graphite_WriteGBuffer(normal, 0.9, 0.0);
            }
        }
        ENDGLSL
    }

    Pass
    {
        Name "Prepass"
        Tags { "LightMode" = "Prepass" }
        Cull Off
        GLSLPROGRAM
        Vertex
        {
            #include "Prowl/TerrainScatter.glsl"

            out vec2 vUV;

            void main()
            {
                vUV = vertexUV0;
                vec3 world, normal;
                vec4 color;
                gl_Position = grassVertex(world, normal, color) ? Graphite_RelativeToClip(world) : vec4(2.0, 2.0, 2.0, 1.0);
            }
        }
        Fragment
        {
            in vec2 vUV;

            uniform sampler2D _MainTex;
            uniform float _AlphaCutoff;

            void main()
            {
                if (texture(_MainTex, vUV).a < _AlphaCutoff)
                    discard;
            }
        }
        ENDGLSL
    }

    Pass
    {
        Name "ShadowCaster"
        Tags { "LightMode" = "ShadowCaster" }
        Cull Off
        GLSLPROGRAM
        Vertex
        {
            #include "Prowl/TerrainScatter.glsl"

            out vec2 vUV;

            void main()
            {
                vUV = vertexUV0;
                vec3 world, normal;
                vec4 color;
                gl_Position = grassVertex(world, normal, color) ? Graphite_RelativeToClip(world) : vec4(2.0, 2.0, 2.0, 1.0);
            }
        }
        Fragment
        {
            in vec2 vUV;

            uniform sampler2D _MainTex;
            uniform float _AlphaCutoff;

            void main()
            {
                if (texture(_MainTex, vUV).a < _AlphaCutoff)
                    discard;
            }
        }
        ENDGLSL
    }
}
