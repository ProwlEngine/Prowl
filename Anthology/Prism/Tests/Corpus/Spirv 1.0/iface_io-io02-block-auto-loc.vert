#version 450
layout(location=2) out Blk { vec4 a; mat3 m; dvec4 d; float f[3]; layout(location=20) vec2 z; vec2 w; } blk;
void main(){ blk.a = vec4(1); blk.m = mat3(1); blk.d = dvec4(1); blk.f[2] = 1.0; blk.z = vec2(0); blk.w = vec2(1); gl_Position = vec4(0); }
