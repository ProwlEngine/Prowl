#ifndef PBR_FUNCTIONS
#define PBR_FUNCTIONS

// ------------------------------------------------------------------------------
#ifndef MATH_PI
#define MATH_PI
static const float PI = 3.14159265359f;
#endif

// ----------------------------------------------------------------------------
float DistributionGGX(float3 N, float3 H, float roughness)
{
    float a = roughness * roughness;
    float a2 = a * a;
    float NdotH = max(dot(N, H), 0.0f);
    float NdotH2 = NdotH * NdotH;
    float nom = a2;
    float denom = (NdotH2 * (a2 - 1.0f) + 1.0f);
    denom = PI * denom * denom;
    return nom / max(denom, 0.001f); // Added epsilon to prevent division by zero
}

// ----------------------------------------------------------------------------
float GeometrySchlickGGX(float NdotV, float roughness)
{
    float r = (roughness + 1.0f);
    float k = (r * r) / 8.0f;
    float nom = NdotV;
    float denom = NdotV * (1.0f - k) + k;
    return nom / max(denom, 0.001f);
}

// ----------------------------------------------------------------------------
float GeometrySmith(float3 N, float3 V, float3 L, float roughness)
{
    float NdotV = max(dot(N, V), 0.0f);
    float NdotL = max(dot(N, L), 0.0f);
    float ggx2 = GeometrySchlickGGX(NdotV, roughness);
    float ggx1 = GeometrySchlickGGX(NdotL, roughness);
    return ggx1 * ggx2;
}

// ----------------------------------------------------------------------------
float3 FresnelSchlick(float cosTheta, float3 F0)
{
    //return F0 + (1.0f - F0) * pow(1.0f - cosTheta, 5.0f);
    return F0 + (1.0f - F0) * exp2(-9.28f * cosTheta); // Faster but slightly less accurate version
}

// ----------------------------------------------------------------------------
void CookTorrance(float3 N, float3 H, float3 L, float3 V, float3 F0, float roughness, float metallic,
                 out float3 kD, out float3 specular)
{
    float NDF = DistributionGGX(N, H, roughness);        
    float G = GeometrySmith(N, V, L, roughness);  
    float3 F = FresnelSchlick(max(dot(H, V), 0.0), F0);
    
    float3 nominator = NDF * G * F;
    float denominator = 4 * max(dot(N, V), 0.0) * max(dot(N, L), 0.0) + 0.001; 
    specular = nominator / denominator;
    
    float3 kS = F;
    kD = (float3)1.0 - kS;
    kD *= 1.0 - metallic;  
}

#endif
