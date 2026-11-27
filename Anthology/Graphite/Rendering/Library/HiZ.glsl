// The view's nearest depth pyramid, as HiZFeature publishes it under RendererOutputs.HiZ for features whose ReadsHiZ
// is set. Each texel of a level holds the nearest, reversed, depth of the 2 x 2 below it. Levels 1 to
// GRAPHITE_HIZ_LEVELS lie side by side in one texture, level L at an x offset of padded - (padded >> (L - 1)), padded
// the view's width rounded up to 32. Level 0 is the depth itself. Bind both with point sampling.
#pragma once

#define GRAPHITE_HIZ_LEVELS 5

// The nearest depth of a cell of a level, 0 off the level, which only rays off screen reach
float Graphite_HiZ(GRAPHITE_SCREEN_TEXTURE depth, GRAPHITE_SCREEN_TEXTURE pyramid, vec2 screen, int level, ivec2 cell)
{
    ivec2 size = (ivec2(screen) + (1 << level) - 1) >> level;
    if (any(lessThan(cell, ivec2(0))) || any(greaterThanEqual(cell, size)))
        return 0.0;
    if (level == 0)
        return Graphite_Fetch(depth, cell, 0).r;
    int padded = (int(screen.x) + 31) & ~31;
    return Graphite_Fetch(pyramid, cell + ivec2(padded - (padded >> (level - 1)), 0), 0).r;
}

// Marches a ray from p0 to p1, in pixels, its reversed depth going from z0 to z1 along it, as a straight ray's does on
// screen. It climbs to coarser cells while the ray stays nearer than everything in them and comes down to pixels where
// it may pass behind something, starting at t, 0 to 1 along the ray. Returns whether it found a pixel the ray reaches or
// passes behind before the screen's edge, with t, the pixel and its depth there. Else t is where it stopped.
bool Graphite_HiZMarch(GRAPHITE_SCREEN_TEXTURE depth, GRAPHITE_SCREEN_TEXTURE pyramid, vec2 screen, vec2 p0, vec2 p1,
    float z0, float z1, int steps, inout float t, out ivec2 pixel, out float nearest)
{
    pixel = ivec2(p0);
    nearest = 0.0;
    vec2 d = p1 - p0;
    float pixels = length(d);
    if (pixels < 1.0)
        return false;

    // Where the ray leaves the screen, along it
    float leave = 1.0;
    for (int a = 0; a < 2; a++)
    {
        if (d[a] > 1e-6)
            leave = min(leave, (screen[a] - 0.01 - p0[a]) / d[a]);
        else if (d[a] < -1e-6)
            leave = min(leave, (0.01 - p0[a]) / d[a]);
    }
    vec2 inverse = vec2(abs(d.x) > 1e-6 ? 1.0 / d.x : 1e30, abs(d.y) > 1e-6 ? 1.0 / d.y : 1e30);
    float nudge = 0.01 / pixels;
    int level = 0;
    for (int i = 0; i < steps && t < leave; i++)
    {
        vec2 p = p0 + d * t;
        ivec2 cell = ivec2(p) >> level;
        vec2 low = vec2(cell << level);
        vec2 bound = low + step(vec2(0.0), d) * float(1 << level);
        vec2 exits = (bound - p0) * inverse;
        float exit = min(max(exits.x, t), max(exits.y, t)) + nudge;
        float z = mix(z0, z1, t), zExit = mix(z0, z1, min(exit, leave));
        float cellNearest = Graphite_HiZ(depth, pyramid, screen, level, cell);
        if (min(z, zExit) > cellNearest)
        {
            t = exit;
            level = min(level + 1, GRAPHITE_HIZ_LEVELS);
            continue;
        }
        if (level > 0)
        {
            level--;
            continue;
        }
        pixel = cell;
        nearest = cellNearest;
        return true;
    }
    t = min(t, leave);
    return false;
}
