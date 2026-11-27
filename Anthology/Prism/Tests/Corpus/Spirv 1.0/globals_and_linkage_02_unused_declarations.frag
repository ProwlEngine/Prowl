#version 460 core

// Every declared global is a linker object even when nothing reaches it, materialized after the bodies
// in declaration order. A global const array is one; a local const array is not, because it only ever
// exists at its use sites.

layout(set = 0, binding = 0) uniform sampler2D usedTex;
layout(set = 0, binding = 1) uniform sampler2D unusedTex;          // unused sampler
layout(set = 0, binding = 2) uniform sampler2DArray unusedArray;

layout(std140, set = 1, binding = 0) uniform UsedBlock
{
    vec4 tint;
} used;

layout(std140, set = 1, binding = 1) uniform UnusedBlock           // unreferenced named block
{
    mat4 whatever;
    vec4 more;
} unusedBlock;

layout(std140, set = 1, binding = 2) uniform UnusedAnonBlock       // unreferenced anonymous block
{
    vec4 anonMember;
    float anonTail;
};

layout(location = 0) in vec2 uv;
layout(location = 1) in vec3 unusedVarying;                        // unused input
layout(location = 0) out vec4 FragColor;
layout(location = 1) out vec4 unusedTarget;                        // unused output

// Global const arrays: linker objects even though only one is read, and the read one is dynamic.
const vec2 DISK[4] = vec2[4](vec2(-0.5, 0.5), vec2(0.5, 0.5), vec2(-0.5, -0.5), vec2(0.5, -0.5));
const float UNUSED_TABLE[3] = float[3](1.0, 2.0, 3.0);
const mat2 UNUSED_MATRIX = mat2(1.0, 0.0, 0.0, 1.0);
const float FOLDED_AWAY = 2.0;                                     // only feeds another constant

float gUnusedPrivate = 3.0;

void main()
{
    // a LOCAL const array is not a linker object
    const float localTable[2] = float[2](0.25, 0.75);

    vec4 acc = vec4(0.0);
    for (int i = 0; i < 4; i++)
        acc += texture(usedTex, uv + DISK[i] * 0.01) * localTable[i & 1];

    FragColor = acc * used.tint * (FOLDED_AWAY * 0.5);
}
