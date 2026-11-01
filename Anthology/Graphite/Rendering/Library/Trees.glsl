// Spatial sets: the four wide trees of H-Forward shading, for lights, decals, reflection probes and any set a host makes.
// A point query walks a tree testing a node's four child boxes at once and returns the leaves whose box holds the point,
// each a run of records. The layout matches LightTreeNode.
//
// Static items live in regions of space, each with a tree around its own origin. A view has a tree over the regions it
// sees, camera relative, whose leaves hold each region's root and the camera's offset to its origin. Moving ones live in
// a tree per view around the camera. Every set's buffers sit in bindless arrays indexed by the set: its records and
// nodes, static then the view's, and the view's regions.
#pragma once

#extension GL_EXT_nonuniform_qualifier : require

#include "Graphite/Core.glsl"

#define GRAPHITE_TREE_LEAF 0x80000000u
#define GRAPHITE_TREE_INVALID 0xFFFFFFFFu

// Each level visited defers at most three siblings, so this covers ten levels of a tree. Above it sit the runs of region
// entries a query found, as first and end pairs
#define GRAPHITE_TREE_STACK 32
#define GRAPHITE_REGION_RUNS 8

#define GRAPHITE_SPATIAL_SETS 16
#define GRAPHITE_SET_LIGHTS 0u
#define GRAPHITE_SET_DECALS 1u
#define GRAPHITE_SET_PROBES 2u

struct Graphite_TreeNode
{
    vec4 minX;
    vec4 minY;
    vec4 minZ;
    vec4 maxX;
    vec4 maxY;
    vec4 maxZ;
    uvec4 child;
    uvec4 count;
};

// A region the view sees: the camera minus its origin split high and low, and its tree's root
struct Graphite_RegionEntry { vec3 offset; uint root; vec3 offsetLow; uint pad; };

layout(set = 1, binding = 3, std430) readonly buffer Graphite_SpatialRecords { uvec4 words[]; } graphite_SpatialRecords[GRAPHITE_SPATIAL_SETS * 2];
layout(set = 1, binding = 4, std430) readonly buffer Graphite_SpatialNodes { Graphite_TreeNode nodes[]; } graphite_SpatialNodes[GRAPHITE_SPATIAL_SETS * 3];
layout(set = 1, binding = 5, std430) readonly buffer Graphite_SpatialRegions { Graphite_RegionEntry entries[]; } graphite_SpatialRegions[GRAPHITE_SPATIAL_SETS];

// Per set: the root of the view's tree over its static regions, and of its tree of moving items
layout(set = 1, binding = 6, std430) readonly buffer Graphite_SpatialRoots { uvec4 graphite_SpatialRoots[]; };

// The records of a set: its static ones at set * 2, the view's moving ones at set * 2 + 1
uint Graphite_StaticRecords(uint set) { return set * 2u; }
uint Graphite_MovingRecords(uint set) { return set * 2u + 1u; }

// A 16 byte word of a record array, stride words to a record
vec4 Graphite_SpatialVec4(uint array, uint word)
{
    return uintBitsToFloat(graphite_SpatialRecords[nonuniformEXT(array)].words[word]);
}

uvec4 Graphite_SpatialUVec4(uint array, uint word)
{
    return graphite_SpatialRecords[nonuniformEXT(array)].words[word];
}

// Nodes still to visit, then a query's runs of region entries
uint graphite_TreeStack[GRAPHITE_TREE_STACK + GRAPHITE_REGION_RUNS * 2];

// The items a query found, gathered before any is shaded so the walk's registers are free by then: each record's index
// with the moving tree in the top bit, and the region entry it was found through, invalid for the moving tree. More
// items than this at one point are left out.
#ifndef GRAPHITE_MAX_HITS
#define GRAPHITE_MAX_HITS 32
#endif
uvec2 graphite_Hits[GRAPHITE_MAX_HITS];

struct Graphite_TreeWalk
{
    uint nodes;              // the node array walked
    int sp;
    uint next;
    uint node;               // the node whose children overlap names, read again as needed so the walk holds few registers
    uint overlap;            // a bit per child whose box holds the point
};

void Graphite_BeginTree(out Graphite_TreeWalk w, uint nodes, uint root)
{
    w.nodes = nodes;
    w.sp = 0;
    w.next = root;
    w.node = 0u;
    w.overlap = 0u;
}

// Finds the next leaf whose box holds p and returns its records as [first, last)
bool Graphite_NextLeaf(inout Graphite_TreeWalk w, vec3 p, out uint first, out uint last)
{
    first = 0u;
    last = 0u;
    for (;;)
    {
        while (w.overlap != 0u)
        {
            int i = findLSB(w.overlap);
            w.overlap &= w.overlap - 1u;

            uint slot = graphite_SpatialNodes[nonuniformEXT(w.nodes)].nodes[w.node].child[i];
            if ((slot & GRAPHITE_TREE_LEAF) == 0u)
            {
                if (w.next == GRAPHITE_TREE_INVALID)
                    w.next = slot;
                else if (w.sp < GRAPHITE_TREE_STACK)
                    graphite_TreeStack[w.sp++] = slot;
                continue;
            }

            first = slot & ~GRAPHITE_TREE_LEAF;
            last = first + graphite_SpatialNodes[nonuniformEXT(w.nodes)].nodes[w.node].count[i];
            return true;
        }

        if (w.next == GRAPHITE_TREE_INVALID)
        {
            if (w.sp == 0)
                return false;
            w.next = graphite_TreeStack[--w.sp];
        }

        w.node = w.next;
        Graphite_TreeNode node = graphite_SpatialNodes[nonuniformEXT(w.nodes)].nodes[w.node];
        w.next = GRAPHITE_TREE_INVALID;
        bvec4 inside = bvec4(uvec4(greaterThanEqual(vec4(p.x), node.minX)) & uvec4(lessThanEqual(vec4(p.x), node.maxX))
                           & uvec4(greaterThanEqual(vec4(p.y), node.minY)) & uvec4(lessThanEqual(vec4(p.y), node.maxY))
                           & uvec4(greaterThanEqual(vec4(p.z), node.minZ)) & uvec4(lessThanEqual(vec4(p.z), node.maxZ)));
        w.overlap = (inside.x ? 1u : 0u) | (inside.y ? 2u : 0u) | (inside.z ? 4u : 0u) | (inside.w ? 8u : 0u);
    }
    return false;
}

// Walks every tree of a set that may hold a point: the static regions the view sees, then the view's tree of moving ones.
// The regions holding the point are found first, so one walker serves every tree and the walk keeps few registers
struct Graphite_TreeQuery
{
    Graphite_TreeWalk items;
    uint set;
    vec3 position;           // camera relative
    vec3 offset;             // what moves a camera relative position into the walked tree's space, high and low with the layer's base
    vec3 offsetLow;
    uint movingRoot;
    uint entry;
    uint entryEnd;
    uint region;             // the region entry of the tree being walked, invalid for the moving tree
    int runs;                // runs of region entries still to walk
    int phase;               // 0 static regions, 1 the moving tree, 2 done
    bool walking;
};

// Queries share one traversal stack, so finish one before beginning another: they do not nest
void Graphite_BeginQuery(out Graphite_TreeQuery q, vec3 position, uint set)
{
    uvec4 roots = set < uint(GRAPHITE_SPATIAL_SETS) ? graphite_SpatialRoots[set] : uvec4(GRAPHITE_TREE_INVALID);
    q.set = set;
    q.position = position;
    q.offset = vec3(0.0);
    q.offsetLow = vec3(0.0);
    q.movingRoot = roots.y;
    q.entry = 0u;
    q.entryEnd = 0u;
    q.region = GRAPHITE_TREE_INVALID;
    q.runs = 0;
    q.phase = roots.x == GRAPHITE_TREE_INVALID && roots.y == GRAPHITE_TREE_INVALID ? 2 : 0;
    q.walking = false;
    Graphite_BeginTree(q.items, set * 3u, GRAPHITE_TREE_INVALID);
    if (roots.x == GRAPHITE_TREE_INVALID)
        return;

    // The regions tree is walked whole now, on the stack the item trees use later, keeping its runs above it
    Graphite_TreeWalk regions;
    Graphite_BeginTree(regions, set * 3u + 2u, roots.x);
    vec3 point = position + Graphite_View().treeOffset.xyz;
    uint first, end;
    while (q.runs < GRAPHITE_REGION_RUNS && Graphite_NextLeaf(regions, point, first, end))
    {
        graphite_TreeStack[GRAPHITE_TREE_STACK + q.runs * 2] = first;
        graphite_TreeStack[GRAPHITE_TREE_STACK + q.runs * 2 + 1] = end;
        q.runs++;
    }
}

// Finds the next run of records whose leaf holds the point, as [first, last) of the record array records. q.offset and
// q.offsetLow stay those of the run's tree until the next call.
bool Graphite_NextRun(inout Graphite_TreeQuery q, out uint records, out uint first, out uint last)
{
    records = Graphite_StaticRecords(q.set);
    first = 0u;
    last = 0u;
    for (;;)
    {
        if (q.walking)
        {
            if (Graphite_NextLeaf(q.items, (q.position + q.offset) + q.offsetLow, first, last))
            {
                records = q.phase == 1 ? Graphite_MovingRecords(q.set) : Graphite_StaticRecords(q.set);
                return true;
            }
            q.walking = false;
            if (q.phase == 1)
                q.phase = 2;
        }

        if (q.phase == 2)
            return false;

        if (q.phase == 1)
        {
            q.offset = vec3(0.0);
            q.offsetLow = Graphite_View().treeOffset.xyz;
            q.region = GRAPHITE_TREE_INVALID;
            Graphite_BeginTree(q.items, q.set * 3u + 1u, q.movingRoot);
            q.walking = true;
            continue;
        }

        if (q.entry < q.entryEnd)
        {
            q.region = q.entry;
            Graphite_RegionEntry region = graphite_SpatialRegions[nonuniformEXT(q.set)].entries[q.entry++];
            q.offset = region.offset;
            q.offsetLow = region.offsetLow + Graphite_View().treeOffset.xyz;
            Graphite_BeginTree(q.items, q.set * 3u, region.root);
            q.walking = true;
            continue;
        }

        if (q.runs > 0)
        {
            q.runs--;
            q.entry = graphite_TreeStack[GRAPHITE_TREE_STACK + q.runs * 2];
            q.entryEnd = graphite_TreeStack[GRAPHITE_TREE_STACK + q.runs * 2 + 1];
            continue;
        }
        q.phase = 1;
    }
    return false;
}

// What moves a camera relative position into the space of the tree a hit was found in
void Graphite_HitOffsets(uint set, uint region, out vec3 offset, out vec3 offsetLow)
{
    vec3 base = Graphite_View().treeOffset.xyz;
    offset = vec3(0.0);
    offsetLow = base;
    if (region == GRAPHITE_TREE_INVALID)
        return;
    Graphite_RegionEntry entry = graphite_SpatialRegions[nonuniformEXT(set)].entries[region];
    offset = entry.offset;
    offsetLow = entry.offsetLow + base;
}

// Walks the items of a host's set whose sphere holds a point. Their records start with their centre in their tree's space
// and their radius, then the host's data: read it with Graphite_SpatialVec4(array, index * stride + 1 + i).
struct Graphite_ItemIterator
{
    Graphite_TreeQuery query;
    uint array;
    uint k;
    uint last;
    uint stride;             // 16 byte words per record, the centre and radius included
};

Graphite_ItemIterator Graphite_BeginItems(uint set, uint stride, vec3 relativePos)
{
    Graphite_ItemIterator it;
    Graphite_BeginQuery(it.query, relativePos, set);
    it.array = 0u;
    it.k = 0u;
    it.last = 0u;
    it.stride = stride;
    return it;
}

// The next item holding the point: its record array and index, and its camera relative centre and radius
bool Graphite_NextItem(inout Graphite_ItemIterator it, out uint array, out uint index, out vec3 center, out float radius)
{
    for (;;)
    {
        while (it.k < it.last)
        {
            index = it.k++;
            array = it.array;
            vec4 cr = Graphite_SpatialVec4(array, index * it.stride);
            precise vec3 c = (cr.xyz - it.query.offset) - it.query.offsetLow;
            vec3 d = it.query.position - c;
            if (dot(d, d) <= cr.w * cr.w)
            {
                center = c;
                radius = cr.w;
                return true;
            }
        }
        if (!Graphite_NextRun(it.query, it.array, it.k, it.last))
            return false;
    }
    return false;
}

// The layer mask of the surface being shaded, all layers outside the fragment stage
uint Graphite_SurfaceLayers()
{
#if defined(GRAPHITE_FRAGMENT) && !defined(GRAPHITE_NO_OBJECT)
    return Graphite_Object().layerMask;
#else
    return 0xFFFFFFFFu;
#endif
}
