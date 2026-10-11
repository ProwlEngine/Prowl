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

// Each level visited defers at most three siblings, so this covers sixteen levels of an item tree and five of the regions tree
#define GRAPHITE_TREE_STACK 48
#define GRAPHITE_REGION_STACK 16

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

// Nodes still to visit: an item tree's from the bottom, the regions tree's above it, so one of each can walk at once
uint graphite_TreeStack[GRAPHITE_TREE_STACK + GRAPHITE_REGION_STACK];

struct Graphite_TreeWalk
{
    uint nodes;              // the node array walked
    int sp;
    int stackBase;
    int stackEnd;
    uint next;
    uint overlap;            // a bit per child whose box holds the point
    uvec4 children;
    uvec4 counts;
};

void Graphite_BeginTree(out Graphite_TreeWalk w, uint nodes, bool regions, uint root)
{
    w.nodes = nodes;
    w.stackBase = regions ? GRAPHITE_TREE_STACK : 0;
    w.stackEnd = regions ? GRAPHITE_TREE_STACK + GRAPHITE_REGION_STACK : GRAPHITE_TREE_STACK;
    w.sp = w.stackBase;
    w.next = root;
    w.overlap = 0u;
    w.children = uvec4(GRAPHITE_TREE_INVALID);
    w.counts = uvec4(0u);
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

            uint slot = w.children[i];
            if ((slot & GRAPHITE_TREE_LEAF) == 0u)
            {
                if (w.next == GRAPHITE_TREE_INVALID)
                    w.next = slot;
                else if (w.sp < w.stackEnd)
                    graphite_TreeStack[w.sp++] = slot;
                continue;
            }

            first = slot & ~GRAPHITE_TREE_LEAF;
            last = first + w.counts[i];
            return true;
        }

        if (w.next == GRAPHITE_TREE_INVALID)
        {
            if (w.sp == w.stackBase)
                return false;
            w.next = graphite_TreeStack[--w.sp];
        }

        Graphite_TreeNode node = graphite_SpatialNodes[nonuniformEXT(w.nodes)].nodes[w.next];
        w.next = GRAPHITE_TREE_INVALID;
        bvec4 inside = bvec4(uvec4(greaterThanEqual(vec4(p.x), node.minX)) & uvec4(lessThanEqual(vec4(p.x), node.maxX))
                           & uvec4(greaterThanEqual(vec4(p.y), node.minY)) & uvec4(lessThanEqual(vec4(p.y), node.maxY))
                           & uvec4(greaterThanEqual(vec4(p.z), node.minZ)) & uvec4(lessThanEqual(vec4(p.z), node.maxZ)));
        w.overlap = (inside.x ? 1u : 0u) | (inside.y ? 2u : 0u) | (inside.z ? 4u : 0u) | (inside.w ? 8u : 0u);
        w.children = node.child;
        w.counts = node.count;
    }
    return false;
}

// Walks every tree of a set that may hold a point: the static regions the view sees, then the view's tree of moving ones
struct Graphite_TreeQuery
{
    Graphite_TreeWalk regions;
    Graphite_TreeWalk items;
    uint set;
    vec3 position;           // camera relative
    vec3 base;               // what moves it relative to the view's first layer, which its trees are built around
    vec3 point;              // the position in the space of the tree being walked
    vec3 offset;             // what moves a camera relative position into that space, high and low with the layer's base
    vec3 offsetLow;
    uint movingRoot;
    uint entry;
    uint entryEnd;
    int phase;               // 0 static regions, 1 the moving tree, 2 done
    bool walking;
};

void Graphite_BeginQuery(out Graphite_TreeQuery q, vec3 position, uint set)
{
    Graphite_ViewData view = Graphite_View();
    uvec4 roots = graphite_SpatialRoots[set];
    q.set = set;
    q.position = position;
    q.base = view.treeOffset.xyz;
    q.point = position + q.base;
    q.offset = vec3(0.0);
    q.offsetLow = q.base;
    q.movingRoot = roots.y;
    q.entry = 0u;
    q.entryEnd = 0u;
    q.phase = 0;
    q.walking = false;
    Graphite_BeginTree(q.regions, set * 3u + 2u, true, roots.x);
    Graphite_BeginTree(q.items, set * 3u, false, GRAPHITE_TREE_INVALID);
}

// Finds the next run of records whose leaf holds the point, as [first, last) of the record array records. q.point and
// q.offset stay those of the run's tree until the next call.
bool Graphite_NextRun(inout Graphite_TreeQuery q, out uint records, out uint first, out uint last)
{
    records = Graphite_StaticRecords(q.set);
    first = 0u;
    last = 0u;
    for (;;)
    {
        if (q.walking)
        {
            if (Graphite_NextLeaf(q.items, q.point, first, last))
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
            q.offsetLow = q.base;
            q.point = q.position + q.base;
            Graphite_BeginTree(q.items, q.set * 3u + 1u, false, q.movingRoot);
            q.walking = true;
            continue;
        }

        if (q.entry < q.entryEnd)
        {
            Graphite_RegionEntry region = graphite_SpatialRegions[nonuniformEXT(q.set)].entries[q.entry++];
            q.offset = region.offset;
            q.offsetLow = region.offsetLow + q.base;
            q.point = (q.position + q.offset) + q.offsetLow;
            Graphite_BeginTree(q.items, q.set * 3u, false, region.root);
            q.walking = true;
            continue;
        }

        if (!Graphite_NextLeaf(q.regions, q.position + q.base, q.entry, q.entryEnd))
            q.phase = 1;
    }
    return false;
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

Graphite_ItemIterator Graphite_BeginItems(uint set, uint stride, vec3 worldPos)
{
    Graphite_ItemIterator it;
    Graphite_BeginQuery(it.query, worldPos, set);
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
