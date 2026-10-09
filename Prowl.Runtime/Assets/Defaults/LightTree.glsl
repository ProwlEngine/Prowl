// Four wide light trees for forward rendering. Each fragment walks the static tree and this frame's dynamic tree
// together, testing a node's four child boxes at once and every light of a leaf it lands in against the light's
// sphere. The layout must match ForwardLightTrees in LightTree.cs.
//
// LIGHT TABLE, 5 vec4 per light, a leaf's lights stored in a run:
//   +0 : Position.xyz, Range
//   +1 : Color.rgb, Intensity
//   +2 : Direction.xyz, TypeAndFlags as int bits (low 2 bits type, bit 2 ShadowEnabled)
//   +3 : SpotCos, InnerSpotCos, ShadowDepthBias, ShadowNormalBias (read for spots and shadowed lights)
//   +4 : ShadowStrength, ShadowQuality, ShadowSlot as int bits, padding (read for shadowed lights)
//
// Reflection probes walk the same kind of tree, their records and nodes in one table, the nodes from
// _ReflectionProbeNodeBase.
//
// NODE TABLE, 8 vec4 per node, one lane per child:
//   +0..+5 : MinX, MinY, MinZ, MaxX, MaxY, MaxZ
//   +6     : child as uint bits. With the leaf bit set the rest is the first light, otherwise a node index
//   +7     : light count of each leaf child, as uint bits

#ifndef PROWL_LIGHT_TREE
#define PROWL_LIGHT_TREE

#define LIGHT_TREE_LEAF_BIT 0x80000000u
#define LIGHT_TREE_INVALID 0xFFFFFFFFu

// Each level visited defers at most three siblings, so this covers sixteen levels
#define LIGHT_TREE_STACK 48

uniform int _StaticLightRoot;
uniform int _DynamicLightRoot;

uniform int _ReflectionProbeNodeBase;

#if defined(PROWL_FRAGMENT_STORAGE_BUFFERS) && !defined(PROWL_VERTEX_STAGE)
layout(std430) readonly buffer ProwlLightData { vec4 _LightData[]; };
layout(std430) readonly buffer ProwlLightNodes { vec4 _LightNodes[]; };
layout(std430) readonly buffer ProwlReflectionProbes { vec4 _ReflectionProbeData[]; };

vec4 LightTexel(int i) { return _LightData[i]; }
vec4 LightNodeTexel(int i) { return _LightNodes[i]; }
vec4 ReflectionProbeTexel(int i) { return _ReflectionProbeData[i]; }
#else
uniform sampler2D _LightDataTex;
uniform int _LightDataTexShift;
uniform sampler2D _LightNodeTex;
uniform int _LightNodeTexShift;
uniform sampler2D _ReflectionProbeTex;
uniform int _ReflectionProbeTexShift;

vec4 LightTexel(int i) { return texelFetch(_LightDataTex, ivec2(i & ((1 << _LightDataTexShift) - 1), i >> _LightDataTexShift), 0); }
vec4 LightNodeTexel(int i) { return texelFetch(_LightNodeTex, ivec2(i & ((1 << _LightNodeTexShift) - 1), i >> _LightNodeTexShift), 0); }
vec4 ReflectionProbeTexel(int i) { return texelFetch(_ReflectionProbeTex, ivec2(i & ((1 << _ReflectionProbeTexShift) - 1), i >> _ReflectionProbeTexShift), 0); }
#endif

#define LIGHT_TREE_LIGHTS 0
#define LIGHT_TREE_PROBES 1

vec4 TreeNodeTexel(int table, int i)
{
    return table == LIGHT_TREE_LIGHTS ? LightNodeTexel(i) : ReflectionProbeTexel(_ReflectionProbeNodeBase + i);
}

struct LightSample
{
    vec3  Position;
    float Range;
    vec3  Color;
    float Intensity;
    vec3  Direction;
    int   Type;            // 0 directional, 1 point, 2 spot
    float SpotCos;         // cos(outer)
    float InnerSpotCos;    // cos(inner)
    float ShadowDepthBias;
    float ShadowNormalBias;
    float ShadowStrength;
    float ShadowQuality;
    int   ShadowSlot;      // -1 if no atlas slot this frame
    int   ShadowEnabled;   // 0 / 1
};

// Reads a light when worldPos is inside its sphere, fetching only the texels its type and shadow state need
bool LightTree_FetchLight(int index, vec3 worldPos, out LightSample L)
{
    int base = index * 5;
    vec4 t0 = LightTexel(base);
    vec3 d = worldPos - t0.xyz;
    if (dot(d, d) > t0.w * t0.w)
        return false;

    vec4 t1 = LightTexel(base + 1);
    vec4 t2 = LightTexel(base + 2);

    int typeAndFlags = floatBitsToInt(t2.w);
    int type = typeAndFlags & 3;
    int shadowEnabled = (typeAndFlags >> 2) & 1;

    L.Position      = t0.xyz;
    L.Range         = t0.w;
    L.Color         = t1.rgb;
    L.Intensity     = t1.w;
    L.Direction     = t2.xyz;
    L.Type          = type;
    L.ShadowEnabled = shadowEnabled;

    if (type == 2 || shadowEnabled != 0)
    {
        vec4 t3 = LightTexel(base + 3);
        L.SpotCos          = t3.x;
        L.InnerSpotCos     = t3.y;
        L.ShadowDepthBias  = t3.z;
        L.ShadowNormalBias = t3.w;
    }
    else
    {
        L.SpotCos          = -1.0;
        L.InnerSpotCos     =  1.0;
        L.ShadowDepthBias  =  0.0;
        L.ShadowNormalBias =  0.0;
    }

    if (shadowEnabled != 0)
    {
        vec4 t4 = LightTexel(base + 4);
        L.ShadowStrength = t4.x;
        L.ShadowQuality  = t4.y;
        L.ShadowSlot     = floatBitsToInt(t4.z);
    }
    else
    {
        L.ShadowStrength = 0.0;
        L.ShadowQuality  = 0.0;
        L.ShadowSlot     = -1;
    }
    return true;
}

// Nodes still to visit. A fragment walks one tree at a time, so the stack is shared
uint _LightTreeStack[LIGHT_TREE_STACK];

struct LightTreeWalk
{
    int   table;
    int   sp;
    int   child;
    uint  next;
    vec4  overlap;
    uvec4 children;
    uvec4 counts;
};

// Starts at the static root with the dynamic root waiting on the stack, so one walk covers both trees
void LightTree_Begin(out LightTreeWalk w)
{
    w.table = LIGHT_TREE_LIGHTS;
    w.sp = 0;
    w.child = 4;
    w.next = LIGHT_TREE_INVALID;
    w.overlap = vec4(0.0);
    w.children = uvec4(LIGHT_TREE_INVALID);
    w.counts = uvec4(0u);
    if (_DynamicLightRoot >= 0) _LightTreeStack[w.sp++] = uint(_DynamicLightRoot);
    if (_StaticLightRoot >= 0) w.next = uint(_StaticLightRoot);
}

// Walks the reflection probe tree, its leaves listing probe records
void ProbeTree_Begin(out LightTreeWalk w, int root)
{
    w.table = LIGHT_TREE_PROBES;
    w.sp = 0;
    w.child = 4;
    w.next = root >= 0 ? uint(root) : LIGHT_TREE_INVALID;
    w.overlap = vec4(0.0);
    w.children = uvec4(LIGHT_TREE_INVALID);
    w.counts = uvec4(0u);
}

// Finds the next leaf whose box holds worldPos and returns its lights as [first, last)
bool LightTree_NextLeaf(inout LightTreeWalk w, vec3 worldPos, out int first, out int last)
{
    for (;;)
    {
        while (w.child < 4)
        {
            int i = w.child++;
            if (w.overlap[i] < 0.5) continue;

            uint slot = w.children[i];
            if ((slot & LIGHT_TREE_LEAF_BIT) == 0u)
            {
                if (w.next == LIGHT_TREE_INVALID) w.next = slot;
                else if (w.sp < LIGHT_TREE_STACK) _LightTreeStack[w.sp++] = slot;
                continue;
            }

            first = int(slot & ~LIGHT_TREE_LEAF_BIT);
            last = first + int(w.counts[i]);
            return true;
        }

        if (w.next == LIGHT_TREE_INVALID)
        {
            if (w.sp == 0) return false;
            w.next = _LightTreeStack[--w.sp];
        }

        int base = int(w.next) * 8;
        w.next = LIGHT_TREE_INVALID;
        w.child = 0;
        w.overlap = step(TreeNodeTexel(w.table, base + 0), vec4(worldPos.x)) * step(vec4(worldPos.x), TreeNodeTexel(w.table, base + 3))
                  * step(TreeNodeTexel(w.table, base + 1), vec4(worldPos.y)) * step(vec4(worldPos.y), TreeNodeTexel(w.table, base + 4))
                  * step(TreeNodeTexel(w.table, base + 2), vec4(worldPos.z)) * step(vec4(worldPos.z), TreeNodeTexel(w.table, base + 5));
        w.children = floatBitsToUint(TreeNodeTexel(w.table, base + 6));
        w.counts = floatBitsToUint(TreeNodeTexel(w.table, base + 7));
    }
    return false;
}

#endif
