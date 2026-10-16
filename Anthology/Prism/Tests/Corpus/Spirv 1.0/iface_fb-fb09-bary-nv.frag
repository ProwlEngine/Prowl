#version 450
#extension GL_NV_fragment_shader_barycentric : require
layout(location=0) pervertexNV in float c[];
layout(location=0) out vec4 o;
void main(){ o = vec4(gl_BaryCoordNV, c[1]); }
