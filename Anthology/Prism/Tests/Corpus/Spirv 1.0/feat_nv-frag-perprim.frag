#version 460
#extension GL_NV_mesh_shader : require
layout(location=0) perprimitiveNV in vec4 pc;
layout(location=0) out vec4 o;
void main(){ o = pc; }
