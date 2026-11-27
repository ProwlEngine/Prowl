#version 450
#extension GL_ARB_sparse_texture2 : enable
#extension GL_EXT_texture_shadow_lod : enable
layout(location=0) out vec4 o;
layout(location=0) in vec4 c;
layout(location=1) flat in ivec4 ic;

layout(binding=0) uniform sampler2D s; struct S { vec4 v; }; void main(){ S x; int r = sparseTextureARB(s, c.xy, x.v); o = x.v + vec4(r); }
