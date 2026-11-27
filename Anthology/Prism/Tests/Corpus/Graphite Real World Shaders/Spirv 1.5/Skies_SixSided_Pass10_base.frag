#version 460
#extension GL_GOOGLE_include_directive : enable
#line 1 "Graphite/Sky/SixSided"

#extension GL_GOOGLE_cpp_style_line_directive : require
#line 25 "Graphite/Sky/SixSided"

#line 1 "Graphite/Sky.glsl"







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







#line 9 "Graphite/Sky.glsl"




















layout(location = 29) in vec2 graphite_SkyClip;






vec3 Graphite_SkyDirection()
{
    Graphite_ViewData view = Graphite_View();
    if (view.projection[2][3] != 0.0)
        return Graphite_ViewRay(graphite_SkyClip);

    float aspect = view.projection[1][1] / max(1e-6, view.projection[0][0]);
    float f = 1.0 / tan(1.0471975512 * 0.5);
    vec3 ray = normalize(vec3(graphite_SkyClip.x * aspect / f, graphite_SkyClip.y / f, 1.0));
    return normalize(transpose(mat3(view.view)) * ray);
}

#line 27 "Graphite/Sky/SixSided"
 layout(set = 0, binding = 0) uniform sampler2D _CubeRight;
            layout(set = 0, binding = 1) uniform sampler2D _CubeLeft;
            layout(set = 0, binding = 2) uniform sampler2D _CubeTop;
            layout(set = 0, binding = 3) uniform sampler2D _CubeBottom;
            layout(set = 0, binding = 4) uniform sampler2D _CubeFront;
            layout(set = 0, binding = 5) uniform sampler2D _CubeBack;
            layout(set = 0, binding = 6, std140) uniform _GraphiteGlobal { vec4 _Tint; float _Exposure; };
            
            layout(location = 0) out vec4 outColor;


            vec4 SampleCubemap(vec3 dir)
            {
                vec3 absDir = abs(dir);
                if (absDir.x >= absDir.y && absDir.x >= absDir.z)
                {
                    if (dir.x > 0.0)
                        return texture(_CubeRight, vec2(- dir.z, - dir.y) / absDir.x * 0.5 + 0.5);
                    return texture(_CubeLeft, vec2(dir.z, - dir.y) / absDir.x * 0.5 + 0.5);
                }
                if (absDir.y >= absDir.x && absDir.y >= absDir.z)
                {
                    if (dir.y > 0.0)
                        return texture(_CubeTop, vec2(dir.x, dir.z) / absDir.y * 0.5 + 0.5);
                    return texture(_CubeBottom, vec2(dir.x, - dir.z) / absDir.y * 0.5 + 0.5);
                }
                if (dir.z > 0.0)
                    return texture(_CubeFront, vec2(dir.x, - dir.y) / absDir.z * 0.5 + 0.5);
                return texture(_CubeBack, vec2(- dir.x, - dir.y) / absDir.z * 0.5 + 0.5);
            }

            void main()
            {
                vec4 color = SampleCubemap(Graphite_SkyDirection());
                outColor = vec4(color.rgb * _Tint.rgb * _Exposure, 1.0);
            }
