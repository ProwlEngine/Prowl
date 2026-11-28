// Where the levels of the farthest depth pyramid of occlusion culling lie in their one texture: level L, 1 and up, is
// the screen's size halved L times rounding up, and sits at x after every level before it. Shared by
// Graphite/Internal/OcclusionHiZ.glsl and CullLate.glsl, and OcclusionHiZFeature on the host.
#pragma once

ivec2 Occlusion_LevelSize(ivec2 screen, int level) { return (screen + (1 << level) - 1) >> level; }

int Occlusion_LevelOffset(int screenWidth, int level)
{
    int offset = 0;
    for (int k = 1; k < level; k++)
        offset += (screenWidth + (1 << k) - 1) >> k;
    return offset;
}
