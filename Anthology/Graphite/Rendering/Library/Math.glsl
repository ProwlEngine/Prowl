// Constants and small helpers the whole library shares
#pragma once

#define GRAPHITE_PI 3.14159265359
#define GRAPHITE_TWO_PI 6.28318530718
#define GRAPHITE_HALF_PI 1.57079632679
#define GRAPHITE_INV_PI 0.31830988618
#define GRAPHITE_INV_TWO_PI 0.15915494309

// The lowest GGX alpha a lobe takes, below which its peak would overflow
#define GRAPHITE_MIN_ALPHA 0.002

// The Rec.709 luminance of a linear colour
float Graphite_Luminance(vec3 c) { return dot(c, vec3(0.2126, 0.7152, 0.0722)); }
vec3 Graphite_ToYCoCg(vec3 rgb) { return vec3(0.25 * rgb.r + 0.5 * rgb.g + 0.25 * rgb.b, 0.5 * rgb.r - 0.5 * rgb.b, -0.25 * rgb.r + 0.5 * rgb.g - 0.25 * rgb.b); }
vec3 Graphite_FromYCoCg(vec3 c) { return vec3(c.x + c.y - c.z, c.x + c.z, c.x - c.y - c.z); }

// Colour clamped to the largest half float, so a pre-exposed light too bright for a half float target saturates
vec3 Graphite_SaturateHalf(vec3 c) { return min(c, vec3(65504.0)); }

// A well mixed hash of a word, PCG's output step
uint Graphite_Hash(uint x)
{
    uint state = x * 747796405u + 2891336453u;
    uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
    return (word >> 22u) ^ word;
}

// A cosine distributed direction around a unit normal from two numbers 0 to 1
vec3 Graphite_CosineDirection(vec3 normal, float u, float v)
{
    float z = u * 2.0 - 1.0;
    float phi = v * GRAPHITE_TWO_PI;
    vec3 sphere = vec3(sqrt(max(1.0 - z * z, 0.0)) * vec2(cos(phi), sin(phi)), z);
    return normalize(normal + sphere * 0.999);
}
