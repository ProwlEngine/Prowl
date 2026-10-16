#version 450
#extension GL_NV_fragment_shader_barycentric : require
layout(location=0) pervertexNV in vec3 n[];
layout(location=0) out vec4 o;
void main(){ vec3 b = gl_BaryCoordNoPerspNV; o = vec4(n[0] * b.x + n[1] * gl_BaryCoordNV.y + n[2] * b.z, 1.0); }
