// Prowl's shared shader helpers over Graphite's library: colour space, tangent frames and normal maps.
#pragma once

#define PROWL_MIN_ROUGHNESS 0.045

#define PROWL_PI 3.14159265359
#define PROWL_TWO_PI 6.28318530718
#define PROWL_INV_PI 0.31830988618

vec3 linearToGammaSpace(vec3 lin)
{
    lin = max(lin, vec3(0.0));
    return mix(lin * 12.92, 1.055 * pow(lin, vec3(1.0 / 2.4)) - 0.055, step(vec3(0.0031308), lin));
}

vec3 gammaToLinearSpace(vec3 gamma)
{
    return gamma * (gamma * (gamma * 0.305306011 + 0.682171111) + 0.012522878);
}

float Prowl_Luminance(vec3 c) { return dot(c, vec3(0.2126, 0.7152, 0.0722)); }

// A tangent frame for a world normal, from the mesh's tangent when it has one, else from an arbitrary axis. A degenerate
// tangent, parallel to the normal or zero, takes the arbitrary axis too.
void Prowl_TangentFrame(vec3 worldNormal, vec3 worldTangent, float handedness, bool hasTangent, out vec3 tangent, out vec3 bitangent)
{
    float sign = handedness < 0.0 ? -1.0 : 1.0;
    if (hasTangent)
    {
        tangent = worldTangent;
        bitangent = cross(worldNormal, tangent) * sign;
        if (dot(bitangent, bitangent) > 0.000001)
            return;
    }
    tangent = abs(worldNormal.y) < 0.999 ? normalize(cross(worldNormal, vec3(0.0, 1.0, 0.0))) : normalize(cross(worldNormal, vec3(1.0, 0.0, 0.0)));
    bitangent = cross(worldNormal, tangent) * sign;
}

// A tangent space normal map texel, scaled, into world space. Falls back to the surface normal when the result is bad.
// Z is rebuilt from X and Y, so two channel maps such as BC5 read the same as three channel ones.
vec3 Prowl_ApplyNormalMap(vec3 texel, vec3 normal, vec3 tangent, vec3 bitangent, float scale)
{
    vec3 normalTS;
    normalTS.xy = texel.xy * 2.0 - 1.0;
    normalTS.z = sqrt(max(0.0, 1.0 - dot(normalTS.xy, normalTS.xy)));
    normalTS.xy *= scale;
    vec3 n = mat3(normalize(tangent), normalize(bitangent), normalize(normal)) * normalTS;
    float len2 = dot(n, n);
    return len2 > 1e-8 ? n * inversesqrt(len2) : normalize(normal);
}
