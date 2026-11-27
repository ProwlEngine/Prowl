// Hair lit along its strands, for the Hair surface model: a fibre scatters light into three lobes around the cone the
// strand's direction makes with the light. R is light glancing off the fibre's surface, TT light passing through it,
// brightest looking toward the light, and TRT light reflected once inside and coming back coloured by the fibre.
// Each lobe is a Gaussian along the strand, wider with roughness and shifted by the tilt of the fibre's scales, times
// a fit to its spread around the strand. Light scattered between strands is a soft term of its own. Included after
// Graphite/PBR/Closure.glsl, the closure prepared as the Lit surface's, its base colour the hair's colour.
#pragma once

float pbr_HairLongitudinal(float width, float x)
{
    return exp(-0.5 * x * x / (width * width)) / (2.5066283 * width);
}

// strand is the fibre's direction, backlit how much light passes through the hair, scatter how much is scattered
// between strands
vec3 PBR_HairLight(PBR_Closure c, Graphite_LightContribution l, vec3 strand, float backlit, float scatter)
{
    vec3 L = l.diffuseL;
    vec3 V = c.view;
    vec3 color = c.metalF0;
    float sinL = clamp(dot(strand, L), -1.0, 1.0);
    float sinV = clamp(dot(strand, V), -1.0, 1.0);
    float cosL = sqrt(max(1.0 - sinL * sinL, 0.0));

    // The angle between light and view around the strand, and half the angle between them along it
    vec3 aroundL = L - strand * sinL;
    vec3 aroundV = V - strand * sinV;
    float cosPhi = dot(aroundL, aroundV) * inversesqrt(dot(aroundL, aroundL) * dot(aroundV, aroundV) + 1e-4);
    float cosHalfPhi = sqrt(clamp(0.5 + 0.5 * cosPhi, 0.0, 1.0));
    float cosDifference = sqrt(clamp(0.5 + 0.5 * (cosL * sqrt(max(1.0 - sinV * sinV, 0.0)) + sinL * sinV), 0.04, 1.0));

    float f0 = (c.dielectricEta - 1.0) / (c.dielectricEta + 1.0);
    f0 *= f0;
    float m = 1.0 - clamp(cosDifference * cosHalfPhi, 0.0, 1.0);
    float fresnel = f0 + (1.0 - f0) * m * m * m * m * m;

    float width = clamp(c.roughness, 0.05, 1.0);
    float along = sinL + sinV;
    const float tilt = 0.035;

    vec3 lobes = vec3(pbr_HairLongitudinal(width, along + 2.0 * tilt) * 0.25 * cosHalfPhi * fresnel);
    float through = (1.0 - fresnel) * (1.0 - fresnel);
    lobes += pbr_HairLongitudinal(width * 0.5, along - tilt) * exp(-3.65 * cosPhi - 3.98) * through * clamp(backlit, 0.0, 1.0)
        * pow(color, vec3(0.5 / cosDifference));
    lobes += pbr_HairLongitudinal(width * 2.0, along - 4.0 * tilt) * exp(17.0 * cosPhi - 16.78) * through * fresnel
        * pow(color, vec3(0.8 / cosDifference));

    // Between strands the light loses its direction and takes the hair's colour, reaching a little past the terminator
    float wrap = clamp((dot(c.normal, L) + 0.3) / 1.3, 0.0, 1.0);
    vec3 scattered = color * (clamp(scatter, 0.0, 1.0) * cosL * wrap / GRAPHITE_PI);

    return (lobes * (GRAPHITE_PI * cosL) + scattered) * l.attenuation * l.radiance;
}

vec3 PBR_HairAmbient(PBR_Closure c, PBR_Indirect i, float scatter)
{
    return c.metalF0 * (0.5 + 0.5 * clamp(scatter, 0.0, 1.0)) * i.irradiance + c.specularAlbedo * i.specular * 0.25;
}
