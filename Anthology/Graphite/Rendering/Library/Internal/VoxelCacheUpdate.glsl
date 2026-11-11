// Updates every _Stride'th slot of the radiance cache from _Phase: traces one cosine distributed ray from the middle
// of its face, lit by the hit's direct light and the cache at the hit, or the sky as far as it reaches, averages it in
// over up to MAX_SAMPLES samples, then sums the local lights reaching the face through the voxel view's light trees.
// Entries unread for MAX_AGE frames, or whose voxel left the box or emptied, are let go. _Clear 1 empties every slot
// instead, _Clear 2 every slot in the slabs the box scrolled into, which now hold other voxels of the world. Bound to
// the voxel view's tables, which hold the voxels and sit at the box's centre at the capture exposure.
Compute
{
    #pragma resource_set 3
    #define GRAPHITE_NO_OBJECT 1
    #define GRAPHITE_LIGHT_MASKS 1
    #define GRAPHITE_HARD_SHADOWS 1
    #define GRAPHITE_VOXEL_WRITER 1

    layout(local_size_x = 64) in;
    #include "Graphite/Lighting.glsl"
    #include "Graphite/VoxelCache.glsl"

    uniform int _Stride;
    uniform int _Phase;
    uniform int _Frame;
    uniform int _Clear;
    uniform ivec4 _Scrolled;    // voxels the box scrolled along each axis, whose slabs _Clear 2 lets go of

    #define MAX_AGE 64u
    #define MAX_SAMPLES 32.0
    #define MAX_STEPS 256u

    // The local lights' light at a point of a face in voxel units, irradiance over pi
    vec3 LocalLight(vec3 voxelPosition, vec3 normal)
    {
        Graphite_LightFrame f;
        f.position = (voxelPosition - vec3(graphite_Voxel.resolution.xyz) * 0.5) * graphite_Voxel.size.x + normal * 0.01;
        f.view = normal;
        f.geomNormal = normal;
        f.baseNormal = normal;
        f.coatNormal = normal;
        f.baseAlpha = 1.0;
        f.coatAlpha = 1.0;
        vec3 sum = vec3(0.0);
        Graphite_LightIterator it = Graphite_BeginLights(f.position);
        Graphite_LightSample L;
        while (Graphite_NextLight(it, L))
        {
            Graphite_LightContribution c;
            if (Graphite_ContributeLocal(L, f, false, c))
                sum += c.radiance * c.diffuse;
        }
        return sum / GRAPHITE_PI;
    }

    void main()
    {
        uint thread = gl_GlobalInvocationID.x + gl_GlobalInvocationID.y * gl_NumWorkGroups.x * 64u;
        uint slot = thread * uint(_Stride) + uint(_Phase);
        if (slot >= GRAPHITE_VOXEL_CACHE_SIZE)
            return;
        if (_Clear == 1)
        {
            graphite_VoxelCacheEntries[slot * 2u] = uvec4(0u);
            graphite_VoxelCacheEntries[slot * 2u + 1u] = uvec4(0u);
            return;
        }
        uvec4 entry = graphite_VoxelCacheEntries[slot * 2u];
        if (entry.x == 0u)
            return;

        uint key = entry.x - 1u;
        uvec3 r = graphite_Voxel.resolution.xyz;
        uvec3 stored = uvec3(key & 511u, (key >> 9u) & 511u, (key >> 18u) & 511u);
        uint face = (key >> 27u) & 7u;
        uint age = entry.y + uint(_Stride);
        uvec3 voxel = (stored + r - graphite_Voxel.wrap.xyz) % r;
        if (_Clear == 2)
        {
            bool scrolledInto = false;
            for (int a = 0; a < 3; a++)
                scrolledInto = scrolledInto || (_Scrolled[a] > 0 ? int(voxel[a]) >= int(r[a]) - _Scrolled[a] : int(voxel[a]) < -_Scrolled[a]);
            if (scrolledInto)
            {
                graphite_VoxelCacheEntries[slot * 2u] = uvec4(0u);
                graphite_VoxelCacheEntries[slot * 2u + 1u] = uvec4(0u);
            }
            return;
        }
        if (any(greaterThanEqual(stored, r)) || face > 5u || age > MAX_AGE || Graphite_VoxelOctants(voxel) == 0u)
        {
            graphite_VoxelCacheEntries[slot * 2u] = uvec4(0u);
            graphite_VoxelCacheEntries[slot * 2u + 1u] = uvec4(0u);
            return;
        }

        vec3 normal = Graphite_VoxelFaceNormal(face);
        vec3 origin = vec3(voxel) + 0.5 + normal * 0.5;
        uint h = Graphite_Hash(slot ^ Graphite_Hash(uint(_Frame)));
        vec3 direction = Graphite_CosineDirection(normal, float(h & 0xFFFFu) / 65536.0, float(Graphite_Hash(h) & 0xFFFFu) / 65536.0);

        Graphite_VoxelHit hit = Graphite_TraceVoxels(origin, direction, 1e6, MAX_STEPS);
        float reach;
        vec3 incoming = hit.hit ? Graphite_VoxelHitLight(hit, origin, direction, false) : hit.lost ? vec3(0.0) : Graphite_VoxelSkyLeaving(origin, direction, reach);

        vec3 light = Graphite_VoxelCacheLight(entry);
        float samples = min(unpackHalf2x16(entry.w).y + 1.0, MAX_SAMPLES);
        light += (incoming - light) / samples;
        vec3 local = LocalLight(origin, normal);
        graphite_VoxelCacheEntries[slot * 2u] = uvec4(entry.x, age, packHalf2x16(light.rg), packHalf2x16(vec2(light.b, samples)));
        graphite_VoxelCacheEntries[slot * 2u + 1u] = uvec4(packHalf2x16(local.rg), packHalf2x16(vec2(local.b, 0.0)), 0u, 0u);
    }
}
