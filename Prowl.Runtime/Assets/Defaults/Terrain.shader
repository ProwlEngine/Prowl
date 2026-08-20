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
    _HeightBlendSharpness ("Height Blend Sharpness", Range(0.02, 1.0)) = 0.25
    _NormalHeightInfluence ("Normal Height Influence", Range(0.0, 1.0)) = 0.6
    _FarTilingScale ("Far Tiling Scale", Float) = 8.0
    _FarTilingStart ("Far Tiling Start Distance", Float) = 35.0
    _FarTilingFade ("Far Tiling Fade Distance", Float) = 300.0
    _FarTilingStrength ("Far Tiling Strength", Range(0.0, 1.0)) = 1.0
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

                // Transform to sample grid space (heights are a vertex grid)
                vec2 coord = uv * (texSize - 1.0);
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

                // Combine pairs for 4-tap bilinear trick. Both sums reach zero on sample-aligned
                // coords, so they are floored to keep the tap positions finite.
                vec2 s0 = max(w0 + w1, vec2(1e-5));
                vec2 s1 = max(w2 + w3, vec2(1e-5));
                vec2 f0 = w1 / s0;
                vec2 f1 = w3 / s1;

                // Texel-center UV of the two bilinear taps per axis
                vec2 t0 = (coord - 0.5 + f0) * invTexSize;
                vec2 t1 = (coord + 1.5 + f1) * invTexSize;

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

            uniform float _HeightBlendSharpness;
            uniform float _NormalHeightInfluence;
            uniform float _FarTilingScale;
            uniform float _FarTilingStart;
            uniform float _FarTilingFade;
            uniform float _FarTilingStrength;

#ifdef TERRAIN_8_LAYERS
#define TERRAIN_LAYERS 8
#else
#define TERRAIN_LAYERS 4
#endif

            vec3 unpackNormal(vec4 packednormal)
            {
                vec3 normal;
                normal.xy = packednormal.rg * 2.0 - 1.0;
                normal.z = sqrt(max(0.0, 1.0 - dot(normal.xy, normal.xy)));
                return normal;
            }

            // Screen-space derivatives of the terrain UV, taken once at the top of main().
            // Every layer tap goes through textureGrad because the taps live inside weight
            // branches (where implicit derivatives are undefined) and because the far-scale
            // tap below reads a deliberately different mip level of the same texture.
            vec2 gUVdx;
            vec2 gUVdy;

            // 0 near the camera, 1 once the enlarged copy of each texture has fully taken over.
            float gFarBlend;

            // Per-layer material gathered before blending, so the blend can compare layers
            // against each other instead of accumulating them one at a time.
            vec3  gAlbedo[TERRAIN_LAYERS];
            vec2  gNormalPD[TERRAIN_LAYERS];   // tangent normal as a slope (xy / z)
            float gHeight[TERRAIN_LAYERS];
            float gRoughness[TERRAIN_LAYERS];
            float gMetallic[TERRAIN_LAYERS];
            float gWeight[TERRAIN_LAYERS];

            // A terrain layer has no height map, but its relief is still recoverable from two
            // channels: luminance (crevices sit in shadow, tops catch the light) and the detail
            // normal (a flat-topped bump keeps n.z near 1, the walls falling away from it do not).
            // Folding the normal in is what turns a soft splatmap gradient into a transition that
            // follows the individual pebbles and grooves of the material.
            float detailHeight(vec3 albedo, vec3 normalTS)
            {
                float luminance = dot(albedo, vec3(0.299, 0.587, 0.114));
                float flatness = clamp(normalTS.z, 0.0, 1.0);
                return luminance * mix(1.0, flatness, _NormalHeightInfluence);
            }

            void gatherLayer(int index, sampler2D albedoTex, sampler2D normalTex,
                             float tiling, float roughness, float metallic, float weight)
            {
                gWeight[index] = weight;
                if (weight <= 0.001)
                {
                    gHeight[index] = 0.0;
                    return;
                }

                vec2 uv = texCoord0 * tiling;
                vec3 albedo = textureGrad(albedoTex, uv, gUVdx * tiling, gUVdy * tiling).rgb;
                vec3 normalTS = unpackNormal(textureGrad(normalTex, uv, gUVdx * tiling, gUVdy * tiling));

                if (gFarBlend > 0.001)
                {
                    // Same texture, far fewer repeats. Up close the near tiling carries the detail;
                    // at distance its repeat period shrinks to a few pixels and reads as a grid, so
                    // the blown-up copy - which has no visible repeat left - takes over instead.
                    float farTiling = tiling / max(_FarTilingScale, 1.0);
                    vec2 farUV = texCoord0 * farTiling;
                    vec3 farAlbedo = textureGrad(albedoTex, farUV, gUVdx * farTiling, gUVdy * farTiling).rgb;
                    vec3 farNormal = unpackNormal(textureGrad(normalTex, farUV, gUVdx * farTiling, gUVdy * farTiling));

                    albedo = mix(albedo, farAlbedo, gFarBlend);
                    normalTS = normalize(mix(normalTS, farNormal, gFarBlend));
                }

                gAlbedo[index] = albedo;
                // Store the normal as a slope. Averaging slopes keeps the steepness of the
                // strongest layer, where averaging unit normals would flatten it toward (0,0,1).
                gNormalPD[index] = normalTS.xy / max(normalTS.z, 1e-3);
                gHeight[index] = detailHeight(albedo, normalTS);
                gRoughness[index] = roughness;
                gMetallic[index] = metallic;
            }

            void main()
            {
                // Terrain holes
                if (_HasHoles > 0 && texture(_HolesMap, texCoord0).r < 0.5)
                    discard;

                gUVdx = dFdx(texCoord0);
                gUVdy = dFdy(texCoord0);

                // Distance ramp for the far-scale tiling. The fade is deliberately long: spread
                // over hundreds of units the crossfade never reaches a rate the eye can catch,
                // so the terrain simply stops tiling instead of visibly changing texture.
                float camDistance = length(_WorldSpaceCameraPos.xyz - worldPos);
                float farEnd = _FarTilingStart + max(_FarTilingFade, 0.001);
                gFarBlend = _FarTilingStrength * smoothstep(_FarTilingStart, farEnd, camDistance);

                // Sample splatmap 0 (layers 0-3)
                vec4 w0 = texture(_Splatmap0, texCoord0);

                gatherLayer(0, _Layer0, _Layer0Normal, _Layer0Tiling, _Layer0Roughness, _Layer0Metallic, w0.r);
                gatherLayer(1, _Layer1, _Layer1Normal, _Layer1Tiling, _Layer1Roughness, _Layer1Metallic, w0.g);
                gatherLayer(2, _Layer2, _Layer2Normal, _Layer2Tiling, _Layer2Roughness, _Layer2Metallic, w0.b);
                gatherLayer(3, _Layer3, _Layer3Normal, _Layer3Tiling, _Layer3Roughness, _Layer3Metallic, w0.a);

#ifdef TERRAIN_8_LAYERS
                // Sample splatmap 1 (layers 4-7)
                vec4 w1 = texture(_Splatmap1, texCoord0);

                gatherLayer(4, _Layer4, _Layer4Normal, _Layer4Tiling, _Layer4Roughness, _Layer4Metallic, w1.r);
                gatherLayer(5, _Layer5, _Layer5Normal, _Layer5Tiling, _Layer5Roughness, _Layer5Metallic, w1.g);
                gatherLayer(6, _Layer6, _Layer6Normal, _Layer6Tiling, _Layer6Roughness, _Layer6Metallic, w1.b);
                gatherLayer(7, _Layer7, _Layer7Normal, _Layer7Tiling, _Layer7Roughness, _Layer7Metallic, w1.a);
#endif

                // Height-aware splat blend. Each layer competes with its painted weight *plus* its
                // per-pixel detail height, and only the layers within _HeightBlendSharpness of the
                // winner get to feather in. The painted weight still decides which layer dominates;
                // the height only decides the shape of the seam, so grass creeps into the low grout
                // of a rock layer instead of the two cross-dissolving as flat colour.
                float peak = -1.0;
                for (int i = 0; i < TERRAIN_LAYERS; i++)
                {
                    if (gWeight[i] > 0.001)
                        peak = max(peak, gHeight[i] + gWeight[i]);
                }
                float cutoff = peak - max(_HeightBlendSharpness, 0.001);

                vec3 albedo = vec3(0.0);
                vec2 blendedNormalPD = vec2(0.0);
                float roughness = 0.0;
                float metallic = 0.0;
                float totalWeight = 0.0;

                for (int i = 0; i < TERRAIN_LAYERS; i++)
                {
                    if (gWeight[i] <= 0.001)
                        continue;

                    float blend = max(gHeight[i] + gWeight[i] - cutoff, 0.0);
                    albedo += gAlbedo[i] * blend;
                    blendedNormalPD += gNormalPD[i] * blend;
                    roughness += gRoughness[i] * blend;
                    metallic += gMetallic[i] * blend;
                    totalWeight += blend;
                }

                // Normalize
                if (totalWeight > 0.0) {
                    albedo /= totalWeight;
                    blendedNormalPD /= totalWeight;
                    roughness /= totalWeight;
                    metallic /= totalWeight;
                }

                vec3 baseColor = gammaToLinearSpace(albedo);
                vec3 blendedNormalTS = normalize(vec3(blendedNormalPD, 1.0));

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
                // Ambient with specular approximation for metallic surfaces
                vec3 ambientLight = CalculateAmbient(finalWorldNormal) * _AmbientStrength;
                vec3 diffuseColor = baseColor * (1.0 - metallic);
                vec3 ambientDiffuse = ambientLight * diffuseColor;

                vec3 F0 = mix(vec3(0.04), baseColor, metallic);
                float NdotV = max(dot(finalWorldNormal, viewDir), 0.0);
                vec3 F = FresnelSchlickRoughness(NdotV, F0, roughness);
                float specOcclusion = 1.0 - roughness * roughness;
                vec3 ambientSpecular = ambientLight * F * mix(specOcclusion, 1.0, 0.25);

                vec3 color = ambientDiffuse + ambientSpecular + lighting;
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
                vec2 coord = uv * (texSize - 1.0);
                vec2 f = fract(coord);
                coord -= f;
                vec2 f2 = f * f; vec2 f3 = f2 * f;
                vec2 w0 = -0.5*f3 + f2 - 0.5*f;
                vec2 w1 = 1.5*f3 - 2.5*f2 + 1.0;
                vec2 w2 = -1.5*f3 + 2.0*f2 + 0.5*f;
                vec2 w3 = 0.5*f3 - 0.5*f2;
                vec2 s0 = max(w0+w1, vec2(1e-5)); vec2 s1 = max(w2+w3, vec2(1e-5));
                vec2 f0 = w1/s0; vec2 f1 = w3/s1;
                vec2 t0 = (coord-0.5+f0)*invTexSize;
                vec2 t1 = (coord+1.5+f1)*invTexSize;
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

Pass "TerrainPrepass"
{
    Tags { "LightMode" = "Prepass" }
    Cull Back
    ZWrite On

    GLSLPROGRAM

        Vertex
        {
            #include "ProwlCG"
            #include "VertexAttributes"

            out vec3 worldNormal;
            out vec2 texCoord0;
            out vec4 vCurrClipNJ;
            out vec4 vPrevClip;

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
                vec2 coord = uv * (texSize - 1.0);
                vec2 f = fract(coord);
                coord -= f;
                vec2 f2 = f * f; vec2 f3 = f2 * f;
                vec2 w0 = -0.5*f3 + f2 - 0.5*f;
                vec2 w1 = 1.5*f3 - 2.5*f2 + 1.0;
                vec2 w2 = -1.5*f3 + 2.0*f2 + 0.5*f;
                vec2 w3 = 0.5*f3 - 0.5*f2;
                vec2 s0 = max(w0+w1, vec2(1e-5)); vec2 s1 = max(w2+w3, vec2(1e-5));
                vec2 f0 = w1/s0; vec2 f1 = w3/s1;
                vec2 t0 = (coord-0.5+f0)*invTexSize;
                vec2 t1 = (coord+1.5+f1)*invTexSize;
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

                // Static terrain: only the camera moves, so previous world position is identical.
                vCurrClipNJ = PROWL_MATRIX_VP_NONJITTERED * vec4(worldPosition, 1.0);
                vPrevClip = PROWL_MATRIX_VP_PREVIOUS * vec4(worldPosition, 1.0);
#else
                gl_Position = PROWL_MATRIX_MVP * vec4(vertexPosition, 1.0);
                worldNormal = normalize((PROWL_MATRIX_M * vec4(0.0, 1.0, 0.0, 0.0)).xyz);
                texCoord0 = vertexTexCoord0;

                vec4 wp = PROWL_MATRIX_M * vec4(vertexPosition, 1.0);
                vCurrClipNJ = PROWL_MATRIX_VP_NONJITTERED * wp;
                vPrevClip = PROWL_MATRIX_VP_PREVIOUS * (PROWL_MATRIX_M_PREVIOUS * vec4(vertexPosition, 1.0));
#endif
            }
        }

        Fragment
        {
            #include "ProwlCG"

            layout (location = 0) out vec4 normalOut;
            layout (location = 1) out vec4 motionRM;
            in vec3 worldNormal;
            in vec2 texCoord0;
            in vec4 vCurrClipNJ;
            in vec4 vPrevClip;

            uniform sampler2D _HolesMap;
            uniform int _HasHoles;

            void main()
            {
                if (_HasHoles > 0 && texture(_HolesMap, texCoord0).r < 0.5)
                    discard;
                normalOut = EncodeViewNormal(worldNormal);

                // Motion vectors (jitter-free). Terrain has no per-pixel material textures here,
                // so pack a diffuse default: roughness 1, metallic 0.
                vec2 currNDC = (vCurrClipNJ.xy / vCurrClipNJ.w) * 0.5 + 0.5;
                vec2 prevNDC = (vPrevClip.xy / vPrevClip.w) * 0.5 + 0.5;
                motionRM = vec4(currNDC - prevNDC, 1.0, 0.0);
            }
        }
    ENDGLSL
}
