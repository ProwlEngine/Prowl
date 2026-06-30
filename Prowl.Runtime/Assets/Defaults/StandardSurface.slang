module StandardSurface;

__exported import ProwlCG;
__exported import Lighting;

// Baked GI, selected per-object by _GIMode (0 = realtime ambient, 1 = baked lightmap RGBM,
// 2 = light-probe SH). _GIMode is per-draw; _Lightmap/_LightmapScaleOffset/_LightmapUV are per-object.
public struct SurfaceGIData
{
    public int _GIMode;
    public Sampler2D<float4> _Lightmap;
    public float4 _LightmapScaleOffset;
    public int _LightmapUV;
}
public ParameterBlock<SurfaceGIData> GI;

public float3 DecodeRGBM(float4 rgbm) { return rgbm.rgb * (rgbm.a * 8.0); }

public float3 CalculateGI(float3 worldNormal, float2 lightmapUV2, float2 uv0)
{
    if (GI._GIMode == 1)
    {
        float2 base = (GI._LightmapUV == 1) ? lightmapUV2 : uv0;
        float2 lmUV = base * GI._LightmapScaleOffset.xy + GI._LightmapScaleOffset.zw;
        return DecodeRGBM(GI._Lightmap.Sample(lmUV));
    }
    if (GI._GIMode == 2)
        return ShadeSH9(worldNormal);

    return CalculateAmbient(worldNormal) * Lights._AmbientStrength;
}

// Full Standard PBR surface with POM and translucency support. hasTangents is the shader's
// HAS_TANGENTS variant const (compile-time, folds the branch). fragCoord is input.position.xy.
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
    bool hasTangents,
    float2 fragCoord)
{
    float2 finalUV = uv;

    // --- Parallax Occlusion Mapping ---
    if (hasTangents && parallaxHeight > 0.0 && parallaxSteps > 0)
    {
        float3 N = normalize(normal);
        float3 T = normalize(tangent);
        float3 B = normalize(bitangent);

        float3 viewDirWorld = Frame._WorldSpaceCameraPos.xyz - worldPos;
        float3 viewDirTS = normalize(mul(float3x3(T, B, N), viewDirWorld));

        finalUV = ParallaxOcclusionMapping(parallaxMap, uv, viewDirTS, parallaxHeight, parallaxSteps);
    }

    // --- Albedo ---
    float4 albedo = albedoTex.Sample(finalUV) * vertColor * mainColor;
    float3 baseColor = gammaToLinearSpace(albedo.rgb);

    // --- Normal mapping ---
    float3 worldNormal;
    if (hasTangents)
        worldNormal = ApplyNormalMap(normalTex, finalUV, normal, tangent, bitangent);
    else
        worldNormal = normalize(normal);

    // --- Surface: R = AO, G = Roughness, B = Metallic ---
    float4 surface = surfaceTex.Sample(finalUV);
    float ao = 1.0 - surface.r;
    float roughness = surface.g;
    float metallic = surface.b;

    // --- Translucency map: G = extra occlusion, B = thickness ---
    float4 transOcc = translucencyMap.Sample(finalUV);
    ao *= transOcc.g;
    float translucency = transOcc.b * translucencyStrength;

    // --- Emission ---
    float3 emission = emissionTex.Sample(finalUV).rgb * emissionIntensity;

    // --- Forward PBR lighting + translucency ---
    float3 viewDir = normalize(Frame._WorldSpaceCameraPos.xyz - worldPos);
    float3 lighting = CalculateForwardLighting(worldPos, worldNormal, viewDir,
                                             baseColor, metallic, roughness, ao,
                                             translucency, scatteringPower,
                                             scatteringDistortion, scatteringScale, fragCoord);

    // --- Ambient / baked GI + Fog ---
    float3 ambientLight = CalculateGI(worldNormal, lightmapUV2, uv) * ao;

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

// Simplified overload without POM/translucency.
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
    bool hasTangents,
    float2 fragCoord)
{
    float4 albedo = albedoTex.Sample(uv) * vertColor * mainColor;
    float3 baseColor = gammaToLinearSpace(albedo.rgb);

    float3 worldNormal;
    if (hasTangents)
        worldNormal = ApplyNormalMap(normalTex, uv, normal, tangent, bitangent);
    else
        worldNormal = normalize(normal);

    float4 surface = surfaceTex.Sample(uv);
    float ao = 1.0 - surface.r;
    float roughness = surface.g;
    float metallic = surface.b;

    float3 emission = emissionTex.Sample(uv).rgb * emissionIntensity;

    float3 viewDir = normalize(Frame._WorldSpaceCameraPos.xyz - worldPos);
    float3 lighting = CalculateForwardLighting(worldPos, worldNormal, viewDir,
                                             baseColor, metallic, roughness, ao, fragCoord);
    float3 ambientLight = CalculateAmbient(worldNormal) * ao * Lights._AmbientStrength;

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
