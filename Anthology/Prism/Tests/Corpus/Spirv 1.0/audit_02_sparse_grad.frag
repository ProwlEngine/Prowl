#version 450
#extension GL_ARB_sparse_texture2 : enable
#extension GL_ARB_sparse_texture_clamp : enable
layout(location=0) out vec4 o;
layout(location=0) in vec4 c;
layout(location=1) flat in ivec4 ic;

layout(binding=0) uniform sampler2D s; void main(){ vec4 t; int r = sparseTextureGradARB(s, c.xy, c.xy, c.zw, t); o = t + vec4(r); }
