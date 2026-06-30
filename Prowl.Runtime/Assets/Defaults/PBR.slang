module PBR;

public static const float PI = 3.14159265359;

// =============================================================
//  Standard Isotropic BRDF (GGX + Smith + Disney Diffuse)
// =============================================================

// GGX/Trowbridge-Reitz Normal Distribution Function
public float DistributionGGX(float3 N, float3 H, float roughness)
{
    float a2 = roughness * roughness;
    float NdotH = max(dot(N, H), 0.0);
    float NdotH2 = NdotH * NdotH;

    float denom = (NdotH2 * (a2 - 1.0) + 1.0);
    denom = PI * denom * denom;

    return a2 / denom;
}

// Schlick-GGX Geometry Function (single direction)
public float GeometrySchlickGGX(float NdotV, float roughness)
{
    float r = (roughness + 1.0);
    float k = (r * r) / 8.0;

    return NdotV / (NdotV * (1.0 - k) + k);
}

// Smith Geometry Function (both directions combined)
public float GeometrySmith(float3 N, float3 V, float3 L, float roughness)
{
    float NdotV = abs(dot(N, V));
    float NdotL = max(dot(N, L), 0.0);
    float ggx2 = GeometrySchlickGGX(NdotV, roughness);
    float ggx1 = GeometrySchlickGGX(NdotL, roughness);

    return ggx1 * ggx2;
}

// Schlick Fresnel Approximation (fast exp2 variant)
public float3 FresnelSchlick(float cosTheta, float3 F0)
{
    return F0 + (1.0 - F0) * exp2(-9.28 * cosTheta);
}

// Fresnel with roughness for IBL/indirect specular (Lazarov Environmental BRDF)
public float3 FresnelSchlickRoughness(float cosTheta, float3 F0, float roughness)
{
    float3 F90 = max(float3(1.0 - roughness), F0);
    return F0 + (F90 - F0) * exp2(-9.28 * cosTheta);
}

// =============================================================
//  Disney Diffuse (from Burley 2012)
// =============================================================

public float DisneyDiffuse(float NdotV, float NdotL, float LdotH, float perceptualRoughness)
{
    float fd90 = 0.5 + 2.0 * LdotH * LdotH * perceptualRoughness;
    float lightScatter = 1.0 + (fd90 - 1.0) * exp2(-8.35 * NdotL);
    float viewScatter = 1.0 + (fd90 - 1.0) * exp2(-8.35 * NdotV);
    return lightScatter * viewScatter / PI;
}

// =============================================================
//  Anisotropic BRDF (Far Cry 4 / GGX Aniso)
// =============================================================

// Anisotropic GGX NDF. mt and mb are SQUARED roughness values.
public float DistributionGGXAniso(float TdotH, float BdotH, float NdotH, float mt, float mb)
{
    float d = TdotH * TdotH / (mt * mt) + BdotH * BdotH / (mb * mb) + NdotH * NdotH;
    return 1.0 / (PI * mt * mb * d * d);
}

// Anisotropic Smith Joint Visibility (Far Cry 4). mt and mb are SQUARED roughness values.
public float GeometrySmithAniso(
    float TdotV, float BdotV, float NdotV,
    float TdotL, float BdotL, float NdotL,
    float mt, float mb)
{
    float lambdaV = NdotL * sqrt(mt * TdotV * TdotV + mb * BdotV * BdotV + NdotV * NdotV);
    float lambdaL = NdotV * sqrt(mt * TdotL * TdotL + mb * BdotL * BdotL + NdotL * NdotL);
    return 0.5 / max(1e-5, lambdaV + lambdaL);
}

// =============================================================
//  Specular Anti-Aliasing (Geometric Normal Filtering)
// =============================================================

public static const float SPEC_AA_VARIANCE = 0.5;
public static const float SPEC_AA_THRESHOLD = 0.1;

public float ApplySpecularAA(float roughness, float3 worldNormal)
{
    float3 ddxN = ddx(worldNormal);
    float3 ddyN = ddy(worldNormal);
    float variance = SPEC_AA_VARIANCE * (dot(ddxN, ddxN) + dot(ddyN, ddyN));
    float kernelRoughnessSq = min(2.0 * variance, SPEC_AA_THRESHOLD);
    float squaredRoughness = clamp(roughness * roughness + kernelRoughnessSq, 0.0, 1.0);
    return sqrt(squaredRoughness);
}

// =============================================================
//  Parallax Occlusion Mapping (POM)
//  viewDirTS must be in tangent space. Height is sampled from the G channel.
// =============================================================

public float2 ParallaxOcclusionMapping(Sampler2D<float4> heightTex, float2 uv, float3 viewDirTS, float heightScale, int linearSteps)
{
    float slopeDamp = 1.0 - clamp(dot(viewDirTS, float3(0.0, 0.0, 1.0)), 0.0, 1.0);

    float2 vMaxOffset = (viewDirTS.xy / -viewDirTS.z) * heightScale * (1.0 - slopeDamp * slopeDamp);

    float fStepSize = 1.0 / float(linearSteps);
    float2 finalStepSize = fStepSize * vMaxOffset;

    float2 dx = ddx(uv);
    float2 dy = ddy(uv);

    float fCurrRayHeight = 1.0;
    float2 vCurrOffset = float2(0.0);
    float2 vLastOffset = float2(0.0);
    float fLastSampledHeight = 1.0;
    float fCurrSampledHeight = 1.0;

    for (int i = 0; i < linearSteps; i++)
    {
        fCurrSampledHeight = heightTex.SampleGrad(uv + vCurrOffset, dx, dy).g;

        if (fCurrSampledHeight > fCurrRayHeight)
            break;

        fCurrRayHeight -= fStepSize;
        vLastOffset = vCurrOffset;
        vCurrOffset += finalStepSize;
        fLastSampledHeight = fCurrSampledHeight;
    }

    float pt0 = fCurrRayHeight + fStepSize;
    float pt1 = fCurrRayHeight;
    float delta0 = pt0 - fLastSampledHeight;
    float delta1 = pt1 - fCurrSampledHeight;

    for (int j = 0; j < 3; j++)
    {
        float intersectionHeight = (pt0 * delta1 - pt1 * delta0) / (delta1 - delta0);
        vCurrOffset = (1.0 - intersectionHeight) * finalStepSize * float(linearSteps);

        fCurrSampledHeight = heightTex.SampleGrad(uv + vCurrOffset, dx, dy).g;

        float delta = intersectionHeight - fCurrSampledHeight;
        if (delta < 0.0)
        {
            delta1 = delta;
            pt1 = intersectionHeight;
        }
        else
        {
            delta0 = delta;
            pt0 = intersectionHeight;
        }
    }

    return uv + vCurrOffset;
}

// =============================================================
//  Translucency (Colin Barre-Brisebois, GDC 2011)
// =============================================================

public float3 CalculateTranslucency(float3 lightDir, float3 viewDir, float3 normal,
                           float translucency, float scatteringPower,
                           float distortion, float scale, float3 lightColor)
{
    if (translucency <= 0.0) return float3(0.0);

    float3 lightScattering = float3(0.0);

    if (scatteringPower < 0.001)
    {
        // Mode 1: Wrapped diffuse + GGX backscatter lobe
        float wrap = 0.5;
        float wrappedNdotL = clamp((dot(-normal, lightDir) + wrap) / ((1.0 + wrap) * (1.0 + wrap)), 0.0, 1.0);

        float VdotL = clamp(dot(viewDir, -lightDir), 0.0, 1.0);
        float a2 = 0.49;
        float d = (VdotL * a2 - VdotL) * VdotL + 1.0;
        float GGX = (a2 / PI) / (d * d);

        lightScattering = wrappedNdotL * GGX * translucency * lightColor;
    }
    else
    {
        // Mode 2: Spherical Gaussian approximation
        float3 transLightDir = lightDir + normal * distortion;
        float transDot = dot(-transLightDir, viewDir);
        transDot = exp2(clamp(transDot, 0.0, 1.0) * scatteringPower - scatteringPower) * translucency;

        lightScattering = transDot * lightColor;
    }

    return lightScattering * scale;
}
