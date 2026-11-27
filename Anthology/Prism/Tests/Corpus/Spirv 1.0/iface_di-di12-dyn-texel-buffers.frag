#version 450
#extension GL_EXT_nonuniform_qualifier : require
layout(location=0) flat in int i;
layout(location=0) out vec4 o;
layout(binding=0) uniform samplerBuffer t[4];
layout(binding=1, r32f) uniform imageBuffer im[4];
void main(){ o = texelFetch(t[i], 0) + imageLoad(im[i], 0); }
