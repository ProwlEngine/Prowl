#ifndef SHADER_CLOUDS
#define SHADER_CLOUDS

// =============================================================
//  Volumetric cloud density, shared by the cloud passes
// =============================================================

uniform sampler2D _CloudCoverage;
uniform sampler2D _CloudNoise2D;
uniform sampler3D _CloudNoise3D;
uniform sampler2D _CloudSunDepth;
uniform vec4 _CloudCoverageRect; // min x, min z, 1 / span, span
uniform vec4 _CloudShape;        // base altitude, thickness, 1 / thickness, planet radius
uniform vec4 _CloudPattern;      // 1 / pattern scale, coverage, 1 / detail scale, erosion
uniform vec2 _CloudWind;
uniform vec3 _CloudToSun;        // toward the sun, straight up when the scene has none
uniform float _CloudErosionSize; // texels along each side of the 3D noise

// The clouds draw in two passes that only add up, so draw order cannot matter. The first adds up moments of how much
// each sample hides at its distance, the second lights the samples and adds each one dimmed by what the moments say
// lies in front of it
uniform int _CloudMomentPass;      // 1 while adding up the moments
uniform sampler2D _CloudMoments0;  // optical depth, times depth, times depth squared
uniform sampler2D _CloudMoments1;  // times depth cubed, times depth to the fourth
uniform vec2 _CloudMomentRange;    // log of the nearest and furthest distance the moments span

float CloudMomentDepth(float dist)
{
    return clamp((log(max(dist, 1.0)) - _CloudMomentRange.x) / (_CloudMomentRange.y - _CloudMomentRange.x), 0.0, 1.0) * 2.0 - 1.0;
}

// The pass blends One and OneMinusSrcAlpha, so a zero alpha adds
void CloudMoments(float alpha, float dist, out vec4 outFirst, out vec4 outSecond)
{
    float tau = -log(1.0 - min(alpha, 0.9999));
    float z = CloudMomentDepth(dist);
    float z2 = z * z;
    outFirst = vec4(tau, tau * z, tau * z2, 0.0);
    outSecond = vec4(tau * z2 * z, tau * z2 * z2, 0.0, 0.0);
}

// How much light gets through everything in front of depth z, rebuilt from the optical depth and four power moments
float CloudMomentTransmittance(vec3 first, vec2 second, float z)
{
    float b0 = first.x;
    if (b0 < 1e-5) return 1.0;
    vec4 b = vec4(first.y, first.z, second.x, second.y) / b0;
    b = mix(b, vec4(0.0, 0.375, 0.0, 0.375), 5e-7);

    // Cholesky factorisation of the moment matrix
    float l21d11 = b.z - b.x * b.y;
    float d11 = b.y - b.x * b.x;
    float l21 = l21d11 / d11;
    float d22 = b.w - b.y * b.y - l21d11 * l21;

    // The polynomial whose roots, with z, place the three points the absorbance is spread over
    vec3 c = vec3(1.0, z, z * z);
    c.y -= b.x;
    c.z -= b.y + l21 * c.y;
    c.y /= d11;
    c.z /= d22;
    c.y -= l21 * c.z;
    c.x -= dot(c.yz, b.xy);
    float p = c.y / c.z, q = c.x / c.z;
    float r = sqrt(max(p * p * 0.25 - q, 0.0));
    float z1 = -p * 0.5 - r, z2 = -p * 0.5 + r;

    // Each point counts fully when in front of z, and a quarter at z itself
    float f0 = 0.25, f1 = z1 < z ? 1.0 : 0.0, f2 = z2 < z ? 1.0 : 0.0;
    float f01 = (f1 - f0) / (z1 - z);
    float f12 = (f2 - f1) / (z2 - z1);
    float f012 = (f12 - f01) / (z2 - z);
    vec3 poly;
    poly.z = f012;
    poly.y = f01 - f012 * z1;
    poly.x = f0 - poly.y * z;
    poly.y = poly.y - poly.z * z;
    float absorbance = poly.x + dot(b.xy, poly.yz);
    float transmittance = clamp(exp(-b0 * absorbance), 0.0, 1.0);
    return isnan(transmittance) ? 1.0 : transmittance;
}

// Adds one lit cloud sample of the given colour, coverage and distance, dimmed by what lies in front of it
void CloudOutput(vec3 color, float alpha, float dist, ivec2 pixel, out vec4 outColor, out vec4 outDistance)
{
    vec3 first = texelFetch(_CloudMoments0, pixel, 0).rgb;
    vec2 second = texelFetch(_CloudMoments1, pixel, 0).rg;
    float weight = alpha * CloudMomentTransmittance(first, second, CloudMomentDepth(dist));
    outColor = vec4(color * weight, 0.0);
    outDistance = vec4(dist * 0.001 * weight, weight, 0.0, 0.0);
}

// How much cloud the weather pattern puts at a point of the ground plane: broad patches broken into lumps, what the
// coverage map stores around the camera
float CloudCoverageNoise(vec2 xz)
{
    vec2 q = (xz - _CloudWind) * _CloudPattern.x;
    float n = texture(_CloudNoise2D, q).r * 0.7 + texture(_CloudNoise2D, q * 3.0).g * 0.3;
    float threshold = 1.0 - _CloudPattern.y;
    return smoothstep(threshold - 0.02, threshold + 0.12, n);
}

// Where a ray from cameraHeight meets the sphere at altitude around the planet's centre, or -1. Written so neither
// root loses its precision to the planet's size
float ShellDistance(vec3 dir, float cameraHeight, float altitude)
{
    float planet = _CloudShape.w;
    float b = (cameraHeight + planet) * dir.y;
    float c = (cameraHeight - altitude) * (cameraHeight + altitude + 2.0 * planet);
    float disc = b * b - c;
    if (disc < 0.0) return -1.0;
    float q = -(b + (b >= 0.0 ? 1.0 : -1.0) * sqrt(disc));
    float t0 = q, t1 = abs(q) > 1e-6 ? c / q : q;
    if (c < 0.0) return max(t0, t1);
    float nearest = min(t0, t1);
    return nearest > 0.0 ? nearest : -1.0;
}

float HenyeyGreenstein(float cosTheta, float g)
{
    float g2 = g * g;
    return (1.0 - g2) / (4.0 * 3.14159265 * pow(max(1.0 + g2 - 2.0 * g * cosTheta, 1e-4), 1.5));
}

// Sunlight reaching a point behind tau of optical depth, scattered toward the viewer. Light scatters many times inside
// a cloud: each order reaches further in, is dimmer and scatters less forward, so light soaks deep into thick cloud
// instead of dying a few hundred meters in
float CloudScattering(float tau, float cosTheta, float silverLining)
{
    float transmitted = 0.0, weight = 1.0, reach = 1.0, forward = 1.0;
    for (int order = 0; order < 3; order++)
    {
        float lobes = mix(HenyeyGreenstein(cosTheta, 0.2 * forward), HenyeyGreenstein(cosTheta, 0.85 * forward), silverLining * 0.6);
        // Never below the light's own energy, whatever the angle
        float phase = max(lobes * 4.0 * 3.14159265, 0.5);
        transmitted += weight * exp(-tau * reach) * phase;
        weight *= 0.5;
        reach *= 0.4;
        forward *= 0.5;
    }
    return transmitted;
}

vec2 CloudMapUV(vec2 xz)
{
    return (xz - _CloudCoverageRect.xy) * _CloudCoverageRect.z;
}

float CloudCoverageAt(vec2 xz)
{
    return textureLod(_CloudCoverage, CloudMapUV(xz), 0.0).r;
}

// Coverage shaped by height: a rounded base, and tops that climb higher where the cloud is dense
float CloudShape(vec3 p, out float height)
{
    height = (p.y - _CloudShape.x) * _CloudShape.z;
    if (height <= 0.0 || height >= 1.0) return 0.0;
    float coverage = CloudCoverageAt(p.xz);
    float top = mix(0.35, 1.0, coverage);
    float shape = coverage * smoothstep(0.0, 0.25, height) * (1.0 - smoothstep(top * 0.55, top, height));

    // Denser toward the base, which gives heavier, darker bottoms
    float below = 1.0 - height;
    return shape * (1.0 + 1.5 * below * below * below * below);
}

// Detail noise eats into the shape, most where it is thin, so edges break up and cores stay solid. Both octaves come
// from one fetch, the fine one only where it can be seen
float Erode(vec3 p, float shape, bool fine, float lod)
{
    if (_CloudPattern.w <= 0.0) return shape;
    vec3 q = vec3(p.x - _CloudWind.x, p.y, p.z - _CloudWind.y) * _CloudPattern.z;
    vec2 octaves = textureLod(_CloudNoise3D, q, lod).rg;
    float cut = (fine ? octaves.r * 0.65 + octaves.g * 0.35 : octaves.r) * _CloudPattern.w;
    return clamp((shape - cut) / max(1.0 - cut, 1e-3), 0.0, 1.0);
}

// How much cloud lies between p and the sun, in density times meters. Steps grow outward, and however many there are
// they reach the same distance, a little over the layer's thickness
float MarchToSun(vec3 p, vec3 toSun, int samples, float lod)
{
    const float growth = 1.3;
    float reach = _CloudShape.y * 1.1;
    float stride = reach * (growth - 1.0) / (pow(growth, float(samples)) - 1.0);
    float travelled = 0.0, optical = 0.0;
    for (int i = 0; i < samples; i++)
    {
        vec3 q = p + toSun * (travelled + stride * 0.5);
        float h;
        float s = CloudShape(q, h);
        if (s > 0.0) optical += Erode(q, s, false, lod) * stride;
        travelled += stride;
        stride *= growth;
    }
    return optical;
}

// The sun map holds MarchToSun from four heights through the layer, one per channel, so a point reads it at its
// place on the ground and blends the two heights around it
float SunDepthAt(vec3 p)
{
    vec4 depths = textureLod(_CloudSunDepth, CloudMapUV(p.xz), 0.0);
    float slot = clamp((p.y - _CloudShape.x) * _CloudShape.z * 4.0 - 0.5, 0.0, 3.0);
    vec4 weights = max(1.0 - abs(vec4(slot) - vec4(0.0, 1.0, 2.0, 3.0)), 0.0);
    return dot(depths, weights);
}

#endif
