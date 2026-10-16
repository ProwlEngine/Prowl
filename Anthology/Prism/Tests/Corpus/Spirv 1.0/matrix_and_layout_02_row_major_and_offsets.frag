#version 460 core

// row_major flips a matrix member's storage order, which is a per-member decoration rather than a
// different type. Mixed with column_major members and explicit offsets in one block.

layout(std140, set = 0, binding = 0) uniform Matrices
{
    layout(row_major) mat4 rowView;
    layout(column_major) mat4 colView;
    layout(row_major) mat3 rowNormal;
    mat2x4 defaultOrder;
    layout(offset = 320) vec4 pinned;
    float trailing;
} mats;

layout(location = 0) in vec3 vPos;
layout(location = 0) out vec4 FragColor;

void main()
{
    vec4 a = mats.rowView * vec4(vPos, 1.0);
    vec4 b = mats.colView * vec4(vPos, 1.0);
    vec3 n = mats.rowNormal * vPos;
    vec4 c = mats.defaultOrder * vec2(1.0, 2.0);

    FragColor = a + b + c + vec4(n, 0.0) + mats.pinned * mats.trailing;
}
