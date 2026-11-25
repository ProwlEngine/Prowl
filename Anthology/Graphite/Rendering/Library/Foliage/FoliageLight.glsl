// The Foliage model's light, shared by its forward pass and the deferred lighting pass. Included after Graphite/PBR/Closure.glsl.
#pragma once

// The PBR light with light from behind passing through, tinted by the diffuse albedo and tint, by amount, wrapped around the far
// side and brightest looking toward the light
vec3 Foliage_Light(PBR_Closure c, Graphite_LightContribution l, vec3 tint, float amount)
{
    vec3 front = PBR_EvaluateLight(c, l);
    float behind = max(dot(-c.normal, l.diffuseL), 0.0) * 0.6 + 0.4 * max(-dot(c.geomNormal, l.diffuseL), 0.0);
    float toward = pow(max(dot(-c.view, l.diffuseL), 0.0), 4.0);
    vec3 through = c.diffuseAlbedo * tint * (amount * (behind + toward) * l.attenuation);
    return front + through * l.radiance;
}
