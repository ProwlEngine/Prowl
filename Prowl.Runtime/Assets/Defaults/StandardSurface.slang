module StandardSurface;

// Standard PBR Surface - shared between opaque and transparent Standard shaders.
//
// Texture channels:
//   _MainTex: RGB Albedo, A Alpha   _NormalTex: tangent-space normal
//   _SurfaceTex: R AO, G Roughness, B Metallic   _EmissionTex: RGB emission
//   _ParallaxMap: G height (POM)   _TranslucencyMap: G occlusion, B thickness
//
// fragCoord (SV_Position.xy) is threaded through to the shadow PCF rotation.

__exported import ProwlCG;
__exported import Lighting;

// Baked GI, selected per-object by _GIMode:
//   0 = realtime ambient, 1 = baked lightmap (RGBM), 2 = light-probe SH.
public struct SurfaceGI
{
    public int _GIMode;
    public float4 _LightmapScaleOffset;
    // Which UV set the lightmap was baked into: 1 = UV2 (dedicated), 0 = UV0 (fallback).
    public int _LightmapUV;
    public Sampler2D<float4> _Lightmap;
}
public ParameterBlock<SurfaceGI> GI;

public float3 DecodeRGBM(float4 rgbm) { return rgbm.rgb * (rgbm.a * 8.0); }

public float3 CalculateGI(float3 worldNormal, float2 lightmapUV2, float2 uv0)
{
    if (GI._GIMode == 1)
    {
        float2 base = (GI._LightmapUV == 1) ? lightmapUV2 : uv0;
        float2 lmUV = base * GI._LightmapScaleOffset.xy + GI._LightmapScaleOffset.zw;
        return DecodeRGBM(GI._Lightmap.Sample(lmUV)); // baked irradiance (linear)
    }
    if (GI._GIMode == 2)
        return ShadeSH9(worldNormal);               // light-probe SH

    return CalculateAmbient(worldNormal) * Light._AmbientStrength;
}

// Full Standard PBR surface with POM and translucency support.
public float4 StandardSurface(
    float2 uv,
    float3 worldPos,
    float4 vertColor,
    float3 normal,
    float3 tangent,
    float3 bitangent,
    Sampler2D<float4> albedoTex,
    Sampler2D<float4> normalTex,
    Sampler2D<float4> surfaceTex,
    Sampler2D<float4> emissionTex,
    float emissionIntensity,
    float4 mainColor,
    Sampler2D<float4> parallaxMap,
    float parallaxHeight,
    int parallaxSteps,
    Sampler2D<float4> translucencyMap,
    float translucencyStrength,
    float scatteringPower,
    float scatteringDistortion,
    float scatteringScale,
    float2 lightmapUV2,
    float2 fragCoord)
{
    float2 finalUV = uv;

    // --- Parallax Occlusion Mapping ---
#ifdef HAS_TANGENTS
    if (parallaxHeight > 0.0 && parallaxSteps > 0)
    {
        float3 N = normalize(normal);
        float3 T = normalize(tangent);
        float3 B = normalize(bitangent);
        float3x3 TBN = float3x3(T, B, N);

        float3 viewDirWorld = Frame._WorldSpaceCameraPos.xyz - worldPos;
        // GLSL transpose(mat3(T,B,N)) (cols) world->tangent == Slang mul(float3x3(T,B,N), v) (rows).
        float3 viewDirTS = normalize(mul(TBN, viewDirWorld));

        finalUV = ParallaxOcclusionMapping(parallaxMap, uv, viewDirTS, parallaxHeight, parallaxSteps);
    }
#endif

    // --- Albedo ---
    float4 albedo = albedoTex.Sample(finalUV) * vertColor * mainColor;
    float3 baseColor = gammaToLinearSpace(albedo.rgb);

    // --- Normal mapping ---
    float3 worldNormal = ApplyNormalMap(normalTex, finalUV, normal, tangent, bitangent);

    // --- Surface: R = AO, G = Roughness, B = Metallic ---
    float4 surface = surfaceTex.Sample(finalUV);
    float ao = 1.0 - surface.r;
    float roughness = surface.g;
    float metallic = surface.b;

    // --- Translucency map: G = extra occlusion, B = thickness ---
    float4 transOcc = translucencyMap.Sample(finalUV);
    ao *= transOcc.g; // Combine surface AO with translucency occlusion
    float translucency = transOcc.b * translucencyStrength;

    // --- Emission ---
    float3 emission = emissionTex.Sample(finalUV).rgb * emissionIntensity;

    // --- Forward PBR lighting + translucency (single pass, shared attenuation/shadow) ---
    float3 viewDir = normalize(Frame._WorldSpaceCameraPos.xyz - worldPos);
    float3 lighting = CalculateForwardLighting(worldPos, worldNormal, viewDir,
                                             baseColor, metallic, roughness, ao,
                                             translucency, scatteringPower,
                                             scatteringDistortion, scatteringScale, fragCoord);

    // --- Ambient / baked GI + Fog ---
    float3 ambientLight = CalculateGI(worldNormal, lightmapUV2, uv) * ao;

    // Diffuse ambient (non-metals only)
    float3 diffuseColor = baseColor * (1.0 - metallic);
    float3 ambientDiffuse = ambientLight * diffuseColor;

    // Specular ambient approximation (critical for metals which have no diffuse)
    float3 F0 = lerp(float3(0.04), baseColor, metallic);
    float NdotV = max(dot(worldNormal, viewDir), 0.0);
    float3 F = FresnelSchlickRoughness(NdotV, F0, roughness);
    float specOcclusion = 1.0 - roughness * roughness;
    float3 ambientSpecular = ambientLight * F * lerp(specOcclusion, 1.0, 0.25);

    float3 ambient = ambientDiffuse + ambientSpecular;
    float3 color = ApplyFog(ambient + lighting + emission, worldPos);

    return float4(color, albedo.a);
}

// Simplified overload without POM/translucency (for basic shaders)
public float4 StandardSurfaceSimple(
    float2 uv,
    float3 worldPos,
    float4 vertColor,
    float3 normal,
    float3 tangent,
    float3 bitangent,
    Sampler2D<float4> albedoTex,
    Sampler2D<float4> normalTex,
    Sampler2D<float4> surfaceTex,
    Sampler2D<float4> emissionTex,
    float emissionIntensity,
    float4 mainColor,
    float2 fragCoord)
{
    // --- Albedo ---
    float4 albedo = albedoTex.Sample(uv) * vertColor * mainColor;
    float3 baseColor = gammaToLinearSpace(albedo.rgb);

    // --- Normal mapping ---
    float3 worldNormal = ApplyNormalMap(normalTex, uv, normal, tangent, bitangent);

    // --- Surface ---
    float4 surface = surfaceTex.Sample(uv);
    float ao = 1.0 - surface.r;
    float roughness = surface.g;
    float metallic = surface.b;

    // --- Emission ---
    float3 emission = emissionTex.Sample(uv).rgb * emissionIntensity;

    // --- Forward PBR lighting ---
    float3 viewDir = normalize(Frame._WorldSpaceCameraPos.xyz - worldPos);
    float3 lighting = CalculateForwardLighting(worldPos, worldNormal, viewDir,
                                             baseColor, metallic, roughness, ao, fragCoord);
    float3 ambientLight = CalculateAmbient(worldNormal) * ao * Light._AmbientStrength;

    float3 diffuseColor = baseColor * (1.0 - metallic);
    float3 ambientDiffuse = ambientLight * diffuseColor;

    float3 F0 = lerp(float3(0.04), baseColor, metallic);
    float NdotV = max(dot(worldNormal, viewDir), 0.0);
    float3 F = FresnelSchlickRoughness(NdotV, F0, roughness);
    float specOcclusion = 1.0 - roughness * roughness;
    float3 ambientSpecular = ambientLight * F * lerp(specOcclusion, 1.0, 0.25);

    float3 ambient = ambientDiffuse + ambientSpecular;
    float3 color = ApplyFog(ambient + lighting + emission, worldPos);

    return float4(color, albedo.a);
}
