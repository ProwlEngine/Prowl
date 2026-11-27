// What a transmissive PBR surface lets through from behind it, for its Transmission pass. The base lobes' share is
// PBR_Transmittance, transmission_weight of the dielectric base under 1 - E_spec, through the coat and fuzz. This
// file finds the light that share carries.
// FULL tier: the opaque view behind, refracted at the base's IOR into the volume and sampled from SceneColor where the
// refracted ray leaves it, at a mip as wide as the refracted lobe. The path through the volume runs from the front face
// to the view's back depth along the refracted ray, cut short by the opaque surface behind. Off screen the probes along
// the refracted direction stand in. Thin walled surfaces pass light straight through. Dispersion refracts each channel
// at its own IOR, Cauchy's fit through the Abbe number at 656.3, 587.6 and 486.1 nm.
// The volume: mu_t = -ln(transmission_color) / depth, mu_s = transmission_scatter / depth, mu_a = mu_t - mu_s shifted
// up by its lowest channel when that is negative, extinction mu_a + mu_s. Light reaching the far side unscattered is
// exp(-extinction L), scattered (1 - exp(-extinction L)) mu_s / extinction, the latter from a blurrier mip toward a
// direction transmission_scatter_anisotropy leans from straight through to the refracted ray. transmission_depth 0
// tints the refracted light by transmission_color instead, see PBR_Prepare.
// LITE tier: no screen reads, the probes along the refracted direction through a volume of transmission_depth.
#pragma once

#include "Graphite/PBR/Closure.glsl"
#include "Graphite/SceneDepth.glsl"

layout(set = 1, binding = 41) uniform texture2DArray Graphite_BackDepth;

// The far side's distance along the pixel's view ray, 0 where no back face was drawn
float Graphite_FetchBackDepth(vec2 screenUV)
{
    ivec2 size = textureSize(Graphite_BackDepth, 0).xy;
    ivec2 pixel = clamp(ivec2(screenUV * vec2(size)), ivec2(0), size - 1);
    return texelFetch(Graphite_BackDepth, ivec3(pixel, gl_ViewIndex), 0).r;
}

// The light leaving the back of a transmissive surface toward the view, before PBR_Transmittance
struct PBR_Transmitted
{
    vec3  light;             // from behind
    vec3  throughput;        // what the volume passes of a uniformly white background
    float pathLength;        // the refracted ray's length inside the volume
};

// A volume's extinction and single scattering albedo per unit length
void PBR_VolumeCoefficients(PBR_Params p, out vec3 extinction, out vec3 albedo)
{
    float depth = max(p.transmissionDepth, 1e-6);
    vec3 muT = -log(clamp(p.transmissionColor, 1e-6, 1.0)) / depth;
    vec3 muS = max(p.transmissionScatter, 0.0) / depth;
    vec3 muA = muT - muS;
    float lowest = min(muA.r, min(muA.g, muA.b));
    if (lowest < 0.0)
        muA -= lowest;
    extinction = muA + muS;
    albedo = muS / max(extinction, vec3(1e-8));
}

// The IOR of each channel's wavelength from the IOR at 587.6 nm, Cauchy A + B / lambda^2 with lambda in micrometres
vec3 PBR_DispersedEta(float eta, float scale, float abbe)
{
    float B = (eta - 1.0) * scale / (max(abbe, 1e-4) * 1.9105);
    float A = eta - B / (0.5876 * 0.5876);
    return A + B / (vec3(0.6563, 0.5876, 0.4861) * vec3(0.6563, 0.5876, 0.4861));
}

// How far a lobe of spread (radians) around dir widens over the distance lever, as a SceneColor mip seen from target
float pbr_RefractionLod(vec3 target, vec3 dir, float spread, float lever)
{
    vec3 side = normalize(cross(dir, abs(dir.y) < 0.99 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0)));
    vec2 a = Graphite_RelativeToScreenUV(target);
    vec2 b = Graphite_RelativeToScreenUV(target + side * (spread * lever));
    float pixels = length((b - a) * Graphite_View().screen.xy);
    return clamp(log2(max(2.0 * pixels, 1.0)), 0.0, Graphite_SceneColorMips() - 1.0);
}

// The probes along dir for a lobe of GGX alpha, black before the sky is captured
vec3 pbr_ProbesAlong(Graphite_ProbeSet probes, vec3 dir, float alpha)
{
    return Graphite_SampleProbes(probes, dir, clamp(alpha, PBR_MIN_ALPHA, 1.0));
}

#ifndef PBR_TIER_LITE
// The opaque view behind along dir from start, leaving the screen for the probes, lever the distance on to what it lands on
vec3 pbr_ViewBehind(Graphite_ProbeSet probes, vec3 start, vec3 dir, float alpha, float lever)
{
    vec2 uv = Graphite_RelativeToScreenUV(start);
    if (any(lessThan(uv, vec2(0.0))) || any(greaterThan(uv, vec2(1.0))))
        return pbr_ProbesAlong(probes, dir, alpha);
    return Graphite_SampleSceneColor(uv, pbr_RefractionLod(start + dir * lever, dir, alpha, lever));
}

// Distances behind past this count as this far, so sky and empty pixels do not blur to the last mip
#define PBR_FAR_LEVER 100.0

#endif

// The direction light refracts into the base along, from the view
vec3 PBR_RefractedDirection(PBR_Closure c, float eta)
{
    vec3 R = refract(-c.view, c.normal, 1.0 / max(eta, 1.0));
    return dot(R, R) > 0.5 ? R : -c.view;
}

// The refracted lobe's GGX alpha: microfacets tilted by alpha turn the ray by alpha (1 - 1 / eta) going in and by
// alpha (eta - 1) coming out of the far face, a thin wall's or a volume's
float PBR_RefractedAlpha(PBR_Closure c)
{
    float eta = max(c.refractionEta, 1.0);
    return sqrt(c.alpha2) * ((1.0 - 1.0 / eta) + (eta - 1.0));
}

// The reactive mask's coverage: geometry_opacity where the surface refracts the view behind it, which moves whatever its
// motion says, else geometry_opacity times transmission_weight
float PBR_ReactiveCoverage(PBR_Closure c, PBR_Params p)
{
#if defined(PBR_THIN_WALLED) || defined(PBR_TIER_LITE)
    return c.opacity * clamp(p.transmissionWeight, 0.0, 1.0);
#else
    return c.opacity;
#endif
}

// probes those PBR_FindProbes found for the closure
PBR_Transmitted PBR_GatherTransmitted(PBR_Closure c, PBR_Params p, Graphite_ProbeSet probes)
{
    PBR_Transmitted t;
    t.throughput = vec3(1.0);
    t.pathLength = 0.0;
    float alpha = PBR_RefractedAlpha(c);

#ifdef PBR_THIN_WALLED
    vec3 R = -c.view;
#else
    vec3 R = PBR_RefractedDirection(c, c.refractionEta);
    float muT = max(-dot(R, c.normal), 1e-4);
#endif

#ifdef PBR_TIER_LITE
#ifndef PBR_THIN_WALLED
    t.pathLength = max(p.transmissionDepth, 0.0) / muT;
#endif
    vec3 unscattered = pbr_ProbesAlong(probes, R, alpha);
#else
    vec2 uv = Graphite_ScreenUV();
    vec3 ray = Graphite_PixelRay();
    float front = dot(c.position, ray);
    float sceneDepth = Graphite_FetchSceneDepth(uv);
    float behind = sceneDepth > 0.0 ? clamp(dot(Graphite_SceneRelativePosition(uv), ray) - front, 0.0, PBR_FAR_LEVER) : PBR_FAR_LEVER;
    float travel = 0.0;
#ifndef PBR_THIN_WALLED
    float back = Graphite_FetchBackDepth(uv);
    if (back > front)
        travel = (min(back, front + behind) - front) * abs(dot(c.geomNormal, ray)) / muT;
    t.pathLength = back > front ? travel : max(p.transmissionDepth, 0.0) / muT;
#endif
    vec3 exit = c.position + R * travel;
    vec3 unscattered;
#ifndef PBR_THIN_WALLED
    if (p.transmissionDispersionScale > 0.0)
    {
        vec3 eta = PBR_DispersedEta(c.refractionEta, p.transmissionDispersionScale, p.transmissionDispersionAbbeNumber);
        for (int k = 0; k < 3; k++)
        {
            vec3 Rk = PBR_RefractedDirection(c, eta[k]);
            vec3 exitK = c.position + Rk * (travel * muT / max(-dot(Rk, c.normal), 1e-4));
            unscattered[k] = pbr_ViewBehind(probes, exitK, Rk, alpha, behind)[k];
        }
    }
    else
#endif
        unscattered = pbr_ViewBehind(probes, exit, R, alpha, behind);
#endif

    t.light = unscattered;
#ifndef PBR_THIN_WALLED
    if (p.transmissionDepth > 0.0)
    {
        vec3 extinction, albedo;
        PBR_VolumeCoefficients(p, extinction, albedo);
        vec3 direct = exp(-extinction * t.pathLength);
        vec3 scattered = (1.0 - direct) * albedo;
        t.throughput = direct + scattered;
        t.light = unscattered * direct;
        if (max(scattered.r, max(scattered.g, scattered.b)) > 0.0)
        {
            float g = clamp(p.transmissionScatterAnisotropy, -1.0, 1.0);
            float forward = max(g, 0.0);
            vec3 dir = normalize(mix(-c.normal, R, forward));
            float spread = mix(1.0, alpha, forward);
#ifdef PBR_TIER_LITE
            t.light += pbr_ProbesAlong(probes, dir, spread) * scattered;
#else
            t.light += pbr_ViewBehind(probes, c.position + dir * travel, dir, spread, travel + behind) * scattered;
#endif
        }
    }
#endif
    return t;
}
