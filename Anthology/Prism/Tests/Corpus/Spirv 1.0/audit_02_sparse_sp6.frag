#version 450
#extension GL_ARB_sparse_texture2 : enable
#extension GL_EXT_texture_shadow_lod : enable
layout(location=0) out vec4 o;
layout(location=0) in vec4 c;
layout(location=1) flat in ivec4 ic;

layout(binding=0) uniform sampler2D s; void main(){ vec4 t[2]; int r = sparseTextureARB(s, c.xy, t[1]); o = t[1] + vec4(r); }
