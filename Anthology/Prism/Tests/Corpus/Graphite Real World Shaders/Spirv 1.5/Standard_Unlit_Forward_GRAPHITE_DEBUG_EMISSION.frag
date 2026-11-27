#version 460
#extension GL_GOOGLE_include_directive : enable
#line 1 "Graphite/Unlit/Forward"

#extension GL_GOOGLE_cpp_style_line_directive : require
#line 16 "Graphite/Unlit/Forward"




#line 26 "Graphite/Unlit/Forward"

#line 1 "Graphite/Standard/Standard.glsl"

















#pragma variant_SURFACEOPAQUECUTOUTTRANSPARENT
#pragma variant_INSTANCE_TINT















#line 1 "Graphite/Lighting.glsl"










#line 1 "Graphite/Trees.glsl"










#extension GL_EXT_nonuniform_qualifier : require

#line 1 "Graphite/Core.glsl"




#extension GL_EXT_multiview : require
#extension GL_EXT_nonuniform_qualifier : require

#line 1 "Graphite/ViewData.glsl"



struct Graphite_ViewData
{
    mat4 view;
    mat4 projection;
    mat4 viewProjection;
    mat4 inverseViewProjection;
    mat4 previousViewProjection;
    vec4 cameraHigh;
    vec4 cameraLow;
    vec4 previousCameraHigh;
    vec4 previousCameraLow;
    vec4 screen;
    vec4 jitter;
    vec4 time;
    vec4 depth;
    vec4 treeOffset;
};
#line 9 "Graphite/Core.glsl"


struct Graphite_DrawRecord { uint chunk; uint object; uint flags; uint subMesh; };

struct Graphite_Transform { vec4 row0; vec4 row1; vec4 row2; vec4 translationLow; };

struct Graphite_ObjectData
{
    uint materialSliceBase;
    uint flags;
    uint objectId;
    uint previousTransform;
    uint lightmapTexture;
    uint probeIndex;
    float lodFade;
    uint layerMask;
    uint lightmapSampler;
    uint currentPositions;
    uint previousPositions;
    uint pad0;
    vec4 lightmapScaleOffset;
    vec4 custom0;
    vec4 custom1;
};

struct Graphite_Chunk
{
    uint streamOffsets[12];
    vec3 boundsMin;
    uint streamsPresent;
    vec3 boundsMax;
    uint page;
    uint firstIndex;
    uint indexCount;
    uint mesh;
    uint subMesh;
};

layout(set = 1, binding = 13, std430) readonly buffer Graphite_Chunks { Graphite_Chunk graphite_Chunks[]; };
layout(set = 1, binding = 14, std430) readonly buffer Graphite_Transforms { Graphite_Transform graphite_Transforms[]; };
layout(set = 1, binding = 15, std430) readonly buffer Graphite_PreviousTransforms { Graphite_Transform graphite_PreviousTransforms[]; };
layout(set = 1, binding = 16, std430) readonly buffer Graphite_Objects { Graphite_ObjectData graphite_Objects[]; };
layout(set = 1, binding = 17, std430) readonly buffer Graphite_MaterialSlots { uint graphite_MaterialSlots[]; };

layout(set = 1, binding = 2, std430) readonly buffer Graphite_Views { Graphite_ViewData graphite_Views[]; };

layout(set = 2, binding = 1, std430) readonly buffer Graphite_Instances { uint graphite_Instances[]; };
layout(set = 2, binding = 2, std430) readonly buffer Graphite_DrawRecords { Graphite_DrawRecord graphite_DrawRecords[]; };

layout(push_constant) uniform Graphite_Push { uint drawRecordBase; uint instanceBase; uint viewBase; uint flags; } graphite_Push;

Graphite_ViewData Graphite_View() { return graphite_Views[graphite_Push.viewBase + gl_ViewIndex]; }

vec3 Graphite_CameraOffset(vec3 high, vec3 low)
{
    Graphite_ViewData view = Graphite_View();
    precise vec3 offset = (high - view.cameraHigh.xyz) + (low - view.cameraLow.xyz);
    return offset;
}


vec3 Graphite_AbsoluteFromRelative(vec3 relative)
{
    Graphite_ViewData view = Graphite_View();
    return relative + view.cameraHigh.xyz + view.cameraLow.xyz;
}

mat3 Graphite_Linear(Graphite_Transform t) { return transpose(mat3(t.row0.xyz, t.row1.xyz, t.row2.xyz)); }

vec3 Graphite_TransformPoint(Graphite_Transform t, vec3 p, vec3 cameraHigh, vec3 cameraLow)
{
    vec3 linear = vec3(dot(t.row0.xyz, p), dot(t.row1.xyz, p), dot(t.row2.xyz, p));
    vec3 high = vec3(t.row0.w, t.row1.w, t.row2.w);
    precise vec3 relative = linear + ( (high - cameraHigh) + (t.translationLow.xyz - cameraLow));
    return relative;
}


mat3 Graphite_NormalMatrix(Graphite_Transform t)
{
    mat3 m = Graphite_Linear(t);
    mat3 cofactor = mat3(cross(m[1], m[2]), cross(m[2], m[0]), cross(m[0], m[1]));
    return dot(m[0], cross(m[1], m[2])) < 0.0 ? - cofactor : cofactor;
}

vec4 Graphite_RelativeToClip(vec3 relative) { return Graphite_View().viewProjection * vec4(relative, 1.0); }


vec3 Graphite_ViewRay(vec2 clip)
{
    mat4 inverse = Graphite_View().inverseViewProjection;
    vec4 near = inverse * vec4(clip, 1.0, 1.0);
    vec4 far = inverse * vec4(clip, 0.0, 1.0);
    return normalize(far.xyz * near.w - near.xyz * far.w);
}

float Graphite_LinearDepth(float deviceDepth)
{

    return Graphite_View().depth.x / max(deviceDepth, 1e-12);
}

vec3 Graphite_RelativeFromDepth(vec2 uv, float deviceDepth)
{
    vec4 clip = vec4(uv * 2.0 - 1.0, deviceDepth, 1.0);
    vec4 relative = Graphite_View().inverseViewProjection * clip;
    return relative.xyz / relative.w;
}


layout(location = 30) flat in uint graphite_ObjectIndex;

uint Graphite_ObjectIndex() { return graphite_ObjectIndex; }
Graphite_ObjectData Graphite_Object() { return graphite_Objects[graphite_ObjectIndex]; }

#line 14 "Graphite/Trees.glsl"














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


struct Graphite_RegionEntry { vec3 offset; uint root; vec3 offsetLow; uint pad; };

layout(set = 1, binding = 3, std430) readonly buffer Graphite_SpatialRecords { uvec4 words[]; } graphite_SpatialRecords[16 * 2];
layout(set = 1, binding = 4, std430) readonly buffer Graphite_SpatialNodes { Graphite_TreeNode nodes[]; } graphite_SpatialNodes[16 * 3];
layout(set = 1, binding = 5, std430) readonly buffer Graphite_SpatialRegions { Graphite_RegionEntry entries[]; } graphite_SpatialRegions[16];


layout(set = 1, binding = 6, std430) readonly buffer Graphite_SpatialRoots { uvec4 graphite_SpatialRoots[]; };


uint Graphite_StaticRecords(uint set) { return set * 2u; }
uint Graphite_MovingRecords(uint set) { return set * 2u + 1u; }


vec4 Graphite_SpatialVec4(uint array, uint word)
{
    return uintBitsToFloat(graphite_SpatialRecords[nonuniformEXT(array)].words[word]);
}

uvec4 Graphite_SpatialUVec4(uint array, uint word)
{
    return graphite_SpatialRecords[nonuniformEXT(array)].words[word];
}


uint graphite_TreeStack[48 + 8 * 2];







uvec2 graphite_Hits[32];

struct Graphite_TreeWalk
{
    uint nodes;
    int sp;
    uint next;
    uint node;
    uint overlap;
};

void Graphite_BeginTree(out Graphite_TreeWalk w, uint nodes, uint root)
{
    w.nodes = nodes;
    w.sp = 0;
    w.next = root;
    w.node = 0u;
    w.overlap = 0u;
}


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
            if ( (slot & 0x80000000u) == 0u)
            {
                if (w.next == 0xFFFFFFFFu)
                    w.next = slot;
                else if (w.sp < 48)
                    graphite_TreeStack[w.sp ++] = slot;
                continue;
            }

            first = slot & ~ 0x80000000u;
            last = first + graphite_SpatialNodes[nonuniformEXT(w.nodes)].nodes[w.node].count[i];
            return true;
        }

        if (w.next == 0xFFFFFFFFu)
        {
            if (w.sp == 0)
                return false;
            w.next = graphite_TreeStack[-- w.sp];
        }

        w.node = w.next;
        Graphite_TreeNode node = graphite_SpatialNodes[nonuniformEXT(w.nodes)].nodes[w.node];
        w.next = 0xFFFFFFFFu;
        bvec4 inside = bvec4(uvec4(greaterThanEqual(vec4(p.x), node.minX)) & uvec4(lessThanEqual(vec4(p.x), node.maxX))
                           & uvec4(greaterThanEqual(vec4(p.y), node.minY)) & uvec4(lessThanEqual(vec4(p.y), node.maxY))
                           & uvec4(greaterThanEqual(vec4(p.z), node.minZ)) & uvec4(lessThanEqual(vec4(p.z), node.maxZ)));
        w.overlap = (inside.x ? 1u : 0u) | (inside.y ? 2u : 0u) | (inside.z ? 4u : 0u) | (inside.w ? 8u : 0u);
    }
    return false;
}



struct Graphite_TreeQuery
{
    Graphite_TreeWalk items;
    uint set;
    vec3 position;
    vec3 offset;
    vec3 offsetLow;
    uint movingRoot;
    uint entry;
    uint entryEnd;
    uint region;
    int runs;
    int phase;
    bool walking;
};


void Graphite_BeginQuery(out Graphite_TreeQuery q, vec3 position, uint set)
{
    uvec4 roots = set < uint(16) ? graphite_SpatialRoots[set] : uvec4(0xFFFFFFFFu);
    q.set = set;
    q.position = position;
    q.offset = vec3(0.0);
    q.offsetLow = vec3(0.0);
    q.movingRoot = roots.y;
    q.entry = 0u;
    q.entryEnd = 0u;
    q.region = 0xFFFFFFFFu;
    q.runs = 0;
    q.phase = roots.x == 0xFFFFFFFFu && roots.y == 0xFFFFFFFFu ? 2 : 0;
    q.walking = false;
    Graphite_BeginTree(q.items, set * 3u, 0xFFFFFFFFu);
    if (roots.x == 0xFFFFFFFFu)
        return;


    Graphite_TreeWalk regions;
    Graphite_BeginTree(regions, set * 3u + 2u, roots.x);
    vec3 point = position + Graphite_View().treeOffset.xyz;
    uint first, end;
    while (q.runs < 8 && Graphite_NextLeaf(regions, point, first, end))
    {
        graphite_TreeStack[48 + q.runs * 2] = first;
        graphite_TreeStack[48 + q.runs * 2 + 1] = end;
        q.runs ++;
    }
}



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
            q.region = 0xFFFFFFFFu;
            Graphite_BeginTree(q.items, q.set * 3u + 1u, q.movingRoot);
            q.walking = true;
            continue;
        }

        if (q.entry < q.entryEnd)
        {
            q.region = q.entry;
            Graphite_RegionEntry region = graphite_SpatialRegions[nonuniformEXT(q.set)].entries[q.entry ++];
            q.offset = region.offset;
            q.offsetLow = region.offsetLow + Graphite_View().treeOffset.xyz;
            Graphite_BeginTree(q.items, q.set * 3u, region.root);
            q.walking = true;
            continue;
        }

        if (q.runs > 0)
        {
            q.runs --;
            q.entry = graphite_TreeStack[48 + q.runs * 2];
            q.entryEnd = graphite_TreeStack[48 + q.runs * 2 + 1];
            continue;
        }
        q.phase = 1;
    }
    return false;
}


void Graphite_HitOffsets(uint set, uint region, out vec3 offset, out vec3 offsetLow)
{
    vec3 base = Graphite_View().treeOffset.xyz;
    offset = vec3(0.0);
    offsetLow = base;
    if (region == 0xFFFFFFFFu)
        return;
    Graphite_RegionEntry entry = graphite_SpatialRegions[nonuniformEXT(set)].entries[region];
    offset = entry.offset;
    offsetLow = entry.offsetLow + base;
}



struct Graphite_ItemIterator
{
    Graphite_TreeQuery query;
    uint array;
    uint k;
    uint last;
    uint stride;
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


bool Graphite_NextItem(inout Graphite_ItemIterator it, out uint array, out uint index, out vec3 center, out float radius)
{
    for (;;)
    {
        while (it.k < it.last)
        {
            index = it.k ++;
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
        if (! Graphite_NextRun(it.query, it.array, it.k, it.last))
            return false;
    }
    return false;
}


uint Graphite_SurfaceLayers()
{

    return Graphite_Object().layerMask;



}
#line 12 "Graphite/Lighting.glsl"
#line 1 "Graphite/Shadows.glsl"






#line 1 "Graphite/Textures.glsl"



#extension GL_EXT_nonuniform_qualifier : require

layout(set = 0, binding = 0) uniform texture2D Graphite_Textures2D[16384];
layout(set = 0, binding = 3) uniform textureCube Graphite_TexturesCube[2048];
layout(set = 1, binding = 0) uniform sampler Graphite_Samplers[64];





#line 8 "Graphite/Shadows.glsl"

struct Graphite_Cascade
{
    mat4 viewToClip;
    vec4 sphere;
    vec4 params;
    vec4 fade;
    vec4 depth;
};

layout(set = 1, binding = 28) uniform samplerShadow Graphite_ShadowSampler;




layout(set = 1, binding = 29, std430) readonly buffer Graphite_ShadowFaces { vec4 graphite_ShadowFaces[]; };

vec4 Graphite_FaceRect(uint face) { return graphite_ShadowFaces[face * 3u]; }
vec4 Graphite_FaceParams(uint face) { return graphite_ShadowFaces[face * 3u + 1u]; }
vec4 Graphite_FaceDepth(uint face) { return graphite_ShadowFaces[face * 3u + 2u]; }
layout(set = 1, binding = 30, std430) readonly buffer Graphite_Cascades { Graphite_Cascade graphite_Cascades[]; };


#pragma variantglobalGRAPHITE_CONTACT_SHADOWS








bool Graphite_ReceivesShadows()
{

    return(Graphite_Object().flags & 4u) != 0u;



}


vec3 Graphite_ShadowBias(vec3 position, vec3 geomNormal, vec3 toLight, float texelWorld, float depthBias, float normalBias)
{
    float NdotL = clamp(dot(geomNormal, toLight), 0.0, 1.0);
    float sinTheta = sqrt(1.0 - NdotL * NdotL);
    return position + (toLight * depthBias + geomNormal * (normalBias * sinTheta)) * texelWorld;
}


vec3 Graphite_ShadowCoords(vec4 clip, vec4 rect)
{
    vec3 ndc = clip.xyz / clip.w;
    vec2 local = vec2(ndc.x * 0.5 + 0.5, 0.5 - ndc.y * 0.5);
    return vec3(rect.xy + local * rect.z, clamp(ndc.z, 0.0, 1.0));
}


vec2 graphite_TentTap(int tap, float f)
{
    if (tap == 0)
    {
        float w = 4.0 - 3.0 * f;
        return vec2(w, (3.0 - 2.0 * f) / w - 2.0);
    }
    if (tap == 1)
        return vec2(7.0, (3.0 + f) / 7.0);
    float w = 1.0 + 3.0 * f;
    return vec2(w, f / w + 2.0);
}


float Graphite_SampleShadow(uint slot, vec4 rect, float inverseSize, vec3 coords, float filtering)
{
    vec2 tileMin = rect.xy + 0.5 * inverseSize;
    vec2 tileMax = rect.xy + rect.z - 0.5 * inverseSize;
    if (filtering == 0.0)
        return texture(sampler2DShadow(Graphite_Textures2D[nonuniformEXT(slot)], Graphite_ShadowSampler), vec3(clamp(coords.xy, tileMin, tileMax), coords.z));



    vec2 texel = coords.xy / inverseSize;
    vec2 base = floor(texel + 0.5);
    vec2 f = texel + 0.5 - base;
    base -= 0.5;

    float lit = 0.0;
    for (int j = 0; j < 3; j ++)
    {
        vec2 y = graphite_TentTap(j, f.y);
        for (int i = 0; i < 3; i ++)
        {
            vec2 x = graphite_TentTap(i, f.x);
            vec2 uv = clamp( (base + vec2(x.y, y.y)) * inverseSize, tileMin, tileMax);
            lit += x.x * y.x * texture(sampler2DShadow(Graphite_Textures2D[nonuniformEXT(slot)], Graphite_ShadowSampler), vec3(uv, coords.z));
        }
    }
    return lit / 144.0;
}


vec2 Graphite_VogelDisc(int index, int count, float angle)
{
    float r = sqrt( (float(index) + 0.5) / float(count));
    float theta = float(index) * 2.39996323 + angle;
    return r * vec2(cos(theta), sin(theta));
}


float Graphite_ShadowNoiseAngle()
{

    vec3 magic = vec3(0.06711056, 0.00583715, 52.9829189);
    return 6.28318531 * fract(magic.z * fract(dot(gl_FragCoord.xy, magic.xy)));



}


void Graphite_ShadowFrame(vec3 direction, out vec3 t1, out vec3 t2)
{
    t1 = normalize(cross(abs(direction.y) < 0.99 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0), direction));
    t2 = cross(direction, t1);
}


uint Graphite_CubeFace(vec3 d)
{
    vec3 b = abs(d);
    return b.x >= b.y && b.x >= b.z ? (d.x > 0.0 ? 0u : 1u) : b.y >= b.z ? (d.y > 0.0 ? 2u : 3u) : (d.z > 0.0 ? 4u : 5u);
}


vec3 Graphite_CubeView(uint face, vec3 d)
{
    switch (face)
    {
        case 0u : return vec3(- d.z, - d.y, d.x);
        case 1u : return vec3(d.z, - d.y, - d.x);
        case 2u : return vec3(d.x, d.z, d.y);
        case 3u : return vec3(d.x, - d.z, - d.y);
        case 4u : return vec3(d.x, - d.y, d.z);
        default : return vec3(- d.x, - d.y, - d.z);
    }
}



vec4 Graphite_FaceClip(uint first, uint face, bool cube, vec3 d)
{
    if (! cube)
    {
        uint w = first * 3u + 3u;
        return mat4(graphite_ShadowFaces[w], graphite_ShadowFaces[w + 1u], graphite_ShadowFaces[w + 2u], graphite_ShadowFaces[w + 3u]) * vec4(d, 1.0);
    }
    vec3 v = Graphite_CubeView(face - first, d);
    vec2 nearFar = Graphite_FaceDepth(face).xy;
    return vec4(v.xy, nearFar.x * (nearFar.y - v.z) / (nearFar.y - nearFar.x), v.z);
}


vec3 Graphite_ShadowPlane(vec3 geomNormal, vec3 toLight)
{
    return dot(geomNormal, geomNormal) > 1e-8 ? geomNormal : toLight;
}


float Graphite_FaceAxis(vec2 nearFar, float depth)
{
    return nearFar.x * nearFar.y / (depth * (nearFar.y - nearFar.x) + nearFar.x);
}

struct Graphite_LocalTap
{
    vec3 coords;
    uint face;
};




Graphite_LocalTap Graphite_LocalShadowTap(uint first, bool cube, vec3 receiver, vec3 normal, vec3 t1, vec3 t2, vec2 offset)
{
    vec3 q = receiver + t1 * offset.x + t2 * offset.y;
    float along = dot(normal, q);
    q *= clamp(abs(along) > 1e-6 ? dot(normal, receiver) / along : 1.0, 0.5, 2.0);
    Graphite_LocalTap tap;
    tap.face = cube ? first + Graphite_CubeFace(q) : first;
    tap.coords = Graphite_ShadowCoords(Graphite_FaceClip(first, tap.face, cube, q), Graphite_FaceRect(tap.face));
    return tap;
}




float Graphite_LocalContact(uint first, bool cube, uint centre, vec3 receiver, vec3 normal, float lightRadius, float receiverAxis)
{
    vec4 centreRect = Graphite_FaceRect(centre);
    float inverseSize = Graphite_FaceParams(centre).z;
    float texelWorld = centreRect.w * max(receiverAxis, 1e-4);
    float largest = centreRect.z / inverseSize * 0.125;
    float near = Graphite_FaceDepth(centre).x;
    float search = clamp(lightRadius * max(receiverAxis - near, 0.0) / (max(receiverAxis, 1e-4) * near * centreRect.w), 1.5, largest);
    vec3 t1, t2;
    Graphite_ShadowFrame(normalize(receiver), t1, t2);
    float angle = Graphite_ShadowNoiseAngle();

    float ratio = 0.0;
    int found = 0;
    for (int i = 0; i <= 16; i ++)
    {
        vec2 offset = i < 16 ? Graphite_VogelDisc(i, 16, angle) * search * texelWorld : vec2(0.0);
        Graphite_LocalTap tap = Graphite_LocalShadowTap(first, cube, receiver, normal, t1, t2, offset);
        vec4 rect = Graphite_FaceRect(tap.face);
        vec2 texel = clamp(tap.coords.xy / inverseSize, rect.xy / inverseSize, (rect.xy + rect.z) / inverseSize - 1.0);
        float depth = texelFetch(sampler2D(Graphite_Textures2D[nonuniformEXT(floatBitsToUint(Graphite_FaceParams(tap.face).x))], Graphite_Samplers[0]), ivec2(texel), 0).r;
        if (depth > tap.coords.z)
        {
            vec2 nearFar = Graphite_FaceDepth(tap.face).xy;
            ratio += Graphite_FaceAxis(nearFar, depth) / Graphite_FaceAxis(nearFar, tap.coords.z);
            found ++;
        }
    }
    if (found == 0)
        return 1.0;

    float radius = clamp(lightRadius * (float(found) / max(ratio, 1e-4) - 1.0) / texelWorld, 1.5, search);
    float lit = 0.0;
    for (int i = 0; i < 16; i ++)
    {
        Graphite_LocalTap tap = Graphite_LocalShadowTap(first, cube, receiver, normal, t1, t2, Graphite_VogelDisc(i, 16, angle) * radius * texelWorld);
        vec4 rect = Graphite_FaceRect(tap.face);
        vec2 uv = clamp(tap.coords.xy, rect.xy + 0.5 * inverseSize, rect.xy + rect.z - 0.5 * inverseSize);
        lit += texture(sampler2DShadow(Graphite_Textures2D[nonuniformEXT(floatBitsToUint(Graphite_FaceParams(tap.face).x))], Graphite_ShadowSampler), vec3(uv, tap.coords.z));
    }
    return lit / float(16);
}


vec3 Graphite_CascadeTap(Graphite_Cascade c, vec3 receiver, vec3 normal, vec3 toLight, vec3 t1, vec3 t2, vec2 offset)
{
    vec3 q = receiver + t1 * offset.x + t2 * offset.y;
    float facing = dot(normal, toLight);
    float slide = abs(facing) > 1e-4 ? dot(normal, receiver - q) / facing : 0.0;
    float limit = 4.0 * length(offset);
    q += toLight * clamp(slide, - limit, limit);
    return Graphite_ShadowCoords(c.viewToClip * vec4(q, 1.0), vec4(0.0, 0.0, 1.0, 0.0));
}




float Graphite_CascadeContact(Graphite_Cascade c, vec3 receiver, vec3 normal, vec3 toLight, float tanRadius, float texelWorld)
{
    float inverseSize = c.params.y;
    uint slot = floatBitsToUint(c.params.x);
    vec3 centre = Graphite_ShadowCoords(c.viewToClip * vec4(receiver, 1.0), vec4(0.0, 0.0, 1.0, 0.0));
    vec2 edge = min(centre.xy, 1.0 - centre.xy) / inverseSize - 1.0;
    float largest = max(min(0.125 / inverseSize, min(edge.x, edge.y)), 1.5);
    float reach = min( (1.0 - centre.z) * c.depth.x, 2.0 * c.sphere.w);
    float search = clamp(reach * tanRadius / texelWorld, 1.5, largest);
    vec3 t1, t2;
    Graphite_ShadowFrame(toLight, t1, t2);
    float angle = Graphite_ShadowNoiseAngle();
    float last = 1.0 / inverseSize - 1.0;

    float gap = 0.0;
    int found = 0;
    for (int i = 0; i <= 16; i ++)
    {
        vec2 offset = i < 16 ? Graphite_VogelDisc(i, 16, angle) * search * texelWorld : vec2(0.0);
        vec3 coords = Graphite_CascadeTap(c, receiver, normal, toLight, t1, t2, offset);
        float depth = texelFetch(sampler2D(Graphite_Textures2D[nonuniformEXT(slot)], Graphite_Samplers[0]), ivec2(clamp(coords.xy / inverseSize, vec2(0.0), vec2(last))), 0).r;
        if (depth > coords.z)
        {
            gap += depth - coords.z;
            found ++;
        }
    }
    if (found == 0)
        return 1.0;

    float radius = clamp(gap / float(found) * c.depth.x * tanRadius / texelWorld, 1.5, search);
    float lit = 0.0;
    for (int i = 0; i < 16; i ++)
    {
        vec3 coords = Graphite_CascadeTap(c, receiver, normal, toLight, t1, t2, Graphite_VogelDisc(i, 16, angle) * radius * texelWorld);
        vec2 uv = clamp(coords.xy, vec2(0.5 * inverseSize), vec2(1.0 - 0.5 * inverseSize));
        lit += texture(sampler2DShadow(Graphite_Textures2D[nonuniformEXT(slot)], Graphite_ShadowSampler), vec3(uv, coords.z));
    }
    return lit / float(16);
}





float Graphite_LocalShadow(int shadowSlot, bool cube, vec3 lightPosition, vec3 lightDirection, float lightRadius, vec3 position, vec3 geomNormal)
{
    uint first = uint(shadowSlot) * 6u;
    vec3 toPosition = position - lightPosition;
    uint index = cube ? first + Graphite_CubeFace(toPosition) : first;
    vec4 rect = Graphite_FaceRect(index);
    vec4 params = Graphite_FaceParams(index);
    float fade = params.y;
    if (fade <= 0.0 || rect.z <= 0.0)
        return 1.0;
    vec2 bias = Graphite_FaceDepth(index).zw;
    float filtering = params.w;

    vec3 toLight = - toPosition / max(length(toPosition), 1e-6);
    vec3 a = abs(toPosition);
    float axisDistance = cube ? max(a.x, max(a.y, a.z)) : max(dot(toPosition, lightDirection), 0.0);
    vec3 d = Graphite_ShadowBias(position, geomNormal, toLight, rect.w * axisDistance, bias.x, bias.y) - lightPosition;
    if (cube)
    {
        uint biased = first + Graphite_CubeFace(d);
        if (biased != index)
        {
            index = biased;
            rect = Graphite_FaceRect(index);
            params = Graphite_FaceParams(index);
            if (rect.z <= 0.0)
                return 1.0;
        }
    }
    vec4 clip = Graphite_FaceClip(first, index, cube, d);
    if (clip.w <= 0.0 || (! cube && any(greaterThan(abs(clip.xy), vec2(clip.w)))))
        return 1.0;





    return mix(1.0, Graphite_SampleShadow(floatBitsToUint(params.x), rect, params.z, Graphite_ShadowCoords(clip, rect), min(filtering, 1.0)), fade);
}


bool Graphite_InCascade(vec3 p, uint cascade, float inset)
{
    vec4 sphere = graphite_Cascades[cascade].sphere;
    vec3 d = p - sphere.xyz;
    float r = sphere.w * (1.0 - 2.0 * inset * graphite_Cascades[cascade].params.y);
    return dot(d, d) <= r * r;
}


float Graphite_CascadeLit(uint cascade, vec3 position, vec3 geomNormal, vec3 toLight, float depthBias, float normalBias, float filtering, float tanRadius)
{
    vec4 params = graphite_Cascades[cascade].params;
    float texelWorld = 2.0 * graphite_Cascades[cascade].sphere.w * params.y;
    vec3 biased = Graphite_ShadowBias(position, geomNormal, toLight, texelWorld, depthBias, normalBias);




    vec4 rect = vec4(0.0, 0.0, 1.0, 0.0);
    return Graphite_SampleShadow(floatBitsToUint(params.x), rect, params.y, Graphite_ShadowCoords(graphite_Cascades[cascade].viewToClip * vec4(biased, 1.0), rect), min(filtering, 1.0));
}



float Graphite_DirectionalShadow(uint directional, vec3 toLight, float depthBias, float normalBias, float strength, float filtering, float tanRadius, vec3 position, vec3 geomNormal)
{
    uint first = directional * 4u;
    uint count = uint(graphite_Cascades[first].params.z);
    if (count == 0u)
        return 1.0;
    vec4 fadeAxis = graphite_Cascades[first].fade;
    float viewDepth = dot(position, fadeAxis.xyz);
    float distance = fadeAxis.w;
    if (viewDepth > distance)
        return 1.0;


    int pick = - 1;
    for (uint c = 0u; c < count && pick < 0; c ++)
    {
        if (Graphite_InCascade(position, first + c, 3.0))
            pick = int(c);
    }
    for (uint c = 0u; c < count && pick < 0; c ++)
    {
        if (Graphite_InCascade(position, first + c, 0.0))
            pick = int(c);
    }
    if (pick < 0)
        return 1.0;


    uint cascade = first + uint(pick);
    float blend = graphite_Cascades[cascade].params.w, w = 0.0;
    if (blend > 0.0 && uint(pick) + 1u < count && Graphite_InCascade(position, cascade + 1u, 3.0))
    {
        vec4 sphere = graphite_Cascades[cascade].sphere;
        vec3 d = position - sphere.xyz;
        float inner = sphere.w * (1.0 - 2.0 * 3.0 * graphite_Cascades[cascade].params.y);
        w = clamp( (sqrt(dot(d, d)) / max(inner, 1e-6) - (1.0 - blend)) / blend, 0.0, 1.0);
    }
    float lit = 0.0;
    for (uint k = 0u; k < 2u; k ++)
    {
        float weight = k == 0u ? 1.0 - w : w;
        if (weight > 0.0)
            lit += weight * Graphite_CascadeLit(cascade + k, position, geomNormal, toLight, depthBias, normalBias, filtering, tanRadius);
    }

    float fade = smoothstep(distance * 0.9, distance, viewDepth);
    return mix(1.0, lit, strength * (1.0 - fade));
}
#line 13 "Graphite/Lighting.glsl"
#line 1 "Graphite/PBR.glsl"






const float PI = 3.14159265359;











float DistributionGGX(vec3 N, vec3 H, float roughness)
{


    roughness = max(roughness, 0.045);

    float a2 = roughness * roughness;
    float NdotH = max(dot(N, H), 0.0);
    float NdotH2 = NdotH * NdotH;

    float denom = (NdotH2 * (a2 - 1.0) + 1.0);
    denom = PI * denom * denom;

    return a2 / denom;
}



float GeometrySchlickGGX(float NdotV, float roughness)
{
    float r = (roughness + 1.0);
    float k = (r * r) / 8.0;

    return NdotV / (NdotV * (1.0 - k) + k);
}



float GeometrySmith(vec3 N, vec3 V, vec3 L, float roughness)
{
    float NdotV = abs(dot(N, V));
    float NdotL = max(dot(N, L), 0.0);
    float ggx2 = GeometrySchlickGGX(NdotV, roughness);
    float ggx1 = GeometrySchlickGGX(NdotL, roughness);

    return ggx1 * ggx2;
}


vec3 FresnelSchlick(float cosTheta, vec3 F0)
{
    return F0 + (1.0 - F0) * exp2(- 9.28 * cosTheta);
}


vec3 EnvBRDF(vec3 F0, float roughness, float NdotV)
{
    vec2 t = clamp(NdotV * (vec2(- 1.443, - 1.49143) + vec2(1.83297, 1.3451) * roughness) + (vec2(0.841227, 0.96635) + vec2(- 0.12654, - 0.705379) * roughness), 0.0, 1.0);
    t *= t;
    float scale = 1.01549 - 0.135605 * roughness + t.x * (0.558371 * roughness - 1.08784);
    float bias = t.y * (0.805868 - 0.770431 * roughness);
    return F0 * scale + bias;
}







float DisneyDiffuse(float NdotV, float NdotL, float LdotH, float perceptualRoughness)
{
    float fd90 = 0.5 + 2.0 * LdotH * LdotH * perceptualRoughness;
    float lightScatter = 1.0 + (fd90 - 1.0) * exp2(- 8.35 * NdotL);
    float viewScatter = 1.0 + (fd90 - 1.0) * exp2(- 8.35 * NdotV);
    return lightScatter * viewScatter / PI;
}








float DistributionGGXAniso(float TdotH, float BdotH, float NdotH, float mt, float mb)
{


    const float minAlpha = 0.045 * 0.045;
    mt = max(mt, minAlpha);
    mb = max(mb, minAlpha);

    float d = TdotH * TdotH / (mt * mt) + BdotH * BdotH / (mb * mb) + NdotH * NdotH;
    return 1.0 / (PI * mt * mb * d * d);
}



float GeometrySmithAniso(
    float TdotV, float BdotV, float NdotV,
    float TdotL, float BdotL, float NdotL,
    float mt, float mb)
{
    float lambdaV = NdotL * sqrt(mt * TdotV * TdotV + mb * BdotV * BdotV + NdotV * NdotV);
    float lambdaL = NdotV * sqrt(mt * TdotL * TdotL + mb * BdotL * BdotL + NdotL * NdotL);
    return 0.5 / max(1e-5, lambdaV + lambdaL);
}











float ApplySpecularAA(float roughness, vec3 worldNormal)
{
    vec3 ddxN = dFdx(worldNormal);
    vec3 ddyN = dFdy(worldNormal);
    float variance = 0.5 * (dot(ddxN, ddxN) + dot(ddyN, ddyN));
    float kernelRoughnessSq = min(2.0 * variance, 0.1);
    float squaredRoughness = clamp(roughness * roughness + kernelRoughnessSq, 0.0, 1.0);
    return sqrt(squaredRoughness);
}












vec2 ParallaxOcclusionMapping(sampler2D heightTex, vec2 uv, vec3 viewDirTS, float heightScale, int linearSteps)
{

    float slopeDamp = 1.0 - clamp(dot(viewDirTS, vec3(0.0, 0.0, 1.0)), 0.0, 1.0);


    vec2 vMaxOffset = (viewDirTS.xy / - viewDirTS.z) * heightScale * (1.0 - slopeDamp * slopeDamp);

    float fStepSize = 1.0 / float(linearSteps);
    vec2 finalStepSize = fStepSize * vMaxOffset;


    vec2 dx = dFdx(uv);
    vec2 dy = dFdy(uv);


    float fCurrRayHeight = 1.0;
    vec2 vCurrOffset = vec2(0.0);
    vec2 vLastOffset = vec2(0.0);
    float fLastSampledHeight = 1.0;
    float fCurrSampledHeight = 1.0;

    for (int i = 0; i < linearSteps; i ++)
    {
        fCurrSampledHeight = textureGrad(heightTex, uv + vCurrOffset, dx, dy).g;

        if (fCurrSampledHeight > fCurrRayHeight)
            break;

        fCurrRayHeight -= fStepSize;
        vLastOffset = vCurrOffset;
        vCurrOffset += finalStepSize;
        fLastSampledHeight = fCurrSampledHeight;
    }


    float pt0 = fCurrRayHeight + fStepSize;
    float pt1 = fCurrRayHeight;
    float delta0 = pt0 - fLastSampledHeight;
    float delta1 = pt1 - fCurrSampledHeight;

    for (int j = 0; j < 3; j ++)
    {


        float secantDenom = delta1 - delta0;
        if (! (abs(secantDenom) > 1e-8)) break;

        float intersectionHeight = (pt0 * delta1 - pt1 * delta0) / secantDenom;
        vCurrOffset = (1.0 - intersectionHeight) * finalStepSize * float(linearSteps);

        fCurrSampledHeight = textureGrad(heightTex, uv + vCurrOffset, dx, dy).g;

        float delta = intersectionHeight - fCurrSampledHeight;
        if (delta < 0.0)
        {
            delta1 = delta;
            pt1 = intersectionHeight;
        }
        else
        {
            delta0 = delta;
            pt0 = intersectionHeight;
        }
    }

    return uv + vCurrOffset;
}














vec3 CalculateTranslucency(vec3 lightDir, vec3 viewDir, vec3 normal,
                           float translucency, float scatteringPower,
                           float distortion, float scale, vec3 lightColor)
{
    if (translucency <= 0.0) return vec3(0.0);

    vec3 lightScattering = vec3(0.0);

    if (scatteringPower < 0.001)
    {


        float wrap = 0.5;
        float wrappedNdotL = clamp( (dot(- normal, lightDir) + wrap) / ( (1.0 + wrap) * (1.0 + wrap)), 0.0, 1.0);


        float VdotL = clamp(dot(viewDir, - lightDir), 0.0, 1.0);
        float a2 = 0.49;
        float d = (VdotL * a2 - VdotL) * VdotL + 1.0;
        float GGX = (a2 / PI) / (d * d);

        lightScattering = wrappedNdotL * GGX * translucency * lightColor;
    }
    else
    {


        vec3 transLightDir = lightDir + normal * distortion;
        float transDot = dot(- transLightDir, viewDir);
        transDot = exp2(clamp(transDot, 0.0, 1.0) * scatteringPower - scatteringPower) * translucency;

        lightScattering = transDot * lightColor;
    }

    return lightScattering * scale;
}
#line 14 "Graphite/Lighting.glsl"



#pragma variantglobalGRAPHITE_LIGHT_MASKS







struct Graphite_LightRecord
{
    vec4 positionRange;
    vec4 colorIntensity;
    vec4 directionType;
    vec4 upSize;
    vec4 shape;
    vec4 spotShadow;
    vec4 shadow;
    uvec4 textures;
    vec4 hostData;
};

struct Graphite_DirectionalRecord
{
    vec4 directionShadow;
    vec4 color;
    vec4 params;
    vec4 hostData;
    vec4 shadow;
};

layout(set = 1, binding = 27, std430) readonly buffer Graphite_Directionals { Graphite_DirectionalRecord graphite_Directionals[]; };
layout(set = 1, binding = 22, std430) readonly buffer Graphite_WorldLighting
{
    uvec4 counts;
    uvec4 reflections;
    vec4 ambientColor;
    vec4 ambientSky;
    vec4 ambientGround;
    vec4 ambientMode;
    vec4 fogColor;
    vec4 fogParams;
    vec4 fogStates;
} graphite_World;




struct LightSample
{
    vec3 Position;
    float Range;
    int Type;
    uint RecordArray;
    uint RecordWord;
    vec3 Color;
    float Intensity;
    vec3 Direction;
    float SourceRadius;
    float SpotCos;
    float InnerSpotCos;
};


LightSample Graphite_NewLightSample(vec3 position, float range, vec3 color, float intensity)
{
    LightSample L;
    L.Position = position;
    L.Range = range;
    L.Type = 1;
    L.RecordArray = 0xFFFFFFFFu;
    L.RecordWord = 0u;
    L.Color = color;
    L.Intensity = intensity;
    L.Direction = vec3(0.0, 0.0, 1.0);
    L.SourceRadius = 0.0;
    L.SpotCos = - 1.0;
    L.InnerSpotCos = 1.0;
    return L;
}


vec4 graphite_LightWord(LightSample L, uint word, vec4 fallback)
{
    return L.RecordArray == 0xFFFFFFFFu ? fallback : Graphite_SpatialVec4(L.RecordArray, L.RecordWord + word);
}


vec3 Graphite_LightDirection(LightSample L) { return graphite_LightWord(L, 2u, vec4(L.Direction, 0.0)).xyz; }


uint Graphite_LightFlags(LightSample L) { return floatBitsToUint(graphite_LightWord(L, 2u, vec4(0.0)).w); }


float Graphite_LightSourceRadius(LightSample L) { return graphite_LightWord(L, 4u, vec4(0.0, L.SourceRadius, 0.0, 0.0)).y; }


vec2 Graphite_LightSpotCos(LightSample L) { return graphite_LightWord(L, 5u, vec4(L.SpotCos, L.InnerSpotCos, 0.0, 0.0)).xy; }


vec3 Graphite_LightUp(LightSample L) { return graphite_LightWord(L, 3u, vec4(0.0, 1.0, 0.0, 0.0)).xyz; }


vec2 Graphite_LightSize(LightSample L) { return vec2(graphite_LightWord(L, 3u, vec4(0.0)).w, graphite_LightWord(L, 4u, vec4(0.0)).x); }


uvec3 Graphite_LightTextures(LightSample L) { return floatBitsToUint(graphite_LightWord(L, 7u, vec4(0.0)).xyz); }


vec4 Graphite_LightHostData(LightSample L) { return graphite_LightWord(L, 8u, vec4(0.0)); }


vec3 Graphite_LightRadiance(LightSample L)
{
    vec4 colorIntensity = graphite_LightWord(L, 1u, vec4(L.Color, L.Intensity));
    return colorIntensity.rgb * colorIntensity.w;
}


float Graphite_LocalLightShadow(LightSample L, vec3 worldPos, vec3 geomNormal)
{
    if (L.RecordArray == 0xFFFFFFFFu || (L.Type != 1 && L.Type != 2) || ! Graphite_ReceivesShadows())
        return 1.0;
    int slot = floatBitsToInt(Graphite_SpatialVec4(L.RecordArray, L.RecordWord + 6u).z);
    if (slot < 0)
        return 1.0;
    return Graphite_LocalShadow(slot, L.Type == 1, L.Position, Graphite_LightDirection(L), Graphite_LightSourceRadius(L), worldPos, geomNormal);
}


void Graphite_ReadLight(uint array, uint index, vec3 offset, vec3 offsetLow, out LightSample L)
{
    uint w = index * 9u;
    vec4 positionRange = Graphite_SpatialVec4(array, w);
    precise vec3 position = (positionRange.xyz - offset) - offsetLow;
    L.Position = position;
    L.Range = positionRange.w;
    L.Type = int(floatBitsToUint(Graphite_SpatialVec4(array, w + 2u).w) & 15u);
    L.RecordArray = array;
    L.RecordWord = w;
    L.Color = vec3(0.0);
    L.Intensity = 0.0;
    L.Direction = vec3(0.0, 0.0, 1.0);
    L.SourceRadius = 0.0;
    L.SpotCos = - 1.0;
    L.InnerSpotCos = 1.0;
}




struct Graphite_LightIterator
{
    uint next;
    uint count;
};

Graphite_LightIterator Graphite_BeginLights(vec3 worldPos)
{
    Graphite_LightIterator it;
    it.next = 0u;
    it.count = 0u;
    uint layers = Graphite_SurfaceLayers();
    Graphite_TreeQuery q;
    Graphite_BeginQuery(q, worldPos, 0u);
    uint array, first, last;
    while (it.count < uint(32) && Graphite_NextRun(q, array, first, last))
    {
        for (uint k = first; k < last && it.count < uint(32); k ++)
        {
            vec4 positionRange = Graphite_SpatialVec4(array, k * 9u);
            precise vec3 position = (positionRange.xyz - q.offset) - q.offsetLow;
            vec3 d = worldPos - position;
            if (dot(d, d) <= positionRange.w * positionRange.w && (floatBitsToUint(Graphite_SpatialVec4(array, k * 9u + 6u).w) & layers) != 0u)
                graphite_Hits[it.count ++] = uvec2(k | ( (array & 1u) << 31), q.region);
        }
    }
    return it;
}

bool Graphite_NextLight(inout Graphite_LightIterator it, out LightSample L)
{
    if (it.next >= it.count)
        return false;
    uvec2 hit = graphite_Hits[it.next ++];
    vec3 offset, offsetLow;
    Graphite_HitOffsets(0u, hit.y, offset, offsetLow);
    Graphite_ReadLight(0u * 2u + (hit.x >> 31), hit.x & 0x7FFFFFFFu, offset, offsetLow, L);
    return true;
}

struct Graphite_DirectionalLight
{
    vec3 direction;
    vec3 color;
    float sinRadius;
    float cosRadius;
    bool shadow;
    uint layers;
    vec4 hostData;
    float shadowDepthBias;
    float shadowNormalBias;
    float shadowStrength;
    float shadowFilter;
};

uint Graphite_DirectionalCount() { return graphite_World.counts.x; }


Graphite_DirectionalLight Graphite_GetDirectional(uint index)
{
    Graphite_DirectionalRecord r = graphite_Directionals[index];
    Graphite_DirectionalLight d;
    d.direction = normalize(r.directionShadow.xyz);
    d.color = r.color.rgb;
    d.sinRadius = r.params.x;
    d.cosRadius = r.params.y;
    d.shadow = r.directionShadow.w > 0.5;
    d.layers = floatBitsToUint(r.color.w);
    d.hostData = r.hostData;
    d.shadowDepthBias = r.shadow.x;
    d.shadowNormalBias = r.shadow.y;
    d.shadowStrength = r.shadow.z;
    d.shadowFilter = r.shadow.w;
    return d;
}


vec3 GetWorldViewDir(vec3 worldPos)
{
    return normalize(- worldPos);
}


vec3 GetTangentViewDir(vec3 worldPos, vec3 worldNormal, vec3 worldTangent, vec3 worldBitangent)
{
    vec3 viewDir = GetWorldViewDir(worldPos);
    return normalize(vec3(dot(viewDir, normalize(worldTangent)), dot(viewDir, normalize(worldBitangent)), dot(viewDir, normalize(worldNormal))));
}





struct Graphite_Surface
{
    vec3 position;
    vec3 normal;
    vec3 geomNormal;
    vec3 view;
    vec3 albedo;
    float metallic;
    float roughness;
    float ao;
};


vec3 Graphite_SurfaceF0(Graphite_Surface s) { return mix(vec3(0.04), s.albedo, s.metallic); }

Graphite_Surface Graphite_MakeSurface(vec3 worldPos, vec3 worldNormal, vec3 geomNormal, vec3 viewDir, vec3 albedo, float metallic, float roughness, float ao)
{
    Graphite_Surface s;
    s.position = worldPos;
    s.normal = worldNormal;
    s.geomNormal = geomNormal;
    s.view = viewDir;
    s.albedo = albedo;
    s.metallic = metallic;
    s.roughness = roughness;
    s.ao = ao;
    return s;
}

struct Graphite_LightContribution
{
    vec3 radiance;
    vec3 diffuseL;
    float diffuse;
    vec3 specularL;
    float specular;
    vec2 extent;
    float attenuation;
};


float Graphite_Window(float dist2, float range)
{
    float factor = dist2 / max(range * range, 1e-6);
    float window = clamp(1.0 - factor * factor, 0.0, 1.0);
    return window * window;
}


float Graphite_AreaEnergy(float roughness, float ex, float ey)
{
    float a = max(roughness, 0.045);
    return(a * a) / (min(sqrt(a * a + ex * ex), 1.0) * min(sqrt(a * a + ey * ey), 1.0));
}



float Graphite_SphereIrradiance(float cosTheta, float sinSigma2)
{
    if (sinSigma2 >= 1.0)
        return PI;
    if (cosTheta * cosTheta > sinSigma2)
        return PI * sinSigma2 * max(cosTheta, 0.0);
    float sinTheta = sqrt(max(1.0 - cosTheta * cosTheta, 0.0));
    float x = sqrt(max(1.0 / sinSigma2 - 1.0, 0.0));
    float y = clamp(- x * cosTheta / max(sinTheta, 1e-6), - 1.0, 1.0);
    float sinThetaSqrtY = sinTheta * sqrt(max(1.0 - y * y, 0.0));
    return max( (cosTheta * acos(y) - x * sinThetaSqrtY) * sinSigma2 + atan(sinThetaSqrtY, max(x, 1e-6)), 0.0);
}

float Graphite_EdgeIntegral(vec3 a, vec3 b, vec3 n)
{
    vec3 axb = cross(a, b);
    float s = length(axb);
    return s > 1e-12 ? atan(s, dot(a, b)) * dot(axb, n) / s : 0.0;
}


float Graphite_QuadIrradiance(vec3 p0, vec3 p1, vec3 p2, vec3 p3, vec3 n)
{
    vec3 q[4] = vec3[4] (p0, p1, p2, p3);
    vec3 v[8];
    int count = 0;
    for (int i = 0; i < 4; i ++)
    {
        vec3 a = q[i], b = q[ (i + 1) & 3];
        float da = dot(a, n), db = dot(b, n);
        if (da >= 0.0)
            v[count ++] = a;
        if ( (da >= 0.0) != (db >= 0.0))
            v[count ++] = a + (b - a) * (da / (da - db));
    }
    if (count < 3)
        return 0.0;
    float sum = 0.0;
    for (int i = 0; i < count; i ++)
    {
        vec3 a = v[i], b = v[ (i + 1) % count];
        float la = length(a), lb = length(b);
        if (la > 1e-6 && lb > 1e-6)
            sum += Graphite_EdgeIntegral(a / la, b / lb, n);
    }
    return 0.5 * abs(sum);
}


float Graphite_LineIrradiance(vec3 a, vec3 b, vec3 n)
{
    vec3 d = b - a;
    float len = length(d);
    if (len < 1e-3)
    {
        vec3 m = 0.5 * (a + b);
        float m2 = max(dot(m, m), 1e-4);
        return max(dot(n, m), 0.0) / (m2 * sqrt(m2));
    }
    vec3 u = d / len;
    float na = dot(n, a), nu = dot(n, u);
    float s0 = 0.0, s1 = len;
    if (abs(nu) > 1e-6)
    {
        float horizon = - na / nu;
        if (nu > 0.0)
            s0 = max(s0, horizon);
        else
            s1 = min(s1, horizon);
    }
    else if (na < 0.0)
    {
        return 0.0;
    }
    if (s1 <= s0)
        return 0.0;



    float bu = dot(a, u);
    vec3 perp = a - u * bu;
    float D = max(dot(perp, perp), 1e-20);
    float np = dot(n, perp);
    float q0 = max(length(a + u * s0), 1e-6), q1 = max(length(a + u * s1), 1e-6);
    float t0 = s0 + bu, t1 = s1 + bu;


    float spread = t0 * t1 > 0.0
        ? (t1 * t1 - t0 * t0) / (q0 * q1 * (t1 * q0 + t0 * q1))
        : (t1 / q1 - t0 / q0) / D;
    return max(np * spread - nu / q1 + nu / q0, 0.0) / len;
}


vec3 Graphite_SpherePoint(vec3 center, float radius, vec3 R)
{
    vec3 toRay = dot(center, R) * R - center;
    return center + toRay * clamp(radius / max(length(toRay), 1e-6), 0.0, 1.0);
}


vec3 Graphite_PlanePoint(vec3 center, vec3 n, vec3 right, vec3 up, vec2 halfSize, bool disk, vec3 R)
{
    float facing = dot(R, n);
    vec3 hit = facing < - 1e-4 ? R * (dot(center, n) / facing) : center + (R - n * facing) * 1e4;
    vec3 local = hit - center;
    vec2 p = vec2(dot(local, right), dot(local, up));
    if (disk)
        p *= min(1.0, halfSize.x / max(length(p), 1e-6));
    else
        p = clamp(p, - halfSize, halfSize);
    return center + right * p.x + up * p.y;
}


vec3 Graphite_LightMask(LightSample L, vec3 w)
{
    vec3 mask = vec3(1.0);
































    return mask;
}


bool Graphite_ContributeLocal(LightSample L, Graphite_Surface s, bool backLit, out Graphite_LightContribution c)
{
    vec3 toLight = L.Position - s.position;
    float dist2 = dot(toLight, toLight);
    float dist = sqrt(dist2);
    vec3 Lc = toLight * (1.0 / max(dist, 1e-6));
    float window = Graphite_Window(dist2, L.Range);
    vec3 N = s.normal;

    c.radiance = vec3(8.0);
    c.diffuseL = Lc;
    c.specularL = Lc;
    c.extent = vec2(0.0);
    float shadow = 1.0;

    if (L.Type == 1 || L.Type == 2)
    {
        float cone = 1.0;
        if (L.Type == 2)
        {
            float axisCos = dot(normalize(Graphite_LightDirection(L)), - Lc);
            vec2 spotCos = Graphite_LightSpotCos(L);
            if (axisCos <= spotCos.x)
                return false;
            cone = smoothstep(spotCos.x, spotCos.y, axisCos);
        }

        float NdotL = dot(N, Lc);
        float radius = Graphite_LightSourceRadius(L);
        bool sphere = radius > 1e-4;
        if (NdotL <= 0.0 && ! backLit && ! sphere)
            return false;

        c.attenuation = window / max(dist2, 0.01) * cone;
        if (c.attenuation <= 0.0001)
            return false;




        shadow = Graphite_LocalLightShadow(L, s.position, s.geomNormal);
        if (shadow <= 0.0)
            return false;


        if (! sphere)
        {
            c.diffuse = max(NdotL, 0.0) * c.attenuation;
            c.specular = c.diffuse;
        }
        else
        {
            float r = radius;
            vec3 R = reflect(- s.view, N);
            c.diffuse = Graphite_SphereIrradiance(NdotL, min(r * r / max(dist2, 1e-8), 1.0)) / (PI * r * r) * window * cone;
            vec3 p = Graphite_SpherePoint(toLight, r, R);
            c.specularL = dot(p, p) > 1e-12 ? normalize(p) : N;
            c.specular = max(dot(N, c.specularL), 0.0) * c.attenuation;
            c.extent = vec2(r / (2.0 * max(dist, 1e-4)));
        }
        c.radiance *= Graphite_LightMask(L, - Lc);
    }
    else if (L.Type == 5)
    {
        c.attenuation = window / max(dist2, 0.01);
        float tubeLength = Graphite_LightSize(L).x;
        float tubeRadius = Graphite_LightSourceRadius(L);
        vec3 R = reflect(- s.view, N);
        vec3 axis = normalize(Graphite_LightDirection(L)) * (0.5 * tubeLength);
        vec3 a = toLight - axis, b = toLight + axis;
        vec3 ab = b - a;
        float t = clamp(- dot(a, ab) / max(dot(ab, ab), 1e-8), 0.0, 1.0);
        vec3 nearest = a + ab * t;
        c.diffuseL = dot(nearest, nearest) > 1e-12 ? normalize(nearest) : N;
        c.diffuse = Graphite_LineIrradiance(a, b, N) * window;


        float rab = dot(R, ab);
        float tr = clamp( (dot(R, a) * rab - dot(a, ab)) / max(dot(ab, ab) - rab * rab, 1e-8), 0.0, 1.0);
        vec3 onLine = a + ab * tr;
        vec3 p = Graphite_SpherePoint(onLine, tubeRadius, R);
        c.specularL = dot(p, p) > 1e-12 ? normalize(p) : N;
        float lineDist = max(length(onLine), 1e-4);
        c.specular = max(dot(N, c.specularL), 0.0) * window / max(dot(onLine, onLine), 0.01);
        c.extent = vec2(tubeRadius / (2.0 * lineDist), tubeLength / (PI * lineDist));
    }
    else
    {

        vec3 n = normalize(Graphite_LightDirection(L));
        float front = dot(- Lc, n);
        if (front <= 0.0)
            return false;
        c.attenuation = window * front / max(dist2, 0.01);
        vec3 up = Graphite_LightUp(L);
        vec2 size = Graphite_LightSize(L);
        vec3 right = cross(up, n);
        bool disk = L.Type == 4;
        vec2 halfSize = disk ? vec2(size.x) : 0.5 * size;
        float area = disk ? PI * halfSize.x * halfSize.x : size.x * size.y;
        if (area <= 0.0)
            return false;

        if (disk)
        {
            float r2 = halfSize.x * halfSize.x;
            c.diffuse = Graphite_SphereIrradiance(dot(N, Lc), r2 / (r2 + dist2)) * front / area * window;
        }
        else
        {
            vec3 hx = right * halfSize.x, hy = up * halfSize.y;
            c.diffuse = Graphite_QuadIrradiance(toLight - hx - hy, toLight + hx - hy, toLight + hx + hy, toLight - hx + hy, N) / area * window;
        }

        vec3 p = Graphite_PlanePoint(toLight, n, right, up, halfSize, disk, reflect(- s.view, N));
        float pDist2 = max(dot(p, p), 1e-8);
        float pDist = sqrt(pDist2);
        c.specularL = p / pDist;
        float pFront = max(dot(- c.specularL, n), 0.0);
        c.extent = disk ? vec2(halfSize.x / (2.0 * pDist)) : size / (2.0 * sqrt(PI) * pDist);
        c.specular = max(dot(N, c.specularL), 0.0) * pFront * window / max(pDist2, 0.01);
    }

    if (c.diffuse <= 0.0 && c.specular <= 0.0 && ! backLit)
        return false;

    c.radiance *= Graphite_LightRadiance(L) * shadow;
    return true;
}


float Graphite_DirectionalLightShadow(Graphite_DirectionalLight d, uint index, vec3 worldPos, vec3 geomNormal)
{



    if (! d.shadow || ! Graphite_ReceivesShadows())
        return 1.0;
    return Graphite_DirectionalShadow(index, d.direction, d.shadowDepthBias, d.shadowNormalBias, d.shadowStrength, d.shadowFilter, d.sinRadius / max(d.cosRadius, 1e-4), worldPos, geomNormal);

}


float MainDirectionalShadowFactor(vec3 worldPos, vec3 geomNormal)
{
    if (Graphite_DirectionalCount() == 0u)
        return 1.0;
    return Graphite_DirectionalLightShadow(Graphite_GetDirectional(0u), 0u, worldPos, geomNormal);
}


bool Graphite_ContributeDirectional(Graphite_DirectionalLight d, uint index, Graphite_Surface s, out Graphite_LightContribution c)
{
    if ( (d.layers & Graphite_SurfaceLayers()) == 0u)
        return false;
    float shadow = Graphite_DirectionalLightShadow(d, index, s.position, s.geomNormal);
    float NdotL = max(dot(s.normal, d.direction), 0.0);
    c.radiance = d.color * 8.0 * shadow;
    c.diffuseL = d.direction;
    c.diffuse = NdotL;
    c.attenuation = 1.0;
    c.specularL = d.direction;
    c.specular = NdotL;
    c.extent = vec2(0.0);
    if (d.sinRadius > 0.0)
    {
        vec3 R = reflect(- s.view, s.normal);
        float RdotL = dot(R, d.direction);
        vec3 across = R - d.direction * RdotL;
        if (RdotL < d.cosRadius && dot(across, across) > 1e-8)
            c.specularL = normalize(d.direction * d.cosRadius + normalize(across) * d.sinRadius);
        else if (RdotL >= d.cosRadius)
            c.specularL = R;
        c.specular = max(dot(s.normal, c.specularL), 0.0);
        c.extent = vec2(0.5 * d.sinRadius);
    }
    return true;
}






vec3 Graphite_DefaultBRDF(Graphite_Surface s, Graphite_LightContribution c)
{
    float NdotV = abs(dot(s.normal, s.view));

    vec3 Hs = normalize(c.specularL + s.view);
    float NdotLs = max(dot(s.normal, c.specularL), 0.0);
    vec3 F = FresnelSchlick(max(dot(c.specularL, Hs), 0.0), Graphite_SurfaceF0(s));
    vec3 specular = DistributionGGX(s.normal, Hs, s.roughness) * GeometrySmith(s.normal, s.view, c.specularL, s.roughness) * F
        / (4.0 * NdotV * NdotLs + 0.0001);

    vec3 Hd = normalize(c.diffuseL + s.view);
    vec3 kD = (vec3(1.0) - F) * (1.0 - s.metallic);
    vec3 diffuse = kD * s.albedo * DisneyDiffuse(NdotV, max(dot(s.normal, c.diffuseL), 0.0), max(dot(c.diffuseL, Hd), 0.0), s.roughness);

    return(diffuse * c.diffuse + specular * (c.specular * Graphite_AreaEnergy(s.roughness, c.extent.x, c.extent.y))) * c.radiance;
}




vec3 Graphite_BRDF(Graphite_Surface s, Graphite_LightContribution c);


vec3 Graphite_BRDF(Graphite_Surface s, Graphite_LightContribution c)
{
    return Graphite_DefaultBRDF(s, c);
}



vec3 Graphite_ShadeLights(Graphite_Surface s)
{
    vec3 total = vec3(0.0);
    Graphite_LightContribution c;
    for (uint i = 0u; i < Graphite_DirectionalCount(); i ++)
    {
        if (Graphite_ContributeDirectional(Graphite_GetDirectional(i), i, s, c))
            total += Graphite_BRDF(s, c);
    }

    Graphite_LightIterator it = Graphite_BeginLights(s.position);
    LightSample L;
    while (Graphite_NextLight(it, L))
    {
        if (Graphite_ContributeLocal(L, s, false, c))
            total += Graphite_BRDF(s, c);
    }
    return total * s.ao;
}





vec3 EvaluateLocalLight(LightSample L, vec3 worldPos, vec3 worldNormal, vec3 geomNormal, vec3 viewDir,
                        vec3 albedo, float metallic, float roughness, float ao)
{
    Graphite_Surface s = Graphite_MakeSurface(worldPos, worldNormal, geomNormal, viewDir, albedo, metallic, roughness, ao);
    Graphite_LightContribution c;
    return Graphite_ContributeLocal(L, s, false, c) ? Graphite_BRDF(s, c) * ao : vec3(0.0);
}


vec3 Graphite_AnisoBRDF(Graphite_Surface s, Graphite_LightContribution c, vec3 worldTangent, vec3 worldBitangent, float mt, float mb)
{
    vec3 lightDir = c.specularL;
    vec3 halfDir = normalize(lightDir + s.view);
    float NdotL = max(dot(s.normal, lightDir), 0.0);
    float NdotV = abs(dot(s.normal, s.view));
    float LdotH = max(dot(lightDir, halfDir), 0.0);
    float NdotH = max(dot(s.normal, halfDir), 0.0);

    float D = DistributionGGXAniso(dot(worldTangent, halfDir), dot(worldBitangent, halfDir), NdotH, mt, mb);
    float V = GeometrySmithAniso(dot(worldTangent, s.view), dot(worldBitangent, s.view), NdotV,
        dot(worldTangent, lightDir), dot(worldBitangent, lightDir), NdotL, mt, mb);
    vec3 F = FresnelSchlick(LdotH, Graphite_SurfaceF0(s));

    vec3 Hd = normalize(c.diffuseL + s.view);
    vec3 kD = (vec3(1.0) - F) * (1.0 - s.metallic);
    float diffuse = DisneyDiffuse(NdotV, max(dot(s.normal, c.diffuseL), 0.0), max(dot(c.diffuseL, Hd), 0.0), s.roughness);


    float at = max(mt, 0.045 * 0.045), ab = max(mb, 0.045 * 0.045);
    float energy = at * ab / (min(sqrt(at * at + c.extent.x * c.extent.x), 1.0) * min(sqrt(ab * ab + c.extent.y * c.extent.y), 1.0));
    return(kD * s.albedo * diffuse * c.diffuse + max(0.0, V * D) * PI * F * (c.specular * energy)) * c.radiance;
}

vec3 EvaluateLocalLightAniso(LightSample L, vec3 worldPos, vec3 worldNormal, vec3 geomNormal, vec3 viewDir,
                              vec3 worldTangent, vec3 worldBitangent,
                              vec3 albedo, float metallic, float mt, float mb,
                              float perceptualRoughness, float ao)
{
    Graphite_Surface s = Graphite_MakeSurface(worldPos, worldNormal, geomNormal, viewDir, albedo, metallic, perceptualRoughness, ao);
    Graphite_LightContribution c;
    return Graphite_ContributeLocal(L, s, false, c) ? Graphite_AnisoBRDF(s, c, worldTangent, worldBitangent, mt, mb) * ao : vec3(0.0);
}


vec3 ShadeDirectional(vec3 lightDir, vec3 lightColor, float shadowFactor, vec3 worldNormal, vec3 viewDir,
                      vec3 albedo, float metallic, float roughness, float ao)
{
    Graphite_Surface s = Graphite_MakeSurface(vec3(0.0), worldNormal, worldNormal, viewDir, albedo, metallic, roughness, ao);
    Graphite_LightContribution c;
    float NdotL = max(dot(worldNormal, lightDir), 0.0);
    c.radiance = lightColor * 8.0 * shadowFactor;
    c.diffuseL = lightDir;
    c.specularL = lightDir;
    c.diffuse = NdotL;
    c.specular = NdotL;
    c.attenuation = 1.0;
    c.extent = vec2(0.0);
    return Graphite_BRDF(s, c) * ao;
}

vec3 EvaluateDirectional(vec3 worldPos, vec3 worldNormal, vec3 geomNormal, vec3 viewDir,
                         vec3 albedo, float metallic, float roughness, float ao)
{
    Graphite_Surface s = Graphite_MakeSurface(worldPos, worldNormal, geomNormal, viewDir, albedo, metallic, roughness, ao);
    vec3 total = vec3(0.0);
    Graphite_LightContribution c;
    for (uint i = 0u; i < Graphite_DirectionalCount(); i ++)
    {
        if (Graphite_ContributeDirectional(Graphite_GetDirectional(i), i, s, c))
            total += Graphite_BRDF(s, c);
    }
    return total * ao;
}


vec3 CalculateForwardLighting(vec3 worldPos, vec3 worldNormal, vec3 geomNormal, vec3 viewDir,
                              vec3 albedo, float metallic, float roughness, float ao)
{
    roughness = ApplySpecularAA(roughness, worldNormal);
    return Graphite_ShadeLights(Graphite_MakeSurface(worldPos, worldNormal, geomNormal, viewDir, albedo, metallic, roughness, ao));
}

vec3 CalculateForwardLightingAniso(vec3 worldPos, vec3 worldNormal, vec3 geomNormal, vec3 viewDir,
                                    vec3 worldTangent, vec3 worldBitangent,
                                    vec3 albedo, float metallic,
                                    float roughness, float anisotropy, float ao)
{
    roughness = ApplySpecularAA(roughness, worldNormal);
    float roughnessT = roughness * (1.0 + anisotropy);
    float roughnessB = roughness * (1.0 - anisotropy);
    float mt = roughnessT * roughnessT;
    float mb = roughnessB * roughnessB;
    Graphite_Surface s = Graphite_MakeSurface(worldPos, worldNormal, geomNormal, viewDir, albedo, metallic, roughness, ao);

    vec3 total = vec3(0.0);
    Graphite_LightContribution c;
    for (uint i = 0u; i < Graphite_DirectionalCount(); i ++)
    {
        if (Graphite_ContributeDirectional(Graphite_GetDirectional(i), i, s, c))
            total += Graphite_AnisoBRDF(s, c, worldTangent, worldBitangent, mt, mb);
    }

    Graphite_LightIterator it = Graphite_BeginLights(worldPos);
    LightSample L;
    while (Graphite_NextLight(it, L))
    {
        if (Graphite_ContributeLocal(L, s, false, c))
            total += Graphite_AnisoBRDF(s, c, worldTangent, worldBitangent, mt, mb);
    }
    return total * s.ao;
}


vec3 Graphite_TranslucentLight(Graphite_Surface s, Graphite_LightContribution c,
                               float translucency, float scatterPower, float scatterDist, float scatterScale)
{
    vec3 result = Graphite_BRDF(s, c) * s.ao;
    if (translucency > 0.0)
    {
        vec3 scatter = CalculateTranslucency(c.diffuseL, s.view, s.normal, translucency, scatterPower, scatterDist, scatterScale,
            c.radiance * (c.attenuation / 8.0));
        result += scatter * s.albedo;
    }
    return result;
}

vec3 EvaluateLocalLightTranslucent(LightSample L, vec3 worldPos, vec3 worldNormal, vec3 geomNormal, vec3 viewDir,
                                    vec3 albedo, float metallic, float roughness, float ao,
                                    float translucency, float scatterPower, float scatterDist, float scatterScale)
{
    Graphite_Surface s = Graphite_MakeSurface(worldPos, worldNormal, geomNormal, viewDir, albedo, metallic, roughness, ao);
    Graphite_LightContribution c;
    return Graphite_ContributeLocal(L, s, true, c) ? Graphite_TranslucentLight(s, c, translucency, scatterPower, scatterDist, scatterScale) : vec3(0.0);
}


vec3 CalculateForwardLighting(vec3 worldPos, vec3 worldNormal, vec3 geomNormal, vec3 viewDir,
                              vec3 albedo, float metallic, float roughness, float ao,
                              float translucency, float scatterPower,
                              float scatterDist, float scatterScale)
{
    roughness = ApplySpecularAA(roughness, worldNormal);
    Graphite_Surface s = Graphite_MakeSurface(worldPos, worldNormal, geomNormal, viewDir, albedo, metallic, roughness, ao);

    vec3 total = vec3(0.0);
    Graphite_LightContribution c;
    for (uint i = 0u; i < Graphite_DirectionalCount(); i ++)
    {
        if (Graphite_ContributeDirectional(Graphite_GetDirectional(i), i, s, c))
            total += Graphite_TranslucentLight(s, c, translucency, scatterPower, scatterDist, scatterScale);
    }

    Graphite_LightIterator it = Graphite_BeginLights(worldPos);
    LightSample L;
    while (Graphite_NextLight(it, L))
    {
        if (Graphite_ContributeLocal(L, s, true, c))
            total += Graphite_TranslucentLight(s, c, translucency, scatterPower, scatterDist, scatterScale);
    }
    return total;
}


vec3 CalculateForwardLighting(vec3 worldPos, vec3 worldNormal, vec3 viewDir,
                              vec3 albedo, float metallic, float roughness, float ao)
{
    return CalculateForwardLighting(worldPos, worldNormal, worldNormal, viewDir, albedo, metallic, roughness, ao);
}

vec3 CalculateForwardLightingAniso(vec3 worldPos, vec3 worldNormal, vec3 viewDir,
                                    vec3 worldTangent, vec3 worldBitangent,
                                    vec3 albedo, float metallic,
                                    float roughness, float anisotropy, float ao)
{
    return CalculateForwardLightingAniso(worldPos, worldNormal, worldNormal, viewDir, worldTangent, worldBitangent,
                                         albedo, metallic, roughness, anisotropy, ao);
}

vec3 CalculateForwardLighting(vec3 worldPos, vec3 worldNormal, vec3 viewDir,
                              vec3 albedo, float metallic, float roughness, float ao,
                              float translucency, float scatterPower,
                              float scatterDist, float scatterScale)
{
    return CalculateForwardLighting(worldPos, worldNormal, worldNormal, viewDir, albedo, metallic, roughness, ao,
                                    translucency, scatterPower, scatterDist, scatterScale);
}





vec3 Graphite_SkyAmbient(vec3 worldNormal);

vec3 CalculateAmbient(vec3 worldNormal)
{
    vec3 ambient = graphite_World.ambientColor.rgb * graphite_World.ambientMode.x;
    if (graphite_World.ambientMode.w > 0.0)
        ambient += Graphite_SkyAmbient(worldNormal) * graphite_World.ambientMode.w;

    float upDot = dot(worldNormal, vec3(0.0, 1.0, 0.0));
    ambient += mix(graphite_World.ambientGround.rgb, graphite_World.ambientSky.rgb, upDot * 0.5 + 0.5) * graphite_World.ambientMode.y;

    return ambient;
}





struct Graphite_ProbeSH { vec4 shAr; vec4 shAg; vec4 shAb; vec4 shBr; vec4 shBg; vec4 shBb; vec4 shC; };

layout(set = 1, binding = 23, std430) readonly buffer Graphite_ObjectProbes { Graphite_ProbeSH graphite_ObjectProbes[]; };





vec3 DecodeLightmap(vec4 texel, uint encoding)
{
    return encoding == 1u ? texel.rgb * (texel.a * 8.0) : texel.rgb;
}


vec3 ShadeSH9(Graphite_ProbeSH sh, vec3 n)
{
    vec4 nrm = vec4(n, 1.0);
    vec3 x;
    x.r = dot(sh.shAr, nrm);
    x.g = dot(sh.shAg, nrm);
    x.b = dot(sh.shAb, nrm);

    vec4 vB = n.xyzz * n.yzzx;
    vec3 x1;
    x1.r = dot(sh.shBr, vB);
    x1.g = dot(sh.shBg, vB);
    x1.b = dot(sh.shBb, vB);

    float vC = n.x * n.x - n.y * n.y;
    vec3 x2 = sh.shC.rgb * vC;

    return max(x + x1 + x2, vec3(0.0));
}

layout(set = 1, binding = 26, std430) readonly buffer Graphite_SkyAmbientSH { Graphite_ProbeSH graphite_SkyAmbient; };


vec3 Graphite_SkyAmbient(vec3 worldNormal)
{
    return ShadeSH9(graphite_SkyAmbient, worldNormal);
}



layout(set = 1, binding = 31) uniform texture2DArray Graphite_ScreenVisibility;
layout(set = 1, binding = 32) uniform texture2DArray Graphite_ScreenIrradiance;
layout(set = 1, binding = 33) uniform sampler Graphite_ScreenSampler;


vec3 graphite_ScreenCoord() { return vec3(gl_FragCoord.xy * Graphite_View().screen.zw, float(gl_ViewIndex)); }


float Graphite_ScreenOcclusion() { return texture(sampler2DArray(Graphite_ScreenVisibility, Graphite_ScreenSampler), graphite_ScreenCoord()).r; }


vec4 Graphite_ScreenIndirect() { return texture(sampler2DArray(Graphite_ScreenIrradiance, Graphite_ScreenSampler), graphite_ScreenCoord()); }






vec3 graphite_BakedDiffuse(vec3 worldNormal, vec2 lightmapUV);

vec3 Graphite_IndirectDiffuse(vec3 worldNormal, vec2 lightmapUV)
{
    vec4 screen = Graphite_ScreenIndirect();
    return mix(graphite_BakedDiffuse(worldNormal, lightmapUV) * Graphite_ScreenOcclusion(), screen.rgb, screen.a);
}

vec3 graphite_BakedDiffuse(vec3 worldNormal, vec2 lightmapUV)
{
    Graphite_ObjectData object = Graphite_Object();
    if (object.lightmapTexture == 0xFFFFFFFEu)
        return CalculateAmbient(worldNormal) * graphite_World.ambientMode.z;
    if (object.lightmapTexture != 0xFFFFFFFFu)
        return DecodeLightmap(texture(sampler2D(Graphite_Textures2D[nonuniformEXT(object.lightmapTexture)], Graphite_Samplers[nonuniformEXT(object.lightmapSampler & 0xFFFFu)]), lightmapUV), object.lightmapSampler >> 16);
    if (object.probeIndex != 0xFFFFFFFFu && graphite_World.counts.z != 0u)
        return ShadeSH9(graphite_ObjectProbes[object.probeIndex], worldNormal);
    return CalculateAmbient(worldNormal) * graphite_World.ambientMode.z;
}


float SpecularOcclusion(float NdotV, float ao, float roughness)
{
    return clamp(pow(NdotV + ao, exp2(- 16.0 * roughness - 1.0)) - 1.0 + ao, 0.0, 1.0);
}








struct Graphite_ReflectionProbe
{
    vec4 row0;
    vec4 row1;
    vec4 row2;
    vec4 box;
    vec4 params;
};

Graphite_ReflectionProbe Graphite_ProbeAt(uint array, uint index)
{
    uint w = index * 5u;
    Graphite_ReflectionProbe p;
    p.row0 = Graphite_SpatialVec4(array, w);
    p.row1 = Graphite_SpatialVec4(array, w + 1u);
    p.row2 = Graphite_SpatialVec4(array, w + 2u);
    p.box = Graphite_SpatialVec4(array, w + 3u);
    p.params = Graphite_SpatialVec4(array, w + 4u);
    return p;
}



bool ReflectionProbesReady() { return graphite_World.reflections.w != 0u; }


vec3 SampleProbeCube(uint slot, uint mips, vec3 dir, float roughness)
{
    return textureLod(samplerCube(Graphite_TexturesCube[nonuniformEXT(slot)], Graphite_Samplers[nonuniformEXT(graphite_World.reflections.y)]), dir, roughness * (float(mips) - 1.0)).rgb;
}

vec3 SampleSkyProbe(vec3 dir, float roughness)
{
    return SampleProbeCube(graphite_World.reflections.x, graphite_World.reflections.z, dir, roughness);
}


vec3 Graphite_ProbeLocal(Graphite_ReflectionProbe probe, vec3 worldPos, vec3 offset, vec3 offsetLow)
{
    precise vec3 center = (vec3(probe.row0.w, probe.row1.w, probe.row2.w) - offset) - offsetLow;
    precise vec3 q = worldPos - center;
    return vec3(dot(probe.row0.xyz, q), dot(probe.row1.xyz, q), dot(probe.row2.xyz, q));
}


vec3 SampleReflectionProbe(Graphite_ReflectionProbe probe, vec3 p, vec3 dir, float roughness)
{
    uint cube = floatBitsToUint(probe.params.w);
    if (probe.params.y > 0.5)
    {
        vec3 halfSize = probe.box.xyz;
        vec3 d = vec3(dot(probe.row0.xyz, dir), dot(probe.row1.xyz, dir), dot(probe.row2.xyz, dir));
        d = mix(vec3(1e-6), d, greaterThanEqual(abs(d), vec3(1e-6)));
        vec3 far = max( (halfSize - p) / d, (- halfSize - p) / d);
        float t = max(min(min(far.x, far.y), far.z), 0.0);
        vec3 hit = p + d * t;


        roughness *= sqrt(clamp(t / max(length(hit), 1e-4), 0.0, 1.0));
        dir = probe.row0.xyz * hit.x + probe.row1.xyz * hit.y + probe.row2.xyz * hit.z;
    }

    return SampleProbeCube(cube & 0xFFFFu, cube >> 16, dir, roughness) * probe.params.x;
}


struct Graphite_ProbeIterator
{
    Graphite_TreeQuery query;
    uint tree;
    uint k;
    uint last;
};

Graphite_ProbeIterator Graphite_BeginProbes(vec3 worldPos)
{
    Graphite_ProbeIterator it;
    Graphite_BeginQuery(it.query, worldPos, 2u);
    it.k = 0u;
    it.last = 0u;
    return it;
}

bool Graphite_NextProbe(inout Graphite_ProbeIterator it, out Graphite_ReflectionProbe probe, out vec3 local)
{
    for (;;)
    {
        while (it.k < it.last)
        {
            probe = Graphite_ProbeAt(it.tree, it.k ++);
            local = Graphite_ProbeLocal(probe, it.query.position, it.query.offset, it.query.offsetLow);
            vec3 inside = probe.box.xyz - abs(local);
            if (min(inside.x, min(inside.y, inside.z)) > 0.0)
                return true;
        }
        if (! Graphite_NextRun(it.query, it.tree, it.k, it.last))
            return false;
    }
    return false;
}


vec3 EvaluateReflectionProbes(vec3 worldPos, vec3 N, vec3 V, float roughness)
{

    float a = roughness * roughness;
    vec3 R = normalize(mix(N, reflect(- V, N), (1.0 - a) * (sqrt(1.0 - a) + a)));


    uvec2 kept[4];
    vec3 local[4];
    float weight[4];
    float priority[4];
    int count = 0;

    Graphite_ProbeIterator it = Graphite_BeginProbes(worldPos);
    Graphite_ReflectionProbe probe;
    vec3 p;
    while (Graphite_NextProbe(it, probe, p))
    {
        vec3 inside3 = probe.box.xyz - abs(p);
        float inside = min(inside3.x, min(inside3.y, inside3.z));
        float w = probe.box.w > 0.0 ? clamp(inside / probe.box.w, 0.0, 1.0) : 1.0;
        float pr = probe.params.z;
        int at;
        if (count < 4)
            at = count ++;
        else if (pr > priority[4 - 1])
            at = 4 - 1;
        else
            continue;

        kept[at] = uvec2(it.tree, it.k - 1u);
        local[at] = p;
        weight[at] = w;
        priority[at] = pr;
        for (int j = at; j > 0 && priority[j] > priority[j - 1]; j --)
        {
            uvec2 tk = kept[j]; kept[j] = kept[j - 1]; kept[j - 1] = tk;
            vec3 tl = local[j]; local[j] = local[j - 1]; local[j - 1] = tl;
            float tw = weight[j]; weight[j] = weight[j - 1]; weight[j - 1] = tw;
            float tp = priority[j]; priority[j] = priority[j - 1]; priority[j - 1] = tp;
        }
    }

    vec3 color = vec3(0.0);
    float total = 0.0;
    for (int i = 0; i < count && total < 0.999; i ++)
    {
        float w = weight[i] * (1.0 - total);
        color += SampleReflectionProbe(Graphite_ProbeAt(kept[i].x, kept[i].y), local[i], R, roughness) * w;
        total += w;
    }
    if (total < 0.999)
        color += SampleSkyProbe(R, roughness) * (1.0 - total);
    return color;
}




vec3 Graphite_IndirectSpecular(vec3 worldPos, vec3 N, vec3 V, vec3 F0, float roughness, float ao, vec3 ambientLight)
{

    ao *= Graphite_ScreenOcclusion();

    float NdotV = max(dot(N, V), 0.0);
    vec3 F = EnvBRDF(F0, roughness, NdotV);
    if (ReflectionProbesReady())
        return EvaluateReflectionProbes(worldPos, N, V, roughness) * F * SpecularOcclusion(NdotV, ao, roughness);
    float specOcclusion = 1.0 - roughness * roughness;
    return ambientLight * F * mix(specOcclusion, 1.0, 0.25);
}










vec3 FogColor(vec3 worldPos)
{
    if (graphite_World.fogStates.w < 0.5 || ! ReflectionProbesReady())
        return graphite_World.fogColor.rgb;


    vec3 dir = worldPos;
    dir.y = max(dir.y, 0.0);
    dir = length(dir) > 1e-4 ? normalize(dir) : vec3(0.0, 0.0, 1.0);
    return SampleSkyProbe(dir, 0.35);
}

vec3 ApplyFog(vec3 color, vec3 worldPos)
{
    vec4 states = graphite_World.fogStates;
    if (states.x + states.y + states.z < 0.5)
        return color;

    vec4 params = graphite_World.fogParams;
    float fogCoord = length(worldPos);
    float prowlFog = 0.0;
    prowlFog += (fogCoord * params.z + params.w) * states.x;
    prowlFog += exp2(- fogCoord * params.y) * states.y;
    prowlFog += exp2(- fogCoord * fogCoord * params.x * params.x) * states.z;
    return mix(FogColor(worldPos), color, clamp(prowlFog, 0.0, 1.0));
}
#line 36 "Graphite/Standard/Standard.glsl"
#line 1 "Graphite/Decals.glsl"













#pragma variantglobalGRAPHITE_DECALS

struct Graphite_DecalRecord
{
    vec4 row0;
    vec4 row1;
    vec4 row2;
    uvec4 order;
    vec4 color;
    uvec4 textures;
    vec4 params;
    vec4 axis;
    uvec4 extra;
    vec4 hostData;
};




Graphite_DecalRecord Graphite_DecalAt(uint array, uint index)
{
    uint w = index * 10u;
    Graphite_DecalRecord d;
    d.row0 = Graphite_SpatialVec4(array, w);
    d.row1 = Graphite_SpatialVec4(array, w + 1u);
    d.row2 = Graphite_SpatialVec4(array, w + 2u);
    d.order = Graphite_SpatialUVec4(array, w + 3u);
    d.color = Graphite_SpatialVec4(array, w + 4u);
    d.textures = Graphite_SpatialUVec4(array, w + 5u);
    d.params = Graphite_SpatialVec4(array, w + 6u);
    d.axis = Graphite_SpatialVec4(array, w + 7u);
    d.extra = Graphite_SpatialUVec4(array, w + 8u);
    d.hostData = Graphite_SpatialVec4(array, w + 9u);
    return d;
}


vec3 Graphite_DecalLocal(Graphite_DecalRecord d, vec3 worldPos, vec3 offset, vec3 offsetLow)
{
    precise vec3 center = (vec3(d.row0.w, d.row1.w, d.row2.w) - offset) - offsetLow;
    precise vec3 q = worldPos - center;
    return vec3(dot(d.row0.xyz, q), dot(d.row1.xyz, q), dot(d.row2.xyz, q));
}


float Graphite_DecalCoverage(Graphite_DecalRecord d, vec3 local, vec3 geomNormal, uint layers)
{
    if (any(greaterThan(abs(local), vec3(0.5))))
        return 0.0;
    if ( (floatBitsToUint(d.params.w) & layers) == 0u)
        return 0.0;

    float facing = dot(geomNormal, d.axis.xyz);
    float threshold = max(d.params.z, 0.0);
    if (facing <= threshold)
        return 0.0;
    float fade = threshold > 0.0 ? clamp( (facing - threshold) / max(1.0 - threshold, 1e-4), 0.0, 1.0) : 1.0;
    return fade * d.color.a;
}

void Graphite_ApplyDecal(Graphite_DecalRecord d, vec3 local, float coverage,
                         inout vec3 albedo, inout vec3 normal, inout float roughness, inout float metallic, inout float occlusion)
{
    vec2 uv = local.xy + 0.5;
    uint flags = floatBitsToUint(d.axis.w);

    vec4 color = d.color;
    if ( (flags & 1u) != 0u)
        color *= texture(sampler2D(Graphite_Textures2D[nonuniformEXT(d.textures.x)], Graphite_Samplers[nonuniformEXT(d.textures.w)]), uv);
    float alpha = coverage * color.a / max(d.color.a, 1e-4);
    albedo = mix(albedo, color.rgb, alpha);

    if ( (flags & 2u) != 0u)
    {
        vec3 tangent = texture(sampler2D(Graphite_Textures2D[nonuniformEXT(d.textures.y)], Graphite_Samplers[nonuniformEXT(d.textures.w)]), uv).xyz * 2.0 - 1.0;
        vec3 decalNormal = normalize(normalize(d.row0.xyz) * tangent.x + normalize(d.row1.xyz) * tangent.y + d.axis.xyz * tangent.z);
        normal = normalize(mix(normal, decalNormal, clamp(alpha * d.params.x, 0.0, 1.0)));
    }

    if ( (flags & 4u) != 0u)
    {
        vec3 mask = texture(sampler2D(Graphite_Textures2D[nonuniformEXT(d.textures.z)], Graphite_Samplers[nonuniformEXT(d.textures.w)]), uv).rgb;
        occlusion = mix(occlusion, mask.r, alpha);
        roughness = mix(roughness, mask.g, alpha);
        metallic = mix(metallic, mask.b, alpha);
    }
}







bool Graphite_DecalBefore(float priority, uint sequence, float otherPriority, uint otherSequence)
{
    return priority < otherPriority || (priority == otherPriority && sequence < otherSequence);
}



void Graphite_ApplyDecals(vec3 worldPos, vec3 geomNormal,
                          inout vec3 albedo, inout vec3 normal, inout float roughness, inout float metallic, inout float occlusion)
{

    return;

    uint layers = Graphite_SurfaceLayers();
    uint hitTree[8];
    uint hitIndex[8];
    float hitPriority[8];
    uint hitSequence[8];
    vec3 hitLocal[8];
    float hitCoverage[8];
    int hits = 0;

    Graphite_TreeQuery query;
    Graphite_BeginQuery(query, worldPos, 1u);
    uint tree, first, last;
    while (Graphite_NextRun(query, tree, first, last))
    {
        for (uint i = first; i < last; i ++)
        {
            Graphite_DecalRecord d = Graphite_DecalAt(tree, i);
            vec3 local = Graphite_DecalLocal(d, worldPos, query.offset, query.offsetLow);
            float coverage = Graphite_DecalCoverage(d, local, geomNormal, layers);
            if (coverage <= 0.0)
                continue;


            float priority = d.params.y;
            uint sequence = d.order.x;
            int at = hits;
            if (hits == 8)
            {
                if (Graphite_DecalBefore(priority, sequence, hitPriority[0], hitSequence[0]))
                    continue;
                for (int k = 0; k < hits - 1; k ++)
                {
                    hitTree[k] = hitTree[k + 1];
                    hitIndex[k] = hitIndex[k + 1];
                    hitPriority[k] = hitPriority[k + 1];
                    hitSequence[k] = hitSequence[k + 1];
                    hitLocal[k] = hitLocal[k + 1];
                    hitCoverage[k] = hitCoverage[k + 1];
                }
                at = hits - 1;
            }
            else
            {
                hits ++;
            }
            while (at > 0 && Graphite_DecalBefore(priority, sequence, hitPriority[at - 1], hitSequence[at - 1]))
            {
                hitTree[at] = hitTree[at - 1];
                hitIndex[at] = hitIndex[at - 1];
                hitPriority[at] = hitPriority[at - 1];
                hitSequence[at] = hitSequence[at - 1];
                hitLocal[at] = hitLocal[at - 1];
                hitCoverage[at] = hitCoverage[at - 1];
                at --;
            }
            hitTree[at] = tree;
            hitIndex[at] = i;
            hitPriority[at] = priority;
            hitSequence[at] = sequence;
            hitLocal[at] = local;
            hitCoverage[at] = coverage;
        }
    }

    for (int h = 0; h < hits; h ++)
    {
        Graphite_DecalRecord d = Graphite_DecalAt(hitTree[h], hitIndex[h]);
        Graphite_ApplyDecal(d, hitLocal[h], hitCoverage[h], albedo, normal, roughness, metallic, occlusion);



    }
}
#line 37 "Graphite/Standard/Standard.glsl"
#line 1 "Graphite/Passes.glsl"






#line 1 "Graphite/Octahedral.glsl"



vec2 Graphite_OctWrap(vec2 v)
{
    return(1.0 - abs(v.yx)) * vec2(v.x >= 0.0 ? 1.0 : - 1.0, v.y >= 0.0 ? 1.0 : - 1.0);
}


vec2 Graphite_OctEncode(vec3 n)
{
    n /= abs(n.x) + abs(n.y) + abs(n.z);
    return n.z >= 0.0 ? n.xy : Graphite_OctWrap(n.xy);
}

vec3 Graphite_OctDecode(vec2 e)
{
    vec3 n = vec3(e, 1.0 - abs(e.x) - abs(e.y));
    if (n.z < 0.0)
        n.xy = Graphite_OctWrap(n.xy);
    return normalize(n);
}
#line 8 "Graphite/Passes.glsl"


layout(location = 1) out vec2 graphite_GBufferNormal;
layout(location = 2) out vec2 graphite_GBufferMaterial;
layout(location = 3) out vec2 graphite_GBufferMotion;
layout(location = 28) in vec4 graphite_MotionCurrent;
layout(location = 29) in vec4 graphite_MotionPrevious;





void Graphite_WriteMotion(vec4 currentClip, vec4 previousClip)
{
    if (currentClip.w <= 1e-6 || previousClip.w <= 1e-6)
    {
        graphite_GBufferMotion = vec2(0.0);
        return;
    }
    Graphite_ViewData view = Graphite_View();
    vec2 current = currentClip.xy / currentClip.w - view.jitter.xy;
    vec2 previous = previousClip.xy / previousClip.w;
    graphite_GBufferMotion = (current - previous) * vec2(0.5, - 0.5);
}

void Graphite_WriteGBuffer(vec3 worldNormal, float roughness, float metallic)
{
    graphite_GBufferNormal = Graphite_OctEncode(normalize(worldNormal));
    graphite_GBufferMaterial = vec2(roughness, metallic);
    Graphite_WriteMotion(graphite_MotionCurrent, graphite_MotionPrevious);
}

#line 38 "Graphite/Standard/Standard.glsl"
#line 1 "Graphite/Debug.glsl"






#pragma variantglobalGRAPHITE_DEBUGOFFUNLITALBEDONORMALSROUGHNESSMETALLICAOEMISSIONLIGHTINGCASCADESLIGHTMAP_DENSITY






void Graphite_DebugInputs(inout vec3 albedo, inout float metallic)
{




}













































vec3 Graphite_DebugView(vec3 color, vec3 albedo, vec3 normal, float roughness, float metallic, float ao, vec3 emission, vec3 position, vec2 lightmapUV)
{













    return emission;










}


#line 39 "Graphite/Standard/Standard.glsl"



layout(set = 0, binding = 1, std140) uniform _GraphiteGlobal { vec2 _Tiling; vec2 _Offset; vec4 _MainColor; int _MainTexUV; };



layout(set = 0, binding = 2) uniform sampler2D _MainTex;



















































                        layout(location = 0) in vec2 vUV;
                        layout(location = 1) in vec2 vUV1;



                        layout(location = 2) in vec3 vWorldPos;
                        layout(location = 3) in vec4 vColor;










void Graphite_StandardTangentFrame(vec3 worldNormal, vec3 worldTangent, float handedness, bool hasTangent, out vec3 tangent, out vec3 bitangent)
{
    float sign = handedness < 0.0 ? - 1.0 : 1.0;
    if (hasTangent)
    {
        tangent = worldTangent;
        bitangent = cross(tangent, worldNormal) * sign;
        if (dot(bitangent, bitangent) > 0.000001)
            return;
    }
    tangent = abs(worldNormal.y) < 0.999 ? normalize(cross(worldNormal, vec3(0.0, 1.0, 0.0))) : normalize(cross(worldNormal, vec3(1.0, 0.0, 0.0)));
    bitangent = cross(tangent, worldNormal) * sign;
}








































struct Graphite_SurfaceData
{
    vec2 UV;
    vec2 UV1;
    vec4 VertexColor;
    vec3 WorldPos;
    vec3 ViewDir;
    vec3 GeometryNormal;
    vec3 BaseColor;
    float Alpha;
    vec3 Normal;
    float Metallic;
    float Roughness;
    float Occlusion;
    vec3 Emission;
};





Graphite_SurfaceData Graphite_NewSurfaceData(vec2 uv, vec2 uv1, vec3 baseColor, float alpha)
{
    Graphite_SurfaceData s;
    s.UV = uv;
    s.UV1 = uv1;
    s.VertexColor = vec4(1.0);
    s.WorldPos = vec3(0.0);
    s.ViewDir = vec3(0.0, 0.0, 1.0);
    s.GeometryNormal = vec3(0.0, 1.0, 0.0);
    s.BaseColor = baseColor;
    s.Alpha = alpha;
    s.Normal = vec3(0.0, 1.0, 0.0);
    s.Metallic = 0.0;
    s.Roughness = 1.0;
    s.Occlusion = 1.0;
    s.Emission = vec3(0.0);
    return s;
}



layout(location = 0) out vec4 fragColor;













void Graphite_StandardFragment()
{
    vec2 uv = vUV;
    vec2 uv1 = vUV1 * _Tiling + _Offset;
    vec3 viewDir = GetWorldViewDir(vWorldPos);






















    vec3 N = vec3(0.0, 1.0, 0.0);


    vec4 albedoTexel = texture(_MainTex, ( (_MainTexUV) == 0 ? (uv) : (uv1)));
    vec3 baseColor = albedoTexel.rgb * _MainColor.rgb * vColor.rgb;
    float alpha = albedoTexel.a * _MainColor.a * vColor.a;






    vec3 worldNormal = N;
    float roughness = 1.0, metallic = 0.0, ao = 1.0;
    vec3 emission = vec3(0.0);

































































    vec3 color = ApplyFog(baseColor + emission, vWorldPos);
    color = Graphite_DebugView(color, baseColor, vec3(0.0), 1.0, 0.0, 1.0, vec3(0.0), vWorldPos, vec2(0.0));

    Graphite_WriteGBuffer(vec3(0.0, 1.0, 0.0), 1.0, 0.0);










































    fragColor = vec4(color, 1.0);

}






















#line 28 "Graphite/Unlit/Forward"

void main() { Graphite_StandardFragment(); }
