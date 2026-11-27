#version 450
#extension GL_ARB_sparse_texture2 : enable
#extension GL_EXT_texture_shadow_lod : enable
layout(location=0) out vec4 o;
layout(location=0) in vec4 c;
layout(location=1) flat in ivec4 ic;

layout(binding=0) uniform sampler2DShadow ss; void main(){ float f; int r = sparseTextureARB(ss, c.xyz, f); o = vec4(f, r, 0, 0); }
