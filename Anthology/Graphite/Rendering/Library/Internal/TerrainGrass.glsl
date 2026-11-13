// Grows one grass type around one view slot's camera, see Prowl.Graphite.Rendering.TerrainGrass. One lane per point of
// a jittered grid anchored in the world, so grass stays put as the camera moves. A point is kept by the weight of the
// layers growing this type's group under it, thinned toward the type's range with the survivors widened to keep coverage, left
// out over holes and outside the view, then appended to its slot's instances with its count.
Compute
{
    layout(local_size_x = 8, local_size_y = 8) in;

    layout(rgba32f) uniform writeonly image2D Terrain_GrassInstances;
    uniform sampler2D Terrain_GrassViews;
    uniform sampler2D Terrain_GrassTypes;
    layout(std430) buffer Terrain_GrassCounts { uint counts[]; };
    uniform sampler2DArray Terrain_PageTable;
    uniform sampler2DArray _TerrainHeights;

    #include "Graphite/Terrain/TerrainLayers.glsl"

    layout(push_constant) uniform Push
    {
        vec4 grid;      // the first cell along x and z, the cell size in metres, cells a side
        vec4 place;     // the slot, the type
        vec4 terrain;   // the height of a 0 sample, metres to a 1 sample, the finest spacing, the page levels
        vec4 extra;     // the camera from the terrain's corner along x and z, the type's range
    } push;

    #define CAPACITY 262144
    #define WIDTH 1024
    #define ROWS 512

    uvec4 Hash(ivec2 cell, uint salt)
    {
        uvec4 v = uvec4(uvec2(cell), salt, 0x9E3779B9u);
        v = v * 1664525u + 1013904223u;
        v.x += v.y * v.w; v.y += v.z * v.x; v.z += v.x * v.y; v.w += v.y * v.z;
        v ^= v >> 16u;
        v.x += v.y * v.w; v.y += v.z * v.x; v.z += v.x * v.y; v.w += v.y * v.z;
        return v;
    }

    vec4 Random(ivec2 cell, uint salt) { return vec4(Hash(cell, salt) >> 8u) / 16777216.0; }

    // Smooth value noise from -1 to 1 with a feature every unit, two octaves
    float Noise(vec2 p, uint salt)
    {
        float sum = 0.0, amplitude = 0.65;
        for (int octave = 0; octave < 2; octave++)
        {
            vec2 i = floor(p), f = p - i;
            f = f * f * (3.0 - 2.0 * f);
            ivec2 c = ivec2(i);
            float a = Random(c, salt).x, b = Random(c + ivec2(1, 0), salt).x, d = Random(c + ivec2(0, 1), salt).x, e = Random(c + ivec2(1, 1), salt).x;
            sum += (mix(mix(a, b, f.x), mix(d, e, f.x), f.y) * 2.0 - 1.0) * amplitude;
            p = p * 2.03 + 17.1;
            amplitude *= 0.54;
        }
        return sum;
    }

    // The ground's colour under a point, the layers' average colours with the terrain's broad light and dark patches,
    // its square root packed eight bits a channel into an integer a float holds exactly
    float Ground(vec2 xz, vec2 at, float slice)
    {
        if (_TerrainSpacing.y < 0.5)
            return 0.0;
        ivec4 ids = ivec4(-1);
        vec4 weights = vec4(0.0);
        Terrain_Gather(vec3((at + 1.5) / TERRAIN_PAGE_SIZE, slice), 1.0, ids, weights);
        vec3 color = Terrain_MacroColor(ids, weights);
        float macro = 0.0, total = 0.0;
        for (int k = 0; k < 4; k++)
        {
            if (ids[k] < 0)
                continue;
            macro += texelFetch(_TerrainLayerData, ivec2(ids[k], 0), 0).w * weights[k];
            total += weights[k];
        }
        float variation = Terrain_Noise(xz / 45.0) * 0.6 + Terrain_Noise(xz / 11.0) * 0.4;
        color *= max(1.0 + variation * macro / max(total, 1e-5), 0.0);
        uvec3 bits = uvec3(round(sqrt(clamp(color, 0.0, 1.0)) * 255.0));
        return float(bits.r * 65536u + bits.g * 256u + bits.b);
    }

    void main()
    {
        ivec2 id = ivec2(gl_GlobalInvocationID.xy);
        int cells = int(push.grid.w);
        if (id.x >= cells || id.y >= cells)
            return;
        ivec2 cell = ivec2(push.grid.xy) + id;
        int type = int(push.place.y);
        vec4 r = Random(cell, uint(type) * 7919u + 1u);
        vec4 s = Random(cell, uint(type) * 7919u + 2u);
        float spacing = push.grid.z;
        vec2 xz = (vec2(cell) + r.xy) * spacing;
        float range = push.extra.z;
        float away = distance(xz, push.extra.xy);
        if (away > range)
            return;

        // The finest resident page with control under the point
        ivec2 tableSize = textureSize(Terrain_PageTable, 0).xy;
        float slice = -1.0;
        vec2 at = vec2(0.0);
        for (int level = 0; level < int(push.terrain.w) && slice < 0.0; level++)
        {
            vec2 onLevel = xz / (push.terrain.z * exp2(float(level)));
            ivec2 page = ivec2(floor(onLevel / 128.0));
            if (any(lessThan(page, ivec2(0))) || any(greaterThanEqual(page, tableSize)))
                return;
            slice = texelFetch(Terrain_PageTable, ivec3(page, level), 0).r;
            at = onLevel - vec2(page) * 128.0;
        }
        if (slice < 0.0)
            return;

        // How much of the point's layers grow this type less what was cut, bilinear over the four control samples around it
        vec2 t = at + 1.0;
        vec2 base = floor(t), f = t - base;
        vec4 shares = vec4((1.0 - f.x) * (1.0 - f.y), f.x * (1.0 - f.y), (1.0 - f.x) * f.y, f.x * f.y);
        int group = int(floor(texelFetch(Terrain_GrassTypes, ivec2(4, type), 0).x + 0.5));
        float density = 0.0;
        for (int c = 0; c < 4; c++)
        {
            uint bits = packUnorm4x8(texelFetch(_TerrainControl, ivec3(ivec2(base) + ivec2(c & 1, c >> 1), int(slice + 0.5)), 0));
            if (c == 0 && (bits & (1u << 20)) != 0u)
                return;
            float blend = float((bits >> 12) & 255u) / 255.0;
            vec4 under = texelFetch(_TerrainLayerData, ivec2(int(bits & 63u), 1), 0);
            vec4 over = texelFetch(_TerrainLayerData, ivec2(int((bits >> 6) & 63u), 1), 0);
            float grown = 1.0 - float((bits >> 21) & 255u) / 255.0;
            density += shares[c] * grown * ((1.0 - blend) * (int(floor(under.x + 0.5)) == group ? under.y : 0.0) + blend * (int(floor(over.x + 0.5)) == group ? over.y : 0.0));
        }

        // Thinning: past the LOD distance fewer survive by the square of the distance, each wider by as much, and the last
        // three tenths of the range fade out
        float lod = texelFetch(Terrain_GrassTypes, ivec2(3, type), 0).w;
        float thin = min(lod * lod / max(away * away, 1e-4), 1.0);
        float keep = thin * (1.0 - smoothstep(range * 0.7, range, away));
        if (r.z >= density * keep || keep <= 0.0)
            return;
        float widen = min(inversesqrt(thin), 3.0);

        vec4 shape = texelFetch(Terrain_GrassTypes, ivec2(0, type), 0);
        vec4 patches = texelFetch(Terrain_GrassTypes, ivec2(4, type), 0);
        vec2 field = xz / patches.y;
        float rise = clamp(smoothstep(-0.5, 0.5, Noise(field, uint(type) * 31u + 7u)) + (s.x - 0.5) * 2.0 * patches.z, 0.0, 1.0);
        float height = mix(shape.y, shape.z, rise) * mix(0.85, 1.0, density);
        float width = shape.w * widen * max(1.0 + patches.w * Noise(field + 41.7, uint(type) * 31u + 11u), 0.05);
        float ground = textureLod(_TerrainHeights, vec3((at + 1.5) / 131.0, slice), 0.0).r * push.terrain.y + push.terrain.x;

        int slot = int(push.place.x);
        vec4 origin = texelFetch(Terrain_GrassViews, ivec2(7, slot), 0);
        vec3 spot = vec3(xz.x - origin.x, ground - origin.z, xz.y - origin.y);

        // The frustum is last frame's, so it is widened by about 22 degrees to hold what a fast turn brings into view
        vec4 camera = texelFetch(Terrain_GrassViews, ivec2(6, slot), 0);
        vec3 centre = spot + vec3(0.0, height * 0.5, 0.0);
        float margin = height + distance(centre, camera.xyz) * 0.4;
        for (int p = 0; p < int(camera.w); p++)
        {
            vec4 plane = texelFetch(Terrain_GrassViews, ivec2(p, slot), 0);
            if (dot(plane.xyz, centre) + plane.w < -margin)
                return;
        }

        uint index = atomicAdd(counts[slot], 1u);
        if (index >= uint(CAPACITY))
            return;
        int texel = int(index) * 2;
        ivec2 where = ivec2(texel % WIDTH, slot * ROWS + texel / WIDTH);
        imageStore(Terrain_GrassInstances, where, vec4(spot, s.y * 6.2831853));
        imageStore(Terrain_GrassInstances, where + ivec2(1, 0), vec4(height, width, Ground(xz, at, slice), float(type) + s.w * 0.5));
    }
}
