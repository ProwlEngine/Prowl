#version 450
layout(location=0) out vec4 o;
layout(location=0) in vec4 v;
layout(location=1) flat in int i; layout(location=2) flat in uint u;
void main(){ ivec2 a = ivec2(3, -4); ivec2 b = abs(a) + sign(a) + min(a, ivec2(0)) + max(a, 1) + clamp(a, -1, 1); uvec2 c = min(uvec2(3u), uvec2(4u)); o = vec4(b, c) + v; }
