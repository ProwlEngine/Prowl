Shader "Default/Terrain"

Properties
{
    _Heightmap ("Heightmap", Texture2D) = "black"
    _Splatmap0 ("Splatmap 0 (Layers 0-3)", Texture2D) = "white"
    _Splatmap1 ("Splatmap 1 (Layers 4-7)", Texture2D) = "black"
    _HolesMap ("Holes Map", Texture2D) = "white"
    _HasHoles ("Has Holes", Int) = 0
    _LayerCount ("Layer Count", Int) = 4
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
    _Layer4 ("Layer 4 Albedo", Texture2D) = "white"
    _Layer4Normal ("Layer 4 Normal", Texture2D) = "normal"
    _Layer4Tiling ("Layer 4 Tiling", Float) = 10.0
    _Layer4Roughness ("Layer 4 Roughness", Float) = 1.0
    _Layer4Metallic ("Layer 4 Metallic", Float) = 0.0
    _Layer5 ("Layer 5 Albedo", Texture2D) = "white"
    _Layer5Normal ("Layer 5 Normal", Texture2D) = "normal"
    _Layer5Tiling ("Layer 5 Tiling", Float) = 10.0
    _Layer5Roughness ("Layer 5 Roughness", Float) = 1.0
    _Layer5Metallic ("Layer 5 Metallic", Float) = 0.0
    _Layer6 ("Layer 6 Albedo", Texture2D) = "white"
    _Layer6Normal ("Layer 6 Normal", Texture2D) = "normal"
    _Layer6Tiling ("Layer 6 Tiling", Float) = 10.0
    _Layer6Roughness ("Layer 6 Roughness", Float) = 1.0
    _Layer6Metallic ("Layer 6 Metallic", Float) = 0.0
    _Layer7 ("Layer 7 Albedo", Texture2D) = "white"
    _Layer7Normal ("Layer 7 Normal", Texture2D) = "normal"
    _Layer7Tiling ("Layer 7 Tiling", Float) = 10.0
    _Layer7Roughness ("Layer 7 Roughness", Float) = 1.0
    _Layer7Metallic ("Layer 7 Metallic", Float) = 0.0
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
            #include "ProwlCG"
            #include "VertexAttributes"

            out vec2 texCoord0;
            out vec3 worldPos;
            out vec3 worldNormal;


            uniform sampler2D _Heightmap;
            uniform float _TerrainSize;
            uniform float _TerrainHeight;
            uniform mat4 _TerrainWorldToLocal;
            uniform mat4 _TerrainLocalToWorld;

            // Vertex UV -> texel-center UV remap
            vec2 hmSampleUV(vec2 uv)
            {
                vec2 s = vec2(textureSize(_Heightmap, 0));
                return uv * (s - 1.0) / s + 0.5 / s;
            }

#ifdef TERRAIN_BICUBIC
            // Bicubic B-spline filtering using 4 bilinear taps (GPU-friendly)
            // Based on the "Fast Cubic Filtering" technique by Sigg & Hadwiger
            float sampleHeightBicubic(vec2 uv)
            {
                vec2 texSize = vec2(textureSize(_Heightmap, 0));
                vec2 invTexSize = 1.0 / texSize;

                // Transform to texel space
                vec2 coord = uv * texSize - 0.5;
                vec2 f = fract(coord);
                coord -= f;

                // Catmull-Rom weights from cubic B-spline
                vec2 f2 = f * f;
                vec2 f3 = f2 * f;

                // w0 = -0.5*t^3 + t^2 - 0.5*t
                // w1 =  1.5*t^3 - 2.5*t^2 + 1
                // w2 = -1.5*t^3 + 2*t^2 + 0.5*t
                // w3 =  0.5*t^3 - 0.5*t^2
                vec2 w0 = -0.5 * f3 + f2 - 0.5 * f;
                vec2 w1 =  1.5 * f3 - 2.5 * f2 + 1.0;
                vec2 w2 = -1.5 * f3 + 2.0 * f2 + 0.5 * f;
                vec2 w3 =  0.5 * f3 - 0.5 * f2;

                // Combine pairs for 4-tap bilinear trick
                vec2 s0 = w0 + w1;
                vec2 s1 = w2 + w3;
                vec2 f0 = w1 / s0;
                vec2 f1 = w3 / s1;

                // Compute the 4 sample positions (leveraging bilinear filtering)
                vec2 t0 = (coord - 0.5 + f0) * invTexSize + 0.5 * invTexSize;
                vec2 t1 = (coord + 1.5 + f1) * invTexSize + 0.5 * invTexSize;

                // 4 bilinear taps
                float h00 = texture(_Heightmap, vec2(t0.x, t0.y)).r;
                float h10 = texture(_Heightmap, vec2(t1.x, t0.y)).r;
                float h01 = texture(_Heightmap, vec2(t0.x, t1.y)).r;
                float h11 = texture(_Heightmap, vec2(t1.x, t1.y)).r;

                // Blend
                float row0 = mix(h00, h10, s1.x / (s0.x + s1.x));
                float row1 = mix(h01, h11, s1.x / (s0.x + s1.x));
                return mix(row0, row1, s1.y / (s0.y + s1.y));
            }

            float sampleHeight(vec2 uv) { return sampleHeightBicubic(uv) * _TerrainHeight; }
#else
            float sampleHeight(vec2 uv) { return texture(_Heightmap, hmSampleUV(uv)).r * _TerrainHeight; }
#endif

            void main()
            {
#ifdef GPU_INSTANCING
                mat4 instanceModel = mat4(instanceModelRow0, instanceModelRow1, instanceModelRow2, instanceModelRow3);
                vec4 worldPos4 = instanceModel * vec4(vertexPosition, 1.0);
                vec3 terrainLocal = (_TerrainWorldToLocal * worldPos4).xyz;
                vec2 terrainUV = terrainLocal.xz / _TerrainSize;
                texCoord0 = terrainUV;


                // Displace: add height along terrain-local Y, transformed back to world
                // terrainLocal with height applied
                float height = sampleHeight(terrainUV);
                vec3 displacedLocal = vec3(terrainLocal.x, height, terrainLocal.z);
                vec3 worldPosition = (_TerrainLocalToWorld * vec4(displacedLocal, 1.0)).xyz;

                // Normal via central differences
                float hmSize = float(textureSize(_Heightmap, 0).x);
                float vertStep = hmSize > 1.0 ? (1.0 / (hmSize - 1.0)) : 0.001;

                float hR = sampleHeight(terrainUV + vec2(vertStep, 0.0));
                float hL = sampleHeight(terrainUV - vec2(vertStep, 0.0));
                float hU = sampleHeight(terrainUV + vec2(0.0, vertStep));
                float hD = sampleHeight(terrainUV - vec2(0.0, vertStep));

                float wStep = vertStep * _TerrainSize;
                float slopeX = (hR - hL) / (wStep * 2.0);
                float slopeZ = (hU - hD) / (wStep * 2.0);

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
            #include "ProwlCG"
            #include "Lighting"

            layout (location = 0) out vec4 fragColor;

            in vec2 texCoord0;
            in vec3 worldPos;
            in vec3 worldNormal;

            // Splatmap textures (each holds 4 layer weights as RGBA)
            uniform sampler2D _Splatmap0;
            uniform sampler2D _HolesMap;
            uniform int _HasHoles;
            uniform int _LayerCount;

            // Layer 0-3 (splatmap 0)
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
            // Layer 4-7 (splatmap 1)
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

            void main()
            {
                // Terrain holes
                if (_HasHoles > 0 && texture(_HolesMap, texCoord0).r < 0.5)
                    discard;

                // Sample splatmap 0 (layers 0-3)
                vec4 w0 = texture(_Splatmap0, texCoord0);

                // Accumulate albedo, normal, roughness, metallic from layers 0-3
                vec3 albedo = vec3(0.0);
                vec3 blendedNormalTS = vec3(0.0);
                float roughness = 0.0;
                float metallic = 0.0;
                float totalWeight = 0.0;

                // Layer 0
                if (w0.r > 0.001) {
                    vec2 uv = texCoord0 * _Layer0Tiling;
                    albedo += texture(_Layer0, uv).rgb * w0.r;
                    blendedNormalTS += unpackNormal(texture(_Layer0Normal, uv)) * w0.r;
                    roughness += _Layer0Roughness * w0.r;
                    metallic += _Layer0Metallic * w0.r;
                    totalWeight += w0.r;
                }
                // Layer 1
                if (w0.g > 0.001) {
                    vec2 uv = texCoord0 * _Layer1Tiling;
                    albedo += texture(_Layer1, uv).rgb * w0.g;
                    blendedNormalTS += unpackNormal(texture(_Layer1Normal, uv)) * w0.g;
                    roughness += _Layer1Roughness * w0.g;
                    metallic += _Layer1Metallic * w0.g;
                    totalWeight += w0.g;
                }
                // Layer 2
                if (w0.b > 0.001) {
                    vec2 uv = texCoord0 * _Layer2Tiling;
                    albedo += texture(_Layer2, uv).rgb * w0.b;
                    blendedNormalTS += unpackNormal(texture(_Layer2Normal, uv)) * w0.b;
                    roughness += _Layer2Roughness * w0.b;
                    metallic += _Layer2Metallic * w0.b;
                    totalWeight += w0.b;
                }
                // Layer 3
                if (w0.a > 0.001) {
                    vec2 uv = texCoord0 * _Layer3Tiling;
                    albedo += texture(_Layer3, uv).rgb * w0.a;
                    blendedNormalTS += unpackNormal(texture(_Layer3Normal, uv)) * w0.a;
                    roughness += _Layer3Roughness * w0.a;
                    metallic += _Layer3Metallic * w0.a;
                    totalWeight += w0.a;
                }

#ifdef TERRAIN_8_LAYERS
                // Sample splatmap 1 (layers 4-7)
                vec4 w1 = texture(_Splatmap1, texCoord0);

                if (w1.r > 0.001) {
                    vec2 uv = texCoord0 * _Layer4Tiling;
                    albedo += texture(_Layer4, uv).rgb * w1.r;
                    blendedNormalTS += unpackNormal(texture(_Layer4Normal, uv)) * w1.r;
                    roughness += _Layer4Roughness * w1.r;
                    metallic += _Layer4Metallic * w1.r;
                    totalWeight += w1.r;
                }
                if (w1.g > 0.001) {
                    vec2 uv = texCoord0 * _Layer5Tiling;
                    albedo += texture(_Layer5, uv).rgb * w1.g;
                    blendedNormalTS += unpackNormal(texture(_Layer5Normal, uv)) * w1.g;
                    roughness += _Layer5Roughness * w1.g;
                    metallic += _Layer5Metallic * w1.g;
                    totalWeight += w1.g;
                }
                if (w1.b > 0.001) {
                    vec2 uv = texCoord0 * _Layer6Tiling;
                    albedo += texture(_Layer6, uv).rgb * w1.b;
                    blendedNormalTS += unpackNormal(texture(_Layer6Normal, uv)) * w1.b;
                    roughness += _Layer6Roughness * w1.b;
                    metallic += _Layer6Metallic * w1.b;
                    totalWeight += w1.b;
                }
                if (w1.a > 0.001) {
                    vec2 uv = texCoord0 * _Layer7Tiling;
                    albedo += texture(_Layer7, uv).rgb * w1.a;
                    blendedNormalTS += unpackNormal(texture(_Layer7Normal, uv)) * w1.a;
                    roughness += _Layer7Roughness * w1.a;
                    metallic += _Layer7Metallic * w1.a;
                    totalWeight += w1.a;
                }
#endif

                // Normalize
                if (totalWeight > 0.0) {
                    albedo /= totalWeight;
                    roughness /= totalWeight;
                    metallic /= totalWeight;
                }

                vec3 baseColor = gammaToLinearSpace(albedo);
                blendedNormalTS = normalize(blendedNormalTS);

                vec3 N = normalize(worldNormal);
                vec3 T = normalize(cross(N, vec3(0.0, 0.0, 1.0)));
                vec3 B = cross(T, N);
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
                // Energy-conserved ambient (metals have no diffuse ambient)
                vec3 diffuseColor = baseColor * (1.0 - metallic);
                vec3 ambient = CalculateAmbient(finalWorldNormal) * diffuseColor * _AmbientStrength;
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
            #include "ProwlCG"
            #include "VertexAttributes"

            out vec3 worldPos;
            out vec2 texCoord0;

            uniform sampler2D _Heightmap;
            uniform float _TerrainSize;
            uniform float _TerrainHeight;
            uniform mat4 _TerrainWorldToLocal;
            uniform mat4 _TerrainLocalToWorld;

            vec2 hmSampleUV(vec2 uv)
            {
                vec2 s = vec2(textureSize(_Heightmap, 0));
                return uv * (s - 1.0) / s + 0.5 / s;
            }

#ifdef TERRAIN_BICUBIC
            float sampleHeightBicubic(vec2 uv)
            {
                vec2 texSize = vec2(textureSize(_Heightmap, 0));
                vec2 invTexSize = 1.0 / texSize;
                vec2 coord = uv * texSize - 0.5;
                vec2 f = fract(coord);
                coord -= f;
                vec2 f2 = f * f; vec2 f3 = f2 * f;
                vec2 w0 = -0.5*f3 + f2 - 0.5*f;
                vec2 w1 = 1.5*f3 - 2.5*f2 + 1.0;
                vec2 w2 = -1.5*f3 + 2.0*f2 + 0.5*f;
                vec2 w3 = 0.5*f3 - 0.5*f2;
                vec2 s0 = w0+w1; vec2 s1 = w2+w3;
                vec2 f0 = w1/s0; vec2 f1 = w3/s1;
                vec2 t0 = (coord-0.5+f0)*invTexSize + 0.5*invTexSize;
                vec2 t1 = (coord+1.5+f1)*invTexSize + 0.5*invTexSize;
                float h00=texture(_Heightmap,vec2(t0.x,t0.y)).r;
                float h10=texture(_Heightmap,vec2(t1.x,t0.y)).r;
                float h01=texture(_Heightmap,vec2(t0.x,t1.y)).r;
                float h11=texture(_Heightmap,vec2(t1.x,t1.y)).r;
                float row0=mix(h00,h10,s1.x/(s0.x+s1.x));
                float row1=mix(h01,h11,s1.x/(s0.x+s1.x));
                return mix(row0,row1,s1.y/(s0.y+s1.y));
            }
            float sampleHeight(vec2 uv) { return sampleHeightBicubic(uv) * _TerrainHeight; }
#else
            float sampleHeight(vec2 uv) { return texture(_Heightmap, hmSampleUV(uv)).r * _TerrainHeight; }
#endif

            void main()
            {
#ifdef GPU_INSTANCING
                mat4 instanceModel = mat4(instanceModelRow0, instanceModelRow1, instanceModelRow2, instanceModelRow3);
                vec4 worldPos4 = instanceModel * vec4(vertexPosition, 1.0);
                vec3 terrainLocal = (_TerrainWorldToLocal * worldPos4).xyz;
                vec2 terrainUV = terrainLocal.xz / _TerrainSize;
                texCoord0 = terrainUV;

                float height = sampleHeight(terrainUV);
                vec3 displacedLocal = vec3(terrainLocal.x, height, terrainLocal.z);
                vec3 worldPosition = (_TerrainLocalToWorld * vec4(displacedLocal, 1.0)).xyz;

                worldPos = worldPosition;
                gl_Position = PROWL_MATRIX_VP * vec4(worldPosition, 1.0);
#else
                gl_Position = PROWL_MATRIX_MVP * vec4(vertexPosition, 1.0);
                worldPos = (PROWL_MATRIX_M * vec4(vertexPosition, 1.0)).xyz;
                texCoord0 = vertexTexCoord0;
#endif
            }
        }

        Fragment
        {
            #include "ProwlCG"

            in vec3 worldPos;
            in vec2 texCoord0;

            uniform sampler2D _HolesMap;
            uniform int _HasHoles;

            void main()
            {
                if (_HasHoles > 0 && texture(_HolesMap, texCoord0).r < 0.5)
                    discard;
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
            #include "ProwlCG"
            #include "VertexAttributes"

            out vec3 worldNormal;
            out vec2 texCoord0;

            uniform sampler2D _Heightmap;
            uniform float _TerrainSize;
            uniform float _TerrainHeight;
            uniform mat4 _TerrainWorldToLocal;
            uniform mat4 _TerrainLocalToWorld;

            vec2 hmSampleUV(vec2 uv)
            {
                vec2 s = vec2(textureSize(_Heightmap, 0));
                return uv * (s - 1.0) / s + 0.5 / s;
            }

#ifdef TERRAIN_BICUBIC
            float sampleHeightBicubic(vec2 uv)
            {
                vec2 texSize = vec2(textureSize(_Heightmap, 0));
                vec2 invTexSize = 1.0 / texSize;
                vec2 coord = uv * texSize - 0.5;
                vec2 f = fract(coord);
                coord -= f;
                vec2 f2 = f * f; vec2 f3 = f2 * f;
                vec2 w0 = -0.5*f3 + f2 - 0.5*f;
                vec2 w1 = 1.5*f3 - 2.5*f2 + 1.0;
                vec2 w2 = -1.5*f3 + 2.0*f2 + 0.5*f;
                vec2 w3 = 0.5*f3 - 0.5*f2;
                vec2 s0 = w0+w1; vec2 s1 = w2+w3;
                vec2 f0 = w1/s0; vec2 f1 = w3/s1;
                vec2 t0 = (coord-0.5+f0)*invTexSize + 0.5*invTexSize;
                vec2 t1 = (coord+1.5+f1)*invTexSize + 0.5*invTexSize;
                float h00=texture(_Heightmap,vec2(t0.x,t0.y)).r;
                float h10=texture(_Heightmap,vec2(t1.x,t0.y)).r;
                float h01=texture(_Heightmap,vec2(t0.x,t1.y)).r;
                float h11=texture(_Heightmap,vec2(t1.x,t1.y)).r;
                float row0=mix(h00,h10,s1.x/(s0.x+s1.x));
                float row1=mix(h01,h11,s1.x/(s0.x+s1.x));
                return mix(row0,row1,s1.y/(s0.y+s1.y));
            }
            float sampleHeight(vec2 uv) { return sampleHeightBicubic(uv) * _TerrainHeight; }
#else
            float sampleHeight(vec2 uv) { return texture(_Heightmap, hmSampleUV(uv)).r * _TerrainHeight; }
#endif

            void main()
            {
#ifdef GPU_INSTANCING
                mat4 instanceModel = mat4(instanceModelRow0, instanceModelRow1, instanceModelRow2, instanceModelRow3);
                vec4 worldPos4 = instanceModel * vec4(vertexPosition, 1.0);
                vec3 terrainLocal = (_TerrainWorldToLocal * worldPos4).xyz;
                vec2 terrainUV = terrainLocal.xz / _TerrainSize;
                texCoord0 = terrainUV;

                float height = sampleHeight(terrainUV);
                vec3 displacedLocal = vec3(terrainLocal.x, height, terrainLocal.z);
                vec3 worldPosition = (_TerrainLocalToWorld * vec4(displacedLocal, 1.0)).xyz;

                float hmSize = float(textureSize(_Heightmap, 0).x);
                float vertStep = hmSize > 1.0 ? (1.0 / (hmSize - 1.0)) : 0.001;
                float hR = sampleHeight(terrainUV + vec2(vertStep, 0.0));
                float hL = sampleHeight(terrainUV - vec2(vertStep, 0.0));
                float hU = sampleHeight(terrainUV + vec2(0.0, vertStep));
                float hD = sampleHeight(terrainUV - vec2(0.0, vertStep));
                float wStep = vertStep * _TerrainSize;
                float slopeX = (hR - hL) / (wStep * 2.0);
                float slopeZ = (hU - hD) / (wStep * 2.0);
                vec3 localNormal = normalize(vec3(-slopeX, 1.0, -slopeZ));
                worldNormal = normalize((_TerrainLocalToWorld * vec4(localNormal, 0.0)).xyz);

                gl_Position = PROWL_MATRIX_VP * vec4(worldPosition, 1.0);
#else
                gl_Position = PROWL_MATRIX_MVP * vec4(vertexPosition, 1.0);
                worldNormal = normalize((PROWL_MATRIX_M * vec4(0.0, 1.0, 0.0, 0.0)).xyz);
                texCoord0 = vertexTexCoord0;
#endif
            }
        }

        Fragment
        {
            #include "ProwlCG"

            layout (location = 0) out vec4 normalOut;
            in vec3 worldNormal;
            in vec2 texCoord0;

            uniform sampler2D _HolesMap;
            uniform int _HasHoles;

            void main()
            {
                if (_HasHoles > 0 && texture(_HolesMap, texCoord0).r < 0.5)
                    discard;
                normalOut = EncodeViewNormal(worldNormal);
            }
        }
    ENDGLSL
}
