// The farthest depth pyramid of occlusion culling: a group of 16 x 16 reduces a 64 x 64 tile of push.baseLevel to the
// six levels above it, level 0 being the depth itself. One dispatch from the depth builds levels 1 to 6, a second from
// level 6 the rest. Levels 1 to push.levels lie side by side in HiZ_Packed, see Graphite/Internal/OcclusionLevels.glsl.
// Every texel is the farthest reversed depth of the 2 x 2 below it, texels past a level's edge clamped to it.
Compute
{
    layout(local_size_x = 16, local_size_y = 16) in;

    #include "Graphite/Internal/OcclusionLevels.glsl"

    uniform sampler2D HiZ_Depth;
    layout(r32f) uniform image2D HiZ_Packed;
    layout(push_constant) uniform Push { ivec2 size; int baseLevel; int levels; } push;

    shared float s_farthest[16][16];

    float Load(ivec2 cell)
    {
        if (push.baseLevel == 0)
            return texelFetch(HiZ_Depth, min(cell, push.size - 1), 0).r;
        ivec2 size = Occlusion_LevelSize(push.size, push.baseLevel);
        return imageLoad(HiZ_Packed, min(cell, size - 1) + ivec2(Occlusion_LevelOffset(push.size.x, push.baseLevel), 0)).r;
    }

    void Store(int level, ivec2 cell, float depth)
    {
        if (level <= push.levels && all(lessThan(cell, Occlusion_LevelSize(push.size, level))))
            imageStore(HiZ_Packed, cell + ivec2(Occlusion_LevelOffset(push.size.x, level), 0), vec4(depth));
    }

    void main()
    {
        ivec2 local = ivec2(gl_LocalInvocationID.xy);
        ivec2 tile = ivec2(gl_WorkGroupID.xy);
        int base = push.baseLevel;

        // Each thread a 4 x 4 of the base level: four texels of the level above, one of the next
        ivec2 corner = tile * 64 + local * 4;
        float farthest = 1.0;
        for (int q = 0; q < 4; q++)
        {
            ivec2 p = corner + ivec2(q & 1, q >> 1) * 2;
            float quad = min(min(Load(p), Load(p + ivec2(1, 0))), min(Load(p + ivec2(0, 1)), Load(p + ivec2(1, 1))));
            Store(base + 1, p >> 1, quad);
            farthest = min(farthest, quad);
        }
        Store(base + 2, corner >> 2, farthest);
        s_farthest[local.y][local.x] = farthest;

        // The four levels after through shared memory, a quarter of the threads each time
        int width = 8;
        for (int level = base + 3; level <= base + 6; level++)
        {
            barrier();
            bool working = all(lessThan(local, ivec2(width)));
            float depth = 1.0;
            if (working)
            {
                ivec2 s = local * 2;
                depth = min(min(s_farthest[s.y][s.x], s_farthest[s.y][s.x + 1]), min(s_farthest[s.y + 1][s.x], s_farthest[s.y + 1][s.x + 1]));
            }
            barrier();
            if (working)
            {
                s_farthest[local.y][local.x] = depth;
                Store(level, tile * width + local, depth);
            }
            width >>= 1;
        }
    }
}
