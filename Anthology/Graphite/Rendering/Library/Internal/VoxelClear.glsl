// Writes the voxel volume's header from the push constants and clears a region of its voxels, colour words and
// occupancy bytes, before the Voxelize pass draws the region again. A region of no voxels writes the header alone.
Compute
{
    layout(local_size_x = 64) in;
    #define GRAPHITE_VOXEL_WRITE
    #include "Graphite/VoxelData.glsl"

    layout(push_constant) uniform Push { Graphite_VoxelHeader header; uvec4 regionMin; uvec4 regionMax; } push;

    void main()
    {
        uint i = gl_GlobalInvocationID.x + gl_GlobalInvocationID.y * gl_NumWorkGroups.x * 64u;
        if (i == 0u)
            graphite_Voxel = push.header;
        uvec3 size = push.regionMax.xyz - push.regionMin.xyz;
        if (i >= size.x * size.y * size.z)
            return;
        uvec3 voxel = push.regionMin.xyz + uvec3(i % size.x, (i / size.x) % size.y, i / (size.x * size.y));
        uvec3 r = push.header.resolution.xyz;
        uvec3 s = (voxel + push.header.wrap.xyz) % r;
        uint index = s.x + r.x * (s.y + r.y * s.z);
        graphite_VoxelWords[index] = 0u;
        atomicAnd(graphite_VoxelWords[push.header.levels.x + (index >> 2u)], ~(0xFFu << ((index & 3u) * 8u)));
    }
}
