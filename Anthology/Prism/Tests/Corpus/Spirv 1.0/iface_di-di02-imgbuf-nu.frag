#version 450
#extension GL_EXT_nonuniform_qualifier : require
layout(location=0) flat in int i;
layout(location=0) out vec4 o;
layout(binding=7, r32f) uniform imageBuffer ib[];
void main(){ o = imageLoad(ib[nonuniformEXT(i)], 0); }
