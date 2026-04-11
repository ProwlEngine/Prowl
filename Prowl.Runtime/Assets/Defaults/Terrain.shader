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

#ifdef GPU_INSTANCING
            layout(location = 8) in vec4 instanceModelRow0;
            layout(location = 9) in vec4 instanceModelRow1;
            layout(location = 10) in vec4 instanceModelRow2;
            layout(location = 11) in vec4 instanceModelRow3;
            layout(location = 12) in vec4 instanceColor;
            layout(location = 13) in vec4 instanceCustomData;
#endif

            uniform sampler2D _Heightmap;
            uniform float _TerrainSize;
            uniform float _TerrainHeight;
            uniform vec3 _TerrainOffset;

			void main()
			{
#ifdef GPU_INSTANCING
                mat4 instanceModel = mat4(
                    instanceModelRow0,
                    instanceModelRow1,
                    instanceModelRow2,
                    instanceModelRow3
                );

                vec3 chunkPosition = instanceModelRow3.xyz;
                float chunkScale = length(instanceModelRow0.xyz);

                vec3 localPos = vertexPosition * chunkScale;
                vec3 worldPosition = chunkPosition + localPos;

                vec2 terrainUV = (worldPosition.xz - _TerrainOffset.xz) / _TerrainSize;
                texCoord0 = terrainUV;

                float height = texture(_Heightmap, terrainUV).r;
                worldPosition.y = worldPosition.y + (height * _TerrainHeight);

                float heightmapSize = float(textureSize(_Heightmap, 0).x);
                float texelSize = heightmapSize > 0.0 ? (1.0 / heightmapSize) : 0.001;

                float heightRight = texture(_Heightmap, terrainUV + vec2(texelSize, 0.0)).r * _TerrainHeight;
                float heightLeft = texture(_Heightmap, terrainUV - vec2(texelSize, 0.0)).r * _TerrainHeight;
                float heightUp = texture(_Heightmap, terrainUV + vec2(0.0, texelSize)).r * _TerrainHeight;
                float heightDown = texture(_Heightmap, terrainUV - vec2(0.0, texelSize)).r * _TerrainHeight;

                float worldStep = texelSize * _TerrainSize;
                float slopeX = (heightRight - heightLeft) / (worldStep * 2.0);
                float slopeZ = (heightUp - heightDown) / (worldStep * 2.0);

                worldNormal = normalize(vec3(-slopeX, 1.0, -slopeZ));
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

			layout (location = 0) out vec4 gBufferA;
			layout (location = 1) out vec4 gBufferB;
			layout (location = 2) out vec4 gBufferC;
			layout (location = 3) out vec4 gBufferD;

			in vec2 texCoord0;
            in vec3 worldPos;
            in vec3 worldNormal;

			uniform sampler2D _Splatmap;

            // Per-layer textures and settings
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

            // Brush preview
            uniform vec2 _BrushPosition;
            uniform float _BrushRadius;
            uniform float _BrushFalloff;
            uniform float _BrushVisible;

            // Unpack normal from normal map (tangent space)
            vec3 unpackNormal(vec4 packednormal)
            {
                vec3 normal;
                normal.xy = packednormal.rg * 2.0 - 1.0;
                normal.z = sqrt(max(0.0, 1.0 - dot(normal.xy, normal.xy)));
                return normal;
            }

			void main()
			{
                // Sample splatmap for blend weights (RGBA = 4 layers)
                vec4 splatWeights = texture(_Splatmap, texCoord0);

                // Normalize weights to ensure they sum to 1
                float weightSum = splatWeights.r + splatWeights.g + splatWeights.b + splatWeights.a;
                if (weightSum > 0.0)
                    splatWeights /= weightSum;

                // Sample each layer with per-layer tiling
                vec2 uv0 = texCoord0 * _Layer0Tiling;
                vec2 uv1 = texCoord0 * _Layer1Tiling;
                vec2 uv2 = texCoord0 * _Layer2Tiling;
                vec2 uv3 = texCoord0 * _Layer3Tiling;

                vec3 albedo0 = texture(_Layer0, uv0).rgb;
                vec3 albedo1 = texture(_Layer1, uv1).rgb;
                vec3 albedo2 = texture(_Layer2, uv2).rgb;
                vec3 albedo3 = texture(_Layer3, uv3).rgb;

                // Blend albedo
                vec3 albedo = albedo0 * splatWeights.r
                            + albedo1 * splatWeights.g
                            + albedo2 * splatWeights.b
                            + albedo3 * splatWeights.a;

                vec3 baseColor = gammaToLinearSpace(albedo);

                // Sample and blend normal maps
                vec3 n0 = unpackNormal(texture(_Layer0Normal, uv0));
                vec3 n1 = unpackNormal(texture(_Layer1Normal, uv1));
                vec3 n2 = unpackNormal(texture(_Layer2Normal, uv2));
                vec3 n3 = unpackNormal(texture(_Layer3Normal, uv3));

                vec3 blendedNormalTS = n0 * splatWeights.r
                                    + n1 * splatWeights.g
                                    + n2 * splatWeights.b
                                    + n3 * splatWeights.a;
                blendedNormalTS = normalize(blendedNormalTS);

                // Blend roughness and metallic
                float roughness = _Layer0Roughness * splatWeights.r
                                + _Layer1Roughness * splatWeights.g
                                + _Layer2Roughness * splatWeights.b
                                + _Layer3Roughness * splatWeights.a;

                float metallic = _Layer0Metallic * splatWeights.r
                               + _Layer1Metallic * splatWeights.g
                               + _Layer2Metallic * splatWeights.b
                               + _Layer3Metallic * splatWeights.a;

                // Construct TBN from world normal (terrain-specific: tangent along X, bitangent along Z)
                vec3 N = normalize(worldNormal);
                vec3 T = normalize(cross(N, vec3(0.0, 0.0, 1.0)));
                vec3 B = cross(N, T);
                mat3 TBN = mat3(T, B, N);

                // Apply normal map
                vec3 finalWorldNormal = normalize(TBN * blendedNormalTS);

                // Brush preview overlay
                if (_BrushVisible > 0.5 && _BrushRadius > 0.0)
                {
                    float dist = length(texCoord0 - _BrushPosition);
                    if (dist < _BrushRadius)
                    {
                        float t = dist / _BrushRadius;
                        float falloffStart = 1.0 - _BrushFalloff;
                        float alpha = 1.0 - smoothstep(falloffStart, 1.0, t);
                        alpha *= 0.3;
                        vec3 brushColor = vec3(0.2, 0.8, 0.6);
                        baseColor = mix(baseColor, brushColor, alpha);
                    }
                }

                // View-space normal
                vec3 viewNormal = normalize((PROWL_MATRIX_V * vec4(finalWorldNormal, 0.0)).xyz);

				// Output to GBuffer
				gBufferA = vec4(baseColor, 1.0);
				gBufferB = vec4(viewNormal * 0.5 + 0.5, 1.0);
				gBufferC = vec4(roughness, metallic, 0.0, 0.0);
				gBufferD = vec4(0.0);
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

#ifdef GPU_INSTANCING
            layout(location = 8) in vec4 instanceModelRow0;
            layout(location = 9) in vec4 instanceModelRow1;
            layout(location = 10) in vec4 instanceModelRow2;
            layout(location = 11) in vec4 instanceModelRow3;
            layout(location = 12) in vec4 instanceColor;
#endif

            uniform sampler2D _Heightmap;
            uniform float _TerrainSize;
            uniform float _TerrainHeight;
            uniform vec3 _TerrainOffset;

			void main()
			{
#ifdef GPU_INSTANCING
                vec3 chunkPosition = instanceModelRow3.xyz;
                float chunkScale = length(instanceModelRow0.xyz);

                vec3 localPos = vertexPosition * chunkScale;
                vec3 worldPosition = chunkPosition + localPos;

                vec2 terrainUV = (worldPosition.xz - _TerrainOffset.xz) / _TerrainSize;

                float height = texture(_Heightmap, terrainUV).r;
                worldPosition.y = worldPosition.y + (height * _TerrainHeight);

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
