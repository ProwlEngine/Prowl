#version 450
layout(location=0) out vec4 o;
layout(location=0) in vec4 v;
layout(location=1) flat in int i;
layout(location=2) flat in uint u;
#extension GL_ARB_sparse_texture_clamp : enable
layout(binding=0) uniform sampler2D s; void main(){ o = textureClampARB(s, v.xy, 1.0, 0.5); }
