#version 460 core

layout(vertices = 4) out;

layout(location = 0) in vec3 vWorldPos[];
layout(location = 1) in vec2 vUV[];

layout(location = 0) out vec3 tcWorldPos[];
layout(location = 1) out vec2 tcUV[];

layout(set = 0, binding = 1) uniform TessellationParams
{
    mat4 viewProj;
    vec4 cameraWorldPos;
    vec2 screenSize;
    float edgeLengthTargetPixels;
    float padding0;
} tessParams;

const float MIN_TESS_LEVEL = 1.0;
const float MAX_TESS_LEVEL = 64.0;

vec2 worldToScreen(vec3 worldPos)
{
    vec4 clip = tessParams.viewProj * vec4(worldPos, 1.0);
    vec3 ndc = clip.xyz / max(clip.w, 0.0001);
    return (ndc.xy * 0.5 + 0.5) * tessParams.screenSize;
}

float edgeTessLevel(vec3 worldA, vec3 worldB)
{
    vec2 screenA = worldToScreen(worldA);
    vec2 screenB = worldToScreen(worldB);
    float pixelLength = distance(screenA, screenB);
    float level = pixelLength / tessParams.edgeLengthTargetPixels;
    return clamp(level, MIN_TESS_LEVEL, MAX_TESS_LEVEL);
}

void main()
{
    tcWorldPos[gl_InvocationID] = vWorldPos[gl_InvocationID];
    tcUV[gl_InvocationID] = vUV[gl_InvocationID];
    gl_out[gl_InvocationID].gl_Position = vec4(vWorldPos[gl_InvocationID], 1.0);

    if (gl_InvocationID == 0)
    {
        // control point winding: 0=(u0,v0) 1=(u1,v0) 2=(u1,v1) 3=(u0,v1)
        float outerLeft = edgeTessLevel(vWorldPos[3], vWorldPos[0]);
        float outerBottom = edgeTessLevel(vWorldPos[0], vWorldPos[1]);
        float outerRight = edgeTessLevel(vWorldPos[1], vWorldPos[2]);
        float outerTop = edgeTessLevel(vWorldPos[2], vWorldPos[3]);

        gl_TessLevelOuter[0] = outerLeft;
        gl_TessLevelOuter[1] = outerBottom;
        gl_TessLevelOuter[2] = outerRight;
        gl_TessLevelOuter[3] = outerTop;

        gl_TessLevelInner[0] = max(outerLeft, outerRight);
        gl_TessLevelInner[1] = max(outerBottom, outerTop);
    }
}
