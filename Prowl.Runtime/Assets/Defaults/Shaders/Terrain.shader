Shader "Default/Terrain"
{
    Properties
    {
        _Heightmap("Heightmap", Texture2D) = "black"
        _TerrainSize("Terrain Size", Float) = 1024.0
        _TerrainHeight("Terrain Height", Float) = 100.0
        _Splatmap0("Splatmap 0 (Layers 0-3)", Texture2D) = "white"
        _Splatmap1("Splatmap 1 (Layers 4-7)", Texture2D) = "black"
        _HolesMap("Holes Map", Texture2D) = "white"
        _HasHoles("Has Holes", Integer) = 0
        _LayerCount("Layer Count", Integer) = 4
        _Layer0("Layer 0 Albedo", Texture2D) = "white"
        _Layer0Normal("Layer 0 Normal", Texture2D) = "normal"
        _Layer0Tiling("Layer 0 Tiling", Float) = 10.0
        _Layer0Roughness("Layer 0 Roughness", Float) = 1.0
        _Layer0Metallic("Layer 0 Metallic", Float) = 0.0
        _Layer1("Layer 1 Albedo", Texture2D) = "white"
        _Layer1Normal("Layer 1 Normal", Texture2D) = "normal"
        _Layer1Tiling("Layer 1 Tiling", Float) = 10.0
        _Layer1Roughness("Layer 1 Roughness", Float) = 1.0
        _Layer1Metallic("Layer 1 Metallic", Float) = 0.0
        _Layer2("Layer 2 Albedo", Texture2D) = "white"
        _Layer2Normal("Layer 2 Normal", Texture2D) = "normal"
        _Layer2Tiling("Layer 2 Tiling", Float) = 10.0
        _Layer2Roughness("Layer 2 Roughness", Float) = 1.0
        _Layer2Metallic("Layer 2 Metallic", Float) = 0.0
        _Layer3("Layer 3 Albedo", Texture2D) = "white"
        _Layer3Normal("Layer 3 Normal", Texture2D) = "normal"
        _Layer3Tiling("Layer 3 Tiling", Float) = 10.0
        _Layer3Roughness("Layer 3 Roughness", Float) = 1.0
        _Layer3Metallic("Layer 3 Metallic", Float) = 0.0
        _Layer4("Layer 4 Albedo", Texture2D) = "white"
        _Layer4Normal("Layer 4 Normal", Texture2D) = "normal"
        _Layer4Tiling("Layer 4 Tiling", Float) = 10.0
        _Layer4Roughness("Layer 4 Roughness", Float) = 1.0
        _Layer4Metallic("Layer 4 Metallic", Float) = 0.0
        _Layer5("Layer 5 Albedo", Texture2D) = "white"
        _Layer5Normal("Layer 5 Normal", Texture2D) = "normal"
        _Layer5Tiling("Layer 5 Tiling", Float) = 10.0
        _Layer5Roughness("Layer 5 Roughness", Float) = 1.0
        _Layer5Metallic("Layer 5 Metallic", Float) = 0.0
        _Layer6("Layer 6 Albedo", Texture2D) = "white"
        _Layer6Normal("Layer 6 Normal", Texture2D) = "normal"
        _Layer6Tiling("Layer 6 Tiling", Float) = 10.0
        _Layer6Roughness("Layer 6 Roughness", Float) = 1.0
        _Layer6Metallic("Layer 6 Metallic", Float) = 0.0
        _Layer7("Layer 7 Albedo", Texture2D) = "white"
        _Layer7Normal("Layer 7 Normal", Texture2D) = "normal"
        _Layer7Tiling("Layer 7 Tiling", Float) = 10.0
        _Layer7Roughness("Layer 7 Roughness", Float) = 1.0
        _Layer7Metallic("Layer 7 Metallic", Float) = 0.0
        _BrushPosition("Brush Position", Vector2) = (0.0, 0.0)
        _BrushRadius("Brush Radius", Float) = 0.0
        _BrushFalloff("Brush Falloff", Float) = 0.5
        _BrushVisible("Brush Visible", Float) = 0.0
    }

    Pass
    {
        Name "Forward"
        Tags { "LightMode" = "Forward" }
        Cull Back
        GLSLPROGRAM
        Vertex
        {
            #include "Prowl/TerrainHeight.glsl"

            out vec2 vTerrainUV;
            out vec3 vWorldPos;
            out vec3 vNormal;

            void main()
            {
                vec3 local = terrainVertex(vTerrainUV);
                vWorldPos = Graphite_ObjectToWorld(local);
                vNormal = Graphite_ObjectToWorldNormal(terrainNormal(vTerrainUV));
                gl_Position = Graphite_RelativeToClip(vWorldPos);
            }
        }
        Fragment
        {
            #include "Prowl/Common.glsl"
            #include "Graphite/Lighting.glsl"
            #include "Graphite/Decals.glsl"
            #include "Graphite/Passes.glsl"
            #pragma variant global GRAPHITE_DEBUG OFF UNLIT ALBEDO NORMALS ROUGHNESS METALLIC AO EMISSION LIGHTING CASCADES LIGHTMAP_DENSITY
            #include "Graphite/Debug.glsl"

            #pragma variant TERRAIN_8_LAYERS

            layout(location = 0) out vec4 fragColor;

            in vec2 vTerrainUV;
            in vec3 vWorldPos;
            in vec3 vNormal;

            // Each splatmap holds four layer weights as RGBA
            uniform sampler2D _Splatmap0;
            uniform sampler2D _HolesMap;
            uniform int _HasHoles;

            uniform sampler2D _Layer0;
            uniform sampler2D _Layer0Normal;
            uniform float _Layer0Tiling;
            uniform float _Layer0Roughness;
            uniform float _Layer0Metallic;
            uniform sampler2D _Layer1;
            uniform sampler2D _Layer1Normal;
            uniform float _Layer1Tiling;
            uniform float _Layer1Roughness;
            uniform float _Layer1Metallic;
            uniform sampler2D _Layer2;
            uniform sampler2D _Layer2Normal;
            uniform float _Layer2Tiling;
            uniform float _Layer2Roughness;
            uniform float _Layer2Metallic;
            uniform sampler2D _Layer3;
            uniform sampler2D _Layer3Normal;
            uniform float _Layer3Tiling;
            uniform float _Layer3Roughness;
            uniform float _Layer3Metallic;

#ifdef TERRAIN_8_LAYERS
            uniform sampler2D _Splatmap1;
            uniform sampler2D _Layer4;
            uniform sampler2D _Layer4Normal;
            uniform float _Layer4Tiling;
            uniform float _Layer4Roughness;
            uniform float _Layer4Metallic;
            uniform sampler2D _Layer5;
            uniform sampler2D _Layer5Normal;
            uniform float _Layer5Tiling;
            uniform float _Layer5Roughness;
            uniform float _Layer5Metallic;
            uniform sampler2D _Layer6;
            uniform sampler2D _Layer6Normal;
            uniform float _Layer6Tiling;
            uniform float _Layer6Roughness;
            uniform float _Layer6Metallic;
            uniform sampler2D _Layer7;
            uniform sampler2D _Layer7Normal;
            uniform float _Layer7Tiling;
            uniform float _Layer7Roughness;
            uniform float _Layer7Metallic;
#endif

            uniform vec2 _BrushPosition;
            uniform float _BrushRadius;
            uniform float _BrushFalloff;
            uniform float _BrushVisible;

            vec3 unpackNormal(vec4 packednormal)
            {
                vec3 normal;
                normal.xy = packednormal.rg * 2.0 - 1.0;
                normal.z = sqrt(max(0.0, 1.0 - dot(normal.xy, normal.xy)));
                return normal;
            }

            #define TERRAIN_LAYER(weight, albedoTex, normalTex, tiling, layerRoughness, layerMetallic) \
                if ((weight) > 0.001) \
                { \
                    vec2 layerUV = vTerrainUV * (tiling); \
                    albedo += texture(albedoTex, layerUV).rgb * (weight); \
                    blendedNormalTS += unpackNormal(texture(normalTex, layerUV)) * (weight); \
                    roughness += (layerRoughness) * (weight); \
                    metallic += (layerMetallic) * (weight); \
                    totalWeight += (weight); \
                }

            void main()
            {
                vec3 albedo = vec3(0.0);
                vec3 blendedNormalTS = vec3(0.0);
                float roughness = 0.0;
                float metallic = 0.0;
                float totalWeight = 0.0;

                if (_HasHoles > 0 && texture(_HolesMap, vTerrainUV).r < 0.5)
                    discard;

                vec4 w0 = texture(_Splatmap0, vTerrainUV);
                TERRAIN_LAYER(w0.r, _Layer0, _Layer0Normal, _Layer0Tiling, _Layer0Roughness, _Layer0Metallic);
                TERRAIN_LAYER(w0.g, _Layer1, _Layer1Normal, _Layer1Tiling, _Layer1Roughness, _Layer1Metallic);
                TERRAIN_LAYER(w0.b, _Layer2, _Layer2Normal, _Layer2Tiling, _Layer2Roughness, _Layer2Metallic);
                TERRAIN_LAYER(w0.a, _Layer3, _Layer3Normal, _Layer3Tiling, _Layer3Roughness, _Layer3Metallic);
#ifdef TERRAIN_8_LAYERS
                vec4 w1 = texture(_Splatmap1, vTerrainUV);
                TERRAIN_LAYER(w1.r, _Layer4, _Layer4Normal, _Layer4Tiling, _Layer4Roughness, _Layer4Metallic);
                TERRAIN_LAYER(w1.g, _Layer5, _Layer5Normal, _Layer5Tiling, _Layer5Roughness, _Layer5Metallic);
                TERRAIN_LAYER(w1.b, _Layer6, _Layer6Normal, _Layer6Tiling, _Layer6Roughness, _Layer6Metallic);
                TERRAIN_LAYER(w1.a, _Layer7, _Layer7Normal, _Layer7Tiling, _Layer7Roughness, _Layer7Metallic);
#endif

                if (totalWeight > 0.0)
                {
                    albedo /= totalWeight;
                    roughness /= totalWeight;
                    metallic /= totalWeight;
                }
                roughness = clamp(roughness, PROWL_MIN_ROUGHNESS, 1.0);
                metallic = clamp(metallic, 0.0, 1.0);

                vec3 baseColor = albedo;
                vec3 N = normalize(vNormal);
                vec3 T = normalize(cross(N, vec3(0.0, 0.0, 1.0)));
                vec3 B = cross(T, N);
                vec3 worldNormal = totalWeight > 0.0 ? normalize(mat3(T, B, N) * normalize(blendedNormalTS)) : N;

                if (_BrushVisible > 0.5 && _BrushRadius > 0.0)
                {
                    float dist = length(vTerrainUV - _BrushPosition);
                    if (dist < _BrushRadius)
                    {
                        float alpha = 1.0 - smoothstep(1.0 - _BrushFalloff, 1.0, dist / _BrushRadius);
                        baseColor = mix(baseColor, vec3(0.2, 0.8, 0.6), alpha * 0.3);
                    }
                }

                float ao = 1.0;
                Graphite_ApplyDecals(vWorldPos, N, baseColor, worldNormal, roughness, metallic, ao);
                Graphite_DebugInputs(baseColor, metallic);

                vec3 viewDir = GetWorldViewDir(vWorldPos);
                vec3 lighting = CalculateForwardLighting(vWorldPos, worldNormal, N, viewDir, baseColor, metallic, roughness, ao);

                vec3 ambientLight = Graphite_IndirectDiffuse(worldNormal, vec2(0.0)) * ao;
                vec3 ambientDiffuse = ambientLight * baseColor * (1.0 - metallic);
                vec3 F0 = mix(vec3(0.04), baseColor, metallic);
                vec3 ambientSpecular = Graphite_IndirectSpecular(vWorldPos, worldNormal, viewDir, F0, roughness, ao, ambientLight);

                vec3 color = ApplyFog(ambientDiffuse + ambientSpecular + lighting, vWorldPos);
                fragColor = vec4(Graphite_DebugView(color, baseColor, worldNormal, roughness, metallic, ao, vec3(0.0), vWorldPos, vec2(0.0)), 1.0);
                Graphite_WriteGBuffer(worldNormal, roughness, metallic);
            }
        }
        ENDGLSL
    }

    Pass
    {
        Name "Prepass"
        Tags { "LightMode" = "Prepass" }
        Cull Back
        GLSLPROGRAM
        Vertex
        {
            #include "Prowl/TerrainHeight.glsl"

            out vec2 vTerrainUV;

            void main()
            {
                gl_Position = Graphite_ObjectToClip(terrainVertex(vTerrainUV));
            }
        }
        Fragment
        {
            in vec2 vTerrainUV;

            uniform sampler2D _HolesMap;
            uniform int _HasHoles;

            void main()
            {
                if (_HasHoles > 0 && texture(_HolesMap, vTerrainUV).r < 0.5)
                    discard;
            }
        }
        ENDGLSL
    }

    Pass
    {
        Name "ShadowCaster"
        Tags { "LightMode" = "ShadowCaster" }
        Cull Back
        GLSLPROGRAM
        Vertex
        {
            #include "Prowl/TerrainHeight.glsl"

            out vec2 vTerrainUV;

            void main()
            {
                gl_Position = Graphite_ObjectToClip(terrainVertex(vTerrainUV));
            }
        }
        Fragment
        {
            in vec2 vTerrainUV;

            uniform sampler2D _HolesMap;
            uniform int _HasHoles;

            void main()
            {
                if (_HasHoles > 0 && texture(_HolesMap, vTerrainUV).r < 0.5)
                    discard;
            }
        }
        ENDGLSL
    }

    Fallback "Graphite/Fallback"
}
