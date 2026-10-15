#version 450
#extension GL_EXT_nonuniform_qualifier : require
layout(location=0) flat in int i;
layout(location=0) out vec4 o;
layout(binding=6) uniform samplerBuffer tb[];
void main(){ o = texelFetch(tb[nonuniformEXT(i)], 0); }
