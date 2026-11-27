// An eye lit as two surfaces in one, for the Eye surface model: the cornea, smooth and bulging, takes the highlight
// from the surface's normal, and the iris under it takes its diffuse light from a normal of its own, dished inward, so
// the side of the iris away from a light catches it as light gathered by the cornea does. The white of the eye is lit
// as the Lit surface. Included after Graphite/PBR/Closure.glsl.
#pragma once

// iris is how much of the pixel is iris, irisNormal the iris's own normal
vec3 PBR_EyeLight(PBR_Closure c, Graphite_LightContribution l, vec3 irisNormal, float iris)
{
    vec3 white = PBR_EvaluateLight(c, l);
    if (iris <= 0.0)
        return white;
    vec3 specular = l.shareSpecular ? c.specularAlbedo : PBR_EvaluateSpecular(c, l.specularL);
    float facing = max(dot(irisNormal, l.diffuseL), 0.0);
    vec3 under = (c.diffuseSingle * (l.attenuation * facing) + specular * l.specular) * l.radiance;
    return mix(white, under, clamp(iris, 0.0, 1.0));
}
