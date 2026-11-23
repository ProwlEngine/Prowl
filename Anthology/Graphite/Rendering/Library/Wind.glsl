// The world's wind, EnvironmentDesc.Wind: the way it blows across the ground, its strength and how fast it gusts, and
// its downwash zones, EnvironmentDesc.WindZones. Foliage, cloth and anything else bends with Graphite_WindSway so they
// move together.
#pragma once

#include "Graphite/Core.glsl"
#include "Graphite/WorldLighting.glsl"

vec4 Graphite_Wind() { return graphite_World.wind; }

// A downwash zone's push across the ground at a point from its centre: calm in the eye, peaking where the column
// spreads, dying at the rim, with gust fronts rolling outward. The same profile as Prowl's WindZone.SampleWind.
vec2 graphite_WindZone(vec3 toPoint, float radius, vec4 p, float time)
{
    float across = length(toPoint.xz);
    float height = abs(toPoint.y);
    if (across >= radius || height >= radius)
        return vec2(0.0);
    float vertical = 1.0 - smoothstep(0.0, radius, height);
    float r = across / radius;
    vec2 outward = across > 1e-4 ? toPoint.xz / across : vec2(0.0);
    float outflow = smoothstep(0.0, 0.22, r) * (1.0 - smoothstep(0.35, 1.0, r));
    float ring = r * 3.0 - time * 0.35 * (1.0 + p.x);
    float front = sin(outward.x * 2.5 + ring) * cos(outward.y * 2.5);
    float wobble = sin(outward.y * 2.5 + ring * 1.3 + 17.0) * cos(outward.x * 2.5);
    float gust = 1.0 + front * p.y + sin((time * p.w - r * 2.0) * 6.28318531) * p.z;
    float twist = wobble * p.y * 0.8;
    float c = cos(twist), s = sin(twist);
    vec2 flow = vec2(outward.x * c - outward.y * s, outward.x * s + outward.y * c);
    return flow * max(p.x * vertical * gust, 0.0) * outflow;
}

// How far the wind pushes a point at a camera relative position at a time, along its way across the ground, scaled by
// how much the point bends: 0 rooted, 1 a free tip. Gusts roll across the world, so neighbours move together.
vec3 Graphite_WindSway(vec3 relativePos, float time, float bend)
{
    if (bend <= 0.0)
        return vec3(0.0);
    vec4 wind = graphite_World.wind;
    Graphite_ViewData view = Graphite_View();
    vec2 push = vec2(0.0);
    if (wind.z > 0.0)
    {
        vec2 world = view.cameraHigh.xz + (view.cameraLow.xz + relativePos.xz);
        float along = dot(world, wind.xy);
        float phase = along * 0.08 - time * wind.w * 1.7 + sin(dot(world, vec2(0.13, 0.11)) + time * wind.w) * 0.6;
        float gust = 0.55 + 0.45 * sin(phase) * (0.75 + 0.25 * sin(phase * 0.37 + 1.3));
        push = wind.xy * (wind.z * gust);
    }
    for (uint z = 0u; z < graphite_World.counts.w; z++)
    {
        vec3 centre = (graphite_World.windZoneHigh[z].xyz - view.cameraHigh.xyz) + (graphite_World.windZoneLow[z].xyz - view.cameraLow.xyz);
        push += graphite_WindZone(relativePos - centre, graphite_World.windZoneHigh[z].w, graphite_World.windZoneParams[z], time);
    }
    return vec3(push.x, 0.0, push.y) * bend;
}
