// The four wide trees of H-Forward shading. A point query walks a tree testing a node's four child boxes at once and
// returns the leaves whose box holds the point, each a run of records. The layout matches LightTreeNode.
//
// Static lights and decals live in regions of space, each with a tree around its own origin. A view has a tree over the
// regions it sees, camera relative, whose leaves hold each region's root and the camera's offset to its origin. Moving
// ones live in a tree per view around the camera.
#pragma once

#include "Graphite/Core.glsl"

#define GRAPHITE_TREE_LEAF 0x80000000u
#define GRAPHITE_TREE_INVALID 0xFFFFFFFFu

// Each level visited defers at most three siblings, so this covers sixteen levels of an item tree and five of the regions tree
#define GRAPHITE_TREE_STACK 48
#define GRAPHITE_REGION_STACK 16

#define GRAPHITE_STATIC_LIGHTS 0u
#define GRAPHITE_VIEW_LIGHTS 1u
#define GRAPHITE_STATIC_DECALS 2u
#define GRAPHITE_VIEW_DECALS 3u
#define GRAPHITE_STATIC_PROBES 4u
#define GRAPHITE_LIGHT_REGIONS 5u
#define GRAPHITE_DECAL_REGIONS 6u
#define GRAPHITE_PROBE_REGIONS 7u

#define GRAPHITE_QUERY_LIGHTS 0u
#define GRAPHITE_QUERY_DECALS 1u
#define GRAPHITE_QUERY_PROBES 2u

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

layout(set = 1, binding = 19, std430) readonly buffer Graphite_StaticLightNodes { Graphite_TreeNode graphite_StaticLightNodes[]; };
layout(set = 1, binding = 21, std430) readonly buffer Graphite_StaticDecalNodes { Graphite_TreeNode graphite_StaticDecalNodes[]; };
layout(set = 1, binding = 4, std430) readonly buffer Graphite_ViewLightNodes { Graphite_TreeNode graphite_ViewLightNodes[]; };
layout(set = 1, binding = 6, std430) readonly buffer Graphite_ViewDecalNodes { Graphite_TreeNode graphite_ViewDecalNodes[]; };
layout(set = 1, binding = 8, std430) readonly buffer Graphite_LightRegionNodes { Graphite_TreeNode graphite_LightRegionNodes[]; };
layout(set = 1, binding = 10, std430) readonly buffer Graphite_DecalRegionNodes { Graphite_TreeNode graphite_DecalRegionNodes[]; };
layout(set = 1, binding = 25, std430) readonly buffer Graphite_ReflectionProbeNodes { Graphite_TreeNode graphite_ReflectionProbeNodes[]; };
layout(set = 1, binding = 12, std430) readonly buffer Graphite_ProbeRegionNodes { Graphite_TreeNode graphite_ProbeRegionNodes[]; };

// A region the view sees: the camera minus its origin split high and low, and its tree's root
struct Graphite_RegionEntry { vec3 offset; uint root; vec3 offsetLow; uint pad; };

layout(set = 1, binding = 7, std430) readonly buffer Graphite_LightRegions { Graphite_RegionEntry graphite_LightRegions[]; };
layout(set = 1, binding = 9, std430) readonly buffer Graphite_DecalRegions { Graphite_RegionEntry graphite_DecalRegions[]; };
layout(set = 1, binding = 11, std430) readonly buffer Graphite_ProbeRegions { Graphite_RegionEntry graphite_ProbeRegions[]; };

Graphite_TreeNode Graphite_TreeNodeAt(uint tree, uint index)
{
    if (tree == GRAPHITE_STATIC_LIGHTS)
        return graphite_StaticLightNodes[index];
    if (tree == GRAPHITE_VIEW_LIGHTS)
        return graphite_ViewLightNodes[index];
    if (tree == GRAPHITE_STATIC_DECALS)
        return graphite_StaticDecalNodes[index];
    if (tree == GRAPHITE_VIEW_DECALS)
        return graphite_ViewDecalNodes[index];
    if (tree == GRAPHITE_STATIC_PROBES)
        return graphite_ReflectionProbeNodes[index];
    if (tree == GRAPHITE_LIGHT_REGIONS)
        return graphite_LightRegionNodes[index];
    if (tree == GRAPHITE_DECAL_REGIONS)
        return graphite_DecalRegionNodes[index];
    return graphite_ProbeRegionNodes[index];
}

// Nodes still to visit: an item tree's from the bottom, the regions tree's above it, so one of each can walk at once
uint graphite_TreeStack[GRAPHITE_TREE_STACK + GRAPHITE_REGION_STACK];

struct Graphite_TreeWalk
{
    uint tree;
    int sp;
    int stackBase;
    int stackEnd;
    uint next;
    uint overlap;            // a bit per child whose box holds the point
    uvec4 children;
    uvec4 counts;
};

void Graphite_BeginTree(out Graphite_TreeWalk w, uint tree, uint root)
{
    bool regions = tree >= GRAPHITE_LIGHT_REGIONS;
    w.tree = tree;
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

        Graphite_TreeNode node = Graphite_TreeNodeAt(w.tree, w.next);
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

// Walks every tree that may hold a point: the static regions the view sees, then the view's tree of moving ones
struct Graphite_TreeQuery
{
    Graphite_TreeWalk regions;
    Graphite_TreeWalk items;
    vec3 position;           // camera relative
    vec3 base;               // what moves it relative to the view's first layer, which its trees are built around
    vec3 point;              // the position in the space of the tree being walked
    vec3 offset;             // what moves a camera relative position into that space, high and low with the layer's base
    vec3 offsetLow;
    uint staticTree;
    uint viewTree;
    uint viewRoot;
    uint entry;
    uint entryEnd;
    int phase;               // 0 static regions, 1 the moving tree, 2 done
    bool walking;
};

// Walks the static regions of a kind the view sees, then its tree of moving ones. Reflection probes have no moving tree.
void Graphite_BeginQuery(out Graphite_TreeQuery q, vec3 position, uint kind)
{
    Graphite_ViewData view = Graphite_View();
    bool decals = kind == GRAPHITE_QUERY_DECALS;
    bool probes = kind == GRAPHITE_QUERY_PROBES;
    q.position = position;
    q.base = view.treeOffset.xyz;
    q.point = position + q.base;
    q.offset = vec3(0.0);
    q.offsetLow = q.base;
    q.staticTree = probes ? GRAPHITE_STATIC_PROBES : decals ? GRAPHITE_STATIC_DECALS : GRAPHITE_STATIC_LIGHTS;
    q.viewTree = decals ? GRAPHITE_VIEW_DECALS : GRAPHITE_VIEW_LIGHTS;
    q.viewRoot = probes ? GRAPHITE_TREE_INVALID : decals ? view.treeRoots.w : view.treeRoots.y;
    q.entry = 0u;
    q.entryEnd = 0u;
    q.phase = 0;
    q.walking = false;
    uint regionTree = probes ? GRAPHITE_PROBE_REGIONS : decals ? GRAPHITE_DECAL_REGIONS : GRAPHITE_LIGHT_REGIONS;
    uint regionRoot = probes ? view.probeRoots.x : decals ? view.treeRoots.z : view.treeRoots.x;
    Graphite_BeginTree(q.regions, regionTree, regionRoot);
    Graphite_BeginTree(q.items, q.staticTree, GRAPHITE_TREE_INVALID);
}

Graphite_RegionEntry Graphite_RegionAt(uint tree, uint index)
{
    if (tree == GRAPHITE_LIGHT_REGIONS)
        return graphite_LightRegions[index];
    if (tree == GRAPHITE_DECAL_REGIONS)
        return graphite_DecalRegions[index];
    return graphite_ProbeRegions[index];
}

// Finds the next run of records whose leaf holds the point, as [first, last) of tree's records. q.point and q.offset
// stay those of the run's tree until the next call.
bool Graphite_NextRun(inout Graphite_TreeQuery q, out uint tree, out uint first, out uint last)
{
    tree = q.staticTree;
    first = 0u;
    last = 0u;
    for (;;)
    {
        if (q.walking)
        {
            if (Graphite_NextLeaf(q.items, q.point, first, last))
            {
                tree = q.items.tree;
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
            Graphite_BeginTree(q.items, q.viewTree, q.viewRoot);
            q.walking = true;
            continue;
        }

        if (q.entry < q.entryEnd)
        {
            Graphite_RegionEntry region = Graphite_RegionAt(q.regions.tree, q.entry++);
            q.offset = region.offset;
            q.offsetLow = region.offsetLow + q.base;
            q.point = (q.position + q.offset) + q.offsetLow;
            Graphite_BeginTree(q.items, q.staticTree, region.root);
            q.walking = true;
            continue;
        }

        if (!Graphite_NextLeaf(q.regions, q.position + q.base, q.entry, q.entryEnd))
            q.phase = 1;
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
