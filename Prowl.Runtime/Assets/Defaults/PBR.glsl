#ifndef PBR_FUNCTIONS
#define PBR_FUNCTIONS

#ifndef MATH_PI
#define MATH_PI
const float PI = 3.14159265359;
#endif

// =============================================================
//  Standard Isotropic BRDF (GGX + Smith + Disney Diffuse)
// =============================================================

// GGX/Trowbridge-Reitz Normal Distribution Function
// Input roughness is perceptual roughness [0,1] — squared internally to get alpha.
float DistributionGGX(vec3 N, vec3 H, float roughness)
{
    float a2 = roughness * roughness; // alpha = perceptualRoughness², a2 = alpha
    float NdotH = max(dot(N, H), 0.0);
    float NdotH2 = NdotH * NdotH;

    float denom = (NdotH2 * (a2 - 1.0) + 1.0);
    denom = PI * denom * denom;

    return a2 / denom;
}

// Schlick-GGX Geometry Function (single direction)
// Uses k = (roughness + 1)² / 8 for direct/analytical lights.
float GeometrySchlickGGX(float NdotV, float roughness)
{
    float r = (roughness + 1.0);
    float k = (r * r) / 8.0;

    return NdotV / (NdotV * (1.0 - k) + k);
}

// Smith Geometry Function (both directions combined)
// Uses abs(NdotV) to handle backface normals gracefully.
float GeometrySmith(vec3 N, vec3 V, vec3 L, float roughness)
{
    float NdotV = abs(dot(N, V));
    float NdotL = max(dot(N, L), 0.0);
    float ggx2 = GeometrySchlickGGX(NdotV, roughness);
    float ggx1 = GeometrySchlickGGX(NdotL, roughness);

    return ggx1 * ggx2;
}

// Schlick Fresnel Approximation (fast exp2 variant)
vec3 FresnelSchlick(float cosTheta, vec3 F0)
{
    return F0 + (1.0 - F0) * exp2(-9.28 * cosTheta);
}

// Fresnel with roughness for IBL/indirect specular (Lazarov Environmental BRDF)
vec3 FresnelSchlickRoughness(float cosTheta, vec3 F0, float roughness)
{
    vec3 F90 = max(vec3(1.0 - roughness), F0);
    return F0 + (F90 - F0) * exp2(-9.28 * cosTheta);
}

// =============================================================
//  Disney Diffuse (from Burley 2012)
//  Roughness-dependent diffuse that darkens at grazing angles
//  for rough surfaces — more physically accurate than Lambertian.
// =============================================================

float DisneyDiffuse(float NdotV, float NdotL, float LdotH, float perceptualRoughness)
{
    float fd90 = 0.5 + 2.0 * LdotH * LdotH * perceptualRoughness;
    float lightScatter = 1.0 + (fd90 - 1.0) * exp2(-8.35 * NdotL);
    float viewScatter = 1.0 + (fd90 - 1.0) * exp2(-8.35 * NdotV);
    return lightScatter * viewScatter / PI;
}

// =============================================================
//  Anisotropic BRDF (Far Cry 4 / GGX Aniso)
//  For brushed metal, hair, and directional surfaces.
// =============================================================

// Anisotropic GGX NDF
// mt and mb are SQUARED roughness values (mt = roughnessT², mb = roughnessB²)
float DistributionGGXAniso(float TdotH, float BdotH, float NdotH, float mt, float mb)
{
    float d = TdotH * TdotH / (mt * mt) + BdotH * BdotH / (mb * mb) + NdotH * NdotH;
    return 1.0 / (PI * mt * mb * d * d);
}

// Anisotropic Smith Joint Visibility (Far Cry 4)
// mt and mb are SQUARED roughness values (mt = roughnessT², mb = roughnessB²)
float GeometrySmithAniso(
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
//  Reduces specular shimmer on normal-mapped surfaces by
//  increasing roughness where normal variance is high.
//  Based on: Kaplanyan & Hill, "Filtering Distributions of Normals"
// =============================================================

#define SPEC_AA_VARIANCE 0.5
#define SPEC_AA_THRESHOLD 0.1

float ApplySpecularAA(float roughness, vec3 worldNormal)
{
    vec3 ddxN = dFdx(worldNormal);
    vec3 ddyN = dFdy(worldNormal);
    float variance = SPEC_AA_VARIANCE * (dot(ddxN, ddxN) + dot(ddyN, ddyN));
    float kernelRoughnessSq = min(2.0 * variance, SPEC_AA_THRESHOLD);
    float squaredRoughness = clamp(roughness * roughness + kernelRoughnessSq, 0.0, 1.0);
    return sqrt(squaredRoughness);
}

// =============================================================
//  Parallax Occlusion Mapping (POM)
//  Linear ray march + secant-method binary refinement.
//
//  Usage:
//    vec2 pomUV = ParallaxOcclusionMapping(heightTex, uv, viewDirTS, heightScale, steps);
//
//  viewDirTS must be in tangent space (use transpose(TBN) * viewDir).
//  Height is sampled from the G channel of the height texture.
// =============================================================

vec2 ParallaxOcclusionMapping(sampler2D heightTex, vec2 uv, vec3 viewDirTS, float heightScale, int linearSteps)
{
    // Slope damping: reduce parallax at grazing angles
    float slopeDamp = 1.0 - clamp(dot(viewDirTS, vec3(0.0, 0.0, 1.0)), 0.0, 1.0);

    // Maximum UV offset based on view angle and height scale
    vec2 vMaxOffset = (viewDirTS.xy / -viewDirTS.z) * heightScale * (1.0 - slopeDamp * slopeDamp);

    float fStepSize = 1.0 / float(linearSteps);
    vec2 finalStepSize = fStepSize * vMaxOffset;

    // Pre-compute texture derivatives for correct mip selection
    vec2 dx = dFdx(uv);
    vec2 dy = dFdy(uv);

    // Linear ray march from surface (1.0) downward
    float fCurrRayHeight = 1.0;
    vec2 vCurrOffset = vec2(0.0);
    vec2 vLastOffset = vec2(0.0);
    float fLastSampledHeight = 1.0;
    float fCurrSampledHeight = 1.0;

    for (int i = 0; i < linearSteps; i++)
    {
        fCurrSampledHeight = textureGrad(heightTex, uv + vCurrOffset, dx, dy).g;

        if (fCurrSampledHeight > fCurrRayHeight)
            break;

        fCurrRayHeight -= fStepSize;
        vLastOffset = vCurrOffset;
        vCurrOffset += finalStepSize;
        fLastSampledHeight = fCurrSampledHeight;
    }

    // Secant-method binary refinement (3 iterations)
    float pt0 = fCurrRayHeight + fStepSize;
    float pt1 = fCurrRayHeight;
    float delta0 = pt0 - fLastSampledHeight;
    float delta1 = pt1 - fCurrSampledHeight;

    for (int j = 0; j < 3; j++)
    {
        float intersectionHeight = (pt0 * delta1 - pt1 * delta0) / (delta1 - delta0);
        vCurrOffset = (1.0 - intersectionHeight) * finalStepSize * float(linearSteps);

        fCurrSampledHeight = textureGrad(heightTex, uv + vCurrOffset, dx, dy).g;

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
//  Translucency
//  Two modes controlled by scatteringPower:
//    - scatteringPower == 0: Wrapped diffuse + GGX backscatter (foliage, thin surfaces)
//    - scatteringPower > 0:  Spherical Gaussian approximation (skin, wax)
//  Reference: Colin Barré-Brisebois, GDC 2011
//
//  translucency: per-pixel thickness/translucency value (from texture B channel)
//  scatteringPower: Gaussian falloff exponent (0 = wrapped mode, 1-8 = gaussian)
//  distortion: how much the normal bends the light direction through the surface
//  scale: overall translucency intensity multiplier
// =============================================================

vec3 CalculateTranslucency(vec3 lightDir, vec3 viewDir, vec3 normal,
                           float translucency, float scatteringPower,
                           float distortion, float scale, vec3 lightColor)
{
    if (translucency <= 0.0) return vec3(0.0);

    vec3 lightScattering = vec3(0.0);

    if (scatteringPower < 0.001)
    {
        // Mode 1: Wrapped diffuse + GGX backscatter lobe
        // foliage, thin cloth, paper
        float wrap = 0.5;
        float wrappedNdotL = clamp((dot(-normal, lightDir) + wrap) / ((1.0 + wrap) * (1.0 + wrap)), 0.0, 1.0);

        // GGX-shaped backscatter approximation
        float VdotL = clamp(dot(viewDir, -lightDir), 0.0, 1.0);
        float a2 = 0.49; // 0.7^2
        float d = (VdotL * a2 - VdotL) * VdotL + 1.0;
        float GGX = (a2 / PI) / (d * d);

        lightScattering = wrappedNdotL * GGX * translucency * lightColor;
    }
    else
    {
        // Mode 2: Spherical Gaussian approximation
        // skin, wax, subsurface materials
        vec3 transLightDir = lightDir + normal * distortion;
        float transDot = dot(-transLightDir, viewDir);
        transDot = exp2(clamp(transDot, 0.0, 1.0) * scatteringPower - scatteringPower) * translucency;

        lightScattering = transDot * lightColor;
    }

    return lightScattering * scale;
}

#endif
