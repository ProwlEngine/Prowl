// Fills a rectangle of one level of the terrain's material cache, see Prowl.Graphite.Rendering.TerrainCache. Each texel
// finds the finest resident page under it through the page table, gathers that page's control samples and blends the
// layers there, then stores albedo with its square root taken and occlusion times the page's, and the world normal's x
// and z with roughness and the strongest layer. Texels wrap around the level, so a level that moves only fills the rows
// and columns it gained.
Compute
{
    layout(local_size_x = 8, local_size_y = 8) in;

    layout(rgba8) uniform writeonly image2DArray Terrain_CacheAlbedo;
    layout(rgba8) uniform writeonly image2DArray Terrain_CacheSurface;
    uniform sampler2DArray Terrain_PageTable;
    uniform sampler2DArray _TerrainHeights;
    uniform sampler2DArray _TerrainNormals;

    #include "Graphite/Terrain/TerrainLayers.glsl"

    layout(push_constant) uniform Push
    {
        vec4 rect;      // the first texel on the level along x and z, the width and height
        vec4 level;     // the texel size in metres, the level's slice, texels a side
        vec4 terrain;   // the height of a 0 sample, metres to a 1 sample, the finest spacing, the page levels
    } push;

    void main()
    {
        ivec2 id = ivec2(gl_GlobalInvocationID.xy);
        if (any(greaterThanEqual(id, ivec2(push.rect.zw))))
            return;
        ivec2 texel = ivec2(push.rect.xy) + id;
        float size = push.level.x;
        vec2 xz = (vec2(texel) + 0.5) * size;
        int side = int(push.level.z);
        ivec3 target = ivec3((texel % side + side) % side, int(push.level.y));

        // The finest resident page under the texel
        ivec2 tableSize = textureSize(Terrain_PageTable, 0).xy;
        float slice = -1.0;
        vec2 at = vec2(0.0);
        for (int pageLevel = 0; pageLevel < int(push.terrain.w); pageLevel++)
        {
            float spacing = push.terrain.z * exp2(float(pageLevel));
            vec2 onLevel = xz / spacing;
            ivec2 page = ivec2(floor(onLevel / 128.0));
            if (any(lessThan(page, ivec2(0))) || any(greaterThanEqual(page, tableSize)))
                continue;
            slice = texelFetch(Terrain_PageTable, ivec3(page, pageLevel), 0).r;
            if (slice >= 0.0)
            {
                at = onLevel - vec2(page) * 128.0;
                break;
            }
        }
        if (slice < 0.0)
        {
            imageStore(Terrain_CacheAlbedo, target, vec4(0.7, 0.7, 0.7, 1.0));
            imageStore(Terrain_CacheSurface, target, vec4(0.5, 0.5, 0.8, 0.0));
            return;
        }

        vec3 page = vec3((at + 1.5) / TERRAIN_PAGE_SIZE, slice);
        float height = textureLod(_TerrainHeights, page, 0.0).r * push.terrain.y + push.terrain.x;
        vec4 normalAo = textureLod(_TerrainNormals, page, 0.0);
        vec2 nxz = normalAo.xy * 2.0 - 1.0;
        vec3 n = normalize(vec3(nxz.x, sqrt(max(0.0, 1.0 - dot(nxz, nxz))), nxz.y));

        Terrain_Material m = Terrain_PlainMaterial(n);
        if (_TerrainSpacing.y > 0.5)
        {
            ivec4 ids = ivec4(-1);
            vec4 weights = vec4(0.0);
            Terrain_Gather(page, 1.0, ids, weights);
            vec3 dx = vec3(size, -n.x / max(n.y, 0.1) * size, 0.0);
            vec3 dy = vec3(0.0, -n.z / max(n.y, 0.1) * size, size);
            m = Terrain_Blend(ids, weights, vec3(xz.x, height, xz.y), dx, dy, n);
        }

        imageStore(Terrain_CacheAlbedo, target, vec4(sqrt(max(m.albedo, 0.0)), m.occlusion * normalAo.z));
        imageStore(Terrain_CacheSurface, target, vec4(m.normal.xz * 0.5 + 0.5, m.roughness, float(m.strongest) / 63.0));
    }
}
