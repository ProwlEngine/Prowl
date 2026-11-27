#version 450
#extension GL_EXT_fragment_shader_barycentric : require
layout(location=0) pervertexEXT in vec4 c[4];
layout(location=0) out vec4 o;
void main(){ o = c[0]; }
