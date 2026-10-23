#version 450
layout(set=0, binding=0) uniform sampler1D s;
layout(location=0) flat in int i;
layout(location=0) out vec4 o;
void main() { vec2 d = vec2(i); o = textureGrad(s, i, i, i) + vec4(max(d, d), d + d); }
