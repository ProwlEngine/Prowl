#version 460
#extension GL_EXT_mesh_shader : require
layout(location=1) perprimitiveEXT flat in int pid;
layout(location=0) out vec4 o;
void main(){ o = vec4(pid, gl_PrimitiveID, gl_Layer, 0); }
