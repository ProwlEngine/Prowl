#version 460 core

// Fullscreen composite pass: blends the integrated froxel volume onto the scene.
layout(location = 0) in vec2 vUV;

layout(location = 0) out vec4 outColor;

layout(set = 0, binding = 0) uniform sampler2D uSceneColor;
layout(set = 0, binding = 1) uniform sampler2D uSceneDepth;
layout(set = 0, binding = 2) uniform sampler3D uIntegratedFog;

layout(set = 0, binding = 3) uniform CompositeParams
{
    mat4 invProj;
    vec4 nearFarNumSlicesPad; // x = near, y = far, z = numSlices, w unused
} uParams;

void main()
{
    float hwDepth = texture(uSceneDepth, vUV).r;

    vec4 clipPos = vec4(vUV * 2.0 - 1.0, hwDepth, 1.0);
    vec4 viewPos4 = uParams.invProj * clipPos;
    vec3 viewPos = viewPos4.xyz / viewPos4.w;
    float viewDepth = viewPos.z;

    float near = uParams.nearFarNumSlicesPad.x;
    float far = uParams.nearFarNumSlicesPad.y;
    float numSlices = uParams.nearFarNumSlicesPad.z;

    // Inverse of the exponential slicing used when the froxel grid was built.
    float sliceIndex = numSlices * log(max(viewDepth, near) / near) / log(far / near);
    sliceIndex = clamp(sliceIndex, 0.0, numSlices - 1.0);

    float w = (sliceIndex + 0.5) / numSlices;
    vec4 fog = texture(uIntegratedFog, vec3(vUV, w));

    vec3 scattering = fog.rgb;
    float transmittance = fog.a;

    vec3 sceneColor = texture(uSceneColor, vUV).rgb;
    vec3 finalColor = sceneColor * transmittance + scattering;

    outColor = vec4(finalColor, 1.0);
}
