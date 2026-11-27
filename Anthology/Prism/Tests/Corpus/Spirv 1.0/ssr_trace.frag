#version 460 core

// Screen space reflection trace pass.
// Marches the view space reflection ray against the depth buffer, refines
// the hit with a binary search, then writes the hit uv plus a confidence
// weight for the resolve pass to consume.

layout(location = 0) in vec2 uv;

layout(location = 0) out vec4 outTrace;
// outTrace.xy = hit uv (only meaningful when outTrace.z > 0.5)
// outTrace.z  = hit flag, 1.0 if a valid crossing was found and passed the
//               thickness test, 0.0 otherwise
// outTrace.a  = confidence weight in 0..1, combines edge fade and grazing
//               angle fade, already zeroed out when the hit flag is 0.0

layout(set = 0, binding = 0) uniform sampler2D depthTex;
layout(set = 0, binding = 1) uniform sampler2D normalTex;

layout(set = 0, binding = 2) uniform SSRTraceParams
{
    mat4 viewMatrix;
    mat4 projMatrix;
    mat4 invProjMatrix;
    vec2 screenSize;
    float maxDistance;
    float thickness;
    float edgeFadeDistance;
    float grazingFadeStart;
} params;

const int STEP_COUNT = 32;
const int BINARY_STEP_COUNT = 8;

// Rebuilds the view space position of a screen uv from the depth buffer.
vec3 reconstructViewPos(vec2 sampleUV)
{
    float d = texture(depthTex, sampleUV).r;
    vec4 ndc = vec4(sampleUV * 2.0 - 1.0, d, 1.0);
    vec4 vp = params.invProjMatrix * ndc;
    return vp.xyz / vp.w;
}

// Projects a view space position back to a 0..1 screen uv.
vec2 viewToUV(vec3 viewPos)
{
    vec4 clipPos = params.projMatrix * vec4(viewPos, 1.0);
    vec2 ndcXY = clipPos.xy / clipPos.w;
    return ndcXY * 0.5 + 0.5;
}

void main()
{
    vec3 viewPos = reconstructViewPos(uv);
    vec3 normalView = normalize(texture(normalTex, uv).xyz * 2.0 - 1.0);
    vec3 viewDir = normalize(viewPos);
    vec3 reflectDir = normalize(reflect(viewDir, normalView));

    float stepLength = params.maxDistance / float(STEP_COUNT);
    vec3 rayStep = reflectDir * stepLength;

    vec3 prevRayPos = viewPos;
    vec3 rayPos = viewPos;

    bool hitFound = false;
    vec2 hitUV = uv;

    for (int i = 0; i < STEP_COUNT; i++)
    {
        prevRayPos = rayPos;
        rayPos += rayStep;

        vec2 sampleUV = viewToUV(rayPos);
        if (sampleUV.x < 0.0 || sampleUV.x > 1.0 || sampleUV.y < 0.0 || sampleUV.y > 1.0)
        {
            break;
        }

        vec3 sceneViewPos = reconstructViewPos(sampleUV);
        float rayDepth = -rayPos.z;
        float sceneDepth = -sceneViewPos.z;

        if (rayDepth > sceneDepth)
        {
            vec3 loPos = prevRayPos;
            vec3 hiPos = rayPos;

            for (int j = 0; j < BINARY_STEP_COUNT; j++)
            {
                vec3 midPos = (loPos + hiPos) * 0.5;
                vec2 midUV = viewToUV(midPos);
                vec3 midSceneViewPos = reconstructViewPos(midUV);
                float midRayDepth = -midPos.z;
                float midSceneDepth = -midSceneViewPos.z;

                if (midRayDepth > midSceneDepth)
                {
                    hiPos = midPos;
                }
                else
                {
                    loPos = midPos;
                }
            }

            vec3 finalPos = hiPos;
            vec2 finalUV = viewToUV(finalPos);
            vec3 finalSceneViewPos = reconstructViewPos(finalUV);
            float finalRayDepth = -finalPos.z;
            float finalSceneDepth = -finalSceneViewPos.z;
            float depthDiff = finalRayDepth - finalSceneDepth;

            if (depthDiff >= 0.0 && depthDiff <= params.thickness)
            {
                hitFound = true;
                hitUV = finalUV;
            }

            break;
        }
    }

    vec2 edgeDist = min(hitUV, 1.0 - hitUV);
    float edgeFade = smoothstep(0.0, params.edgeFadeDistance, min(edgeDist.x, edgeDist.y));
    float grazingFade = smoothstep(0.0, params.grazingFadeStart, abs(reflectDir.z));

    float hitFlag = hitFound ? 1.0 : 0.0;
    float confidence = hitFlag * edgeFade * grazingFade;

    outTrace = vec4(hitUV, hitFlag, confidence);
}
