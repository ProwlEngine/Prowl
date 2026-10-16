#version 460
#extension GL_EXT_mesh_shader : require
layout(location=0) perprimitiveEXT in PB { vec4 c; flat int k; } pb;
layout(location=0) out vec4 o;
void main(){ o = pb.c + vec4(pb.k); }
