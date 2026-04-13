Shader "Default/Terrain"

Properties
{
    _Heightmap ("Heightmap", Texture2D) = "black"
    _Splatmap ("Splatmap", Texture2D) = "white"
    _Layer0 ("Layer 0 Albedo", Texture2D) = "white"
    _Layer0Normal ("Layer 0 Normal", Texture2D) = "normal"
    _Layer0Tiling ("Layer 0 Tiling", Float) = 10.0
    _Layer0Roughness ("Layer 0 Roughness", Float) = 1.0
    _Layer0Metallic ("Layer 0 Metallic", Float) = 0.0
    _Layer1 ("Layer 1 Albedo", Texture2D) = "white"
    _Layer1Normal ("Layer 1 Normal", Texture2D) = "normal"
    _Layer1Tiling ("Layer 1 Tiling", Float) = 10.0
    _Layer1Roughness ("Layer 1 Roughness", Float) = 1.0
    _Layer1Metallic ("Layer 1 Metallic", Float) = 0.0
    _Layer2 ("Layer 2 Albedo", Texture2D) = "white"
    _Layer2Normal ("Layer 2 Normal", Texture2D) = "normal"
    _Layer2Tiling ("Layer 2 Tiling", Float) = 10.0
    _Layer2Roughness ("Layer 2 Roughness", Float) = 1.0
    _Layer2Metallic ("Layer 2 Metallic", Float) = 0.0
    _Layer3 ("Layer 3 Albedo", Texture2D) = "white"
    _Layer3Normal ("Layer 3 Normal", Texture2D) = "normal"
    _Layer3Tiling ("Layer 3 Tiling", Float) = 10.0
    _Layer3Roughness ("Layer 3 Roughness", Float) = 1.0
    _Layer3Metallic ("Layer 3 Metallic", Float) = 0.0
    _TerrainSize ("Terrain Size", Float) = 1024.0
    _TerrainHeight ("Terrain Height", Float) = 100.0
    _BrushPosition ("Brush Position", Vector2) = (0.0, 0.0)
    _BrushRadius ("Brush Radius", Float) = 0.0
    _BrushFalloff ("Brush Falloff", Float) = 0.5
    _BrushVisible ("Brush Visible", Float) = 0
}

Pass "Terrain"
{
    Tags { "RenderOrder" = "Opaque" }
    Cull Back
    ZWrite On
    Blend Off

    GLSLPROGRAM

        Vertex
        {
            #include "Fragment"
            #include "VertexAttributes"

            out vec2 texCoord0;
            out vec3 worldPos;
            out vec3 worldNormal;


            uniform sampler2D _Heightmap;
            uniform float _TerrainSize;
            uniform float _TerrainHeight;
            uniform mat4 _TerrainWorldToLocal;
            uniform mat4 _TerrainLocalToWorld;

            void main()
            {
#ifdef GPU_INSTANCING
                // Instance matrix = terrainWorldMatrix * localChunkMatrix
                mat4 instanceModel = mat4(instanceModelRow0, instanceModelRow1, instanceModelRow2, instanceModelRow3);

                // Transform mesh vertex (0-1 range) to world via instance matrix
                vec4 worldPos4 = instanceModel * vec4(vertexPosition, 1.0);

                // Convert to terrain-local space for UV and heightmap sampling
                vec3 terrainLocal = (_TerrainWorldToLocal * worldPos4).xyz;
                vec2 terrainUV = terrainLocal.xz / _TerrainSize;
                texCoord0 = terrainUV;

                // Sample height in terrain-local Y
                float height = texture(_Heightmap, terrainUV).r * _TerrainHeight;

                // Displace: add height along terrain-local Y, transformed back to world
                // terrainLocal with height applied
                vec3 displacedLocal = vec3(terrainLocal.x, height, terrainLocal.z);
                // Transform back to world: inverse of worldToLocal = localToWorld
                // We can use the instance model's terrain transform portion
                // Since instanceModel = terrainToWorld * chunkLocal, and we need terrainToWorld,
                // we can reconstruct: worldPos = _TerrainLocalToWorld * displacedLocal
                vec3 worldPosition = (_TerrainLocalToWorld * vec4(displacedLocal, 1.0)).xyz;

                // Normal via central differences in terrain-local space
                float hmSize = float(textureSize(_Heightmap, 0).x);
                float texelSize = hmSize > 0.0 ? (1.0 / hmSize) : 0.001;

                float hR = texture(_Heightmap, terrainUV + vec2(texelSize, 0.0)).r * _TerrainHeight;
                float hL = texture(_Heightmap, terrainUV - vec2(texelSize, 0.0)).r * _TerrainHeight;
                float hU = texture(_Heightmap, terrainUV + vec2(0.0, texelSize)).r * _TerrainHeight;
                float hD = texture(_Heightmap, terrainUV - vec2(0.0, texelSize)).r * _TerrainHeight;

                float wStep = texelSize * _TerrainSize;
                float slopeX = (hR - hL) / (wStep * 2.0);
                float slopeZ = (hU - hD) / (wStep * 2.0);

                // Local normal -> world normal via terrain rotation
                vec3 localNormal = normalize(vec3(-slopeX, 1.0, -slopeZ));
                worldNormal = normalize((_TerrainLocalToWorld * vec4(localNormal, 0.0)).xyz);

                worldPos = worldPosition;
                gl_Position = PROWL_MATRIX_VP * vec4(worldPosition, 1.0);
#else
                gl_Position = PROWL_MATRIX_MVP * vec4(vertexPosition, 1.0);
                texCoord0 = vertexTexCoord0;
                worldPos = (PROWL_MATRIX_M * vec4(vertexPosition, 1.0)).xyz;
                worldNormal = normalize((PROWL_MATRIX_M * vec4(0.0, 1.0, 0.0, 0.0)).xyz);
#endif
            }
        }

        Fragment
        {
            #include "Fragment"
            #include "Lighting"

            layout (location = 0) out vec4 fragColor;

            in vec2 texCoord0;
            in vec3 worldPos;
            in vec3 worldNormal;

            uniform sampler2D _Splatmap;

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

            void main()
            {
                vec4 splatWeights = texture(_Splatmap, texCoord0);
                float weightSum = splatWeights.r + splatWeights.g + splatWeights.b + splatWeights.a;
                if (weightSum > 0.0) splatWeights /= weightSum;

                vec2 uv0 = texCoord0 * _Layer0Tiling;
                vec2 uv1 = texCoord0 * _Layer1Tiling;
                vec2 uv2 = texCoord0 * _Layer2Tiling;
                vec2 uv3 = texCoord0 * _Layer3Tiling;

                vec3 albedo = texture(_Layer0, uv0).rgb * splatWeights.r
                            + texture(_Layer1, uv1).rgb * splatWeights.g
                            + texture(_Layer2, uv2).rgb * splatWeights.b
                            + texture(_Layer3, uv3).rgb * splatWeights.a;

                vec3 baseColor = gammaToLinearSpace(albedo);

                vec3 blendedNormalTS = normalize(
                    unpackNormal(texture(_Layer0Normal, uv0)) * splatWeights.r +
                    unpackNormal(texture(_Layer1Normal, uv1)) * splatWeights.g +
                    unpackNormal(texture(_Layer2Normal, uv2)) * splatWeights.b +
                    unpackNormal(texture(_Layer3Normal, uv3)) * splatWeights.a);

                float roughness = _Layer0Roughness * splatWeights.r + _Layer1Roughness * splatWeights.g
                                + _Layer2Roughness * splatWeights.b + _Layer3Roughness * splatWeights.a;
                float metallic = _Layer0Metallic * splatWeights.r + _Layer1Metallic * splatWeights.g
                               + _Layer2Metallic * splatWeights.b + _Layer3Metallic * splatWeights.a;

                vec3 N = normalize(worldNormal);
                vec3 T = normalize(cross(N, vec3(0.0, 0.0, 1.0)));
                vec3 B = cross(N, T);
                vec3 finalWorldNormal = normalize(mat3(T, B, N) * blendedNormalTS);

                // Brush visualization
                if (_BrushVisible > 0.5 && _BrushRadius > 0.0)
                {
                    float dist = length(texCoord0 - _BrushPosition);
                    if (dist < _BrushRadius)
                    {
                        float t = dist / _BrushRadius;
                        float alpha = 1.0 - smoothstep(1.0 - _BrushFalloff, 1.0, t);
                        baseColor = mix(baseColor, vec3(0.2, 0.8, 0.6), alpha * 0.3);
                    }
                }

                // Forward lighting
                vec3 viewDir = normalize(_WorldSpaceCameraPos.xyz - worldPos);
                vec3 lighting = CalculateForwardLighting(worldPos, finalWorldNormal, viewDir,
                                                         baseColor, metallic, roughness, 1.0);
                vec3 ambient = CalculateAmbient(finalWorldNormal) * baseColor * _AmbientStrength;
                vec3 color = ambient + lighting;
                color = ApplyFog(color, worldPos);

                fragColor = vec4(color, 1.0);
            }
        }
    ENDGLSL
}

Pass "TerrainShadow"
{
    Tags { "LightMode" = "ShadowCaster" }
    Cull Back

    GLSLPROGRAM

        Vertex
        {
            #include "Fragment"
            #include "VertexAttributes"

            out vec3 worldPos;


            uniform sampler2D _Heightmap;
            uniform float _TerrainSize;
            uniform float _TerrainHeight;
            uniform mat4 _TerrainWorldToLocal;
            uniform mat4 _TerrainLocalToWorld;

            void main()
            {
#ifdef GPU_INSTANCING
                mat4 instanceModel = mat4(instanceModelRow0, instanceModelRow1, instanceModelRow2, instanceModelRow3);
                vec4 worldPos4 = instanceModel * vec4(vertexPosition, 1.0);
                vec3 terrainLocal = (_TerrainWorldToLocal * worldPos4).xyz;
                vec2 terrainUV = terrainLocal.xz / _TerrainSize;

                float height = texture(_Heightmap, terrainUV).r * _TerrainHeight;
                vec3 displacedLocal = vec3(terrainLocal.x, height, terrainLocal.z);
                vec3 worldPosition = (_TerrainLocalToWorld * vec4(displacedLocal, 1.0)).xyz;

                worldPos = worldPosition;
                gl_Position = PROWL_MATRIX_VP * vec4(worldPosition, 1.0);
#else
                gl_Position = PROWL_MATRIX_MVP * vec4(vertexPosition, 1.0);
                worldPos = (PROWL_MATRIX_M * vec4(vertexPosition, 1.0)).xyz;
#endif
            }
        }

        Fragment
        {
            #include "Fragment"

            in vec3 worldPos;

            void main()
            {
                gl_FragDepth = gl_FragCoord.z;
            }
        }
    ENDGLSL
}

Pass "TerrainDepthNormals"
{
    Tags { "LightMode" = "DepthNormals" }
    Cull Back

    GLSLPROGRAM

        Vertex
        {
            #include "Fragment"
            #include "VertexAttributes"

            out vec3 worldNormal;


            uniform sampler2D _Heightmap;
            uniform float _TerrainSize;
            uniform float _TerrainHeight;
            uniform mat4 _TerrainWorldToLocal;
            uniform mat4 _TerrainLocalToWorld;

            void main()
            {
#ifdef GPU_INSTANCING
                mat4 instanceModel = mat4(instanceModelRow0, instanceModelRow1, instanceModelRow2, instanceModelRow3);
                vec4 worldPos4 = instanceModel * vec4(vertexPosition, 1.0);
                vec3 terrainLocal = (_TerrainWorldToLocal * worldPos4).xyz;
                vec2 terrainUV = terrainLocal.xz / _TerrainSize;

                float height = texture(_Heightmap, terrainUV).r * _TerrainHeight;
                vec3 displacedLocal = vec3(terrainLocal.x, height, terrainLocal.z);
                vec3 worldPosition = (_TerrainLocalToWorld * vec4(displacedLocal, 1.0)).xyz;

                float hmSize = float(textureSize(_Heightmap, 0).x);
                float texelSize = hmSize > 0.0 ? (1.0 / hmSize) : 0.001;
                float hR = texture(_Heightmap, terrainUV + vec2(texelSize, 0.0)).r * _TerrainHeight;
                float hL = texture(_Heightmap, terrainUV - vec2(texelSize, 0.0)).r * _TerrainHeight;
                float hU = texture(_Heightmap, terrainUV + vec2(0.0, texelSize)).r * _TerrainHeight;
                float hD = texture(_Heightmap, terrainUV - vec2(0.0, texelSize)).r * _TerrainHeight;
                float wStep = texelSize * _TerrainSize;
                float slopeX = (hR - hL) / (wStep * 2.0);
                float slopeZ = (hU - hD) / (wStep * 2.0);
                vec3 localNormal = normalize(vec3(-slopeX, 1.0, -slopeZ));
                worldNormal = normalize((_TerrainLocalToWorld * vec4(localNormal, 0.0)).xyz);

                gl_Position = PROWL_MATRIX_VP * vec4(worldPosition, 1.0);
#else
                gl_Position = PROWL_MATRIX_MVP * vec4(vertexPosition, 1.0);
                worldNormal = normalize((PROWL_MATRIX_M * vec4(0.0, 1.0, 0.0, 0.0)).xyz);
#endif
            }
        }

        Fragment
        {
            #include "Fragment"

            layout (location = 0) out vec4 normalOut;
            in vec3 worldNormal;

            void main()
            {
                normalOut = EncodeViewNormal(worldNormal);
            }
        }
    ENDGLSL
}
