#version 450
layout(set = 0, binding = 0) uniform Camera {
    mat4 viewProj;
} camera;
layout(set = 0, binding = 1, std430) readonly buffer Bones {
    mat4 boneMatrices[];
} skin;
layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec4 aBoneWeights;
layout(location = 3) in ivec4 aBoneIndices;
layout(location = 0) out vec3 vNormal;
void main() {
    mat4 skinMat =
        aBoneWeights.x * skin.boneMatrices[aBoneIndices.x] +
        aBoneWeights.y * skin.boneMatrices[aBoneIndices.y] +
        aBoneWeights.z * skin.boneMatrices[aBoneIndices.z] +
        aBoneWeights.w * skin.boneMatrices[aBoneIndices.w];
    vec4 skinnedPos = skinMat * vec4(aPosition, 1.0);
    vNormal = mat3(skinMat) * aNormal;
    gl_Position = camera.viewProj * skinnedPos;
}
