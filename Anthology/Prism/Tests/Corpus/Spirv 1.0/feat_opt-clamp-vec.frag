#version 450
layout(location=0) out vec4 o;
layout(location=0) in vec4 v;
void main(){ ivec3 lo = ivec3(-1); ivec3 hi = ivec3(5); vec3 x = vec3(2.0, -3.0, 9.0); vec3 c = clamp(x, vec3(-1.0), vec3(4.0)); ivec3 d = clamp(ivec3(-9, 0, 7), lo, hi); uvec2 e = clamp(uvec2(1u, 8u), uvec2(2u), uvec2(6u)); o = vec4(c, float(d.x + d.z + int(e.x + e.y))) + v; }
