// 240-bool-in-ubo: a bool member in a std140 uniform block (reclassified from reject/):
// BOTH compilers lower it without OpTypeBool - glslang and Prism each rewrite the member to a
// 32-bit int with conversions at access sites (probe-verified: the modules are valid, member
// offsets identical). LIMITATIONS.md's "we throw" claim is stale. Must compile + validate.
#version 450
layout(std140, binding=0) uniform B { bool b; float f; } u;
layout(location=0) out vec4 o;
void main(){ o = vec4(u.b ? 1.0 : 0.0, u.f, u.b ? 2.0 : 3.0, 1.0); }