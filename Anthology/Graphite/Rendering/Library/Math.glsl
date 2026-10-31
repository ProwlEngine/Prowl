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

// Colour clamped to the largest half float, so a pre-exposed light too bright for a half float target saturates
vec3 Graphite_SaturateHalf(vec3 c) { return min(c, vec3(65504.0)); }
