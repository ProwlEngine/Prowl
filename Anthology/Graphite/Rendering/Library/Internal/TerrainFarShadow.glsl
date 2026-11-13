// Fills rows of the terrain's far shadow heights, see Prowl.Graphite.Rendering.TerrainFarShadow. Each texel marches
// from its spot toward the sun over the resident pages, coarser further out, and keeps the highest the ground blocks the
// sun's rays at over it: the most any height h at distance t along the way stands above t times the sun's rise. Points
// below that are in shadow. With the sun at or under the horizon everything is.
Compute
{
    layout(local_size_x = 8, local_size_y = 8) in;

    layout(r32f) uniform writeonly image2D Terrain_FarShadow;
    uniform sampler2DArray Terrain_PageTable;
    uniform sampler2DArray _TerrainHeights;

    layout(push_constant) uniform Push
    {
        vec4 area;      // the first texel's corner along x and z in metres from the terrain's corner, the texel size, the first row
        vec4 sun;       // toward the sun across the ground along x and z, its rise per metre across, how far to march
        vec4 terrain;   // the height of a 0 sample, metres to a 1 sample, the finest spacing, the page levels
        vec4 place;     // the terrain's height in the world, texels a side, rows in this dispatch
    } push;

    // The ground's height at a point from the terrain's corner, from the page of the given level or the next coarser
    // one resident, -1e30 off the terrain
    float Height(vec2 xz, int level)
    {
        ivec2 tableSize = textureSize(Terrain_PageTable, 0).xy;
        for (int l = level; l < int(push.terrain.w); l++)
        {
            float spacing = push.terrain.z * exp2(float(l));
            vec2 onLevel = xz / spacing;
            ivec2 page = ivec2(floor(onLevel / 128.0));
            if (any(lessThan(page, ivec2(0))) || any(greaterThanEqual(page, tableSize)))
                return -1e30;
            float slice = texelFetch(Terrain_PageTable, ivec3(page, l), 0).r;
            if (slice < 0.0)
                continue;
            vec2 at = onLevel - vec2(page) * 128.0;
            return textureLod(_TerrainHeights, vec3((at + 1.5) / 131.0, slice), 0.0).r * push.terrain.y + push.terrain.x;
        }
        return -1e30;
    }

    void main()
    {
        ivec2 id = ivec2(gl_GlobalInvocationID.xy);
        int side = int(push.place.y);
        if (id.x >= side || id.y >= int(push.place.z))
            return;
        ivec2 texel = ivec2(id.x, id.y + int(push.area.w));
        float cell = push.area.z;
        vec2 xz = push.area.xy + (vec2(texel) + 0.5) * cell;

        float shadow = -1e30;
        if (push.sun.z <= 0.0)
            shadow = 1e30;
        else
        {
            float t = cell;
            while (t < push.sun.w)
            {
                int level = int(clamp(floor(log2(max(t * 0.02, cell * 0.5) / push.terrain.z)), 0.0, push.terrain.w - 1.0));
                shadow = max(shadow, Height(xz + push.sun.xy * t, level) - t * push.sun.z);
                t *= 1.08;
            }
        }
        imageStore(Terrain_FarShadow, texel, vec4(shadow + push.place.x));
    }
}
