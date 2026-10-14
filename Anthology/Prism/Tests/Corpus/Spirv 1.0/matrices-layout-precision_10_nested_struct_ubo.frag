#version 460
struct Inner { vec3 a; float b; };
layout(binding = 0, std140) uniform U { Inner inner; vec4 c; } u;
layout(location = 0) out vec4 o;
void main() { o = vec4(u.inner.a, u.inner.b) + u.c; }
