// Builds one level of the voxel occupancy pyramid over a box of its cells from the level below, a thread per cell.
// Cells are in storage coordinates, so each lines up with the world's grid and holds still as the box scrolls. A cell is
// filled when any of its eight children is: at level 1 any occupancy byte of its 2x2x2 voxels, an x pair a word read.
Compute
{
    layout(local_size_x = 64) in;
    #define GRAPHITE_VOXEL_WRITE
    #include "Graphite/VoxelData.glsl"

    layout(push_constant) uniform Push { uvec4 level; uvec4 cellMin; uvec4 cellMax; } push;

    bool VoxelPairFilled(uvec3 storage)
    {
        uvec3 r = graphite_Voxel.resolution.xyz;
        uint index = storage.x + r.x * (storage.y + r.y * storage.z);
        uint word = graphite_VoxelWords[graphite_Voxel.levels.x + (index >> 2u)];
        return ((word >> ((index & 3u) * 8u)) & 0xFFFFu) != 0u;
    }

    bool CellFilled(uvec3 cell)
    {
        if (push.level.x == 1u)
        {
            uvec3 first = cell * 2u;
            return VoxelPairFilled(first) || VoxelPairFilled(first + uvec3(0u, 1u, 0u))
                || VoxelPairFilled(first + uvec3(0u, 0u, 1u)) || VoxelPairFilled(first + uvec3(0u, 1u, 1u));
        }
        for (uint c = 0u; c < 8u; c++)
        {
            if (Graphite_VoxelCellFilled(push.level.x - 1u, cell * 2u + uvec3(c & 1u, (c >> 1u) & 1u, c >> 2u)))
                return true;
        }
        return false;
    }

    // Past the last level, a byte per level 1 cell of the coarsest level whose cell holding it is empty, 0 where filled,
    // four cells along x to a thread so each writes a whole word
    void EmptyLevels(uint i, uvec3 size)
    {
        uvec3 words = uvec3(size.x / 4u, size.y, size.z);
        if (i >= words.x * words.y * words.z)
            return;
        uvec3 first = push.cellMin.xyz + uvec3(i % words.x * 4u, (i / words.x) % words.y, i / (words.x * words.y));
        uvec3 r = graphite_Voxel.resolution.xyz >> 1u;
        uint bytes = 0u;
        for (uint b = 0u; b < 4u; b++)
        {
            uvec3 cell = first + uvec3(b, 0u, 0u);
            uint empty = 0u;
            for (uint level = 1u; level <= GRAPHITE_VOXEL_LEVELS && !Graphite_VoxelCellFilled(level, cell >> (level - 1u)); level++)
                empty = level;
            bytes |= empty << (b * 8u);
        }
        graphite_VoxelWords[graphite_Voxel.levels2.w + ((first.x + r.x * (first.y + r.y * first.z)) >> 2u)] = bytes;
    }

    void main()
    {
        uint i = gl_GlobalInvocationID.x + gl_GlobalInvocationID.y * gl_NumWorkGroups.x * 64u;
        uvec3 size = push.cellMax.xyz - push.cellMin.xyz;
        if (push.level.x > GRAPHITE_VOXEL_LEVELS)
        {
            EmptyLevels(i, size);
            return;
        }
        if (i >= size.x * size.y * size.z)
            return;
        uvec3 cell = push.cellMin.xyz + uvec3(i % size.x, (i / size.x) % size.y, i / (size.x * size.y));
        uvec3 r = graphite_Voxel.resolution.xyz >> push.level.x;
        uint index = cell.x + r.x * (cell.y + r.y * cell.z);
        uint word = graphite_VoxelLevelStart(push.level.x) + (index >> 5u), bit = 1u << (index & 31u);
        if (CellFilled(cell))
            atomicOr(graphite_VoxelWords[word], bit);
        else
            atomicAnd(graphite_VoxelWords[word], ~bit);
    }
}
