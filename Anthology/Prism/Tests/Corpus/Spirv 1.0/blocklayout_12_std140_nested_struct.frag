#version 460

struct S {
    vec3 a;
    float b;
};

layout(set = 0, binding = 0, std140) uniform Block {
    S s;
    vec4 c;
} u;

layout(location = 0) out vec4 o;

void main() {
    o = vec4(u.s.a, u.s.b) + u.c;
}
