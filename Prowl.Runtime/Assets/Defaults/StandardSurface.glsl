// Standard PBR Surface - shared between opaque and transparent Standard shaders.
//
// Texture channels:
//   _MainTex:          RGB = Albedo, A = Alpha
//   _NormalTex:        RGB = Tangent-space normal
//   _SurfaceTex:       R = AO, G = Roughness, B = Metallic
//   _EmissionTex:      RGB = Emission color
//   _ParallaxMap:      G = Height (for POM)
//   _TranslucencyMap:  G = Occlusion, B = Thickness/Translucency

#ifndef STANDARD_SURFACE
#define STANDARD_SURFACE

#include "ProwlCG"
#include "Lighting"

// --- Baked GI, selected per-object by _GIMode ---
//   0 = realtime ambient (CalculateAmbient), 1 = baked lightmap (RGBM), 2 = light-probe SH.
// _GIMode is set per-draw by the render pipeline; _Lightmap/_LightmapScaleOffset are per-object
// (lightmapped statics); the prowl_SH* uniforms (in Lighting) are per-object (probe-lit dynamics).
uniform int _GIMode;
uniform sampler2D _Lightmap;
uniform vec4 _LightmapScaleOffset;
// Which UV set the lightmap was baked into: 1 = UV2 (dedicated), 0 = UV0 (primary, fallback for
// meshes without UV2). Matches LightmapBakeService's per-mesh fallback.
uniform int _LightmapUV;

vec3 DecodeRGBM(vec4 rgbm) { return rgbm.rgb * (rgbm.a * 8.0); }

vec3 CalculateGI(vec3 worldNormal, vec2 lightmapUV2, vec2 uv0)
{
    if (_GIMode == 1)
    {
        vec2 base = (_LightmapUV == 1) ? lightmapUV2 : uv0;
        vec2 lmUV = base * _LightmapScaleOffset.xy + _LightmapScaleOffset.zw;
        return DecodeRGBM(texture(_Lightmap, lmUV)); // baked irradiance (linear)
    }
    if (_GIMode == 2)
        return ShadeSH9(worldNormal);               // light-probe SH

    return CalculateAmbient(worldNormal) * _AmbientStrength;
}

// Full Standard PBR surface with POM and translucency support.
vec4 StandardSurface(
    vec2 uv,
    vec3 worldPos,
    vec4 vertColor,
    vec3 normal,
    vec3 tangent,
    vec3 bitangent,
    sampler2D albedoTex,
    sampler2D normalTex,
    sampler2D surfaceTex,
    sampler2D emissionTex,
    float emissionIntensity,
    vec4 mainColor,
    sampler2D parallaxMap,
    float parallaxHeight,
    int parallaxSteps,
    sampler2D translucencyMap,
    float translucencyStrength,
    float scatteringPower,
    float scatteringDistortion,
    float scatteringScale,
    vec2 lightmapUV2)
{
    vec2 finalUV = uv;

    // --- Parallax Occlusion Mapping ---
#ifdef HAS_TANGENTS
    if (parallaxHeight > 0.0 && parallaxSteps > 0)
    {
        vec3 N = normalize(normal);
        vec3 T = normalize(tangent);
        vec3 B = normalize(bitangent);
        mat3 TBN = mat3(T, B, N);

        vec3 viewDirWorld = _WorldSpaceCameraPos.xyz - worldPos;
        // Transform to tangent space, then normalize preserves direction accuracy
        vec3 viewDirTS = normalize(transpose(TBN) * viewDirWorld);

        finalUV = ParallaxOcclusionMapping(parallaxMap, uv, viewDirTS, parallaxHeight, parallaxSteps);
    }
#endif

    // --- Albedo ---
    vec4 albedo = texture(albedoTex, finalUV) * vertColor * mainColor;
    vec3 baseColor = gammaToLinearSpace(albedo.rgb);

    // --- Normal mapping ---
    vec3 worldNormal = ApplyNormalMap(normalTex, finalUV, normal, tangent, bitangent);

    // --- Surface: R = AO, G = Roughness, B = Metallic ---
    vec4 surface = texture(surfaceTex, finalUV);
    float ao = 1.0 - surface.r;
    float roughness = surface.g;
    float metallic = surface.b;

    // --- Translucency map: G = extra occlusion, B = thickness ---
    vec4 transOcc = texture(translucencyMap, finalUV);
    ao *= transOcc.g; // Combine surface AO with translucency occlusion
    float translucency = transOcc.b * translucencyStrength;

    // --- Emission ---
    vec3 emission = texture(emissionTex, finalUV).rgb * emissionIntensity;

    // --- Forward PBR lighting + translucency (single pass, shared attenuation/shadow) ---
    vec3 viewDir = normalize(_WorldSpaceCameraPos.xyz - worldPos);
    vec3 lighting = CalculateForwardLighting(worldPos, worldNormal, viewDir,
                                             baseColor, metallic, roughness, ao,
                                             translucency, scatteringPower,
                                             scatteringDistortion, scatteringScale);

    // --- Ambient / baked GI + Fog ---
    vec3 ambientLight = CalculateGI(worldNormal, lightmapUV2, uv) * ao;

    // Diffuse ambient (non-metals only, metals have no diffuse)
    vec3 diffuseColor = baseColor * (1.0 - metallic);
    vec3 ambientDiffuse = ambientLight * diffuseColor;

    // Specular ambient approximation (critical for metals which have no diffuse)
    // Without IBL/environment maps we approximate indirect specular using the ambient
    // light, Fresnel at the view angle, and a roughness-dependent falloff.
    vec3 F0 = mix(vec3(0.04), baseColor, metallic);
    float NdotV = max(dot(worldNormal, viewDir), 0.0);
    vec3 F = FresnelSchlickRoughness(NdotV, F0, roughness);
    // Rough surfaces scatter indirect specular broadly, reducing intensity
    float specOcclusion = 1.0 - roughness * roughness;
    vec3 ambientSpecular = ambientLight * F * mix(specOcclusion, 1.0, 0.25);

    vec3 ambient = ambientDiffuse + ambientSpecular;
    vec3 color = ApplyFog(ambient + lighting + emission, worldPos);

    return vec4(color, albedo.a);
}

// Simplified overload without POM/translucency (for basic shaders)
vec4 StandardSurfaceSimple(
    vec2 uv,
    vec3 worldPos,
    vec4 vertColor,
    vec3 normal,
    vec3 tangent,
    vec3 bitangent,
    sampler2D albedoTex,
    sampler2D normalTex,
    sampler2D surfaceTex,
    sampler2D emissionTex,
    float emissionIntensity,
    vec4 mainColor)
{
    // --- Albedo ---
    vec4 albedo = texture(albedoTex, uv) * vertColor * mainColor;
    vec3 baseColor = gammaToLinearSpace(albedo.rgb);

    // --- Normal mapping ---
    vec3 worldNormal = ApplyNormalMap(normalTex, uv, normal, tangent, bitangent);

    // --- Surface ---
    vec4 surface = texture(surfaceTex, uv);
    float ao = 1.0 - surface.r;
    float roughness = surface.g;
    float metallic = surface.b;

    // --- Emission ---
    vec3 emission = texture(emissionTex, uv).rgb * emissionIntensity;

    // --- Forward PBR lighting ---
    vec3 viewDir = normalize(_WorldSpaceCameraPos.xyz - worldPos);
    vec3 lighting = CalculateForwardLighting(worldPos, worldNormal, viewDir,
                                             baseColor, metallic, roughness, ao);
    vec3 ambientLight = CalculateAmbient(worldNormal) * ao * _AmbientStrength;

    vec3 diffuseColor = baseColor * (1.0 - metallic);
    vec3 ambientDiffuse = ambientLight * diffuseColor;

    vec3 F0 = mix(vec3(0.04), baseColor, metallic);
    float NdotV = max(dot(worldNormal, viewDir), 0.0);
    vec3 F = FresnelSchlickRoughness(NdotV, F0, roughness);
    float specOcclusion = 1.0 - roughness * roughness;
    vec3 ambientSpecular = ambientLight * F * mix(specOcclusion, 1.0, 0.25);

    vec3 ambient = ambientDiffuse + ambientSpecular;
    vec3 color = ApplyFog(ambient + lighting + emission, worldPos);

    return vec4(color, albedo.a);
}

#endif
