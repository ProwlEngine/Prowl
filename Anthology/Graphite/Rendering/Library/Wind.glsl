// The world's wind, EnvironmentDesc.Wind: the way it blows across the ground, its strength and how fast it gusts.
// Foliage, cloth and anything else bends with Graphite_WindSway so they move together.
#pragma once

#include "Graphite/Core.glsl"
#include "Graphite/WorldLighting.glsl"

vec4 Graphite_Wind() { return graphite_World.wind; }

// How far the wind pushes a point at a camera relative position at a time, along its way across the ground, scaled by
// how much the point bends: 0 rooted, 1 a free tip. Gusts roll across the world, so neighbours move together.
vec3 Graphite_WindSway(vec3 relativePos, float time, float bend)
{
    vec4 wind = graphite_World.wind;
    if (wind.z <= 0.0 || bend <= 0.0)
        return vec3(0.0);
    Graphite_ViewData view = Graphite_View();
    vec2 world = view.cameraHigh.xz + (view.cameraLow.xz + relativePos.xz);
    float along = dot(world, wind.xy);
    float phase = along * 0.08 - time * wind.w * 1.7 + sin(dot(world, vec2(0.13, 0.11)) + time * wind.w) * 0.6;
    float gust = 0.55 + 0.45 * sin(phase) * (0.75 + 0.25 * sin(phase * 0.37 + 1.3));
    return vec3(wind.x, 0.0, wind.y) * (wind.z * gust * bend);
}
