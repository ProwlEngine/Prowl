#version 450
#extension GL_ARB_sparse_texture2 : enable
#extension GL_ARB_sparse_texture_clamp : enable
layout(location=0) out vec4 o;
layout(location=0) in vec4 c;
layout(location=1) flat in ivec4 ic;

layout(binding=0) uniform sampler2DShadow s; void main(){ float t; int r = sparseTextureLodARB(s, c.xyz, 1.0, t); o = vec4(t, r, 0, 0); }
