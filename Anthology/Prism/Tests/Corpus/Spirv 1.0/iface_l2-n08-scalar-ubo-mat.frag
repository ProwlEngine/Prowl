#version 450
#extension GL_EXT_scalar_block_layout : require
layout(scalar, binding=0) uniform U { float x; mat3 m; layout(row_major) mat2x3 r; vec3 v[2]; } u;
layout(location=0) out vec4 o;
void main(){ o = vec4(u.m[2], u.x) + vec4(u.r[1], u.v[1].x); }
