// The four wide trees of H-Forward shading. A point query walks a tree testing a node's four child boxes at once and
// returns the leaves whose box holds the point, each a run of records. The layout matches LightTreeNode.
#pragma once

#include "Graphite/Core.glsl"

#define GRAPHITE_TREE_LEAF 0x80000000u
#define GRAPHITE_TREE_INVALID 0xFFFFFFFFu

// Each level visited defers at most three siblings, so this covers sixteen levels
#define GRAPHITE_TREE_STACK 48

#define GRAPHITE_STATIC_LIGHTS 0u
#define GRAPHITE_VIEW_LIGHTS 1u
#define GRAPHITE_STATIC_DECALS 2u
#define GRAPHITE_VIEW_DECALS 3u

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

layout(set = 0, binding = 12, std430) readonly buffer Graphite_StaticLightNodes { Graphite_TreeNode graphite_StaticLightNodes[]; };
layout(set = 0, binding = 14, std430) readonly buffer Graphite_StaticDecalNodes { Graphite_TreeNode graphite_StaticDecalNodes[]; };
layout(set = 1, binding = 4, std430) readonly buffer Graphite_ViewLightNodes { Graphite_TreeNode graphite_ViewLightNodes[]; };
layout(set = 1, binding = 6, std430) readonly buffer Graphite_ViewDecalNodes { Graphite_TreeNode graphite_ViewDecalNodes[]; };

Graphite_TreeNode Graphite_TreeNodeAt(uint tree, uint index)
{
    if (tree == GRAPHITE_STATIC_LIGHTS)
        return graphite_StaticLightNodes[index];
    if (tree == GRAPHITE_VIEW_LIGHTS)
        return graphite_ViewLightNodes[index];
    if (tree == GRAPHITE_STATIC_DECALS)
        return graphite_StaticDecalNodes[index];
    return graphite_ViewDecalNodes[index];
}

// Nodes still to visit. A fragment walks one tree at a time, so the stack is shared
uint graphite_TreeStack[GRAPHITE_TREE_STACK];

struct Graphite_TreeWalk
{
    uint tree;
    int sp;
    int child;
    uint next;
    vec4 overlap;
    uvec4 children;
    uvec4 counts;
};

void Graphite_BeginTree(out Graphite_TreeWalk w, uint tree, uint root)
{
    w.tree = tree;
    w.sp = 0;
    w.child = 4;
    w.next = root;
    w.overlap = vec4(0.0);
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
        while (w.child < 4)
        {
            int i = w.child++;
            if (w.overlap[i] < 0.5)
                continue;

            uint slot = w.children[i];
            if ((slot & GRAPHITE_TREE_LEAF) == 0u)
            {
                if (w.next == GRAPHITE_TREE_INVALID)
                    w.next = slot;
                else if (w.sp < GRAPHITE_TREE_STACK)
                    graphite_TreeStack[w.sp++] = slot;
                continue;
            }

            first = slot & ~GRAPHITE_TREE_LEAF;
            last = first + w.counts[i];
            return true;
        }

        if (w.next == GRAPHITE_TREE_INVALID)
        {
            if (w.sp == 0)
                return false;
            w.next = graphite_TreeStack[--w.sp];
        }

        Graphite_TreeNode node = Graphite_TreeNodeAt(w.tree, w.next);
        w.next = GRAPHITE_TREE_INVALID;
        w.child = 0;
        w.overlap = step(node.minX, vec4(p.x)) * step(vec4(p.x), node.maxX)
                  * step(node.minY, vec4(p.y)) * step(vec4(p.y), node.maxY)
                  * step(node.minZ, vec4(p.z)) * step(vec4(p.z), node.maxZ);
        w.children = node.child;
        w.counts = node.count;
    }
    return false;
}

// The layer mask of the surface being shaded, all layers outside the fragment stage
uint Graphite_SurfaceLayers()
{
#ifdef GRAPHITE_FRAGMENT
    return Graphite_Object().layerMask;
#else
    return 0xFFFFFFFFu;
#endif
}
